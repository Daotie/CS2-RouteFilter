using Unity.Entities;

namespace RouteFilter.Components;

public enum RoadLeaseState : byte { Active, Released, RestoreConflict, LaneDeleted }

/// <summary>
/// RouteFilter-owned temporary mutation of one vanilla <c>CarLane</c> blockage interval.
///
/// Ownership facts verified against Cities: Skylines II 1.6.0f1:
/// <list type="bullet">
/// <item><c>Game.Net.CarLane</c> is <c>ISerializable</c> and writes
/// <c>m_BlockageStart</c>/<c>m_BlockageEnd</c>, so a live lease IS save-visible. Release must
/// therefore happen before the native serializer runs, not merely before RouteFilter's own
/// serialize callback.</item>
/// <item><c>Game.Pathfind.PathUtils.GetCarDriveSpecification</c> turns a non-empty interval into
/// <c>RuleFlags.HasBlockage</c> on the published edge, and
/// <c>Game.Pathfind.PathfindJobs.IsValidDelta</c> then rejects every traversal that overlaps it
/// for every seeker that does not ignore the rule. The mutation is coarse: it is not scoped to
/// one prefab.</item>
/// <item><c>Game.Pathfind.LaneDataSystem.CheckBlockage</c> recomputes the interval from
/// non-moving <c>LaneObject</c>s and clears it when a lane-data refresh runs, so the write is a
/// short-lived override, not a durable graph edit.</item>
/// </list>
///
/// Rail has no equivalent: <c>Game.Net.TrackLane</c> has no blockage interval and
/// <c>Game.Pathfind.PathUtils.GetTrackDriveSpecification</c> never emits
/// <c>RuleFlags.HasBlockage</c>. See RAIL_ENFORCEMENT_DESIGN.md.
/// </summary>
/// <remarks>
/// Runtime ownership only. Deliberately not <c>IComponentData</c> and not
/// <c>ISerializable</c>: the save format stores player intent, never this.
/// </remarks>
public struct RoadLaneLease
{
    public Entity Lane, Target, GateEntryLane;
    public int RestrictionRevision;
    public byte OriginalBlockageStart, OriginalBlockageEnd;
    public byte WrittenBlockageStart, WrittenBlockageEnd;
    public uint CreatedFrame, AbsoluteExpiryFrame, OwnerToken;
    public RoadLeaseState State;
}

/// <summary>
/// Pure rules shared by the runtime, the regression fixtures and the save tests.
/// Nothing here touches ECS state, so every invariant is verifiable offline.
/// </summary>
public static class RoadLeaseRules
{
    /// <summary>
    /// Vanilla represents an empty interval by start greater than end, and
    /// <c>Game.Net.CarLane.Deserialize</c> writes 255/0 for lanes that never had blockage
    /// data, so "empty" must not be hard-coded to 0/0.
    /// </summary>
    public static bool IsEmpty(byte start, byte end) => start > end;

    /// <summary>Wrap-safe absolute expiry on the simulation frame counter.</summary>
    public static bool HasExpired(uint frame, uint absoluteExpiry)
        => unchecked((int)(frame - absoluteExpiry)) >= 0;

    /// <summary>
    /// RouteFilter may only put back what it wrote, and only while it still believes it owns the
    /// write. Any other value means vanilla, the player or another mod changed the lane, and the
    /// safe answer is to leave that value alone.
    /// </summary>
    public static bool OwnsCurrent(RoadLaneLease lease, byte start, byte end)
        => lease.State == RoadLeaseState.Active &&
           start == lease.WrittenBlockageStart && end == lease.WrittenBlockageEnd;

    /// <summary>
    /// A lease may only be created on a lane that vanilla currently considers empty. Acquiring on
    /// an occupied lane would overwrite another owner's blockage, so that is rejected outright.
    /// </summary>
    public static bool CanAcquire(byte currentStart, byte currentEnd) => IsEmpty(currentStart, currentEnd);
}
