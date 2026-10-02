using System;
using Unity.Entities;

namespace RouteFilter.Components;

/// <summary>Which backend owns a restriction event for one canonical vehicle or consist.</summary>
/// <remarks>
/// Ownership is decided by construction, not by a runtime election: a road candidate requires
/// <c>Car</c> plus <c>CarCurrentLane</c>, a rail candidate requires <c>Train</c> plus
/// <c>TrainCurrentLane</c>. A tram is a <c>Train</c> and is therefore only ever visible to the
/// rail backend, even when it drives on a lane entity that also carries <c>CarLane</c>.
/// </remarks>
public enum EnforcementBackend : byte
{
    None = 0,
    Road = 1,
    Rail = 2
}

/// <summary>
/// Classification of a canonical road vehicle. Lives here rather than in the safety pipeline
/// because emergency exemption is an enforcement policy decision, not an observation.
/// </summary>
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

public enum EnforcementAttemptState : byte
{
    /// <summary>Obsolete was written; waiting for vanilla to resolve it.</summary>
    Requested = 0,
    /// <summary>Vanilla cleared Obsolete and produced a usable path again.</summary>
    Resolved = 1,
    /// <summary>Vanilla reported Failed, Stuck, or kept Obsolete past the absolute deadline.</summary>
    Unresolved = 2,
    /// <summary>RouteFilter gave up before requesting anything; vehicle is grandfathere.</summary>
    Grandfathered = 3
}

public enum EnforcementRefusalReason : byte
{
    None = 0,
    /// <summary>Already requested for this vehicle/target pair and the attempt is still live.</summary>
    AttemptAlreadyActive = 1,
    /// <summary>Vanilla already has a path request in flight for this vehicle.</summary>
    PathBusy = 2,
    /// <summary>Restriction changed after the candidate was produced.</summary>
    StaleRevision = 3,
    /// <summary>Vehicle is destroyed, parked, temporary or despawning.</summary>
    VehicleInvalid = 4,
    /// <summary>Vehicle follows a fixed line or dispatch; RouteFilter never redraws those.</summary>
    FixedRoute = 5,
    /// <summary>Category is exempt, currently emergency services.</summary>
    ExemptCategory = 6,
    /// <summary>Both backends would act on the same canonical vehicle for this frame.</summary>
    BackendConflict = 7,
    /// <summary>The city-scale graph mutation budget for this second is exhausted.</summary>
    MutationBudgetExhausted = 8,
    /// <summary>Target, gate lane or next lane no longer exists.</summary>
    TopologyInvalid = 9,
    /// <summary>Safety evaluation did not return Safe.</summary>
    NotSafe = 10,
    /// <summary>Runtime lease/attempt store is full; this frame's request is dropped, not queued.</summary>
    StoreFull = 11
}

/// <summary>
/// One reroute request for one canonical vehicle or consist against one target.
/// Runtime-only: never serialized, never persisted, rebuilt from player configuration.
/// </summary>
public struct EnforcementAttempt
{
    /// <summary>Canonical vehicle (road) or locomotive (rail). Holds the vanilla PathOwner.</summary>
    public Entity Vehicle;
    public Entity Target;
    public Entity GateEntryLane;
    /// <summary>Lane whose blockage interval RouteFilter owns, or Entity.Null when unbacked.</summary>
    public Entity OwnedLane;
    public int RestrictionRevision;
    public Game.Pathfind.PathFlags OriginalPathState, WrittenPathState;
    public int OriginalElementIndex;
    public uint RequestedFrame;
    /// <summary>Hard deadline. No attempt may be extended; the record simply ends.</summary>
    public uint AbsoluteDeadlineFrame;
    public EnforcementBackend Backend;
    public EnforcementAttemptState State;
    public EnforcementRefusalReason LastRefusal;
    public RoadVehicleCategory Category;
}

