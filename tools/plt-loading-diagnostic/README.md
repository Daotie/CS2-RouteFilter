# PLT loading diagnostic / PLT 加载诊断

Upstream: https://github.com/KruemelGames/ParkingLotTool
Base: 992bea469dee6267c24b5eed7768ee03de433c72. GPL-3.0; see LICENSE.

Apply upstream-changes.patch at the upstream repository root, then copy ParkingLotTextureLoadDiagnostic.cs into its Tools directory. The patch includes the previous bounded clone/rebind optimization, build-output overrides, and diagnostic writer disposal. This folder is development evidence, not a RouteFilter runtime system.

在上游基线仓库应用补丁，并将 ParkingLotTextureLoadDiagnostic.cs 放入 Tools。补丁包含此前分批加载修改及诊断资源释放。本目录不参与 RouteFilter 运行。

## Result / 状态

The bounded optimization failed to resolve the 2026-10-02 15:00:19 city-load crash. The verified optimized module still crashed in AreaBatchSystem.Load -> Material.SetTextureImpl. The renderer already limits pending texture loading to one per UpdateTextures invocation. PLT causality and memory exhaustion are not proven.

分批优化未解决此次载城崩溃。不能断言 PLT 是唯一根因，也不能断言内存溢出。

## Diagnostic / 诊断

Harmony observes only the private AreaBatchSystem.Load(ref TextureData, Material, int) entry and return. The original method runs unmodified. Logs record BEGIN/END, frame, material/batch name, texture asset identity/path/dimensions/format where exposed. Each entry is flushed to disk before native execution. Native crashes cannot be caught by a managed finalizer; unmatched BEGIN supplies the next investigation target, not proof that the named asset is defective.

诊断记录原生调用前后的 BEGIN/END，保留原方法；最后未配对记录用于定位，不等同于证明资产损坏。不改存档，不过滤纹理，不修改材质、车道或车辆。

Additional hot-path work follows existing renderer batch slots; disk writes follow actual pending texture loads, capped at 4096 per process. Diagnostic synchronous disk flushing can affect loading speed. Remove the diagnostic after locating the failing call. The writer closes on mod disposal, and logging faults disable logging without suppressing rendering.

额外工作随渲染批次检查增长，磁盘写入随实际待加载纹理增长，最多4096次。同步诊断可能拖慢加载；定位后移除。

Debug / Release / webpack: 0 errors, 3 existing upstream warnings. Diff check passed. Diagnostic GAME VERIFIED: no.
Release deployed SHA256: 34B3A3943DAFC968F499F91ABEEFBA213A83B1F16D6F28F4FAEE7C9D1DC6944E
Current playset retains 369 entries; subscribed PLT 161156 remains disabled. Only local PLT package replaced, backup retained outside game.

Next test: load the affected backup city once. Do not overwrite the original save. Inspect Logs/PLT-texture-load-*.log and Player.log together after exit/crash.
下一步：加载问题城市备份一次，保留原存档；退出或崩溃后对照纹理诊断和 Player.log。

## Deferred recovery test / 手动延迟恢复测试

Current test build registers existing menu-time prefabs as before, but pauses new PLT surface clone publication and obsolete-surface rebinding from city preload until the player explicitly starts recovery. This is a scoped recovery delay, not deferred loading of the entire mod or all game textures. The runtime gate resets on every city preload and is never stored in city restriction/save data. The button is disabled before city loading completes and after a successful one-shot start; repeated clicks do not restart the recovery queue.

本测试版保留主菜单阶段必要的 prefab 注册；城市预载入开始后，暂停新的地表克隆发布及失效地表重绑定。每次进城都需要手动启动，状态只在内存中存在。不是延迟全部模组加载，也不会延迟游戏本体所有纹理。

Options -> Mods -> Parking Lot Tool -> Start deferred surface recovery.
选项 -> 模组 -> Parking Lot Tool -> 开始延迟地表恢复。
The new action and description support English and Simplified Chinese. Existing upstream settings are not fully translated by this patch.
新增按钮与说明支持英文、简体中文，本补丁未全面翻译上游原有设置。

Test: load the affected backup, leave recovery stopped and observe for a few minutes. If stable, click the button once and compare behavior. Do not overwrite the original city save. If it crashes before the button is clicked, post-load recovery does not explain that crash; check texture diagnostics, existing registered clones and other content rather than claiming success.
测试：先保持恢复暂停，观察数分钟；稳定后只点击一次恢复。若点击前就崩溃，不能归因于点击后恢复。不要覆盖原存档。

Extra waiting-state cost is a boolean branch; it avoids queue capture/rebind/clone registration while waiting. Existing upstream seeding calls remain. Resumed workload follows pending clones and relevant recovery records under the previous caps.
等待状态新增开销为布尔判断，不扫描恢复队列、不发布新克隆；上游已有种子检查保留。恢复后工作量随待建克隆和待恢复记录增长。

Debug / Release / webpack: 0 errors, 3 existing upstream warnings. Diff check passed; compiled DLL inspection confirms the gate precedes clone registration and surface repair.
GAME VERIFIED: no. Deploy verified all 14 package files; playset remains 369 entries and old subscribed PLT disabled.
Module ID: a2d88dca-84a3-4996-886d-ef88b76bb138
Release SHA256: 9248E8439C37D1BC3B5201C161DBF432E27B4CC6169D57C4D70022DA732AFCD7
