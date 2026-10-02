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

Check(!RoadLeaseRules.HasExpired(99, 100), "before expiry");
Check(RoadLeaseRules.HasExpired(100, 100), "absolute expiry inclusive");
Check(RoadLeaseRules.HasExpired(101, 100), "after expiry");
Check(!RoadLeaseRules.HasExpired(uint.MaxValue, 1), "frame wrap before expiry");
Check(RoadLeaseRules.HasExpired(1, 1), "frame wrap expiry");

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
Check(EnforcementPolicy.CanRequestReroute(PathFlags.Updated | PathFlags.Append),
    "append/updated is still requestable");

// ---------------------------------------------------------------- category exemption

Check(EnforcementPolicy.IsExempt(RoadVehicleCategory.Emergency), "emergency services are exempt");
foreach (RoadVehicleCategory category in Enum.GetValues(typeof(RoadVehicleCategory)))
{
    if (category == RoadVehicleCategory.Emergency) continue;
    Check(!EnforcementPolicy.IsExempt(category), $"{category} must not be exempt");
}

// ---------------------------------------------------------------- fixed route refusal

Check(EnforcementPolicy.IsFixedRouteManaged(true), "a vehicle with PathInformation is line-managed");
Check(!EnforcementPolicy.IsFixedRouteManaged(false), "an ordinary vehicle is not line-managed");

// ---------------------------------------------------------------- distance model

var required = EnforcementPolicy.RequiredDistance(10f, 5f, 1f, 4.5f, 5f);
Check(required > 0f && float.IsFinite(required), "required distance is a finite positive number");
// required = speed*latency + 0.5*v^2/braking + speed*(4/15) + length + margin
var expected = 10f * 1f + 0.5f * 100f / 5f + 10f * 4f / 15f + 4.5f + 5f;
Check(System.Math.Abs(required - expected) < 1e-4f, "required distance follows the documented model");
Check(required > 10f * 1f, "latency term is included");
Check(required > 0.5f * 100f / 5f, "braking term is included");
Check(required > 4.5f, "vehicle length is included");
Check(required > 5f, "uncertainty margin is included");

Check(float.IsPositiveInfinity(EnforcementPolicy.RequiredDistance(10f, 0f, 1f, 4f, 5f)),
    "unknown braking must refuse rather than under-estimate");
Check(float.IsPositiveInfinity(EnforcementPolicy.RequiredDistance(10f, 5f, 0f, 4f, 5f)),
    "non-positive latency must refuse");
Check(float.IsPositiveInfinity(EnforcementPolicy.RequiredDistance(float.NaN, 5f, 1f, 4f, 5f)),
    "non-finite speed must refuse");
Check(float.IsPositiveInfinity(EnforcementPolicy.RequiredDistance(0f, 5f, 1f, 4f, -1f)),
    "negative margin must refuse");

// A larger latency budget must never make a vehicle look safer.
var shortBudget = EnforcementPolicy.RequiredDistance(20f, 4f, 0.5f, 5f, 2f);
var longBudget = EnforcementPolicy.RequiredDistance(20f, 4f, 3f, 5f, 2f);
Check(longBudget > shortBudget, "a longer latency budget requires more room");
Check(longBudget / 20f > shortBudget / 20f, "the latency term dominates at speed");

// Strictly greater than, so a vehicle exactly at the decision point is treated as too late.
Check(EnforcementPolicy.HasRoomToAct(required + 0.01f, required), "just enough room acts");
Check(!EnforcementPolicy.HasRoomToAct(required, required), "exactly at the decision point does not act");
Check(!EnforcementPolicy.HasRoomToAct(required - 0.01f, required), "too little room does not act");
Check(!EnforcementPolicy.HasRoomToAct(float.NaN, required), "unmeasurable distance does not act");
Check(!EnforcementPolicy.HasRoomToAct(required, float.PositiveInfinity), "infinite requirement does not act");
Check(!EnforcementPolicy.HasRoomToAct(0f, 0f), "a stopped vehicle at the anchor does not act");

// ---------------------------------------------------------------- token monotonicity

var token = 0u;
for (var i = 0; i < 1000; i++) token = EnforcementPolicy.NextToken(token);
Check(token == 1000u, "tokens are monotonic and never reused");

Console.WriteLine("PASS: 65,536 lane ownership pairs, vanilla empty interval, exact expiry, frame wrap, " +
                  "ended ownership, acquire-only-on-empty, reroute admission against every PathFlags " +
                  "combination that matters, emergency exemption, fixed-route refusal, distance model " +
                  "monotonicity and frame-wrap behaviour.");
Console.WriteLine("NOT TESTED HERE: Unity jobs, ECS scheduling, the game's SerializerSystem, and live " +
                  "pathfinding. Those need the game running.");
