# Cultivation4X 项目架构

## 1. 项目定位与当前闭环

Cultivation4X 是 Unity 单机修仙宗门经营原型。当前设计优先验证人物成长、宗门资源循环、来源化事件，以及“从破败洞府到建立宗门”的新手流程。

```text
新档立宗
  -> 从 10 名候选者中选择 3 名核心弟子
  -> 选择宗门初始传承
  -> 修复洞府、修炼并提升功法理解
  -> 接触青石村并取得凡人支持
  -> 建成传承方向设施，完成立宗
  -> 进入任务、设施、事件与探索循环
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
  Resources/
    Configs/
      CharacterEvents/    来源化事件
      ExplorationRegions/ 探索区域
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

- `PlayerData.gold` 保留旧字段名，UI 表示宗门数值资源“灵材”。
- `PlayerData.reputation` 保存声望。
- `WarehouseData.items` 保存物品；`WarehouseManager.NormalizeItems()` 合并同 ID 重复栈。
- `PlayerData.founding.village` 保存固定村庄青石村的人口、关系、劳动力总量和已预留劳动力。

凡人劳动力仅为数量资源，不创建村民实体或 AI。劳动力任务可以没有执行弟子；开始时预留数量，完成、失败或取消时释放，避免重复占用。

### 3.3 设施

设施类型由 `FacilityType` 和 `FacilityRules` 定义：

- 既有设施：`MissionHall`、`Warehouse`、`TrainingRoom`、`SecretRealm`、`AlchemyRoom`、`ExplorationHall`
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

普通任务受任务堂等级和每日候选约束。设施行动、探索任务、立宗故事任务和劳动力任务均复用同一状态机，不创建第二套任务系统。`isStoryAction` 任务可在任务堂为 0 时执行；`FoundingActionKind` 描述修复、村庄、劳动力、路线建设和路线行动的结算语义。

任务成本与劳动力在启动时原子校验和预留。仓库容量不足时使用 `AwaitingReward` 保留奖励，同时释放执行弟子。

### 3.5 事件

`EventManager` 负责配置加载、来源触发、参与者绑定、收件箱、过期、效果执行和历史记录。事件内容来自 JSON 模板与受控效果枚举，不使用运行时大模型生成。

主要来源包括任务开始/节点/完成/失败、修炼、伤病、设施升级、秘境、炼丹、探索、宗门日常、招募、后续事件和立宗里程碑。立宗功法理解、村庄关系以及传承方向通过事件收件箱呈现选择和结果。

关键事件不会自动过期，并可阻止继续结束一天。旧 `RandomEventManager` 仅保留为兼容和调试入口。

### 3.6 探索与发现

探索系统只提供宗门外部世界入口，不包含世界地图、坐标、地块或势力系统。三个预设区域位于 `Resources/Configs/ExplorationRegions`，状态保存在 `PlayerData.explorationRegions`。

探索复用任务系统：

- `Survey`：勘察未知区域，全局同时最多一个。
- `Progress`：推进已发现区域，每个区域同时最多一个。
- `Ongoing`：最终发现后的持续驻守，每个区域同时最多一个。

区域进度由 `ExplorationRules` 推进，发现事件进入 `EventManager` 收件箱，奖励继续走现有奖励与仓库路径。`ExplorationPanel` 是当前验证 UI，不代表未来世界地图方案。

## 4. 洞府立宗状态机

`FoundingState` 保存在 `PlayerData` 中，不新增全局 Manager：

```text
CandidateSelection
  -> TechniqueSelection
  -> Cave
  -> Completed
