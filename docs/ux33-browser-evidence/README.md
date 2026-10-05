# UX-33 browser presentation evidence — MOCK DATA

These are browser captures of the production UI bundle at 1920×1080 with mocked cs2 bindings/native components and an illustrative theme. They are **not GAME VERIFIED**, not native HUD recovery evidence, and not actual city geometry. Native coui icon requests cannot resolve in this browser harness. The synthetic catalog, clipboard outcome and Apply/Recent updates do not prove native backend execution.

| Scope | Browser capture | Remaining game evidence |
|---|---|---|
| A, C | [Main panel; active/inactive independent stars](A-main.png) | Native panel/HUD/style continuity |
| B | [Favorites selector](B-favorites.png) | Native persisted filtering |
| D | [Menu; disabled empty Paste](D-menu.png) | Native menu focus/input |
| E | [Preset list](E-presets.png) | Native CRUD/pending application |
| F | [Counted Copy result inside menu; Paste available](F-clipboard.png) | Native saved forbidden IDs and compatible pending paste |
| G | [Recent populated via mocked Apply receipt](G-recent.png) | Successful native Apply and binding receipt logs |
| H | [Persistent upper-right palette; main remains](H-appearance.png) | Native wheel, preview, Reset/Finish and camera recovery |
| I, J | NOT TESTED | Native assembly viewed from approach and opposite roadside |
| K | [Synthetic network; shared coordinate layers](K-map.png) | Actual recognizable city road network and extraction logs |
| L | [Synthetic zoom/pan](L-map-pan-zoom.png) | Native pan/zoom and target selection |
| M | [Map closed; main remains](M-restored.png) | Native HUD/input/camera fully restored |

Observed browser interaction: favorite click emitted only toggleFavoriteAsset, no selection event; empty Paste was disabled and Copy enabled it; preset actions appeared only after contextual action; palette retained main panel and Escape removed it; map zoom/layer controls worked and clicking node emitted selectMapTarget 13:1; Escape closed map, emitted backend close/false bindings and retained main panel. No application errors remained. The harness is kept locally at tmp/ux33-preview/index.html; these captures cannot replace A–M game acceptance.
