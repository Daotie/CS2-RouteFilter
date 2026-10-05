# RouteFilter UX-38 correction — 2026-10-06

## Scope
- Batch button text changes to 批量应用 / Batch apply. Left/right brush operations unchanged.
- Physical legends use their own reviewed dictionary, never UI translations, prefab names, asset descriptions or internal keys. CN names: 公交车、货车、市政车辆、出租车. The heterogeneous Other group is not a real vehicle taxonomy; its unresolved scope uses 车辆限行, with exact assets retained in the UI.
- Explicit Restricted / Except / Notice semantic kinds. Compare positive/reverse equivalent coverage; exceptions require whole allowed classes. Complete maintenance/refuse classes share the existing municipal category, goods and emergency families consolidate only with full coverage. Bus/taxi never become an invented public-transport umbrella.
- One supplementary plate, at most two lines. CN breaks only between whole names and preserves 除外; e.g. 公交车、出租车除外 and 公交车、出租车 / 市政车辆除外. Partial scope uses 部分. Font height reduced from 254 to 230 pixels and width margin increased.
- Map geometry and restriction targets include native TrainTrack and TramTrack. Rail overlays do not depend on road entry records. Navigation arrows use fixed world geometry and scale with map zoom; transparent hit target remains easy to select.

## Verification
Debug/Release build, 16 road coverage, 65536 lease, 23 safety, 261 save, 93 sign geometry checks passed. Final resolver suite: 2075 checks including all 63 nonempty subsets of a mixed six-class fleet, exact set equivalence for complete positive/exception descriptions, unknown catchall and partial exception rejection, one/two-line combinations, hostile asset-name/translation fallback rejection. Font raster tests, opaque atlas/light face/border/back/mask tests, UI typecheck/tests/build, native mesh UV and whitespace checks passed. Final Release rebuilt after municipal semantics normalization and removal of obsolete Detail field.
Production map helper tests prove arrows halve/double with zoom and preserve rail edge/node records with 0/0 road entry counts. Chinese raster exports were visually inspected. No successful browser mock or native game acceptance is claimed for this build.

## Wording reference
GB 5768.2-2022 is the current traffic-sign standard, as listed by the national standards platform. The requested short category names and the user's 除外 examples govern this mod's legends; they are not asserted to be verbatim GB labels. Strict coverage is checked before any exception or upper-category compression.
https://openstd.samr.gov.cn/bzgk/std/std_list?p.p1=0&p.p2=GB+5768&p.p90=circulation_date&p.p91=desc

## Deployment
Install UX-38 into existing Mods/RouteFilter, hash-verify all payload files, preserve the active playset JSON byte-for-byte and all other enabled mods/assets. Commit identity and deployment hashes are recorded in package BUILD.txt and tmp/ux38-deployment.json. Restart the game to load the new DLL; physical rendering and real rail map still require native acceptance.
