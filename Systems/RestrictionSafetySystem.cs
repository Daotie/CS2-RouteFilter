using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using RouteFilter.Components;
using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using VehicleLaneFlags = Game.Vehicles.CarLaneFlags;
using VehicleAmbulance = Game.Vehicles.Ambulance;
using VehicleCargoTransport = Game.Vehicles.CargoTransport;
using VehicleDeliveryTruck = Game.Vehicles.DeliveryTruck;
using VehicleFireEngine = Game.Vehicles.FireEngine;
using VehicleGarbageTruck = Game.Vehicles.GarbageTruck;
using VehicleHearse = Game.Vehicles.Hearse;
using VehicleMaintenance = Game.Vehicles.MaintenanceVehicle;
using VehiclePersonalCar = Game.Vehicles.PersonalCar;
using VehiclePoliceCar = Game.Vehicles.PoliceCar;
using VehiclePostVan = Game.Vehicles.PostVan;
using VehiclePublicTransport = Game.Vehicles.PublicTransport;
using VehicleTaxi = Game.Vehicles.Taxi;
using VehicleWork = Game.Vehicles.WorkVehicle;

namespace RouteFilter.Systems;

/// <summary>
/// Phase 1C read-only safety analysis and latency instrumentation. This system emits
/// Safe/Unsafe/Unknown records but never mutates vehicles, paths, lanes, or restrictions.
/// </summary>
public sealed partial class RestrictionSafetySystem : GameSystemBase
{
    public enum LatencyMetric : byte
    {
        CandidatePipeline = 0,
        ObservedPositionChangeGap = 1,
        NaturalPendingRise = 2,
        NaturalPendingDuration = 3,
        GraphPublication = 4,
        Count = 5
    }

    private struct SafetyObservation
    {
        public uint FirstSeenFrame;
        public uint LastCandidateFrame;
        public uint LastPositionFrame;
        public uint PendingStartFrame;
        public float LastCurveAnchor;
        public PathFlags LastPathState;
        public bool HasPosition;
        public bool HasPendingStart;
    }

    [BurstCompile]
    private struct EvaluateCandidatesJob : IJob
    {
        private const uint kObservationRetentionFrames = 4096;
        private const int kHistogramBins = 512;

        [ReadOnly] public NativeArray<CandidateMatch> Candidates;
        [ReadOnly] public EntityStorageInfoLookup EntityStorage;
        [ReadOnly] public ComponentLookup<Deleted> DeletedData;
        [ReadOnly] public ComponentLookup<Temp> TempData;
        [ReadOnly] public ComponentLookup<Controller> Controllers;
        [ReadOnly] public ComponentLookup<PathOwner> PathOwners;
        [ReadOnly] public ComponentLookup<CarCurrentLane> CurrentLanes;
        [ReadOnly] public BufferLookup<CarNavigationLane> NavigationLanes;
        [ReadOnly] public ComponentLookup<Moving> MovingData;
        [ReadOnly] public ComponentLookup<Curve> Curves;
        [ReadOnly] public ComponentLookup<PrefabRef> PrefabRefs;
        [ReadOnly] public BufferLookup<LayoutElement> Layouts;
        [ReadOnly] public ComponentLookup<CarData> PrefabCarData;
        [ReadOnly] public ComponentLookup<ObjectGeometryData> PrefabGeometryData;
        [ReadOnly] public ComponentLookup<VehiclePersonalCar> PersonalCars;
        [ReadOnly] public ComponentLookup<VehicleTaxi> Taxis;
        [ReadOnly] public ComponentLookup<VehiclePublicTransport> PublicTransports;
        [ReadOnly] public ComponentLookup<VehicleCargoTransport> CargoTransports;
        [ReadOnly] public ComponentLookup<VehicleDeliveryTruck> DeliveryTrucks;
        [ReadOnly] public ComponentLookup<GoodsDeliveryVehicle> GoodsDeliveryVehicles;
        [ReadOnly] public ComponentLookup<VehiclePoliceCar> PoliceCars;
        [ReadOnly] public ComponentLookup<VehicleAmbulance> Ambulances;
        [ReadOnly] public ComponentLookup<VehicleFireEngine> FireEngines;
        [ReadOnly] public ComponentLookup<VehicleMaintenance> MaintenanceVehicles;
        [ReadOnly] public ComponentLookup<VehicleGarbageTruck> GarbageTrucks;
        [ReadOnly] public ComponentLookup<VehicleHearse> Hearses;
        [ReadOnly] public ComponentLookup<VehiclePostVan> PostVans;
        [ReadOnly] public ComponentLookup<VehicleWork> WorkVehicles;

        public NativeList<RerouteSafetyEvaluation> Evaluations;
        public NativeParallelHashMap<CanonicalApproachKey, SafetyObservation> Observations;
        public NativeList<CanonicalApproachKey> ObservationOrder;
        public NativeArray<uint> Histograms;
        public NativeArray<uint> HistogramMaxima;
        public NativeArray<SafetyDiagnosticCounters> Counters;
        public NativeArray<int> CategoryVerdicts;

        public int RestrictionRevision;
        public uint EvaluationFrame;
        public float SelectedSimulationSpeed;
        public float SmoothSimulationSpeed;
        /// <summary>Budget for vanilla to enqueue, run and apply one car pathfind, plus reaction.</summary>
        public float ExpectedLatencySeconds;
        public float UncertaintyMargin;

