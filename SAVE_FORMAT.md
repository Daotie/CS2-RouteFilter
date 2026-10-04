# Save format / 存档格式

Only player intent is persisted. No lease, attempt, candidate, verdict or topology is serialized.
仅持久化玩家意图，不保存租约、请求、候选、安全判断或拓扑。

## Wire format / 二进制布局
The game supplies an outer component-system block. Legacy V1/V2 start with an integer version.
Their Entity fields are ONE entity-table index, remapped by IReader.Read(Entity).
Schemas 3/4/5 start with an integer byte count, then RFLT magic, ushort schema, ushort flags,
a UTF-16 prefab table and target records. Target kind is an int, not a byte.
游戏提供外层块。旧 V1/V2 使用游戏实体表映射；新版长度后保存完整 RFLT 头、名称表与目标记录。

The runtime and offline tests share RestrictionByteSource/RestrictionByteSink. String length
is consumed exactly once. Empty prefab table entries retain their indices.
运行时与离线测试共用字节编码器；字符串长度只读一次，空名称不会改变后续索引。

## Containment / 保护
Unknown schemas, malformed bodies and partial parses lock editing and enforcement. The ENTIRE
original schema-3/4/5 body is re-emitted, including magic/version/flags. Explicit Reset discards it
and unlocks. Unresolved names/targets remain intent and are saved; Clear/Apply discard pending
intent for the edited target so it cannot reappear later.
未知或损坏数据锁定编辑和执行，保存完整原文。显式重置才丢弃并解锁。
缺失资产与目标保留；清除或应用目标会移除该目标的待恢复记录。

Malformed unframed legacy counts cannot be safely resynchronized: loading fails rather than
guessing or silently overwriting. Corruption of the game's outer frame or native length prefix
is NOT covered by offline containment tests and requires actual game integration validation.
旧版无独立记录边界，损坏计数拒绝猜测。游戏外层块与原生长度损坏不在离线验证范围内。

Geometry identity is exact and unique, never nearest-neighbour. Coincident nodes, parallel edges
or duplicate prefab names fail closed. This format's geometry identity is not a universal
identity solution for all future network edits; it remains a documented compatibility limitation.
几何目标必须精确且唯一，不猜最近目标；重叠或重复名称不自动绑定。
几何身份仍有兼容边界，不能声称覆盖所有未来道路修改。

Schemas 4/5 use CRC32 over the entire intent body; schema 3 remains readable without a checksum.
格式 4/5 对完整意图进行 CRC32 校验，旧格式 3 无校验格式仍可读取。
Both encode and framed load enforce a 64 MiB payload limit. Integrity verification is O(payload bytes), load/save only.
编码与新版载入限制为 64 MiB；校验仅在加载和保存时执行，成本随意图字节数增长。

Resolved intent retains the full original name set, independent of current prefab entities. Known target movement updates its identity; a known deleted target retires instead of rebinding to a future road.
已解析意图仍保留完整名称集合，不依赖当前 prefab 实体。已知目标移动会更新身份，已知删除的目标会退役而非绑定到未来道路。

## Native protocol diagnostic / 原生协议诊断
The settings action “Test native save protocol (isolated memory)” exercises the actual game
BinaryWriter/BinaryReader with temporary buffers, distinct writer/reader entity maps, a Chinese
legacy name, current-schema data and following-data sentinels. Production native framing and name
helpers are shared with this fixture. It never calls the live persistence system or writes files.
“测试存档原生协议（隔离内存）”使用游戏真实读写器、临时缓冲、不同实体映射表、中文旧版名称、
当前格式数据与后续哨兵值。共用生产代码的原生读写辅助函数，不调用城市的持久化系统或写文件。

This diagnostic is implemented and build-verified, but NOT GAME TESTED. Even a PASS only proves
these primitive protocol checks, not serializer scheduling, network identity, prefab restore,
truncated outer native blocks, or full city save/load. Read the build ID in its result log.
此诊断已实现并编译验证，尚未游戏执行。即使 PASS，也仅验证协议基础接口，不能替代序列化顺序、
道路身份、资产恢复、原生外层块截断与完整城市保存载入测试。结果日志包含构建识别码。
## Direction configuration (schema 5) / 方向配置（格式 5）

Each target record appends an entry count: `-1` means ALL, `0` means NONE, and
`1..64` supplies the enabled logical entries shared by all forbidden road prefabs.
V1/V2 restores and schema 3/4 records have no direction field and retain ALL behavior.
每个目标追加入口数量：`-1` 为全部，`0` 为无，`1..64` 为启用禁行的逻辑入口；
所有被禁止的道路车型共享入口集合。旧格式按全部入口解释。

An entry records an exact road connection identity (endpoint anchors and length),
the actual entering endpoint anchor, curve midpoint and road prefab identifier.
Connection endpoint order is normalized for identity comparison; the entering endpoint
is never swapped. Geometry/road replacement that cannot resolve exactly never borrows
a nearby direction. Runtime groups, lane entities and world UI geometry are not saved.
入口保存精确道路连接、真实进入端点、曲线中点和道路 prefab 名称。连接起止顺序
归一化，但进入端点不交换；替换道路后不能精确匹配时，不会借用邻近方向。
运行时分组、车道实体和世界 UI 几何不写入存档。

Semantically invalid but fully parsed entry identities retain the forbidden prefab
record as ALL, with a load warning. Missing identities or shared native graph edges
disable custom editing and preserve ALL behavior with a localized explanation.
CRC/framing failures continue to lock and preserve the entire original payload.
完整解析但身份非法的方向记录保留车型禁行并回退全部入口，加载时警告。
身份无法解析或共用图边时阻止自定义编辑，并明确显示原因；校验或结构损坏
仍锁定并保留整个原始数据。

Road direction configuration does not change rail target restrictions.
道路方向配置不改变轨道的整体目标禁行。
