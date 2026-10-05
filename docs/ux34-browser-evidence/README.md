# UX-34 browser evidence — MOCK ONLY

Production UI bundle with synthetic cs2 bindings; screenshots are NOT game acceptance. Native coui icons do not resolve in a normal browser. Individual prefab thumbnail availability, physical rendering and native selection require the game.

- initial/menu: merged persistent right Sign Tool, numeric values, SVG favorite, category groups, lower Copy/Paste, dark menu surface.
- map-separated/compact-map: cached synthetic land/water in shared coordinates; map and Sign Tool do not overlap at 1920×1080 or 1280×720. The initial failed overlap screenshot is intentionally not represented as a pass.
- recent: actual production Recent binding/filter renders the synthetic successful-Apply response.
- node: lower batch controls absent in Node mode; Sign Tool remains present.
- Runtime application error list empty. Numeric field accepted 0.50 and sent setSignAppearance. Copy enables Paste; Apply response publishes Recent. DOM wheel exercise retained the cursor anchor within 0.1 px; pure production math tests retain it exactly.

Screenshots precede minor final text/aggregate-state styling changes. Final native build and fresh payload are recorded in UNIFIED_TEST_REPORT.md.
