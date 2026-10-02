# RouteFilter 2.0 architecture

Status: `2.0.0-dev`, schema 3. Written against Cities: Skylines II **1.6.0f1**.

Every claim about vanilla behaviour below was read out of a decompile of `Game.dll`
(`Game.Pathfind.*`, `Game.Net.*`, `Game.Simulation.*`, `Game.Vehicles.*`,
`Colossal.Serialization.Entities.*`) rather than inferred from field names.

## 1. The four non-negotiables

Correctness, performance, save compatibility and recoverability outrank feature count and outrank
100% enforcement compliance. The failure mode is always the same: **grandfather through**. A vehicle
that RouteFilter cannot handle cleanly drives on. RouteFilter never

- stops a vehicle, reverses one, teleports one, or despawns one;
- writes a lane or track field it cannot prove it still owns;
- retries a reroute, queues a request, or extends a lifetime;
- persists anything derived from runtime state.

## 2. Data flow

```
 player configuration (persistent)
   NodeAssetRestrictionV1 / SegmentAssetRestrictionV1 + RestrictedVehicleAssetV1
                    |
                    |  RestrictionIndexSystem   (managed, revision-driven)
                    v
   topology: DirectedEntryGate per (target, entry lane -> next lane)
                    |
                    v
   RestrictionCandidateSystem  (Burst)  watched entry-lane LaneObjects -> CandidateMatch
                    |
                    v
   RestrictionSafetySystem      (Burst)  CandidateMatch -> RerouteSafetyEvaluation
                    |
        +-----------+-----------+
        v                       v
 RoadEnforcementCoordinator  RailEnforcementBackend
 (lease + one reroute)       (one reroute, no graph mutation)
```

Everything below the configuration line is derived. Delete it at any moment and it is rebuilt from
the configuration on the next frame.

## 3. Source of truth

| Layer | Owner | Lifetime |
| --- | --- | --- |
| Restriction configuration | player, persisted | persistent |
| Topology (`DirectedEntryGate`, `DirectedTrackGate`) | `RestrictionIndexSystem` / `RailEnforcementBackend` | derived, revision-stamped |
| Candidate | `RestrictionCandidateSystem` | one frame |
| Safety verdict | `RestrictionSafetySystem` | one frame |
| Attempt, lease | the backend that created it | bounded by absolute deadline |
| Diagnostics | `RouteFilterDiagnosticsSystem` | observational |

`Revision` increments only when the restriction configuration really changes. Every derived stage
compares against it and discards its own output when it is stale; there is no path where a changed
restriction is enforced against a stale candidate.

## 4. Road backend

### Detection
Watched set is the union of every `DirectedEntryGate.m_EntryLane`. The candidate job walks those
lanes' `LaneObject` buffers, canonicalises each physical entity up the `Controller` chain (depth
capped at four hops, then verified to be a terminal controller), and rejects anything without `Car`
and `CarCurrentLane`. A tram is a `Train` with `TrainCurrentLane` and therefore cannot appear here;
that is the tram ownership rule (§ Tram, below).

### Emergency Protection
`PoliceCar`, `Ambulance`, `FireEngine` and `Hearse` are dropped **before** the gate walk, the prefab
match and the navigation validation, not after. `CandidateDiagnosticCounters.m_EmergencyExempt`
counts them so the saving is visible rather than assumed.

### Safety model
`SafeToAttemptRerouteEvaluator` is pure. The decision point is the end of the current entry-lane
traversal, which is the first instant at which the vehicle is committed to the target. Required
distance is

```
required = speed * latencyBudget
         + 0.5 * speed^2 / braking
         + speed * 4/15                     (vanilla's own term, CarNavigationSystem)
         + vehicleLength
         + uncertaintyMargin
```

and a reroute is issued only when `distanceToGateAnchor > required`, strictly.

`latencyBudget` and `uncertaintyMargin` are **conservative assumptions, not measurements**. They
default to 1.0 s and 5 m and are exposed in Settings. Over-estimating makes RouteFilter refuse
more often, which is the safe direction: it costs one grandfathered vehicle instead of one
too-late reroute. `SafetyConfidence.Instrumenting` no longer exists in the runtime path; the only
reason a verdict is not `Safe` is an actual measured fact.

