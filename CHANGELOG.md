# Changelog / 更新日志

All notable changes follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and Semantic Versioning prerelease conventions.

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
