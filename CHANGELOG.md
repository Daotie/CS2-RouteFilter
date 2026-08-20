# Changelog / 更新日志

All notable changes follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and Semantic Versioning prerelease conventions.

## 1.0.5 — 2026-08-21

### Fixed

- Game 1.6.0f1 initializes mods on a background thread, where the Input System's Temp allocator fails inside key binding resolution (`ArgumentNullException: destination` in `InputBindingResolver.AddActionMap`), which aborted the entire mod during `OnLoad`. Key binding registration is now retried on the main thread, and the mod always continues to start: the top-left panel button remains available even when the shortcut key cannot be registered at all.
- Systems that poll the shortcut actions now tolerate a missing action map instead of throwing every frame.

### 中文

- 游戏 1.6.0f1 会在后台线程初始化 mod，输入系统的 Temp 分配器在该线程的按键绑定解析中必然失败（`InputBindingResolver.AddActionMap` 抛出 `ArgumentNullException: destination`），导致整个 mod 在 `OnLoad` 阶段中止。现在按键绑定注册会在主线程重试，且无论注册是否成功，mod 都会继续启动：即使快捷键完全不可用，左上角面板按钮仍然可用。
- 轮询快捷键的系统现在能容忍动作映射缺失，不再每帧抛异常。

## 1.0.4 — 2026-08-20

### Fixed

- The vehicle catalog is no longer built only once: it now rebuilds after each save finishes loading and whenever vehicle prefabs or content availability actually change, so late-loading modded assets are no longer missing from the list. Opening the panel never triggers a rebuild.
- The manual refresh button used the "↻" character, which is missing from the game UI font and rendered as a box; it now draws an inline SVG icon.
- The panel now reserves the game's top bar and bottom toolbar areas and grows with the available viewport height, showing noticeably more asset rows without covering the toolbar at any supported screen size or UI scale.
- The tool system now skips its per-frame net raycast while the tool is inactive, and no longer overwrites the vanilla tool selection when it should not run.
- Project builds are no longer broken by the toolchain's Entities source generators, which mis-handle file-scoped namespaces under current compilers (CS0311 on every `UpdateAt<TSystem>` call).

### Changed

- Enforcement scanning now runs in a parallel Burst chunk job that returns only matching vehicles; the main thread processes the rare matches instead of walking every moving vehicle in the city each frame.
- The pathfinding system now skips the pathfind data container entirely while no detour barrier is active.
- Restriction badges are drawn by a single batched parallel job instead of one job per restricted target.
- The restriction index rebuild reuses its per-target sets, so steady-state rebuilds are allocation free.

### 中文

- 车辆目录不再只构建一次：现在每次存档加载完成后自动重建，且当车辆预制件或内容集实际发生变化时也会重建，晚加载的模组资产不再从列表中缺失；打开面板不会触发任何重建。
- 手动刷新按钮原使用游戏字体缺失的“↻”字符而显示为方框，现改为内联 SVG 图标。
- 面板预留了游戏顶栏与底部工具栏区域，高度随可用视口增长，可显示更多资产行，且在任何受支持的屏幕尺寸与 UI 缩放下都不会遮挡工具栏。
- 工具未激活时不再每帧进行路网射线检测，也不再干扰原版工具的选中状态。
- 修复工具链 Entities 源码生成器与当前编译器不兼容导致的构建失败（CS0311）。
- 执法扫描改为并行 Burst 分块任务，主线程只处理命中的少量车辆，不再每帧遍历全城行驶车辆。
- 无绕行屏障时寻路系统完全跳过 pathfind 数据管道。
- 禁行标牌改为单个批量并行任务绘制，不再按目标逐个调度任务。
- 限制索引重建复用集合，稳态重建零分配。

## 1.0.3 — 2026-08-20

### Fixed

