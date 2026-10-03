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
    Grandfathered = 3,
    Rerouted = 4,
    ConfirmedNoAlternative = 5
}

/// <summary>Evidence from ONE owned native query, not the vehicle's generic PathOwner flags.</summary>
public enum RoadQueryOutcome : byte { EnforcementUncertain, AlternativePathFound, ConfirmedNoAlternative }

public struct RoadQueryProof
{
    public bool CompleteExclusion, ValidEndpoints, QueryCompleted, AvoidsTarget, EmptyPath;
    public bool SearchExhausted, BaselineCrossesTarget, ExactNoRouteScope, Cancelled, Exception, RestoreConflict;
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
    public Entity ViaLane;
    public Entity ViaLane2;
    /// <summary>Imminent forbidden lane required in the query exclusion set. No physical blockage.</summary>
    public Entity OwnedLane;
    public Entity NativeDestination;
    public Entity MatchedPrefab;
    public ulong Generation;
    public float Braking, GeometryLength;
    public float TraversalEnd;
    public bool Forward;
    public int RestrictionRevision;
    public Game.Pathfind.PathFlags OriginalPathState, WrittenPathState;
    public int OriginalElementIndex;
    public uint RequestedFrame;
    /// <summary>Hard admission deadline, never extended. A terminal dedupe record may outlive it.</summary>
    public uint AbsoluteDeadlineFrame;
    public EnforcementBackend Backend;
    public EnforcementAttemptState State;
    public EnforcementRefusalReason LastRefusal;
    public RoadVehicleCategory Category;
    /// <summary>Native query exclusion was consumed once for this approach.</summary>
    public bool QueryIntercepted;
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
    public static bool OwnsRoadReceipt(in EnforcementAttempt live, in EnforcementAttempt receipt)
        => OwnsReceipt(live, receipt, EnforcementBackend.Road);

    public static bool OwnsRailReceipt(in EnforcementAttempt live, in EnforcementAttempt receipt)
        => OwnsReceipt(live, receipt, EnforcementBackend.Rail);

    private static bool OwnsReceipt(in EnforcementAttempt live, in EnforcementAttempt receipt, EnforcementBackend backend)
        => live.Backend == backend && receipt.Backend == backend && live.QueryIntercepted &&
           live.State == EnforcementAttemptState.Requested && live.Generation != 0 &&
           live.Generation == receipt.Generation && live.Vehicle == receipt.Vehicle &&
           live.Target == receipt.Target && live.MatchedPrefab == receipt.MatchedPrefab &&
           live.RestrictionRevision == receipt.RestrictionRevision && live.OwnedLane == receipt.OwnedLane &&
           live.GateEntryLane == receipt.GateEntryLane && live.NativeDestination == receipt.NativeDestination &&
           live.ViaLane == receipt.ViaLane && live.ViaLane2 == receipt.ViaLane2;

    public static RoadQueryOutcome ClassifyRoadQuery(in RoadQueryProof proof)
    {
        if (!proof.CompleteExclusion || !proof.ValidEndpoints || !proof.QueryCompleted ||
            proof.Cancelled || proof.Exception || proof.RestoreConflict)
            return RoadQueryOutcome.EnforcementUncertain;
        if (proof.AvoidsTarget && !proof.EmptyPath) return RoadQueryOutcome.AlternativePathFound;
        return proof.EmptyPath && proof.SearchExhausted && proof.BaselineCrossesTarget && proof.ExactNoRouteScope ? RoadQueryOutcome.ConfirmedNoAlternative :
            RoadQueryOutcome.EnforcementUncertain;
    }
    /// <summary>Wrap-safe expiry; no pending native request may extend the absolute deadline.</summary>
    public static bool HasExpired(uint frame, uint deadline) => unchecked((int)(frame - deadline)) >= 0;

    public static bool OwnsRequest(in EnforcementAttempt attempt, Game.Pathfind.PathFlags state, int elementIndex)
        => state == attempt.WrittenPathState && elementIndex == attempt.OriginalElementIndex;

    // VehicleUtils.SetupPathfind consumes our Obsolete, sets Pending and preserves all other
    // clean flags. Navigation buffers may then be cleared by vanilla before CompleteSetup.
    public static bool IsExpectedSetup(in EnforcementAttempt attempt, Game.Pathfind.PathFlags state)
        => state == (attempt.OriginalPathState | Game.Pathfind.PathFlags.Pending);

    // Conservative admission, not a claim of equivalence to every vanilla AI.
    // Do not interrupt an append request or an unconsumed result.
    public static bool CanRequestReroute(Game.Pathfind.PathFlags state)
        => (state & (Game.Pathfind.PathFlags.Obsolete | Game.Pathfind.PathFlags.DivertObsolete)) == 0 &&
           (state & (Game.Pathfind.PathFlags.Pending | Game.Pathfind.PathFlags.Scheduled |
                     Game.Pathfind.PathFlags.Append | Game.Pathfind.PathFlags.Updated |
                     Game.Pathfind.PathFlags.Divert | Game.Pathfind.PathFlags.CachedObsolete |
                     Game.Pathfind.PathFlags.Failed | Game.Pathfind.PathFlags.Stuck)) == 0;

    /// <summary>
    /// Emergency services are exempt from RouteFilter enforcement. A city that cannot send an
    /// ambulance is already broken, and emergency vehicles are the one class whose detour cost
    /// is a public-safety cost rather than a traffic cost.
    /// </summary>
    public static bool IsExempt(RoadVehicleCategory category) => category == RoadVehicleCategory.Emergency;

}
