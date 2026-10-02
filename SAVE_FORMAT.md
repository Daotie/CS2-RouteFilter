# Save format / 存档格式

Only player intent is persisted. No lease, attempt, candidate, verdict or topology is serialized.
仅持久化玩家意图，不保存租约、请求、候选、安全判断或拓扑。

## Wire format / 二进制布局
The game supplies an outer component-system block. Legacy V1/V2 start with an integer version.
Their Entity fields are ONE entity-table index, remapped by IReader.Read(Entity).
Schemas 3/4 start with an integer byte count, then RFLT magic, ushort schema, ushort flags,
a UTF-16 prefab table and target records. Target kind is an int, not a byte.
游戏提供外层块。旧 V1/V2 使用游戏实体表映射；新版长度后保存完整 RFLT 头、名称表与目标记录。

The runtime and offline tests share RestrictionByteSource/RestrictionByteSink. String length
is consumed exactly once. Empty prefab table entries retain their indices.
运行时与离线测试共用字节编码器；字符串长度只读一次，空名称不会改变后续索引。

## Containment / 保护
Unknown schemas, malformed bodies and partial parses lock editing and enforcement. The ENTIRE
original schema-3/4 body is re-emitted, including magic/version/flags. Explicit Reset discards it
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

Schema 4 writes CRC32 over the entire intent body; schema 3 remains readable without a checksum.
新写入 schema 4 对完整意图进行 CRC32 校验，旧 schema 3 无校验格式仍可读取。
Both encode and framed load enforce a 64 MiB payload limit. Integrity verification is O(payload bytes), load/save only.
编码与新版载入限制为 64 MiB；校验仅在加载和保存时执行，成本随意图字节数增长。

Resolved intent retains the full original name set, independent of current prefab entities. Known target movement updates its identity; a known deleted target retires instead of rebinding to a future road.
已解析意图仍保留完整名称集合，不依赖当前 prefab 实体。已知目标移动会更新身份，已知删除的目标会退役而非绑定到未来道路。
