# RouteFilter architecture / 架构

Player intent is a target and a set of forbidden prefab identifiers. Topology, candidates,
safety observations, lane leases and reroute attempts are disposable runtime state.
玩家配置只包含目标与禁止资产；拓扑、候选、安全观测、租约和请求均为可丢弃的运行时数据。

## Pipeline / 管线
RestrictionIndex → directed road/track gates → watched LaneObjects → canonical vehicles/consists.
Road candidates feed read-only safety evaluation and a bounded coordinator. Rail has a separate
observation backend; it currently performs no vehicle or lane mutations.
道路候选进入只读安全评估和有界协调器。轨道使用独立编组检测，目前不修改车辆或车道。

## Admission / 准入
Safety remains Instrumenting, not Calibrated. Road writes are therefore refused until a measured
decision point, latency bound and graph publication contract have been established.
当前安全数据处于观测阶段；未证明决策点、延迟上界与图发布契约前，不允许道路写入。
This is an unfinished reconstruction checkpoint, not a working enforcement release.
这是未完成重构的安全检查点，不是可用禁行发布版。

## Ownership / 归属
Lost lane ownership is never reasserted, even when another writer leaves an empty interval.
Original lane values are restored only on an exact match to the owned written pair.
PathOwner restoration checks the full written flags and original element index; externally changed
state is untouched. Serialize, Reset, unload and Dispose complete owned jobs before release.
车道写入失去归属后不重复覆盖；仅在当前值与自身写入完全一致时恢复原值。
寻路状态也检查完整快照，保存、重置与卸载边界先完成自有任务再释放。

## Remaining gates / 未通过项
Real game reader/writer integration, graph acknowledgement, calibrated Road admission,
asset-specific avoidance, fixed routes, independent Rail avoidance, profiler, save/reload and soak.
仍需真实游戏读写、图确认、道路校准、逐资产绕行、固定线路、独立轨道绕行、性能和存档长测。
