using Colossal.Entities;
using Game.Common;
using Game;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using RouteFilter.Components;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using NetEdge = Game.Net.Edge;
using NetLane = Game.Net.Lane;
using NetSubLane = Game.Net.SubLane;
using TrackLane = Game.Net.TrackLane;

namespace RouteFilter.Systems;

/// <summary>
/// A directed transition from an upstream track lane into a restricted target's internal track lane.
/// Mirrors <see cref="DirectedEntryGate"/> but is a separate type on purpose: rail has no blockage
/// primitive, no road-style gate flags and its own ownership rules, and sharing a type would only
/// invite the road backend to be applied to rail by mistake.
/// </summary>
internal readonly struct DirectedTrackGate
{
    internal readonly Entity EntryLane;
    internal readonly Entity NextLane;
    internal readonly Entity Target;

    internal DirectedTrackGate(Entity entryLane, Entity nextLane, Entity target)
    {
        EntryLane = entryLane;
        NextLane = nextLane;
        Target = target;
    }
}

internal struct RailCandidate
{
    public Entity Consist;      // locomotive; the entity that owns Target, PathOwner and navigation.
    public Entity EntryLane;
    public Entity NextLane;
    public Entity Target;
    public Entity MatchedPrefab;
    public int RestrictionRevision;
    public uint DetectionFrame;
    public float DistanceToGateAnchor;
    public float CurveLength;
    public float Speed;
    public float Braking;
    public float ConsistLength;
    public RoadVehicleCategory Category;
}

/// <summary>
/// Rail enforcement backend.
///
/// WORKLOAD GROWS WITH: watched rail entry lanes plus the LaneObjects on them, plus new candidates
/// plus active attempts. It never scans trains, consists or track lanes by archetype.
/// FAST PATH: no watched rail entry lanes means the topology snapshot is empty and both jobs return
/// before touching anything.
/// COMPLEXITY: O(watched rail entry lanes + their LaneObjects + new candidates + active attempts).
/// ALLOCATIONS: none after construction.
/// JOB DEPENDENCIES: one detect job and one enforce job per frame, chained.
/// MAIN THREAD SYNC: none per frame.
/// STRUCTURAL CHANGES: none. This backend performs no ECS structural change at all.
///
/// BLOCKED BY VERIFIED TECHNICAL LIMITATION - no graph primitive.
/// <c>Game.Net.TrackLane</c> has no blockage interval and
/// <c>Game.Pathfind.PathUtils.GetTrackDriveSpecification</c> never emits
/// <c>RuleFlags.HasBlockage</c>, so there is no road-equivalent way to make the vanilla rail
/// pathfinder avoid a lane. The only mutable TrackLane fields are flags, speed limit and access
/// restriction, all of which are either recomputed by <c>Game.Pathfind.LaneDataSystem</c> or are
/// serialised. RouteFilter therefore mutates no TrackLane state at all, the observation backend performs zero PathOwner or TrackLane writes.
/// Rail avoidance remains unsupported until an independent safe primitive is proven. See RAIL_ENFORCEMENT_DESIGN.md.
/// </summary>
public sealed partial class RailEnforcementBackend : GameSystemBase
{
    private const int kMaxConsists = 32;
    private const int kMaxAttempts = 64;
    /// <summary>Absolute attempt deadline. Never extended and never retried.</summary>
    private const uint kAttemptDeadlineFrames = 600;

    private const int rLeasesAcquired = 0;   // slot reuse: rail has no lease, kept for symmetry
    private const int rReroutesRequested = 5;
    private const int rAttemptsOpened = 6;
    private const int rAttemptsResolved = 7;
    private const int rAttemptsUnresolved = 8;
    private const int rRefusedNotSafe = 9;
    private const int rRefusedAlreadyActive = 10;
    private const int rRefusedPathBusy = 11;
    private const int rRefusedExempt = 12;
    private const int rRefusedFixedRoute = 13;
    private const int rRefusedTopology = 14;
    private const int rRefusedBudget = 15;
    private const int rRefusedStoreFull = 17;
    private const int rRefusedStaleRevision = 18;
    private const int rGrandfathered = 19;
    private const int rCounterCount = 20;

