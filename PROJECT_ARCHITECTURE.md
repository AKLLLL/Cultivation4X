# Cultivation4X 项目架构

## 1. 项目定位与当前闭环

Cultivation4X 是 Unity 单机修仙宗门经营原型。当前设计优先验证世界选址、洞府立宗、人物成长、宗门资源循环、来源化事件，以及宗门对外部世界的认知与影响范围。

```text
新档立宗
  -> 确定性生成世界地图并选择宗门落点
  -> 从 10 名候选者中选择 3 名核心弟子
  -> 选择宗门初始传承
  -> 命名宗门，建立唯一宗门驻地和初始影响圈
  -> 修复洞府、修炼并提升功法理解
  -> 接触青石村并取得凡人支持
  -> 建成传承方向设施，完成立宗
  -> 进入任务、设施、事件与探索循环
  -> 宗门发展触发青石村外部威胁
  -> 调查、选择战斗方案并承担村庄与宗门后果
  -> 在世界地图查看认知、影响等级、地点与宗门简报
```

技术基线：

- Unity 2022.3 LTS
- UGUI + TextMesh Pro
- Newtonsoft.Json
- `Resources/Configs` 下的 JSON 内容配置
- `Application.persistentDataPath` 下的 JSON 存档
- Unity Test Framework EditMode 自动化测试

项目没有自定义程序集定义。生产代码编译进 `Assembly-CSharp`，Editor 测试编译进 Unity 生成的测试程序集。

## 2. 目录结构

```text
Assets/
  C#/
    Data/       配置模型、存档模型和领域规则
    Manager/    系统入口、配置加载、调度和持久化
    RunTime/    Mission、NPCRuntime 等运行时对象
    UI/         场景 UI 与运行时创建的 UGUI 面板
    Utility/    枚举、物品栈、成长规则和辅助工具
    WorldMap/   世界生成、静态快照、动态进度、影响力/内容/区域规则和地图表现
  Resources/
    Configs/
      CharacterEvents/    来源化事件
      ExternalThreats/    外部威胁定义
      Founding/           立宗候选、特点和功法目录
      Items/              物品
      Missions/           常规、设施、探索和立宗任务
      Traits/             人物特质
    NPC/                  NPC ScriptableObject 模板
    Prefab/               UI Prefab
  Scenes/                 Unity 场景
  Tests/Editor/           EditMode 测试
```

## 3. 核心领域模型

### 3.1 人物

- `NPCData`：ScriptableObject 人物模板。
- `CharacterState`：可序列化的人物事实来源，保存稳定 ID、显示名、属性、资质、境界、修为、健康、特质、关系和履历。
- `NPCRuntime`：组合模板与状态，向任务、事件和 UI 提供运行时接口。

立宗候选使用确定性种子生成 10 人。玩家选出的 3 人以 `CharacterState` 保存自定义属性、资质和初始特点，读档时不依赖预制 `NPCData` 资产。`CultivationRealm.Mortal = -1`，保留旧境界枚举的既有数值。

死亡角色继续保留在存档、关系和履历中，但不能被派遣、修炼或被普通新事件选中。

### 3.2 宗门资源、村庄与劳动力

- 灵石统一为仓库物品 `LingShi_001`；任务、事件、设施升级和地图奖励只通过 `WarehouseManager` 增减，不再保存独立货币字段。
- `PlayerData.reputation` 保存声望。
- `WarehouseData.items` 保存物品；`WarehouseManager.NormalizeItems()` 合并同 ID 重复栈。灵石及带 `warehouse_capacity_exempt` 标签的物品不占物品种类槽，其余物品仍受仓库容量限制。
- `ItemData.price` 保留为以灵石计价的单位价格，本版不新增商店定价系统。
- `PlayerData.founding.village` 保存固定村庄青石村的人口、关系、劳动力总量和已预留劳动力。

凡人劳动力仅为数量资源，不创建村民实体或 AI。劳动力任务可以没有执行弟子；开始时预留数量，完成、失败或取消时释放，避免重复占用。青石村关系首次跨过支持阈值时只授予一次基础劳动力，后续关系波动不会自动补满；外部威胁可以降低人口和劳动力，并按既定顺序取消超出剩余劳动力的活动任务。

