using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Vehicles;
using RouteFilter.Components;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using VehicleLaneFlags = Game.Vehicles.CarLaneFlags;

namespace RouteFilter.Systems;

/// <summary>
/// Lane-centric Phase 1B detector. It observes watched LaneObject buffers and emits
/// CandidateMatch records only; it never changes a vehicle, path, lane, or restriction.
/// </summary>
public sealed partial class RestrictionCandidateSystem : GameSystemBase
{
    private struct DirectedEntryGateRuntime
    {
        public Entity EntryLane;
        public Entity NextLane;
        public Entity Target;
        public RestrictionTopologyTargetType TargetType;
        public LaneTraversalDirection EntryDirection;
        public LaneTraversalDirection NextDirection;
        public RestrictionEndpoint TargetEndpoint;
    }

    private struct CandidateIdentity : IEquatable<CandidateIdentity>
    {
        public Entity Vehicle;
        public Entity EntryLane;
        public Entity NextLane;
        public Entity Target;

        public bool Equals(CandidateIdentity other)
            => Vehicle == other.Vehicle && EntryLane == other.EntryLane &&
               NextLane == other.NextLane && Target == other.Target;
    }

    private struct CandidateObservation
    {
        public CandidateIdentity Identity;
        public uint FirstSeenFrame;
        public uint LastSeenFrame;
    }

    [BurstCompile]
    private struct CollectCandidatesJob : IJob
    {
        private const uint kObservationRetentionFrames = 1024;

        [ReadOnly] public NativeList<Entity> WatchedEntryLanes;
        [ReadOnly] public NativeParallelMultiHashMap<Entity, DirectedEntryGateRuntime> GatesByEntryLane;
        [ReadOnly] public NativeParallelMultiHashMap<Entity, Entity> TargetPrefabs;
        [ReadOnly] public EntityStorageInfoLookup EntityStorage;
        [ReadOnly] public BufferLookup<LaneObject> LaneObjects;
        [ReadOnly] public BufferLookup<CarNavigationLane> NavigationLanes;
        [ReadOnly] public BufferLookup<LayoutElement> Layouts;
        [ReadOnly] public ComponentLookup<Deleted> DeletedData;
        [ReadOnly] public ComponentLookup<ParkedCar> ParkedCars;
        [ReadOnly] public ComponentLookup<Vehicle> Vehicles;
        [ReadOnly] public ComponentLookup<Car> Cars;
        [ReadOnly] public ComponentLookup<Train> Trains;
        [ReadOnly] public ComponentLookup<Controller> Controllers;
        [ReadOnly] public ComponentLookup<PrefabRef> PrefabRefs;
        [ReadOnly] public ComponentLookup<CarCurrentLane> CurrentLanes;
        [ReadOnly] public ComponentLookup<Moving> MovingData;
        [ReadOnly] public ComponentLookup<Curve> Curves;
        [ReadOnly] public ComponentLookup<MasterLane> MasterLanes;
        [ReadOnly] public ComponentLookup<CarData> PrefabCarData;

        public NativeList<CandidateMatch> Matches;
        public NativeList<RejectedCandidate> DebugRejections;
        public NativeList<Entity> CanonicalSeen;
        public NativeList<CandidateObservation> Observations;
        public NativeArray<CandidateDiagnosticCounters> Counters;
        public NativeArray<int> RejectionReasons;

        public Entity DebugTarget;
        public int RestrictionRevision;
        public uint DetectionFrame;

