# RouteFilter 2.1.0-dev — unified SIGN-32 test build

Build: `RF21-20261005-SIGN-32`. Player game acceptance is pending.

## Implemented presentation changes

Restrictions now pass through stable visual semantic IDs, coverage-aware consolidation, profile/primary selection and localized text. The rejected prefab-category-to-plate generator is removed. Complete road-vehicle scope uses the verified native no-entry primary alone. Complete goods subclasses collapse to one category; large-goods-only retains its narrower meaning; selected subsets keep a qualifier. Road maintenance is derived from native maintenance metadata. Unknown/missing classification uses selected vehicles rather than guessed names. Complex mixes consolidate before rendering, with at most two text-only supplementary plates.

`Signage Profile` is separate from AUTO/CUSTOM asset mode and active locale. AUTO reads each entry road's ThemeObject / ThemePrefab.assetPrefix metadata: NA keeps US under all languages; EU + Simplified Chinese resolves CN, EU + en-GB resolves UK, other/unfinished refinements use Generic Europe. Manual choice survives language changes. CN/UK have no independently verified national primary assets and explicitly display a compatible no-entry fallback. US uses a native NA-family no-entry asset when available. Dedicated goods/bus prohibition capabilities are absent from native metadata; no graphics are invented to pretend they exist. HK/JP stay hidden.

Supplementary semantic dictionaries have independent zh-CN/zh-HANS, en-US and en-GB terms. Unsupported locales explicitly fall back to English. Installed CJK and Latin font metrics are separate, with measured SHORT/MEDIUM/LONG/VERY_LONG fitting, safe margins and at most two lines. Text materials are shared by locale, semantic ID/qualifier, profile/style, tier and resolved text. Category results survive locale/profile/transform changes; vehicle metadata classification is cached until catalog change. Textures are neither rendered per frame nor allocated per sign. Original RF-Plate mesh and positive +Z front/UV model remain.

Ordinary approaches get traffic-hand roadside placement; approaches >=7m wide may repeat on the other side for gameplay visibility. This is a game approximation, not a national placement rule. Permanent red ground lines remain removed. Native information shows semantic scope independently of whether a supplementary plate is needed.

The prior compact main panel, independent stars, All/Favorites/Recent library, Tools, copy/paste, presets, thumbnail asset selector, five-parameter wheel palette, explicit start/end connected range, native selected-info integration and cached restriction map remain. Enforcement, directed topology, exact prefab matching, admission, reroute/no-path outcomes and city-save schema are unchanged.

## Automated validation

| Check | Status |
|---|---|
| C# Debug / Release | BUILD VERIFIED — zero errors/warnings |
| RoadCoverage | BUILD VERIFIED — 16 production matcher/receipt checks plus fixtures |
| LeaseRules | BUILD VERIFIED — 65,536 ownership pairs plus native fixtures |
| SafetyRules | BUILD VERIFIED — 23 checks |
| SaveFormatTests | BUILD VERIFIED — 261 codec/schema/direction checks |
| RoadSigns | BUILD VERIFIED — 43 placement/divider/visibility checks |
| UxRules / actual visual semantics | BUILD VERIFIED — 2,086 checks, including all 511 nonempty fleet selection combinations, narrow coverage, consolidation, missing assets, profile matrix, manual overrides, locale fallback and stable-key handling |
| Installed-font rasterization | BUILD VERIFIED — 17 locale/label combinations, actual ink bounds, unclipped margins and large short CN labels |
| UI typecheck / tests / production build | BUILD VERIFIED — previous UX checks plus exposed/hidden profiles, fallback notice, locale policy and conservative native-info empty state |
| Authored plate / positive facing / UV / whitespace | STATICALLY VERIFIED / BUILD VERIFIED |
| Stable backend/schema | STATICALLY VERIFIED — no edits to enforcement, RestrictionIndex/topology, exact matcher, pathfind, leases, admission or city persistence |

Test package: `dist/sign32-final/RouteFilter-2.1.0-dev-RF21-20261005-SIGN-32.zip`.

## Exposed profiles and language coverage

| Profile | Implemented behavior | Primary limitation | Game status |
|---|---|---|---|
| AUTO | Per-entry theme + locale heuristic | Depends on reliable native EU/NA metadata and available native assets | NOT TESTED |
| Generic Europe | CS2 EU family baseline, native no-entry fallback | No unified European legal-standard claim | NOT TESTED |
| CN | Manual/AUTO preference and formal Simplified Chinese semantics independent of plate-language choice | Explicit generic/native primary fallback; national asset coverage unavailable | NOT TESTED |
| UK | Manual/AUTO preference and independent British terminology / condensed font candidates | Explicit generic/native primary fallback; national asset coverage unavailable | NOT TESTED |
| US | NA native primary family and separate American terminology | Missing NA assets use explicit native fallback; dedicated category signs unavailable | NOT TESTED |
| HK / JP | Architectural reservation only, hidden | Regional assets and independent locale review incomplete; English supplementary fallback | NOT TESTED |

No full GB, TSRGD or MUTCD compliance is claimed. Current authoritative reference details are kept in the ignored local `internal-notes/TRAFFIC_SIGN_LOCALIZATION_REFERENCES.md`. No standard graphics or proprietary fonts are distributed.

## Required player acceptance

GAME VERIFIED, PERFORMANCE VERIFIED, SAVELOAD VERIFIED and MIGRATION VERIFIED: **NOT TESTED for SIGN-32**. Build checks cannot establish native world visibility, mirrored/missing rendered glyphs, native click selection, UI behavior or full-playset performance.

For each exposed profile test: all applicable vehicles (no redundant plate); complete goods (dedicated primary if independently supported, otherwise one legend); large-only and selected large assets; maintenance; mixed subsets (no tower); both faces on wide roads; curved/ramp/one-way/asymmetric/junction entries; EU/US mixed road themes; every AUTO locale matrix case; manual US/CN with English/Chinese locale changes; missing RF-Plate/font/localization/custom/NA assets; save/load and city switch. Confirm exact selected assets and direction masks remain authoritative in UI. CN/UK tests must acknowledge explicit fallback, not national asset coverage.

Retest existing stars/search/filtered bulk/copy/paste/presets, upper-left wheel palette and camera barrier, start/end range and topology change before confirm, map layers/pan/zoom/Fit/click-to-edit, close/reset/unload cleanup. Use the complete playset: 370 mods, 345 enabled. No playset edits are part of this deployment.
