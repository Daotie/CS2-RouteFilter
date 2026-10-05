# UX-35 unified verification

Build RF21-20261005-UX-35. Final suite results will be appended after execution.

The user's UX-34 screenshots and native UI logs demonstrate the previous menu/palette failure and opaque auxiliary text plane. Production UI preview verifies the new surfaces using synthetic bindings with HTMLSelectElement.options deliberately unavailable; this is not native acceptance. Native alpha-cutout, hover persistence and median placement still require direct in-game observation.

Final verification: full unified suite passed (Debug/Release, RoadCoverage 16, LeaseRules 65,536, SafetyRules 23, SaveFormat 261, RoadSigns 93, UxRules 2,099, SignText 17 measured labels plus transparent-pixel check, UI typecheck/tests/build, authored plate conversion and git whitespace). Final shortened-status/SVG sizing cleanup rechecked with Release and UI typecheck/tests/build.

Browser interaction: category Enable sent IDs 1,2,3 with true and produced 3/3; Disable sent the same IDs with false and produced 0/3. Tools opened, Presets navigated and palette remained present. Preview with deliberately unavailable HTMLSelectElement.options had zero captured app errors and no select elements. Initial short-viewport collapsed list found and corrected with explicit height/overflow before final package. These are mock browser checks, not native checks.