### 3.3 设施

设施类型由 `FacilityType` 和 `FacilityRules` 定义：

- 既有设施：`MissionHall`、`Warehouse`、`TrainingRoom`、`SecretRealm`、`AlchemyRoom`
- 立宗设施：`ProtectionArray`、`InheritanceChamber`、`ForgeRoom`、`FormationPlatform`

正式立宗新档允许设施为 0 级，表示损坏或尚未建立；旧档迁移后保持原有设施至少 1 级。修复和建造通过故事任务完成 0 → 1，不改写既有 `PlayerManager.TryUpgradeFacility()` 的 1 → 3 级升级路径。等级缩放查询对 0 级返回 0。

### 3.4 任务

- `MissionData`：JSON 配置，包含类型、设施需求、成本、耗时、奖励、节点及可选立宗/探索字段。
- `Mission`：运行时实例，保存执行者、剩余天数、节点、状态和待发奖励。
- `MissionManager`：统一加载、校验、派遣、推进和结算任务。

`MissionState`：

- `NotStarted`
- `Active`
- `WaitingNode`
- `Completed`
- `Failed`
- `AwaitingReward`

普通任务受任务堂等级和每日候选约束。设施行动、探索任务、立宗故事任务、劳动力任务、威胁调查和世界地图行动均复用同一状态机，不创建第二套任务系统。`isStoryAction` 任务可在任务堂为 0 时执行；`FoundingActionKind` 描述修复、村庄、劳动力、路线建设和路线行动的结算语义，`ThreatMissionKind` 标记威胁调查任务，地图行动通过 `MapMissionContext` 绑定目标格与地点。

任务成本与劳动力在启动时原子校验和预留。仓库容量不足时使用 `AwaitingReward` 保留奖励，同时释放执行弟子。

### 3.5 事件

`EventManager` 负责配置加载、来源触发、参与者绑定、收件箱、过期、效果执行和历史记录。事件内容来自 JSON 模板与受控效果枚举，不使用运行时大模型生成。

主要来源包括任务开始/节点/完成/失败、修炼、伤病、设施升级、秘境、炼丹、探索、宗门日常、招募、后续事件、立宗里程碑和外部威胁发现。立宗功法理解、村庄关系以及传承方向通过事件收件箱呈现选择和结果。

关键事件不会自动过期，并可阻止继续结束一天。普通非关键事件在立宗后的第 10、20、30……天最多抽取一条；显式 ID、关键事件、FollowUp、探索发现和威胁发现仍即时进入收件箱。旧 `RandomEventManager` 仅保留为兼容和调试入口。

### 3.6 探索与发现

旧区域探索系统（勘察/推进/驻守区域任务、`ExplorationPanel`、`PlayerData.explorationRegions` 状态与 `Configs/ExplorationRegions` 配置）已在 v14 整体移除。探索统一由世界地图探索行动（`map_explore`，见 3.8）承担：玩家在地图上派遣弟子探索格子并发现候选地点，不再有独立探索堂或区域进度状态。

### 3.7 外部威胁与战斗结算

外部威胁采用“持久化威胁状态 + 无状态数值规则”，不新增全局 Manager，也不建立实时战斗、回合、血量、技能循环或战斗 AI。

- `ExternalThreatDefinition`：从 `Resources/Configs/ExternalThreats` 加载触发条件、威胁战力、袭击周期、调查任务、准备成本和固定叙事模板。
- `ActiveThreatState`：保存在 `FoundingState.externalThreat`，记录 `Scheduled / Active / Resolved`、触发日、下次袭击日、情报、袭击次数、玩家选择和完整结算。
- `CombatPowerCalculator`：根据人物基础属性、战斗悟性、战斗经验、境界、宗门功法和预留装备参数临时计算战力，不保存冗余战力字段。
- `CombatResolver`：纯数值执行第一次交手、苦战、五档结果和撤退判定；准备、情报和先手修正统一作用于我方有效战力。
- `ExternalThreatRules`：负责青石村威胁调度、周期压力、调查情报、方案校验、后果应用和固定模板描述。

