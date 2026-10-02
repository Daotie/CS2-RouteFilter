# Rail enforcement design

Everything here was read from a decompile of `Game.dll` 1.6.0f1, not inferred from names.

## 1. What a rail restriction minimally has to express

"Consists whose locomotive prefab is in the forbidden set must not enter this target." The minimum
target is the same as for road: a `Node` or an `Edge`. What differs is that the *mechanism* has no
graph primitive at all (§7), so the only lever is the vehicle's own path state.

## 2. How a consist is identified

`Game.Vehicles.Train` on a physical entity, `Game.Vehicles.TrainCurrentLane` for position, and
`Game.Vehicles.Train` + `TrainCurrentLane` present on the canonical entity. Parked trains
(`ParkedTrain`) and deleted/temp entities are rejected before anything else.

Detection reuses the road approach: walk the `LaneObject` buffer of a watched **track** entry lane.
Every carriage of a consist occupies its own `LaneObject` on its own track lane, so the raw scan
sees the whole consist.

## 3. Canonicalisation, and why it is mandatory

`Game.Simulation.TrainNavigationSystem` reads `Target`, `PathOwner` and the navigation buffers from
`LayoutElement[0].m_Vehicle` of the chunk entity. Every other member of the consist has its own
`TrainCurrentLane` but no path ownership.

RouteFilter therefore canonicalises by two independent means and requires both to agree:

1. walk the `Controller.m_Controller` chain (depth-capped at four, cycle-checked) to the terminal
   controller, which for a train is the locomotive;
2. require `LayoutElement[0].m_Vehicle == canonical`.

If they disagree the entity is refused, not guessed.

**Consequence:** a ten-car train produces exactly one candidate, one safety evaluation and at most
one reroute. Cost is O(cars) only in the one place that reads `LayoutElement` for consist length,
which is required for the braking-distance model and is bounded by consist length.

## 4. Direction

Track lanes carry `Lane` (`m_StartNode`, `m_EndNode`), and `TrackLaneFlags.Twoway` decides how many
directed traversals a lane has. A `DirectedTrackGate` is emitted only when the adjacent lane's
`m_EndNode` equals the internal lane's `m_StartNode`, so a gate is a genuinely directed transition
and not "the two lanes are near each other". For a two-way track the reverse direction produces a
second, independent gate, so a consist travelling the other way is matched by the correct one.

## 5. Navigation validation

The canonical entity's `TrainCurrentLane.m_Front.m_Lane` must equal the gate's entry lane, and
`TrainNavigationLane[0].m_Lane` must equal the gate's next lane with a non-zero forward delta.
`TrainLaneFlags.Obsolete`, `Return`, `ParkingSpace` and `Connection` are refused. This is the exact
analogue of the road check, with rail's own flag set.

## 6. What the reroute primitive is

The same as road: `PathOwner.m_State |= PathFlags.Obsolete` on the canonical entity.
`Game.Vehicles.VehicleUtils.SetupPathfind(ref TrainCurrentLane, ref PathOwner, ...)` is the rail
overload that clears the flags, sets `Pending` and enqueues; `VehicleUtils.RequireNewPath` gates it.

`Game.Simulation.TrainNavigationSystem.UpdateNavigationLanes` shows the recovery path: with
`Obsolete` set and `Append` clear, the navigation lanes are cleared and the existing `PathElement`
buffer is kept until vanilla produces a new path. That is a bounded, vanilla-owned transition, and
RouteFilter never writes `TrainCurrentLane`, `TrainNavigation`, `PathElement` or any motion state.

## 7. There is no rail blockage primitive — verified

| Claim | Evidence |
| --- | --- |
| `TrackLane` has no blockage interval | `Game.Net/TrackLane.cs` has exactly `m_AccessRestriction`, `m_Flags`, `m_SpeedLimit`, `m_Curviness` |
| Track edges never carry `HasBlockage` | `Game.Pathfind/PathUtils.GetTrackDriveSpecification` never sets `RuleFlags.HasBlockage`, and never copies a blockage interval into `PathSpecification` |
| Therefore `IsValidDelta` never rejects a track edge | `Game.Pathfind/PathfindJobs.cs` only applies the blockage test when the edge spec has the flag |
| Nothing else is safe to hijack | `LaneDataSystem` recomputes `m_AccessRestriction`, `m_Flags` (partly), `m_SpeedLimit` and `m_Curviness` from prefab data and policies whenever a lane carries `Updated`; `TrackLane` is `ISerializable`, so any value written would persist in the save |

