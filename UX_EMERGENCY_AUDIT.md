# RouteFilter 2.1 emergency correction audit

User game acceptance of SIGN-32: overall UX, continuity, UI stability, map, menus, favorites, segmented filter, Recent, clipboard, appearance and supplementary placement **FAILED**. Primary native sign spawning **GAME VERIFIED**; composition **PARTIALLY WORKING**. These statuses are not replaced by compilation results.

| Layer | Backend / binding | Presentation / interaction | Correction |
|---|---|---|---|
| UI lifecycle | PARTIAL: teardown exists via NotifyPanelClose; secondary close/input ownership is distributed | FAILED: appearance condition unmounts main panel; secondary lifecycle distributed | P0: centralized backend teardown, frontend close/Escape/unmount cleanup, preserve main panel, local failure boundary |
| 2.0 foundation | Existing 2.0 layout and tokens available in v2.0.0 | FAILED: filter uses default buttons, Reset moved to menu | P1: restore exact baseline SCSS/main footer; shared segmented selector; isolated star |
| Clipboard | PARTIAL: stable names stored, but empty copy marks clipboard ready; no result binding | FAILED: no visible copy/paste outcome | P2: counted visible status, compatible pending set, empty clipboard disabled |
| Recent | PARTIAL: Apply hook exists but no end-to-end evidence; parent/child ranking can be wrong | FAILED by user | P2: committed-only stable IDs, binding revision and UI receipt diagnostics, deterministic newest-first grouping |
| Presets | WORKING CRUD bindings (game verification pending) | FAILED: permanent form and action wall | P2: grouped menu list, contextual edit, disclosed name editor |
| Appearance | PARTIAL: wheel backend exists, no Reset; close/tool-switch recovery incomplete | FAILED: palette on left, main hidden | P3: persistent upper-right palette, five tool parameter rows, wheel/step/reset/finish |
| Plate assembly | Primary spawning GAME VERIFIED | FAILED longitudinal/front relationship: auxiliary assumes zero-offset face, native face anchor not measured | P3: explicit approach/local frame and metadata-derived primary front anchor; common basis both sides |
| Map | UNKNOWN extraction at runtime; text payload and parser implemented | FAILED: viewBox/non-scaling-stroke relies on unsupported/uncertain Gameface behavior | P4: inspect reference pixel SVG + matrix world transform; counts/payload/bounds/UI receipt evidence before styling |

Order: P0 -> P1 -> P2 -> P3 -> P4 -> P5. Independently buildable phase commits. No stable enforcement/topology/matching/reroute/no-path/grandfather/save-schema changes. Final status is IMPLEMENTED + BUILD VERIFIED until the user's screenshot-based game acceptance.


P0–P4 phase commits: ea98232, 9b1749b, 6b68621, bbfc28a, 17f4434. Each phase passed backend build and UI typecheck/production build. P5 integrates secondary failure containment, native Escape priority, coherent tokens and final unified package UX-33.

Map reference: tmp/transit-reference/network-panel.tsx (pixel width/height, explicit matrix and minimum pixel stroke), network.module.scss, network-extras.tsx and TP_PlannerUISystem.Network/Roads/Ground/Terrain.cs. RouteFilter keeps its own road extraction, revision cache and 500-road path chunks. Runtime evidence now logs queried/extracted/rejected roads, UTF-8 bytes, bounds, overlay target count and frontend receipt. No game capture has established extraction or rendering success yet.

Sign geometry: native primary ObjectGeometryData.bounds front + native scale + 20 mm clearance determines supplementary front. Local +Z faces approaching traffic; offset signs use the same frame on both sides. Automated frame checks cover offset signs, three scales and rotated approaches; physical game placement remains unverified.