首个切片为“沾染灵气的野兽冲击青石村”：关系达到 20 后延迟 5 天激活，玩家可以反复派遣单名弟子执行调查任务，并在正面迎击、简单防御和退守洞府之间选择。战斗结果可以改变弟子伤势、经验、死亡状态以及村庄人口、劳动力和关系，但失败不会直接结束游戏。

### 3.8 世界地图、认知与宗门影响力

世界地图采用“静态生成快照 + 动态地图进度”分层：

- `WorldMap` 保存生成版本、参数快照、六边格、河流、灵脉、区域快照和兴趣点；生成完成后不写入玩法进度。
- `WorldMapProgressState` 保存显式揭示格、地图地点、影响来源、非零影响格缓存和整图脏标记。
- `WorldMapSession` 保存当前运行时地图与进度引用，不是新的全局 `MonoBehaviour` Manager。
- `WorldMapInfluenceRules` 是无 UI 依赖的规则类，负责确定性重算、认知派生和权限查询。

当前唯一真实影响来源是玩家宗门驻地：来源格 100、距离 1 为 60、距离 2 为 20，更远为 0；地图边缘自然裁剪。同宗来源按稳定 ID 排序累加并封顶 100。等级为 `None`、`Outer`、`Influence`、`Core`，阈值分别是 0、1–29、30–69、70–100。

认知和影响力相互独立：

- `revealedCellIndices` 是显式探索认知的持久化事实来源。
- 非零影响格自动视为 `Known`，但不会反向写入显式揭示列表。
- `RevealCell()` 只能产生 `Known + None`，不会授予开发权限。
- 未知格只显示暗色粗略地形，不显示危险、地点、灵脉、控制方、来源或影响值。

地图内容规则（`WorldMapContentRules`）已实现七类候选地点：村落、灵泉、灵矿、青木森林资源点、洞府、兽巢、遗迹，每类恰好一个，落点由确定性评分生成；宗门选址时会自动顺延占用格，并保证灵泉落在驻地相邻格。地点状态从 `Hidden` 经探索或提示进入 `Discovered`：

- 探索行动一次性：结算成功把目标格写入 `exploredCellIndices` 并揭示；仅当结算为优秀时才有机会发现该格上的候选地点（灵泉必发现，其余地点 65% 概率）。
- 影响范围内的隐藏地点会按确定性概率升级为 `Hinted`（“可疑线索”标记），但提示不授予交互权限；仅 `Discovered` 地点可交互。
- 各行动有影响等级门槛：调查/清理（灵泉、兽巢、遗迹）需外缘及以上，开发/建交/据点（灵矿、青木森林、村落、洞府）需影响及以上。

地点行动复用 Mission 任务状态机，模板由 `MissionManager.RegisterMapMissionTemplates()` 代码注册（非 JSON），奖励由 `WorldMapContentRules.CreateReward()` 按格子与行动计算；成功后的实际后果由 `WorldMapContentEffects` 一次性结算：灵泉每日为每名空闲存活弟子 +1 修为；灵矿和青木森林开发后每 30 天分别产出灵石与青灵草；村落关系 +15、声望 +10；兽巢清理抑制或顺延外部威胁节点；遗迹功法理解 +5；洞府据点开发后无额外设施后果（仅保留任务奖励本身）。

资源运行态保存在 `WorldMapProgressState.resourceNodes` 与 `spiritualVeins`。自然灵脉由地图已有 `SpiritVein.pathCellIndices` 与区域交集确定性派生；节点按地貌、区域灵气趋势（0.8/1/1.2）及同属性最高品阶灵脉计算月产出，并保存小数余量与已结算月份。灵脉是否已知由同区域已发现资源点推导，不另存发现标记。

区域规则（`WorldMapRegionRules`）把静态地图划分为 10 类区域（平原/森林/山脉/丘陵/山谷/荒原/泽地/湖/海/小山），每格带 32 种内部位置标签（山脚/山脊/林缘/河岸等），区域名与标签由种子确定性生成；存档校验用同一规则重建区域并与快照逐格比对，防止篡改。

