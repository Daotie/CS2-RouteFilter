# RouteFilter 1.0.3

## English

RouteFilter 1.0.3 is a performance and reliability release. It removes the per-frame scanning bottleneck behind the reported FPS drops, makes saved restrictions carry the exact forbidden asset lists, and cleans up vehicles that cannot reroute.

### Fixed

- Reworked exact-asset enforcement into inverted lookup indexes: vehicles whose prefabs are not restricted anywhere are skipped with a single set lookup, and lane ownership is resolved from a precomputed map. This eliminates the per-frame O(vehicles × lanes × owner depth × forbidden assets) scan that caused severe CPU load and FPS drops.
- Save payload upgraded to version 2: forbidden asset lists are stored as stable prefab names and re-applied after loading, so reloading a save restores both the restricted locations and the exact restricted assets.
- Vehicles that still have to cross a restricted target after rerouting, or that stay stopped past the ~1 second reroute timeout, are removed through the game's built-in cleanup instead of retrying forever.
- Mod settings and key bindings are now loaded before bindings are registered, so option changes persist across game restarts.

### Added

- While the RouteFilter tool is active, restricted nodes and segments show a flat prohibition tag floating above the target, sized close to the selection highlight.

### Compatibility

The save payload version is now 2. Saves from RouteFilter `1.0.1` and earlier still load and keep their per-entity restriction data; new saves store the forbidden asset lists inside the versioned payload.

## 中文

RouteFilter 1.0.3 是性能与可靠性版本：消除导致 FPS 骤降的每帧扫描瓶颈，让存档完整保存被禁行的具体资产，并清理无法绕行的车辆。

### 修复

- 将逐资产拦截改为反转索引查询：与任何禁行清单都无关的车辆每帧只需一次集合判断即可跳过，车道归属改为查预计算映射，消除了每帧 O(车辆数 × 车道数 × 层级 × 禁行资产数) 的扫描。
- 存档数据升级为 v2：禁行资产清单以稳定的 prefab 名称保存并在读档后重新解析，重载后恢复的不只是禁行位置，还有具体禁行资产。
- 重规划后仍必须经过禁行处、或超过约 1 秒绕行超时仍停驻的车辆，交由游戏自带清理机制移除，不再无限重试。
- 选项与按键绑定改为先加载后注册，重启游戏后设置不再丢失。

### 新增

- 工具启用时，有禁行的节点与路段上方显示平面禁行标签，大小接近选择高亮框。

### 兼容性

存档数据版本升级为 v2。RouteFilter `1.0.1` 及更早版本的存档仍可正常读取并保留逐实体限制数据；新存档会把禁行资产清单写入版本化数据块。