        public void Execute()
        {
            for (var i = 0; i < RejectionReasons.Length; i++) RejectionReasons[i] = 0;
            var counters = new CandidateDiagnosticCounters
            {
                m_WatchedGates = GatesByEntryLane.Count(),
                m_WatchedEntryLanes = WatchedEntryLanes.Length
            };

            PruneObservations();

            for (var laneIndex = 0; laneIndex < WatchedEntryLanes.Length; laneIndex++)
            {
                var entryLane = WatchedEntryLanes[laneIndex];
                if (!EntityStorage.Exists(entryLane))
                {
                    counters.m_AmbiguousRejected++;
                    AddDebugRejection(Entity.Null, Entity.Null, entryLane, Entity.Null, Entity.Null,
                        RejectedCandidateReason.LaneDeleted);
                    continue;
                }
                if (!LaneObjects.TryGetBuffer(entryLane, out var laneObjects))
                {
                    counters.m_AmbiguousRejected++;
                    AddDebugRejection(Entity.Null, Entity.Null, entryLane, Entity.Null, Entity.Null,
                        RejectedCandidateReason.LaneObjectBufferMissing);
                    continue;
                }
                if (!GatesByEntryLane.TryGetFirstValue(entryLane, out var firstGate, out var firstIterator))
                    continue;

                for (var objectIndex = 0; objectIndex < laneObjects.Length; objectIndex++)
                {
                    counters.m_LaneObjectsScanned++;
                    var laneObject = laneObjects[objectIndex];
                    var physical = laneObject.m_LaneObject;
                    if (physical == Entity.Null || !EntityStorage.Exists(physical))
                    {
                        counters.m_AmbiguousRejected++;
                        AddDebugRejection(physical, Entity.Null, entryLane, Entity.Null, firstGate.Target,
                            RejectedCandidateReason.PhysicalEntityInvalid);
                        continue;
                    }

                    counters.m_PhysicalEntitiesSeen++;
                    if (DeletedData.HasComponent(physical))
                    {
                        counters.m_DeletedRejected++;
                        AddDebugRejection(physical, Entity.Null, entryLane, Entity.Null, firstGate.Target,
                            RejectedCandidateReason.Deleted);
                        continue;
                    }
                    if (ParkedCars.HasComponent(physical))
                    {
                        counters.m_AmbiguousRejected++;
                        AddDebugRejection(physical, Entity.Null, entryLane, Entity.Null, firstGate.Target,
                            RejectedCandidateReason.Parked);
                        continue;
                    }
                    if (!Vehicles.HasComponent(physical))
                    {
                        counters.m_AmbiguousRejected++;
                        AddDebugRejection(physical, Entity.Null, entryLane, Entity.Null, firstGate.Target,
                            RejectedCandidateReason.NotVehicle);
                        continue;
                    }

                    if (!TryResolveCanonicalVehicle(physical, out var canonical, out var canonicalReason))
                    {
                        counters.m_AmbiguousRejected++;
                        AddDebugRejection(physical, canonical, entryLane, Entity.Null, firstGate.Target,
                            canonicalReason);
                        continue;
                    }

                    if (!ContainsEntity(CanonicalSeen, canonical))
                    {
                        CanonicalSeen.Add(canonical);
                        counters.m_CanonicalVehicles++;
                    }

                    var gate = firstGate;
                    var iterator = firstIterator;
                    do
                    {
                        ProcessGate(physical, canonical, laneObject, gate, ref counters);
                    } while (GatesByEntryLane.TryGetNextValue(out gate, ref iterator));
                }
            }

            counters.m_CandidatesMatched = Matches.Length;
            Counters[0] = counters;
        }