        public void Execute()
        {
            var counters = default(SafetyDiagnosticCounters);

            for (var i = 0; i < Candidates.Length; i++)
            {
                var candidate = Candidates[i];
                var vehicleValid = IsValid(candidate.m_Vehicle);
                var targetValid = IsValid(candidate.m_Target);
                var controllerValid = vehicleValid && ResolveCanonical(candidate.m_PhysicalVehicle) == candidate.m_Vehicle;
                var current = default(CarCurrentLane);
                var hasCurrent = vehicleValid && CurrentLanes.TryGetComponent(candidate.m_Vehicle, out current);
                var currentMatches = hasCurrent && current.m_Lane == candidate.m_EntryLane;
                var laneChangeUnambiguous = hasCurrent && current.m_ChangeLane == Entity.Null && current.m_ChangeProgress == 0f;

                var transitionSupported = hasCurrent &&
                    (current.m_LaneFlags & (VehicleLaneFlags.TransformTarget |
                                            VehicleLaneFlags.ParkingSpace |
                                            VehicleLaneFlags.EnteringRoad |
                                            VehicleLaneFlags.Obsolete |
                                            VehicleLaneFlags.Area)) == 0;

                var navigation = default(DynamicBuffer<CarNavigationLane>);
                var hasNavigation = vehicleValid && NavigationLanes.TryGetBuffer(candidate.m_Vehicle, out navigation) &&
                                    navigation.Length != 0;
                var nextIndex = candidate.m_ViaLane == Entity.Null ? 0 : 1;
                var next = hasNavigation && navigation.Length > nextIndex ? navigation[nextIndex] : default;
                var immediateMatches = hasNavigation && navigation.Length > nextIndex && next.m_Lane == candidate.m_NextLane &&
                    (nextIndex == 0 || navigation[0].m_Lane == candidate.m_ViaLane);
                var entryDelta = hasCurrent ? current.m_CurvePosition.z - current.m_CurvePosition.x : 0f;
                var nextDelta = hasNavigation ? next.m_CurvePosition.y - next.m_CurvePosition.x : 0f;
                var entryDirectionMatches = hasCurrent && DirectionMatches(candidate.m_EntryDirection, entryDelta);
                var nextDirectionMatches = hasNavigation && nextDelta != 0f &&
                                           DirectionMatches(candidate.m_NextDirection, nextDelta);
                var atOrPastAnchor = hasCurrent &&
                    (candidate.m_EntryDirection == LaneTraversalDirection.Forward
                        ? current.m_CurvePosition.x >= current.m_CurvePosition.z
                        : current.m_CurvePosition.x <= current.m_CurvePosition.z);

                var curve = default(Curve);
                var hasCurve = EntityStorage.Exists(candidate.m_EntryLane) &&
                               Curves.TryGetComponent(candidate.m_EntryLane, out curve);
                var curveLength = hasCurve ? curve.m_Length : 0f;
                var gateDistance = hasCurrent && hasCurve
                    ? GateApproachDistance.Remaining(curve, current.m_CurvePosition.x, current.m_CurvePosition.z)
                    : float.NaN;
                var speed = vehicleValid && MovingData.TryGetComponent(candidate.m_Vehicle, out var moving)
                    ? math.length(moving.m_Velocity)
                    : 0f;

                GetVehicleMeasurements(candidate.m_Vehicle, candidate.m_PhysicalVehicle,
                    out var braking, out var geometryLength);
                var pathOwner = default(PathOwner);
                var hasPathOwner = vehicleValid && PathOwners.TryGetComponent(candidate.m_Vehicle, out pathOwner);
                var pathState = hasPathOwner ? pathOwner.m_State : 0;
                var category = Classify(candidate.m_Vehicle);

                // CurrentLane.x = present position, .z = traversal endpoint (vanilla navigation
                // initializes it from next.xy as xxy). Bootstrap is explicit, not measured calibration.
                var calibration = new RerouteSafetyCalibration
                {
                    m_Confidence = SafetyConfidence.ConservativeInitial,
                    m_HasLastSafeDecisionPoint = hasCurrent && hasCurve,
                    m_DistanceToLastSafeDecisionPoint = gateDistance,
                    m_ExpectedLatencySeconds = ExpectedLatencySeconds,
                    m_VehicleGeometryMargin = geometryLength,
                    m_LaneChangeMargin = 0f,
                    m_UncertaintyMargin = UncertaintyMargin
                };

                var evaluation = SafeToAttemptRerouteEvaluator.Evaluate(new RerouteSafetyInput
                {
                    m_Candidate = candidate,
                    m_EvaluationFrame = EvaluationFrame,
                    m_CurrentRestrictionRevision = RestrictionRevision,
                    m_VehicleValid = vehicleValid,
                    m_TargetValid = targetValid,
                    m_CanonicalControllerValid = controllerValid,
                    m_CurrentLaneMatches = currentMatches,
                    m_ImmediateLaneMatches = immediateMatches,
                    m_LaneChangeUnambiguous = laneChangeUnambiguous,
                    m_TransitionSupported = transitionSupported,
                    m_EntryDirectionMatches = entryDirectionMatches,
                    m_NextDirectionMatches = nextDirectionMatches,
                    m_AtOrPastGateAnchor = atOrPastAnchor,
                    m_HasCurve = hasCurve,
                    m_HasPathOwner = hasPathOwner,
                    m_PathState = pathState,
                    m_Category = category,
                    m_SelectedSimulationSpeed = SelectedSimulationSpeed,
                    m_SmoothSimulationSpeed = SmoothSimulationSpeed,
                    m_Speed = speed,
                    m_Braking = braking,
                    m_CurveLength = curveLength,
                    m_DistanceToGateAnchor = gateDistance,
                    m_VehicleGeometryLength = geometryLength,
                    m_Calibration = calibration
                });

                Evaluations.Add(evaluation);
                UpdateObservation(candidate, hasCurrent ? current.m_CurvePosition.x : float.NaN, pathState);
                Record(LatencyMetric.CandidatePipeline, evaluation.m_CandidatePipelineFrames);
                CountEvaluation(evaluation, ref counters);
            }

            if (EvaluationFrame % 64 == 0) PruneObservations();
            Counters[0] = counters;
        }

