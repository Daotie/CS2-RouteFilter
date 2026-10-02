# Performance contracts / 性能契约

No all-city vehicle or lane query runs each simulation frame.
禁止每模拟帧遍历全城车辆或车道。

| System | Workload / 工作量 | Bound / 上限 |
|---|---|---|
| Index | Dirty restrictions and relevant network topology / 脏限制与相关拓扑 | Revision driven / 修订触发 |
| Road candidate | Watched entry lanes + their LaneObjects + matching gates / 监视车道及相关对象与门 | 4096 matches/frame, 8192 approaches |
| Safety | New candidates + periodic retained-observation sweep / 新候选及周期观测清理 | 4096 evaluations, 8192 observations |
| Road coordinator | New evaluations + active leases/attempts / 新评估及活动租约请求 | 32 leases, 64 attempts |
| Rail observation | Watched track lanes + relevant consist references / 监视轨道与相关编组 | 4096 consists/candidates |
| Restore | Pending intent during load/content events / 加载或内容事件中的待恢复意图 | 64 visits/update, 3 passes/event |
| Diagnostics | Fixed counter arrays / 固定计数数组 | Requested or opt-in window only |

Candidate, safety and rail output stores are preallocated. Overflow grandfathers traffic;
it never grows a retry queue. Topology capacity scales with restriction data on a dirty rebuild.
输出容器预分配；容量耗尽放行，不积累重试队列；拓扑容量只随脏限制重建增长。

Road reserves acquire+release budget in windows of 60 SIMULATION frames. This is not a measured
wall-clock second. No reassertion exists. Recovery release is never delayed to obey a budget.
道路按 60 模拟帧窗口预留写入与释放预算，不把它冒充实测每秒；恢复释放不因预算延期。

Completed jobs may be retired with Complete only after IsCompleted. Save/Reset/unload/Dispose
explicitly wait for owned readers/writers. Diagnostics does not wait on unfinished jobs.
普通更新只回收已完成作业；保存与重置等边界等待自有任务；诊断不等待未完成任务。

Zero restriction schedules no candidate scan/safety/coordinator/rail job once owned state is
released. FPS collapse root cause, before/after timings, large-city and soak are NOT VERIFIED.
零限制且无待释放状态时不调度上述作业。FPS 根因、前后耗时、大城市和长测尚未验证。