        private void ProcessGate(
            Entity physical,
            Entity canonical,
            LaneObject laneObject,
            DirectedEntryGateRuntime gate,
            ref CandidateDiagnosticCounters counters)
        {
            if (!EntityStorage.Exists(gate.Target) || !EntityStorage.Exists(gate.NextLane))
            {
                counters.m_AmbiguousRejected++;
                AddDebugRejection(physical, canonical, gate.EntryLane, gate.NextLane, gate.Target,
                    RejectedCandidateReason.TargetDeleted);
                return;
            }

            if (!VehiclePrefabMatcher.TryMatch(
                    physical,
                    canonical,
                    gate.Target,
                    PrefabRefs,
                    Layouts,
                    TargetPrefabs,
                    out var matchedPrefab))
            {
                counters.m_PrefabRejected++;
                AddDebugRejection(physical, canonical, gate.EntryLane, gate.NextLane, gate.Target,
                    RejectedCandidateReason.PrefabNotRestricted);
                return;
            }

            if (!TryValidateImmediateNavigation(
                    canonical,
                    gate,
                    out var currentLane,
                    out var observedNextLane,
                    out var reason))
            {
                if (reason == RejectedCandidateReason.StaleLaneObject)
                    counters.m_StaleLaneObjectRejected++;
                else if (reason == RejectedCandidateReason.ImmediateNavigationMissing ||
                         reason == RejectedCandidateReason.ImmediateLaneMismatch ||
                         reason == RejectedCandidateReason.EntryDirectionMismatch ||
                         reason == RejectedCandidateReason.NextDirectionMismatch)
                    counters.m_NavigationRejected++;
                else
                    counters.m_AmbiguousRejected++;

                AddNavigationDebugRejection(
                    physical,
                    canonical,
                    laneObject,
                    gate,
                    currentLane,
                    observedNextLane,
                    reason);
                return;
            }

            var identity = new CandidateIdentity
            {
                Vehicle = canonical,
                EntryLane = gate.EntryLane,
                NextLane = gate.NextLane,
                Target = gate.Target
            };
            if (ContainsMatch(identity))
            {
                counters.m_DuplicatesRemoved++;
                return;
            }

            var firstSeen = GetFirstSeenFrame(identity);
            var curveLength = Curves.TryGetComponent(gate.EntryLane, out var curve) ? curve.m_Length : 0f;
            var remainingDistance = curveLength * math.abs(currentLane.m_CurvePosition.z - currentLane.m_CurvePosition.x);
            // CarCurrentLane.x is the current traversal's destination anchor; z is the
            // vehicle's current anchor. Keep the actual anchor rather than replacing it
            // with a normalized lane endpoint, because partial traversals need not end at 0/1.
            var endpoint = currentLane.m_CurvePosition.x;
            var atOrPastAnchor = gate.EntryDirection == LaneTraversalDirection.Forward
                ? currentLane.m_CurvePosition.z >= currentLane.m_CurvePosition.x
                : currentLane.m_CurvePosition.z <= currentLane.m_CurvePosition.x;
            var velocity = MovingData.TryGetComponent(canonical, out var moving)
                ? moving.m_Velocity
                : MovingData.TryGetComponent(physical, out moving) ? moving.m_Velocity : default;
            var braking = 0f;
            if (PrefabRefs.TryGetComponent(canonical, out var prefabRef) &&
                PrefabCarData.TryGetComponent(prefabRef.m_Prefab, out var carData))
                braking = carData.m_Braking;

            Matches.Add(new CandidateMatch
            {
                m_Vehicle = canonical,
                m_PhysicalVehicle = physical,
                m_EntryLane = gate.EntryLane,
                m_NextLane = gate.NextLane,
                m_Target = gate.Target,
                m_RestrictedPrefab = matchedPrefab,
                m_MatchedPrefab = matchedPrefab,
                m_TargetType = gate.TargetType,
                m_EntryDirection = gate.EntryDirection,
                m_NextDirection = gate.NextDirection,
                m_TargetEndpoint = gate.TargetEndpoint,
                m_Source = CandidateSource.LaneObject,
                m_RestrictionRevision = RestrictionRevision,
                m_DetectionFrame = DetectionFrame,
                m_FirstSeenFrame = firstSeen,
                m_CurrentCurvePosition = currentLane.m_CurvePosition,
                m_LaneObjectCurvePosition = laneObject.m_CurvePosition,
                m_Velocity = velocity,
                m_ChangeLane = currentLane.m_ChangeLane,
                m_ChangeProgress = currentLane.m_ChangeProgress,
                m_EntryTraversalEndpoint = endpoint,
                m_EntryLaneLength = curveLength,
                m_BrakingCapability = braking,
                m_DirectionAwareRemainingDistanceApprox = remainingDistance,
                m_AtOrPastEntryAnchorWhenFirstSeen = atOrPastAnchor && firstSeen == DetectionFrame
            });
        }

        private bool TryResolveCanonicalVehicle(
            Entity physical,
            out Entity canonical,
            out RejectedCandidateReason reason)
        {
            canonical = physical;
            reason = RejectedCandidateReason.None;
            var previous0 = Entity.Null;
            var previous1 = Entity.Null;
            var previous2 = Entity.Null;
            var previous3 = Entity.Null;

            for (var depth = 0; depth < 4; depth++)
            {
                if (!Controllers.TryGetComponent(canonical, out var controller) ||
                    controller.m_Controller == Entity.Null || controller.m_Controller == canonical)
                    break;

                var next = controller.m_Controller;
                if (next == physical || next == previous0 || next == previous1 ||
                    next == previous2 || next == previous3)
                {
                    reason = RejectedCandidateReason.CanonicalControllerInvalid;
                    return false;
                }

                previous3 = previous2;
                previous2 = previous1;
                previous1 = previous0;
                previous0 = canonical;
                canonical = next;
                if (!EntityStorage.Exists(canonical) || DeletedData.HasComponent(canonical))
                {
                    reason = RejectedCandidateReason.CanonicalControllerInvalid;
                    return false;
                }
            }

            // A fifth distinct controller would make the normalization result dependent on
            // an arbitrary depth cap. Treat it as unsupported instead of emitting a false
            // canonical identity.
            if (Controllers.TryGetComponent(canonical, out var remainingController) &&
                remainingController.m_Controller != Entity.Null &&
                remainingController.m_Controller != canonical)
            {
                reason = RejectedCandidateReason.CanonicalControllerInvalid;
                return false;
            }

            if (!EntityStorage.Exists(canonical) || !Vehicles.HasComponent(canonical))
            {
                reason = RejectedCandidateReason.CanonicalControllerInvalid;
                return false;
            }
            if (Trains.HasComponent(physical) || Trains.HasComponent(canonical))
            {
                reason = RejectedCandidateReason.RailUnsupported;
                return false;
            }
            if (!Cars.HasComponent(canonical) || !CurrentLanes.HasComponent(canonical))
            {
                reason = RejectedCandidateReason.RoadControllerMissing;
                return false;
            }
            return true;
        }

