# Prefab matching fixtures / 车辆 prefab 匹配测试

Run `dotnet run --project Tests/RoadCoverage/RoadCoverage.csproj -c Release`.

These fixtures execute the production `VehiclePrefabMatcher` against small in-memory ECS adapters. They cover exact target/prefab matching, canonical and physical layout sources, physical-only receipts, detached or changed parts, deleted/temporary entities, controller cycles and the four-hop ownership bound. No asset names or vehicle-category allowlists are used.

They do **not** establish which components a live motorcycle or maintenance vehicle has, nor validate Unity scheduling, native pathfinding, catalog discovery or gameplay. Those require the game and selected-vehicle trace data. The adapters are test-only and are not compiled into RouteFilter.

运行：`dotnet run --project Tests/RoadCoverage/RoadCoverage.csproj -c Release`。

这些 fixture 使用轻量内存 ECS 适配器执行实际 `VehiclePrefabMatcher`，验证目标与 prefab 精确匹配、canonical/physical layout 来源、仅 physical 命中的结果校验、脱离或变更部件、Deleted/Temp 实体、Controller 循环和四跳归属上限；不使用资产名称或车型白名单。

测试**不能**证明实际摩托车、维护车的组件组合，也不验证 Unity 调度、原生寻路、资产目录发现或游戏行为。这些需要实际游戏与单车追踪数据。适配器仅用于测试，不编入 RouteFilter。
