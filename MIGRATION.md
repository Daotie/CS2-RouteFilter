# Migration / 迁移

V1/V2 byte-layout fixtures model a single Entity TABLE INDEX, not raw Entity.Index/Version.
The live adapter uses IReader.Read(Entity), defers target geometry/prefab resolution until
ModificationEnd, and converts to schema-3 player intent. Missing assets are not guessed.
V1/V2 实体字段只有一个表索引；运行时由游戏映射，延后解析几何和资产，转换为新版意图。

Framed schema-3 bodies are bounded and decoded with the same production codec as offline tests.
Future bodies are preserved byte-for-byte; corrupt/partial bodies remain locked and unchanged.
新版使用生产编码器；未来数据原样保留，损坏或部分解析数据锁定且不覆盖。

Run `dotnet run --project Tests/SaveFormatTests` and `dotnet run --project Tests/LeaseRules`.
Fixtures cover UTF-16 alignment, empty name slots, current round trips, each truncation offset,
future headers including nonzero flags and empty bodies, legacy layout/counts, ownership and expiry.
离线测试涵盖字符串对齐、空名称、新版往返、逐字节截断、未来头部、旧版布局、归属与过期。

These are NOT proof of live migration. Real V1/V2 saves, remapped ECS identities, missing targets,
Reset → save → reload, active mutation serialization and upgrades need game verification.
离线通过不代表真实迁移完成；真实旧存档、实体重映射、重置保存重载与升级仍需游戏验证。

Schema 4 writes CRC32 over the entire intent body; schema 3 remains readable without a checksum.
新写入 schema 4 对完整意图进行 CRC32 校验，旧 schema 3 无校验格式仍可读取。
Both encode and framed load enforce a 64 MiB payload limit. Integrity verification is O(payload bytes), load/save only.
编码与新版载入限制为 64 MiB；校验仅在加载和保存时执行，成本随意图字节数增长。
