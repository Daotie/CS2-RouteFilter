# Performance contract / 性能约定

Status: static audit and build only. CPU/GC/large-city measurements **NOT TESTED**.
状态：目前仅静态审计与构建；CPU、GC、大城市性能 **未测试**。

| System / 系统 | Actual workload / 实际工作量 | Fast path / 快速路径 |
| --- | --- | --- |
| Index | Restricted targets and their local topology, dirty or periodic rebuild / 受限目标及局部拓扑，dirty 或周期重建 | Empty cached queries return without arrays / 无限制时查询为空即返回 |
| Candidate | Watched lanes + relevant LaneObjects, with linear-list dedup/first-seen overhead / watched lanes 与相关对象，另有线性列表去重及追踪开销 | No watched gates: no scan / 无 gate 不扫描 |
| Safety | Candidates × retained observations in current lookup implementation, plus observation cleanup / 当前查找为候选数乘追踪记录数，另有记录清理 | No watched gates/new scan: no evaluation / 无 gate 或新扫描不评估 |
| Lease probe | New evaluations + active leases, **cap one active lease** / 新评估与活跃 lease，最多一条 | Unarmed and no lease: immediate return / 未请求且无 lease 时立即返回 |

The Candidate/Safety linear searches are investigation leads, not proof of the reported
130→32 FPS regression. They do **not yet** satisfy the intended linear scaling contract.
Candidate/Safety 的线性查找是调查线索，尚不能证明其为 130→32 FPS 的根因；当前实现**尚未达到**预期的线性增长约定。

## Lease probe resources / Lease 资源

- Persistent NativeList capacity 1 and five native counters; disposed on system destroy.
  固定容量 1 的 persistent NativeList 与五个计数；系统销毁时 Dispose。
- One Burst job only when armed or holding a lease. Safety reader dependency is registered;
  writable CarLane lookup participates in ECS dependencies. One barrier ECB per active update,
  including updates without writes: its allocation cost must be measured, not claimed zero.
  仅请求测试或存在 lease 时提交一个 Burst job；注册 Safety reader，车道写入参与 ECS 依赖。
  活跃更新每次创建一个 barrier ECB（即使未写入），其分配成本必须实测，不能声称为零。
- No city vehicle/lane query, per-target EntityQuery, LINQ, path writes or repeated admission.
  不查询全城车辆/车道，不建立逐目标查询，无 LINQ、寻路写入或重复 admission。
- Poll IsCompleted before normal completion; unfinished jobs are not forced to finish for counters.
  Lifecycle release deliberately synchronizes at Clear/Reset/save/preload/dispose, and can stall.
  正常完成前检查 IsCompleted；计数不强制等待。Clear/Reset/保存/切城/卸载会同步释放，可能产生等待。

## Game measurements / 游戏测量

Compare 0/1/10/100 restricted targets and downtown/highway cases against the same city without
RouteFilter restrictions. Record job CPU, watched lanes, scanned objects, candidates, safety count,
topology rebuilds, leases, structural changes, GC, main-thread waits. Reroute count must be zero in 1D.
对同城无 restriction 基线比较 0/1/10/100 目标及市中心/高速场景，记录 job CPU、watch lanes、
扫描对象、候选、评估、拓扑重建、lease、结构修改、GC 与主线程等待。1D 的 reroute 必须为零。
