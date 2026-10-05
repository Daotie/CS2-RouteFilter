# UX-35 — screenshot-driven correction

Build: RF21-20261005-UX-35

- Category headers have independent Enable/Disable restriction controls. They update the pending selection in the current search/Favorites/Recent scope; Apply persists it. Expand uses owned inline SVG.
- Replaced the native HTML select with ordinary profile buttons. UX-34 native UI logged repeated `Cannot read properties of undefined (reading 'length')` and FeatureBoundary hid both menu and palette. Unsupported HTML select options are the identified React DOM compatibility risk. UX-35 browser harness deliberately removes HTMLSelectElement.options and verifies these surfaces still render.
- Utility menu and persistent palette are independent fixed surfaces in the established panel portal. Short viewport layouts retain the asset list and scroll overflow.
- Loaded/Applied messages describe state only; instructions and redundant vehicle-selection/plate-language/wheel paragraphs removed.
- Supplementary glyphs use alpha-cutout lit material: transparent pixels are discarded, preventing the opaque black rectangle seen in the user's native screenshot. Authored plate mesh, base color, mask, and control textures retained.
- Each physical sign gets its own positioned ECS owner. Rendering guard strips Highlighted only from owned runtime signs before BatchInstanceSystem: installed vanilla BatchInstanceSystem.UpdateObjectInstances converts Highlighted to OutlineOnly, explaining hover disappearance. Native sign hit/selection metadata retained; no road or vanilla prop is modified.
- Repeated inner signs require the nearest opposing carriageway to leave at least 1.6 m of space. Painted/unseparated dividers and unpaired roads get the traffic-side outer sign only; wide genuinely separated approaches retain a repeat. This is a geometric clearance criterion, not a prefab-name guess.

Validation status: Release build and UI tests pass during implementation. Full final suite and native UX-35 acceptance are recorded separately below when executed. User screenshots remain native UX-34 failure evidence. Browser previews are synthetic bindings, not game acceptance. The enforcement matcher, save schema and full playset configuration are untouched.

Final full suite passed; see UNIFIED_TEST_REPORT.md. Native UX-35 remains pending until game reload and direct observation. Runtime log evidence and the user's screenshots informed this correction; compilation/browser mocks do not prove native material/highlight acceptance.