## 4. 洞府立宗状态机

`FoundingState` 保存在 `PlayerData` 中，不新增全局 Manager：

```text
WorldSelection
  -> CandidateSelection
  -> TechniqueSelection
  -> SectConfirmation
  -> Cave
  -> Completed
```

状态内保存世界种子与所选世界格、候选生成种子与候选快照、核心弟子 ID、所选功法、理解度、里程碑标记和村庄数据。`SectConfirmation` 负责宗门命名；确认成功后创建唯一 `SectBase` 地点及同 ID 影响来源，再进入 `Cave`。

三条传承方向：

| 传承 | 发展方向 | 路线设施 |
|---|---|---|
| 青木长生诀 | 灵植、炼丹、恢复 | `AlchemyRoom` |
| 赤阳炼体诀 | 战斗、防御、炼器 | `ForgeRoom` |
| 太虚观想法 | 阵法、探索、神魂 | `FormationPlatform` |

功法理解度范围为 0–100。核心弟子空闲修炼时按悟性增加理解度，传承室提供额外增益；达到里程碑后通过事件呈现能力方向。当前完成条件由洞府核心修复、理解度和所选路线设施共同判定。

青石村关系达到阶段阈值后提供固定劳动力。布道、帮助村民和相关事件提升关系；路线设施通过复用 Mission 的劳动力建造任务完成。

## 5. 存档与迁移

`GameState` 当前版本为 `SaveDataVersion.Current = 16`。主要内容：

- 天数、确定性随机种子和抽取次数
- 宗门资源、设施、立宗状态、村庄和外部威胁状态
- 仓库
- 全部人物状态
- 活动任务与每日候选
- 事件历史、待触发事件、收件箱和当前事件
- 普通事件十日节奏的生成日与当日生成计数
- 最近未读日结
- 世界地图静态快照、区域快照和 `WorldMapProgressState`
- 地图内容地点（`mapSites`）、资源节点、区域灵脉、影响来源与影响缓存
- 地图任务上下文（`MapMissionContext`）

当前项目明确不兼容旧存档：`Load()` 只接受版本 16，版本更低或更高都会拒绝，并要求删除旧档后新开游戏。历史版本节点：

- v1–v3 旧档迁移为“已完成立宗”，保留既有角色和设施等级。
- v4 新档允许 0 级设施及未完成立宗状态。
- v5 增加战斗悟性、战斗经验和任务能力快照。
- v6 增加外部威胁状态、青石村一次性劳动力授予标记和普通事件节奏状态。
- v8 增加世界地图快照、生成参数和世界选址。
- v9 增加地图进度、宗门身份、唯一驻地和立宗确认。
- v10 增加持久化影响来源、非零影响缓存和严格一致性校验。
- v12 增加地图内容地点、行动上下文与后果结算，世界地图生成版本升至 4（版本号直接从 v10 跳到 v12）。
- v13 增加区域规则、区域快照与确定性重建校验，以及区域表现、认知和小块呈现。
- v14 移除探索堂设施与旧区域探索系统（区域任务、探索面板、`explorationRegions` 状态），重排 `FoundingStage` 数值为流程顺序，删除未使用的图例常量；`FoundingStage` 重排改变持久化整数值，由版本门槛直接拒绝旧档兜底。
- v16 将灵石统一为仓库物品，增加资源节点、自然灵脉派生态、30 天资源结算、物品标签稀缺度及相应严格校验。

保存前会确保影响缓存已重算。读取当前版本时只允许对“已有合法来源但派生缓存为空或标脏”的情况重算；缺失地图进度、缺失来源真源、非法索引、重复索引、错误等级、悬空来源、控制宗门冲突或干净缓存与来源不一致都会拒绝。v12 起还会校验地图地点状态/行动列表/可用交互、地图任务的上下文与奖励快照一致性；v13 起会把区域按规则重建并与快照逐格比对。`MigrateState()` 仍负责当前状态中的集合规范化，但不再承担旧版本升级。

## 6. Manager 职责