        private bool TryValidateImmediateNavigation(
            Entity canonical,
            DirectedEntryGateRuntime gate,
            out CarCurrentLane currentLane,
            out Entity observedNextLane,
            out RejectedCandidateReason reason)
        {
            currentLane = CurrentLanes[canonical];
            observedNextLane = Entity.Null;
            reason = RejectedCandidateReason.None;

            if (currentLane.m_Lane != gate.EntryLane)
            {
                reason = RejectedCandidateReason.StaleLaneObject;
                return false;
            }
            if (currentLane.m_ChangeLane != Entity.Null || currentLane.m_ChangeProgress != 0f)
            {
                reason = RejectedCandidateReason.LaneChangeAmbiguous;
                return false;
            }

            const VehicleLaneFlags specialFlags =
                VehicleLaneFlags.TransformTarget |
                VehicleLaneFlags.ParkingSpace |
                VehicleLaneFlags.EnteringRoad |
                VehicleLaneFlags.Obsolete |
                VehicleLaneFlags.Area;
            if ((currentLane.m_LaneFlags & specialFlags) != 0)
            {
                reason = RejectedCandidateReason.SpecialLaneTransition;
                return false;
            }
            if (MasterLanes.HasComponent(gate.EntryLane) || MasterLanes.HasComponent(gate.NextLane))
            {
                reason = RejectedCandidateReason.MasterLaneUnsupported;
                return false;
            }
            if (!NavigationLanes.TryGetBuffer(canonical, out var navigation) || navigation.Length == 0)
            {
                reason = RejectedCandidateReason.ImmediateNavigationMissing;
                return false;
            }

            var next = navigation[0];
            observedNextLane = next.m_Lane;
            if (next.m_Lane != gate.NextLane)
            {
                reason = RejectedCandidateReason.ImmediateLaneMismatch;
                return false;
            }

            var entryDelta = currentLane.m_CurvePosition.x - currentLane.m_CurvePosition.z;
            if ((gate.EntryDirection == LaneTraversalDirection.Forward && entryDelta < 0f) ||
                (gate.EntryDirection == LaneTraversalDirection.Reverse && entryDelta > 0f))
            {
                reason = RejectedCandidateReason.EntryDirectionMismatch;
                return false;
            }

            var nextDelta = next.m_CurvePosition.y - next.m_CurvePosition.x;
            if (nextDelta == 0f ||
                (gate.NextDirection == LaneTraversalDirection.Forward && nextDelta < 0f) ||
                (gate.NextDirection == LaneTraversalDirection.Reverse && nextDelta > 0f))
            {
                reason = RejectedCandidateReason.NextDirectionMismatch;
                return false;
            }

            return true;
        }

        private void AddNavigationDebugRejection(
            Entity physical,
            Entity vehicle,
            LaneObject laneObject,
            DirectedEntryGateRuntime gate,
            CarCurrentLane currentLane,
            Entity observedNextLane,
            RejectedCandidateReason reason)
        {
            RejectionReasons[(int)reason]++;
            if (DebugTarget == Entity.Null || gate.Target != DebugTarget) return;

            var curveLength = Curves.TryGetComponent(gate.EntryLane, out var curve) ? curve.m_Length : 0f;
            var velocity = MovingData.TryGetComponent(vehicle, out var moving)
                ? moving.m_Velocity
                : MovingData.TryGetComponent(physical, out moving) ? moving.m_Velocity : default;
            var atOrPastAnchor = gate.EntryDirection == LaneTraversalDirection.Forward
                ? currentLane.m_CurvePosition.z >= currentLane.m_CurvePosition.x
                : currentLane.m_CurvePosition.z <= currentLane.m_CurvePosition.x;

            DebugRejections.Add(new RejectedCandidate
            {
                m_PhysicalVehicle = physical,
                m_Vehicle = vehicle,
                m_EntryLane = gate.EntryLane,
                m_NextLane = gate.NextLane,
                m_ObservedCurrentLane = currentLane.m_Lane,
                m_ObservedNextLane = observedNextLane,
                m_Target = gate.Target,
                m_Reason = reason,
                m_DetectionFrame = DetectionFrame,
                m_CurrentCurvePosition = currentLane.m_CurvePosition,
                m_LaneObjectCurvePosition = laneObject.m_CurvePosition,
                m_Velocity = velocity,
                m_DirectionAwareRemainingDistanceApprox =
                    curveLength * math.abs(currentLane.m_CurvePosition.x - currentLane.m_CurvePosition.z),
                m_LaneObjectAppearedStale = reason == RejectedCandidateReason.StaleLaneObject,
                m_AtOrPastEntryAnchor = atOrPastAnchor
            });
        }

