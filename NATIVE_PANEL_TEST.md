# Native panel appearance / 原生面板外观

Build: RF2-20261002-NATIVE-PANEL-03

The popup now follows PLT's use of native CS2 theme tokens: gradient panel, dark title/footer bands, menu hover/active colors and compact typography. This is an independent visual implementation. No PLT source or runtime was added.
弹窗采用与 PLT 相同的游戏原生主题机制：渐变面板、深色标题和底栏、菜单悬停与选中颜色、紧凑文字。本次独立实现视觉效果，没有加入 PLT 源码或运行逻辑。

Position remains left 18rem / top 72rem; width remains 400rem. Selection, search filtering, grouping, Apply/Clear and Reset callbacks are unchanged. The search hint is a localized overlay because Cohtml may omit HTML placeholders. Search, Reset and checkmark use built-in game icons; Close uses a local white SVG. Asset category mapping remains unchanged.
位置保持左侧 18rem / 顶部 72rem，宽度仍为 400rem。目标选择、搜索过滤、分组、应用/清除和重置回调保持不变。搜索提示使用已有双语文字覆盖显示，避免 Cohtml 不显示 placeholder。搜索、重置与勾选使用游戏图标，关闭使用本地白色 SVG。资产类别映射保持不变。

Validation: Debug / Release and webpack builds; diff whitespace check. Game appearance and interaction are NOT GAME VERIFIED. Check modern/legacy game themes, both languages, long asset names, hover/selection/disabled state, search, Apply/Clear and Reset confirmation in game.
验证：Debug / Release、webpack 构建与 diff 空白检查。游戏外观及交互尚未经过游戏验证。需要检查新旧游戏主题、中英文、长资产名称、悬停/选中/禁用、搜索、应用/清除及重置确认。
