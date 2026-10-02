# Rail enforcement design / 轨道禁行设计

## Verified facts / 已核对事实
Game 1.6.0f1 TrackLane has no CarLane-style blockage interval. GetTrackDriveSpecification does
not publish HasBlockage. PathOwner IS serialized: zero TrackLane writes alone never proves save
safety. Obsolete by itself does not make a pathfinder avoid a target.
TrackLane 无道路式 blockage 字段；轨道图不发布 HasBlockage。PathOwner 会序列化；只不写
TrackLane 不能证明存档安全。仅设置 Obsolete 不能提供目标绕行约束。

## Current backend / 当前实现
Independent directed track gates watch relevant LaneObjects. Physical members canonicalize to
the consist path owner through controller/layout information; a consist is detected once per scan.
Train navigation validates the immediate gate. Tram is excluded from the road candidate backend.
The rail backend performs ZERO TrackLane and ZERO PathOwner writes. Candidate diagnostics only.
独立有向轨道门监视相关对象，车厢归一为编组所有者；验证下一门，有轨电车不进入道路后端。
当前轨道后端仅诊断，不修改 TrackLane 或 PathOwner。

## Unresolved mechanism / 尚未解决的机制
A safe, bounded, preferably request-specific exclusion primitive has not been demonstrated.
Native graph UpdateAction exists, but changes affect shared seekers and are asynchronous;
ownership, publication acknowledgement, vanilla rebuild conflicts and release must be proven.
Absence of CarLane blockage does NOT prove that every rail approach is impossible.
尚未证明安全的、最好逐请求的排除机制。原生图更新存在，但影响共享寻路并异步执行，需要
证明归属、发布确认、原生重建冲突和释放。缺少道路 blockage 不代表所有轨道方案均不可能。

Fixed-line passenger/freight/subway/tram failure behavior, station approach, reversing,
alternative routes, target deletion, track rebuild and mixed ownership need actual game tests.
固定线路、车站、折返、替代路径、目标删除、轨道重建与混合归属仍需真实游戏测试。

STATUS: observation implemented; safe avoidance primitive UNRESOLVED; enforcement NOT IMPLEMENTED;
game/save/performance verification NOT TESTED. Do not report reroute-only as working rail restriction.
状态：观测已实现；安全排除机制未解决；轨道禁行未实现；游戏、存档和性能未测试。