        private bool ContainsMatch(CandidateIdentity identity)
        {
            for (var i = 0; i < Matches.Length; i++)
            {
                var match = Matches[i];
                if (match.m_Vehicle == identity.Vehicle && match.m_EntryLane == identity.EntryLane &&
                    match.m_NextLane == identity.NextLane && match.m_Target == identity.Target)
                    return true;
            }
            return false;
        }

        private uint GetFirstSeenFrame(CandidateIdentity identity)
        {
            for (var i = 0; i < Observations.Length; i++)
            {
                var observation = Observations[i];
                if (!observation.Identity.Equals(identity)) continue;
                observation.LastSeenFrame = DetectionFrame;
                Observations[i] = observation;
                return observation.FirstSeenFrame;
            }

            Observations.Add(new CandidateObservation
            {
                Identity = identity,
                FirstSeenFrame = DetectionFrame,
                LastSeenFrame = DetectionFrame
            });
            return DetectionFrame;
        }

        private void PruneObservations()
        {
            for (var i = Observations.Length - 1; i >= 0; i--)
            {
                if (DetectionFrame - Observations[i].LastSeenFrame > kObservationRetentionFrames)
                    Observations.RemoveAtSwapBack(i);
            }
        }

        private static bool ContainsEntity(NativeList<Entity> entities, Entity entity)
        {
            for (var i = 0; i < entities.Length; i++)
                if (entities[i] == entity) return true;
            return false;
        }

        private void AddDebugRejection(
            Entity physical,
            Entity vehicle,
            Entity entryLane,
            Entity nextLane,
            Entity target,
            RejectedCandidateReason reason)
        {
            RejectionReasons[(int)reason]++;
            if (DebugTarget == Entity.Null || target != DebugTarget) return;
            DebugRejections.Add(new RejectedCandidate
            {
                m_PhysicalVehicle = physical,
                m_Vehicle = vehicle,
                m_EntryLane = entryLane,
                m_NextLane = nextLane,
                m_ObservedCurrentLane = Entity.Null,
                m_ObservedNextLane = Entity.Null,
                m_Target = target,
                m_Reason = reason,
                m_DetectionFrame = DetectionFrame
            });
        }
    }

    private RestrictionIndexSystem m_Index = null!;
    private SimulationSystem m_SimulationSystem = null!;
    private NativeList<Entity> m_WatchedEntryLanes;
    private NativeParallelMultiHashMap<Entity, DirectedEntryGateRuntime> m_GatesByEntryLane;
    private NativeParallelMultiHashMap<Entity, Entity> m_TargetPrefabs;
    private NativeList<CandidateMatch> m_Matches;
    private NativeList<RejectedCandidate> m_DebugRejections;
    private NativeList<Entity> m_CanonicalSeen;
    private NativeList<CandidateObservation> m_Observations;
    private NativeArray<CandidateDiagnosticCounters> m_Counters;
    private NativeArray<int> m_RejectionReasons;
    private JobHandle m_ScanHandle;
    private JobHandle m_ReaderHandle;
    private bool m_ScanPending;
    private int m_RuntimeRevision = -1;
    private int m_ReportScans;
    private CandidateDiagnosticCounters m_ReportCounters;
    private readonly int[] m_ReportRejectionReasons = new int[(int)RejectedCandidateReason.Count];
    private uint m_ScanSequence;

    /// <summary>Optional development filter. Entity.Null disables per-candidate logging.</summary>
    public Entity DebugTarget { get; set; } = Entity.Null;

