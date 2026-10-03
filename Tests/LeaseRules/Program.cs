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

// ROAD-NO-ALTERNATIVE-001 / 无合法出口：仅完整、归属正确的耗尽查询允许移除。
var exhausted = new RoadQueryProof { CompleteExclusion = true, ValidEndpoints = true,
    QueryCompleted = true, EmptyPath = true, SearchExhausted = true, BaselineCrossesTarget = true, ExactNoRouteScope = true };
Check(EnforcementPolicy.ClassifyRoadQuery(exhausted) == RoadQueryOutcome.ConfirmedNoAlternative,
    "ROAD-NO-ALTERNATIVE-001: owned exhaustive exclusion has a distinct no-route terminal");
// ROAD-ALTERNATIVE-001 / 有替代道路：成功路径必须绕开目标，不能进入删除终态。
var alternative = exhausted; alternative.EmptyPath = false; alternative.AvoidsTarget = true;
Check(EnforcementPolicy.ClassifyRoadQuery(alternative) == RoadQueryOutcome.AlternativePathFound,
    "ROAD-ALTERNATIVE-001: an avoiding path is rerouted, never removed");
var crossingPath = alternative; crossingPath.AvoidsTarget = false;
Check(EnforcementPolicy.ClassifyRoadQuery(crossingPath) == RoadQueryOutcome.EnforcementUncertain,
    "ROAD-ALTERNATIVE-001: a path still crossing the target is not alternative/no-route evidence");
// ROAD-UNCERTAIN-001 / 证据不足：费用截断、无效端点、不完整排除、异常、冲突全部放行。
for (var variant = 0; variant < 10; variant++)
{
    var uncertain = exhausted;
    switch (variant)
    {
        case 0: uncertain.CompleteExclusion = false; break;
        case 1: uncertain.ValidEndpoints = false; break;
        case 2: uncertain.QueryCompleted = false; break;
        case 3: uncertain.SearchExhausted = false; break;
        case 4: uncertain.Cancelled = true; break;
        case 5: uncertain.Exception = true; break;
        case 6: uncertain.RestoreConflict = true; break;
        case 7: uncertain.EmptyPath = false; break;
        case 8: uncertain.BaselineCrossesTarget = false; break;
        case 9: uncertain.ExactNoRouteScope = false; break;
    }
    Check(EnforcementPolicy.ClassifyRoadQuery(uncertain) == RoadQueryOutcome.EnforcementUncertain,
        $"ROAD-UNCERTAIN-001: insufficient proof variant {variant} must not delete");
}
var liveAttempt = new EnforcementAttempt { Backend = EnforcementBackend.Road, QueryIntercepted = true,
    Generation = 8, Vehicle = new Unity.Entities.Entity { Index = 100, Version = 2 },
    Target = new Unity.Entities.Entity { Index = 200, Version = 3 }, RestrictionRevision = 9,
    MatchedPrefab = new Unity.Entities.Entity { Index = 300 }, OwnedLane = new Unity.Entities.Entity { Index = 400 },
    GateEntryLane = new Unity.Entities.Entity { Index = 500 }, NativeDestination = new Unity.Entities.Entity { Index = 600 } };
