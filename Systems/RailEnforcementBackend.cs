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
    internal readonly Entity ViaLane;

    internal DirectedTrackGate(Entity entryLane, Entity nextLane, Entity target, Entity viaLane = default)
    {
        EntryLane = entryLane;
        NextLane = nextLane;
        Target = target;
        ViaLane = viaLane;
    }
}

internal struct RailCandidate
{
    public Entity Consist;      // Controller owns Target, PathOwner and navigation.
    public Entity Front;        // Layout[0] owns physical movement and TrainCurrentLane.
    public Entity EntryLane;
    public Entity NextLane;
    public Entity Target;
    public Entity ViaLane;
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
/// Independent rail backend: directed consist detection, conservative admission and one native
/// reroute. RestrictionPathfindHook applies a request-local Track-method graph exclusion.
/// WORKLOAD GROWS WITH: watched entry lanes + relevant LaneObjects, controller/layout resolution
/// and target-prefab matching. Canonical consists are deduplicated per scan.
/// FAST PATH: no watched track lanes => no detection job.
/// COMPLEXITY: indexed relevant objects/gates; output capped at 4096.
/// ALLOCATIONS: preallocated scan output; topology allocation only on dirty revisions.
/// JOB DEPENDENCIES: detect, then bounded admission; shared PathOwner writers are dependency tracked.
/// MAIN THREAD SYNC: retire only completed work; Reset/unload/Dispose complete owned work.
/// STRUCTURAL CHANGES: none.
/// TrackLane has no road-style interval; this backend writes none. Query overlays use the native
/// graph writer scheduler. Implementation built; actual game avoidance NOT VERIFIED.
/// </summary>
public sealed partial class RailEnforcementBackend : GameSystemBase
{
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
        /// <c>PathOwner</c> and the navigation buffers from the controller; physical state
        /// comes from <c>LayoutElement[0]</c>. Anything
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
                !Navigation.HasBuffer(head))
            {
                head = Entity.Null;
                return false;
            }

            // Vanilla navigation reads route buffers from the controller and physical state
            // from layout[0]. A reversed consist need not put the controller first.
            if (!Layouts.TryGetBuffer(head, out var layout) || layout.Length == 0 ||
                !EntityStorage.Exists(layout[0].m_Vehicle) || DeletedData.HasComponent(layout[0].m_Vehicle) ||
                !Trains.HasComponent(layout[0].m_Vehicle) || !CurrentLanes.HasComponent(layout[0].m_Vehicle) ||
                (layout[0].m_Vehicle != head && (!Controllers.TryGetComponent(layout[0].m_Vehicle, out var frontController) ||
                    frontController.m_Controller != head)))
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

            var front = Layouts[head][0].m_Vehicle;
            var current = CurrentLanes[front];
            if (current.m_Front.m_Lane != gate.EntryLane) return false;
            if ((current.m_Front.m_LaneFlags & (TrainLaneFlags.Obsolete | TrainLaneFlags.Return |
                                                 TrainLaneFlags.ParkingSpace | TrainLaneFlags.Connection)) != 0)
                return false;

            if (!Navigation.TryGetBuffer(head, out var navigation) || navigation.Length == 0) return false;
            var nextIndex = gate.ViaLane == Entity.Null ? 0 : 1;
            if (navigation.Length <= nextIndex || navigation[nextIndex].m_Lane != gate.NextLane ||
                (nextIndex == 1 && navigation[0].m_Lane != gate.ViaLane)) return false;
            var nextDelta = navigation[nextIndex].m_CurvePosition.y - navigation[nextIndex].m_CurvePosition.x;
            if (nextDelta == 0f) return false;