    public int RuntimeRevision => m_RuntimeRevision;
    public bool HasWatchedGates => m_WatchedEntryLanes.IsCreated && m_WatchedEntryLanes.Length != 0;
    public uint ScanSequence => m_ScanSequence;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_Index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
        m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
        m_WatchedEntryLanes = new NativeList<Entity>(64, Allocator.Persistent);
        m_GatesByEntryLane = new NativeParallelMultiHashMap<Entity, DirectedEntryGateRuntime>(128, Allocator.Persistent);
        m_TargetPrefabs = new NativeParallelMultiHashMap<Entity, Entity>(128, Allocator.Persistent);
        m_Matches = new NativeList<CandidateMatch>(64, Allocator.Persistent);
        m_DebugRejections = new NativeList<RejectedCandidate>(32, Allocator.Persistent);
        m_CanonicalSeen = new NativeList<Entity>(64, Allocator.Persistent);
        m_Observations = new NativeList<CandidateObservation>(128, Allocator.Persistent);
        m_Counters = new NativeArray<CandidateDiagnosticCounters>(1, Allocator.Persistent);
        m_RejectionReasons = new NativeArray<int>((int)RejectedCandidateReason.Count, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        JobHandle.CombineDependencies(m_ScanHandle, m_ReaderHandle).Complete();
        m_WatchedEntryLanes.Dispose();
        m_GatesByEntryLane.Dispose();
        m_TargetPrefabs.Dispose();
        m_Matches.Dispose();
        m_DebugRejections.Dispose();
        m_CanonicalSeen.Dispose();
        m_Observations.Dispose();
        m_Counters.Dispose();
        m_RejectionReasons.Dispose();
        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        if (!CompletePreviousScanWithoutStall()) return;

        if (m_RuntimeRevision != m_Index.Revision)
            RebuildRuntimeSnapshot();

        // Zero restrictions or zero unambiguous gates: do not schedule a scan job.
        if (m_WatchedEntryLanes.Length == 0) return;

        m_Matches.Clear();
        m_DebugRejections.Clear();
        m_CanonicalSeen.Clear();

        var job = new CollectCandidatesJob
        {
            WatchedEntryLanes = m_WatchedEntryLanes,
            GatesByEntryLane = m_GatesByEntryLane,
            TargetPrefabs = m_TargetPrefabs,
            EntityStorage = GetEntityStorageInfoLookup(),
            LaneObjects = GetBufferLookup<LaneObject>(true),
            NavigationLanes = GetBufferLookup<CarNavigationLane>(true),
            Layouts = GetBufferLookup<LayoutElement>(true),
            DeletedData = GetComponentLookup<Deleted>(true),
            ParkedCars = GetComponentLookup<ParkedCar>(true),
            Vehicles = GetComponentLookup<Vehicle>(true),
            Cars = GetComponentLookup<Car>(true),
            Trains = GetComponentLookup<Train>(true),
            Controllers = GetComponentLookup<Controller>(true),
            PrefabRefs = GetComponentLookup<PrefabRef>(true),
            CurrentLanes = GetComponentLookup<CarCurrentLane>(true),
            MovingData = GetComponentLookup<Moving>(true),
            Curves = GetComponentLookup<Curve>(true),
            MasterLanes = GetComponentLookup<MasterLane>(true),
            PrefabCarData = GetComponentLookup<CarData>(true),
            Matches = m_Matches,
            DebugRejections = m_DebugRejections,
            CanonicalSeen = m_CanonicalSeen,
            Observations = m_Observations,
            Counters = m_Counters,
            RejectionReasons = m_RejectionReasons,
            DebugTarget = DebugTarget,
            RestrictionRevision = m_RuntimeRevision,
            DetectionFrame = m_SimulationSystem.frameIndex
        };

        m_ScanHandle = job.Schedule(Dependency);
        m_ScanSequence++;
        m_ReaderHandle = default;
        m_ScanPending = true;
        Dependency = m_ScanHandle;
    }

    /// <summary>
    /// Phase 1C can schedule a read-only job against this array and dependency, then
    /// register its handle through AddCandidateReader before the detector reuses storage.
    /// </summary>
    public NativeArray<CandidateMatch> GetCandidateMatches(out JobHandle dependency)
    {
        dependency = m_ScanPending ? m_ScanHandle : default;
        return m_ScanPending ? m_Matches.AsDeferredJobArray() : m_Matches.AsArray();
    }

    public void AddCandidateReader(JobHandle reader)
        => m_ReaderHandle = JobHandle.CombineDependencies(m_ReaderHandle, reader);

