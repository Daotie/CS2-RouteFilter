using System;
using System.Reflection;
using System.Collections;
using System.Threading;
using Colossal.Entities;
using Game.Common;
using Game.Net;
using Game.Pathfind;
using Game.Simulation;
using Game.Objects;
using Game.Tools;
using Game.Vehicles;
using HarmonyLib;
using RouteFilter.Components;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace RouteFilter.Systems;

/// <summary>
/// Narrow Game 1.6 queue bridge: a selected native query runs under the queue's existing graph
/// write dependencies, with a query-local edge overlay. Other native readers run before or after
/// the transaction, never during it. No ECS lane field, graph topology, or save data is changed.
/// Cost: O(native requests) O(1) admission lookup, O(admitted target lanes + native query work)
/// per admitted request. No city vehicle/lane scan or graph clone. Hard cap: 8 queries/64 frames,
/// 8 concurrent transactions, 128 target lanes; overflow runs the unmodified native request.
/// </summary>
internal static unsafe class RestrictionPathfindHook
{
    private const string HarmonyId = "RouteFilter.QueryExclusion";
    private static World s_World;
    private static MethodInfo s_Schedule;
    private static FieldInfo s_Workers;
    private static readonly Transaction[] s_Transactions = new Transaction[8];
    private static uint s_Window, s_Admissions;
    internal static bool Available { get; private set; }
    internal static uint Queries, RoadQueries, RailQueries, Alternatives, Fallbacks, Applied, Restored, Conflicts;
    internal static uint RoadAlternatives, RailAlternatives, RoadFallbacks, RailFallbacks;
    internal static int ActiveTransactions
    { get { var count = 0; foreach (var transaction in s_Transactions) if (transaction != null) count++; return count; } }

    private sealed class Transaction
    {
        internal NativeArray<Entity> Lanes;
        internal NativeArray<Entity> GraphLanes;
        internal EnforcementAttempt Attempt;
        // claim, applied edges, restored edges, conflicts, alternative, fallback, cancel
        internal NativeArray<int> State;
        internal NativeArray<EdgeID> Edges;
        internal NativeArray<PathMethod> Originals;
        internal JobHandle Handle;
        internal Entity Target;
        internal int Revision;
        internal uint Expiry;
        internal PathfindQueueSystem Queue;
        internal PathfindAction Action;
        internal Entity Owner;
        internal uint ResultFrame;
        internal object System;
        internal PathEventData EventData;
        internal bool WantsEvent, Scheduled, HighPriority, Rail;
        internal bool ExactNoRouteScope;
        internal void Dispose() { Lanes.Dispose(); GraphLanes.Dispose(); State.Dispose(); Edges.Dispose(); Originals.Dispose(); }
    }

    // Managed job deliberately preserves finally semantics if the native query throws.
    // Native queue SetPathfindData schedules a copy for each worker graph. Atomic claim chooses
    // exactly one; the other copies do nothing. The queue owns every graph read/write dependency.
    private struct QueryTransactionJob : IJob, ModificationJobs.IPathfindModificationJob
    {
        public NativePathfindData Graph;
        [ReadOnly] public NativeArray<Entity> Lanes;
        [ReadOnly] public NativeArray<Entity> GraphLanes;
        [NativeDisableContainerSafetyRestriction] public NativeArray<int> State;
        [NativeDisableContainerSafetyRestriction] public NativeArray<EdgeID> Edges;
        [NativeDisableContainerSafetyRestriction] public NativeArray<PathMethod> Originals;
        [NativeDisableUnsafePtrRestriction] public PathfindActionData* Action;
        public PathfindHeuristicData Heuristic;
        public float PassengerSpeed, CargoSpeed;
        public uint Seed;
        public bool ExactNoRouteScope;
        public void SetPathfindData(NativePathfindData data) => Graph = data;

