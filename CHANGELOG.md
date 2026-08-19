# Changelog / 更新日志

All notable changes follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and Semantic Versioning prerelease conventions.

## 0.5.0-beta.1 — 2026-08-19

### Added

- Circular node outlines and dashed segment hover highlights.
- Contextual road/rail asset filtering based on the hovered network target.
- Hover details for maximum speed (editor value divided by two), acceleration, and braking.
- Expandable fixed-trailer and multiple-unit engine/carriage groups.
- Minimal line icons for the toolbar, road vehicles, rail vehicles, and trailers.

### Fixed

- Replaced per-frame catalog getters with event-driven value bindings.
- Replaced unsupported grid CSS with a scrollable flex layout compatible with the game UI engine.
- Added explicit forbidden/allowed wording and prevented an empty left-click selection from clearing a target.

### 中文

- 新增节点圆形线框、路段虚线悬浮高亮，以及道路/轨道资产自动筛选。
- 新增资产参数悬浮信息、牵引车辆与车厢分组和简约线稿图标。
- 将每帧资产绑定改为事件驱动，并以可滚动 Flex 布局替代游戏 UI 不支持的 Grid。
- 明确“选中即禁行”的界面语义，空选择左键操作不再意外清除限制。

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
