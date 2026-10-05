# RouteFilter UX-36 correction and verification

Build: RF21-20261005-UX-36

The user's UX-35 screenshots are the failure evidence: category text actions were unwanted, secondary surfaces diverged from the main gradient, menus covered the editor and shifted between pages, wheel step had no editable field, and the supplementary face remained black. The screenshots also show that the menu and persistent palette now mount in the native game; this does not establish visual acceptance.

UX-36 changes:
- Category bulk selection uses the exact SelectionCheckbox shared with the existing asset rows, including empty/partial/full state and scoped pending changes. Header glyphs use the previously supplied local PNG assets; expand arrows are white inline SVG with rotation transitions. Prefab icon behavior remains unchanged.
- Every window uses the main panel gradient/blur and the same legacy theme override. Secondary controls, preset input and save/cancel actions use established tokens. Button hover/press, popup entry, submenu page and preset editor transitions added. Numeric/preset text line-height and padding centered.
- Tools popup is anchored outside the main panel on its right; only open/resize positions it. Submenu/return/editor changes retain that position. The far-right sign selector opens outside its own palette, using the left side only when the screen edge leaves no room on the right.
- Wheel step adds a decimal input (three decimal places) alongside existing quick choices, using actual degrees/meters/scale units. Native step clamp permits .001–1. Changing step preserves the active parameter.
- Removed the separate supplementary text mesh/material entirely. Production PlateLegendAtlas composites glyphs into the original RF-Plate +Z UV region using opaque base texture, then uses a copy of the existing lit plate material. No alpha shader keyword or transparent overlay remains. Original model and texture files retained. Runtime HDRP mask conversion sets only green/AO to 255; the authored zero channel suppressed all ambient light. Metallic (R), detail (B), smoothness (A) remain byte-identical. Channel packing reference: [Unity HDRP mask map documentation](https://docs.unity3d.com/cn/Packages/com.unity.render-pipelines.high-definition%4010.4/manual/Mask-Map-and-Detail-Map.html).

Verification: Release/typecheck/UI tests passed during implementation. Production atlas test proves opaque output with text ink and light face; every changed pixel stays in the label region, leaving frame/rear/edge unchanged. A readable front-face export comes from this production compositor. This CPU texture proof is not a native screenshot.

Browser interaction verified custom step .025 reaches setSignAdjustment; main/menu/palette computed gradients are identical; Tools→Presets retains (426,84.5) outside the panel ending at x=418. Browser uses synthetic bindings and is not native acceptance. Final unified run passed: Debug/Release zero warnings/errors; RoadCoverage 16, LeaseRules 65,536, SafetyRules 23, SaveFormat 261, RoadSigns 93, UxRules 2,099; SignText typography, opaque atlas and mask preservation; UI typecheck/tests/build; authored mesh/UV verification and whitespace checks. Browser category checkbox dispatch verified full selection then clear, with no errors. Save preset and Back retained menu position.

Native UX-36 material/UI animation acceptance remains pending. UX-35 black-face failure is not marked passed. Full playset and stable enforcement/save behavior are untouched.