    public void ResetRuntimeState()
    {
        JobHandle.CombineDependencies(m_ScanHandle, m_ReaderHandle).Complete();
        m_ScanPending = false;
        m_ScanHandle = default;
        m_ReaderHandle = default;
        m_WatchedEntryLanes.Clear();
        m_GatesByEntryLane.Clear();
        m_TargetPrefabs.Clear();
        m_Matches.Clear();
        m_DebugRejections.Clear();
        m_CanonicalSeen.Clear();
        m_Observations.Clear();
        for (var i = 0; i < m_Counters.Length; i++) m_Counters[i] = default;
        for (var i = 0; i < m_RejectionReasons.Length; i++) m_RejectionReasons[i] = 0;
        m_RuntimeRevision = -1;
        m_ReportScans = 0;
        m_ReportCounters = default;
        System.Array.Clear(m_ReportRejectionReasons, 0, m_ReportRejectionReasons.Length);
        m_ScanSequence = 0;
        DebugTarget = Entity.Null;
    }

    public CandidateDiagnosticCounters GetLastCounters()
    {
        if (m_ScanPending) return default;
        return m_Counters[0];
    }

    private bool CompletePreviousScanWithoutStall()
    {
        if (!m_ScanPending) return true;

        var combined = JobHandle.CombineDependencies(m_ScanHandle, m_ReaderHandle);
        if (!combined.IsCompleted) return false;

        combined.Complete();
        m_ScanPending = false;

        if (m_RuntimeRevision != m_Index.Revision)
        {
            var counters = m_Counters[0];
            counters.m_StaleRevisionRejected += m_Matches.Length;
            m_Counters[0] = counters;
            m_Matches.Clear();
        }

        ProcessCompletedDiagnostics();
        return true;
    }

    private void RebuildRuntimeSnapshot()
    {
        m_WatchedEntryLanes.Clear();
        m_GatesByEntryLane.Clear();
        m_TargetPrefabs.Clear();
        m_Observations.Clear();

        var uniqueLanes = new HashSet<Entity>();
        var gates = new List<DirectedEntryGateRuntime>();
        foreach (var pair in m_Index.TargetDirectedGates)
        {
            if (!m_Index.TryGetTargetType(pair.Key, out var targetType)) continue;
            foreach (var gate in pair.Value)
            {
                if (gate.m_EntryLane == Entity.Null || gate.m_NextLane == Entity.Null ||
                    gate.m_Target == Entity.Null)
                    continue;

                uniqueLanes.Add(gate.m_EntryLane);
                gates.Add(new DirectedEntryGateRuntime
                {
                    EntryLane = gate.m_EntryLane,
                    NextLane = gate.m_NextLane,
                    Target = gate.m_Target,
                    TargetType = targetType,
                    EntryDirection = gate.m_EntryDirection,
                    NextDirection = gate.m_NextDirection,
                    TargetEndpoint = gate.m_TargetEndpoint
                });
            }
        }

        if (m_WatchedEntryLanes.Capacity < uniqueLanes.Count)
            m_WatchedEntryLanes.Capacity = uniqueLanes.Count;
        foreach (var lane in uniqueLanes) m_WatchedEntryLanes.Add(lane);

        if (m_GatesByEntryLane.Capacity < math.max(1, gates.Count))
            m_GatesByEntryLane.Capacity = math.max(1, gates.Count);
        foreach (var gate in gates) m_GatesByEntryLane.Add(gate.EntryLane, gate);

        var prefabCount = 0;
        foreach (var pair in m_Index.TargetPrefabs) prefabCount += pair.Value.Count;
        if (m_TargetPrefabs.Capacity < math.max(1, prefabCount))
            m_TargetPrefabs.Capacity = math.max(1, prefabCount);
        foreach (var pair in m_Index.TargetPrefabs)
            foreach (var prefab in pair.Value)
                if (pair.Key != Entity.Null && prefab != Entity.Null)
                    m_TargetPrefabs.Add(pair.Key, prefab);

        m_RuntimeRevision = m_Index.Revision;
    }

    private void ProcessCompletedDiagnostics()
    {
        var counters = m_Counters[0];
        Accumulate(ref m_ReportCounters, counters);
        for (var i = 0; i < m_ReportRejectionReasons.Length; i++)
            m_ReportRejectionReasons[i] += m_RejectionReasons[i];
        m_ReportScans++;

        DumpDebugTargetMatches();
        if (m_ReportScans < 256) return;

        LogAggregateDiagnostics(m_ReportCounters);
        LogRejectionReasons(m_ReportRejectionReasons);
        m_ReportScans = 0;
        m_ReportCounters = default;
        System.Array.Clear(m_ReportRejectionReasons, 0, m_ReportRejectionReasons.Length);
    }

