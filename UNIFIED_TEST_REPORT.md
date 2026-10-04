# ROUTEFILTER 2.1.0 UNIFIED TEST BUILD REPORT

Date: 2026-10-04 (Asia/Shanghai). Version: `2.1.0-dev`. Build: `RF21-20261004-UNIFIED-30`.

## RESUME AUDIT

The disk state differed substantially from the pasted handoff. HEAD was `df891f1` (1.0.5), with uncommitted 2.0 enforcement and early 2.1 UX work. Native main signs, the actual thumbnail/name dropdown, favorites/recent/copy/paste were IMPLEMENTED. Presets, map, brush, plate rendering and appearance/lifecycle integration were PARTIAL or MISSING. The old badge implementation still enumerated all restrictions each active-tool frame. The resumed baseline passed Debug and TypeScript checks and was preserved in `4bf5776` before further changes.

## A–G completion matrix

| Phase | Implemented result | Verification |
| --- | --- | --- |
| A | Native main prohibition instances, target ownership, road-theme AUTO, stable-name CUSTOM dropdown with native icon/display name, authored RF-Plate mesh integration | IMPLEMENTED / STATICALLY VERIFIED / BUILD VERIFIED; runtime NOT TESTED |
| B | Fixed-size localized category plates, downward stacks with clearance, shared mesh/texture/material/text caches; active-tool sign hover/click; persistent ground restriction lines | IMPLEMENTED / STATICALLY VERIFIED / BUILD VERIFIED; rendering/input NOT TESTED |
| C | Favorites, recent applied assets, copy/paste of asset names; selection bindings update immediately | IMPLEMENTED / STATICALLY VERIFIED / BUILD VERIFIED; game UX NOT TESTED |
| D | Named user presets, overwrite/delete/load, stable asset names only; separate missing/unsupported counts | IMPLEMENTED / STATICALLY VERIFIED / BUILD VERIFIED; settings/save lifecycle NOT TESTED |
| E | Restriction Map snapshots, a combined road-background path, restricted target selection, counts/directions, fit/pan/zoom | IMPLEMENTED / STATICALLY VERIFIED / BUILD VERIFIED; Cohtml interaction NOT TESTED |
| F | Segment Brush LMB apply/RMB clear, frozen asset selection, unique pending targets and preview, release-time batch write, Escape/disable/switch/reset cleanup | IMPLEMENTED / STATICALLY VERIFIED / BUILD VERIFIED; native input NOT TESTED |
| G | Main scale, height, lateral offset and plate gap; popup exclusivity and independent pointer areas; unload/reset/dispose cleanup; Release debug overlay/trace gates | IMPLEMENTED / STATICALLY VERIFIED / BUILD VERIFIED; visual/lifecycle behavior NOT TESTED |

## Exact behavior and implementation

- AUTO prefers the approach road prefab's theme, then generic/available no-entry signs. CUSTOM stores the selected prefab name; unavailable custom content temporarily falls back to AUTO without erasing the user's choice. The dropdown is expandable, searchable and virtualized.
- RF-Plate uses the supplied binary FBX's geometry and UVs, converted deterministically into an embedded resource: 120 vertices / 76 triangles, normalized to **0.800 × 0.250 × 0.020 m**. BaseColor, MaskMap and ControlMask are embedded and loaded once per resource cache. No runtime Blender/FBX importer is needed.
- Distinct road vehicle categories produce one plate each: cars, buses, trucks, taxis, garbage trucks, fire engines, ambulances and police cars. English/Chinese labels use the active localization dictionary. The label image/material is cached by text. No per-frame localization or text generation is performed.
- Plates stack downward with a configurable 0.02–0.20 m gap. The assembly lifts when required to leave at least 0.35 m below the lowest plate. Main scaling has its own assembly transform and cloned mesh views; vanilla prefab mesh/material data is not edited. Plate failure is caught independently and leaves the main marker in place.
- Presets and clipboard contain asset names only. They do not copy targets, directions or topology. Loading changes the pending selection and requires Apply. A currently unsupported asset is counted separately from an unavailable asset. Favorites/recent/presets are personal settings, retained by Reset; clipboard and temporary UX state are cleared.
- The map builds only while open and on configuration/topology/actual geometry changes. Repeated unchanged Updated tags are deduplicated by geometry stamps. Its road background becomes one SVG path. Closing clears the snapshot and selection cache.
- Brush freezes assets at mouse-down, queues at most 4096 distinct segments, and creates only temporary previews during dragging. Release writes the queued batch; backend leases are released once for the batch. All-entry direction intent is used on brushed segments. Escape, tool disable/switch, target-mode change, popup close and Reset discard pending work. The RMB hint reads “Drag to clear restrictions”.
- Persistent ground meshes replace the previous DrawBadgesJob and its full per-frame restriction queries. Marker rebuilds are driven by settings, localization, catalog, target geometry, configuration or topology changes. No new all-vehicle/all-lane scan is introduced.
- Only RouteFilter-owned native marker/assembly entities are removed. Runtime native markers are excluded from the serializer with Temp; Unity visual objects use DontSave. Map, preview and assembly resources are cleared on preload/world destruction, and visuals are disabled after mod disposal.

