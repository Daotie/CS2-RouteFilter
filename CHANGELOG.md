# Changelog / 更新日志

All notable changes follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and Semantic Versioning prerelease conventions.

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