        private void UpdateObservation(in CandidateMatch candidate, float curveAnchor, PathFlags pathState)
        {
            var key = new CanonicalApproachKey { Vehicle = candidate.m_Vehicle, Target = candidate.m_Target };
            if (Observations.TryGetValue(key, out var observation))
            {
                // Pending transitions are observed only for vehicles that are currently candidates.
                // The previous implementation walked every retained observation every frame, which
                // made cost grow with the number of vehicles that had ever passed the gate rather
                // than with the number currently approaching it.
                var pending = (pathState & PathFlags.Pending) != 0;
                var wasPending = (observation.LastPathState & PathFlags.Pending) != 0;
                if (pending && !wasPending)
                {
                    observation.PendingStartFrame = EvaluationFrame;
                    observation.HasPendingStart = true;
                    Record(LatencyMetric.NaturalPendingRise, EvaluationFrame - observation.FirstSeenFrame);
                }
                else if (!pending && wasPending && observation.HasPendingStart)
                {
                    Record(LatencyMetric.NaturalPendingDuration, EvaluationFrame - observation.PendingStartFrame);
                    observation.HasPendingStart = false;
                }

                if (observation.HasPosition && math.isfinite(curveAnchor) &&
                    math.abs(curveAnchor - observation.LastCurveAnchor) > 1e-6f)
                {
                    Record(LatencyMetric.ObservedPositionChangeGap, EvaluationFrame - observation.LastPositionFrame);
                    observation.LastPositionFrame = EvaluationFrame;
                    observation.LastCurveAnchor = curveAnchor;
                }
                else if (!observation.HasPosition && math.isfinite(curveAnchor))
                {
                    observation.HasPosition = true;
                    observation.LastPositionFrame = EvaluationFrame;
                    observation.LastCurveAnchor = curveAnchor;
                }

                observation.LastCandidateFrame = EvaluationFrame;
                observation.LastPathState = pathState;
                Observations[key] = observation;
                return;
            }

            if (Observations.Count() >= 8192) return;
            ObservationOrder.Add(key);
            Observations.Add(key, new SafetyObservation
            {
                FirstSeenFrame = candidate.m_FirstSeenFrame,
                LastCandidateFrame = EvaluationFrame,
                LastPositionFrame = EvaluationFrame,
                LastCurveAnchor = curveAnchor,
                LastPathState = pathState,
                HasPosition = math.isfinite(curveAnchor),
                HasPendingStart = (pathState & PathFlags.Pending) != 0,
                PendingStartFrame = EvaluationFrame
            });
        }

        /// <summary>
        /// Amortised sweep. Observations are removed once they stop appearing as candidates, which
        /// bounds the map by concurrent approaches rather than by session length.
        /// </summary>
        private void PruneObservations()
        {
            for (var i = ObservationOrder.Length - 1; i >= 0; i--)
            {
                var key = ObservationOrder[i];
                if (!Observations.TryGetValue(key, out var observation)) continue;
                if (IsValid(key.Vehicle) && EvaluationFrame - observation.LastCandidateFrame <= kObservationRetentionFrames)
                    continue;
                Observations.Remove(key);
                ObservationOrder.RemoveAtSwapBack(i);
            }
        }

        private void CountEvaluation(RerouteSafetyEvaluation evaluation, ref SafetyDiagnosticCounters counters)
        {
            counters.m_Evaluated++;
            var verdictIndex = (int)evaluation.m_Verdict;
            CategoryVerdicts[(int)evaluation.m_Category * 3 + verdictIndex]++;

            if (evaluation.m_Verdict == RerouteSafetyVerdict.Safe) counters.m_Safe++;
            else if (evaluation.m_Verdict == RerouteSafetyVerdict.Unsafe) counters.m_Unsafe++;
            else counters.m_Unknown++;

            const SafetyReason pathReasons = SafetyReason.PathPending | SafetyReason.PathScheduled |
                SafetyReason.PathFailed | SafetyReason.PathStuck | SafetyReason.PathObsolete |
                SafetyReason.PathAppend | SafetyReason.PathDivert | SafetyReason.PathDivertObsolete |
                SafetyReason.PathCachedObsolete;
            const SafetyReason contextReasons = SafetyReason.CanonicalControllerChanged |
                SafetyReason.CurrentLaneChanged | SafetyReason.ImmediateLaneChanged |
                SafetyReason.LaneChangeAmbiguous | SafetyReason.UnsupportedTransition |
                SafetyReason.EntryDirectionChanged | SafetyReason.NextDirectionChanged;

            if ((evaluation.m_Reasons & pathReasons) != 0) counters.m_PathStateRejected++;
            if ((evaluation.m_Reasons & contextReasons) != 0) counters.m_ContextRejected++;
            if ((evaluation.m_Reasons & SafetyReason.AtOrPastGateAnchor) != 0) counters.m_LatePass++;
            if (evaluation.m_Verdict == RerouteSafetyVerdict.Unknown) counters.m_AmbiguousPass++;
            if ((evaluation.m_Reasons & (SafetyReason.LatencyUncalibrated |
                                         SafetyReason.LastSafeDecisionPointUnknown)) != 0)
                counters.m_Uncalibrated++;
        }

