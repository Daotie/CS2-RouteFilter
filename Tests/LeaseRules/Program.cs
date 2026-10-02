using Game.Pathfind;
using RouteFilter.Components;

static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }

// ---------------------------------------------------------------- lane lease ownership

// Vanilla represents an empty interval by start > end and writes 255/0 for lanes that never had
// blockage data, so "empty" must not be hard-coded.
Check(RoadLeaseRules.IsEmpty(255, 0), "vanilla empty interval");
Check(!RoadLeaseRules.IsEmpty(0, 0), "0/0 must not be assumed empty");
Check(!RoadLeaseRules.IsEmpty(0, 255), "full blockage belongs to an existing owner");
Check(RoadLeaseRules.CanAcquire(255, 0), "empty lane can be leased");
Check(RoadLeaseRules.CanAcquire(10, 3), "inverted interval counts as empty");
Check(!RoadLeaseRules.CanAcquire(0, 255), "occupied lane must never be taken over");

Check(!EnforcementPolicy.HasExpired(99, 100), "before expiry");
Check(EnforcementPolicy.HasExpired(100, 100), "absolute expiry inclusive");
Check(EnforcementPolicy.HasExpired(101, 100), "after expiry");
Check(!EnforcementPolicy.HasExpired(uint.MaxValue, 1), "frame wrap before expiry");
Check(EnforcementPolicy.HasExpired(1, 1), "frame wrap expiry");

var lease = new RoadLaneLease
{
    OriginalBlockageStart = 255,
    OriginalBlockageEnd = 0,
    WrittenBlockageStart = 0,
    WrittenBlockageEnd = 1,
    State = RoadLeaseState.Active
};
var exactMatches = 0;
for (var start = 0; start <= 255; start++)
for (var end = 0; end <= 255; end++)
{
    var owns = RoadLeaseRules.OwnsCurrent(lease, (byte)start, (byte)end);
    Check(owns == (start == 0 && end == 1), "must reject every conflicting pair");
    if (owns) exactMatches++;
}
Check(exactMatches == 1, "only the exact written interval is restored");
lease.State = RoadLeaseState.RestoreConflict;
Check(!RoadLeaseRules.OwnsCurrent(lease, 0, 1), "ended ownership cannot restore");
Check(lease.OriginalBlockageStart == 255 && lease.OriginalBlockageEnd == 0, "original state retained");

// ---------------------------------------------------------------- reroute admission

Check(EnforcementPolicy.CanRequestReroute((PathFlags)0), "idle path can be rerouted");
// VehicleUtils.RequireNewPath refuses while Pending, Failed or Stuck is set, so writing Obsolete
// in those states would be a silent no-op that still marks the vehicle as handled.
Check(!EnforcementPolicy.CanRequestReroute(PathFlags.Pending), "pending path is not requestable");
Check(!EnforcementPolicy.CanRequestReroute(PathFlags.Failed), "failed path is not requestable");
Check(!EnforcementPolicy.CanRequestReroute(PathFlags.Stuck), "stuck path is not requestable");
Check(!EnforcementPolicy.CanRequestReroute(PathFlags.Scheduled), "scheduled path is not requestable");
// Writing Obsolete again would be a duplicate request for the same vehicle.
Check(!EnforcementPolicy.CanRequestReroute(PathFlags.Obsolete), "already obsolete is not re-requested");
Check(!EnforcementPolicy.CanRequestReroute(PathFlags.DivertObsolete), "divert-obsolete is not re-requested");
Check(!EnforcementPolicy.CanRequestReroute(PathFlags.Updated | PathFlags.Append),
    "append or unconsumed result is not a supported reroute state");

// ---------------------------------------------------------------- category exemption

Check(EnforcementPolicy.IsExempt(RoadVehicleCategory.Emergency), "emergency services are exempt");
foreach (RoadVehicleCategory category in Enum.GetValues(typeof(RoadVehicleCategory)))
{
    if (category == RoadVehicleCategory.Emergency) continue;
    Check(!EnforcementPolicy.IsExempt(category), $"{category} must not be exempt");
}

// Actual runtime compare-before-restore contract, including native changes to unrelated flags.
var attempt = new EnforcementAttempt { OriginalPathState = PathFlags.Updated,
    WrittenPathState = PathFlags.Updated | PathFlags.Obsolete, OriginalElementIndex = 7 };
for (var state = 0; state < 65536; state++)
{
    var owns = EnforcementPolicy.OwnsRequest(attempt, (PathFlags)state, 7);
    Check(owns == ((PathFlags)state == attempt.WrittenPathState), "full flags snapshot required");
    Check(!EnforcementPolicy.OwnsRequest(attempt, (PathFlags)state, 8), "changed path element never restored");
}
var setup = new EnforcementAttempt { OriginalPathState = 0, WrittenPathState = PathFlags.Obsolete };
Check(EnforcementPolicy.IsExpectedSetup(setup, PathFlags.Pending), "native consumes Obsolete then clears navigation");
Check(!EnforcementPolicy.IsExpectedSetup(setup, PathFlags.Pending | PathFlags.Updated), "replacement result is not our setup");
Check(!EnforcementPolicy.IsExpectedSetup(setup, PathFlags.Pending | PathFlags.Append), "append is not our setup");
Check(!EnforcementPolicy.IsExpectedSetup(setup, PathFlags.Obsolete), "query has not consumed our request");

Console.WriteLine("PASS: 65,536 lane ownership pairs, vanilla empty interval, exact expiry, frame wrap, " +
                  "ended ownership, acquire-only-on-empty, reroute admission against every PathFlags " +
                  "combination that matters, emergency exemption, exact PathOwner ownership and frame-wrap behaviour.");
Console.WriteLine("NOT TESTED HERE: Unity jobs, ECS scheduling, the game's SerializerSystem, and live " +
                  "pathfinding. Those need the game running.");
