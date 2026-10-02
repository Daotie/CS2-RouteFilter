using System;
using Game.Pathfind;
using Unity.Entities;

namespace RouteFilter.Components;

public enum RerouteSafetyVerdict : byte
{
    Safe = 0,
    Unsafe = 1,
    Unknown = 2
}

public enum SafetyConfidence : byte
{
    None = 0,
    Instrumenting = 1,
    Calibrated = 2
}

public enum RoadVehicleCategory : byte
{
    Personal = 0,
    DummyTraffic = 1,
    Taxi = 2,
    PublicTransport = 3,
    Cargo = 4,
    Delivery = 5,
    Emergency = 6,
    MunicipalService = 7,
    WorkVehicle = 8,
    Other = 9
}

[Flags]
public enum SafetyReason : ulong
{
    None = 0,
    VehicleInvalid = 1UL << 0,
    TargetInvalid = 1UL << 1,
    RestrictionRevisionChanged = 1UL << 2,
    CanonicalControllerChanged = 1UL << 3,
    PathOwnerMissing = 1UL << 4,
    PathPending = 1UL << 5,
    PathScheduled = 1UL << 6,
    PathFailed = 1UL << 7,
    PathStuck = 1UL << 8,
    PathObsolete = 1UL << 9,
    PathAppend = 1UL << 10,
    PathDivert = 1UL << 11,
    PathDivertObsolete = 1UL << 12,
    PathCachedObsolete = 1UL << 13,
    CurrentLaneChanged = 1UL << 14,
    ImmediateLaneChanged = 1UL << 15,
    LaneChangeAmbiguous = 1UL << 16,
    UnsupportedTransition = 1UL << 17,
    EntryDirectionChanged = 1UL << 18,
    NextDirectionChanged = 1UL << 19,
    AtOrPastGateAnchor = 1UL << 20,
    CurveUnavailable = 1UL << 21,
    BrakingUnavailable = 1UL << 22,
    LastSafeDecisionPointUnknown = 1UL << 23,
    LatencyUncalibrated = 1UL << 24,
    GeometryMarginUncalibrated = 1UL << 25,
    LaneChangeMarginUncalibrated = 1UL << 26,
    UncertaintyMarginUncalibrated = 1UL << 27,
    InsufficientAvailableDistance = 1UL << 28
}

/// <summary>
/// Optional calibrated inputs for the pure evaluator. Phase 1C deliberately supplies an
/// uncalibrated value until game measurements establish these quantities.
/// </summary>
public struct RerouteSafetyCalibration
{
    public SafetyConfidence m_Confidence;
    public bool m_HasLastSafeDecisionPoint;
    public float m_DistanceToLastSafeDecisionPoint;
    public float m_ExpectedLatencySeconds;
    public float m_VehicleGeometryMargin;
    public float m_LaneChangeMargin;
    public float m_UncertaintyMargin;
}

public struct RerouteSafetyInput
{
    public CandidateMatch m_Candidate;
    public uint m_EvaluationFrame;
    public int m_CurrentRestrictionRevision;
    public bool m_VehicleValid;
    public bool m_TargetValid;
    public bool m_CanonicalControllerValid;
    public bool m_CurrentLaneMatches;
    public bool m_ImmediateLaneMatches;
    public bool m_LaneChangeUnambiguous;
    public bool m_TransitionSupported;
    public bool m_EntryDirectionMatches;
    public bool m_NextDirectionMatches;
    public bool m_AtOrPastGateAnchor;
    public bool m_HasCurve;
    public bool m_HasPathOwner;
    public PathFlags m_PathState;
    public RoadVehicleCategory m_Category;
    public float m_SelectedSimulationSpeed;
    public float m_SmoothSimulationSpeed;
    public float m_Speed;
    public float m_Braking;
    public float m_CurveLength;
    public float m_DistanceToGateAnchor;
    public float m_VehicleGeometryLength;
    public RerouteSafetyCalibration m_Calibration;
}

/// <summary>Read-only Phase 1C result. It is not an enforcement request or token.</summary>
public struct RerouteSafetyEvaluation
{
    public Entity m_Vehicle;
    public Entity m_PhysicalVehicle;
    public Entity m_Target;
    public Entity m_EntryLane;
    public Entity m_NextLane;
    public Entity m_MatchedPrefab;
    public RerouteSafetyVerdict m_Verdict;
    public SafetyConfidence m_Confidence;
    public SafetyReason m_Reasons;
    public RoadVehicleCategory m_Category;
    public PathFlags m_PathState;
    public int m_RestrictionRevision;
    public uint m_CandidateFrame;
    public uint m_FirstSeenFrame;
    public uint m_EvaluationFrame;
    public uint m_CandidatePipelineFrames;
    public uint m_ObservedApproachFrames;
    public float m_SelectedSimulationSpeed;
    public float m_SmoothSimulationSpeed;
    public float m_Speed;
    public float m_Braking;
    public float m_VanillaTimeStep;
    public float m_BrakingDistance;
    public float m_CurveLength;
    public float m_DistanceToGateAnchor;
    public float m_DistanceToLastSafeDecisionPoint;
    public float m_ExpectedLatencySeconds;
    public float m_ExpectedLatencyDistance;
    public float m_VehicleGeometryLength;
    public float m_VehicleGeometryMargin;
    public float m_LaneChangeMargin;
    public float m_UncertaintyMargin;
    public float m_RequiredDistance;
    public float m_EstimatedSecondsToGate;
}

public struct SafetyDiagnosticCounters
{
    public int m_Evaluated;
    public int m_Safe;
    public int m_Unsafe;
    public int m_Unknown;
    public int m_PathStateRejected;
    public int m_ContextRejected;
    public int m_LatePass;
    public int m_AmbiguousPass;
    public int m_Uncalibrated;
}

public struct LatencyPercentiles
{
    public uint m_Count;
    public uint m_MedianFrames;
    public uint m_P95Frames;
    public uint m_P99Frames;
    public uint m_MaxObservedFrames;
}
