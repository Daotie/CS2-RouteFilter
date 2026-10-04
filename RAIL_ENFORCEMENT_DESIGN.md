# Rail enforcement / 轨道禁行

## Native facts / 原生依据

Game 1.6.0f1 TrackLane has no CarLane blockage interval and GetTrackDriveSpecification emits no
HasBlockage. Native PathfindJobs.DisallowConnection rejects an edge whose method mask has no
intersection with the request. This includes Track. Lack of physical blockage is not a rail blocker.
TrackLane 无道路阻塞字段，但原生寻路会拒绝方法掩码不匹配的边，包括 Track；因此仍能逐请求排除。
The native queue's private generic graph modification scheduler joins all graph readers/writers.
The test build uses that exclusive transaction boundary, not shared cost/rule edits or CarLane writes.
测试构建使用原生图独占读写边界，不使用共享成本修改或道路阻塞。

## Executable backend / 实际执行后端

Directed TrackLane gates watch relevant LaneObjects. Controller chains and LayoutElement[0] identify
one authoritative consist head; members cannot independently reroute. Road explicitly excludes Train.
只监视相关轨道对象，以控制器链和 LayoutElement[0] 找到编组所有者，整列只请求一次；道路排除 Train。
Navigation must confirm the next target lane or the exact endpoint-connector/target sequence.
Current train front position is float4.y; traversal endpoint is float4.w. Remaining distance uses
a Bezier chord lower bound. Admission requires finite speed/braking, consist length, clean PathOwner
and distance beyond initial latency + braking + consist geometry + uncertainty margin.
列车当前位置为前端 float4.y、终点为 w，以曲线弦长保守估算；核对导航、制动、编组长度和剩余距离。
One Obsolete requests the original vanilla query. The shared bridge uses a rail-specific target lane
set, temporarily removes Track eligibility under exclusive ownership, runs the original native query,
verifies its returned path avoids those lanes and compare-restores the method snapshots.
只置一次 Obsolete；以独立轨道集合执行临时排除，原生查询结束后核对结果并比较恢复。

## Fixed lines and failure / 固定线路与失败

Passenger/freight trains, subway and tram retain native stops/destinations. A detour is possible only
if an alternative connects the SAME native origin and destination. RouteFilter does not redraw lines,
skip stations, reverse, teleport or inject custom routes. A restricted stop/end target may have no
detour and must be grandfathered. Already committed/late approaches are likewise grandfathered.
保持原生目的地和站点；仅绕行同一起终点，不改线路、跳站、折返、传送或注入路线。无路或过晚则放行。
No alternative triggers at most one unexcluded query in the same action after full overlay release.
No repeated Obsolete, pending retry or custom train recovery exists. Admission deadline is 240 frames;
terminal dedupe records remain within a 64-record bound until the approach ends.
无路时恢复排除层，在同请求内最多查询一次放行路径；无重复请求或自定义恢复。

## Ownership, resources and verification / 归属、资源与验证

No TrackLane ECS field is written. PathOwner undo uses an exact full-state/element comparison.
Graph overlay and all transaction data are runtime-only. Reset, Clear, Apply, target deletion,
save and unload close or cancel owned work; other mod/vanilla fields are not reset globally.
不写 TrackLane，寻路撤销严格匹配快照；排除层不保存，重置等边界释放自身状态，不清全城原生字段。
See ARCHITECTURE and PERFORMANCE for native hook compatibility and concurrency limits.
**Status: actual exclusion and one-shot reroute IMPLEMENTED; gameplay/scheduling compatibility,
train/subway/tram detours, fixed-line behavior, profiler and save/reload NOT GAME VERIFIED.**
**状态：实际排除与一次绕行已实现；游戏调度、各轨道类别绕行、固定线路、性能和存档未游戏验证。**
