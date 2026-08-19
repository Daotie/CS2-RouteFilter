# RouteFilter 1.0.0

## English

RouteFilter 1.0.0 is the first stable release of the exact vehicle-asset access tool for Cities: Skylines II road and rail networks.

### Player-facing changes

- Opening the RouteFilter panel now activates the selection tool immediately. The redundant in-panel tool activation button has been removed, and closing the panel closes the tool.
- The default remappable panel shortcut is now `Ctrl+Shift+N`.
- The panel uses viewport-aware top and bottom boundaries. The target selector, asset list, paging controls, and application buttons remain on one screen; only the asset list scrolls.
- Maximum speed is displayed in km/h, with acceleration and braking available on asset hover.
- Each selected node or segment loads its own saved forbidden list. Bulk selection changes remain pending until the player explicitly applies them to that target.
- Road and rail catalogs are filtered by target compatibility, and recognized engines, carriages, and trailers can be controlled as a group or individually.

### Restriction enforcement

- Exact prefab matching covers the vehicle controller and all recognized assets in its consist.
- Detection checks current lanes, upcoming navigation lanes, underlying path elements, and restricted node endpoints.
- Enforcement runs after road and rail navigation updates but before vehicle movement.
- Matching vehicles request a new path while the selected target is temporarily presented as unavailable to the native pathfinder.
- If a fixed transit route or disconnected network offers no valid detour, the matching vehicle is stopped and may retry instead of continuing normally through the restriction.
- Outside traffic is evaluated through the same exact-asset path.

### Save compatibility

Version 1.0.0 retains the asset-level V1 save schema used by `0.4.0-beta.1` and `0.5.0-beta.1`. Existing restrictions created by those RouteFilter versions remain structurally compatible.

Please report reproducible compatibility issues with the game version, active traffic/network mods, reproduction steps, and `Player.log`.

## 中文

RouteFilter 1.0.0 是首个正式版本，为《都市：天际线 II》道路与轨道路网提供精确到车辆资产的通行控制。

### 玩家可见改动

- 打开 RouteFilter 面板时会立即启用选择工具，不再显示重复的“打开工具”按钮；关闭面板时工具也会关闭。
- 默认且可重新绑定的面板快捷键改为 `Ctrl+Shift+N`。
- 面板根据视口上下边界自适应，目标选择、资产列表、分页和应用按钮保持在同一页面，只有资产列表内部滚动。
- 最高速度以 km/h 显示，悬浮资产时同时显示加速度和制动减速度。
- 每个节点或路段会加载自己保存的禁行清单；批量选择仅修改待应用清单，明确点击应用后才写入目标。
- 道路与轨道资产会按目标兼容性筛选，已识别车头、车厢和挂车可分组或逐项控制。

### 禁行执行

- 准确预制资产匹配覆盖车辆控制实体及编组内所有已识别资产。
- 检测覆盖当前车道、前方导航车道、底层路径元素和受限节点端点。
- 执行顺序位于道路、轨道导航更新之后以及车辆实际移动之前。
- 命中车辆请求新路径时，目标会短暂以不可用状态提供给原生寻路系统。
- 固定公共交通线路或断开路网没有有效绕行路线时，匹配车辆会停车并可能重试，而不是继续正常穿过限制。
- 过境交通使用相同的逐资产检测链路。

### 存档兼容

1.0.0 保留 `0.4.0-beta.1` 与 `0.5.0-beta.1` 使用的逐资产 V1 存档结构，这些 RouteFilter 版本创建的限制在结构上保持兼容。

报告可复现的兼容性问题时，请提供游戏版本、当前交通/路网模组、复现步骤和 `Player.log`。