    [BurstCompile]
    private struct DetectConsistsJob : IJob
    {
        private const uint kPruneIntervalFrames = 256;
        private const uint kObservationRetentionFrames = 1024;

        [ReadOnly] public NativeList<Entity> WatchedEntryLanes;
        [ReadOnly] public NativeParallelMultiHashMap<Entity, DirectedTrackGate> GatesByEntryLane;
        [ReadOnly] public NativeParallelMultiHashMap<Entity, Entity> TargetPrefabs;
        [ReadOnly] public EntityStorageInfoLookup EntityStorage;
        [ReadOnly] public BufferLookup<LaneObject> LaneObjects;
        [ReadOnly] public BufferLookup<LayoutElement> Layouts;
        [ReadOnly] public BufferLookup<TrainNavigationLane> Navigation;
        [ReadOnly] public ComponentLookup<Deleted> DeletedData;
        [ReadOnly] public ComponentLookup<Temp> TempData;
        [ReadOnly] public ComponentLookup<ParkedTrain> ParkedTrains;
        [ReadOnly] public ComponentLookup<Vehicle> Vehicles;
        [ReadOnly] public ComponentLookup<Train> Trains;
        [ReadOnly] public ComponentLookup<Controller> Controllers;
        [ReadOnly] public ComponentLookup<PrefabRef> PrefabRefs;
        [ReadOnly] public ComponentLookup<TrainCurrentLane> CurrentLanes;
        [ReadOnly] public ComponentLookup<Moving> MovingData;
        [ReadOnly] public ComponentLookup<Curve> Curves;
        [ReadOnly] public ComponentLookup<TrainData> PrefabTrainData;
        [ReadOnly] public ComponentLookup<ObjectGeometryData> PrefabGeometryData;

        public NativeList<RailCandidate> Candidates;
        public NativeParallelHashSet<Entity> ConsistSeen;
        /// <summary>[0] lane objects scanned, [1] consists canonicalised, [2] non-head members skipped.</summary>
        public NativeArray<int> WorkCounters;

        public Entity DebugTarget;
        public int RestrictionRevision;
        public uint Frame;

        public void Execute()
        {
            WorkCounters[0] = 0;
            WorkCounters[1] = 0;
            WorkCounters[2] = 0;

            for (var laneIndex = 0; laneIndex < WatchedEntryLanes.Length; laneIndex++)
            {
                var entryLane = WatchedEntryLanes[laneIndex];
                if (!EntityStorage.Exists(entryLane) || !LaneObjects.TryGetBuffer(entryLane, out var objects)) continue;
                if (!GatesByEntryLane.TryGetFirstValue(entryLane, out var firstGate, out var iterator)) continue;

                for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
                {
                    WorkCounters[0]++;
                    var physical = objects[objectIndex].m_LaneObject;
                    if (physical == Entity.Null || !EntityStorage.Exists(physical)) continue;
                    if (DeletedData.HasComponent(physical) || TempData.HasComponent(physical)) continue;
                    if (!Vehicles.HasComponent(physical) || !Trains.HasComponent(physical)) continue;
                    if (ParkedTrains.HasComponent(physical)) continue;

                    if (!TryCanonicalizeConsist(physical, out var head)) continue;
                    if (ConsistSeen.Count() >= 4096 || !ConsistSeen.Add(head)) continue;
                    WorkCounters[1]++;

                    var gate = firstGate;
                    var walk = iterator;
                    do
                    {
                        if (!VehiclePrefabMatcher.TryMatch(head, head, gate.Target, PrefabRefs, Layouts,
                                TargetPrefabs, out var matchedPrefab)) continue;
                        if (!TryValidateNavigation(head, gate, out var distanceToGate, out var curveLength)) continue;
                        if (Candidates.Length >= 4096) continue;
                        Candidates.Add(Build(head, gate, matchedPrefab, distanceToGate, curveLength));
                    } while (GatesByEntryLane.TryGetNextValue(out gate, ref walk));
                }
            }
        }

