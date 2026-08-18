# RouteFilter 0.4.0-beta.1

## English

RouteFilter now provides asset-level access control. Its searchable in-game catalog discovers every currently available vehicle prefab and lets players restrict any combination at a node or across an entire road or rail segment.

Restrictions use an explicit V1 save schema containing prefab entity references. A vehicle is affected only when its exact `PrefabRef` matches an entry saved on the target. Matching vehicles promptly request another route through RouteFilter's short-lived, reference-counted pathfinding barrier.

This is a beta release. Start with a new city or a clearly identified save backup, then verify node restrictions, segment restrictions, asset matching, and save/reload behavior before using it on an important city.

## 中文

RouteFilter 现已支持逐资产通行控制。游戏内可搜索目录会发现当前全部可用车辆预制资产，玩家可任意组合选择，并对节点或整段道路、轨道路段设置限制。

限制数据采用明确的 V1 存档结构并保存预制实体引用。只有车辆的准确 `PrefabRef` 与目标上保存的条目一致时才会受限。匹配车辆会通过 RouteFilter 的短时引用计数寻路屏障立即请求其他路线。

本版本仍处于 Beta 阶段。请从新建城市或明确标记的存档备份开始测试，并在重要城市中使用前验证节点限制、路段限制、资产匹配及存读档行为。