```

状态内保存候选生成种子与候选快照、核心弟子 ID、所选功法、理解度、里程碑标记和村庄数据。

三条传承方向：

| 传承 | 发展方向 | 路线设施 |
|---|---|---|
| 青木长生诀 | 灵植、炼丹、恢复 | `AlchemyRoom` |
| 赤阳炼体诀 | 战斗、防御、炼器 | `ForgeRoom` |
| 太虚观想法 | 阵法、探索、神魂 | `FormationPlatform` |

功法理解度范围为 0–100。核心弟子空闲修炼时按悟性增加理解度，传承室提供额外增益；达到里程碑后通过事件呈现能力方向。当前完成条件由洞府核心修复、理解度和所选路线设施共同判定。

青石村关系达到阶段阈值后提供固定劳动力。布道、帮助村民和相关事件提升关系；路线设施通过复用 Mission 的劳动力建造任务完成。

## 5. 存档与迁移

`GameState` 当前版本为 `SaveDataVersion.Current = 4`。主要内容：

- 天数、确定性随机种子和抽取次数
- 宗门资源、设施、立宗状态、村庄和探索区域
- 仓库
- 全部人物状态
- 活动任务与每日候选
- 事件历史、待触发事件、收件箱和当前事件
- 最近未读日结

`SaveManager.MigrateState()` 负责旧字段默认值和版本迁移：

- v1–v3 旧档迁移为“已完成立宗”，保留既有角色和设施等级。
- v4 新档允许 0 级设施及未完成立宗状态。
- 加载旧版本存档并迁移前，会建立 `.pre-v4` 备份作为回滚点。
- 加载后恢复人物、任务、事件和仓库，并规范化重复物品栈。

兼容要求：不得改名已有 JSON 字段、境界旧枚举值、角色稳定 ID 或 `MissionState`；新增存档字段必须有默认值和迁移测试。

## 6. Manager 职责

| Manager | 主要职责 |
|---|---|
| `TimeManager` | 每日推进入口、日结生成、自动保存 |
| `NPCManager` | 创建/恢复/查询角色，派遣、恢复、伤亡、关系与招募 |
| `MissionManager` | 所有任务配置加载、校验、推进、失败清理和待领奖 |
| `EventManager` | 来源化事件、收件箱、过期、效果和历史 |
| `PlayerManager` | 宗门资源、设施、立宗状态、功法理解、村庄关系和劳动力 |
| `WarehouseManager` | 物品增减、容量检查和重复栈合并 |
| `RewardManager` | 向宗门、人物和仓库发放任务奖励 |
| `SaveManager` | 捕获、保存、读取、备份和迁移 `GameState` |
| `ItemDatabase` / `TraitDatabase` | 加载和查询 JSON 内容 |
| `UIManager` | 面板打开、关闭和返回栈 |
| `RandomEventManager` | 旧随机事件兼容与调试 |

立宗切片沿用现有 Manager，没有新增全局单例。需要跨场景保留的对象通过 `DontDestroyUtility.MarkPersistent()` 处理。

## 7. 每日推进顺序

`TimeManager.EndDay()` 的既有职责和顺序保持不变：

1. 检查未关闭的日结面板。
2. 清理到期事件，并检查关键事件与收件箱容量。
3. 捕获当日开始快照。
4. 天数 +1。
5. `NPCManager.OnDayPassed()` 推进恢复和空闲修炼；立宗核心弟子的空闲修炼同时推进功法理解。
6. 广播 `OnDayPassed`，由 `MissionManager` 推进活动任务。
7. `EventManager.ProcessDay()` 处理后续事件、宗门日常和招募检查。
8. 生成 `DaySettlementSummary`。
9. 自动保存。
10. 显示每日结算。

结束日前的派遣消耗、事件选择、设施升级和待领奖领取通过既有预推进资源记录进入下一份日结。

## 8. UI 架构

主要面板：

- `FoundingPanel`：候选选择、传承选择、洞府修复、功法理解、村庄与路线建设入口。
- `SectPanel` / `NPCInfoPanel`：弟子列表与人物详情。
- `MissionPanel` / `MissionNodePanel`：任务、设施行动、节点选择和待领奖。
- `CharacterEventPanel`：事件收件箱、正文和选项。
- `SectDevelopmentPanel`：设施状态和既有升级入口。
- `ExplorationPanel`：区域列表、详情与探索派遣。
- `AlchemyPanel`：炼丹设施行动。
- `DaySettlementPanel`：每日结算。
- `WarehousePanel`：仓库与物品详情。

`FoundingPanel`、`CharacterEventPanel`、`DaySettlementPanel`、`SectDevelopmentPanel`、`ExplorationPanel` 和 `AlchemyPanel` 使用 `RuntimeUIFactory` 创建基础 UGUI 控件。UI 通过 Manager 命令接口改变状态，不应直接修改 Manager 内部集合。

## 9. 配置约定

配置继续使用 `Resources.LoadAll` 或单个 `Resources.Load`。新增 JSON 字段优先设计为可选字段，旧配置缺失时使用默认值。

重要约定：

- `material_001` 是基础材料。
- 宗门“灵材”不等同于仓库物品 `LingShi_001`。
- `Configs/Founding/founding.json` 是候选姓名、特点和三条传承的目录。
- `Configs/Missions/founding` 保存立宗故事与劳动力任务。
- `Configs/CharacterEvents/founding_*` 保存立宗里程碑事件。
- 设施行动、探索和立宗行为均复用任务系统。
- 配置 ID 的跨文件引用由 `ConfigValidator` 和 EditMode 测试检查。

## 10. 测试与验证

EditMode 测试文件：

- `CharacterStateTests.cs`
- `FacilityLoopTests.cs`
- `EventInboxTests.cs`
- `ExplorationSystemTests.cs`
- `FoundingSystemTests.cs`

立宗测试覆盖候选确定性与唯一性、生成角色读档、凡人境界兼容、v3 → v4 迁移、v4 的 0 级设施、故事任务绕过任务堂、成本原子扣除、功法理解、村庄劳动力预留/释放、路线建设失败清理、无 NPC 劳动力任务、完成条件及配置交叉引用。

当前自动化基线：Unity EditMode 45/45 通过。合并前仍需确认：

1. Unity Editor 编译通过。
2. Resources JSON 可解析且引用有效。
3. `git diff --check` 无格式错误且没有无关场景、Prefab、字体或 ProjectSettings 改动。
4. 手动验证候选选择、传承选择、修复、理解里程碑、村庄支持、路线建设、完成立宗，以及保存后重启恢复。

`dotnet build` 不能可靠验证该项目，因为解决方案中存在 Unity 生成的同名 `Assembly-CSharp` 项目；以 Unity Editor 和 Unity Test Runner 为准。

## 11. 高风险区与扩展规则

高风险区：

- `GameState`、存档版本、备份和迁移
- 角色稳定 ID、自定义生成角色恢复和境界枚举值
- `TimeManager.EndDay()` 每日顺序
- Mission/Event 状态机及失败、取消、死亡、待领奖清理
- NPC 关系引用与死亡清理
- Resources JSON 字段、ID 和 Unity `.meta` GUID
- 场景、Prefab、TMP 字体、Manager 初始化顺序

扩展原则：

- 新玩法优先复用 `Mission`、`EventManager`、`CharacterState` 和 `FacilityRules`。
- 不为未来需求提前增加全局 Manager、复杂技能树、人口 AI 或世界地图。
- 新事件效果必须补配置校验和测试。
- 新存档字段必须提供默认值、迁移策略和旧档回滚方式。
- 新任务行为必须覆盖完成、失败、取消、角色死亡、读档和 UI 路径。

具体协作规则、风险审批和变更预算以根目录 `AGENTS.md` 为准。
