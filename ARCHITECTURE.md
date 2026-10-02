# RouteFilter architecture / 架构

Player intent is a target and forbidden prefab identifiers. Topology, candidates, observations,
approach records and query transactions are disposable runtime state, never part of the save schema.
玩家配置只包含目标与禁止资产；拓扑、候选、观测、接近记录与查询事务不写入模组存档。

## Executable pipeline / 实际执行管线

RestrictionIndex → directed gates → watched LaneObjects → canonical vehicle/consist → safety
admission → one PathOwner.Obsolete → original vanilla setup request → request-local exclusion →
native pathfind → restore exclusion → original native result adoption.
道路与轨道分别识别候选，只为导航明确、距离足够的指定资产请求一次原生绕行。
Node excludes its internal traversal lanes; Segment excludes its internal lanes. A short Segment
endpoint connector adds a local upstream watch and requires the exact connector/target sequence.
节点排除内部通行车道；路段排除其内部车道。短端点连接车道增加局部上游监视，并核对连续导航。

## Primitive and publication / 排除原语与图更新

Game 1.6.0f1 LaneDataSystem recalculates CarLane blockage on refresh; a physical blockage is
shared by every seeker. TrackLane has no matching field. Neither supports selective prefab isolation.
原生刷新会重算道路阻塞，且物理阻塞影响所有寻路者；轨道没有对应字段。
The test build therefore replaces physical lane leases with an exclusive native graph transaction.
This deliberately replaces the earlier CarLane-lease proposal; it is not a claimed CarLane implementation.
测试构建改用独占原生图查询事务，明确替代之前的 CarLane 租约方案。

Harmony intercepts only an admitted owner's Enqueue(PathfindAction...). Capture happens during
CompleteSetup; dispatch happens AFTER native Queue.OnUpdate schedules graph updates. The native
private ScheduleModificationJob<T> supplies all worker graph reader/writer dependencies.
在原生队列图更新调度后执行事务，沿用原生所有读写依赖，避免读取上一代图。
An atomic claim runs exactly one native query on one graph. Only target edge method masks are saved,
temporarily set to zero and compare-restored in finally. Other graph copies do nothing; other
readers cannot observe the overlay. The native result queue receives exactly one completed action.
原子认领只执行一份查询；临时保存并清零目标边的方法掩码，finally 比较恢复。其他读者不能看到排除层。
Start/end targets, methods, weights, events and destinations remain supplied by vanilla.
原生仍负责起终点、权重、事件、目的地和路径采用，不注入自造路径。
VehicleUtils.SetupPathfind consumes Obsolete into Pending; native navigation may clear its cache
before CompleteSetup. Admission snapshots the confirmed transition, destination and gate endpoint.
Capture validates the exact expected Pending flags, unchanged destination/current entry and fresh
distance; a vanished navigation cache alone is not a refusal. Any surviving cache must still match.
原生 Pending 会清空导航缓存；使用已核对的准入快照，再检查准确状态转换、目的地、当前车道和实时距离。

## Lifetime and failures / 生命周期与失败

Admission expires absolutely after 240 simulation frames; no retry or deadline extension exists.
The active overlay lasts one synchronous native query and is restored before publishing its result.
This is NOT a preemptive wall-clock timeout: an already running native query cannot be interrupted
safely halfway through. Graph writer contention and large-query duration require game profiling.
准入窗口为 240 模拟帧；排除层仅存于一次同步原生查询内。它不是可中途抢占的墙钟超时，需游戏测量。
No alternative (or a path still containing a forbidden lane) triggers at most one unexcluded native
fallback within the SAME action. No second Obsolete/request is created. This grandfathers traffic
instead of stranding it. A failed fallback has normal native failure semantics, not custom recovery.
无替代路线最多执行一次同请求的原生放行查询；不再次置 Obsolete，不增加恢复循环。
Budget overflow, ambiguous navigation and late approaches pass without exclusion.
预算耗尽、导航不明或接近过晚时放行。

## Ownership and save boundaries / 归属与保存边界

No CarLane/TrackLane fields, Transform, speed or vanilla global flags are cleared. PathOwner undo
requires the complete written flag snapshot AND original element index to match. Save's Serialize
phase, Reset, target edits, preload and Dispose release owned transactions before removing caches.
不改道路/轨道阻塞和车辆运动；寻路请求撤销仅在完整快照匹配时恢复。保存和重置等边界先释放事务。
Runtime graph edits are absent from ECS serialization; approach records and transaction arrays
are not serialized. Player intent remains save schema 4 with older migration and opaque retention.
图排除不进入 ECS 序列化；仍沿用已有配置格式及迁移保护。

## Verification / 验证

Road and Rail execution paths are implemented and enabled in the test build. ConservativeInitial
uses vanilla AI's 16-frame cadence and 64-frame setup horizon with additional bootstrap allowance,
clamped to 2.4–4 seconds; this is a policy estimate, NOT measured latency. Invalid data remains refused.
道路和轨道测试执行路径已开启；初始预算有原生节奏依据，但不冒充实测延迟。
Private hook compatibility, native scheduling, actual reroutes, allowed traffic, no-alternative behavior,
save/reload, profiler and soak remain **NOT GAME VERIFIED**. Hook signature/dispatch failure disables
admission, logs FAILED and returns unscheduled actions to vanilla; it must not be reported as working.
私有接口兼容、实际绕行、放行与存档长测均未游戏验证；安装失败必须报告失败。
