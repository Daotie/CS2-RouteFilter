# RouteFilter legacy mutation audit

Audit performed against the published Git history from `v0.1.0-beta.1` through `v1.0.5`.

| Version | System | Method | Entity/component | Field or mutation | Condition | Restore mechanism | Ownership evidence |
|---|---|---|---|---|---|---|---|
| 1.0.0–1.0.5 | `VehicleAccessSystem` | `MarkTargetLanesUpdated` | target `NetSubLane` children | adds vanilla `Updated` | after creating or retaining `AccessDetourBlock` | no saved ownership record; later detour release marks lanes updated | RouteFilter call site proves the write, but does not identify a unique lane owner after unload |
| 1.0.0–1.0.5 | `RestrictionPathSystem` | `ApplyRules` | pathfind lane data | applies `RuleFlags.HasBlockage` to target lanes | while a restricted target has a RouteFilter barrier | rebuilt by pathfind system; no persistent lane snapshot | RouteFilter system and target component are the only proof |
| 1.0.0–1.0.5 | `VehicleDetourSystem` | detour warmup | vehicle `PathOwner` | sets `PathFlags.Obsolete`, adds vanilla `Updated` | after a detour request reaches warmup | vanilla pathfinding/release path; no ownership token | RouteFilter request component is the only proof |
| 1.0.0–1.0.5 | `VehicleAccessSystem` | acquire/release | `AccessDetourBlock`, `VehicleDetourRequest` | adds/removes RouteFilter ECS components | matching restricted vehicle/target | release path removes components | component identity is definitive |
| 1.0.0–1.0.5 | `VehicleDetourSystem` | timeout recovery | vehicle/path state | may remove unreachable vehicle through old recovery logic | failed or non-moving detour request | game cleanup path | old system ownership is definitive only while request component exists |

The history contains no direct assignment to `CarLane.m_BlockageStart` or `CarLane.m_BlockageEnd`.
The old path system used `RuleFlags.HasBlockage` in pathfinding data and the old access system
added `Updated` to target lanes. A non-zero `CarLane` blockage found in a current city therefore
cannot be attributed to RouteFilter from the Git evidence alone.

The cleanup utility consequently classifies current non-default `CarLane` blockage as **unknown**
and reports it without writing it. It removes only the exact RouteFilter component identities:
`NodeAssetRestrictionV1`, `SegmentAssetRestrictionV1`, `RestrictedVehicleAssetV1`,
`AccessDetourBlock`, `VehicleDetourRequest`, and `RerouteCooldown`.

It does not clear `PathOwner`, `Updated`, `Failed`, `Obsolete`, `LaneObject`, or any `CarLane` field.