| Manager | 主要职责 |
|---|---|
| `TimeManager` | 每日推进入口、外部威胁日处理顺序、日结生成、自动保存 |
| `NPCManager` | 创建/恢复/查询角色，派遣、恢复、伤亡、关系与招募 |
| `MissionManager` | 所有任务配置加载、校验、推进、失败/取消清理、威胁调查、地图行动模板注册和待领奖 |
| `EventManager` | 来源化事件、收件箱、过期、效果和历史 |
| `PlayerManager` | 世界选址、宗门确认、宗门资源、设施、立宗状态、功法理解、村庄人口、关系和劳动力 |
| `WarehouseManager` | 物品增减、容量检查和重复栈合并 |
| `RewardManager` | 向宗门、人物和仓库发放任务奖励 |
| `SaveManager` | 捕获、保存、读取和严格验证 `GameState`、世界地图快照及动态地图进度 |
| `ItemDatabase` / `TraitDatabase` | 加载和查询 JSON 内容 |
| `UIManager` | 面板打开、关闭、Esc 返回栈及按打开顺序分配模态面板显示层级 |
| `RandomEventManager` | 旧随机事件兼容与调试 |

立宗、探索、世界地图、影响力和外部威胁切片沿用现有 Manager，没有为地图或影响力新增全局单例。地图、影响力、战斗与威胁计算位于无状态数据/规则类中；需要跨场景保留的对象通过 `DontDestroyUtility.MarkPersistent()` 处理。

## 7. 每日推进顺序

`TimeManager.EndDay()` 的既有职责和顺序保持不变：

1. 检查未关闭的日结面板。
2. 清理到期事件，并检查关键事件与收件箱容量。
3. 捕获当日开始快照。
4. 天数 +1。
5. `NPCManager.OnDayPassed()` 推进恢复和空闲修炼；立宗核心弟子的空闲修炼同时推进功法理解；随后 `WorldMapContentEffects.ApplyDaily()` 结算灵泉等每日效果。
6. 广播 `OnDayPassed`，由 `MissionManager` 推进活动任务。
7. `ExternalThreatRules.ProcessDay()` 激活威胁、施加周期袭击并取消超额劳动力任务。
8. `EventManager.ProcessDay()` 处理后续事件、整十日普通事件和招募检查。
9. 若天数是 30 的倍数，`ResourceManager.MonthUpdate()` 在事件后结算已开发资源点；当日刚开发的节点不产出，满仓损失写入结算记录。
10. 生成 `DaySettlementSummary`，按物品 ID 汇总仓库变化，并合并资源产出、任务、事件和威胁通知。
11. 自动保存。
12. 显示每日结算；弟子自主决策订阅该通知，因此能读取本次资源变化后的稀缺度。

结束日前的派遣消耗、事件选择、设施升级和待领奖领取通过既有预推进资源记录进入下一份日结。

首版影响力没有每日增长或衰减，不订阅 `OnDayPassed`，也不改变上述推进顺序。影响来源创建或修改、保存前检查和合法的读档缓存修复才会触发重算。

## 8. UI 架构

主要面板：

- `WorldMapPresenter`：世界生成结果、六边格选址、认知遮蔽、影响覆盖层、地点和地图详情。
- `SectWorldInterface`：宗门资源栏、宗门简报、宗门布局及任务堂入口。
- `FoundingPanel`：候选选择、传承选择、宗门命名、洞府修复、功法理解、村庄与路线建设入口。
- `SectPanel` / `NPCInfoPanel`：弟子列表与人物详情。
- `MissionPanel` / `MissionNodePanel`：任务、设施行动、节点选择和待领奖。
- `CharacterEventPanel`：事件收件箱、正文和选项。
- `ExternalThreatPanel`：威胁情报、调查、参战弟子、处理方案和结算记录。
- `SectDevelopmentPanel`：设施状态和既有升级入口。
- `AlchemyPanel`：炼丹设施行动。
- `DaySettlementPanel`：每日结算。
- `WarehousePanel`：仓库与物品详情。