## Files and systems changed

- `Components/UserPreferenceData.cs`, `Components/PendingBrushBatch.cs`: bounded settings codec and isolated stroke state.
- `Systems/RoadRestrictionVisualSignsSystem.cs`, `.Assembly.cs`, `RuntimeSignResources.cs`, `RoadRestrictionSignTooltipSystem.cs`, `SignAppearance.cs`, `VisualGeometryStamp.cs`: rendering, cache, placement, ownership, localization and tool hover.
- `Systems/RestrictionGroundIndicatorSystem.cs`: persistent ground lines and temporary brush previews.
- `Systems/RestrictionToolSystem.cs`, `.Brush.cs`: sign selection, pointer areas, native mouse hints and release-time brush commit.
- `Systems/RouteFilterUISystem.cs`, `.Library.cs`, `.Presets.cs`, `.Map.cs`, `RouteFilterResetSystem.cs`, `RestrictionOverlaySystem.cs`: bindings, lifecycle, map/preset controls and old badge removal.
- `UI/src/mods/route-filter/components/Enhancements.tsx`, `RoadSignSelector.tsx`, `RouteFilterPanel.tsx`, `index.tsx`, `mapGeometry.ts`, stylesheet: complete feature UI and popup/input cleanup.
- `Setting.cs`, `Mod.cs`, project/version metadata, workflow, changelog/release notes and readme badges.
- `Resources/RF-Plate.mesh`, `tools/Extract-PlateMesh.py`, `Verify-PlateMesh.py`, `Build-UnifiedTest.ps1`, `Tests/UxRules`, `UI/tests/map-geometry.test.cjs`: resources, verification and reproducible packaging.

## Final builds and automated verification

| Check | Result |
| --- | --- |
| C# Debug | BUILD VERIFIED — 0 errors / 0 warnings |
| C# Release | BUILD VERIFIED — 0 errors / 0 warnings |
| UI production build | BUILD VERIFIED |
| TypeScript | BUILD VERIFIED |
| RoadCoverage | PASS — production matcher/receipt and policy fixtures |
| LeaseRules | PASS — 65,536 ownership pairs plus lease/path/rail receipt checks |
| SafetyRules | PASS — 23 safety admission checks |
| SaveFormatTests | PASS — 261 schema/codec/direction/migration-parser fixture checks |
| RoadSigns | PASS — 42 approach-placement/divider geometry checks |
| UxRules | PASS — 873 preference/stack-clearance/appearance/brush-state checks |
| UI filtered bulk and map geometry | PASS |
| Authored RF-Plate resource | PASS — dimensions, UV range, indices, deterministic source conversion |
| Whitespace and protected enforcement file comparison | STATICALLY VERIFIED |

`RestrictionTopology`, `LogicalEntry/EntryGroup`, `DirectedEntryGate`, direction topology, candidate/safety pipelines, road/rail rerouting, ConfirmedNoPath handling, vehicle removal policy and save schema are unchanged relative to the preserved disk baseline. Brush reuses the existing write APIs and batches their lease release; it does not introduce a second enforcement pipeline or topology resolver.

The policy remains: **AlternativeFound → Rerouted; ConfirmedNoPath → Removed; EnforcementUncertain → Grandfathered.** No hardcoded test spawn was added. Development route previews are compiled out of Release; P0 trace entry calls are Debug-only and the preview starts disabled.

## Git and package identity

Branch: `main`. Resume preservation commit: `4bf5776`. Later feature/report commits and the exact final HEAD are provided with delivery; the package's `BUILD.txt` records its source HEAD and branch. `SHA256SUMS.txt` records all shipped payload files. No public mod publication or Git push is performed for this local test build.

All task source changes are committed. The following pre-existing, unrelated untracked local items are retained, not silently deleted or included in the test build: `PLAYSET_CODE_MODS_20260925.md`, `dsh-code-review-preset/`, `release-artifacts/`, `tmp/`, `tools/ParkingLotTool/`. Therefore the whole repository is not a clean working tree; the task has no uncommitted source changes. Build/package output is ignored under `dist/`.