            if (!Curves.TryGetComponent(gate.EntryLane, out var curve)) return false;
            curveLength = curve.m_Length;
            // Train front is float4: x=traversal start, y=current, z=lookahead, w=end.
            distanceToGate = GateApproachDistance.Remaining(curve, current.m_Front.m_CurvePosition.y, current.m_Front.m_CurvePosition.w);
            return true;
        }

        private RailCandidate Build(Entity head, DirectedTrackGate gate, Entity prefab, float distanceToGate, float curveLength)
        {
            var front = Layouts[head][0].m_Vehicle;
            var speed = MovingData.TryGetComponent(front, out var moving) ? math.length(moving.m_Velocity) : 0f;
            var braking = 0f;
            if (PrefabRefs.TryGetComponent(front, out var prefabRef) &&
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
                Consist = head, Front = front,
                EntryLane = gate.EntryLane,
                NextLane = gate.NextLane,
                Target = gate.Target,
                ViaLane = gate.ViaLane,
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



    [BurstCompile]
    private struct AdmitRailJob : IJob
    {
        [ReadOnly] public NativeArray<RailCandidate> Candidates;
        [ReadOnly] public EntityStorageInfoLookup Entities;
        [ReadOnly] public ComponentLookup<Deleted> Deleted;
        [ReadOnly] public ComponentLookup<TrainCurrentLane> Current;
        [ReadOnly] public ComponentLookup<Game.Common.Target> Destinations;
        [ReadOnly] public BufferLookup<TrainNavigationLane> Navigation;
        public ComponentLookup<PathOwner> Owners;
        public NativeList<EnforcementAttempt> Attempts;
        public NativeParallelHashMap<Entity, int> ByVehicle;
        public NativeArray<uint> Counters;
        public NativeArray<ulong> Generations;
        public uint Frame;
        public int Revision;
        public float Latency, Margin;
        public void Execute()
        {
            for (var i = Attempts.Length - 1; i >= 0; i--)
            {
                var attempt = Attempts[i];
                var valid = Entities.Exists(attempt.Vehicle) && Entities.Exists(attempt.Target) &&
                    !Deleted.HasComponent(attempt.Vehicle) && !Deleted.HasComponent(attempt.Target) &&
                    Current.TryGetComponent(attempt.NavigationVehicle, out var current) && current.m_Front.m_Lane == attempt.GateEntryLane;
                if (!valid || attempt.RestrictionRevision != Revision)
                {
                    RestoreRequest(attempt);
                    ByVehicle.Remove(attempt.Vehicle); Attempts.RemoveAtSwapBack(i);
                    if (i < Attempts.Length) ByVehicle[Attempts[i].Vehicle] = i;
                    continue;
                }
                if (EnforcementPolicy.HasExpired(Frame, attempt.AbsoluteDeadlineFrame))
                {
                    RestoreRequest(attempt);
                    attempt.State = EnforcementAttemptState.Grandfathered;
                    Attempts[i] = attempt; // Bounded terminal record until this approach ends.
                }
            }
            for (var i = 0; i < Candidates.Length; i++)
            {
                var c = Candidates[i];
                if (c.RestrictionRevision != Revision || Frame - c.DetectionFrame > 2 ||
                    !Entities.Exists(c.Consist) || !Entities.Exists(c.Target) || Deleted.HasComponent(c.Target) || Deleted.HasComponent(c.Consist) ||
                    !Current.TryGetComponent(c.Front, out var current) || current.m_Front.m_Lane != c.EntryLane ||
                    !Navigation.TryGetBuffer(c.Consist, out var nav) ||
                    (c.ViaLane == Entity.Null ? nav.Length == 0 || nav[0].m_Lane != c.NextLane :
                        nav.Length < 2 || nav[0].m_Lane != c.ViaLane || nav[1].m_Lane != c.NextLane) ||
                    !math.isfinite(c.Braking) || c.Braking <= 0 || c.ConsistLength <= 0 ||
                    !math.isfinite(c.Speed) || !math.isfinite(c.DistanceToGateAnchor)) { Counters[9]++; continue; }
                // Query-only admission: no physical braking/stop mutation is performed.
                if (!EnforcementPolicy.CanRunQueryBeforeGate(c.DistanceToGateAnchor)) { Counters[9]++; continue; }
                if (ByVehicle.ContainsKey(c.Consist) || Attempts.Length >= 64) continue;
                if (!Destinations.TryGetComponent(c.Consist, out var destination) || destination.m_Target == Entity.Null) continue;
                if (Frame - Counters[16] >= 64) { Counters[16] = Frame; Counters[18] = 0; }
                if (Counters[18] >= 4) { Counters[15]++; continue; }
                if (!Owners.TryGetComponent(c.Consist, out var owner) || !EnforcementPolicy.CanRequestReroute(owner.m_State)) continue;
                var before = owner.m_State; owner.m_State |= PathFlags.Obsolete; Owners[c.Consist] = owner;
                ByVehicle.TryAdd(c.Consist, Attempts.Length);
                Attempts.Add(new EnforcementAttempt { Vehicle = c.Consist, NavigationVehicle = c.Front, Target = c.Target, GateEntryLane = c.EntryLane, ViaLane = c.ViaLane,
                    OwnedLane = c.NextLane, MatchedPrefab = c.MatchedPrefab, Generation = ++Generations[0],
                    RestrictionRevision = Revision, RequestedFrame = Frame, AbsoluteDeadlineFrame = Frame + 240,
                    OriginalPathState = before, WrittenPathState = owner.m_State, OriginalElementIndex = owner.m_ElementIndex,
                    Braking = c.Braking, GeometryLength = c.ConsistLength,
                    NativeDestination = destination.m_Target, TraversalEnd = current.m_Front.m_CurvePosition.w,
                    Forward = current.m_Front.m_CurvePosition.w > current.m_Front.m_CurvePosition.y,
                    Backend = EnforcementBackend.Rail, State = EnforcementAttemptState.Requested, Category = c.Category });
                Counters[5]++; Counters[6]++; Counters[18]++;
            }
        }
        private void RestoreRequest(in EnforcementAttempt attempt)
        {
            if (Owners.TryGetComponent(attempt.Vehicle, out var owner) &&
                EnforcementPolicy.OwnsRequest(attempt, owner.m_State, owner.m_ElementIndex))
            { owner.m_State = attempt.OriginalPathState; Owners[attempt.Vehicle] = owner; }
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
    private NativeList<EnforcementAttempt> m_Attempts;
    private NativeParallelHashMap<Entity, int> m_ByVehicle;
    private NativeArray<uint> m_EnforcementCounters;
    private NativeArray<ulong> m_Generations;
    private int m_LastLaneObjectsScanned;
    public int LastCandidateCount { get; private set; }
    private JobHandle m_Work;
    private JobHandle m_DetectHandle;
    private bool m_DetectPending;
    private int m_RuntimeRevision = -1;

    private readonly HashSet<Entity> m_LaneSet = new();
    private readonly List<Entity> m_InternalTraversals = new();
    private readonly List<Entity> m_AdjacentTraversals = new();

    public bool EnforcementEnabled { get; set; } = true;
    public int ActiveAttempts { get; private set; }
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
        m_Attempts = new NativeList<EnforcementAttempt>(64, Allocator.Persistent);
        m_ByVehicle = new NativeParallelHashMap<Entity, int>(64, Allocator.Persistent);
        m_EnforcementCounters = new NativeArray<uint>(21, Allocator.Persistent);
        m_Generations = new NativeArray<ulong>(1, Allocator.Persistent);
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
        m_Attempts.Dispose(); m_ByVehicle.Dispose(); m_EnforcementCounters.Dispose(); m_Generations.Dispose();
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

        if (!m_Work.IsCompleted) return;
        m_Work.Complete();
        ActiveAttempts = m_Attempts.Length;

        if (!EnforcementEnabled || !RestrictionPathfindHook.Available || Mod.Settings?.EnableRailEnforcement == false ||
            !World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable || Mod.RestrictionsDirty)
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
        if (m_DetectPending)
        {
            m_DetectHandle.Complete();
            m_DetectPending = false;
            m_LastLaneObjectsScanned = m_WorkCounters[0];
            LastCandidateCount = m_Candidates.Length;
            foreach (var candidate in m_Candidates)
                if (candidate.Consist == P0Diagnostics.Vehicle && candidate.Target == P0Diagnostics.Target)
                {
                    P0Diagnostics.Milestone("CandidateCreated", "True");
                    P0Diagnostics.Milestone("DirectedGateMatched", "True");
                    P0Diagnostics.Record("RemainingDistance", candidate.DistanceToGateAnchor.ToString("F2"));
                    P0Diagnostics.Record("RequiredDistance", "0.00 (query-only; no physical stop mutation)");
                    if (m_ByVehicle.TryGetValue(candidate.Consist, out var admitted))
                    {
                        P0Diagnostics.Milestone("SafetyVerdict", "Safe");
                        P0Diagnostics.Milestone("RerouteRequested", "True");
                        P0Diagnostics.Record("AttemptGeneration", m_Attempts[admitted].Generation.ToString());
                    }
                    else if (!EnforcementPolicy.CanRunQueryBeforeGate(candidate.DistanceToGateAnchor))
                        P0Diagnostics.Grandfather(candidate.Consist, $"RailInsufficientAvailableDistance remaining={candidate.DistanceToGateAnchor:F2} required=0 (query-only) speed={candidate.Speed:F2} braking={candidate.Braking:F2} consistLength={candidate.ConsistLength:F2}");
                }
        }

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

        m_Work = new AdmitRailJob { Candidates = m_Candidates.AsDeferredJobArray(), Entities = GetEntityStorageInfoLookup(),
            Deleted = GetComponentLookup<Deleted>(true), Current = GetComponentLookup<TrainCurrentLane>(true),
            Navigation = GetBufferLookup<TrainNavigationLane>(true), Owners = GetComponentLookup<PathOwner>(),
            Destinations = GetComponentLookup<Game.Common.Target>(true),
            Attempts = m_Attempts, ByVehicle = m_ByVehicle, Counters = m_EnforcementCounters, Generations = m_Generations,
            Frame = m_Simulation.frameIndex, Revision = m_RuntimeRevision, Latency = ExpectedLatencySeconds,
            Margin = UncertaintyMargin }.Schedule(m_DetectHandle);
        Dependency = m_Work;
    }

    private float ExpectedLatencySeconds => math.clamp(Mod.Settings?.RerouteLatencySeconds ?? 2.4f, 2.4f, 4f);
    private float UncertaintyMargin => Mod.Settings?.RerouteUncertaintyMetres ?? 5f;

    public void ReleaseAll()
    {
        if (!m_Candidates.IsCreated) return;
        m_Work.Complete();
        m_DetectHandle.Complete();
        Dependency.Complete();
        m_DetectPending = false;
        RestrictionPathfindHook.ReleaseAll();
        for (var i = 0; i < m_Attempts.Length; i++)
        {
            var a = m_Attempts[i];
            if (!EntityManager.Exists(a.Vehicle) || !EntityManager.HasComponent<PathOwner>(a.Vehicle)) continue;
            var owner = EntityManager.GetComponentData<PathOwner>(a.Vehicle);
            if (!EnforcementPolicy.OwnsRequest(a, owner.m_State, owner.m_ElementIndex)) continue;
            owner.m_State = a.OriginalPathState; EntityManager.SetComponentData(a.Vehicle, owner);
        }
        m_Attempts.Clear(); m_ByVehicle.Clear(); ActiveAttempts = 0;
    }

    public void ResetRuntimeState()
    {
        ReleaseAll();
        m_Candidates.Clear();
        m_ConsistSeen.Clear();
        m_WatchedEntryLanes.Clear();
        m_GatesByEntryLane.Clear();
        m_TargetPrefabs.Clear();
        m_LaneSet.Clear();
        m_GateList.Clear();
        m_PrefabTargets.Clear();
        m_InternalTraversals.Clear();
        m_AdjacentTraversals.Clear();
        m_TargetLanes.Clear();
        m_RuntimeRevision = -1;
        for (var i = 0; i < m_WorkCounters.Length; i++) m_WorkCounters[i] = 0;
        m_LastLaneObjectsScanned = 0;
        LastCandidateCount = 0;
        for (var i = 0; i < m_EnforcementCounters.Length; i++) m_EnforcementCounters[i] = 0;
    }

    public void CopyCounters(NativeArray<uint> destination)
    {
        if (!destination.IsCreated) return;
        if (!m_Work.IsCompleted) return;
        m_Work.Complete();
        for (var i = 0; i < math.min(destination.Length, m_EnforcementCounters.Length); i++) destination[i] = m_EnforcementCounters[i];
    }

    internal bool TryGetRequest(Entity vehicle, out EnforcementAttempt attempt)
    {
        attempt = default;
        if (!m_Work.IsCompleted) return false;
        m_Work.Complete();
        if (!m_ByVehicle.TryGetValue(vehicle, out var index)) return false;
        attempt = m_Attempts[index]; return attempt.State == EnforcementAttemptState.Requested && !attempt.QueryIntercepted;
    }
    internal void MarkIntercepted(Entity vehicle)
    { if (m_ByVehicle.TryGetValue(vehicle, out var i)) { var a = m_Attempts[i]; a.QueryIntercepted = true; m_Attempts[i] = a; } }
    internal int CopyTargetLanes(Entity target, NativeArray<Entity> output)
    {
        if (!m_TargetLanes.TryGetValue(target, out var lanes)) return 0;
        if (lanes.Count > output.Length) return -1;
        var count = 0;
        foreach (var lane in lanes) output[count++] = lane;
        return count;
    }

    internal bool PathCrossesTarget(Entity target, DynamicBuffer<PathElement> path)
    {
        if (!m_TargetLanes.TryGetValue(target, out var lanes) || lanes.Count == 0) return true;
        for (var p = 0; p < path.Length; p++)
            foreach (var lane in lanes)
            {
                if (path[p].m_Target == lane) return true;
                if (EntityManager.TryGetComponent(lane, out SlaveLane slave) &&
                    EntityManager.TryGetComponent(lane, out Owner owner) &&
                    EntityManager.TryGetBuffer(owner.m_Owner, true, out DynamicBuffer<NetSubLane> subs) &&
                    slave.m_MasterIndex < subs.Length && path[p].m_Target == subs[slave.m_MasterIndex].m_SubLane) return true;
            }
        return false;
    }

    internal bool ConsumeOwnedResult(in EnforcementAttempt captured, RoadQueryOutcome outcome, out RoadQueryOutcome accepted)
    {
        accepted = RoadQueryOutcome.EnforcementUncertain;
        if (!m_Work.IsCompleted) return false;
        m_Work.Complete();
        if (!m_ByVehicle.TryGetValue(captured.Vehicle, out var i)) return true;
        var live = m_Attempts[i];
        if (!EnforcementPolicy.OwnsRailReceipt(live, captured))
        { P0Diagnostics.Grandfather(captured.Vehicle, $"RailReceiptIdentityMismatch liveGeneration={live.Generation} capturedGeneration={captured.Generation} liveState={live.State}"); return true; }
        var valid = m_Index.Revision == live.RestrictionRevision && !Mod.RestrictionsDirty &&
            !EnforcementPolicy.HasExpired(m_Simulation.frameIndex, live.AbsoluteDeadlineFrame) &&
            EntityManager.Exists(live.Vehicle) && !EntityManager.HasComponent<Deleted>(live.Vehicle) &&
            EntityManager.Exists(live.Target) && !EntityManager.HasComponent<Deleted>(live.Target) &&
            EntityManager.Exists(live.OwnedLane) && !EntityManager.HasComponent<Deleted>(live.OwnedLane) &&
            EntityManager.TryGetComponent(live.Vehicle, out Game.Common.Target destination) && destination.m_Target == live.NativeDestination &&
            EntityManager.TryGetComponent(live.NavigationVehicle, out TrainCurrentLane current) && current.m_Front.m_Lane == live.GateEntryLane &&
            EntityManager.TryGetBuffer(live.Vehicle, true, out DynamicBuffer<LayoutElement> layout) &&
            layout.Length > 0 && layout[0].m_Vehicle == live.NavigationVehicle && StillForbidden(live);
        if (outcome == RoadQueryOutcome.ConfirmedNoAlternative && valid)
        {
            var front = EntityManager.GetComponentData<TrainCurrentLane>(live.NavigationVehicle).m_Front;
            valid = EntityManager.TryGetComponent(live.Vehicle, out PathOwner owner) && EnforcementPolicy.IsExpectedSetup(live, owner.m_State) &&
                (live.Forward ? front.m_CurvePosition.y < live.TraversalEnd : front.m_CurvePosition.y > live.TraversalEnd);
            var members = EntityManager.GetBuffer<LayoutElement>(live.Vehicle, true);
            foreach (var member in members)
                if (!EntityManager.Exists(member.m_Vehicle) || EntityManager.HasComponent<Deleted>(member.m_Vehicle) ||
                    (member.m_Vehicle != live.Vehicle && (!EntityManager.TryGetComponent(member.m_Vehicle, out Controller controller) ||
                        controller.m_Controller != live.Vehicle))) valid = false;
            if (valid)
            {
                var commands = World.GetOrCreateSystemManaged<EndFrameBarrier>().CreateCommandBuffer();
                Mod.Log.Warn($"[RouteFilter.NoRouteRemoval] backend=Rail nativeApi=VehicleUtils.DeleteVehicle vehicle={live.Vehicle} prefab={live.MatchedPrefab} target={live.Target} revision={live.RestrictionRevision} generation={live.Generation} frame={m_Simulation.frameIndex} outcome={outcome} build={Mod.BuildId}");
                VehicleUtils.DeleteVehicle(commands, live.Vehicle, members);
                m_EnforcementCounters[20]++;
            }
        }
        accepted = valid ? outcome : RoadQueryOutcome.EnforcementUncertain;
        live.State = accepted == RoadQueryOutcome.AlternativePathFound ? EnforcementAttemptState.Rerouted :
            accepted == RoadQueryOutcome.ConfirmedNoAlternative ? EnforcementAttemptState.ConfirmedNoAlternative : EnforcementAttemptState.Grandfathered;
        m_Attempts[i] = live;
        if (accepted != RoadQueryOutcome.EnforcementUncertain) P0Diagnostics.OwnedResult(live.Vehicle, accepted, live.Generation);
        else P0Diagnostics.Grandfather(live.Vehicle, $"RailReceiptRejectedOrQueryUncertain generation={live.Generation} revision={live.RestrictionRevision} currentRevision={m_Index.Revision} deadline={live.AbsoluteDeadlineFrame} frame={m_Simulation.frameIndex} queryOutcome={outcome}");
        return true;
    }

    private bool StillForbidden(in EnforcementAttempt attempt)
    {
        if (!EntityManager.TryGetBuffer(attempt.Target, true, out DynamicBuffer<RestrictedVehicleAssetV1> restrictions)) return false;
        var restricted = false;
        foreach (var item in restrictions) if (item.m_Prefab == attempt.MatchedPrefab) restricted = true;
        if (!restricted) return false;
        if (EntityManager.TryGetComponent(attempt.Vehicle, out PrefabRef prefab) && prefab.m_Prefab == attempt.MatchedPrefab) return true;
        if (!EntityManager.TryGetBuffer(attempt.Vehicle, true, out DynamicBuffer<LayoutElement> layout)) return false;
        foreach (var member in layout)
            if (EntityManager.TryGetComponent(member.m_Vehicle, out PrefabRef part) && part.m_Prefab == attempt.MatchedPrefab &&
                (member.m_Vehicle == attempt.Vehicle || EntityManager.TryGetComponent(member.m_Vehicle, out Controller controller) &&
                    controller.m_Controller == attempt.Vehicle)) return true;
        return false;
    }

    public int LaneObjectsScanned => m_LastLaneObjectsScanned;

    private readonly Dictionary<Entity, HashSet<Entity>> m_PrefabTargets = new();
    private readonly List<DirectedTrackGate> m_GateList = new();
    private readonly Dictionary<Entity, HashSet<Entity>> m_TargetLanes = new();

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
        m_TargetLanes.Clear();

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
        var exclusion = new HashSet<Entity>(m_InternalTraversals);
        m_TargetLanes[target] = exclusion;

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
            if (!m_Index.TryGetConnectionLane(internalLane, out NetLane to)) continue;
            for (var j = 0; j < m_AdjacentTraversals.Count; j++)
            {
                var adjacent = m_AdjacentTraversals[j];
                if (adjacent == Entity.Null || adjacent == internalLane) continue;
                if (!m_Index.TryGetConnectionLane(adjacent, out NetLane from)) continue;
                var fromTwoWay = (EntityManager.GetComponentData<TrackLane>(adjacent).m_Flags & TrackLaneFlags.Twoway) != 0;
                var toTwoWay = (EntityManager.GetComponentData<TrackLane>(internalLane).m_Flags & TrackLaneFlags.Twoway) != 0;
                if (!from.m_EndNode.Equals(to.m_StartNode) &&
                    !(fromTwoWay && from.m_StartNode.Equals(to.m_StartNode)) &&
                    !(toTwoWay && from.m_EndNode.Equals(to.m_EndNode)) &&
                    !(fromTwoWay && toTwoWay && from.m_StartNode.Equals(to.m_EndNode))) continue;
                m_GateList.Add(new DirectedTrackGate(adjacent, internalLane, target));
                if (EntityManager.HasComponent<NetEdge>(target)) AddUpstreamTrackWatches(adjacent, internalLane, target);
            }
        }
        // Native direct track joins may coexist with node connectors, just like roads.
        if (EntityManager.HasComponent<Node>(target))
            foreach (var inbound in m_AdjacentTraversals)
            foreach (var outbound in m_AdjacentTraversals)
            {
                if (inbound == outbound || !EntityManager.TryGetComponent(inbound, out Owner entryOwner) ||
                    !EntityManager.TryGetComponent(outbound, out Owner exitOwner) || entryOwner.m_Owner == exitOwner.m_Owner ||
                    !m_Index.TryGetConnectionLane(inbound, out NetLane from) || !m_Index.TryGetConnectionLane(outbound, out NetLane to)) continue;
                var fromTwoWay = (EntityManager.GetComponentData<TrackLane>(inbound).m_Flags & TrackLaneFlags.Twoway) != 0;
                var toTwoWay = (EntityManager.GetComponentData<TrackLane>(outbound).m_Flags & TrackLaneFlags.Twoway) != 0;
                var join = from.m_EndNode.GetOwnerIndex() == target.Index &&
                    (from.m_EndNode.Equals(to.m_StartNode) || (toTwoWay && from.m_EndNode.Equals(to.m_EndNode))) ||
                    fromTwoWay && from.m_StartNode.GetOwnerIndex() == target.Index &&
                    (from.m_StartNode.Equals(to.m_StartNode) || (toTwoWay && from.m_StartNode.Equals(to.m_EndNode)));
                if (!join) continue;
                m_GateList.Add(new DirectedTrackGate(inbound, outbound, target));
                exclusion.Add(outbound);
            }
    }

    private void AddUpstreamTrackWatches(Entity connectorEntity, Entity targetLane, Entity target)
    {
        if (!EntityManager.TryGetComponent(connectorEntity, out NetLane connector) ||
            !EntityManager.TryGetComponent(connectorEntity, out Owner owner) ||
            !EntityManager.TryGetBuffer(owner.m_Owner, true, out DynamicBuffer<ConnectedEdge> edges)) return;
        foreach (var edge in edges)
        {
            if (edge.m_Edge == target || !EntityManager.TryGetBuffer(edge.m_Edge, true, out DynamicBuffer<NetSubLane> lanes)) continue;
            foreach (var sub in lanes)
            {
                var entity = sub.m_SubLane;
                if (!EntityManager.TryGetComponent(entity, out NetLane lane) || !EntityManager.HasComponent<TrackLane>(entity)) continue;
                if (!lane.m_EndNode.Equals(connector.m_StartNode) && !lane.m_StartNode.Equals(connector.m_EndNode) &&
                    !lane.m_StartNode.Equals(connector.m_StartNode) && !lane.m_EndNode.Equals(connector.m_EndNode)) continue;
                m_GateList.Add(new DirectedTrackGate(entity, targetLane, target, connectorEntity));
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
