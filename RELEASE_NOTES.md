# RouteFilter 0.5.0-beta.1

## English

This beta rebuilds the asset panel for responsiveness and clarity. The catalog is now sent only when it changes, while the list uses a game-compatible scrollable flex layout instead of unsupported grid CSS.

Nodes receive a red circular hover outline and segments a dashed highlight. The panel filters road or rail assets according to the hovered target, clearly states that selected entries are forbidden, and shows maximum speed in km/h, acceleration, and braking when an asset is hovered.

Where the game exposes an explicit fixed-trailer or multiple-unit relationship, RouteFilter groups carriages beneath their tractor or engine. A collapsed group action applies to the engine and listed carriages; expanding it enables individual selection.

Target editing now follows an explicit select, configure, and apply workflow. Left-click selects a node or segment, right-click cancels that selection, and panel actions apply or clear restrictions only on the selected target. Panel input is isolated from world clicks, segment hits resolve through owned lanes, and the asset list adds pagination alongside scrolling. Catalog-wide allow/forbid actions always affect every asset.

Selecting a target now loads only that target's saved forbidden list, while bulk controls remain pending until the explicit apply action. Enforcement checks current and upcoming navigation lanes, underlying path elements, node endpoints, and every prefab in a vehicle consist. It runs before vehicle movement so road vehicles, fixed-route trains, and outside traffic cannot continue normally through a matching restriction.

This remains a beta release. Verify scrolling, target detection, grouping, node and segment application, detours, and save/reload behavior on a backed-up city.

## 中文

本 Beta 重构了资产面板的响应与表达。资产目录只在内容变化时发送，列表使用游戏 UI 引擎兼容的可滚动 Flex 布局，不再采用不受支持的 Grid CSS。

节点现会显示红色圆形悬浮线框，路段显示虚线高亮。面板会根据悬浮目标筛选道路或轨道资产，明确提示“选中即禁行”，并在悬浮资产时显示以 km/h 为单位的最高速度、加速度和制动减速度。

当游戏提供明确的固定挂车或动车组关系时，RouteFilter 会将车厢归入牵引车辆。折叠状态下操作分组会同时作用于车头及所列车厢；展开后可以逐项选择。

目标编辑现采用明确的“选择、配置、应用”流程：左键选中节点或路段，右键取消选择，面板中的应用或清除操作只作用于所选目标。面板鼠标输入已与地图工具隔离，路段命中会从子车道追溯到所属路段；资产列表在滚动之外增加分页兜底，全部禁行/放行始终作用于完整目录。

选中目标时只加载该目标保存的禁行清单，批量操作在明确点击应用前仅修改待应用清单。执行检测现覆盖当前及前方导航车道、底层路径元素、路段端点节点和车辆完整编组，并在车辆移动前运行，使道路车辆、固定线路列车和过境交通都不能继续正常穿过匹配的限制目标。

本版本仍处于 Beta 阶段。请在有备份的城市中验证滚动、目标识别、分组、节点与路段应用、车辆绕行及存读档行为。
