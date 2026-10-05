# RouteFilter 2.1.0-dev — UX-34 verification

Build: RF21-20261005-UX-34. Status: BUILD VERIFIED; native game acceptance pending unless explicitly recorded below. User's second acceptance failures are not overridden by this report.

## Verified

- Debug and Release against installed Game.dll: zero errors/warnings.
- RoadCoverage: 16 production matcher/receipt fixtures; LeaseRules: 65,536 ownership/admission pairs; SafetyRules: 23; SaveFormat: 261; RoadSigns: 89.
- UX: 2099 checks, including a native-buffer-shaped regression fixture whose generic enumeration throws; production indexed snapshot, compatible clipboard resolver, missing IDs, stable Recent history, shared semantic compression and theme-first AUTO.
- SignText: 17 installed-font labels plus transparent-overlay check. Authored RF-Plate remains 120 vertices / 76 triangles / 0.800×0.250×0.020 m, verified deterministic conversion/UVs and front text winding. No source mesh regeneration.
- UI typecheck, regression tests, production bundle: pass. Metadata category overrides misleading names, native URI parsing, legacy 8-field compatibility, group ordering, filtered bulk scope, global clipboard control availability, persistent numeric tool, SVG favorite, Segment-only footer batch controls, cached terrain coordinates/fit and cursor zoom.
- Browser MOCK: 1920×1080 and 1280×720 inspected. Fixed map/Sign Tool collision. Empty application error list; numeric input, clipboard availability and Recent rendering exercised. Cursor DOM wheel error <0.1 px. Evidence: docs/ux34-browser-evidence/.
- Native replacement audit: public NetToolSystem.CreatePath reused with read-only lookups and disposable output; no native road replacement tool, Apply, definitions or prefab writes. See NATIVE_REPLACE_SELECTION_AUDIT.md.
- Asset/playset issue: user confirmed “没有问题了”. Preserve current 369-entry playset, including user changes to enabled mods; deployment must compare its immediate before/after snapshots rather than restoring old counts.

## Native acceptance remaining

Four actual Node/Segment clipboard combinations; successful Apply -> persisted Recent after restart; actual prefab thumbnails/categories; selected sign visibility at native/default and scaled sizes; lit plate front/border/rear under day/night; sign placement on flat/elevated roads; intermittent Segment readiness; real terrain coastline and camera ownership; native drag release on straight/curved/junction/loop/one-way/elevated roads, invalid targets, cancellation, direction intent and save/load. Browser fixtures and API compilation do not establish these.

The complete requirement/status matrix is SECOND_ACCEPTANCE_MATRIX.md. No change to schema, matcher, topology enforcement, reroute policy, no-path fallback or grandfather ownership was authorized or made in this correction.
