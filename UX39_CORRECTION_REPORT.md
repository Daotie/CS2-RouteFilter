# UX-39 — focused correction

User UX-38 game screenshots show oversized map arrows overlapping in a starburst and the 46/98 selected restriction (Other 0/52, all five other categories selected) printing 车辆限行 instead of 其它车辆除外.

Arrow cause: UX-38 converted a screen-space marker size into world geometry using whole-map fit scale, so large terrain bounds produced a huge physical marker. Correction: remove fit-scale input; arrow tip-to-back length is 6 world metres, independent of map extent. Its shape is transformed with the same map matrix as the roads and scales proportionally with zoom. Transparent click target remains usable.

Legend cause: valid road assets in the UI Other category shared the unknown/missing semantic, making the resolver reject the allowed set. Correction: append OtherRoadVehicles as the exact remainder of the existing road category partition. Existing road CarData assets without a named category have this semantic; missing and non-road assets keep SpecifiedVehicles/Notice. Complete Other allowance prints 其它车辆除外, the corresponding restriction prints 其它车辆, and partial Other allowance cannot falsely exempt the whole category. No asset names or legacy UI translation fallback added.

Verification: Debug/Release zero warnings/errors; existing 16 coverage, 65536 lease, 23 safety, 261 save and 93 sign geometry checks; resolver 2143 checks, including actual screenshot 46/98 and 0/52 counts and exact coverage across all nonempty seven-asset partitions. New marker regression checks absolute physical size and 200m/2km/14km map extents, in addition to proportional zoom tests. Sign font/opaque atlas/mask tests include 其它车辆除外; UI typecheck/tests/build and authored mesh checks pass.

Scope: only these two reported defects. No palette/layout/batch operation changes. Deploy UX-39 to existing Mods/RouteFilter, preserve current playset configuration and other entries byte-for-byte. Native UX-39 visual acceptance remains pending restart; user screenshots establish UX-38 failure, not UX-39 success.
