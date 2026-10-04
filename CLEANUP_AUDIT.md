# RouteFilter 2.0 audit — 2026-09-05

Baseline: HEAD df891f1 (1.0.5), with existing uncommitted 2.0 changes. No baseline changes reverted.

## Classification before deletion

| Class | Evidence and decision |
|---|---|
| A — unused adapters | LaneTargets, FinalEntryLanes, TryGetRestrictedTarget, TryGetBarrierLanes, IsFinalEntryLane only serve the disabled legacy systems; delete together. |
| B — compatibility | Keep NodeAssetRestrictionV1, SegmentAssetRestrictionV1, RestrictedVehicleAssetV1 and persistence system identity/payload version. Keep AccessDetourBlock, VehicleDetourRequest, RerouteCooldown field layouts so Reset can identify owned data. |
| C — debug | Delete InteractionProbe, document mousedown observer and UI click/binding telemetry. Retain conditional topology dumps and candidate/safety observations: these are consumed by Phase 1C, not disposable just because they resemble diagnostics. |
| D — old enforcement | VehicleAccessSystem, RestrictionPathSystem, VehicleDetourSystem all have DisableAutoCreation; Mod does not schedule them; their GetOrCreate references are internal to the old trio. None implements serialization. Reset does not invoke them. Delete execution code. |
| E — old UI | UI/src/index.tsx registers only mods/route-filter. Old routeFilterUI.tsx and its SCSS/SVG have no new UI import. Delete after checking references. |
| F — runtime dependencies | Keep tool, overlay, persistence, directed topology, prefab matcher, candidate and safety implementation. Candidate readers are completed before caches clear; each persistent native allocation has an OnDestroy Dispose. |
| G — uncertain ownership | Shared lane blockage cannot be restored from the old target marker or the old m_BlockedLanes dictionary: original byte values were never saved, subsequent writes cannot be excluded. Do not write lane/path state. |

## Reset safety

Persistent ECS restrictions and pending restore/name-map caches must clear in the same reset operation. Serialize then observes zero restricted targets. Loading an older save intentionally restores that older save's restrictions; Reset cannot rewrite other saves.

Legacy request/cooldown/barrier components are proven RouteFilter-owned data. Their existence does not prove ownership of current CarLane bytes. Nearby nonzero blockage is only probable legacy state; other lanes are unknown. Neither class is modified. No global vehicle scan, PathOwner flag changes, Updated removal, movement changes or rail enforcement.

## Settings investigation evidence

1.0.5 registers before LoadSettings; working tree registers after LoadSettings. Neither change alone establishes the cause of a missing page. Latest game log records both registration and localization success, with no registration exception. Local Game.Modding.ModSetting.RegisterInOptionsUI discards the bool returned by Game.Settings.Setting.RegisterInOptionsUI; with no default ECS world it silently does nothing. Therefore the old success log was not proof of registration. Inspect the actual OptionsUISystem pages after registration and retry only a missing page on the UI thread. Historical in-game success is user-reported, not reproducible from Git alone.

## Visual reference

Only the positive concept image was supplied. No negative in-game image is present in this task. Historical UI_REFERENCE_NOTES.md claims TTE source research but provides no source path/revision; treat those palette claims as unverified. Use the supplied concept for blue-gray translucent material, title-case header, compact selector, continuous rows and small utility footer. Native Cohtml blur support is not game-verified; provide a readable translucent fallback and avoid stacking glass cards.
