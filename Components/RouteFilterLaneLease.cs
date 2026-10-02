using Unity.Entities;

namespace RouteFilter.Components;

public enum LaneLeaseState : byte { Active, Released, RestoreConflict, LaneDeleted }

/// <summary>Runtime ownership only. Deliberately not IComponentData or ISerializable.</summary>
public struct RouteFilterLaneLease
{
    public Entity Lane, Target, Vehicle;
    public int RestrictionRevision;
    public byte OriginalBlockageStart, OriginalBlockageEnd;
    public byte WrittenBlockageStart, WrittenBlockageEnd;
    public uint CreatedFrame, AbsoluteExpiryFrame, OwnerToken;
    public LaneLeaseState State;
}

/// <summary>Pure rules shared by the runtime and regression fixtures.</summary>
public static class LaneLeaseRules
{
    // Vanilla represents an empty interval by start > end, not necessarily 0/0.
    public static bool IsEmpty(byte start, byte end) => start > end;
    public static bool HasExpired(uint frame, uint absoluteExpiry)
        => unchecked((int)(frame - absoluteExpiry)) >= 0;
    public static bool OwnsCurrent(RouteFilterLaneLease lease, byte start, byte end)
        => lease.State == LaneLeaseState.Active &&
           start == lease.WrittenBlockageStart && end == lease.WrittenBlockageEnd;
}
