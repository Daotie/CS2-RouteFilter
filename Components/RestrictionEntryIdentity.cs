using System;

namespace RouteFilter.Persistence;

/// <summary>Player intent: enter through this endpoint of this exact road connection.
/// No lane entity, compass heading, camera direction or overlay geometry is persisted.</summary>
public struct RestrictionEntryIdentity : IEquatable<RestrictionEntryIdentity>
{
    public RestrictionTargetIdentity Connection;
    public RestrictionAnchor Endpoint;
    public RestrictionAnchor CurveMidpoint;
    public string RoadPrefab;

    // Edge start/end ordering is runtime representation, not the identity of the
    // actual entering endpoint. Normalize only the connection; retain Endpoint.
    public RestrictionEntryIdentity Normalized()
    {
        var copy = this;
        var a = Connection.Anchor; var b = Connection.EndAnchor;
        if (a.X > b.X || (a.X == b.X && (a.Y > b.Y || (a.Y == b.Y && a.Z > b.Z))))
        { copy.Connection.Anchor = b; copy.Connection.EndAnchor = a; }
        return copy;
    }
    public bool Equals(RestrictionEntryIdentity other) => Normalized().Connection.Equals(other.Normalized().Connection) &&
        Endpoint.Equals(other.Endpoint) && CurveMidpoint.Equals(other.CurveMidpoint) &&
        string.Equals(RoadPrefab, other.RoadPrefab, StringComparison.Ordinal);
    public override bool Equals(object other) => other is RestrictionEntryIdentity entry && Equals(entry);
    public override int GetHashCode() => unchecked(((Normalized().Connection.GetHashCode() * 397 ^ Endpoint.GetHashCode()) * 397 ^
        CurveMidpoint.GetHashCode()) * 397 ^ (RoadPrefab?.GetHashCode() ?? 0));
    public bool IsValid => Connection.Kind == 1 && !Connection.Anchor.Equals(Connection.EndAnchor) && Connection.LengthCentimetres > 0 &&
        (Endpoint.Equals(Connection.Anchor) || Endpoint.Equals(Connection.EndAnchor)) &&
        !string.IsNullOrWhiteSpace(RoadPrefab) && RoadPrefab.Length <= RouteFilterSaveData.MaxPrefabNameLength;
}
