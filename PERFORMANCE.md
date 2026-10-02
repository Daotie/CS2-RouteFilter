# RouteFilter performance

All numbers below are **work counts**, not guesses. The counters that produce them are in
`RouteFilterDiagnosticsSystem`; turn on `Verbose diagnostics` or press `Log current statistics` in
Settings to read them. No claim below has been validated with an in-game profiler; see the status
table at the end.

## 1. Per-system budget

| System | Workload grows with | Fast path | Complexity | Allocations | Job dependencies | Main-thread sync | Structural changes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `RestrictionIndexSystem` | restriction changes, net rebuilds | 0 targets + 0 restricted entities: two cached emptiness checks, no snapshot, no log | O(targets x lanes per target) on rebuild only | none (managed pools reused) | none | none | none |
| `RestrictionCandidateSystem` | watched road entry lanes + their `LaneObject`s | 0 watched lanes: returns before scheduling | O(watched lanes + LaneObjects) per frame | none | 1 chained job | none | none |
| `RestrictionSafetySystem` | new candidates | no new candidates: returns before scheduling | O(new candidates + concurrent approaches/64) | none | 1 job chained after detection | none | none |
| `RoadEnforcementCoordinator` | new safe candidates + active leases + active attempts | no leases, no attempts, no evaluations: returns before scheduling | O(new safe candidates + leases + attempts); all three hard-capped at 32/64 | none | 1 job chained after safety | none | `Updated` add per publish, capped at 8 per second |
| `RailEnforcementBackend` | watched rail entry lanes + their `LaneObject`s | 0 watched lanes: returns before scheduling | same shape as road, consist-canonicalised | none | 2 jobs chained | none | **none, ever** |
| `RouteFilterDiagnosticsSystem` | number of backends | samples once per 5 s | O(1) | one string per interval only when logging is on | none | none | none |
| `RouteFilterUISystem` | player interaction | binding updates only, O(1) | catalog poll every 30 frames, not per frame | only on actual catalog change | none | none | none |
| `RestrictionOverlaySystem` | active tool only | returns unless the tool is active | bounded: 128 lanes / 256 objects / 32 vehicles / 256 elements / 1024 curves | none | 1 job | none | none |

No system scans vehicles, lanes, track lanes, trains or consists by archetype. No system runs LINQ,
allocates a `List`/`Dictionary`/`HashSet`, formats a string, or calls `JobHandle.Complete()` on the
simulation hot path.

## 2. What was removed and why it mattered

### Quadratic lookups in the candidate detector (fixed)
`ContainsMatch`, `GetFirstSeenFrame` and `ContainsEntity` were linear scans over per-frame lists.
With one restriction this is invisible. With 100 targets the watched set is hundreds of lanes with
tens of vehicles each, so the dedupe and first-seen lookups alone are O(n^2) in traffic density -
the classic "one restriction is fine, a hundred restrictions melt the frame" failure. They are now
`NativeParallelHashSet` / `NativeParallelHashMap` lookups.

### Per-observation sweep in the safety stage (fixed)
`ObserveTrackedPathStates` walked **every retained observation every frame**, so cost grew with the
number of vehicles that had ever passed a gate rather than with the number currently approaching
one. Pending transitions are now observed inside the candidate loop, and the map is swept every 64
frames.

### `JobHandle.Complete()` for diagnostics (removed)
`GetCategoryVerdicts` and `GetLatencyPercentiles` synchronised the job purely so a UI refresh could
read counters. They now return managed mirrors that are refreshed inside `ProcessDiagnostics`, which
only runs when the job is already finished. No consumer can stall the simulation thread for a number.

### Per-frame catalog poll in the UI (gated)
`RouteFilterUISystem.OnUpdate` walked the prefab query's chunks every frame, including while paused.
It is now sampled every 30 frames; all explicit refresh paths still force an immediate rebuild.

## 3. The 130 -> 32 FPS report

**Status: root cause identified structurally; in-game confirmation NOT TESTED.**

There are three real mechanisms in the decompiled game that can turn "restriction enabled" into a
frame-time collapse, and the old 1.x design hit all three:

1. **Non-moving objects create lane blockage.** `Game.Pathfind.LaneDataSystem.CheckBlockage` marks a
   lane interval blocked for every `LaneObject` that lacks `Moving`. A stopped vehicle in front of a
   restriction therefore blocks the lane.
2. **Blockage is a hard pathfinding rule for everyone.**
   `Game.Pathfind.PathUtils.GetCarDriveSpecification` turns a non-empty interval into
   `RuleFlags.HasBlockage`, and `Game.Pathfind.PathfindJobs.IsValidDelta` rejects every overlapping
   traversal for every seeker that does not ignore the rule. One stopped vehicle changes routing for
   the whole city, and `PathfindTargetSeeker` additionally reroutes anything whose path touches it.
3. **Each lane write republishes the graph.** Adding `Updated` selects the lane in
   `LaneDataSystem` and in `LanesModifiedSystem`, which invalidates paths. The old
   `AccessDetourBlock` kept a barrier alive with a request count and a quiet-tick grace period, so a
   steady stream of matching vehicles kept a lane in the invalidated set continuously. That is the
   single most plausible mechanism for a sustained multi-year drop, and it is exactly the "sticky
   barrier" this rewrite forbids.

RouteFilter 2.0 removes all three levers:

- RouteFilter never stops a vehicle, so it never creates blockage through `CheckBlockage`.
- leases are acquired **only** on a lane vanilla currently considers empty, so a lease never
  displaces a real blockage;
- leases have an absolute 30-frame lifetime, are never extended, and are re-asserted only while
  vanilla's own value is still empty;
- a city-wide ceiling of 8 lane publications per second bounds the republish rate no matter how much
  traffic is affected, and over-budget requests are dropped rather than queued;
- the old `AccessDetourBlock` request-count/quiet-grace barrier is deleted.

## 4. What still has to be measured in game

The counters exist; the run does not. Before claiming a performance result, capture, for each row of
the matrix below, one `Log current statistics` line plus the game's own system timing:

| Case | Targets | Notes |
| --- | --- | --- |
| P0 | 0 | release gate: `watchedEntryLanes=0`, `laneObjects=0`, `reroutes=0`, `graphMutations=0` |
| P1 | 1 road | |
| P2 | 10 road | |
| P3 | 100 road | the case that exposed the quadratic lookups |
| P4 | dense road traffic | |
| P5 | highway throughput | |
| P6 | 1 rail | |
| P7 | multiple rail | |
| P8 | long consists | verify one candidate per consist, not per carriage |
| P9 | road + rail mixed | |
| P10 | large realistic city | |
| P11 | stress | |
| P12 | long soak | memory and all runtime state must return to baseline |

For each: FPS, simulation speed, main-thread time, RouteFilter system times, allocations, and the
work counts. **FPS alone is not evidence** - it moves with the GPU and with everything else in the
build. The attribution argument is `work counts x measured per-unit cost`, which is why every
system above documents its workload.

## 5. Native container growth

Every native container is persistent and reused; none is disposed and recreated per frame. The only
growth-capable stores are the observation maps and lists, and both are pruned: candidate
observations after 1024 unseen frames, safety observations after 4096 unseen frames, and both sweeps
are amortised (every 256 and every 64 frames). Attempt and lease stores are hard-capped at 64 and 32
and are never grown past the cap - a full store drops the request for that frame instead of
allocating.

## 6. Memory

Runtime memory is proportional to active restrictions, watched topology, and concurrent approaches.
It is not proportional to session length. `RouteFilterDiagnosticsSystem` reports `activeRoadLeases`,
`activeRoadAttempts` and `activeRailAttempts` every interval so this is checkable rather than
asserted.

## 7. Status

| Item | Status |
| --- | --- |
| Algorithmic complexity review of every system | STATICALLY VERIFIED |
| Removal of per-frame `Complete()` from diagnostics and UI paths | STATICALLY VERIFIED |
| Removal of O(n^2) dedupe and observation sweeps | STATICALLY VERIFIED |
| Zero-restriction fast path in every system | STATICALLY VERIFIED |
| Debug and Release builds | BUILD VERIFIED |
| P0-P12 matrix | **NOT TESTED** |
| 130 -> 32 FPS before/after profiler evidence | **NOT TESTED** (root cause structurally identified above) |
| Long-session soak | **NOT TESTED** |
