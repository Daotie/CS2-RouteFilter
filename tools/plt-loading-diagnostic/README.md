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
