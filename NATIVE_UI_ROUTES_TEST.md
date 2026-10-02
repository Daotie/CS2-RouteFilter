# Compact native UI and local route preview / 紧凑原生 UI 与附近路线预览

Build: RF2-20261002-NATIVE-UI-ROUTES-02

The launcher now uses the game's FloatingButton, as verified in the existing PLT integration, with RouteFilter's own clean prohibition SVG. No PLT source was copied into this repository. Native toolbar sizing, tint, tooltip and selection behavior replace the custom flat button and its duplicate styling.
按钮改用游戏原生 FloatingButton，以 RouteFilter 自绘禁止符号为图标。原生组件负责尺寸、提示和选中外观，删除重复自定义按钮样式。没有引入 PLT 源码。

Panel width 560 -> 400rem; maximum height 86 -> 78vh; list maximum height 42 -> 32vh; header 70 -> 52rem; rows 49 -> 37rem; selector 54 -> 38rem. Main surface and text use native panelColorNormal, menuText1Normal and panelBlur, following the user's game opacity. No extra panel shadow. Header uses the clean glyph; compact footer shows the build identifier and preserves reset confirmation. Existing English/Simplified Chinese labels remain unchanged.
面板及控件缩小，主材质和文字使用游戏颜色变量，底部保留识别码与重置入口。现有中英翻译与重置确认保留。

## Route visibility / 路线显示

Previous implementation handed ToolSystem.selected to vanilla but did not own TrafficRoutesSystem.routesVisible. Its visible paths therefore depended on an external native route-layer state. Vanilla route collection scans UpdateFrame buckets of city PathOwner entities; do not turn that global scan on solely for this tool.
此前 RouteFilter 只设置所选目标，没有控制原生路线开关。原生路线搜索随全城路径实体分组增长，本次不为了预览启动这套全城搜索。

New read-only preview in the existing overlay system samples LaneObject buffers on the selected target and, for a node, adjacent edge lanes. Only paths that reference the selected target or its own lanes are drawn. It updates immediately after a target change and otherwise every 30 rendered frames. Source controllers are deduplicated; missing/deleted/temp sources are ignored; shared lane curves are drawn once.
新的只读预览采样选中目标及相邻路段上的车辆已有路径，只画实际途经目标的路径，换目标立即刷新，随后每30渲染帧刷新。不会发起寻路，也不修改 PathOwner、阻塞、车辆或保存数据。

Workload follows inspected adjacent lanes + relevant LaneObjects + sampled PathElements, never a new city-wide vehicle query. Caps: 128 unique source lanes, 256 LaneObjects, 32 vehicles, 256 remaining elements per path, 1024 drawn unique curves. Adjacent edge enumeration and each SubLane buffer are capped at 128. Rendering follows cached curves. Persistent NativeList is reused, job completed before refill, disposed on destroy and cleared on Reset; managed sets reused and cleared. No preview data enters persistence.
工作量随局部车道、LaneObject 与采样路径增长。明确上限如上；缓存可丢弃，Reset清空，退出释放，不写存档。

This is a bounded nearby-vehicle sample, not a complete map of all future city traffic. It may be empty when no sampled vehicle has a remaining path through the target, or when the relevant path lies outside the sample caps. It uses existing path lane geometry, without computing a new route.
这是附近车辆采样，不是全城未来路线全集。没有相关车辆或超出采样上限时仍可为空。采用已有路径车道几何，不新算路线。

## Verification / 验证

Debug and Release: 0 errors, 0 warnings. Webpack: success. Diff whitespace check: passed. Game: NOT VERIFIED. Native toolbar export has a working reference in the currently loaded PLT, but this RouteFilter change still requires visual and interaction verification in game.
Debug、Release、UI构建和差异检查通过；本轮游戏内视觉与路线显示未验证，不能声明完全修复或视觉定稿。