        public void Execute()
        {
            ref var claim = ref UnsafeUtility.ArrayElementAsRef<int>(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(State), 0);
            if (Interlocked.CompareExchange(ref claim, 1, 0) != 0) return;
            var graph = Graph.GetReadOnlyData();
            // A target can have primary and taxi/secondary edges. The overlay modifies methods
            // only; all connections, costs, rules and access authorizations are left unchanged.
            var count = 0;
            var completeExclusion = ReadState(6) == 0;
            var validEndpoints = ValidTargets(ref graph, Action->m_StartTargets) && ValidTargets(ref graph, Action->m_EndTargets);
            var proof = new RoadQueryProof { ValidEndpoints = validEndpoints, ExactNoRouteScope = ExactNoRouteScope };
            try
            {
                if (ReadState(6) == 0)
                    for (var i = 0; i < Lanes.Length; i++)
                    {
                        if (Lanes[i] == Entity.Null) continue;
                        if (graph.GetEdge(GraphLanes[i], out var primary))
                        {
                            var alreadyOwned = false;
                            for (var e = 0; e < count; e++) if (Edges[e].Equals(primary)) alreadyOwned = true;
                            if (!alreadyOwned && graph.GetEdge(primary).m_Specification.m_Methods == 0) completeExclusion = false;
                            Exclude(ref graph, primary, ref count);
                        }
                        else completeExclusion = false;
                        if (graph.GetSecondaryEdge(GraphLanes[i], out var secondary)) Exclude(ref graph, secondary, ref count);
                    }
                State[1] = count;
                // The original vanilla request supplies endpoints, weights, methods and flags.
                // We neither fabricate routes nor move vehicles. The native result system adopts it.
                PathfindJobs.PathfindJob.Execute(Graph, Allocator.Temp, new Unity.Mathematics.Random(Seed),
                    Heuristic, PassengerSpeed, CargoSpeed, ref *Action);
                var avoidsTarget = Action->m_Path.Length > 0;
                for (var p = 0; count > 0 && avoidsTarget && p < Action->m_Path.Length; p++)
                    for (var lane = 0; lane < Lanes.Length; lane++)
                        if (Lanes[lane] != Entity.Null && (Action->m_Path[p].m_Target == Lanes[lane] ||
                            Action->m_Path[p].m_Target == GraphLanes[lane]))
                        { avoidsTarget = false; break; }
                proof.QueryCompleted = true;
                proof.AvoidsTarget = avoidsTarget;
                proof.EmptyPath = Action->m_Path.Length == 0;
                State[13] = Action->m_Result.Length > 0 ? (int)Action->m_Result[0].m_GraphTraversal : 0;
                State[14] = Action->m_Result.Length > 0 ? (int)Action->m_Result[0].m_ErrorCode : -1;
                State[15] = math.asint(Action->m_Result.Length > 0 ? Action->m_Result[0].m_TotalCost : float.NaN);
                State[16] = Action->m_Path.Length;
                // Game 1.6 returns -1 on heap exhaustion, float.MaxValue on cost cutoff.
                // Missing/invalid endpoints, error codes and ignored paths are never no-route proof.
                proof.SearchExhausted = proof.EmptyPath && Action->m_Result.Length == 1 &&
                    Action->m_Result[0].m_ErrorCode == ErrorCode.None && Action->m_Result[0].m_TotalCost == -1f &&
                    Action->m_Result[0].m_GraphTraversal > 0 &&
                    (Action->m_Parameters.m_PathfindFlags & (PathfindFlags.IgnorePath | PathfindFlags.SkipPathfind)) == 0;
                if (count > 0 && !avoidsTarget && !proof.EmptyPath) { Action->m_Path.Clear(); Action->m_Result.Clear(); }
                State[4] = avoidsTarget && count > 0 ? 1 : 0;
            }
            catch (Exception) { State[7] = 1; }
            finally
            {
                State[1] = count;
                for (var i = count - 1; i >= 0; i--)
                {
                    ref var edge = ref graph.GetEdge(Edges[i]);
                    if (edge.m_Specification.m_Methods == 0)
                    { edge.m_Specification.m_Methods = Originals[i]; State[2]++; }
                    else State[3]++; // External/unknown write: never overwrite it.
                }
            }
            proof.CompleteExclusion = completeExclusion && count > 0;
            proof.Cancelled = ReadState(6) != 0;
            proof.Exception = State[7] != 0;
            proof.RestoreConflict = State[3] != 0 || State[2] != count;
            var baselineExecuted = false;
            var excludedResult = Action->m_Result.Length > 0 ? Action->m_Result[0] : default;
            if (proof.ExactNoRouteScope && proof.EmptyPath && proof.SearchExhausted && proof.CompleteExclusion &&
                proof.ValidEndpoints && !proof.Cancelled && !proof.Exception && !proof.RestoreConflict)
            {
                // Counterfactual proof: the SAME original native request must succeed and cross
                // the excluded target without our overlay. A pre-existing unreachable destination
                // or overbroad exclusion must never be attributed to RouteFilter for deletion.
                Action->m_Path.Clear(); Action->m_Result.Clear(); baselineExecuted = true;
                try
                {
                    PathfindJobs.PathfindJob.Execute(Graph, Allocator.Temp, new Unity.Mathematics.Random(Seed),
                        Heuristic, PassengerSpeed, CargoSpeed, ref *Action);
                    if (Action->m_Result.Length > 0 && Action->m_Result[0].m_ErrorCode == ErrorCode.None &&
                        Action->m_Result[0].m_TotalCost >= 0 && Action->m_Path.Length > 0)
                        for (var p = 0; p < Action->m_Path.Length && !proof.BaselineCrossesTarget; p++)
                            for (var lane = 0; lane < Lanes.Length; lane++)
                                if (Lanes[lane] != Entity.Null && (Action->m_Path[p].m_Target == Lanes[lane] ||
                                    Action->m_Path[p].m_Target == GraphLanes[lane])) { proof.BaselineCrossesTarget = true; break; }
                }
                catch (Exception)
                {
                    State[7] = 3; proof.Exception = true;
                    Action->m_Path.Clear(); Action->m_Result.Clear();
                }
            }
            proof.Cancelled = ReadState(6) != 0;
            var outcome = EnforcementPolicy.ClassifyRoadQuery(proof);
            if (outcome == RoadQueryOutcome.ConfirmedNoAlternative)
            { Action->m_Path.Clear(); Action->m_Result.Clear(); Action->m_Result.Add(excludedResult); }
            State[8] = proof.CompleteExclusion ? 1 : 0;
            State[9] = (int)outcome;
            State[10] = proof.BaselineCrossesTarget ? 1 : 0;
            State[12] = !proof.ValidEndpoints ? 1 : !proof.CompleteExclusion ? 2 : proof.Exception ? 3 :
                proof.RestoreConflict ? 4 : proof.Cancelled ? 5 : !proof.ExactNoRouteScope ? 6 :
                !proof.EmptyPath && !proof.AvoidsTarget ? 7 : !proof.SearchExhausted && proof.EmptyPath ? 8 :
                proof.EmptyPath && !proof.BaselineCrossesTarget ? 9 : 0;
            // A single bounded grandfather query prevents a failed exclusion from discarding the
            // original path and stranding a vehicle. This is the SAME native request, not another
            // Obsolete write or an attempt retry. The graph is already restored for this fallback.
            if (outcome == RoadQueryOutcome.EnforcementUncertain)
            {
                State[5] = 1;
                if (!baselineExecuted) try
                {
                    Action->m_Result.Clear(); Action->m_Path.Clear();
                    PathfindJobs.PathfindJob.Execute(Graph, Allocator.Temp, new Unity.Mathematics.Random(Seed),
                        Heuristic, PassengerSpeed, CargoSpeed, ref *Action);
                }
                catch (Exception)
                {
                    State[7] = 2;
                    Action->m_Path.Clear(); Action->m_Result.Clear();
                    Action->m_Result.Add(new PathfindResult { m_Distance = -1, m_Duration = -1, m_TotalCost = -1 });
                }
            }
            // The native result consumer always reads result[0], including failed queries.
            if (Action->m_Result.Length == 0)
                Action->m_Result.Add(new PathfindResult { m_Distance = -1, m_Duration = -1, m_TotalCost = -1 });
            Interlocked.MemoryBarrier();
            // Hold this exact no-route result until the coordinator revalidates ownership and
            // queues vanilla deletion. Ordinary Failed flags can never enter this handshake.
            Action->m_State = outcome == RoadQueryOutcome.ConfirmedNoAlternative ?
                PathfindActionState.Pending : PathfindActionState.Completed;
        }