Check(EnforcementPolicy.OwnsRoadReceipt(liveAttempt, liveAttempt), "exact owned receipt is accepted");
var changedPhysical = liveAttempt;
changedPhysical.PhysicalVehicle = new Unity.Entities.Entity { Index = 987, Version = 1 };
Check(!EnforcementPolicy.OwnsRoadReceipt(liveAttempt, changedPhysical), "road receipt cannot substitute physical prefab evidence");
for (var variant = 0; variant < 8; variant++)
{
    var stale = liveAttempt;
    switch (variant)
    {
        case 0: stale.Generation++; break;
        case 1: stale.Vehicle.Version++; break;
        case 2: stale.Target.Version++; break;
        case 3: stale.RestrictionRevision++; break;
        case 4: stale.MatchedPrefab.Index++; break;
        case 5: stale.OwnedLane.Index++; break;
        case 6: stale.GateEntryLane.Index++; break;
        case 7: stale.NativeDestination.Index++; break;
    }
    Check(!EnforcementPolicy.OwnsRoadReceipt(liveAttempt, stale), $"ROAD-UNCERTAIN-001: foreign receipt {variant}");
}
var endedAttempt = liveAttempt; endedAttempt.State = EnforcementAttemptState.Grandfathered;
Check(!EnforcementPolicy.OwnsRoadReceipt(endedAttempt, liveAttempt), "ended attempt cannot accept a late no-route result");
var notIntercepted = liveAttempt; notIntercepted.QueryIntercepted = false;
Check(!EnforcementPolicy.OwnsRoadReceipt(notIntercepted, liveAttempt), "generic Failed/no intercepted query is never deletion evidence");
var railReceipt = liveAttempt; railReceipt.Backend = EnforcementBackend.Rail;
Check(EnforcementPolicy.OwnsRailReceipt(railReceipt, railReceipt), "RAIL-NO-ALTERNATIVE-001: exact owned rail receipt");
Check(!EnforcementPolicy.OwnsRailReceipt(railReceipt, liveAttempt), "rail rejects road receipt");
Check(!EnforcementPolicy.OwnsRoadReceipt(liveAttempt, railReceipt), "road rejects rail receipt");
for (var variant = 0; variant < 10; variant++)
{
    var stale = railReceipt;
    switch (variant)
    {
        case 0: stale.Generation++; break;
        case 1: stale.Vehicle.Version++; break;
        case 2: stale.Target.Version++; break;
        case 3: stale.RestrictionRevision++; break;
        case 4: stale.MatchedPrefab.Index++; break;
        case 5: stale.OwnedLane.Index++; break;
        case 6: stale.GateEntryLane.Index++; break;
        case 7: stale.NativeDestination.Index++; break;
        case 8: stale.ViaLane.Index++; break;
        case 9: stale.ViaLane2.Index++; break;
    }
    Check(!EnforcementPolicy.OwnsRailReceipt(railReceipt, stale), $"RAIL-UNCERTAIN-001: foreign receipt {variant}");
}
Check(EnforcementPolicy.ClassifyRoadQuery(exhausted) == RoadQueryOutcome.ConfirmedNoAlternative, "RAIL-NO-ALTERNATIVE-001: complete exhaustive proof");
Check(EnforcementPolicy.ClassifyRoadQuery(alternative) == RoadQueryOutcome.AlternativePathFound, "RAIL-ALTERNATIVE-001: never remove an avoiding result");
var broadTrackScope = exhausted; broadTrackScope.ExactNoRouteScope = false;
Check(EnforcementPolicy.ClassifyRoadQuery(broadTrackScope) == RoadQueryOutcome.EnforcementUncertain, "rail two-way outbound exclusion is not deletion proof");
Console.WriteLine("PASS: rail receipt identity, cross-backend isolation, and shared alternative/no-route/uncertain policy. Live game test remains required.");
Console.WriteLine("PASS: ROAD-NO-ALTERNATIVE-001 / ROAD-ALTERNATIVE-001 / ROAD-UNCERTAIN-001 policy and receipt fixtures. GAME TEST STILL REQUIRED.");

Console.WriteLine("PASS: 65,536 lane ownership pairs, vanilla empty interval, exact expiry, frame wrap, " +
                  "ended ownership, acquire-only-on-empty, reroute admission against every PathFlags " +
                  "combination that matters, emergency exemption, exact PathOwner ownership and frame-wrap behaviour.");
Console.WriteLine("NOT TESTED HERE: Unity jobs, ECS scheduling, the game's SerializerSystem, and live " +
                  "pathfinding. Those need the game running.");

// Reproduced case: 70 m to gate at 50 m/s was blocked by a 253 m stop budget.
Check(EnforcementPolicy.CanRunQueryBeforeGate(70.15f), "query-only 70.15 m approach is reachable");
foreach (var distance in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
    Check(!EnforcementPolicy.CanRunQueryBeforeGate(distance), "query-only rejects at/past/unknown gate");
var reversedRail = railReceipt;
reversedRail.NavigationVehicle = new Unity.Entities.Entity { Index = 701, Version = 2 };
Check(EnforcementPolicy.OwnsRailReceipt(reversedRail, reversedRail), "reversed rail front may differ from path owner");
var changedFront = reversedRail; changedFront.NavigationVehicle.Version++;
Check(!EnforcementPolicy.OwnsRailReceipt(reversedRail, changedFront), "changed rail front invalidates no-route receipt");
Console.WriteLine("PASS: query-only before-gate admission and independent rail physical-front receipt.");