## GAME VERIFIED and NOT TESTED

**GAME VERIFIED: none for this exact 2.1.0 build.** The user's prior 2.0 game verification is baseline history, not proof of this payload.

**NOT TESTED:** actual native marker creation/culling/visibility; prefab thumbnails in Cohtml; AUTO/CUSTOM switching; RF-Plate GPU rendering/UV/text appearance; native scaled main shaders; many-category placement on curved/sloped roads and left/right assemblies; hover/click; live brush input/batch results; UI popup behavior; Reset crash behavior; city save/load/unload/reload; CPU/GPU/memory performance; live migration.

**PERFORMANCE TEST REQUIRED. SAVE/LOAD TEST REQUIRED.** This implementation is performance-oriented and statically reviewed, but is not PERFORMANCE VERIFIED or SAVE/LOAD VERIFIED. Offline legacy byte-layout tests do not establish MIGRATION VERIFIED against a real city save.

## Known limitations

- Main-sign scaling outside scale 1 uses isolated Unity mesh views with vanilla materials. Each chosen prefab's shader must be checked in-game. Resource acquisition failure falls back to the native main sign at scale 1; a successful call does not prove GPU visibility.
- The main-face bottom is estimated from the prefab's bounds and face width to separate its pole. Unusual custom props may need appearance adjustments and an in-game spacing check. Plate clearance is measured against the approach geometry, not a separate terrain-height sample at the verge.
- Mask/control resources are shared and assigned where the selected shader exposes those properties. The runtime auxiliary shader currently uses an unlit appearance; authored PBR mask effects are not promised.
- Hover/click editing is available while the RouteFilter tool is active. RF-Plate has no separate gameplay prefab/collider; the native main marker owns selection.
- Brush supports segments, applies all entries and caps one stroke at 4096 targets. It samples the target under the pointer each tool update; it does not interpolate skipped distant segments between fast cursor samples.
- Preset names are capped at 80 characters, with 64 presets and 512 stable asset names per preset. Copy/paste uses the pending selection and still requires Apply.
- Restriction Map is a network geometry overview with target IDs/counts, not a terrain/satellite map. Very large open maps still require profiling. Road/rail enforcement retains the previously documented 2.0 detection limitation.

## Unified in-game test checklist

Use the single final package and verify the in-game build label `RF21-20261004-UNIFIED-30`.

1. **World signs:** apply a road restriction; confirm actual vanilla main signs are visible on both sides, not just a resolver log. Expand the selector and check native thumbnails + localized/display asset names, AUTO, CUSTOM switching and missing-custom fallback.
2. **RF-Plate:** verify mesh/material rendering, fixed dimensions, one and many-category stacks, 0.35 m lowest clearance, main-to-first spacing, plate gaps, Chinese/English text and locale switching. Repeat on curved/sloped roads, both driving sides and left/right assemblies; test scale/height/offset/gap controls.
3. **Directions and editing:** compare 4/4 versus 2/4 applied entries; signs and ground lines must match only enabled entries. Hover the main sign, inspect the native tooltip, click to edit; clear restrictions and delete/change the target, checking for stale visuals.
4. **Library:** test favorites, recent apply history, filtered Allow/Forbid All, parent/trailer selection and copy/paste. Save/replace/delete/load presets; distinguish missing assets from assets incompatible with the current road/rail target. Confirm loading does not copy directions or apply automatically.
5. **Map and popups:** open/close, select node/segment, pan/zoom/fit, modify restrictions and roads with the map open; close it and check sustained cost. Switch map/presets/appearance/prefab dropdown without overlapping popups or road clicks through UI.
6. **Brush:** in Segment mode, drag LMB to apply and RMB to clear several segments. Verify only previews/pending count change during dragging and the batch commits on release; test revisiting segments, UI crossing, empty selection, Escape cancellation, tool/mode switch, close and deleted pending targets. Check mouse hints.
7. **Reset and persistence:** test Reset with map/popup/brush active; own visuals and pending state must clear while vanilla/player/other-mod props remain. Save and reload restricted cities; unload/reload another city, change locale/theme/content, and repeat. Verify runtime markers are regenerated, not persisted or duplicated. Retest 2.0 reroute/no-path/uncertain outcomes.
8. **Performance:** compare tool off/on, paused/running, map closed/open and unchanged/edited visuals with increasing restriction counts. Look for sustained scans/rebuilds, forced synchronization, CPU spikes, allocations, GPU cost and resource growth over repeated rebuild/reset/load cycles.