        /// <summary>
        /// Canonicalises a physical rail entity to the single entity that owns the consist's
        /// pathfind state. <c>Game.Simulation.TrainNavigationSystem</c> reads <c>Target</c>,
        /// <c>PathOwner</c> and the navigation buffers from <c>LayoutElement[0]</c>, so anything
        /// else in the consist is a member and must never be acted on. A long train therefore
        /// costs one candidate, one safety evaluation and at most one reroute, not one per carriage.
        /// </summary>
        private bool TryCanonicalizeConsist(Entity physical, out Entity head)
        {
            head = physical;
            var previous0 = Entity.Null;
            var previous1 = Entity.Null;
            var previous2 = Entity.Null;
            var previous3 = Entity.Null;
            for (var depth = 0; depth < 4; depth++)
            {
                if (!Controllers.TryGetComponent(head, out var controller) ||
                    controller.m_Controller == Entity.Null || controller.m_Controller == head)
                    break;
                var next = controller.m_Controller;
                if (next == physical || next == previous0 || next == previous1 || next == previous2 ||
                    next == previous3 || !EntityStorage.Exists(next) || DeletedData.HasComponent(next))
                {
                    head = Entity.Null;
                    return false;
                }
                previous3 = previous2;
                previous2 = previous1;
                previous1 = previous0;
                previous0 = head;
                head = next;
            }

            if (!EntityStorage.Exists(head) || DeletedData.HasComponent(head) ||
                !Trains.HasComponent(head) || !CurrentLanes.HasComponent(head) ||
                !Vehicles.HasComponent(head))
            {
                head = Entity.Null;
                return false;
            }

            // LayoutElement[0] is the consist's authoritative owner. If the controller chain
            // disagrees, refuse rather than guess.
            if (!Layouts.TryGetBuffer(head, out var layout) || layout.Length == 0 ||
                layout[0].m_Vehicle != head)
            {
                WorkCounters[2]++;
                head = Entity.Null;
                return false;
            }
            return true;
        }

        private bool TryValidateNavigation(Entity head, DirectedTrackGate gate, out float distanceToGate, out float curveLength)
        {
            distanceToGate = float.NaN;
            curveLength = 0f;
            if (!EntityStorage.Exists(gate.Target) || !EntityStorage.Exists(gate.NextLane)) return false;

            var current = CurrentLanes[head];
            if (current.m_Front.m_Lane != gate.EntryLane) return false;
            if ((current.m_Front.m_LaneFlags & (TrainLaneFlags.Obsolete | TrainLaneFlags.Return |
                                                 TrainLaneFlags.ParkingSpace | TrainLaneFlags.Connection)) != 0)
                return false;

            if (!Navigation.TryGetBuffer(head, out var navigation) || navigation.Length == 0) return false;
            if (navigation[0].m_Lane != gate.NextLane) return false;
            var nextDelta = navigation[0].m_CurvePosition.y - navigation[0].m_CurvePosition.x;
            if (nextDelta == 0f) return false;

            if (!Curves.TryGetComponent(gate.EntryLane, out var curve)) return false;
            curveLength = curve.m_Length;
            var anchorDelta = math.abs(current.m_Front.m_CurvePosition.z - current.m_Front.m_CurvePosition.x);
            distanceToGate = curveLength * anchorDelta;
            return true;
        }

        private RailCandidate Build(Entity head, DirectedTrackGate gate, Entity prefab, float distanceToGate, float curveLength)
        {
            var speed = MovingData.TryGetComponent(head, out var moving) ? math.length(moving.m_Velocity) : 0f;
            var braking = 0f;
            if (PrefabRefs.TryGetComponent(head, out var prefabRef) &&
                PrefabTrainData.TryGetComponent(prefabRef.m_Prefab, out var trainData))
                braking = trainData.m_Braking;

            var consistLength = 0f;
            if (Layouts.TryGetBuffer(head, out var layout))
                for (var i = 0; i < layout.Length; i++)
                {
                    var part = layout[i].m_Vehicle;
                    if (part == Entity.Null || !PrefabRefs.TryGetComponent(part, out var partRef)) continue;
                    if (!PrefabGeometryData.TryGetComponent(partRef.m_Prefab, out var geometry)) continue;
                    consistLength += math.max(0f, geometry.m_Bounds.max.z - geometry.m_Bounds.min.z);
                }

            return new RailCandidate
            {
                Consist = head,
                EntryLane = gate.EntryLane,
                NextLane = gate.NextLane,
                Target = gate.Target,
                MatchedPrefab = prefab,
                RestrictionRevision = RestrictionRevision,
                DetectionFrame = Frame,
                DistanceToGateAnchor = distanceToGate,
                CurveLength = curveLength,
                Speed = speed,
                Braking = braking,
                ConsistLength = consistLength,
                Category = RoadVehicleCategory.PublicTransport
            };
        }

    }



