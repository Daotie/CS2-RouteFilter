# UX-37 interaction corrections

Build RF21-20261006-UX-37.

- Wheel events belong to the hovered numeric input, with immediate draft and native value binding updates at three decimal places. No global world-wheel mutation remains. Camera Zoom barrier exists only for input hover/map interaction. Input typing updates valid numeric values immediately; persistence is debounced by .4 seconds. Scale is removed from UI/native setters and runtime main geometry stays at scale 1. Finish button removed.
- Profiles exposed: Auto, EU, US. Custom sign supplementary plates are off unless the reused checkbox explicitly forces them on. Native settings persist this preference; changing locale rebuilds detailed legend labels.
- Exception legends are emitted only when every remaining unrestricted asset is exactly a complete semantic category/family. Partial exemptions cannot broaden into Except buses/goods vehicles. English exception wording follows GB terminology; see [DfT Traffic Signs Manual chapter 3](https://assets.publishing.service.gov.uk/government/uploads/system/uploads/attachment_data/file/782724/traffic-signs-manual-chapter-03.pdf). Unknown/mixed restrictions identify actual localized vehicle assets rather than only Selected vehicles. Exception legends cannot be suppressed by a primary sign.
- US supplementary anchor reserves .5m below the primary-face estimate for the native Wrong Way panel. Frame and ground-clearance inequalities are tested. Native overlap acceptance remains pending: CPU geometry tests do not establish native mesh bounds.
- Trucks preset includes only native goods road vehicles (including tractor-linked trailers); Cargo trucks includes those plus native cargo trains, excluding buses/refuse/fire/passenger trains.
- One Batch tool: left drag applies, right drag clears. Native cancel cannot intercept right drag while brush is active. Native replacement path helper and transactional rollback remain. Track lanes/TrainData are accepted through selection and commit validation alongside roads/CarData. Actual rail-path execution requires native testing.
- Main panel has no outer scrolling. Asset list shrinks within available space; action/footer rows remain visible. Clipboard/Clear gap is 10rem. Resolved sign text stays on one line with ellipsis. Map entry marker now has four navigation vertices with a concave rear notch.

Verification: unified Debug/Release, 16 coverage checks, 65,536 lease cases, 23 safety checks, 261 save checks, 93 road sign geometry checks, 2,107 UX semantic/profile checks, sign raster/opaque atlas/mask tests, UI typecheck/tests/build, authored mesh UV and whitespace passed. Final Release rebuild after text/locale-cache cleanup: zero warnings/errors.

Browser MOCK production-bundle checks: step .025 + height wheel displays .025; world wheel makes no appearance mutation; five inputs (four positions/rotation + step), no scale/Finish; category/force checkbox interaction; one Batch action; identical existing surfaces; no browser errors. At 1280x720 panel clientHeight=scrollHeight=562, asset list shrinks to 60px and footer stays inside panel. Screenshot/data use synthetic bindings, not native acceptance.

Native UX-36 OnLoad was present in latest game log before this work. UX-37 has not yet received game acceptance. Full playset preserved during local deployment.
