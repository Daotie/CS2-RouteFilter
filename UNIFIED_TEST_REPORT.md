# RouteFilter 2.1.0 release verification

Release date: 2026-10-06 (Asia/Shanghai).
The author independently found, investigated, fixed and tested the missed-vehicle issue, confirmed the final UX-39 in-game test passed, and requested publication of 2.1.0. This records the author’s own test results, not external player feedback.

The stable payload retains UX-39 behavior and existing save schema. Only version/build identifiers and release documentation/metadata change during promotion. UX-39 automated verification: Debug/Release zero warnings/errors, 16 coverage, 65536 lease, 23 safety, 261 save, 93 sign geometry and 2143 UX/semantic checks; typography/opaque atlas/mask, UI typecheck/tests/build, arrow absolute-size/zoom regressions and mesh UV checks passed. Stable-version Debug/Release builds, all listed rule/font/mesh tests, UI typecheck/tests/build and clean dependency install passed. npm audit --omit=dev reports zero runtime vulnerabilities. Version consistency and whitespace checks passed.

Release package includes compiled DLL/dependencies, UI bundle, icons, bilingual notes, source commit identity and SHA-256 sums. Publish to existing Paradox ModId 155839 using NewVersion and GitHub tag v2.1.0. Preserve the current local playset during stable payload deployment.