    [Conditional("DEBUG")]
    private void DumpDebugTargetMatches()
    {
        if (DebugTarget == Entity.Null) return;

        foreach (var match in m_Matches)
        {
            if (match.m_Target != DebugTarget) continue;
            Mod.Log.Info(
                $"[RouteFilter.Candidate] Vehicle={FormatEntity(match.m_Vehicle)} " +
                $"PhysicalEntity={FormatEntity(match.m_PhysicalVehicle)} " +
                $"Target={FormatEntity(match.m_Target)} EntryLane={FormatEntity(match.m_EntryLane)} " +
                $"NextLane={FormatEntity(match.m_NextLane)} MatchedPrefab={FormatEntity(match.m_MatchedPrefab)} " +
                $"RemainingDistanceApprox={match.m_DirectionAwareRemainingDistanceApprox:F2} " +
                $"Velocity={math.length(match.m_Velocity):F2} ChangeLane={FormatEntity(match.m_ChangeLane)} " +
                $"Revision={match.m_RestrictionRevision} FirstSeen={match.m_FirstSeenFrame}");
        }

        foreach (var rejection in m_DebugRejections)
            Mod.Log.Debug(
                $"[RouteFilter.CandidateRejected] Vehicle={FormatEntity(rejection.m_Vehicle)} " +
                $"Physical={FormatEntity(rejection.m_PhysicalVehicle)} Target={FormatEntity(rejection.m_Target)} " +
                $"Entry={FormatEntity(rejection.m_EntryLane)} Next={FormatEntity(rejection.m_NextLane)} " +
                $"ObservedCurrent={FormatEntity(rejection.m_ObservedCurrentLane)} " +
                $"ObservedNext={FormatEntity(rejection.m_ObservedNextLane)} " +
                $"RemainingDistanceApprox={rejection.m_DirectionAwareRemainingDistanceApprox:F2} " +
                $"Velocity={math.length(rejection.m_Velocity):F2} " +
                $"LaneObjectStale={rejection.m_LaneObjectAppearedStale} " +
                $"AtOrPastAnchor={rejection.m_AtOrPastEntryAnchor} Reason={rejection.m_Reason}");
    }

    [Conditional("DEBUG")]
    private static void LogAggregateDiagnostics(CandidateDiagnosticCounters value)
        => Mod.Log.Info(
            $"[RouteFilter.CandidateStats] WatchedGates={value.m_WatchedGates} " +
            $"WatchedEntryLanes={value.m_WatchedEntryLanes} LaneObjectsScanned={value.m_LaneObjectsScanned} " +
            $"PhysicalEntitiesSeen={value.m_PhysicalEntitiesSeen} CanonicalVehicles={value.m_CanonicalVehicles} " +
            $"DuplicatesRemoved={value.m_DuplicatesRemoved} PrefabRejected={value.m_PrefabRejected} " +
            $"NavigationRejected={value.m_NavigationRejected} AmbiguousRejected={value.m_AmbiguousRejected} " +
            $"CandidatesMatched={value.m_CandidatesMatched} DeletedRejected={value.m_DeletedRejected} " +
            $"StaleLaneObjectRejected={value.m_StaleLaneObjectRejected} " +
            $"StaleRevisionRejected={value.m_StaleRevisionRejected}");

    [Conditional("DEBUG")]
    private static void LogRejectionReasons(int[] values)
    {
        var text = string.Empty;
        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] == 0) continue;
            if (text.Length != 0) text += ";";
            text += $"{(RejectedCandidateReason)i}={values[i]}";
        }
        Mod.Log.Info($"[RouteFilter.CandidateRejections] {text}");
    }

    private static void Accumulate(
        ref CandidateDiagnosticCounters total,
        CandidateDiagnosticCounters value)
    {
        total.m_WatchedGates = value.m_WatchedGates;
        total.m_WatchedEntryLanes = value.m_WatchedEntryLanes;
        total.m_LaneObjectsScanned += value.m_LaneObjectsScanned;
        total.m_PhysicalEntitiesSeen += value.m_PhysicalEntitiesSeen;
        total.m_CanonicalVehicles += value.m_CanonicalVehicles;
        total.m_DuplicatesRemoved += value.m_DuplicatesRemoved;
        total.m_PrefabRejected += value.m_PrefabRejected;
        total.m_NavigationRejected += value.m_NavigationRejected;
        total.m_AmbiguousRejected += value.m_AmbiguousRejected;
        total.m_CandidatesMatched += value.m_CandidatesMatched;
        total.m_DeletedRejected += value.m_DeletedRejected;
        total.m_StaleLaneObjectRejected += value.m_StaleLaneObjectRejected;
        total.m_StaleRevisionRejected += value.m_StaleRevisionRejected;
    }

    private static string FormatEntity(Entity entity)
        => entity == Entity.Null ? "Null" : $"{entity.Index}:{entity.Version}";
}