        private Entity ResolveCanonical(Entity physical)
        {
            if (!IsValid(physical)) return Entity.Null;
            var entity = physical;
            var previous0 = Entity.Null;
            var previous1 = Entity.Null;
            var previous2 = Entity.Null;
            var previous3 = Entity.Null;
            for (var depth = 0; depth < 4; depth++)
            {
                if (!Controllers.TryGetComponent(entity, out var controller) ||
                    controller.m_Controller == Entity.Null || controller.m_Controller == entity)
                    return entity;
                var next = controller.m_Controller;
                if (next == physical || next == previous0 || next == previous1 ||
                    next == previous2 || next == previous3 || !IsValid(next))
                    return Entity.Null;
                previous3 = previous2;
                previous2 = previous1;
                previous1 = previous0;
                previous0 = entity;
                entity = next;
            }
            if (Controllers.TryGetComponent(entity, out var remaining) &&
                remaining.m_Controller != Entity.Null && remaining.m_Controller != entity)
                return Entity.Null;
            return entity;
        }

        private bool IsValid(Entity entity)
            => entity != Entity.Null && EntityStorage.Exists(entity) &&
               !DeletedData.HasComponent(entity) && !TempData.HasComponent(entity);

        private static bool DirectionMatches(LaneTraversalDirection direction, float delta)
            => direction == LaneTraversalDirection.Forward ? delta >= 0f : delta <= 0f;

        private RoadVehicleCategory Classify(Entity vehicle)
        {
            if (PoliceCars.HasComponent(vehicle) || Ambulances.HasComponent(vehicle) || FireEngines.HasComponent(vehicle))
                return RoadVehicleCategory.Emergency;
            if (Taxis.HasComponent(vehicle)) return RoadVehicleCategory.Taxi;
            if (PublicTransports.HasComponent(vehicle)) return RoadVehicleCategory.PublicTransport;
            if (CargoTransports.HasComponent(vehicle)) return RoadVehicleCategory.Cargo;
            if (DeliveryTrucks.HasComponent(vehicle) || GoodsDeliveryVehicles.HasComponent(vehicle))
                return RoadVehicleCategory.Delivery;
            if (MaintenanceVehicles.HasComponent(vehicle) || GarbageTrucks.HasComponent(vehicle) ||
                Hearses.HasComponent(vehicle) || PostVans.HasComponent(vehicle))
                return RoadVehicleCategory.MunicipalService;
            if (WorkVehicles.HasComponent(vehicle)) return RoadVehicleCategory.WorkVehicle;
            if (PersonalCars.TryGetComponent(vehicle, out var personal))
                return (personal.m_State & PersonalCarFlags.DummyTraffic) != 0
                    ? RoadVehicleCategory.DummyTraffic
                    : RoadVehicleCategory.Personal;
            return RoadVehicleCategory.Other;
        }

        private void GetVehicleMeasurements(Entity vehicle, Entity physical, out float braking, out float geometryLength)
        {
            braking = float.NaN;
            geometryLength = 0f;
            AddPrefabMeasurements(vehicle, ref braking, ref geometryLength);
            if (physical != vehicle) AddPrefabMeasurements(physical, ref braking, ref geometryLength);

            if (Layouts.TryGetBuffer(vehicle, out var layout))
            {
                var consistLength = 0f;
                for (var i = 0; i < layout.Length; i++)
                    consistLength += AddPrefabMeasurements(layout[i].m_Vehicle, ref braking, ref geometryLength);
                geometryLength = math.max(geometryLength, consistLength);
            }
        }

        private float AddPrefabMeasurements(Entity entity, ref float braking, ref float geometryLength)
        {
            if (entity == Entity.Null || !PrefabRefs.TryGetComponent(entity, out var prefabRef)) return 0f;
            if (PrefabCarData.TryGetComponent(prefabRef.m_Prefab, out var carData))
                braking = math.select(math.min(braking, carData.m_Braking), carData.m_Braking, math.isnan(braking));
            if (!PrefabGeometryData.TryGetComponent(prefabRef.m_Prefab, out var geometry)) return 0f;
            var length = math.max(0f, geometry.m_Bounds.max.z - geometry.m_Bounds.min.z);
            geometryLength = math.max(geometryLength, length);
            return length;
        }

        private void Record(LatencyMetric metric, uint frames)
        {
            var metricIndex = (int)metric;
            var bin = (int)math.min(frames, kHistogramBins - 1u);
            Histograms[metricIndex * kHistogramBins + bin]++;
            HistogramMaxima[metricIndex] = math.max(HistogramMaxima[metricIndex], frames);
        }
    }

    private const int kHistogramBins = 512;
    private const int kVehicleCategoryCount = 10;
    private const int kVerdictCount = 3;
    private const int kSafetyReasonBitCount = 29;

    private sealed class ReportDistribution
    {
        private readonly uint[] m_Bins = new uint[kHistogramBins];
        private readonly float m_BinSize;
        private double m_Sum;

        public uint Count { get; private set; }
        public float Min { get; private set; } = float.PositiveInfinity;
        public float Max { get; private set; }

        public ReportDistribution(float binSize) => m_BinSize = binSize;

