using System;
using Unity.Entities;

namespace RouteFilter.Components;

/// <summary>The kind of restriction entity described by a topology record.</summary>
public enum RestrictionTopologyTargetType : byte
{
    Node = 0,
    Segment = 1
}

/// <summary>Direction in which a directed lane traversal follows its stored Lane nodes.</summary>
public enum LaneTraversalDirection : byte
{
    Forward = 0,
    Reverse = 1
}

/// <summary>Endpoint of a restricted segment through which a traversal enters.</summary>
public enum RestrictionEndpoint : byte
{
    None = 0,
    Start = 1,
    End = 2
}

/// <summary>
/// A directed transition from an upstream road lane into a restricted target's internal
/// lane. Entity values and compact enums make the record suitable for a later NativeArray
/// or NativeParallelMultiHashMap representation.
/// </summary>
public struct DirectedEntryGate : IEquatable<DirectedEntryGate>
{
    public Entity m_EntryLane;
    public Entity m_NextLane;
    public Entity m_Target;
    public LaneTraversalDirection m_EntryDirection;
    public LaneTraversalDirection m_NextDirection;
    public RestrictionEndpoint m_TargetEndpoint;

    public bool Equals(DirectedEntryGate other)
        => m_EntryLane == other.m_EntryLane &&
           m_NextLane == other.m_NextLane &&
           m_Target == other.m_Target &&
           m_EntryDirection == other.m_EntryDirection &&
           m_NextDirection == other.m_NextDirection &&
           m_TargetEndpoint == other.m_TargetEndpoint;

    public override bool Equals(object obj) => obj is DirectedEntryGate other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = m_EntryLane.GetHashCode();
            hash = (hash * 397) ^ m_NextLane.GetHashCode();
            hash = (hash * 397) ^ m_Target.GetHashCode();
            hash = (hash * 397) ^ (int)m_EntryDirection;
            hash = (hash * 397) ^ (int)m_NextDirection;
            return (hash * 397) ^ (int)m_TargetEndpoint;
        }
    }
}

/// <summary>Machine-readable reason why a lane could not safely become a directed gate.</summary>
public enum RestrictionTopologyAmbiguityReason : byte
{
    MissingSubLanes = 0,
    MissingLaneData = 1,
    DegenerateTraversal = 2,
    NoRoadInternalLanes = 3,
    NoDirectedEntryGate = 4,
    NoInboundTraversal = 5,
    NoOutboundTraversal = 6,
    EndpointOwnerMismatch = 7,
    SelfReferentialGate = 8
}

/// <summary>Compact diagnostic record; reasons are enums rather than hot-path strings.</summary>
public struct RestrictionTopologyAmbiguity : IEquatable<RestrictionTopologyAmbiguity>
{
    public Entity m_Target;
    public Entity m_Lane;
    public Entity m_RelatedLane;
    public RestrictionTopologyAmbiguityReason m_Reason;

    public bool Equals(RestrictionTopologyAmbiguity other)
        => m_Target == other.m_Target &&
           m_Lane == other.m_Lane &&
           m_RelatedLane == other.m_RelatedLane &&
           m_Reason == other.m_Reason;

    public override bool Equals(object obj)
        => obj is RestrictionTopologyAmbiguity other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = m_Target.GetHashCode();
            hash = (hash * 397) ^ m_Lane.GetHashCode();
            hash = (hash * 397) ^ m_RelatedLane.GetHashCode();
            return (hash * 397) ^ (int)m_Reason;
        }
    }
}
