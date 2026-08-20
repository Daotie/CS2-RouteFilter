# RouteFilter 1.0.4

## English

RouteFilter 1.0.4 fixes the occasionally incomplete vehicle catalog, enlarges the asset panel, and moves the per-frame enforcement scan off the main thread.

### Fixed

- The vehicle catalog was built only once, so modded assets that finished loading afterwards (for example CR400AF trains) were permanently missing. The catalog now rebuilds once after each save finishes loading and whenever vehicle prefabs or content availability actually change; opening the panel never triggers a rebuild.
- The manual refresh button used the "↻" character, which is missing from the game UI font and rendered as a box; it now uses a proper icon.
- The panel is taller and adapts to the screen size and UI scale, showing more asset rows while reserving the game's top bar and bottom toolbar so the panel edge never covers them.
- The tool system no longer runs a net raycast every frame while the tool is inactive, and no longer overwrites the vanilla tool selection while it should not run.

### Performance

- Enforcement scanning now runs in a parallel Burst chunk job that returns only matching vehicles; the main thread cost no longer scales with the whole city.
- The pathfinding pipeline is skipped entirely while no detour barrier is active.
- Restriction badges are drawn by one batched parallel job instead of one job per restricted target.
- The restriction index rebuild reuses its allocations, keeping steady-state rebuilds garbage free.

### Compatibility

The save payload version stays 2. Saves from RouteFilter `1.0.1` and earlier still load and keep their per-entity restriction data.

## 中文

RouteFilter 1.0.4 修复了偶发的车辆目录不完整问题，扩大了资产面板，并把每帧执法扫描移出主线程。

### 修复

- 车辆目录原先只构建一次，之后才加载完成的模组资产（如 CR400AF 系列）会永久缺失。现在每次存档加载完成后自动重建一次，并在车辆预制件或内容集实际变化时重建；打开面板不会触发任何重建。
- 手动刷新按钮原使用游戏字体缺失的“↻”字符而显示为方框，现改用正规图标。
- 面板更高，随屏幕尺寸与 UI 缩放自适应，能显示更多资产行，同时预留游戏顶栏与底部工具栏区域，面板边缘不再遮挡它们。
- 工具未激活时不再每帧进行射线检测，也不再覆盖原版工具的选中状态。

### 性能

- 执法扫描改为并行 Burst 分块任务，只把命中的车辆交回主线程，主线程开销不再随城市规模增长。
- 无绕行屏障时寻路系统完全跳过 pathfind 管道。
- 禁行标牌由单个批量并行任务绘制，不再按目标逐个调度任务。
- 限制索引重建复用已有集合，稳态重建零垃圾回收压力。

### 兼容性

存档数据版本仍为 v2。RouteFilter `1.0.1` 及更早版本的存档仍可正常读取并保留逐实体限制数据。