/// <summary>
/// Key for "one canonical vehicle (or consist head) approaching one target". Shared by the road and
/// rail pipelines, which track approaches independently but mean exactly the same thing by it.
/// </summary>
public struct CanonicalApproachKey : IEquatable<CanonicalApproachKey>
{
    public Entity Vehicle;
    public Entity Target;

    public bool Equals(CanonicalApproachKey other) => Vehicle == other.Vehicle && Target == other.Target;

    public override bool Equals(object obj) => obj is CanonicalApproachKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked { return (Vehicle.GetHashCode() * 397) ^ Target.GetHashCode(); }
    }
}

/// <summary>
/// All policy that must be provable without the game lives here, as pure functions over plain
/// data, so the offline fixtures can exercise every branch.
/// </summary>
public static class EnforcementPolicy
{
    public static bool OwnsRequest(in EnforcementAttempt attempt, Game.Pathfind.PathFlags state, int elementIndex)
        => state == attempt.WrittenPathState && elementIndex == attempt.OriginalElementIndex;

    /// <summary>
    /// Vanilla turns <c>PathFlags.Obsolete</c> into an actual path request only through
    /// <c>Game.Vehicles.VehicleUtils.RequireNewPath</c>, which refuses while the owner is
    /// Pending, Failed or Stuck. Writing Obsolete in those states would be a silent no-op that
    /// still marks the vehicle as handled, so RouteFilter must not write it at all.
    /// </summary>
    public static bool CanRequestReroute(Game.Pathfind.PathFlags state)
        => (state & (Game.Pathfind.PathFlags.Obsolete | Game.Pathfind.PathFlags.DivertObsolete)) == 0 &&
           (state & (Game.Pathfind.PathFlags.Pending | Game.Pathfind.PathFlags.Scheduled |
                     Game.Pathfind.PathFlags.Failed | Game.Pathfind.PathFlags.Stuck)) == 0;

    /// <summary>
    /// Emergency services are exempt from RouteFilter enforcement. A city that cannot send an
    /// ambulance is already broken, and emergency vehicles are the one class whose detour cost
    /// is a public-safety cost rather than a traffic cost.
    /// </summary>
    public static bool IsExempt(RoadVehicleCategory category) => category == RoadVehicleCategory.Emergency;

    /// <summary>
    /// Distances required before a reroute request is issued. The latency term is a deliberate
    /// conservative assumption, not a measurement: it is the budget for vanilla to enqueue,
    /// run and apply one car pathfind plus the vehicle's own reaction. It is deliberately far
    /// larger than the typical case so that a busy city fails toward grandfathering rather than
    /// toward a too-late reroute.
    /// </summary>
    public static float RequiredDistance(
        float speed,
        float braking,
        float expectedLatencySeconds,
        float vehicleLength,
        float uncertaintyMargin)
    {
        if (!(speed >= 0f) || !(braking > 0f) || !IsFinite(speed) || !IsFinite(braking))
            return float.PositiveInfinity;
        if (!(expectedLatencySeconds > 0f) || !(uncertaintyMargin >= 0f)) return float.PositiveInfinity;
        var brakingDistance = 0.5f * speed * speed / braking + speed * 4f / 15f;
        return speed * expectedLatencySeconds + brakingDistance + (vehicleLength > 0f ? vehicleLength : 0f) + uncertaintyMargin;
    }

    /// <summary>
    /// A request is only issued when the vehicle still has room to act before it commits to the
    /// restricted target. Strictly greater than, never greater-or-equal, so a vehicle exactly at
    /// the decision point is treated as too late.
    /// </summary>
    public static bool HasRoomToAct(float distanceToGateAnchor, float requiredDistance)
        => IsFinite(distanceToGateAnchor) && IsFinite(requiredDistance) &&
           distanceToGateAnchor > requiredDistance;

    /// <summary>
    /// A token is a monotonic, never-reused counter. It exists so a released lease can never be
    /// confused with a later lease on the same lane, without any reference counting.
    /// </summary>
    public static uint NextToken(uint current) => current + 1u;

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