`FoundingPanel`、`CharacterEventPanel`、`DaySettlementPanel`、`SectDevelopmentPanel` 和 `AlchemyPanel` 使用 `RuntimeUIFactory` 创建基础 UGUI 控件。UI 通过 Manager 命令接口改变状态，不应直接修改 Manager 内部集合。

`WorldMapPresenterObservability` 提供地形、温度、湿度、五行和灵脉等调试视图，并承载符号图例（`WorldMapLegendGraphic`）；影响样式由 `WorldMapInfluencePresentation.TryGetOverlayStyle()` 提供。`LegacyWorldUiGate` 隐藏旧场景世界入口。宗门内部建筑仍只存在于宗门布局界面，不占用世界地图格。

内容较多的面板使用轻量页签而不是无限延长单一滚动列表：

- `FoundingPanel`：候选前五名/后五名，跨页保留选择，确认区固定在底部。
- `MissionPanel`：洞府修复、劳动力、村庄与威胁、其他任务；待领奖固定显示。
- `ExternalThreatPanel`：威胁情报、调查、参战弟子、处理方案。
- `NPCSelectPanel`：超过 8 人后按每页 8 人分页。
- `DaySettlementPanel`：总览、任务与事件、弟子变化、资源与设施。
- `CharacterEventPanel`：正文独立滚动，事件选项始终位于滚动区外。

页签只管理展示状态，不能复制或改变任务、威胁、事件和人物数据。跨页选择仍由原面板字段保存。

## 9. 配置约定

配置继续使用 `Resources.LoadAll` 或单个 `Resources.Load`。新增 JSON 字段优先设计为可选字段，旧配置缺失时使用默认值。

重要约定：

- `material_001` 是基础材料。
- 宗门“灵材”不等同于仓库物品 `LingShi_001`。
- `Configs/Founding/founding.json` 是候选姓名、特点和三条传承的目录。
- `Configs/Missions/founding` 保存立宗故事与劳动力任务。
- `Configs/CharacterEvents/founding_*` 保存立宗里程碑事件。
- `Configs/ExternalThreats` 保存外部威胁定义；调查任务仍位于 `Configs/Missions`。
- 设施行动、探索、立宗行为和世界地图行动均复用任务系统；地图行动模板由代码注册（`MissionManager.RegisterMapMissionTemplates()`），不走 `Configs/Missions` JSON，奖励按格子与行动实时计算。
- 配置 ID 的跨文件引用由 `ConfigValidator` 和 EditMode 测试检查。

## 10. 测试与验证

EditMode 测试文件：

- `CharacterStateTests.cs`
- `FacilityLoopTests.cs`
- `EventInboxTests.cs`
- `ExternalThreatTests.cs`
- `FoundingSystemTests.cs`
- `SectVitalityTests.cs`
- `UIPaginationTests.cs`
- `UIManagerStackingTests.cs`
- `WorldMapContentEffectsTests.cs`
- `WorldMapContentTests.cs`
- `WorldMapRegionTests.cs`
- `WorldMapIntegrationTests.cs`
- `WorldMapProgressTests.cs`
- `SectFoundingIntegrationTests.cs`

自动化覆盖候选确定性与唯一性、人物与任务状态、设施循环、来源化事件、探索、宗门生命力、外部威胁两阶段结算、世界生成确定性、地图进度、认知遮蔽、影响阈值与 1/6/12 分布、多来源累加、地图内容发现/行动/后果、资源节点与灵脉派生、30/60 天月结算、区域规则与快照、presenter 标签与安全区、v16 存档校验、立宗驻地、节点失败重选、死亡清理、UI 栈和分页。

当前自动化基线：Unity 2022.3.62f3 EditMode 187/187 通过（2026-08-04 验证，见 `Logs/design-fixes-final-editmode-results.xml`）。每次合并前仍需确认：

1. Unity Editor 编译通过。
2. Resources JSON 可解析且引用有效。
3. `git diff --check` 无格式错误且没有无关场景、Prefab、字体或 ProjectSettings 改动。
4. 手动验证世界选址、宗门命名、1/6/12 影响显示、未知格遮蔽、宗门简报、候选选择、任务/威胁/探索/日结页签、资源点开发与月产出、Esc 返回，以及 v16 保存后重启恢复。

