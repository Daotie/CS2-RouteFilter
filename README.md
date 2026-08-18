# NodeGate

**Per-vehicle access control for road and rail nodes in Cities: Skylines II.**

NodeGate adds a top-left in-game button and a compact vehicle picker. Select one or more exact vehicle types, then left-click a road or track node to restrict it; right-click clears the node. The interface automatically follows the game's English or Simplified Chinese language.

> Current status: **0.1.0-beta.1**. This is a test release, not yet recommended for irreplaceable saves.

## Highlights

- 20 independently selectable types: private cars, taxis, delivery vehicles, buses, trams, passenger/cargo/subway trains, bicycles, and individual city-service vehicles.
- Road and rail node support.
- Visible in-game UI button; `Ctrl+Shift+X` remains an optional fallback.
- English and Simplified Chinese auto-switching.
- Restrictions are serialized into the save.
- Configurable exact gate, optional vanilla broad-category rerouting, emergency override, and look-ahead distance.

## Beta behavior

Cities: Skylines II exposes pathfinding flags for broad groups, but not every individual service vehicle. NodeGate therefore stores exact type masks and enforces them at the node entrance. With exact-only mode, a blocked vehicle may wait at the gate instead of calculating an ideal detour. Optional native rerouting is deliberately off by default because it affects broader groups.

Back up important saves before testing. Please include `Cities: Skylines II/Logs/Player.log`, game version, NodeGate version, and a reproducible save or screenshot in bug reports.

## Build

Requirements: Cities: Skylines II with the official modding toolchain, .NET SDK, and Node.js 18+.

```powershell
dotnet build NodeGate.csproj -c Release
cd UI
npm install
$env:NODEGATE_OUTPUT_DIR = (Join-Path (Get-Location) 'build')
npm run build
```

## License

Copyright © 2026 Daotie. Licensed under the [GNU General Public License v3.0](LICENSE).

---

# NodeGate 节点通行

**《都市：天际线 II》道路与轨道节点的精确车型通行管理模组。**

NodeGate 在游戏左上角加入按钮和车型选择面板。选择具体车型后，左键点击道路或轨道节点应用限制，右键清除；界面会随游戏语言在简体中文与英文间自动切换。

> 当前状态：**0.1.0-beta.1**。这是测试版本，不建议直接用于无法替代的重要存档。

## 主要功能

- 20 种独立车型：私家车、出租车、配送车辆、公交、有轨电车、客运/货运/地铁列车、自行车，以及逐项区分的市政服务车辆。
- 支持道路和轨道节点。
- 有可见的游戏内 UI 按钮；`Ctrl+Shift+X` 仅作为备用。
- 中英双语自动切换。
- 节点限制随存档保存。
- 可配置精确闸门、原生大类绕行、应急车辆保护与前瞻车道数。

## Beta 行为说明

游戏原生寻路只提供交通大类限制，无法表达每一种服务车辆。因此 NodeGate 保存精确车型掩码，并在节点入口执行拦截。只启用精确模式时，被拦车辆可能在闸门前等待，而不是立刻找到完美绕行路线。原生大类绕行默认关闭，因为它可能影响比所选车型更广的车辆。

测试前请备份重要存档。提交问题时请附带游戏版本、NodeGate 版本、`Player.log`，以及可复现存档或截图。

## 许可证

版权所有 © 2026 Daotie，以 [GNU GPL v3.0](LICENSE) 开源。
