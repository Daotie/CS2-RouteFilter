# Phase 1D game gate / 游戏验证关口

Build ID: `RF2-20261002-LEASE-PROBE-01`.
This is a one-shot primitive, **not working road enforcement**. Phase 1E is not implemented.
这是一次性 primitive，**不是已完成的道路禁行**；尚未实现 Phase 1E。

## Entry check / 前置检查

Use a small disposable test city and ensure exactly one RouteFilter version is enabled.
Confirm the build ID in the panel/log. Apply one target/asset restriction. In native Settings,
use “1D: attempt one lease on selected target”, then “1D: log lease probe counters”.
使用小型测试城市，仅启用一个 RouteFilter 版本。核对面板/日志识别码；对一个目标设置一个资产限制。
原生设置中点击“1D：对选中目标尝试一次 lease”，再记录计数。

**Known gate:** Phase 1C currently supplies Instrumenting calibration, so its verdicts cannot
admit a lease. A zero-admission probe validates refusal only, not ownership/restoration.
Do not fabricate Safe or enable normal enforcement to bypass this. Calibration and graph
publication measurement must be obtained in the test environment before positive admission.
**已知关口：** 当前 1C 使用 Instrumenting 参数，不能 admission。创建数为零只验证拒绝路径，
不验证 ownership/恢复；禁止伪造 Safe 或开启正常 enforcement 绕过。正向测试前必须取得实测校准与图发布证据。

## Mandatory positive checks / 必测正向项目

- One fresh calibrated Safe candidate, empty forward next lane: one `[0,1]` byte interval only;
  no whole lane/target blocking and no second lease. Reverse/missing/occupied/blocked lanes pass.
  当帧已校准 Safe、空正向 next lane：只写一条 `[0,1]` 字节区间；反向、缺失、占用或已有阻塞时放行。
- Capture CarLane→Updated→LanesModified→UpdateAction→pathfind graph evidence.
  Updated removal alone is **not** proof. No publication instrumentation exists yet.
  记录完整图发布链；Updated 消失不证明发布。当前尚无完整发布探针。
- Exact hard expiry, unchanged written values restore original; other-system overwrite survives.
  Verify Clear, Reset, target/lane deletion, preload, disable/dispose, active-lease save/reload.
  到期恢复原始值；其他系统改写必须保留。验证 Clear、Reset、目标/车道删除、切城、卸载和活跃 lease 保存重载。
- Measure frame/job CPU, GC, main-thread stalls and structural churn against no restriction baseline.
  与无限制基线比较 CPU、GC、主线程等待和结构修改。

**STOP:** Until these pass, do not implement Phase 1E. After 1E implementation, immediately test
specified-asset detours, unaffected other assets, no permanent stops/residual blockage and expected
performance; merely triggering a restriction is not acceptance.
**停止关口：** 上述未通过，不实现 1E。1E 实现后立即测试指定资产绕行、非指定资产不受影响、
无永久停车/残留 blockage，以及预期性能；restriction 触发不算验收通过。
