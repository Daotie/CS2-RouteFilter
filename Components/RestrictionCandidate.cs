using Unity.Entities;
using Unity.Mathematics;

namespace RouteFilter.Components;

public enum CandidateSource : byte
{
    LaneObject = 0
}

public enum RejectedCandidateReason : byte
{
    None = 0,
    LaneDeleted = 1,
    LaneObjectBufferMissing = 2,
    PhysicalEntityInvalid = 3,
    Deleted = 4,
    Parked = 5,
    NotVehicle = 6,
    CanonicalControllerInvalid = 7,
    RailUnsupported = 8,
    RoadControllerMissing = 9,
    PrefabNotRestricted = 10,
    TargetDeleted = 11,
    StaleLaneObject = 12,
    LaneChangeAmbiguous = 13,
    SpecialLaneTransition = 14,
    ImmediateNavigationMissing = 15,
    ImmediateLaneMismatch = 16,
    EntryDirectionMismatch = 17,
    NextDirectionMismatch = 18,
    MasterLaneUnsupported = 19,
    StaleRevision = 20,
    Count = 21
}

/// <summary>Read-only Phase 1B output. No field is consumed as an enforcement command.</summary>
public struct CandidateMatch
{
    public Entity m_Vehicle;
    public Entity m_PhysicalVehicle;
    public Entity m_EntryLane;
    public Entity m_NextLane;
    public Entity m_Target;
    public Entity m_RestrictedPrefab;
    public Entity m_MatchedPrefab;
    public RestrictionTopologyTargetType m_TargetType;
    public LaneTraversalDirection m_EntryDirection;
    public LaneTraversalDirection m_NextDirection;
    public RestrictionEndpoint m_TargetEndpoint;
    public CandidateSource m_Source;
    public int m_RestrictionRevision;
    public uint m_DetectionFrame;
    public uint m_FirstSeenFrame;
    public float3 m_CurrentCurvePosition;
    public float2 m_LaneObjectCurvePosition;
    public float3 m_Velocity;
    public Entity m_ChangeLane;
    public float m_ChangeProgress;
    public float m_EntryTraversalEndpoint;
    public float m_EntryLaneLength;
    public float m_BrakingCapability;
    public float m_DirectionAwareRemainingDistanceApprox;
    public bool m_AtOrPastEntryAnchorWhenFirstSeen;
}

public struct RejectedCandidate
{
    public Entity m_PhysicalVehicle;
    public Entity m_Vehicle;
    public Entity m_EntryLane;
    public Entity m_NextLane;
    public Entity m_ObservedCurrentLane;
    public Entity m_ObservedNextLane;
    public Entity m_Target;
    public RejectedCandidateReason m_Reason;
    public uint m_DetectionFrame;
    public float3 m_CurrentCurvePosition;
    public float2 m_LaneObjectCurvePosition;
    public float3 m_Velocity;
    public float m_DirectionAwareRemainingDistanceApprox;
    public bool m_LaneObjectAppearedStale;
    public bool m_AtOrPastEntryAnchor;
}

public struct CandidateDiagnosticCounters
{
    public int m_WatchedGates;
    public int m_WatchedEntryLanes;
    public int m_LaneObjectsScanned;
    public int m_PhysicalEntitiesSeen;
    public int m_CanonicalVehicles;
    public int m_DuplicatesRemoved;
    public int m_PrefabRejected;
    public int m_NavigationRejected;
    public int m_AmbiguousRejected;
    public int m_CandidatesMatched;
    public int m_DeletedRejected;
    public int m_StaleLaneObjectRejected;
    public int m_StaleRevisionRejected;
}