        private bool ValidTargets(ref UnsafePathfindData graph, UnsafeList<PathTarget> targets)
        {
            if (targets.Length == 0) return false;
            var usable = false;
            for (var i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                if (!math.isfinite(target.m_Cost) || !math.isfinite(target.m_Delta) || target.m_Delta < 0 || target.m_Delta > 1) return false;
                EdgeID edge;
                var exists = (target.m_Flags & EdgeFlags.Secondary) != 0 ?
                    graph.GetSecondaryEdge(target.m_Entity, out edge) : graph.GetEdge(target.m_Entity, out edge);
                if (exists && (target.m_Flags & (EdgeFlags.Forward | EdgeFlags.Backward)) != 0 &&
                    (graph.GetEdge(edge).m_Specification.m_Methods & Action->m_Parameters.m_Methods) != 0) usable = true;
            }
            return usable;
        }

        private int ReadState(int index) => Volatile.Read(ref UnsafeUtility.ArrayElementAsRef<int>(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(State), index));
        private void Exclude(ref UnsafePathfindData graph, EdgeID id, ref int count)
        {
            for (var i = 0; i < count; i++) if (Edges[i].Equals(id)) return;
            ref var edge = ref graph.GetEdge(id);
            if (edge.m_Specification.m_Methods == 0) return;
            Edges[count] = id;
            Originals[count++] = edge.m_Specification.m_Methods;
            edge.m_Specification.m_Methods = 0;
            if (edge.m_Specification.m_Methods == 0) State[11]++; // Actual read-back in the owning graph job.
        }
    }