        public void Add(float value)
        {
            if (!math.isfinite(value) || value < 0f) return;
            var bin = math.min((int)(value / m_BinSize), kHistogramBins - 1);
            m_Bins[bin]++;
            Count++;
            m_Sum += value;
            Min = math.min(Min, value);
            Max = math.max(Max, value);
        }

        public float Percentile(uint percentile)
        {
            if (Count == 0) return float.NaN;
            var threshold = (Count * percentile + 99u) / 100u;
            uint cumulative = 0;
            for (var i = 0; i < m_Bins.Length; i++)
            {
                cumulative += m_Bins[i];
                if (cumulative >= threshold) return i * m_BinSize;
            }
            return (kHistogramBins - 1) * m_BinSize;
        }

        public float Mean => Count == 0 ? float.NaN : (float)(m_Sum / Count);

        public void Reset()
        {
            Array.Clear(m_Bins, 0, m_Bins.Length);
            Count = 0;
            m_Sum = 0d;
            Min = float.PositiveInfinity;
            Max = 0f;
        }
    }

    private RestrictionCandidateSystem m_CandidateSystem = null!;
    private RestrictionIndexSystem m_Index = null!;
    private SimulationSystem m_SimulationSystem = null!;
    private NativeList<RerouteSafetyEvaluation> m_Evaluations;
    private NativeParallelHashMap<CanonicalApproachKey, SafetyObservation> m_Observations;
    private NativeList<CanonicalApproachKey> m_ObservationOrder;
    private NativeArray<uint> m_Histograms;
    private NativeArray<uint> m_HistogramMaxima;
    private NativeArray<SafetyDiagnosticCounters> m_Counters;
    private NativeArray<int> m_CategoryVerdicts;
    private JobHandle m_EvaluationHandle;
    private JobHandle m_ReaderHandle;
    private bool m_EvaluationPending;
    private int m_ReportScans;
    private SafetyDiagnosticCounters m_ReportCounters;
    private readonly int[] m_ReportCategoryVerdicts = new int[kVehicleCategoryCount * kVerdictCount];
    private readonly int[] m_LastCategoryVerdicts = new int[kVehicleCategoryCount * kVerdictCount];
    private readonly LatencyPercentiles[] m_LastPercentiles = new LatencyPercentiles[(int)LatencyMetric.Count];
    private readonly int[] m_ReportReasons = new int[kSafetyReasonBitCount];
    private readonly ReportDistribution m_ReportRemainingDistance = new(1f);
    private readonly ReportDistribution m_ReportBrakingDistance = new(1f);
    private readonly ReportDistribution m_ReportFirstSeenFrames = new(1f);
    private uint m_LastConsumedScanSequence;
    private int m_DebugMissScans;

    /// <summary>Managed mirror of the last completed per-frame evaluation count.</summary>
    private int m_LastEvaluationCount;

    /// <summary>
    /// Evaluations produced by the most recent completed job, or 0 while one is still in flight.
    /// Read without synchronising: callers use this only as a cheap "is there anything to do" test.
    /// </summary>
    public int EvaluationCount => m_EvaluationPending ? 0 : m_Evaluations.Length;

    /// <summary>Conservative reroute latency budget in seconds. See PERFORMANCE.md.</summary>
    // 16-frame AI cadence + 64-frame vanilla setup queue + another 64-frame query/adoption
    // allowance, at the 60 Hz simulation basis used by navigation's 16/60 time step.
    // This is an initial test budget, not an observed upper bound on native pathfinding.
    public float ExpectedLatencySeconds => math.clamp(Mod.Settings?.RerouteLatencySeconds ?? 2.4f, 2.4f, 4f);

    /// <summary>Extra distance margin, in metres, on top of the modelled braking distance.</summary>
    public float UncertaintyMargin => Mod.Settings?.RerouteUncertaintyMetres ?? 5f;

