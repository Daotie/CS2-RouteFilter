# Native replace selection audit — second acceptance

Inspected the installed Game.dll using ilspycmd, Game.Tools.NetToolSystem. Full decompiled reference is local at tmp/replace-audit/NetToolSystem.cs (not distributed).

- Native Replace uses State.Applying, press to begin, held Update, release Apply. Apply sets ApplyMode.Apply and creates replacement definitions; activating that tool is unsuitable for restrictions.
- SnapJob clears/recomputes UpgradeStates and calls public static CreatePath with the current first and last ControlPoint. Thus the current endpoint determines the preview, not a collection of every touched edge.
- Public CreatePath reads Edge/Node/Curve/PrefabRef/NetData/ConnectedEdge lookups and writes its supplied disposable NativeList<PathEdge>. It handles required layer compatibility and native graph cost/route choice without replacement definitions.
- RouteFilter calls ONLY CreatePath with read-only lookups. Native AddControlPoints/CreateReplacement/Apply, road prefab selection and tool activation are never called.
- Road hit points are projected to the authored curve at 1/32 intervals; full segments are restricted. No partial segment replacement is performed. Native path membership is bounded to 4096 targets; unsupported/deleted/temp/disconnected targets cancel the preview/commit.
- Only one current range is held. Press freezes the pending asset list, held movement replaces the range, release revalidates the same native selection and every road transport mode. Releasing over UI/empty space cancels. Escape/secondary cancel/tool exit discard transient geometry.
- Batch Apply/Clear are lower functional controls visible only in Segment mode. Each target retains its own persisted entry identities. Preview writes no restriction components or persistence intent.
- Prevalidation and snapshots precede mutation. Enforcement leases release once; dirty flags and visual target sets coalesce at the normal system boundary. Unexpected write failures restore the previous asset buffers and per-target direction intents; Recent records only a successful commit.

Validation: installed-game API builds successfully. Native helper execution, forks, loops, one-way roads, elevated roads and release behavior still require native game acceptance. Standalone graph fixtures cannot verify the native helper; do not present those as native tests.
