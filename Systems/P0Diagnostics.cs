using System;
using System.Collections.Generic;
using System.IO;
using Colossal.Entities;
using Game.Pathfind;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Vehicles;
using RouteFilter.Components;
using Unity.Entities;
using UnityEngine;

namespace RouteFilter.Systems;

// Temporary P0 instrumentation. One explicitly selected canonical road vehicle only.
// No queries over city vehicles. Config/statistics sampling is at most once per real second.
internal static class P0Diagnostics
{
    internal static bool Tool = true, Highlight = true, Catalog = true, Restriction = true, Overlay = true;
    internal static Entity Vehicle, Target;
    internal static bool HasOwnedRequest => s_Milestones.Contains("RerouteRequested");
    private static Entity s_PhysicalVehicle;
    private static bool s_NoPathConfirmed;
    private static bool s_OwnedResult, s_ExpectedAlternative;
    internal static readonly string ControlPath = Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "..", "LocalLow", "Colossal Order", "Cities Skylines II", "RouteFilter-p0.txt"));
    private static float s_NextPoll, s_NextReport, s_ReportStart;
    private static int s_Frames;
    private static int s_TopologyRevision = -1;
    private static DateTime s_LastWrite;
    private static readonly Dictionary<string, string> s_Pending = new();
    private static readonly Dictionary<string, string> s_Last = new();
    private static readonly HashSet<string> s_Milestones = new();

    // Preserve the first successful transition immediately. Later per-frame observations
    // must not erase the evidence that a request reached this stage before crossing.
    internal static void Milestone(string stage, string value)
    {
        if (Vehicle == Entity.Null || !s_Milestones.Add(stage)) return;
        s_Pending.Remove(stage);
        s_Last[stage] = value;
        Mod.Log.Info($"[RF.Trace] Vehicle={Vehicle} Target={Target} {stage}={value}");
    }

    internal static void Record(string stage, string value)
    {
        if (Vehicle == Entity.Null) return;
        if (stage == "GraphMutation" || stage == "GateCrossing" || stage == "FinalOutcome")
        {
            s_Pending.Remove(stage);
            if (!s_Last.TryGetValue(stage, out var previous) || previous != value)
            {
                Mod.Log.Info($"[RF.Trace] Vehicle={Vehicle} Target={Target} {stage}={value}");
                s_Last[stage] = value;
                if (stage == "GateCrossing" && value.EndsWith("=True"))
                    foreach (var step in new[] { "PrefabMatch", "Gate", "Candidate", "RejectReason", "Safety", "Lease", "GraphMutation", "Reroute", "PathResult" })
                        Mod.Log.Info($"[RouteFilter.P0.CrossingPipeline] vehicle={Vehicle} {step}=" +
                            (s_Pending.TryGetValue(step, out var pending) ? pending : s_Last.TryGetValue(step, out var last) ? last : "NOT REACHED"));
            }
            return;
        }
        if (!s_Milestones.Contains(stage)) s_Pending[stage] = value;
    }

    internal static void Arm(World world, Entity selected)
    {
        var manager = world.EntityManager;
        s_PhysicalVehicle = selected;
        for (var i = 0; i < 4 && manager.Exists(selected) && manager.TryGetComponent(selected, out Controller controller)
            && controller.m_Controller != Entity.Null && controller.m_Controller != selected; i++) selected = controller.m_Controller;
        if (!manager.Exists(selected) || !manager.HasComponent<Vehicle>(selected)) return;
        Vehicle = selected; Target = Entity.Null; s_Pending.Clear(); s_Last.Clear(); s_Milestones.Clear();
        s_OwnedResult = s_ExpectedAlternative = s_NoPathConfirmed = false;
        foreach (var field in new[] { "DirectedGateMatched", "GateDirectionValid", "CandidateCreated", "CandidateRejected",
            "LeaseRequested", "LeaseCreated", "GraphMutationIssued", "UpdatedIssued", "RerouteRequested", "PendingObserved", "ResultObserved", "FailedObserved" })
            s_Pending[field] = "False";
        foreach (var field in new[] { "OriginalBlockage", "WrittenBlockage", "ActualBlockageAfterWrite" })
            s_Pending[field] = "NOT_APPLICABLE: request-local graph exclusion; no physical CarLane mutation";
        s_Pending["LeaseLane"] = "NOT_APPLICABLE: request-local transaction";
        foreach (var stage in new[] { "PrefabMatch", "Gate", "Candidate", "RejectReason", "Safety", "Lease", "GraphMutation", "Reroute", "PathResult", "GateCrossing" })
            s_Pending[stage] = "not observed yet";
        Mod.Log.Info($"[RouteFilter.P0.Trace] ARMED canonicalVehicle={Vehicle} build={Mod.BuildId}");
    }

    internal static void Poll(World world)
    {
        var now = Time.realtimeSinceStartup;
        s_Frames++;
        if (now < s_NextPoll) return;
        s_NextPoll = now + 1f;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(ControlPath);
            if (stamp != s_LastWrite && File.Exists(ControlPath))
            {
                // Fixed small control file, no frame IO and no persistent/save mutation.
                var info = new FileInfo(ControlPath);
                if (info.Length <= 4096)
                    foreach (var line in File.ReadAllLines(ControlPath))
                    {
                        var pair = line.Trim().Split('=');
                        if (pair.Length != 2) continue;
                        var on = pair[1].Trim().Equals("on", StringComparison.OrdinalIgnoreCase);
                        switch (pair[0].Trim().ToLowerInvariant())
                        {
                            case "tool": Tool = on; break;
                            case "highlight": Highlight = on; break;
                            case "catalog": Catalog = on; break;
                            case "restriction": Restriction = on; break;
                            case "overlay": Overlay = on; break;
                        }
                    }
                s_LastWrite = stamp;
                Mod.Log.Info($"[RouteFilter.P0.Isolation] tool={Tool} highlight={Highlight} catalog={Catalog} restriction={Restriction} overlay={Overlay}");
            }
        }
        catch (Exception error) { Mod.Log.Warn("[RouteFilter.P0] control read failed: " + error.Message); }
        var tool = world.GetExistingSystemManaged<RestrictionToolSystem>();
        if (tool != null && Target != Entity.Null && tool.SelectedTarget != Entity.Null && tool.SelectedTarget != Target && Vehicle != Entity.Null)
            Arm(world, s_PhysicalVehicle); // Never reuse evidence from a different restriction target.
        if (tool?.SelectedTarget != Entity.Null && tool != null) Target = tool.SelectedTarget;
        var index = world.GetExistingSystemManaged<RestrictionIndexSystem>();
        if (Vehicle == Entity.Null && Target != Entity.Null && index != null) ArmLocalForbiddenVehicle(world, index);
        if (Target != Entity.Null && index != null && index.Revision != s_TopologyRevision)
        {
            s_TopologyRevision = index.Revision;
            Mod.Log.Info(index.BuildTopologyDump(Target));
        }
        if (s_NoPathConfirmed && Vehicle != Entity.Null &&
            (!world.EntityManager.Exists(Vehicle) || world.EntityManager.HasComponent<Game.Common.Deleted>(Vehicle)))
        {
            Milestone("NativeRemovalObserved", "True");
            Record("TerminationPhase", "DeletedOrEntityGone");
        }
        else if (Vehicle != Entity.Null && world.EntityManager.Exists(Vehicle) && Target != Entity.Null)
            Observe(world);
        foreach (var pair in s_Pending)
            if (!s_Last.TryGetValue(pair.Key, out var previous) || previous != pair.Value)
            {
                Mod.Log.Info($"[RF.Trace] Vehicle={Vehicle} Target={Target} {pair.Key}={pair.Value}");
                s_Last[pair.Key] = pair.Value;
            }
        s_Pending.Clear();
        if (now >= s_NextReport)
        {
            if (s_ReportStart != 0)
                Mod.Log.Info($"[RouteFilter.P0.Sample] wallFps={s_Frames / Math.Max(.001f, now - s_ReportStart):F1} simFrame={world.GetExistingSystemManaged<SimulationSystem>()?.frameIndex} tool={Tool} highlight={Highlight} catalog={Catalog} restriction={Restriction} overlay={Overlay} previewCurves={world.GetExistingSystemManaged<RestrictionOverlaySystem>()?.PreviewCurveCount} target={tool?.SelectedTarget}");
            s_Frames = 0; s_ReportStart = now; s_NextReport = now + 5f;
        }
    }

    private static void Observe(World world)
    {
        var manager = world.EntityManager;
        var index = world.GetExistingSystemManaged<RestrictionIndexSystem>();
        if (index == null || !manager.Exists(Target)) return;
        var prefabs = new List<Entity>(8);
        Record("Controller", Vehicle.ToString());
        Record("PhysicalVehicle", s_PhysicalVehicle.ToString());
        Record("PhysicalPrefab", PrefabDescription(world, s_PhysicalVehicle));
        Record("Prefab", PrefabDescription(world, Vehicle));
        Record("PhysicalComponents", ComponentDescription(manager, s_PhysicalVehicle));
        Record("CanonicalComponents", ComponentDescription(manager, Vehicle));
        var chain = new System.Text.StringBuilder();
        var linked = s_PhysicalVehicle;
        for (var depth = 0; depth <= 4 && manager.Exists(linked); depth++)
        {
            chain.Append(linked).Append(" -> ");
            if (!manager.TryGetComponent(linked, out Controller controller) || controller.m_Controller == Entity.Null || controller.m_Controller == linked) break;
            linked = controller.m_Controller;
        }
        Record("ControllerChain", chain.ToString());
        if (manager.TryGetComponent(Vehicle, out Game.Vehicles.MaintenanceVehicle maintenance))
            Record("NativeMaintenanceState", $"{maintenance.m_State}; vanilla skips RequireNewPath while TryWork/Working is set=" +
                ((maintenance.m_State & (MaintenanceVehicleFlags.TryWork | MaintenanceVehicleFlags.Working)) != 0));
        Record("EmergencyProtectionExemption", (Mod.Settings?.EmergencyProtection != false &&
            (IsProtectedService(manager, Vehicle) || IsProtectedService(manager, s_PhysicalVehicle))).ToString());
        Record("TargetType", manager.HasComponent<Node>(Target) ? "Node" : manager.HasComponent<Game.Net.Edge>(Target) ? "Segment" : "InvalidTarget");
        Record("RestrictionRevision", index.Revision.ToString());
        if (manager.TryGetBuffer(Target, true, out DynamicBuffer<RestrictedVehicleAssetV1> restriction))
        {
            var names = new List<string>(restriction.Length);
            foreach (var item in restriction) names.Add(PrefabName(world, item.m_Prefab));
            Record("UIForbiddenPrefabs", string.Join(",", names));
        }
        if (manager.TryGetComponent(s_PhysicalVehicle, out PrefabRef physicalPrefab)) prefabs.Add(physicalPrefab.m_Prefab);
        if (manager.TryGetComponent(Vehicle, out PrefabRef own)) prefabs.Add(own.m_Prefab);
        if (manager.TryGetBuffer(Vehicle, true, out DynamicBuffer<LayoutElement> layout))
            for (var i = 0; i < layout.Length && i < 64; i++)
                if (manager.TryGetComponent(layout[i].m_Vehicle, out PrefabRef part)) prefabs.Add(part.m_Prefab);
        var layoutNames = new List<string>(8);
        if (manager.TryGetBuffer(Vehicle, true, out DynamicBuffer<LayoutElement> parts))
            for (var i = 0; i < parts.Length && i < 64; i++) layoutNames.Add(PrefabDescription(world, parts[i].m_Vehicle));
        Record("LayoutTrailerPrefabs", string.Join(",", layoutNames));
        var catalog = world.GetExistingSystemManaged<RouteFilterUISystem>();
        var catalogMatches = new List<string>(prefabs.Count);
        foreach (var prefab in prefabs)
            catalogMatches.Add($"{PrefabName(world, prefab)}:catalog={catalog?.ContainsCatalogPrefab(prefab)}:CarData={manager.HasComponent<CarData>(prefab)}");
        Record("CatalogMatch", string.Join(";", catalogMatches));
        Record("ForbiddenPrefabMatch", index.TargetRestricts(Target, prefabs).ToString());
        Record("PrefabMatch", $"matched={index.TargetRestricts(Target, prefabs)} prefabs={string.Join(",", prefabs)} revision={index.Revision} dirty={Mod.RestrictionsDirty}");
        if (manager.TryGetBuffer(Vehicle, true, out DynamicBuffer<TrainNavigationLane> railNavigation) &&
            manager.TryGetBuffer(Vehicle, true, out DynamicBuffer<LayoutElement> railLayout) && railLayout.Length > 0 &&
            manager.TryGetComponent(railLayout[0].m_Vehicle, out TrainCurrentLane trainCurrent))
        { ObserveRail(world, trainCurrent); return; }
        Record("CarCurrentLanePresent", manager.HasComponent<CarCurrentLane>(Vehicle).ToString());
        Record("CarNavigationLanePresent", manager.HasBuffer<CarNavigationLane>(Vehicle).ToString());
        Record("PathOwnerPresent", manager.HasComponent<PathOwner>(Vehicle).ToString());
        if (!manager.TryGetComponent(Vehicle, out CarCurrentLane current))
        { Record("CandidateRejectReason", "RoadControllerMissing: selected canonical lacks CarCurrentLane"); return; }
        var next = Entity.Null;
        if (manager.TryGetBuffer(Vehicle, true, out DynamicBuffer<CarNavigationLane> nav) && nav.Length > 0) next = nav[0].m_Lane;
        var gate = false;
        if (index.TryGetTargetGates(Target, out var gates))
            foreach (var value in gates)
                if (value.m_EntryLane == current.m_Lane && value.m_NextLane == next) { gate = true; break; }
        Record("Gate", $"entry={current.m_Lane} next={next} directGate={gate} watched={world.GetExistingSystemManaged<RestrictionCandidateSystem>()?.WatchedEntryLaneCount} curve={current.m_CurvePosition}");
        Record("EntryLane", current.m_Lane.ToString()); Record("NextLane", next.ToString()); Record("DirectedGateMatched", gate.ToString());
        if (index.TryGetConnectionLane(current.m_Lane, out Lane entryConnection) &&
            index.TryGetConnectionLane(next, out Lane nextConnection))
            Record("NativeDirectConnection", $"entryEnd={entryConnection.m_EndNode} nextStart={nextConnection.m_StartNode} samePathNode={entryConnection.m_EndNode.Equals(nextConnection.m_StartNode)} endOwnerIndex={entryConnection.m_EndNode.GetOwnerIndex()} targetIndex={Target.Index}");
        if (gate && manager.TryGetComponent(next, out Game.Net.CarLane actualLane))
        {
            Milestone("OriginalBlockage", $"({actualLane.m_BlockageStart},{actualLane.m_BlockageEnd}); read-only snapshot, no RouteFilter write");
            Record("ActualCarLaneBlockage", $"({actualLane.m_BlockageStart},{actualLane.m_BlockageEnd})");
        }
        var crossing = false;
        if (index.TryGetInternalLanes(Target, out var internals))
            foreach (var lane in internals) if (lane == current.m_Lane) { crossing = true; break; }
        Record("GateCrossing", $"insideRestrictedTarget={crossing}");
        if (manager.TryGetComponent(Vehicle, out PathOwner owner))
        {
            Record("PathResult", $"nativeState={owner.m_State} cursor={owner.m_ElementIndex}");
            Record("PathOwnerAfter", owner.m_State.ToString());
            if ((owner.m_State & (PathFlags.Pending | PathFlags.Scheduled)) != 0) Milestone("PendingObserved", "True");
            if ((owner.m_State & PathFlags.Failed) != 0) Milestone("FailedObserved", "True");
            if (manager.TryGetBuffer(Vehicle, true, out DynamicBuffer<PathElement> path))
            {
                Record("PathElementCount", path.Length.ToString());
                // Read-only upstream evidence: fixed 16-element lookahead for this one actor.
                // This proves whether the route already advertised the forbidden target before
                // the immediate-navigation candidate became eligible.
                var lookahead = new List<string>(16);
                var targetLanes = TargetPathLanes(world);
                var firstRestricted = -1;
                var start = Math.Max(0, owner.m_ElementIndex);
                for (var p = start; p < path.Length && p < start + 16; p++)
                {
                    lookahead.Add($"{p}:{path[p].m_Target}");
                    if (firstRestricted < 0 && targetLanes.Contains(path[p].m_Target)) firstRestricted = p;
                }
                Record("OriginalPathLookahead", string.Join(" -> ", lookahead));
                Record("RestrictedTargetAheadInPath", (firstRestricted >= 0).ToString());
                Record("FirstRestrictedPathIndex", firstRestricted.ToString());
                Record("CurrentLaneLength", manager.TryGetComponent(current.m_Lane, out Curve curve) ? curve.m_Length.ToString("F2") : "CurveMissing");
                // The exact owned query completed, AND vanilla has consumed its pending state.
                // Inspect this one vehicle's adopted path, never every city path.
                if (s_OwnedResult && s_ExpectedAlternative &&
                    (owner.m_State & (PathFlags.Pending | PathFlags.Scheduled | PathFlags.Obsolete)) == 0)
                {
                    var crosses = PathCrossesTarget(world, path);
                    Milestone("ResultObserved", "True");
                    Record("NewPathStillCrossesTarget", crosses.ToString());
                    var adopted = path.Length > 0 && (owner.m_State & (PathFlags.Failed | PathFlags.Stuck)) == 0;
                    if (crosses || !adopted) Record("EnforcementFailureReason", crosses ? "RestrictedTargetStillPresent" : "OwnedAlternativeNotAdopted");
                    Record("FinalOutcome", !crosses && adopted ? "Rerouted" : "EnforcementFailed");
                    s_ExpectedAlternative = false;
                }
            }
        }

    }

    private static void ObserveRail(World world, TrainCurrentLane current)
    {
        var manager = world.EntityManager;
        var rail = world.GetExistingSystemManaged<RailEnforcementBackend>();
        Record("Backend", "Rail"); Record("EntryLane", current.m_Front.m_Lane.ToString());
        Record("TrainFrontPosition", current.m_Front.m_CurvePosition.ToString());
        if (manager.TryGetBuffer(Vehicle, true, out DynamicBuffer<TrainNavigationLane> nav) && nav.Length > 0)
            Record("NextLane", nav[0].m_Lane.ToString());
        if (!manager.TryGetComponent(Vehicle, out PathOwner owner)) return;
        Record("PathOwnerAfter", owner.m_State.ToString());
        if ((owner.m_State & (PathFlags.Pending | PathFlags.Scheduled)) != 0) Milestone("PendingObserved", "True");
        if ((owner.m_State & PathFlags.Failed) != 0) Milestone("FailedObserved", "True");
        if (!manager.TryGetBuffer(Vehicle, true, out DynamicBuffer<PathElement> path)) return;
        Record("PathElementCount", path.Length.ToString());
        if (!s_OwnedResult || !s_ExpectedAlternative || rail == null ||
            (owner.m_State & (PathFlags.Pending | PathFlags.Scheduled | PathFlags.Obsolete)) != 0) return;
        var crosses = rail.PathCrossesTarget(Target, path);
        var adopted = path.Length > 0 && (owner.m_State & (PathFlags.Failed | PathFlags.Stuck)) == 0;
        Milestone("ResultObserved", "True"); Record("NewPathStillCrossesTarget", crosses.ToString());
        if (crosses || !adopted) Record("EnforcementFailureReason", crosses ? "RestrictedTrackTargetStillPresent" : "OwnedRailAlternativeNotAdopted");
        Record("FinalOutcome", !crosses && adopted ? "Rerouted" : "EnforcementFailed");
        s_ExpectedAlternative = false;
    }

    // Diagnostic pick only: at most 64 local LaneObjects, once per second, until ONE vehicle
    // is locked. Works even when topology is empty, so a gate bug cannot hide the trace.
    private static void ArmLocalForbiddenVehicle(World world, RestrictionIndexSystem index)
    {
        var manager = world.EntityManager;
        if (!manager.TryGetBuffer(Target, true, out DynamicBuffer<RestrictedVehicleAssetV1> restrictions) ||
            restrictions.Length == 0 || !manager.TryGetBuffer(Target, true, out DynamicBuffer<ConnectedEdge> edges)) return;
        var budget = 64;
        var laneBudget = 64;
        foreach (var edge in edges)
        {
            if (!manager.TryGetBuffer(edge.m_Edge, true, out DynamicBuffer<Game.Net.SubLane> lanes)) continue;
            foreach (var lane in lanes)
            {
                if (--laneBudget < 0) return;
                if (!manager.TryGetBuffer(lane.m_SubLane, true, out DynamicBuffer<LaneObject> objects)) continue;
                foreach (var item in objects)
                {
                    if (--budget < 0) return;
                    var physical = item.m_LaneObject;
                    var truck = physical;
                    for (var depth = 0; depth < 4 && manager.Exists(truck) && manager.TryGetComponent(truck, out Controller controller) &&
                        controller.m_Controller != Entity.Null && controller.m_Controller != truck; depth++) truck = controller.m_Controller;
                    if (!manager.Exists(truck)) continue;
                    var roadTruck = manager.HasComponent<Car>(truck) && manager.HasComponent<CarCurrentLane>(truck);
                    var railConsist = manager.HasBuffer<TrainNavigationLane>(truck);
                    if (!roadTruck && !railConsist) continue;
                    var prefabs = new List<Entity>(8);
                    if (manager.TryGetComponent(physical, out PrefabRef physicalPrefab)) prefabs.Add(physicalPrefab.m_Prefab);
                    if (manager.TryGetComponent(truck, out PrefabRef prefab)) prefabs.Add(prefab.m_Prefab);
                    if (manager.TryGetBuffer(truck, true, out DynamicBuffer<LayoutElement> layout))
                        for (var p = 0; p < layout.Length && p < 64; p++)
                            if (manager.TryGetComponent(layout[p].m_Vehicle, out PrefabRef part)) prefabs.Add(part.m_Prefab);
                    if (!index.TargetRestricts(Target, prefabs)) continue;
                    var selectedTarget = Target; Arm(world, physical); Target = selectedTarget;
                    Milestone("PrefabMatch", $"matched=True prefabs={string.Join(",", prefabs)} revision={index.Revision}; target-local diagnostic pick");
                    return;
                }
            }
        }
    }

    internal static void OwnedResult(Entity vehicle, RoadQueryOutcome outcome, ulong generation)
    {
        if (vehicle != Vehicle) return;
        s_OwnedResult = true; s_ExpectedAlternative = outcome == RoadQueryOutcome.AlternativePathFound;
        Milestone("NativeQueryResultObserved", "True"); Record("AttemptGeneration", generation.ToString());
        if (outcome == RoadQueryOutcome.ConfirmedNoAlternative)
        {
            s_NoPathConfirmed = true;
            Milestone("ResultObserved", "True"); Record("FinalOutcome", "NoPath/Removed");
            Record("TerminationPhase", "NoPathConfirmed; awaiting vanilla cleanup");
            Record("NativeRemovalObserved", "False");
        }
    }

    internal static void Grandfather(Entity vehicle, string invariant)
    {
        if (vehicle != Vehicle || s_NoPathConfirmed) return;
        if (s_Last.TryGetValue("FinalOutcome", out var terminal) && (terminal == "Rerouted" || terminal == "NoPath/Removed")) return;
        Milestone("InitialGrandfatherReason", invariant);
        Record("GrandfatherReason", invariant); Record("FinalOutcome", "Grandfathered");
    }

    private static string PrefabName(World world, Entity entity)
    {
        var prefabs = world.GetExistingSystemManaged<PrefabSystem>();
        return prefabs != null && prefabs.TryGetPrefab<PrefabBase>(entity, out var prefab) ? $"{prefab.name} [{entity}]" : entity.ToString();
    }
    private static string PrefabDescription(World world, Entity vehicle) =>
        world.EntityManager.Exists(vehicle) && world.EntityManager.TryGetComponent(vehicle, out PrefabRef prefab) ? PrefabName(world, prefab.m_Prefab) : "PrefabRefMissing";

    private static bool IsProtectedService(EntityManager manager, Entity vehicle) => manager.Exists(vehicle) &&
        (manager.HasComponent<Game.Vehicles.PoliceCar>(vehicle) || manager.HasComponent<Game.Vehicles.Ambulance>(vehicle) ||
         manager.HasComponent<Game.Vehicles.FireEngine>(vehicle) || manager.HasComponent<Game.Vehicles.Hearse>(vehicle));

    // One selected vehicle, once per real second. Never used for discovery or admission.
    private static string ComponentDescription(EntityManager manager, Entity vehicle)
    {
        if (!manager.Exists(vehicle)) return "EntityMissing";
        using var types = manager.GetComponentTypes(vehicle, Unity.Collections.Allocator.Temp);
        var text = new System.Text.StringBuilder();
        foreach (var type in types) text.Append(type.GetManagedType()?.FullName).Append(';');
        return text.ToString();
    }

    private static bool PathCrossesTarget(World world, DynamicBuffer<PathElement> path)
    {
        var lanes = TargetPathLanes(world);
        if (lanes.Count == 0) return true; // Cannot certify an avoiding path against empty topology.
        for (var i = 0; i < path.Length; i++) if (lanes.Contains(path[i].m_Target)) return true;
        return false;
    }

    private static HashSet<Entity> TargetPathLanes(World world)
    {
        var index = world.GetExistingSystemManaged<RestrictionIndexSystem>();
        var lanes = new HashSet<Entity>();
        if (index == null) return lanes;
        System.Collections.Generic.IReadOnlyCollection<Entity> internals;
        if (!index.TryGetDirectionExclusion(Target, out internals))
            internals = index.TryGetInternalLanes(Target, out var all) ? all : null;
        if (internals == null) return lanes;
        foreach (var lane in internals)
        {
            lanes.Add(lane);
            if (world.EntityManager.TryGetComponent(lane, out SlaveLane slave) &&
                world.EntityManager.TryGetComponent(lane, out Game.Common.Owner owner) &&
                world.EntityManager.TryGetBuffer(owner.m_Owner, true, out DynamicBuffer<Game.Net.SubLane> sub) && slave.m_MasterIndex < sub.Length)
                lanes.Add(sub[slave.m_MasterIndex].m_SubLane);
        }
        return lanes;
    }
}