    // --- managed topology ---------------------------------------------------------------

    private EntityQuery m_RestrictedNodes;
    private EntityQuery m_RestrictedSegments;
    private RestrictionIndexSystem m_Index;
    private SimulationSystem m_Simulation;
    private UpdateSystem m_Update;

    private NativeList<Entity> m_WatchedEntryLanes;
    private NativeParallelMultiHashMap<Entity, DirectedTrackGate> m_GatesByEntryLane;
    private NativeParallelMultiHashMap<Entity, Entity> m_TargetPrefabs;
    private NativeList<RailCandidate> m_Candidates;
    private NativeParallelHashSet<Entity> m_ConsistSeen;
    private NativeArray<int> m_WorkCounters;
    private NativeArray<uint> m_Counters;
    private JobHandle m_Work;
    private JobHandle m_DetectHandle;
    private bool m_DetectPending;
    private int m_RuntimeRevision = -1;

    private readonly HashSet<Entity> m_LaneSet = new();
    private readonly List<Entity> m_InternalTraversals = new();
    private readonly List<Entity> m_AdjacentTraversals = new();

    public bool EnforcementEnabled { get; set; } = true;
    public int ActiveAttempts => 0;
    public int WatchedEntryLanes => m_WatchedEntryLanes.IsCreated ? m_WatchedEntryLanes.Length : 0;
    public int WatchedGates => m_GatesByEntryLane.IsCreated ? m_GatesByEntryLane.Count() : 0;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_Index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
        m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
        m_Update = World.GetOrCreateSystemManaged<UpdateSystem>();
        m_RestrictedNodes = GetEntityQuery(
            ComponentType.ReadOnly<NodeAssetRestrictionV1>(),
            ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_RestrictedSegments = GetEntityQuery(
            ComponentType.ReadOnly<NetEdge>(),
            ComponentType.ReadOnly<SegmentAssetRestrictionV1>(),
            ComponentType.ReadOnly<RestrictedVehicleAssetV1>());
        m_WatchedEntryLanes = new NativeList<Entity>(16, Allocator.Persistent);
        m_GatesByEntryLane = new NativeParallelMultiHashMap<Entity, DirectedTrackGate>(16, Allocator.Persistent);
        m_TargetPrefabs = new NativeParallelMultiHashMap<Entity, Entity>(16, Allocator.Persistent);
        m_Candidates = new NativeList<RailCandidate>(4096, Allocator.Persistent);
        m_ConsistSeen = new NativeParallelHashSet<Entity>(4096, Allocator.Persistent);
        m_WorkCounters = new NativeArray<int>(3, Allocator.Persistent);
        m_Counters = new NativeArray<uint>(rCounterCount, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        ReleaseAll();
        m_WatchedEntryLanes.Dispose();
        m_GatesByEntryLane.Dispose();
        m_TargetPrefabs.Dispose();
        m_Candidates.Dispose();
        m_ConsistSeen.Dispose();
        m_WorkCounters.Dispose();
        m_Counters.Dispose();
        base.OnDestroy();
    }

    protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
    {
        ReleaseAll();
        base.OnGamePreload(purpose, mode);
    }

    protected override void OnUpdate()
    {
        if (m_Update.currentPhase == SystemUpdatePhase.Serialize)
        {
            ReleaseAll();
            return;
        }

        if (!EnforcementEnabled || Mod.Settings?.EnableRailEnforcement == false ||
            !World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable)
        {
            if (ActiveAttempts != 0 || m_DetectPending) ReleaseAll();
            return;
        }

        if (!m_DetectPending && m_RuntimeRevision != m_Index.Revision)
            RebuildTopology();

        if (!m_DetectPending && m_WatchedEntryLanes.Length == 0)
        {
            if (ActiveAttempts != 0) ReleaseAll();
            return;
        }

        if (m_DetectPending && !m_DetectHandle.IsCompleted) return;
        if (m_DetectPending) { m_DetectHandle.Complete(); m_DetectPending = false; }

        m_Candidates.Clear();
        m_ConsistSeen.Clear();
        var detect = new DetectConsistsJob
        {
            WatchedEntryLanes = m_WatchedEntryLanes,
            GatesByEntryLane = m_GatesByEntryLane,
            TargetPrefabs = m_TargetPrefabs,
            EntityStorage = GetEntityStorageInfoLookup(),
            LaneObjects = GetBufferLookup<LaneObject>(true),
            Layouts = GetBufferLookup<LayoutElement>(true),
            Navigation = GetBufferLookup<TrainNavigationLane>(true),
            DeletedData = GetComponentLookup<Deleted>(true),
            TempData = GetComponentLookup<Temp>(true),
            ParkedTrains = GetComponentLookup<ParkedTrain>(true),
            Vehicles = GetComponentLookup<Vehicle>(true),
            Trains = GetComponentLookup<Train>(true),
            Controllers = GetComponentLookup<Controller>(true),
            PrefabRefs = GetComponentLookup<PrefabRef>(true),
            CurrentLanes = GetComponentLookup<TrainCurrentLane>(true),
            MovingData = GetComponentLookup<Moving>(true),
            Curves = GetComponentLookup<Curve>(true),
            PrefabTrainData = GetComponentLookup<TrainData>(true),
            PrefabGeometryData = GetComponentLookup<ObjectGeometryData>(true),
            Candidates = m_Candidates,
            ConsistSeen = m_ConsistSeen,
            WorkCounters = m_WorkCounters,
            DebugTarget = Entity.Null,
            RestrictionRevision = m_RuntimeRevision,
            Frame = m_Simulation.frameIndex
        };
        m_DetectHandle = detect.Schedule(Dependency);
        m_DetectPending = true;

        // Detection only until a graph exclusion primitive is proven. Marking Obsolete
        // alone can recompute the same route and is not rail restriction enforcement.
        m_Work = m_DetectHandle;
        Dependency = m_Work;
    }

    private float ExpectedLatencySeconds => Mod.Settings?.RerouteLatencySeconds ?? 1.0f;
    private float UncertaintyMargin => Mod.Settings?.RerouteUncertaintyMetres ?? 5f;

    public void ReleaseAll()
    {
        if (!m_Candidates.IsCreated) return;
        m_Work.Complete();
        m_DetectHandle.Complete();
        Dependency.Complete();
    }

    public void ResetRuntimeState()
    {
        ReleaseAll();
        m_Candidates.Clear();
        m_ConsistSeen.Clear();
        for (var i = 0; i < m_Counters.Length; i++) m_Counters[i] = 0;
    }

    public void CopyCounters(NativeArray<uint> destination)
    {
        if (!destination.IsCreated || destination.Length < rCounterCount) return;
        if (!m_Work.IsCompleted) return;
        m_Work.Complete();
        for (var i = 0; i < rCounterCount; i++) destination[i] = m_Counters[i];
    }

    /// <summary>LaneObjects scanned per frame. Zero is the release gate for "no rail cost".</summary>
    public int LaneObjectsScanned => m_WorkCounters.IsCreated && !m_DetectPending ? m_WorkCounters[0] : 0;

    private readonly Dictionary<Entity, HashSet<Entity>> m_PrefabTargets = new();
    private readonly List<DirectedTrackGate> m_GateList = new();

    /// <summary>
    /// Rebuilds the rail gate index. Runs only when the restriction configuration revision
    /// changes; a stable configuration does no managed work at all.
    /// </summary>
    private void RebuildTopology()
    {
        m_WatchedEntryLanes.Clear();
        m_GatesByEntryLane.Clear();
        m_TargetPrefabs.Clear();
        m_GateList.Clear();
        m_LaneSet.Clear();

        using var nodes = m_RestrictedNodes.ToEntityArray(Allocator.Temp);
        for (var i = 0; i < nodes.Length; i++) CollectTarget(nodes[i]);
        using var segments = m_RestrictedSegments.ToEntityArray(Allocator.Temp);
        for (var i = 0; i < segments.Length; i++) CollectTarget(segments[i]);

        for (var i = 0; i < m_GateList.Count; i++)
        {
            var gate = m_GateList[i];
            m_GatesByEntryLane.Add(gate.EntryLane, gate);
            if (m_LaneSet.Add(gate.EntryLane)) m_WatchedEntryLanes.Add(gate.EntryLane);
        }
        foreach (var pair in m_PrefabTargets)
            foreach (var prefab in pair.Value)
                if (pair.Key != Entity.Null && prefab != Entity.Null)
                    m_TargetPrefabs.Add(pair.Key, prefab);
        m_PrefabTargets.Clear();
        m_GateList.Clear();

        m_RuntimeRevision = m_Index.Revision;
        Mod.Log.Info($"[RouteFilter.Rail] topology revision={m_RuntimeRevision} gates={m_GatesByEntryLane.Count()} " +
                     $"watchedEntryLanes={m_WatchedEntryLanes.Length}");
    }

    private void CollectTarget(Entity target)
    {
        if (!EntityManager.TryGetBuffer(target, true, out DynamicBuffer<RestrictedVehicleAssetV1> assets) ||
            assets.Length == 0) return;

        if (!m_PrefabTargets.TryGetValue(target, out var prefabs))
            m_PrefabTargets[target] = prefabs = new HashSet<Entity>();
        for (var i = 0; i < assets.Length; i++)
            if (assets[i].m_Prefab != Entity.Null) prefabs.Add(assets[i].m_Prefab);
        if (prefabs.Count == 0) return;

        m_InternalTraversals.Clear();
        m_AdjacentTraversals.Clear();
        CollectTrackTraversals(target, m_InternalTraversals);

        if (EntityManager.HasComponent<NetEdge>(target))
        {
            var edge = EntityManager.GetComponentData<NetEdge>(target);
            if (edge.m_Start != Entity.Null) CollectTrackTraversals(edge.m_Start, m_AdjacentTraversals);
            if (edge.m_End != Entity.Null) CollectTrackTraversals(edge.m_End, m_AdjacentTraversals);
        }
        else if (EntityManager.TryGetBuffer(target, true, out DynamicBuffer<ConnectedEdge> connected))
        {
            for (var i = 0; i < connected.Length; i++)
            {
                var other = connected[i].m_Edge;
                if (other != Entity.Null && other != target) CollectTrackTraversals(other, m_AdjacentTraversals);
            }
        }

        for (var i = 0; i < m_InternalTraversals.Count; i++)
        {
            var internalLane = m_InternalTraversals[i];
            if (!EntityManager.TryGetComponent(internalLane, out NetLane to)) continue;
            for (var j = 0; j < m_AdjacentTraversals.Count; j++)
            {
                var adjacent = m_AdjacentTraversals[j];
                if (adjacent == Entity.Null || adjacent == internalLane) continue;
                if (!EntityManager.TryGetComponent(adjacent, out NetLane from)) continue;
                // Directed: the adjacent lane's end node must be the internal lane's start node.
                if (!from.m_EndNode.Equals(to.m_StartNode)) continue;
                m_GateList.Add(new DirectedTrackGate(adjacent, internalLane, target));
            }
        }
    }

    private void CollectTrackTraversals(Entity owner, List<Entity> output)
    {
        if (!EntityManager.TryGetBuffer(owner, true, out DynamicBuffer<NetSubLane> subLanes)) return;
        for (var i = 0; i < subLanes.Length; i++)
        {
            var lane = subLanes[i].m_SubLane;
            if (lane == Entity.Null || !EntityManager.HasComponent<TrackLane>(lane)) continue;
            if (!EntityManager.TryGetComponent(lane, out NetLane data)) continue;
            if (data.m_StartNode.Equals(data.m_EndNode)) continue;
            output.Add(lane);
        }
    }
}
