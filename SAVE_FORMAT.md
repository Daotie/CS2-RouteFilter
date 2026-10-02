# Save boundary / 存档边界

Current payload is the existing version 2 player-restriction format; this checkpoint does not
change it. It writes targets through the game's Entity writer and prefab names. Entity remapping,
unknown future schema protection, corrupt records and V1/V2 migration remain **NOT VERIFIED**.
当前仍是既有 version 2 玩家限制格式；本检查点不修改格式。目标通过游戏 Entity writer 写入，
资产保存 prefab 名称。Entity 重映射、未来版本保护、损坏记录及 V1/V2 迁移仍**未验证**。

RouteFilterLaneLease is a native runtime record, not an ECS/save component. No lease, candidate,
safety verdict or diagnostic state is added to the RouteFilter payload.
RouteFilterLaneLease 是 native 运行时记录，不是 ECS/存档 component；payload 不新增 lease、
candidate、Safety verdict 或诊断状态。

**Game.Net.CarLane.Serialize writes both blockage bytes.** Local API audit confirms this.
The lease system therefore runs release before Game.Serialization.SerializerSystem in the
Serialize phase; the restriction serializer itself is not a sufficient pre-save barrier.
**原生 CarLane.Serialize 会写入两个 blockage 字节**，已从本地游戏实现确认。
因此在 Serialize 阶段、原生 SerializerSystem 之前释放 lease；不能仅依赖限制数据的 Serialize 方法。

Release restores the exact original pair only while current equals written. A conflict ends
ownership without overwriting the other system's value. No global blockage/path cleanup.
释放只在 current 精确等于 written 时恢复原始值；冲突时结束 ownership，不覆盖其他系统。
不执行全城 blockage 或 PathOwner 清理。

The hook ordering and active-lease→save→quit→reload behavior require game validation. No claim
of save safety follows from compilation. Reset→save→reload must separately confirm zero restrictions.
钩子顺序及 active lease→保存→退出→重载必须实测；编译成功不证明存档安全。
Reset→保存→重载须另行验证限制为零。