- Reworked exact-asset enforcement into inverted lookup indexes: vehicles whose prefabs are not restricted anywhere are skipped with a single set lookup, and lane ownership is resolved from a precomputed map instead of crawling the owner chain and comparing against every saved asset list every frame. This removes the per-frame O(vehicles × lanes × owner depth × forbidden assets) scan that caused severe CPU load and FPS drops.
- The pathfinding barrier now only processes targets that currently carry an active detour block, so the per-frame rule application does no work while no vehicle is being rerouted.
- Vehicles that still have to cross a restricted target after rerouting (no alternative route exists) are now handed to the game's built-in cleanup instead of being left to stop and retry forever.
- Restricted-target badges now render as camera-facing billboards that always face the player while the tool is active.
- Vehicles that fail to complete a reroute within the ~1 second timeout while standing still are also removed through the game's cleanup.
- Save payload upgraded to version 2: forbidden asset lists are stored as stable prefab names and re-resolved after loading, so the exact saved assets survive save/reload instead of only the restricted locations.
- Restricted-target badges are now flat horizontal tags floating above the target, sized close to the selection highlight, and no longer rotate with the camera.

### 中文

- 将逐资产拦截改为反转索引查询：与任何禁行清单都无关的车辆每帧只需一次集合判断即可跳过；车道归属改为查预计算映射，不再每帧沿所有者链逐级比对全部禁行资产。消除了每帧 O(车辆数 × 车道数 × 层级 × 禁行资产数) 的扫描，解决 FPS 骤降的性能问题。
- 寻路屏障只在存在活跃绕行目标时执行，无车辆绕行时不再做无谓的每帧规则写入。
- 重规划后仍必须经过禁行处的车辆（不存在可替代路线）改为交给游戏自带清理机制移除，不再原地停车无限重试。
- 禁行目标悬浮图标改为始终面向玩家摄像机的投影式标牌。
- 绕行请求约 1 秒内未完成且车辆静止无移动的，同样交由游戏清理机制移除。
- 存档数据升级为 v2：禁行资产清单以稳定的 prefab 名称保存并在读档后重新解析，重载后能恢复具体禁行资产而不只是禁行位置。
- 禁行目标标牌改为固定在目标上方的平面圆盘标签，大小接近选择框，不再随镜头旋转。

## 1.0.2 — 2026-08-20

### Fixed

- Added a versioned, save-embedded restriction payload that is written on every save and re-applied to nodes and segments after loading, so applied restrictions no longer disappear on reload.
- Loaded saved mod settings before registering key bindings so binding and option changes persist across game restarts.

### Added

- When the RouteFilter tool is active, restricted nodes and segments now show a floating prohibition badge; the badge enlarges for the hovered or selected target.

### 中文

- 新增随存档保存的版本化禁行数据：每次存档写入全部禁行节点/路段清单，读档后自动重新挂载，避免重载后设定丢失。
- 修正设置加载顺序，按键绑定与选项修改在重启后不再丢失。
- 工具启用时，有禁行的节点和路段显示悬浮禁行图标，悬停或选中的目标会放大提示。

## 1.0.1 — 2026-08-19

### Fixed

- Prevented the bottom application controls from being clipped by the panel at supported viewport and UI scales.
- Kept the in-panel version label on one line beside the localized title.
- Shortened the forbidden-selection notice so it states only that selected assets will be blocked.

### 中文

- 修复受支持视口与 UI 缩放下底部应用操作按钮被面板裁切的问题。
- 游戏内版本号固定在本地化标题旁单行显示。
- 精简禁行提示，仅保留“选中的资产将被禁止通行”。

## 1.0.0 — 2026-08-19

### Added

- Stable-release branding, public documentation, screenshot integration points, and official Paradox Mods publishing metadata.
- A viewport-aware panel layout that keeps selection, assets, paging, and application controls on one screen.

### Changed

- Opening or closing the RouteFilter panel now activates or closes the selection tool at the same time; the separate in-panel activation button was removed.
- Changed the default remappable panel shortcut from `Ctrl+Shift+X` to `Ctrl+Shift+N`.
- Promoted the verified asset-level V1 save schema and exact-asset enforcement workflow to the first stable release.

### 中文

