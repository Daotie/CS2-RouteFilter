# RouteFilter 1.0.5

## English

RouteFilter 1.0.5 keeps the mod running on game 1.6.0f1, where mods are initialized on a background thread and the Input System throws inside key binding registration.

### Fixed

- On game 1.6.0f1, mod `OnLoad` can run on a thread-pool continuation. The Input System's Temp allocator fails there, so resolving `ToggleRestrictionTool:<Keyboard>/n` inside `InputBindingResolver.AddActionMap` threw `ArgumentNullException: destination` and aborted the whole mod (the same failure also breaks other key-binding mods). Key binding registration is now retried on the main thread, where the same registration succeeds.
- Even when every registration attempt fails, the mod still starts: the shortcut key is skipped gracefully and the top-left panel button remains the way to open the panel.
- Systems that poll the shortcut actions tolerate a missing action map instead of throwing every frame.

### Compatibility

Save payload version stays 2. Saves from RouteFilter `1.0.1` and earlier still load and keep their per-entity restriction data.

## 中文

RouteFilter 1.0.5 保证在游戏 1.6.0f1 上正常运行。该版本会在后台线程初始化 mod，输入系统在按键绑定注册时会抛异常，此前会导致整个 mod 启动失败。

### 修复

- 游戏 1.6.0f1 中，mod 的 `OnLoad` 可能在后台线程执行；输入系统的 Temp 分配器在该线程必然失败，解析 `ToggleRestrictionTool:<Keyboard>/n` 时在 `InputBindingResolver.AddActionMap` 内抛出 `ArgumentNullException: destination`，导致整个 mod 初始化中止（其他注册快捷键的 mod 也以同样方式失败）。现在按键绑定注册会在主线程重试，同一注册在主线程可以成功。
- 即使所有注册尝试都失败，mod 仍会正常启动：快捷键被优雅跳过，左上角面板按钮始终可用。
- 轮询快捷键的系统在动作映射缺失时不再每帧抛异常。

### 兼容性

存档数据版本仍为 v2。RouteFilter `1.0.1` 及更早版本的存档仍可正常读取并保留逐实体限制数据。