`dotnet build` 可作为快速编译辅助，但会报告 Unity/CodeCoverage 的既有程序集版本冲突；最终结果以 Unity Editor 和 Unity Test Runner 为准。

## 11. 高风险区与扩展规则

高风险区：

- `GameState`、存档版本和严格加载校验
- 世界地图生成版本、参数快照、格索引、区域快照、地图内容地点/行动上下文和 `WorldMapProgressState`
- 角色稳定 ID、自定义生成角色恢复和境界枚举值
- `TimeManager.EndDay()` 每日顺序
- Mission/Event 状态机及失败、取消、死亡、待领奖清理
- NPC 关系引用与死亡清理
- Resources JSON 字段、ID 和 Unity `.meta` GUID
- 场景、Prefab、TMP 字体、Manager 初始化顺序

扩展原则：

- 新玩法优先复用 `Mission`、`EventManager`、`CharacterState` 和 `FacilityRules`。
- 不为未来需求提前增加全局 Manager、复杂技能树、人口 AI、多宗门外交或战争 AI。
- 世界生成数据与动态地图进度必须分离；影响力只能通过格索引和 `HexCoord.Distance()` 计算。
- 认知与影响等级必须分离，UI 不得自行复制行动权限判断。
- 新事件效果必须补配置校验和测试。
- 新存档字段必须明确版本、默认值、严格校验及旧档是否直接拒绝。
- 新任务行为必须覆盖完成、失败、取消、角色死亡、读档和 UI 路径。

具体协作规则、风险审批和变更预算以根目录 `AGENTS.md` 为准。

## 12. 历史切片：第一次宗门生命力提升

`CharacterState` 新增 `baseCombatComprehension` 与 `combatExperience`，保留既有 `attack` 序列化字段，并在玩家界面显示为“力量”。无状态的 `CharacterCapabilityRules` 统一计算战力和任务评分，不增加 Manager。战力由力量、敏捷、体质、战斗悟性、封顶战斗经验、境界、当前宗门功法和预留装备参数组成。

功法目录的传承配置新增 `tags` 与受理解度门槛控制的 `effects`。当前效果仅覆盖每日修为、战力、理解增量和匹配任务评分，均作用于全宗门；不建立个人功法栏或技能树。

`MissionData` 可配置战力门槛、偏好功法标签、偏好性格与优秀分数。派遣时的评分和档位写入 `MissionSaveData` 快照；能力不足沿用失败处理，达标使用原奖励，优秀只使灵石物品与修为奖励增加 50%。战斗类任务按失败、达标、优秀分别提供 1/3/5 点战斗经验。

候选列表和弟子详情显示战力及其基础构成；任务档位同时进入每日结算和人物履历。洞府概览显示当前传承标签、理解度与已解锁效果，不承载派遣行动。

任务来源改为状态驱动：立宗前显示洞府、村庄、劳动力和路线事务；立宗后普通任务按声望开放阶级。旧 `dailyMissionCandidateIds` 仅保留为确定性兼容查询。`MissionHall` 枚举和旧存档字段继续兼容，但不再是任务门槛，不能升级且不会出现在建设界面。`MissionPanel` 是“宗门事务”入口，`FoundingPanel` 只显示洞府概览。

立宗新档只有 5 份基础材料；洞府修复仍复用现有 Mission。护山阵把普通任务失败伤势从 3 天降为 1 天。立宗完成后，未修复的洞府与村庄事务仍可执行。

该切片落地时存档版本为 v5；当时的迁移会以普通悟性补齐旧生成弟子的战斗悟性、将战斗经验安全归零，并为缺少能力快照的活动任务补算一次。

此后 v6 加入外部威胁、青石村劳动力授予标记和事件生成节奏；v8–v10 继续加入世界地图、立宗驻地和影响力；v12–v13 加入地图内容地点/行动/后果与区域规则；v14 移除探索堂并重排立宗阶段枚举。当前写入版本以第 5 节所述 v14 为准。
