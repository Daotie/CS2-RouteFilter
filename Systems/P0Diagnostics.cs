using System;
using System.Collections.Generic;
using System.IO;
using Colossal.Entities;
using Game.Pathfind;
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
        Mod.Log.Info($"[RouteFilter.P0.Trace] vehicle={Vehicle} target={Target} {stage}: {value}");
    }

    internal static void Record(string stage, string value)
    {
        if (Vehicle == Entity.Null) return;
        if (stage == "GraphMutation" || stage == "GateCrossing")
        {
            s_Pending.Remove(stage);
            if (!s_Last.TryGetValue(stage, out var previous) || previous != value)
            {
                Mod.Log.Info($"[RouteFilter.P0.Trace] vehicle={Vehicle} target={Target} {stage}: {value}");
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
        for (var i = 0; i < 4 && manager.Exists(selected) && manager.TryGetComponent(selected, out Controller controller)
            && controller.m_Controller != Entity.Null && controller.m_Controller != selected; i++) selected = controller.m_Controller;
        if (!manager.Exists(selected) || !manager.HasComponent<CarCurrentLane>(selected)) return;
        Vehicle = selected; Target = Entity.Null; s_Pending.Clear(); s_Last.Clear(); s_Milestones.Clear();
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
        if (tool?.SelectedTarget != Entity.Null && tool != null) Target = tool.SelectedTarget;
        var index = world.GetExistingSystemManaged<RestrictionIndexSystem>();
        if (Target != Entity.Null && index != null && index.Revision != s_TopologyRevision)
        {
            s_TopologyRevision = index.Revision;
            Mod.Log.Info(index.BuildTopologyDump(Target));
        }
        if (Vehicle != Entity.Null && world.EntityManager.Exists(Vehicle) && Target != Entity.Null)
            Observe(world);
        foreach (var pair in s_Pending)
            if (!s_Last.TryGetValue(pair.Key, out var previous) || previous != pair.Value)
            {
                Mod.Log.Info($"[RouteFilter.P0.Trace] vehicle={Vehicle} target={Target} {pair.Key}: {pair.Value}");
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
        if (manager.TryGetComponent(Vehicle, out PrefabRef own)) prefabs.Add(own.m_Prefab);
        if (manager.TryGetBuffer(Vehicle, true, out DynamicBuffer<LayoutElement> layout))
            for (var i = 0; i < layout.Length && i < 64; i++)
                if (manager.TryGetComponent(layout[i].m_Vehicle, out PrefabRef part)) prefabs.Add(part.m_Prefab);
        Record("PrefabMatch", $"matched={index.TargetRestricts(Target, prefabs)} prefabs={string.Join(",", prefabs)} revision={index.Revision} dirty={Mod.RestrictionsDirty}");
        if (!manager.TryGetComponent(Vehicle, out CarCurrentLane current)) return;
        var next = Entity.Null;
        if (manager.TryGetBuffer(Vehicle, true, out DynamicBuffer<CarNavigationLane> nav) && nav.Length > 0) next = nav[0].m_Lane;
        var gate = false;
        if (index.TryGetTargetGates(Target, out var gates))
            foreach (var value in gates)
                if (value.m_EntryLane == current.m_Lane && value.m_NextLane == next) { gate = true; break; }
        Record("Gate", $"entry={current.m_Lane} next={next} directGate={gate} watched={world.GetExistingSystemManaged<RestrictionCandidateSystem>()?.WatchedEntryLaneCount} curve={current.m_CurvePosition}");
        var crossing = false;
        if (index.TryGetInternalLanes(Target, out var internals))
            foreach (var lane in internals) if (lane == current.m_Lane) { crossing = true; break; }
        Record("GateCrossing", $"insideRestrictedTarget={crossing}");
        if (manager.TryGetComponent(Vehicle, out PathOwner owner))
            Record("PathResult", $"nativeState={owner.m_State} cursor={owner.m_ElementIndex}");

    }
}
