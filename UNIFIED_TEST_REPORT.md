# UX-36 unified verification

Build RF21-20261005-UX-36.

Production atlas test: opaque pixels, visible glyph ink and light face, unchanged border/back/edge. Readable export verifies the authored +Z UV mapping. Release/typecheck/UI tests passed during implementation. Browser checks confirm identical window gradients, stable rightward menu anchoring and custom .025 wheel-step dispatch; these use synthetic bindings.

Final unified run passed: Debug/Release zero warnings/errors, RoadCoverage 16, LeaseRules 65,536, SafetyRules 23, SaveFormat 261, RoadSigns 93, UxRules 2,099; typography/opaque atlas/HDRP mask preservation; UI typecheck/tests/build; authored front UV/mesh and whitespace checks. Category full/clear checkbox dispatch, preset save/back and zero browser errors verified with mock bindings. Native UX-36 material, animation and interaction acceptance are pending; UX-35 black auxiliary face remains the last native failure evidence.