    internal static void Install(World world)
    {
        Available = false;
        s_World = world;
        s_Schedule = typeof(PathfindQueueSystem).GetMethod("ScheduleModificationJob", BindingFlags.NonPublic | BindingFlags.Instance);
        if (s_Schedule == null || !s_Schedule.IsGenericMethodDefinition)
            throw new NotSupportedException("Native graph writer scheduler signature changed");
        s_Schedule = s_Schedule.MakeGenericMethod(typeof(QueryTransactionJob));
        s_Workers = typeof(PathfindQueueSystem).GetField("m_WorkerData", BindingFlags.NonPublic | BindingFlags.Instance);
        if (s_Workers == null) throw new NotSupportedException("Native worker dependency field changed");
        var harmony = new Harmony(HarmonyId);
        var signature = new[] { typeof(PathfindAction), typeof(Entity), typeof(JobHandle), typeof(uint), typeof(object), typeof(bool) };
        var simple = typeof(PathfindQueueSystem).GetMethod("Enqueue", signature);
        var eventSignature = new[] { typeof(PathfindAction), typeof(Entity), typeof(JobHandle), typeof(uint), typeof(object), typeof(PathEventData), typeof(bool) };
        var withEvent = typeof(PathfindQueueSystem).GetMethod("Enqueue", eventSignature);
        if (simple == null || withEvent == null) throw new NotSupportedException("Native pathfind enqueue signature changed");
        var update = typeof(PathfindQueueSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        var destroy = typeof(PathfindQueueSystem).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
        var preload = typeof(PathfindQueueSystem).GetMethod("PreDeserialize", BindingFlags.Instance | BindingFlags.Public);
        if (update == null || destroy == null || preload == null) throw new NotSupportedException("Native queue lifecycle signature changed");
        try
        {
            harmony.Patch(simple, prefix: new HarmonyMethod(typeof(RestrictionPathfindHook), nameof(Prefix)));
            harmony.Patch(withEvent, prefix: new HarmonyMethod(typeof(RestrictionPathfindHook), nameof(EventPrefix)));
            harmony.Patch(update, postfix: new HarmonyMethod(typeof(RestrictionPathfindHook), nameof(AfterNativeQueueUpdate)));
            harmony.Patch(destroy, prefix: new HarmonyMethod(typeof(RestrictionPathfindHook), nameof(BeforeQueueReset)));
            harmony.Patch(preload, prefix: new HarmonyMethod(typeof(RestrictionPathfindHook), nameof(BeforeQueueReset)));
        }
        catch { harmony.UnpatchAll(HarmonyId); throw; }
        ResetCounters();
        s_Window = s_Admissions = 0;
        Available = true;
        Mod.Log.Info("[RouteFilter.QueryExclusion] installed request-local native graph transaction hook");
    }

    private static bool Prefix(PathfindQueueSystem __instance, PathfindAction action, Entity owner,
        JobHandle dependencies, uint resultFrame, object system, bool highPriority)
        => !TryIntercept(__instance, action, owner, dependencies, resultFrame, system, default, false, highPriority);

    private static void BeforeQueueReset() => ReleaseAll();

    private static bool EventPrefix(PathfindQueueSystem __instance, PathfindAction action, Entity owner,
        JobHandle dependencies, uint resultFrame, object system, PathEventData eventData, bool highPriority)
        => !TryIntercept(__instance, action, owner, dependencies, resultFrame, system, eventData, true, highPriority);

    private static bool TryIntercept(PathfindQueueSystem queue, PathfindAction action, Entity owner,
        JobHandle dependencies, uint resultFrame, object system, PathEventData eventData, bool wantsEvent, bool highPriority)
    {
        if (!Available) return Refused(owner, "QueryHookUnavailable");
        if (s_World == null || !s_World.IsCreated) return Refused(owner, "RuntimeWorldUnavailable");
        if (!dependencies.IsCompleted) return Refused(owner, "NativeSetupDependencyStillPending");
        var index = s_World.GetExistingSystemManaged<RestrictionIndexSystem>();
        if (index == null) return Refused(owner, "RestrictionIndexMissing");
        if (index.ActiveTargetCount == 0) return Refused(owner, "NoActiveRestrictionTargets");
        if (Mod.RestrictionsDirty) return Refused(owner, "RestrictionIndexDirty");
        var persistence = s_World.GetExistingSystemManaged<RestrictionPersistenceSystem>();
        if (persistence == null || !persistence.ConfigurationEditable) return Refused(owner, "configuration not editable");
        var manager = s_World.EntityManager;
        if (!manager.Exists(owner) || manager.HasComponent<Deleted>(owner) || manager.HasComponent<Temp>(owner)) return Refused(owner, "owner invalid/deleted/temp");
        var isRail = manager.HasBuffer<TrainNavigationLane>(owner);
        EnforcementAttempt attempt;
        if (isRail)
        {
            if (Mod.Settings?.EnableRailEnforcement == false ||
                s_World.GetExistingSystemManaged<RailEnforcementBackend>()?.TryGetRequest(owner, out attempt) != true) return Refused(owner, "rail disabled/no owned attempt");
        }
        else
        {
            if (Mod.Settings?.EnableRoadEnforcement == false) return Refused(owner, "RoadEnforcementDisabledInSettings");
            var road = s_World.GetExistingSystemManaged<RoadEnforcementCoordinator>();
            if (road == null) return Refused(owner, "RoadCoordinatorMissing");
            if (!road.TryGetRequest(owner, out attempt, out var invariant)) return Refused(owner, invariant);
        }
        var frame = s_World.GetExistingSystemManaged<SimulationSystem>().frameIndex;
        if (attempt.RestrictionRevision != index.Revision || EnforcementPolicy.HasExpired(frame, attempt.AbsoluteDeadlineFrame) ||
            !manager.Exists(attempt.Target) || !manager.Exists(attempt.OwnedLane) ||
            manager.HasComponent<Deleted>(attempt.Target) || manager.HasComponent<Temp>(attempt.Target) ||
            manager.HasComponent<Deleted>(attempt.OwnedLane)) return Refused(owner, "revision stale/expired/target or lane invalid");
        dependencies.Complete(); // Already complete; release setup's safety ownership without a stall.
        if (manager.TryGetComponent(owner, out Controller controller) && controller.m_Controller != Entity.Null &&
            controller.m_Controller != owner) return Refused(owner, "owner is noncanonical controller");
        if (!manager.TryGetComponent(owner, out PathOwner pathOwner) || !EnforcementPolicy.IsExpectedSetup(attempt, pathOwner.m_State) ||
            !manager.TryGetComponent(owner, out Game.Common.Target destination) || destination.m_Target != attempt.NativeDestination) return Refused(owner, "unexpected PathOwner setup state/destination changed");
        var nextIndex = attempt.ViaLane2 != Entity.Null ? 2 : attempt.ViaLane == Entity.Null ? 0 : 1;
        if (isRail)
        {
            if (!manager.TryGetBuffer(owner, true, out DynamicBuffer<LayoutElement> layout) || layout.Length == 0 ||
                layout[0].m_Vehicle != attempt.NavigationVehicle ||
                !manager.HasComponent<TrainCurrentLane>(attempt.NavigationVehicle) ||
                manager.GetComponentData<TrainCurrentLane>(attempt.NavigationVehicle).m_Front.m_Lane != attempt.GateEntryLane) return Refused(owner, "rail current lane changed");
            if (manager.TryGetBuffer(owner, true, out DynamicBuffer<TrainNavigationLane> nav) &&
                ((nav.Length > nextIndex && nav[nextIndex].m_Lane != attempt.OwnedLane) ||
                 (nextIndex == 1 && nav.Length > 0 && nav[0].m_Lane != attempt.ViaLane))) return Refused(owner, "rail immediate navigation changed");
        }
        else if (!manager.HasComponent<CarCurrentLane>(owner) ||
                 manager.GetComponentData<CarCurrentLane>(owner).m_Lane != attempt.GateEntryLane) return Refused(owner, "road current lane changed");
        else
        {
            var current = manager.GetComponentData<CarCurrentLane>(owner);
            if (current.m_ChangeLane != Entity.Null || current.m_ChangeProgress != 0) return Refused(owner, "road lane change active");
            if (manager.TryGetBuffer(owner, true, out DynamicBuffer<CarNavigationLane> nav) &&
                ((nav.Length > nextIndex && nav[nextIndex].m_Lane != attempt.OwnedLane) ||
                 (nextIndex > 0 && nav.Length > 0 && nav[0].m_Lane != attempt.ViaLane) ||
                 (nextIndex == 2 && nav.Length > 1 && nav[1].m_Lane != attempt.ViaLane2))) return Refused(owner, "road immediate navigation changed");
        }
        // Admission can precede CompleteSetup by many frames. Recheck the live approach,
        // not the old candidate's distance. Too late or unknown means native grandfathering.
        if (!manager.TryGetComponent(attempt.GateEntryLane, out Curve approachCurve) ||
            !manager.TryGetComponent(isRail ? attempt.NavigationVehicle : owner, out Moving moving) ||
            !math.isfinite(attempt.Braking) || attempt.Braking <= 0 ||
            !math.isfinite(attempt.GeometryLength) || attempt.GeometryLength <= 0) return Refused(owner, "curve/movement/braking/geometry missing");
        var speed = math.length(moving.m_Velocity);
        // Pending clears navigation and can shorten the current traversal end. Use the admitted
        // gate endpoint on the unchanged entry lane; validate live position and destination.
        var position = isRail ? manager.GetComponentData<TrainCurrentLane>(attempt.NavigationVehicle).m_Front.m_CurvePosition.y :
            manager.GetComponentData<CarCurrentLane>(owner).m_CurvePosition.x;
        if (attempt.Forward ? position >= attempt.TraversalEnd : position <= attempt.TraversalEnd) return Refused(owner, "at/past admitted gate endpoint");
        var remaining = GateApproachDistance.Remaining(approachCurve, position, attempt.TraversalEnd);
        // This transaction edits only the graph seen by one query. Braking distance
        // is not a prerequisite; destructive result handling rechecks before-gate position.
        var required = 0f;
        if (!math.isfinite(speed) || !math.isfinite(remaining) || !EnforcementPolicy.CanRunQueryBeforeGate(remaining))
            return Refused(owner, $"LiveApproachDistanceInvariant remaining={remaining:F2} required={required:F2} speed={speed:F2} position={position:F4} endpoint={attempt.TraversalEnd:F4}");
        RetireCompleted();
        if (frame - s_Window >= 64) { s_Window = frame; s_Admissions = 0; }
        if (s_Admissions >= 8) return Refused(owner, "native query admission budget exhausted");
        var slot = -1;
        for (var i = 0; i < s_Transactions.Length; i++) if (s_Transactions[i] == null) { slot = i; break; }
        if (slot < 0) return Refused(owner, "transaction store full");
        var count = 0;
        var lanes = new NativeArray<Entity>(128, Allocator.Persistent);
        // Rail uses its own track topology, not the road index. Both exclude target-internal edges.
        if (isRail) count = s_World.GetExistingSystemManaged<RailEnforcementBackend>().CopyTargetLanes(attempt.Target, lanes);
        else if (index.TryGetInternalLanes(attempt.Target, out var internalLanes))
            foreach (var lane in internalLanes)
            {
                if (count == lanes.Length) { lanes.Dispose(); return Refused(owner, "target lane count exceeds 128"); }
                lanes[count++] = lane;
            }
        if (count <= 0) { lanes.Dispose(); return Refused(owner, "target internal lane set empty"); }
        // The transaction must include the imminent forbidden lane; refuse incomplete topology.
        var hasGate = false;
        for (var i = 0; i < count; i++) if (lanes[i] == attempt.OwnedLane) hasGate = true;
        if (!hasGate) { lanes.Dispose(); return Refused(owner, "imminent gate lane absent from exclusion set"); }
        var graphLanes = new NativeArray<Entity>(lanes.Length, Allocator.Persistent);
        var exactNoRouteScope = true;
        for (var i = 0; i < count; i++)
        {
            graphLanes[i] = lanes[i];
            // A direct node join uses its outbound edge. Excluding a two-way outbound edge
            // could also remove legal access from its far end, so failure is not no-route proof.
            if (!isRail && manager.HasComponent<Node>(attempt.Target) &&
                manager.TryGetComponent(lanes[i], out Game.Common.Owner scopeOwner) && scopeOwner.m_Owner != attempt.Target &&
                manager.TryGetComponent(lanes[i], out CarLane scopeLane) && (scopeLane.m_Flags & Game.Net.CarLaneFlags.Twoway) != 0)
                exactNoRouteScope = false;
            if (isRail && manager.HasComponent<Node>(attempt.Target) &&
                manager.TryGetComponent(lanes[i], out Game.Common.Owner trackOwner) && trackOwner.m_Owner != attempt.Target &&
                manager.TryGetComponent(lanes[i], out TrackLane trackScope) && (trackScope.m_Flags & TrackLaneFlags.Twoway) != 0)
                exactNoRouteScope = false;
            if (manager.TryGetComponent(lanes[i], out SlaveLane slave) &&
                manager.TryGetComponent(lanes[i], out Game.Common.Owner laneOwner) &&
                manager.TryGetBuffer(laneOwner.m_Owner, true, out DynamicBuffer<SubLane> ownerLanes) && slave.m_MasterIndex < ownerLanes.Length)
            {
                var master = ownerLanes[slave.m_MasterIndex].m_SubLane;
                if (manager.HasComponent<MasterLane>(master)) graphLanes[i] = master;
            }
        }
        var transaction = new Transaction { Lanes = lanes, GraphLanes = graphLanes, Attempt = attempt,
            ExactNoRouteScope = exactNoRouteScope,
            State = new NativeArray<int>(17, Allocator.Persistent),
            Edges = new NativeArray<EdgeID>(256, Allocator.Persistent), Originals = new NativeArray<PathMethod>(256, Allocator.Persistent),
            Target = attempt.Target, Revision = index.Revision, Expiry = attempt.AbsoluteDeadlineFrame,
            Queue = queue, Action = action, Owner = owner, ResultFrame = resultFrame, System = system,
            EventData = eventData, WantsEvent = wantsEvent, HighPriority = highPriority, Rail = isRail };
        // Enqueue occurs inside CompleteSetup, BEFORE the native queue drains its graph updates.
        // Capture only here. The postfix runs after those updates are scheduled, so its graph
        // writer dependencies include the current publication, not the previous graph generation.
        s_Transactions[slot] = transaction;
        if (owner == P0Diagnostics.Vehicle)
        {
            P0Diagnostics.Milestone("Lease", $"query-only transaction target={attempt.Target} expiry={attempt.AbsoluteDeadlineFrame} lanes={count}");
            P0Diagnostics.Record("LeaseRequested", "NOT_APPLICABLE: no physical lane lease");
            P0Diagnostics.Record("LeaseCreated", "NOT_APPLICABLE: no physical lane lease");
            P0Diagnostics.Record("ExclusionTransactionCreated", "True");
            P0Diagnostics.Milestone("RerouteRequested", "True");
            P0Diagnostics.Milestone("PathOwnerBefore", attempt.OriginalPathState.ToString());
            P0Diagnostics.Record("PathOwnerAfter", pathOwner.m_State.ToString());
            P0Diagnostics.Record("AttemptGeneration", attempt.Generation.ToString());
            P0Diagnostics.Record("LeaseGeneration", attempt.Generation.ToString()); P0Diagnostics.Record("LeaseExpiry", attempt.AbsoluteDeadlineFrame.ToString());
            P0Diagnostics.Milestone("PendingObserved", "True");
            P0Diagnostics.Milestone("Reroute", "native enqueue intercepted");
        }
        s_Admissions++; Queries++;
        if (isRail) { RailQueries++; s_World.GetExistingSystemManaged<RailEnforcementBackend>().MarkIntercepted(owner); }
        else { RoadQueries++; s_World.GetExistingSystemManaged<RoadEnforcementCoordinator>().MarkIntercepted(owner); }
        return true;
    }

    private static bool Refused(Entity owner, string reason)
    {
        if (owner == P0Diagnostics.Vehicle)
        { P0Diagnostics.Record("Reroute", reason); P0Diagnostics.Record("NativeEnqueueBypassReason", reason); }
        return false;
    }

    private static void AfterNativeQueueUpdate(PathfindQueueSystem __instance)
    {
        if (!Available) { ReleaseAll(); return; }
        RetireCompleted();
        for (var slot = 0; slot < s_Transactions.Length; slot++)
        {
            var transaction = s_Transactions[slot];
            if (transaction == null || transaction.Scheduled || transaction.Queue != __instance) continue;
            try { ScheduleTransaction(transaction); }
            catch (Exception error)
            {
                Available = false;
                Mod.Log.Error("[RouteFilter.QueryExclusion] dispatch FAILED; hook disabled: " + error);
                if (!transaction.Scheduled && transaction.State[0] == 0) ReturnToNative(transaction);
                else throw; // Do not dispose memory that a scheduled native job may still own.
            }
        }
    }

    private static void ScheduleTransaction(Transaction transaction)
    {
        var queue = transaction.Queue;
        var heuristic = s_World.GetOrCreateSystemManaged<Game.Prefabs.NetInitializeSystem>().GetHeuristicData();
        s_World.GetOrCreateSystemManaged<TransportLineSystem>().GetMaxTransportSpeed(out var passenger, out var cargo);
        var frame = s_World.GetExistingSystemManaged<SimulationSystem>().frameIndex;
        var results = queue.GetPathfindActions();
        // Reserve before starting native work; insertion below must not allocate afterwards.
        if (results.m_Items.Capacity < results.m_Items.Count + 1) results.m_Items.Capacity = results.m_Items.Count + 8;
        var job = new QueryTransactionJob { Lanes = transaction.Lanes, GraphLanes = transaction.GraphLanes,
            ExactNoRouteScope = transaction.ExactNoRouteScope, State = transaction.State,
            Edges = transaction.Edges, Originals = transaction.Originals,
            Action = (PathfindActionData*)transaction.Action.m_Data.GetUnsafePtr(), Heuristic = heuristic,
            PassengerSpeed = passenger, CargoSpeed = cargo, Seed = math.max(1u, frame ^ (uint)transaction.Owner.Index) };
        try { transaction.Handle = (JobHandle)s_Schedule.Invoke(queue, new object[] { job }); }
        catch (Exception error)
        {
            // Native scheduling can fail after scheduling the first graph copy. Never free its
            // scratch or enqueue the action a second time while that copy still owns it.
            Available = false;
            Interlocked.Exchange(ref UnsafeUtility.ArrayElementAsRef<int>(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(transaction.State), 6), 1);
            foreach (var worker in (IEnumerable)s_Workers.GetValue(queue))
                ((JobHandle)worker.GetType().GetField("m_WriteHandle").GetValue(worker)).Complete();
            Mod.Log.Error("[RouteFilter.QueryExclusion] scheduler FAILED; native fallback; hook disabled: " + error);
            if (transaction.State[0] == 0)
            {
                ReturnToNative(transaction);
                return;
            }
            transaction.Handle = default; // A completed partial job has a valid native result.
        }
        // Insert at the scheduled/pending boundary. Existing pending query indices and priority
        // counts remain valid; no ActionType is queued, so native workers cannot execute it twice.
        results.m_Items.Insert(results.m_NextIndex, new PathfindQueueSystem.ActionListItem<PathfindAction>(
            transaction.Action, transaction.Owner, transaction.Handle, PathFlags.Scheduled | (transaction.WantsEvent ? PathFlags.WantsEvent : 0),
            transaction.ResultFrame, transaction.EventData, transaction.System));
        results.m_NextIndex++;
        transaction.Scheduled = true;
    }

    internal static void RetireCompleted()
    {
        var liveWorld = s_World != null && s_World.IsCreated;
        var frame = liveWorld ? s_World.GetExistingSystemManaged<SimulationSystem>()?.frameIndex ?? 0 : 0;
        var revision = liveWorld ? s_World.GetExistingSystemManaged<RestrictionIndexSystem>()?.Revision ?? -1 : -1;
        for (var i = 0; i < s_Transactions.Length; i++)
        {
            var transaction = s_Transactions[i];
            if (transaction == null) continue;
            if (EnforcementPolicy.HasExpired(frame, transaction.Expiry) || transaction.Revision != revision ||
                !liveWorld || !s_World.EntityManager.Exists(transaction.Target) ||
                s_World.EntityManager.HasComponent<Deleted>(transaction.Target))
                Interlocked.Exchange(ref UnsafeUtility.ArrayElementAsRef<int>(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(transaction.State), 6), 1);
            if (!transaction.Scheduled || !transaction.Handle.IsCompleted) continue;
            transaction.Handle.Complete();
            if (transaction.Owner == P0Diagnostics.Vehicle)
            {
                P0Diagnostics.Record("ExcludedNativeResult", $"pathElements={transaction.State[16]} totalCost={math.asfloat(transaction.State[15])} errorCode={transaction.State[14]} graphTraversal={transaction.State[13]} exactNoRouteScope={transaction.ExactNoRouteScope} baselineCrossesTarget={transaction.State[10]}");
                P0Diagnostics.Record("GraphMutationIssued", (transaction.State[1] > 0).ToString());
                P0Diagnostics.Record("GraphEdgesWritten", transaction.State[1].ToString());
                P0Diagnostics.Record("ActualGraphEdgesAfterWrite", transaction.State[11].ToString());
                P0Diagnostics.Record("GraphReadbackMatchesWrite", (transaction.State[1] > 0 && transaction.State[11] == transaction.State[1]).ToString());
                P0Diagnostics.Record("UpdatedIssued", "False");
                P0Diagnostics.Record("GraphPublicationMechanism", "Native queue writer dependency; exclusion + query + restore in same owning job; no ECS Updated");
                if ((RoadQueryOutcome)transaction.State[9] == RoadQueryOutcome.EnforcementUncertain)
                    P0Diagnostics.Grandfather(transaction.Owner, "QueryProofInvariant: " + transaction.State[12] switch
                    {
                        1 => "ValidNativeEndpoints=false", 2 => "CompleteExclusion=false (required primary graph edge missing or already externally disabled)",
                        3 => "NativeQueryException", 4 => "OwnedGraphRestoreConflict", 5 => "AttemptCancelledDuringQuery",
                        6 => "NoRouteScopeNotExact (two-way outbound edge)", 7 => "NativePathStillContainsExcludedTarget",
                        8 => "NativeSearchNotExhausted (cost cutoff, skip/ignore path, error or empty traversal)",
                        9 => "OriginalGraphCounterfactualDidNotProveTargetCrossing", _ => "QueryProofNotCompleted"
                    });
            }
            // Road and rail use the same native held-result ownership handshake.
            {
                var outcome = (RoadQueryOutcome)transaction.State[9];
                var heldNoRoute = outcome == RoadQueryOutcome.ConfirmedNoAlternative;
                if (heldNoRoute && !OwnsHeldQueueItem(transaction))
                {
                    // No ownership proof: do not dereference/requeue/delete an action vanilla
                    // may have transferred elsewhere. Only our scratch arrays are disposed.
                    if (transaction.Owner == P0Diagnostics.Vehicle) P0Diagnostics.Milestone("PathResult", "terminal=Grandfathered; held action no longer owned by exact native queue item");
                    transaction.Dispose(); s_Transactions[i] = null; continue;
                }
                if (transaction.State[6] != 0) outcome = RoadQueryOutcome.EnforcementUncertain;
                var road = liveWorld ? s_World.GetExistingSystemManaged<RoadEnforcementCoordinator>() : null;
                var accepted = RoadQueryOutcome.EnforcementUncertain;
                if (transaction.Rail)
                {
                    var rail = liveWorld ? s_World.GetExistingSystemManaged<RailEnforcementBackend>() : null;
                    if (rail != null && !rail.ConsumeOwnedResult(transaction.Attempt, outcome, out accepted)) continue;
                }
                else if (road != null && !road.ConsumeOwnedResult(transaction.Attempt, outcome, out accepted)) continue;
                if (heldNoRoute)
                {
                    // Only held actions may be accessed here; vanilla may have disposed other results.
                    if (accepted == RoadQueryOutcome.ConfirmedNoAlternative)
                        transaction.Action.data.m_State = PathfindActionState.Completed;
                    else
                    {
                        transaction.Action.data.m_Result.Clear(); transaction.Action.data.m_Path.Clear();
                        transaction.Action.data.m_State = PathfindActionState.Pending;
                        if (transaction.Rail) RailFallbacks++; else RoadFallbacks++;
                        Fallbacks++;
                        if (transaction.Owner == P0Diagnostics.Vehicle) P0Diagnostics.Record("PathResult", "terminal=Grandfathered; no-route receipt ownership/revision/lifecycle revalidation failed");
                        ReturnToNative(transaction); continue;
                    }
                }
            }
            if (transaction.Owner == P0Diagnostics.Vehicle)
                P0Diagnostics.Record("GraphMutation", $"generation={transaction.Attempt.Generation} applied={transaction.State[1]} restored={transaction.State[2]} conflicts={transaction.State[3]} completeExclusion={transaction.State[8]} baselineCrossesTarget={transaction.State[10]} alternative={transaction.State[4]} fallback={transaction.State[5]} exception={transaction.State[7]}");
            Applied += (uint)transaction.State[1];
            Restored += (uint)transaction.State[2]; Conflicts += (uint)transaction.State[3];
            Alternatives += (uint)transaction.State[4]; Fallbacks += (uint)transaction.State[5];
            if (transaction.Rail) { RailAlternatives += (uint)transaction.State[4]; RailFallbacks += (uint)transaction.State[5]; }
            else { RoadAlternatives += (uint)transaction.State[4]; RoadFallbacks += (uint)transaction.State[5]; }
            if (transaction.State[7] != 0) Mod.Log.Error("[RouteFilter.QueryExclusion] native query exception; owned overlay restored; errorStage=" + transaction.State[7]);
            transaction.Dispose(); s_Transactions[i] = null;
        }
    }

    internal static void ReleaseAll()
    {
        for (var i = 0; i < s_Transactions.Length; i++)
        {
            var transaction = s_Transactions[i];
            if (transaction == null) continue;
            Interlocked.Exchange(ref UnsafeUtility.ArrayElementAsRef<int>(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(transaction.State), 6), 1);
            if (!transaction.Scheduled)
            {
                // Already marked consumed, so this re-enters the unmodified vanilla enqueue.
                ReturnToNative(transaction);
                continue;
            }
            transaction.Handle.Complete(); // Explicit Reset/save/unload only.
        }
        RetireCompleted();
    }

    private static void ReturnToNative(Transaction transaction)
    {
        if (transaction.Scheduled)
        {
            // The held no-route Action is already in the scheduled portion of this same list.
            // Transfer that ONE item back to native scheduling; never enqueue a second owner
            // of the same unsafe Action buffers.
            var list = transaction.Queue.GetPathfindActions();
            var found = false;
            for (var i = 0; i < list.m_NextIndex; i++)
            {
                var item = list.m_Items[i];
                if (item.m_Owner != transaction.Owner || item.m_Action.m_Data.GetUnsafePtr() != transaction.Action.m_Data.GetUnsafePtr()) continue;
                list.m_Items.RemoveAt(i); list.m_NextIndex--; found = true; break;
            }
            if (!found) throw new InvalidOperationException("Held RouteFilter action lost queue ownership; refusing duplicate enqueue");
        }
        if (transaction.WantsEvent) transaction.Queue.Enqueue(transaction.Action, transaction.Owner,
            default, transaction.ResultFrame, transaction.System, transaction.EventData, transaction.HighPriority);
        else transaction.Queue.Enqueue(transaction.Action, transaction.Owner, default,
            transaction.ResultFrame, transaction.System, transaction.HighPriority);
        for (var i = 0; i < s_Transactions.Length; i++)
            if (s_Transactions[i] == transaction) s_Transactions[i] = null;
        transaction.Dispose();
    }

    private static bool OwnsHeldQueueItem(Transaction transaction)
    {
        var list = transaction.Queue.GetPathfindActions();
        for (var i = 0; i < list.m_NextIndex; i++)
        {
            var item = list.m_Items[i];
            if (item.m_Owner == transaction.Owner && item.m_ResultFrame == transaction.ResultFrame &&
                item.m_System == transaction.System && (item.m_Flags & PathFlags.Scheduled) != 0 &&
                item.m_Action.m_Data.GetUnsafePtr() == transaction.Action.m_Data.GetUnsafePtr()) return true;
        }
        return false;
    }

    internal static void ResetCounters()
    {
        Queries = RoadQueries = RailQueries = Alternatives = Fallbacks = Applied = Restored = Conflicts = 0;
        RoadAlternatives = RailAlternatives = RoadFallbacks = RailFallbacks = 0;
    }

    internal static void Uninstall()
    {
        Available = false;
        ReleaseAll();
        new Harmony(HarmonyId).UnpatchAll(HarmonyId);
        s_World = null;
    }
}