    public Entity DebugVehicle { get; set; } = Entity.Null;
    public Entity DebugTarget { get; set; } = Entity.Null;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_CandidateSystem = World.GetOrCreateSystemManaged<RestrictionCandidateSystem>();
        m_Index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
        m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
        m_Evaluations = new NativeList<RerouteSafetyEvaluation>(4096, Allocator.Persistent);
        m_Observations = new NativeParallelHashMap<CanonicalApproachKey, SafetyObservation>(8192, Allocator.Persistent);
        m_ObservationOrder = new NativeList<CanonicalApproachKey>(8192, Allocator.Persistent);
        m_Histograms = new NativeArray<uint>((int)LatencyMetric.Count * kHistogramBins, Allocator.Persistent);
        m_HistogramMaxima = new NativeArray<uint>((int)LatencyMetric.Count, Allocator.Persistent);
        m_Counters = new NativeArray<SafetyDiagnosticCounters>(1, Allocator.Persistent);
        m_CategoryVerdicts = new NativeArray<int>(10 * 3, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        JobHandle.CombineDependencies(m_EvaluationHandle, m_ReaderHandle).Complete();
        m_Evaluations.Dispose();
        m_Observations.Dispose();
        m_ObservationOrder.Dispose();
        m_Histograms.Dispose();
        m_HistogramMaxima.Dispose();
        m_Counters.Dispose();
        m_CategoryVerdicts.Dispose();
        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        if (!CompletePreviousEvaluationWithoutStall()) return;
        if (!m_CandidateSystem.HasWatchedGates) { m_Evaluations.Clear(); return; }
        if (m_CandidateSystem.ScanSequence == m_LastConsumedScanSequence) return;

        m_Evaluations.Clear();
        var candidates = m_CandidateSystem.GetCandidateMatches(out var candidateDependency);
        var job = new EvaluateCandidatesJob
        {
            Candidates = candidates,
            EntityStorage = GetEntityStorageInfoLookup(),
            DeletedData = GetComponentLookup<Deleted>(true),
            TempData = GetComponentLookup<Temp>(true),
            Controllers = GetComponentLookup<Controller>(true),
            PathOwners = GetComponentLookup<PathOwner>(true),
            CurrentLanes = GetComponentLookup<CarCurrentLane>(true),
            NavigationLanes = GetBufferLookup<CarNavigationLane>(true),
            MovingData = GetComponentLookup<Moving>(true),
            Curves = GetComponentLookup<Curve>(true),
            PrefabRefs = GetComponentLookup<PrefabRef>(true),
            Layouts = GetBufferLookup<LayoutElement>(true),
            PrefabCarData = GetComponentLookup<CarData>(true),
            PrefabGeometryData = GetComponentLookup<ObjectGeometryData>(true),
            PersonalCars = GetComponentLookup<VehiclePersonalCar>(true),
            Taxis = GetComponentLookup<VehicleTaxi>(true),
            PublicTransports = GetComponentLookup<VehiclePublicTransport>(true),
            CargoTransports = GetComponentLookup<VehicleCargoTransport>(true),
            DeliveryTrucks = GetComponentLookup<VehicleDeliveryTruck>(true),
            GoodsDeliveryVehicles = GetComponentLookup<GoodsDeliveryVehicle>(true),
            PoliceCars = GetComponentLookup<VehiclePoliceCar>(true),
            Ambulances = GetComponentLookup<VehicleAmbulance>(true),
            FireEngines = GetComponentLookup<VehicleFireEngine>(true),
            MaintenanceVehicles = GetComponentLookup<VehicleMaintenance>(true),
            GarbageTrucks = GetComponentLookup<VehicleGarbageTruck>(true),
            Hearses = GetComponentLookup<VehicleHearse>(true),
            PostVans = GetComponentLookup<VehiclePostVan>(true),
            WorkVehicles = GetComponentLookup<VehicleWork>(true),
            Evaluations = m_Evaluations,
            Observations = m_Observations,
            ObservationOrder = m_ObservationOrder,
            Histograms = m_Histograms,
            HistogramMaxima = m_HistogramMaxima,
            Counters = m_Counters,
            CategoryVerdicts = m_CategoryVerdicts,
            RestrictionRevision = m_Index.Revision,
            EvaluationFrame = m_SimulationSystem.frameIndex,
            SelectedSimulationSpeed = m_SimulationSystem.selectedSpeed,
            SmoothSimulationSpeed = m_SimulationSystem.smoothSpeed,
            ExpectedLatencySeconds = ExpectedLatencySeconds,
            UncertaintyMargin = UncertaintyMargin
        };

        m_EvaluationHandle = job.Schedule(JobHandle.CombineDependencies(Dependency, candidateDependency));
        m_LastConsumedScanSequence = m_CandidateSystem.ScanSequence;
        m_ReaderHandle = default;
        m_EvaluationPending = true;
        m_CandidateSystem.AddCandidateReader(m_EvaluationHandle);
        Dependency = m_EvaluationHandle;
    }

    public NativeArray<RerouteSafetyEvaluation> GetEvaluations(out JobHandle dependency)
    {
        dependency = m_EvaluationPending ? m_EvaluationHandle : default;
        return m_EvaluationPending ? m_Evaluations.AsDeferredJobArray() : m_Evaluations.AsArray();
    }

    public void AddEvaluationReader(JobHandle reader)
        => m_ReaderHandle = JobHandle.CombineDependencies(m_ReaderHandle, reader);

    public void ResetRuntimeState()
    {
        JobHandle.CombineDependencies(m_EvaluationHandle, m_ReaderHandle).Complete();
        m_EvaluationPending = false;
        m_EvaluationHandle = default;
        m_ReaderHandle = default;
        m_Evaluations.Clear();
        m_Observations.Clear();
        m_ObservationOrder.Clear();
        for (var i = 0; i < m_Histograms.Length; i++) m_Histograms[i] = 0;
        for (var i = 0; i < m_HistogramMaxima.Length; i++) m_HistogramMaxima[i] = 0;
        for (var i = 0; i < m_Counters.Length; i++) m_Counters[i] = default;
        for (var i = 0; i < m_CategoryVerdicts.Length; i++) m_CategoryVerdicts[i] = 0;
        m_ReportScans = 0;
        m_ReportCounters = default;
        System.Array.Clear(m_ReportCategoryVerdicts, 0, m_ReportCategoryVerdicts.Length);
        System.Array.Clear(m_ReportReasons, 0, m_ReportReasons.Length);
        m_ReportRemainingDistance.Reset();
        m_ReportBrakingDistance.Reset();
        m_ReportFirstSeenFrames.Reset();
        m_LastConsumedScanSequence = 0;
        m_DebugMissScans = 0;
        DebugVehicle = Entity.Null;
        DebugTarget = Entity.Null;
    }

    public SafetyDiagnosticCounters GetLastCounters()
        => m_EvaluationPending ? default : m_Counters[0];

