# RouteFilter 2.1 UX conformance build

Build: `RF21-20261005-UX-31`. This supersedes the UNIFIED-30 prototype UI.

## Implemented behavior

- The main panel keeps the original compact editing sequence. Search precedes All / Favorites / Recent. Favorites are independent far-left outline / yellow stars; restriction checkboxes are on the right.
- The header Tools menu owns copy / paste, presets, map, appearance, sign style, range and reset. Paste is disabled for an empty clipboard. Reset retains its existing confirmation.
- Presets contain Trucks and Cargo Trucks plus user save, load, rename and delete. Loading changes the pending vehicle selection; Apply remains explicit.
- The existing thumbnail dropdown retains AUTO and custom assets together, scrolling, localized display names and cached catalog thumbnails. No per-item 3D render targets are created.
- Appearance enters a compact persistent upper-left palette in place of the primary RouteFilter panel. Choose Scale, Lateral, Longitudinal, Height or Rotation, then scroll over the world. Translation/scale steps: .01 / .05 / .1 / .5; rotation: 1 degree. Shift multiplies by .2; Ctrl by 5. A native camera Zoom action barrier prevents simultaneous camera zoom. Close restores the primary panel. Settings saves are debounced after wheel input. While a target is selected, wheel input previews that target; the remaining signs refresh once after input settles. Vehicle/category label caches survive transform changes.
- Text geometry now faces local +Z with front-view upright UVs, matching both roadside sign frames. The text face covers 750 × 204 mm of the 800 × 250 mm plate. Adaptive glyph-bound fitting uses the installed Windows Microsoft YaHei bold sans-serif family; long labels use at most two centered lines. No font files are bundled or redistributed. This is a system CJK signage-oriented font choice, not a claim of exact compliance with a specific highway typography standard.
- Category labels deduplicate tractor/trailer variants and identify trucks, motorcycles and services. If every applicable road vehicle asset is selected, there are no auxiliary plates. Direction coverage is not used for this decision.
- Persistent red ground meshes have been removed, including the old enabled-by-default setting. Only a temporary blue range preview exists while editing.
- Native `InfoSectionBase` / `SelectedInfoUISystem.AddMiddleSection` and the game's `selectedInfoSectionComponents` registry host sign information. Ownership resolves through bounded native Owner chains; both markers and assembly roots carry the owning target, connection and entry ordinal. Native roots provide PrefabRef and Transform for selection promotion. The section shows vehicle categories, road name, restrictions and Edit in RouteFilter.
- Start/end Segment Range replaces drag brush input. It uses a deterministic minimum-segment-count chain over connected native Road edges, independent of vehicle routing. A bounded search (16,384 visited edges) fails closed. Confirmation revalidates topology, snapshots the current vehicle selection, releases enforcement once and marks the batch dirty for one normal rebuild. Blue preview meshes are grouped into chunks instead of one object per segment.
- Restriction Map is a dedicated RouteFilter window. World-coordinate road geometry and restriction overlays have separate bindings and memoized parsing. Geometry rebuilds on network change/open, not restriction-only revision. Modification-end change collection observes Updated/Deleted roads; closed maps perform no road/restriction queries. SVG paths are chunked. Roads, restricted segments, nodes and reliable entry arrows have separate layer controls. Drag pans, wheel/buttons zoom, Fit resets the view, and clicking a restricted target loads the primary editor.
- Native camera barriers, pointer regions, map bindings, preview meshes and cached runtime materials are released on close/reset/load/disposal. Road/rail enforcement algorithms and city save schema are unchanged.

## Architecture reference inspected

[TransitPlanner, Public branch](https://github.com/bruceyboy24804/TransitPlanner/tree/Public/TransitPlanner): `network-panel.tsx`, `network.module.scss`, `network-extras.tsx`, `TP_PlannerUISystem.Network.cs`, `.Roads.cs`, `.Ground.cs`, `.Terrain.cs`.

Useful patterns: an independent SVG viewport in world coordinates; cached/versioned background layers; chunked path strings; overlay/background separation; explicit layer toggles. RouteFilter implements road restriction data and editing semantics itself. Transit routing, terrain sampling and building layers were not copied.

## Automated validation

| Check | Result |
|---|---|
| C# Debug / Release | PASS — 0 errors, 0 warnings |
| RoadCoverage | PASS — 16 production matcher/receipt checks plus policy fixtures |
| LeaseRules | PASS — 65,536 ownership pairs plus native receipt/policy fixtures |
| SafetyRules | PASS — 23 admission checks |
| SaveFormatTests | PASS — 261 codec/schema/direction checks |
| RoadSigns | PASS — 42 placement/divider geometry checks |
| UxRules | PASS — 881 checks, including deterministic range and category/full-prohibition cases |
| Actual installed-font rasterization | PASS — 7 Chinese/English labels, unclipped glyph bounds and large short-label fitting |
| UI typecheck / tests / production build | PASS — filtered bulk, map geometry/chunks/entry validation, star hit target, secondary hierarchy, empty clipboard, presets, range gating and native section data props |
| Authored mesh / text front / UV / safe area | PASS |
| Whitespace | PASS |
| Stable enforcement/schema comparison | No changes in RoadEnforcementCoordinator, RailEnforcementBackend, RestrictionPathfindHook or city save schema |
| Local game payload | 28 packaged files copied and SHA-256 matched; previous RouteFilter payload backed up; playset configuration hash unchanged |

Validated package: `dist/ux31-final/RouteFilter-2.1.0-dev-RF21-20261005-UX-31.zip`.


## Required player game acceptance

Compilation and API inspection do not prove physical visibility, hit selection, Gameface layout or compatibility with the complete playset. The following require the player's in-game test on this build:

| Area | Check |
|---|---|
| Signs | Both roadside faces readable; short/long CJK and English labels; full prohibition has no plates; tractor/trailer deduplication; native and custom main signs |
| Native info | Click physical signs outside the RouteFilter tool, also after Scale adjustment; native section shows correct road/entry; Edit opens the matching restriction |
| Primary panel | Independent stars, right checkboxes, search-first tabs, filtered bulk selection, Tools menu, empty clipboard, copy/paste, built-in and user preset operations |
| Appearance | Palette remains upper-left while adjusting; five parameters preview immediately; wheel, step, Shift/Ctrl; camera does not zoom; close restores editor |
| Segment Range | Start/end/same-edge/disconnected/fork cases; explicit apply/clear/cancel; road changes between preview and confirmation; no writes on mouse release |
| Map | Actual road snapshot, each layer, reliable direction arrows, pan/zoom/Fit, click-to-edit, close/reopen; changing only restrictions keeps geometry cached |
| Persistence | Save/load, city changes and Reset retain the existing ownership/safety boundaries; no ghost signs or preview resources |
| Compatibility/performance | Complete playset, pause/resume, large city, idle closed map, large range, repeated open/close and locale changes; no persistent red ground lines |

No in-game result is claimed for UX-31 until those tests are performed.
