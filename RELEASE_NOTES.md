# RouteFilter 2.1.0-dev — emergency UX correction test build

Build: `RF21-20261005-UX-33`.

Restores the 2.0 panel foundation and Reset footer, unifies Node/Segment and All/Favorites/Recent selectors, restores small independent stars, and replaces secondary action walls with anchored menu rows and contextual preset editing. Copy/Paste shows counts and leaves Paste pending until Apply. Recent follows successful application only. The persistent five-parameter wheel palette sits upper-right with Reset/Finish. Auxiliary plates use the native primary front bounds. The cached road map uses an explicit pixel viewport/world matrix, layers, fit, pan, zoom and target selection.

Adds Escape priority, centralized teardown and secondary failure containment. Localized signage semantics from SIGN-32 remain. Stable enforcement and city-save schema are unchanged. P0–P5 are IMPLEMENTED + BUILD VERIFIED; game, performance and save/load acceptance remain pending. Deployment preserves the complete compatibility playset. See `UNIFIED_TEST_REPORT.md` and `EMERGENCY_ACCEPTANCE_CHECKLIST.md`.

## RouteFilter 2.0.0 — Previous stable release

## English

- Rebuilt road and rail asset restrictions around directed entry detection and request-local native pathfinding exclusion.
- Matching vehicles reroute when a legal alternative exists. A reliably confirmed no-path result ends the vehicle through a vanilla-compatible termination/deletion lifecycle; it does not enter a parking or retry loop.
- Restriction detection follows watched entry lanes and relevant lane objects instead of scanning every vehicle in the city. Fixed the severe FPS drop while selecting a target with simulation paused.
- Refreshed the panel and asset catalog, restored native mod settings, and added confirmed Reset RouteFilter recovery. Bulk Allow All / Forbid All now affects only the filtered asset list.
- English and Simplified Chinese are supported. Restrictions remain city-save configuration; enforcement runtime state is rebuilt after loading.
- Road rerouting and road/rail no-alternative termination passed player game testing of the release candidate. This does not imply every asset, network layout or mod combination has been tested.

### Known issue

**Some vehicles may be missed by restriction detection and pass through a restricted target. This is a known limitation in 2.0.0; we will investigate and attempt to fix it in a future update.**

## 简体中文

- 重构道路与轨道逐资产禁行，基于有向入口检测和单次请求的原生寻路排除执行限制。
- 有合法替代路线时车辆绕行；可靠确认无合法路线后，通过兼容原版的任务终止／删除流程结束车辆，不进入停车等待或反复重试。
- 检测工作量随受监视入口车道和相关车道对象增长，不再扫描全城车辆；修复暂停状态选择目标时的严重掉帧问题。
- 更新面板和资产目录，恢复游戏原生模组设置，加入需确认的“重置 RouteFilter”恢复功能；“全部允许／全部禁止”只作用于筛选后的资产列表。
- 支持英文和简体中文；存档保存玩家禁行配置，执行过程的运行时状态在加载后重建。
- 发布候选版的道路绕行及道路／轨道无替代路线终止已通过玩家游戏测试；不代表所有资产、路网和模组组合都已验证。

### 已知问题

**禁行检测可能漏掉部分车辆，使其仍能通过受限目标。这是 2.0.0 的已知限制，后续更新将调查并尝试修复。**