    /// <summary>
    /// Reads the last completed per-frame numbers. No JobHandle.Complete: the values are mirrored
    /// from the native counters while the job was already complete, so a UI refresh can never
    /// stall the simulation thread.
    /// </summary>
    public void GetCategoryVerdicts(
        RoadVehicleCategory category,
        out int safe,
        out int unsafeCount,
        out int unknown)
    {
        if (m_EvaluationPending)
        {
            safe = m_LastCategoryVerdicts[(int)category * 3 + (int)RerouteSafetyVerdict.Safe];
            unsafeCount = m_LastCategoryVerdicts[(int)category * 3 + (int)RerouteSafetyVerdict.Unsafe];
            unknown = m_LastCategoryVerdicts[(int)category * 3 + (int)RerouteSafetyVerdict.Unknown];
            return;
        }
        var offset = (int)category * 3;
        safe = m_CategoryVerdicts[offset + (int)RerouteSafetyVerdict.Safe];
        unsafeCount = m_CategoryVerdicts[offset + (int)RerouteSafetyVerdict.Unsafe];
        unknown = m_CategoryVerdicts[offset + (int)RerouteSafetyVerdict.Unknown];
    }

    public LatencyPercentiles GetLatencyPercentiles(LatencyMetric metric)
    {
        if (m_EvaluationPending) return m_LastPercentiles[(int)metric];
        var offset = (int)metric * kHistogramBins;
        uint count = 0;
        for (var i = 0; i < kHistogramBins; i++) count += m_Histograms[offset + i];
        return new LatencyPercentiles
        {
            m_Count = count,
            m_MedianFrames = Percentile(offset, count, 50),
            m_P95Frames = Percentile(offset, count, 95),
            m_P99Frames = Percentile(offset, count, 99),
            m_MaxObservedFrames = m_HistogramMaxima[(int)metric]
        };
    }

    private uint Percentile(int offset, uint count, uint percentile)
    {
        if (count == 0) return 0;
        var threshold = (count * percentile + 99u) / 100u;
        uint cumulative = 0;
        for (var i = 0; i < kHistogramBins; i++)
        {
            cumulative += m_Histograms[offset + i];
            if (cumulative >= threshold) return (uint)i;
        }
        return kHistogramBins - 1u;
    }

    private bool CompletePreviousEvaluationWithoutStall()
    {
        if (!m_EvaluationPending) return true;
        var combined = JobHandle.CombineDependencies(m_EvaluationHandle, m_ReaderHandle);
        if (!combined.IsCompleted) return false;
        combined.Complete();
        m_EvaluationPending = false;
        ProcessDiagnostics();
        return true;
    }

    private void ProcessDiagnostics()
    {
        var counters = m_Counters[0];
        Accumulate(ref m_ReportCounters, counters);
        m_ReportScans++;
        AccumulateDetailedDiagnostics();
        if (P0Diagnostics.Vehicle != Entity.Null)
            foreach (var evaluation in m_Evaluations)
                if (evaluation.m_Vehicle == P0Diagnostics.Vehicle)
                    P0Diagnostics.Record("Safety", $"verdict={evaluation.m_Verdict} reasons={evaluation.m_Reasons} remaining={evaluation.m_DistanceToGateAnchor:F1} required={evaluation.m_RequiredDistance:F1} speed={evaluation.m_Speed:F1} braking={evaluation.m_Braking:F1} path={evaluation.m_PathState}");
        DumpFilteredEvaluations();

        // Mirror the per-frame numbers into managed arrays. This is the only place the native
        // counters are read outside a completed job, which is what lets every consumer above stay
        // free of JobHandle.Complete().
        m_LastEvaluationCount = m_Evaluations.Length;
        for (var i = 0; i < m_LastCategoryVerdicts.Length; i++) m_LastCategoryVerdicts[i] = m_CategoryVerdicts[i];
        for (var metric = 0; metric < (int)LatencyMetric.Count; metric++)
            m_LastPercentiles[metric] = GetLatencyPercentiles((LatencyMetric)metric);

        if (m_ReportScans < 256) return;
        LogAggregate(m_ReportCounters);
        LogDetailedAggregate();
        m_ReportScans = 0;
        m_ReportCounters = default;
        Array.Clear(m_ReportCategoryVerdicts, 0, m_ReportCategoryVerdicts.Length);
        Array.Clear(m_ReportReasons, 0, m_ReportReasons.Length);
        m_ReportRemainingDistance.Reset();
        m_ReportBrakingDistance.Reset();
        m_ReportFirstSeenFrames.Reset();
    }

    private void AccumulateDetailedDiagnostics()
    {
        foreach (var value in m_Evaluations)
        {
            m_ReportCategoryVerdicts[(int)value.m_Category * kVerdictCount + (int)value.m_Verdict]++;
            m_ReportRemainingDistance.Add(value.m_DistanceToGateAnchor);
            m_ReportBrakingDistance.Add(value.m_BrakingDistance);
            m_ReportFirstSeenFrames.Add(value.m_ObservedApproachFrames);

            var reasons = (ulong)value.m_Reasons;
            for (var bit = 0; bit < kSafetyReasonBitCount; bit++)
                if ((reasons & (1UL << bit)) != 0)
                    m_ReportReasons[bit]++;
        }
    }

