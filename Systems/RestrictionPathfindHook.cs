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
        internal void Dispose() { Lanes.Dispose(); State.Dispose(); Edges.Dispose(); Originals.Dispose(); }
    }

    // Managed job deliberately preserves finally semantics if the native query throws.
    // Native queue SetPathfindData schedules a copy for each worker graph. Atomic claim chooses
    // exactly one; the other copies do nothing. The queue owns every graph read/write dependency.
    private struct QueryTransactionJob : IJob, ModificationJobs.IPathfindModificationJob
    {
        public NativePathfindData Graph;
        [ReadOnly] public NativeArray<Entity> Lanes;
        [NativeDisableContainerSafetyRestriction] public NativeArray<int> State;
        [NativeDisableContainerSafetyRestriction] public NativeArray<EdgeID> Edges;
        [NativeDisableContainerSafetyRestriction] public NativeArray<PathMethod> Originals;
        [NativeDisableUnsafePtrRestriction] public PathfindActionData* Action;
        public PathfindHeuristicData Heuristic;
        public float PassengerSpeed, CargoSpeed;
        public uint Seed;
        public void SetPathfindData(NativePathfindData data) => Graph = data;

        public void Execute()
        {
            ref var claim = ref UnsafeUtility.ArrayElementAsRef<int>(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(State), 0);
            if (Interlocked.CompareExchange(ref claim, 1, 0) != 0) return;
            var graph = Graph.GetReadOnlyData();
            // A target can have primary and taxi/secondary edges. The overlay modifies methods
            // only; all connections, costs, rules and access authorizations are left unchanged.
            var count = 0;
            try
            {
                if (ReadState(6) == 0)
                    for (var i = 0; i < Lanes.Length; i++)
                    {
                        if (Lanes[i] == Entity.Null) continue;
                        if (graph.GetEdge(Lanes[i], out var primary)) Exclude(ref graph, primary, ref count);
                        if (graph.GetSecondaryEdge(Lanes[i], out var secondary)) Exclude(ref graph, secondary, ref count);
                    }
                State[1] = count;
                // The original vanilla request supplies endpoints, weights, methods and flags.
                // We neither fabricate routes nor move vehicles. The native result system adopts it.
                PathfindJobs.PathfindJob.Execute(Graph, Allocator.Temp, new Unity.Mathematics.Random(Seed),
                    Heuristic, PassengerSpeed, CargoSpeed, ref *Action);
                var avoidsTarget = Action->m_Path.Length > 0;
                for (var p = 0; count > 0 && avoidsTarget && p < Action->m_Path.Length; p++)
                    for (var lane = 0; lane < Lanes.Length; lane++)
                        if (Lanes[lane] != Entity.Null && Action->m_Path[p].m_Target == Lanes[lane])
                        { avoidsTarget = false; break; }
                if (count > 0 && !avoidsTarget) { Action->m_Path.Clear(); Action->m_Result.Clear(); }
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
            // A single bounded grandfather query prevents a failed exclusion from discarding the
            // original path and stranding a vehicle. This is the SAME native request, not another
            // Obsolete write or an attempt retry. The graph is already restored for this fallback.
            if ((Action->m_Path.Length == 0 && State[1] > 0) || State[7] != 0)
            {
                Action->m_Result.Clear();
                Action->m_Path.Clear();
                State[5] = 1;
                try
                {
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
            Action->m_State = PathfindActionState.Completed;
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
        if (!Available || s_World == null || !s_World.IsCreated || !dependencies.IsCompleted) return Refused(owner, "queue unavailable/world invalid/setup dependency still pending");
        var index = s_World.GetExistingSystemManaged<RestrictionIndexSystem>();
        if (index == null || index.ActiveTargetCount == 0 || Mod.RestrictionsDirty) return Refused(owner, "index absent/zero active targets/dirty restriction");
        var persistence = s_World.GetExistingSystemManaged<RestrictionPersistenceSystem>();
        if (persistence == null || !persistence.ConfigurationEditable) return Refused(owner, "configuration not editable");
        var manager = s_World.EntityManager;
        if (!manager.Exists(owner) || manager.HasComponent<Deleted>(owner) || manager.HasComponent<Temp>(owner)) return Refused(owner, "owner invalid/deleted/temp");
        var isRail = manager.HasComponent<Train>(owner);
        EnforcementAttempt attempt;
        if (isRail)
        {
            if (Mod.Settings?.EnableRailEnforcement == false ||
                s_World.GetExistingSystemManaged<RailEnforcementBackend>()?.TryGetRequest(owner, out attempt) != true) return Refused(owner, "rail disabled/no owned attempt");
        }
        else if (Mod.Settings?.EnableRoadEnforcement == false ||
                 s_World.GetExistingSystemManaged<RoadEnforcementCoordinator>()?.TryGetRequest(owner, out attempt) != true) return Refused(owner, "road disabled/no owned attempt");
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
        var nextIndex = attempt.ViaLane == Entity.Null ? 0 : 1;
        if (isRail)
        {
            if (!manager.HasComponent<TrainCurrentLane>(owner) ||
                manager.GetComponentData<TrainCurrentLane>(owner).m_Front.m_Lane != attempt.GateEntryLane) return Refused(owner, "rail current lane changed");
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
                 (nextIndex == 1 && nav.Length > 0 && nav[0].m_Lane != attempt.ViaLane))) return Refused(owner, "road immediate navigation changed");
        }
        // Admission can precede CompleteSetup by many frames. Recheck the live approach,
        // not the old candidate's distance. Too late or unknown means native grandfathering.
        if (!manager.TryGetComponent(attempt.GateEntryLane, out Curve approachCurve) ||
            !manager.TryGetComponent(owner, out Moving moving) ||
            !math.isfinite(attempt.Braking) || attempt.Braking <= 0 ||
            !math.isfinite(attempt.GeometryLength) || attempt.GeometryLength <= 0) return Refused(owner, "curve/movement/braking/geometry missing");
        var speed = math.length(moving.m_Velocity);
        // Pending clears navigation and can shorten the current traversal end. Use the admitted
        // gate endpoint on the unchanged entry lane; validate live position and destination.
        var position = isRail ? manager.GetComponentData<TrainCurrentLane>(owner).m_Front.m_CurvePosition.y :
            manager.GetComponentData<CarCurrentLane>(owner).m_CurvePosition.x;
        if (attempt.Forward ? position >= attempt.TraversalEnd : position <= attempt.TraversalEnd) return Refused(owner, "at/past admitted gate endpoint");
        var remaining = GateApproachDistance.Remaining(approachCurve, position, attempt.TraversalEnd);
        var required = speed * math.clamp(Mod.Settings?.RerouteLatencySeconds ?? 2.4f, 2.4f, 4f) +
            speed * speed / (2 * attempt.Braking) + speed * SafeToAttemptRerouteEvaluator.VanillaRoadNavigationTimeStep +
            attempt.GeometryLength + (Mod.Settings?.RerouteUncertaintyMetres ?? 5f);
        if (!math.isfinite(speed) || !math.isfinite(remaining) || !math.isfinite(required) || remaining <= required) return Refused(owner, "remaining distance insufficient/nonfinite");
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
        var transaction = new Transaction { Lanes = lanes, State = new NativeArray<int>(8, Allocator.Persistent),
            Edges = new NativeArray<EdgeID>(256, Allocator.Persistent), Originals = new NativeArray<PathMethod>(256, Allocator.Persistent),
            Target = attempt.Target, Revision = index.Revision, Expiry = attempt.AbsoluteDeadlineFrame,
            Queue = queue, Action = action, Owner = owner, ResultFrame = resultFrame, System = system,
            EventData = eventData, WantsEvent = wantsEvent, HighPriority = highPriority, Rail = isRail };
        // Enqueue occurs inside CompleteSetup, BEFORE the native queue drains its graph updates.
        // Capture only here. The postfix runs after those updates are scheduled, so its graph
        // writer dependencies include the current publication, not the previous graph generation.
        s_Transactions[slot] = transaction;
        if (owner == P0Diagnostics.Vehicle) { P0Diagnostics.Record("Lease", $"query-only transaction target={attempt.Target} expiry={attempt.AbsoluteDeadlineFrame} lanes={count}"); P0Diagnostics.Record("Reroute", "native enqueue intercepted"); }
        s_Admissions++; Queries++;
        if (isRail) { RailQueries++; s_World.GetExistingSystemManaged<RailEnforcementBackend>().MarkIntercepted(owner); }
        else { RoadQueries++; s_World.GetExistingSystemManaged<RoadEnforcementCoordinator>().MarkIntercepted(owner); }
        return true;
    }

    private static bool Refused(Entity owner, string reason)
    {
        if (owner == P0Diagnostics.Vehicle) P0Diagnostics.Record("Reroute", reason);
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
        var job = new QueryTransactionJob { Lanes = transaction.Lanes, State = transaction.State,
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
                P0Diagnostics.Record("GraphMutation", $"applied={transaction.State[1]} restored={transaction.State[2]} conflicts={transaction.State[3]} alternative={transaction.State[4]} fallback={transaction.State[5]} exception={transaction.State[7]}");
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
        if (transaction.WantsEvent) transaction.Queue.Enqueue(transaction.Action, transaction.Owner,
            default, transaction.ResultFrame, transaction.System, transaction.EventData, transaction.HighPriority);
        else transaction.Queue.Enqueue(transaction.Action, transaction.Owner, default,
            transaction.ResultFrame, transaction.System, transaction.HighPriority);
        for (var i = 0; i < s_Transactions.Length; i++)
            if (s_Transactions[i] == transaction) s_Transactions[i] = null;
        transaction.Dispose();
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