- 新增正式版品牌素材、公开说明、游戏内截图接入位置及官方 Paradox Mods 发布元数据。
- 面板根据视口自适应，目标选择、资产列表、分页和应用按钮保持在同一页面。
- 面板开关与选择工具同步，不再保留面板内单独的工具启用按钮。
- 默认且可重新绑定的快捷键由 `Ctrl+Shift+X` 改为 `Ctrl+Shift+N`。
- 将已验证的逐资产 V1 存档结构和准确资产禁行流程发布为首个正式版本。

## 0.5.0-beta.1 — 2026-08-19

### Added

- Circular node outlines and dashed segment hover highlights.
- Contextual road/rail asset filtering based on the hovered network target.
- Hover details for maximum speed in km/h, acceleration, and braking.
- Expandable fixed-trailer and multiple-unit engine/carriage groups.
- Minimal line icons for the toolbar, road vehicles, rail vehicles, and trailers.

### Fixed

- Replaced per-frame catalog getters with event-driven value bindings.
- Replaced unsupported grid CSS with a scrollable flex layout compatible with the game UI engine.
- Added explicit forbidden/allowed wording; applying an empty forbidden list now intentionally allows all compatible assets.
- Changed editing to an explicit select → configure → apply workflow; right-click now cancels target selection.
- Fixed segment raycasts that hit owned lanes instead of the parent network edge.
- Isolated panel pointer input from the world tool and made asset/group controls independently clickable.
- Added fixed-height scrolling plus pagination, and made catalog-wide allow/forbid actions independent of filtering.
- Corrected maximum-speed presentation from metres per second to km/h.
- Loaded each target's saved forbidden list on selection instead of reusing a global-looking pending state.
- Expanded enforcement to current lanes, navigation lanes, path elements, network-node endpoints, and complete vehicle consists.
- Scheduled exact-asset enforcement after road/rail navigation and before vehicle movement.
- Reduced panel height and removed the redundant engine-group instruction line.

### 中文

- 新增节点圆形线框、路段虚线悬浮高亮，以及道路/轨道资产自动筛选。
- 新增资产参数悬浮信息、牵引车辆与车厢分组和简约线稿图标。
- 将每帧资产绑定改为事件驱动，并以可滚动 Flex 布局替代游戏 UI 不支持的 Grid。
- 明确“选中即禁行”的界面语义；明确应用空禁行清单时会放行全部兼容资产。
- 改为“选择目标 → 配置清单 → 明确应用”的交互，右键只取消目标选择。
- 修复射线命中路段子车道时无法解析到所属路段的问题。
- 隔离面板与地图工具的鼠标输入，并将资产圆形控件和分组展开控件拆分为独立操作。
- 新增固定高度滚动与分页兜底，全部禁行/放行不再受筛选条件影响。
- 将最高速度从米/秒正确换算为 km/h 显示。
- 选中目标时载入其独立保存的禁行清单，不再复用看似全图共享的暂存状态。
- 将执行检测扩展到当前车道、导航车道、路径元素、路段端点节点和完整车辆编组。
- 将逐资产拦截明确安排在道路/轨道导航之后、车辆移动之前。
- 缩短面板并删除重复的编组操作说明行。

## 0.4.0-beta.1 — 2026-08-19

### Added

- Searchable selection for every available vehicle prefab asset.
- Asset-level restrictions for individual nodes and complete road or rail segments.
- Versioned V1 save components that store selected prefab entity references.
- Asset-catalog refresh and selection count in the in-game panel.

### Behavior

- A vehicle is restricted only when its exact `PrefabRef` matches a saved asset entry.
- Matching vehicles request another route through a short-lived, reference-counted pathfinding barrier.
- Police, ambulance, and fire assets can be exempted through the mod settings.

### 中文

- 新增可搜索的完整可用车辆预制资产列表。
- 节点与整段道路、轨道路段均支持逐资产限制。
- 新增带版本标识的 V1 存档组件，保存所选预制实体引用。
- 游戏内面板新增资产目录刷新与选择数量显示。
- 仅当车辆的准确 `PrefabRef` 与已保存资产匹配时才执行限制。
