# RouteFilter second acceptance implementation matrix

Authority: latest direct message and second attached directive override conflicts; first directive retained for compatible clarifications. Prior user failures remain authoritative until native retest. Commit each independently buildable phase P0–P6.

| Requirement | Classification | Current | Required | Layer | Implementation | Verification |
|---|---|---|---|---|---|---|
| Popup surface | BUG FIX / VISUAL CORRECTION | Invisible menu background | Explicit single dark surface / native tokens | UI CSS | IMPLEMENTED / native retest pending | BUILD VERIFIED; prior game failure not yet retested |
| Selected sign visibility | BUG FIX | Disappears on selection | Owned render state survives selection | Visual lifecycle | IMPLEMENTED / native retest pending | BUILD VERIFIED; prior game failure not yet retested |
| Sign values | BUG FIX | Duplicate/overlapping values | One numeric representation | UI | IMPLEMENTED / native retest pending | BUILD VERIFIED; prior game failure not yet retested |
| Segment directions | INVESTIGATION / BUG FIX | Intermittent unsupported groups | Diagnose geometry/timing/canonical target without changing enforcement | Editor topology | IMPLEMENTED / native retest pending | BUILD VERIFIED; prior game failure not yet retested |
| Copy/Paste placement | EXISTING FEATURE MODIFICATION | In secondary menu | Concise Copy/Paste lower action row | UI | IMPLEMENTED / native retest pending | BUILD VERIFIED; prior game failure not yet retested |
| Global clipboard / four target combinations | DATA/STATE CORRECTION | Native buffer LINQ throws NotImplementedException | Stable prefab IDs, compatible pending paste survives mode/target | Session / bindings | IMPLEMENTED / native retest pending | BUILD VERIFIED; prior game failure not yet retested |
| Clipboard feedback / missing IDs | BUG FIX | Copy callback aborts | Counted non-modal availability feedback | Session / UI | IMPLEMENTED / native retest pending | BUILD VERIFIED; prior game failure not yet retested |
| Recent | BUG FIX / DATA/STATE CORRECTION | Native buffer LINQ aborts successful Apply receipt | Committed IDs -> stable history -> binding -> rendered matches | Session / UI | IMPLEMENTED / native retest pending | BUILD VERIFIED; prior game failure not yet retested |
| Favorite SVG | BUG FIX | Font glyph mojibake | Outline/filled owned SVG with independent hit area | UI assets | IMPLEMENTED / native retest pending | BUILD VERIFIED; prior game failure not yet retested |
| Category classification | NEW FEATURE | Flat list / name-heuristic glyphs | Shared native evidence semantic classification | Catalog / signs | IMPLEMENTED | BUILD VERIFIED; game retest pending |
| Category expand / aggregate | NEW FEATURE | Missing | Expandable category, restrained aggregate state | UI | IMPLEMENTED | BUILD VERIFIED; game retest pending |
| Category search / favorites / recent | NEW FEATURE | Flat filters | Matched children with preserved category grouping/order | UI model | IMPLEMENTED | BUILD VERIFIED; game retest pending |
| Prefab thumbnails | NEW FEATURE | Generic glyph reused | Cached native icon; fallback only missing | Catalog / UI | IMPLEMENTED | BUILD VERIFIED; game retest pending |
| Persistent merged Sign Tool | INTERACTION REPLACEMENT | Separate menu actions | Upper-right while RF road context active, collapse optional | UI / bindings | IMPLEMENTED / native retest pending | BUILD VERIFIED; prior game failure not yet retested |
| Numeric + wheel only | INTERACTION REPLACEMENT | Value buttons | Direct input, selected wheel parameter, no sliders | UI / settings | IMPLEMENTED / native retest pending | BUILD VERIFIED; prior game failure not yet retested |
| Wheel step / input ownership | EXISTING FEATURE MODIFICATION | Partial | Effective step, fine/coarse, release camera input on exit | Input | IMPLEMENTED / native retest pending | BUILD VERIFIED; prior game failure not yet retested |
| Geometry placement | VISUAL CORRECTION | Physically odd | Native entering-lane geometry, bounds and clearances | Signs | PENDING | FAILED / NOT TESTED |
| RF plate native material | VISUAL CORRECTION | Unlit uniform appearance / ignored masks | Lit material like original; front/back/edge distinction | Rendering | PENDING | FAILED / NOT TESTED |
| RF plate border | VISUAL CORRECTION | Not visible | UV-aware surviving front border | Textures / rendering | PENDING | FAILED / NOT TESTED |
| Front-only text | BUG FIX | Possible duplicated/bleeding layers | Single front, correct culling/clearance | Mesh / material | PENDING | FAILED / NOT TESTED |
| AUTO theme / locale | EXISTING FEATURE MODIFICATION | Locale can drive primary profile | Physical US/EU theme first; supplementary UI language | Semantic resolver | PENDING | FAILED / NOT TESTED |
| Semantic compression / direction separation | EXISTING FEATURE MODIFICATION | Protected prior feature | Reuse classification; all directions independent from vehicle coverage | Semantics | PENDING | FAILED / NOT TESTED |
| Map land | NEW FEATURE | Road network only (basic GAME VERIFIED) | Cached coarse land/water through terrain API | Map backend / UI | PENDING | FAILED / NOT TESTED |
| Map instruction removal | VISUAL CORRECTION | Permanent paragraph | Remove | UI | IMPLEMENTED / native retest pending | BUILD VERIFIED; prior game failure not yet retested |
| Map cursor zoom / fit | EXISTING FEATURE MODIFICATION | Pixel math implemented, game acceptance failed | Cursor anchor using actual viewport; combined land/network fit | UI geometry | IMPLEMENTED / native retest pending | BUILD VERIFIED; prior game failure not yet retested |
| Map pan / input / selection | BUG FIX | Partial | Isolated camera/drag/wheel, shared layer transform | Input / UI | PENDING | FAILED / NOT TESTED |
| Replace investigation | INVESTIGATION | Old start/end implementation | Inspect actual vanilla interaction, range, commit, coupling | Native code audit | PENDING | FAILED / NOT TESTED |
| Segment-only bottom batch controls | INTERACTION REPLACEMENT | Range menu in both contexts | Lower tool row, hidden Node; Apply/Clear operation | UI | PENDING | FAILED / NOT TESTED |
| Press / held endpoint / release | INTERACTION REPLACEMENT | Click start then end | Live current candidate only, release commits once | Tool | PENDING | FAILED / NOT TESTED |
| Native selection semantics / junctions / loops | INTERACTION REPLACEMENT | Custom path before native audit | Reuse safe helpers or documented native-based fallback | Native selection | PENDING | FAILED / NOT TESTED |
| Ephemeral world preview | DATA/STATE CORRECTION | Stored range preview | No restriction/intent mutation until release; geometry overlay | Tool / overlay | PENDING | FAILED / NOT TESTED |
| Atomic batch | DATA/STATE CORRECTION | Batch helpers exist | Prevalidate whole set; one release/dirty/rebuild transaction | Tool / index | PENDING | FAILED / NOT TESTED |
| Batch direction ownership | DATA/STATE CORRECTION | Must preserve | Each target existing directions; no copied LogicalEntry | Tool / persistence | PENDING | FAILED / NOT TESTED |
| 2.0 visual conformance | VISUAL CORRECTION | User partially accepts foundation | Preserve panel, compact tool roles, no debug panel expansion | UI | PENDING | FAILED / NOT TESTED |
| Performance / caching | EXISTING FEATURE MODIFICATION | Not game verified | Event-driven metadata, terrain, road and sign resources | All layers | PENDING | FAILED / NOT TESTED |
| Full playset assets | INVESTIGATION | User reports assets not applied | Diagnose enabled/downloaded/applied state; preserve full playset | Launcher / Skyve | PENDING | FAILED / NOT TESTED |
| Unified build / game matrix | INVESTIGATION | Prior BUILD VERIFIED, failed game acceptance | Build all phases; four clipboard scopes, categories, material faces, road cases, map lifecycle | Verification | PENDING | FAILED / NOT TESTED |

Root-cause evidence: native UI.log 2026-10-05 16:24:07 copy and 16:27:04 Apply throw Unity.Entities.DynamicBuffer<T>.IEnumerable<T>.GetEnumerator NotImplementedException. Avoid LINQ over native buffer; snapshot by index before any generic collection processing. Map queried/extracted 2697 roads, payload 395183 bytes, confirms basic geometry only.

Checkpoint P0/P1: Debug build, 2099 UX checks, UI typecheck and UI regression tests pass. Selected sign retention/editor readiness are fixes for identified code paths, not claims of complete game resolution. Persistent merged tool implemented alongside numeric correction to avoid two competing controls.
