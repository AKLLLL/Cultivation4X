# Cultivation4X 项目当前状态（2026-08-22 更新）

本文档记录世界地图与旧玩法融合、资源生态、弟子个性自主行为和炼气期成长接入后的当前基线。

## 1. 概览

- 当前分支：`agent/influence-integration`，远端：`origin`。
- 当前里程碑：资源生态月结、弟子 Utility AI 自主行为、炼气纳气与月度计划已进入同一条 `TimeManager → NPC/Mission/Event → 日结/存档` 流程。
- 存档版本：`SaveDataVersion.Current = 18`；不兼容旧档，更新后从新档测试。
- 世界地图生成版本保持现状，本次未修改 `WorldGenerator`、`TerrainRenderer`、Camera、场景或 Prefab。
- Unity EditMode 全量：**444/444 通过**（failed=0 / skipped=0）。

## 2. 已完成系统

### 2.1 世界地图与旧玩法融合

- `WorldMapData / WorldCell / HexGeometry / Terrain` 是唯一地图数据源。
- `MapSiteData` 保存地点玩法状态，`WorldLocation` 只作为地图与交互门面。
- 地点行动由 `WorldLocation.availableActions` 与 `availableMissionIds` 驱动，统一进入既有 Mission/Event/TimeManager 流程。
- Hidden/Hinted 地点不会通过 HUD、行动页或地图图标泄露；只有 Discovered 内容可见。
- 宗门、村庄、仓库、设施和地点任务继续复用既有面板，没有建立第二套地图玩法。

### 2.2 资源生态与月结

- `WarehouseData.items: List<ItemStack>` 仍是物品库存唯一真源，所有增减通过 `WarehouseManager`。
- 地图资源节点状态位于 `WorldMapProgressState.resourceNodes`；地点开发状态仍由 `MapSiteData` 决定。
- 区域天然灵脉从既有 `SpiritVein.pathCellIndices` 与 Region 关系派生，不复制世界地图灵脉数据。
- `ResourceManager.MonthUpdate` 每 30 天结算一次，使用 `lastSettledMonth` 保证幂等；结算日刚开发的节点从下月开始产出。
- 仓库无法接收新品类时，本月产出记为损失并进入日结，不延迟补发。
- 宗门事务按实际执行人日累计：每 5 人日产出 1 基础材料，余数保存在 `sectDutyWorkCredit`。

### 2.3 弟子个性自主行为

- `DiscipleDecisionManager` 只在日结后调度；Goal/Utility 评分位于 `DiscipleAIEvaluator`，执行统一桥接到既有 Mission。
- 个性、特质、能力、资源环境和关系目标影响弟子选择，但 AI 不直接写奖励、关系或成长状态。
- 自主 Mission 只能使用月计划中的自由日，启动时必须同时满足剩余自由预算和月末前可完成。
- 玩家手动 Mission 优先级最高，可无损中止无节点的 Active AI Mission；取消不受伤、不触发失败事件、不写失败日结。
- 原 AI“自由修炼”改为“研读传承”，只提升个人功法掌握度，不再触发炼气纳气、自动突破或闭关心境惩罚。

### 2.4 炼气期成长与月度计划

- `CharacterState.cultivation` 现表示当日累计灵气（0–100），每天开始归零；长期成长由 `naqiProgress`（0–100）保存。
- 所有灵气来源统一走 `NPCRuntime.AddCultivation → NaqiGrowthRules.AddDailyAura`，按增量曲线结算，避免拆分奖励重复获益。
- 首次达到 100 当日灵气只完成一次大周天，并增加固定纳气与功法掌握奖励。
- 宗门传承 ID 的唯一真源仍是 `FoundingState.selectedTechniqueId`；角色只保存个人 `techniqueMastery`。
- 月计划每 30 天一月；当前月锁定，只能编排下月；第 0 天可编排第 1 月。未制定计划或新入门弟子默认全部自由。
- 每弟子计划为修炼/宗务/自由三项，10% 步进且合计 100%；默认启用方案为 50/20/30。
- 手动 Mission 或伤病占用当天时，确定性欠额调度器仍消耗原计划类别，不在以后补做。
- 灵石辅助按实际修炼日原子消耗 1 下品灵石；不足时按普通纳气结算并提示。
- 灵气紊乱使用稳定的“存档种子 + 日期 + 角色 ID”判定；关键事件提供服用调息丹、暂停十日、继续承受三种处理。
- 新增调息丹：炼丹房固定 2 天，消耗 10 灵石与 2 基础材料，产出 2 枚，不受设施等级产量缩放。
- 旧 `expReward` 在炼气 V1 冻结，不再转化为纳气，也暂未迁移为角色等级经验。

## 3. 时间与数据流

```text
结束今天
  → CurrentDay + 1
  → 弟子恢复、月计划类别记账、计划修炼/宗务
  → 地图每日效果
  → Mission 推进
  → 外部威胁与 Event
  → 每 30 天资源月结
  → DaySettlementSummary
  → 自动存档
  → 日结后 DiscipleDecisionManager 选择下一项自主 Mission
```

玩家手动 Mission 可覆盖自主行动，但不会返还已经实际使用的自由日或当天被覆盖的计划类别。

## 4. 验证基线

| 项目 | 当前结果 |
|---|---|
| Unity 编译 | Assembly-CSharp / Assembly-CSharp-Editor 0 错误 |
| EditMode 全量 | **444/444 通过** |
| 新增成长/计划定向测试 | 月界、精确比例、纳气曲线、大周天、AI 月末限制、取消、预算转移、紊乱倒计时、灵石消耗均覆盖 |
| JSON 配置 | 新增事件、物品、Mission 与 AI 配置均可解析，物品引用有效 |
| diff 检查 | `git diff --check` 通过 |
| Play Mode | 未执行，需用户授权后手测 |

测试证据：`Logs/naqi-v18-final-results.xml`（本地日志，不提交）。

## 5. 已知限制与手测清单

1. 只实现炼气期“纳气”，没有预埋经脉、脏腑、通脉、化灵或筑基数据结构。
2. 旧 Mission 的 `expReward` 当前不产生角色成长；未来若启用等级经验，需要单独设计并迁移配置。
3. SampleScene 旧按钮/旧物件仍由 `LegacyWorldUiGate` 隐藏；清理时必须同时修改场景与 gate。
4. 建议 Play Mode 手测：
   - 第 0 天制定首月计划，第 30/31 天切月并确认当前月不可编辑；
   - 手动 Mission 中止 AI 行动、自由预算不回退、月末禁止跨月启动；
   - 灵石充足/不足两种纳气结算及 100% 后剩余训练日转自由；
   - 灵气紊乱三种选项、重复紊乱重置十日、关键事件阻止推进；
   - 调息丹固定产出 2 枚，炼丹房互斥与仓库容量表现；
   - 资源节点第 30/60 天月结、仓库满时损失记录、宗务人日余数读档恢复；
   - 弟子个性是否形成可读的不同自主选择与经历记录。

## 6. 下一步建议

1. 先完成上述 Play Mode 闭环验收，再调整纳气收益与紊乱概率数值。
2. 若恢复 `expReward`，先明确它属于角色等级经验还是其他成长线，禁止重新写入纳气。
3. 若扩展资源生产或弟子成长，继续复用 MapSite/WorldLocation、Warehouse、Mission/Event/TimeManager，不新增平行系统。
