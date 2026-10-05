# RouteFilter 2.1 UX conformance audit

| Feature | Original design | Current implementation | Mismatch | Required rework |
|---|---|---|---|---|
| Main panel | Compact restriction workflow | Permanent utility rows | Advanced actions dominate | Header Tools menu, secondary windows |
| Library | Search then All / Favorites / Recent | Filters above search | Hierarchy reversed | Move segmented filter below search |
| Favorites | Independent far-left star | Boxed button after checkbox | Order and visual role wrong | Neutral outline / yellow filled star |
| Presets | Built-ins and user CRUD | User save / load / delete | Built-ins and rename absent | Trucks, Cargo Trucks, rename |
| Sign style | Thumbnail dropdown, AUTO / CUSTOM | Cached dropdown already present | Always occupies primary panel | Preserve dropdown behind Tools |
| Appearance | Persistent wheel palette | Sliders | Missing longitudinal and rotation | Five parameters, step and modifiers |
| Auxiliary plates | Large readable front-facing text | Small text, reversed face | Wrong front geometry and fitting | Front-facing quad, measured adaptive type |
| Full prohibition | Main sign alone | Category plates always created | Direction coverage confused with vehicle coverage | Compare applicable road vehicle catalog |
| Ground overlay | No gameplay debug stripes | Red mesh strips enabled | Unaccepted world presentation | Remove persistent ground rendering |
| Sign selection | Native information section | Tool-only hover / selection | No native selected-info integration | Owner resolution and native section |
| Batch segments | Start / end connected range | Drag brush | Wrong interaction and topology | Deterministic road chain, explicit confirmation |
| Restriction map | Cached geometry and independent overlays | Geometry rebuilt on every restriction revision | Unnecessary world scan | Separate cached road binding, layers, pan / zoom |
| Terminology | Formal bilingual transport language | Prototype terms | Inconsistent user language | Localize tool, sign, preset and map labels |

Implementation proceeds in P0–P4 order. Stable enforcement and save format remain the backend baseline. In-game acceptance is required after the unified build; compilation alone is not visual acceptance.

## SIGN-32 semantic addendum

The original table records the pre-UX-31 baseline. SIGN-32 replaces direct category plates with stable visual semantics, coverage-qualified consolidated legends and independent profile/language/asset state. Profile limitations and current automated versus game verification are documented in UNIFIED_TEST_REPORT.md. The original UX tool/map/editing contract remains; no gameplay red line, icons on RF-Plate, new enforcement behavior or save-schema changes.