Conclusion: **RouteFilter performs zero TrackLane writes.** There is no faked primitive, no
zeroed speed limit, no borrowed access restriction.

This is a good outcome, not only a constraint: because the rail backend owns no graph state, it
**cannot contaminate a save**. The rail active-mutation save test degenerates to "there is nothing
to leak", which is the strongest form of that gate.

## 8. Enforcement, therefore, is reroute-only

For a forbidden consist approaching a restricted target:

1. canonicalise, match the forbidden prefab set, validate navigation, evaluate safety;
2. if the safety model says the train still has room, write `Obsolete` once;
3. vanilla pathfinds. If an alternative exists it is taken. If not, vanilla sets `Failed`/`Stuck`,
   the train stops on vanilla's own terms, RouteFilter closes the attempt as unresolved and never
   touches it again.

The rail latency budget is `max(RerouteLatencySeconds, 2.5 s)` because a train is slower to react and
its pathfind is more expensive. Over-refusing costs one grandfathered train; under-refusing costs a
train committed to track it cannot leave.

## 9. Fixed lines

`Game.Pathfind.PathInformation` present on the canonical entity means a line or dispatch owns the
route. Rail refuses these outright and reports `RailRefusedFixedRoute`. Player-drawn lines are never
redrawn, never invalidated and never broken.

## 10. Freight, passenger, subway, tram

They share `Train`/`TrainCurrentLane`/`TrainNavigationSystem`, so detection and the reroute mechanism
are identical. Behaviour differs only through vanilla's own data: `TrainData.m_TrackType` selects
the track class, `TrainFlags.Reversed` selects travel direction, and passenger/freight differences
appear in boarding and depot logic that RouteFilter never touches. No separate code path per mode is
needed, and none exists.

## 11. Answers to the questions that had to be answered first

1. **Minimal target?** A `Node` or `Edge`, identical to road.
2. **Identify an approaching consist?** `LaneObject` on a watched `TrackLane`, then canonicalisation.
3. **Canonicalise?** Controller chain **and** `LayoutElement[0]`, both must agree.
4. **Avoid per-carriage cost?** Yes - one candidate, one safety evaluation, one attempt per consist.
5. **Direction?** Directed `DirectedTrackGate` from `Lane` node adjacency plus `TrackLaneFlags.Twoway`.
6. **Make vanilla avoid the target?** Only by requesting a new path. There is no graph primitive (§7).
7. **Rail equivalent of blockage?** None exists. Verified, not assumed.
8. **Graph publication?** Not applicable. RouteFilter publishes nothing for rail.
9. **Fixed PT route with no alternative?** Refused before any write, so the question never arises.
10. **Freight/passenger/subway/tram consistent?** Yes; one code path.
11. **Can failure cancel a line?** No - RouteFilter never writes line state, and it refuses
    line-managed vehicles.
12. **Runtime mutation ownership?** No lane state owned at all; only bounded vehicle path requests.
13. **Bounded release?** Attempts have absolute deadlines and are removed; there is nothing to restore.
14. **Save contamination?** Structurally impossible for rail: no `TrackLane` write exists.

## 12. Blocked by verified technical limitation

**Rail graph-level avoidance is blocked.** Evidence is §7. The impact is that a forbidden train is
diverted only if vanilla's own pathfinder already prefers an alternative when asked to re-plan. In a
network where the restricted target is the only route, the train is grandfathere through. The
alternative would be to write `TrackLane` fields, which the decompile shows are both recomputed and
serialised; that would trade a known, documented limitation for an unknown save-corruption and
lane-state-contamination risk, which is exactly the trade this project refuses.
