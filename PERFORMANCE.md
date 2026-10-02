# Performance contracts / 性能契约

No all-city vehicle or lane query runs each simulation frame. No physical barrier entity is created.
不逐帧扫描全城车辆/车道，不创建物理障碍实体。

| System / 系统 | Work grows with / 工作量随什么增长 | Bound / 上限 |
|---|---|---|
| Index / 索引 | Dirty restrictions and relevant local topology / 脏限制与相关局部拓扑 | Revision driven / 修订触发 |
| Road candidate / 道路候选 | Watched lanes + relevant LaneObjects + matching gates / 监视车道、相关对象与门 | 4096 candidates, 8192 observations |
| Safety / 安全 | New candidates + periodic retained-observation pruning / 新候选及周期清理 | 4096 evaluations, 8192 observations |
| Road coordinator / 道路协调 | New evaluations + active approaches / 新评估与接近记录 | 64 records; 4 requests/64 simulation frames |
| Rail backend / 轨道后端 | Watched tracks + relevant consist members + matched gates / 监视轨道、相关编组与门 | 4096 candidates, 64 records; 4 requests/64 frames |
| Native queue hook / 原生队列钩子 | Actual native enqueue events: O(1) owner map lookup, not a vehicle scan / 原生入队事件，常数查询 | 8 exclusions/64 frames; 8 transactions |
| Query transaction / 查询事务 | Target lanes + returned path length + normal native explored graph / 目标车道、路径长度与原生搜索规模 | 128 lanes, 256 edges; at most 2 native queries/action |
| Restore / 配置恢复 | Pending intent on load/content events / 加载事件中的待恢复配置 | 64 visits/update, 3 passes/event |
| Diagnostics / 诊断 | Fixed counters / 固定计数 | Requested or opt-in window only |

Native hook cost includes a prefix on every actual native path query, even unselected owners.
It does not enumerate vehicles. With no restrictions it exits before owner/component reads.
原生事件钩子仍有每请求前缀成本，但不枚举车辆；零限制时在读取车辆前退出。
Each transaction joins ALL native worker graph read/write handles. The selected graph runs a managed
IJob invoking the native path executor; other graph copies no-op. This can delay other readers and
is not a claim of zero performance cost. Graphs are not cloned and city graph edges are not scanned.
事务汇合所有原生图依赖，选中图运行托管作业调用原生执行器；有调度与等待成本，但不克隆或遍历全图。
The native search itself retains its existing graph-size/exploration costs. Bounded request rate
does not bound the duration of a single vanilla search. Profile query duration and graph contention.
原生搜索仍有自身搜索规模成本；请求限额不等于单次搜索时间上限，必须测量图等待与查询时长。

Scratch arrays are allocated per admitted query, not per frame, and disposed after all job copies
complete. Other pipeline stores are preallocated; topology allocations occur only on dirty rebuild.
查询临时数组按实际准入分配，所有副本完成后释放；其他容器预分配，拓扑仅脏重建时分配。
Overflow grandfathers traffic, never growing a retry queue. Native fallback runs once within the same
action. Zero restrictions and no owned state schedule no candidate/safety/coordinator/rail work.
溢出放行，不积累重试；无替代仅在同一请求放行一次。零限制且无自有状态不调度业务作业。

Regular updates retire only completed jobs. Explicit save/Reset/unload/Dispose (and exceptional
partial-scheduler recovery) may wait for owned work. Diagnostics never force-completes unfinished work.
普通更新只回收已完成作业；保存、重置、卸载或异常调度恢复可等待自有任务，诊断不强制等待。
FPS, before/after timings, large-city, no-restriction overhead and soak are **NOT GAME VERIFIED**.
FPS、前后耗时、大城市、零限制开销及长测均未游戏验证。