    [Conditional("DEBUG")]
    private void DumpFilteredEvaluations()
    {
        if (DebugVehicle == Entity.Null && DebugTarget == Entity.Null && m_Evaluations.Length != 0)
        {
            DebugVehicle = m_Evaluations[0].m_Vehicle;
            DebugTarget = m_Evaluations[0].m_Target;
            m_CandidateSystem.DebugTarget = DebugTarget;
            Mod.Log.Info(
                $"[RouteFilter.Safety] Development trace selected Vehicle={FormatEntity(DebugVehicle)} " +
                $"Target={FormatEntity(DebugTarget)}");
        }

        var matchedFilter = false;
        foreach (var value in m_Evaluations)
        {
            if (DebugVehicle != Entity.Null && value.m_Vehicle != DebugVehicle) continue;
            if (DebugTarget != Entity.Null && value.m_Target != DebugTarget) continue;
            matchedFilter = true;
            Mod.Log.Info(
                $"[RouteFilter.Safety] Vehicle={FormatEntity(value.m_Vehicle)} Target={FormatEntity(value.m_Target)} " +
                $"Verdict={value.m_Verdict} Reasons={value.m_Reasons} Category={value.m_Category} " +
                $"Speed={value.m_Speed:F2} Braking={value.m_Braking:F2} " +
                $"GateDistance={value.m_DistanceToGateAnchor:F2} BrakingDistance={value.m_BrakingDistance:F2} " +
                $"GeometryLength={value.m_VehicleGeometryLength:F2} PipelineFrames={value.m_CandidatePipelineFrames} " +
                $"ApproachFrames={value.m_ObservedApproachFrames} SimSpeed={value.m_SelectedSimulationSpeed:F2}/" +
                $"{value.m_SmoothSimulationSpeed:F2} Confidence={value.m_Confidence}");
        }

        if (DebugVehicle == Entity.Null && DebugTarget == Entity.Null) return;
        if (matchedFilter)
        {
            m_DebugMissScans = 0;
            return;
        }

        // Rotate the development trace after the observed vehicle has left the gate. This
        // preserves single-vehicle logging while allowing a test session to cover more than
        // one scenario without restarting the game.
        if (++m_DebugMissScans < 64) return;
        DebugVehicle = Entity.Null;
        DebugTarget = Entity.Null;
        m_CandidateSystem.DebugTarget = Entity.Null;
        m_DebugMissScans = 0;
    }

    [Conditional("DEBUG")]
    private static void LogAggregate(SafetyDiagnosticCounters value)
        => Mod.Log.Info(
            $"[RouteFilter.SafetyStats] Evaluated={value.m_Evaluated} Safe={value.m_Safe} " +
            $"Unsafe={value.m_Unsafe} Unknown={value.m_Unknown} " +
            $"PathStateRejected={value.m_PathStateRejected} ContextRejected={value.m_ContextRejected} " +
            $"LatePass={value.m_LatePass} AmbiguousPass={value.m_AmbiguousPass} " +
            $"Uncalibrated={value.m_Uncalibrated}");

    [Conditional("DEBUG")]
    private void LogDetailedAggregate()
    {
        Mod.Log.Info(
            $"[RouteFilter.SafetyDistribution] RemainingDistanceMeters={FormatDistribution(m_ReportRemainingDistance)} " +
            $"BrakingDistanceMeters={FormatDistribution(m_ReportBrakingDistance)} " +
            $"FirstSeenAgeFrames={FormatDistribution(m_ReportFirstSeenFrames)}");

        var categories = string.Empty;
        for (var category = 0; category < kVehicleCategoryCount; category++)
        {
            var offset = category * kVerdictCount;
            var safe = m_ReportCategoryVerdicts[offset + (int)RerouteSafetyVerdict.Safe];
            var unsafeCount = m_ReportCategoryVerdicts[offset + (int)RerouteSafetyVerdict.Unsafe];
            var unknown = m_ReportCategoryVerdicts[offset + (int)RerouteSafetyVerdict.Unknown];
            if (safe + unsafeCount + unknown == 0) continue;
            if (categories.Length != 0) categories += ";";
            categories += $"{(RoadVehicleCategory)category}=S{safe}/U{unsafeCount}/?{unknown}";
        }
        Mod.Log.Info($"[RouteFilter.SafetyByCategory] {categories}");

        var reasons = string.Empty;
        for (var bit = 0; bit < kSafetyReasonBitCount; bit++)
        {
            if (m_ReportReasons[bit] == 0) continue;
            if (reasons.Length != 0) reasons += ";";
            reasons += $"{(SafetyReason)(1UL << bit)}={m_ReportReasons[bit]}";
        }
        Mod.Log.Info($"[RouteFilter.SafetyReasons] {reasons}");
    }

    private static string FormatDistribution(ReportDistribution value)
        => value.Count == 0
            ? "n=0"
            : $"n={value.Count},min={value.Min:F1},p50={value.Percentile(50):F1}," +
              $"p95={value.Percentile(95):F1},p99={value.Percentile(99):F1}," +
              $"max={value.Max:F1},mean={value.Mean:F1}";

    private static void Accumulate(ref SafetyDiagnosticCounters total, SafetyDiagnosticCounters value)
    {
        total.m_Evaluated += value.m_Evaluated;
        total.m_Safe += value.m_Safe;
        total.m_Unsafe += value.m_Unsafe;
        total.m_Unknown += value.m_Unknown;
        total.m_PathStateRejected += value.m_PathStateRejected;
        total.m_ContextRejected += value.m_ContextRejected;
        total.m_LatePass += value.m_LatePass;
        total.m_AmbiguousPass += value.m_AmbiguousPass;
        total.m_Uncalibrated += value.m_Uncalibrated;
    }

    private static string FormatEntity(Entity entity)
        => entity == Entity.Null ? "Null" : $"{entity.Index}:{entity.Version}";
}