### Reroute
The single write is `PathOwner.m_State |= PathFlags.Obsolete`. This is the canonical primitive:
every vanilla AI system gates its pathfind on `Game.Vehicles.VehicleUtils.RequireNewPath`, which is
true exactly when `Obsolete` or `DivertObsolete` is set and `Pending`, `Scheduled`, `Failed` and
`Stuck` are clear. `EnforcementPolicy.CanRequestReroute` refuses in exactly those busy states, so
RouteFilter never writes a flag that vanilla would silently ignore.

Exactly one attempt exists per (vehicle, target) pair. There is no retry, no cooldown ladder, no
request queue and no reference counting. The attempt has an absolute deadline; after it, the attempt
is simply closed as unresolved and the vehicle is never touched again for that target.

### Lane lease
`CarLane.m_BlockageStart/End` is written to `0/1` on the gate's *next* lane (the lane inside the
restricted target). Verified properties:

- `Game.Net.CarLane` is `ISerializable` and **writes the blockage interval**, so an active lease is
  save-visible. This is why release happens in the `Serialize` phase, ordered before
  `Game.Serialization.SerializerSystem`, and again in `OnGamePreload` and `OnDispose`.
- `Game.Pathfind.PathUtils.GetCarDriveSpecification` turns a non-empty interval into
  `RuleFlags.HasBlockage` on the published edge, and
  `Game.Pathfind.PathfindJobs.IsValidDelta` then rejects **every** traversal overlapping it for
  **every** seeker that does not ignore the rule. The mutation is coarse: it is not scoped to one
  prefab and it is not scoped to one vehicle.
- `Game.Pathfind.LaneDataSystem.CheckBlockage` recomputes the interval from non-moving
  `LaneObject`s whenever a lane carries `Updated`, which is the same flag that publishes the change
  into the pathfind graph. RouteFilter's write is therefore short-lived and is normally erased by
  vanilla on the following frame.

Ownership rules, all enforced:

| Rule | Behaviour |
| --- | --- |
| Acquire | only when the lane's current interval is vanilla-empty (`start > end`) |
| Re-assert | only when vanilla has erased our value back to empty; never over a real blockage |
| Yield | give the lane up permanently if another owner published a real blockage |
| Release | only when the current interval is exactly what we wrote |
| Expire | absolute frame deadline; never extended, never renewed |
| Budget | city-wide ceiling of lane publications per second; over budget means grandfather, not queue |

`RoadLeaseRules` contains all of this as pure functions and is covered by the offline fixture.

## 5. Rail backend

Rail is a separate system with its own gate type, its own candidate job and its own attempt store.
It shares only restriction semantics, prefab matching, revision, persistence, settings, diagnostics
conventions and UI. See `RAIL_ENFORCEMENT_DESIGN.md` for the reverse-engineered mechanism and for
the verified technical limitation that shapes it.

## 6. Tram and fixed-route ownership

- A tram is a `Train` with `TrainCurrentLane`. Road detection requires `Car` + `CarCurrentLane`, so
  a tram is structurally invisible to the road backend even on a lane entity that also carries
  `CarLane` (the shared-lane case that `GetCarDriveSpecification` overload with `TrackLane` exists
  for). One backend per canonical entity, by construction, not by election.
- Bus, tram, train, subway and freight vehicles on a player-drawn line carry
  `Game.Pathfind.PathInformation`, whose origin and destination are owned by vanilla. Their route is
  redrawn by their own AI every tick, so a RouteFilter reroute would be undone immediately and a
  failure would damage a whole line. Both backends refuse them and report
  `EnforcementRefusalReason.FixedRoute`. RouteFilter never redraws a player's line.

## 7. Persistence

`SAVE_FORMAT.md` has the byte layout. The architectural rule is one sentence: **save player intent,
never implementation state.** A future 3.0 that replaces the entire enforcement engine still reads
"these prefabs are forbidden at these targets" and nothing else.

## 8. Legacy

`VehicleAccessSystem`, `RestrictionPathSystem`, `VehicleDetourSystem` and the 1.x runtime
components (`AccessDetourBlock`, `VehicleDetourRequest`, `RerouteCooldown`) are removed. They were
never `ISerializable`, so no city ever carried them and no migration shim is needed. RouteFilter
does not run a startup full-city scan to clean up historical pollution; that belongs to the
standalone `RouteFilterCleanup` mod, because a per-load city-wide scan reintroduces exactly the
performance, safety and ownership risks this rewrite exists to remove.
