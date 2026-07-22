# Cultivation4X 项目架构

## 1. 项目概览

Cultivation4X 是一个 Unity 单机修仙宗门经营原型。当前核心体验围绕宗门资源、弟子安排、任务推进、人物养成、来源化随机事件和设施升级展开。

```text
安排弟子
  -> 消耗时间/资源
  -> 来源合理的事件进入收件箱
  -> 获得资源、修为、特质、关系和履历
  -> 升级宗门设施
  -> 解锁更高阶任务和更高效率的行动
```

当前技术基线：

- Unity 2022.3 LTS
- UGUI + TextMesh Pro
- Newtonsoft.Json
- JSON 配置放在 `Resources/Configs`
- 存档为 JSON 文件，写入 `Application.persistentDataPath`
- 自动化测试以 Unity Test Framework EditMode 测试为主

## 2. 目录结构

```text
Assets/
  C#/
    Data/       静态配置模型、存档模型和领域数据类型
    Manager/    系统入口、配置加载、调度和持久化
    RunTime/    任务和 NPC 的运行时对象
    UI/         UGUI 面板与运行时 UI
    Utility/    枚举、物品栈、成长规则和辅助工具
  Resources/
    Configs/    物品、任务、人物事件和特质 JSON
    NPC/        NPC ScriptableObject 模板
    Prefab/     UI Prefab
  Scenes/       Unity 场景
  Tests/Editor/ EditMode 自动化测试
```

项目当前没有自定义程序集定义。生产代码编译进 `Assembly-CSharp`，Editor 测试编译进 Unity 生成的测试程序集。

## 3. 核心领域模型

### 3.1 人物

- `NPCData`：ScriptableObject 人物模板，保存初始属性和初始特质。
- `CharacterState`：可序列化人物状态，是存档中的人物事实来源。
- `NPCRuntime`：组合 `NPCData` 和 `CharacterState`，提供任务、事件、UI 使用的运行时接口。

`CharacterState` 当前包含：

- 稳定角色 ID 和模板 ID
- 显示名、年龄、等级、旧经验字段
- 修为和境界
- 健康状态
- 性格与经历特质 ID
- 关系记录
- 个人履历

死亡角色保留在存档、履历和关系引用中，但不能继续执行任务、修炼或被普通新事件选中。

### 3.2 宗门资源与设施

宗门顶部数值资源使用 `PlayerData.gold` 字段保存，但 UI 显示为“灵材”，用于和仓库物品“下品灵石”区分。`PlayerData.reputation` 保存声望。

基础材料使用仓库物品 `material_001`，不新增第二套材料账户。仓库由 `WarehouseData.items` 保存，`WarehouseManager.NormalizeItems()` 会合并同 ID 重复栈，避免同一种物品显示为多个格子。

设施定义在 `FacilityRules`：

- `MissionHall`：任务堂，影响每日任务候选数和普通任务并行数。
- `Warehouse`：仓库，影响不同物品种类上限。
- `TrainingRoom`：修炼室，影响空闲弟子每日基础修为。
- `SecretRealm`：秘境，提供固定探索设施行动。
- `AlchemyRoom`：炼丹房，提供固定炼丹设施行动。

所有设施当前 1-3 级。升级入口在 `PlayerManager.TryUpgradeFacility()`，升级前先校验资源和等级，成功后一次性扣除灵材与基础材料。

### 3.3 任务

- `MissionData`：任务 JSON 数据，包含类型、阶级、设施需求、消耗、耗时、奖励和节点。
- `Mission`：单个任务实例，保存执行弟子、剩余天数、当前节点、状态和待发奖励。
- `MissionManager`：加载任务配置、刷新每日候选、校验派遣、推进任务、处理完成/失败/待领奖。

普通任务受任务堂等级控制。秘境和炼丹复用 `Mission` 状态机，作为设施行动，不占每日候选位。

`MissionState` 包含：

- `NotStarted`
- `Active`
- `WaitingNode`
- `Completed`
- `Failed`
- `AwaitingReward`

`AwaitingReward` 用于仓库容量不足时保留奖励。进入该状态后，弟子恢复空闲，奖励等待玩家腾出仓库空间后领取。

任务配置里的 `expReward` 当前仍保留旧字段名，但发放时作为人物“修为”处理，入口在 `RewardManager.GiveReward()`。

### 3.4 事件

新事件系统由 `EventManager` 负责，配置模型在 `EventModels.cs`。

事件定义包含：

- `EventDefinition`
- `EventCondition`
- `EventParticipantRule`
- `EventOptionDefinition`
- `EventOutcomeDefinition`
- `EventEffect`
- `PendingEvent`
- `EventInboxEntry`
- `EventHistoryRecord`

事件来源使用 `EventSource` 控制，不再每天固定抽取全事件池。当前来源包括：

- 任务开始、节点、完成、失败
- 修炼
- 受伤、恢复
- 设施升级
- 秘境
- 炼丹
- 宗门日常
- 招募检查
- 后续事件

事件进入收件箱后由玩家处理。普通事件有过期天数，关键事件不会自动过期，并会阻止继续结束一天。事件结果会写入事件历史和人物履历，并在 Console 打印汇总，例如修为、灵材、声望、特质、伤势、死亡和后续事件。

旧 `RandomEventManager` 仍保留作为兼容层和调试入口，正式随机事件流程以 `EventManager` 为准。

### 3.5 存档

`GameState` 是完整存档快照，当前版本为 `SaveDataVersion.Current = 2`。

存档包含：

- 当前天数
- 确定性随机种子和抽取次数
- 宗门资源、声望、设施等级
- 仓库
- 全部人物状态
- 活动任务
- 事件历史
- 待触发后续事件
- 每日任务候选
- 事件收件箱
- 当前打开事件
- 最近未读每日结算

`SaveManager.MigrateState()` 对旧存档补齐新增字段默认值。读档恢复仓库后会调用 `WarehouseManager.NormalizeItems()`，合并旧存档中的重复物品栈。

## 4. Manager 职责

| Manager | 主要职责 |
|---|---|
| `TimeManager` | 结束一天、每日推进顺序、日结生成、未读日结状态 |
| `NPCManager` | 创建、恢复、查询角色；处理派遣、恢复、受伤、死亡、关系和招募 |
| `MissionManager` | 任务配置加载、候选刷新、派遣校验、活动任务推进、待领奖 |
| `EventManager` | 事件配置加载、来源触发、参与者绑定、收件箱、过期、效果执行、历史 |
| `PlayerManager` | 宗门灵材、声望、设施等级和设施升级 |
| `WarehouseManager` | 仓库物品增加、扣除、容量检查、重复物品栈合并 |
| `RewardManager` | 将任务奖励发放到宗门资源、弟子修为和仓库 |
| `SaveManager` | 捕获、保存、读取和迁移 `GameState` |
| `ItemDatabase` | 加载和查询物品 JSON |
| `TraitDatabase` | 加载和查询特质 JSON |
| `RandomEventManager` | 旧随机事件兼容与调试 |
| `UIManager` | UI 面板打开、关闭和返回栈 |

所有需要 `DontDestroyOnLoad` 的对象通过 `DontDestroyUtility.MarkPersistent()` 处理。该工具会先把对象移到场景根节点，再调用 Unity 的 `DontDestroyOnLoad`，避免非根对象产生 warning。

## 5. 每日推进顺序

`TimeManager.EndDay()` 是每日推进入口。当前顺序：

1. 检查每日结算面板是否未关闭。
2. 让 `EventManager` 清理到期事件、检查关键事件和收件箱容量。
3. 捕获当天开始快照。
4. 天数 +1。
5. `NPCManager.OnDayPassed()` 推进恢复和空闲修炼。
6. 广播 `OnDayPassed`，由 `MissionManager` 推进活动任务、秘境和炼丹。
7. `EventManager.ProcessDay()` 处理后续事件、宗门日常和招募检查。
8. 生成 `DaySettlementSummary`。
9. 自动保存。
10. 通知 `DaySettlementPanel` 显示每日结算。

结束日前发生的资源变化，例如派任务消耗、设施升级、手动处理事件、领取待领奖奖励，会通过 `TimeManager.RecordPreAdvanceResourceChange()` 进入下一份每日结算，避免只显示结束日推进过程中的增量。

## 6. UI 架构

主要 UI：

- `SectPanel`：弟子列表。
- `NPCInfoPanel`：人物详情。当前默认只显示性格；经历已拆为可选 `experienceText`，未绑定时不显示。
- `MissionPanel`：每日候选任务、设施行动、待领奖入口。
- `MissionNodePanel`：任务节点选择，显示任务名、节点名和执行弟子。
- `CharacterEventPanel`：事件收件箱、事件正文和选项。
- `SectDevelopmentPanel`：设施等级和升级入口。
- `DaySettlementPanel`：每日结算。
- `WarehousePanel`：仓库格子和物品详情。

部分新增 UI 通过运行时创建：

- `CharacterEventPanel`
- `DaySettlementPanel`
- `SectDevelopmentPanel`

这些运行时 UI 使用 `RuntimeUIFactory` 创建基础 UGUI 控件。当前 UI 仍直接依赖全局 Manager，尚未形成独立展示模型。

## 7. 配置和内容

配置位于：

- `Assets/Resources/Configs/Items`
- `Assets/Resources/Configs/Missions`
- `Assets/Resources/Configs/CharacterEvents`
- `Assets/Resources/Configs/Traits`

配置加载仍使用 `Resources.LoadAll`。新增配置字段以可选字段为主，旧 JSON 缺失字段时走默认值。

当前重要约定：

- `material_001` 是基础材料。
- 宗门数值资源显示为“灵材”，不等同于仓库里的 `LingShi_001` 物品。
- 设施行动复用任务配置，不创建第二套行动系统。
- 事件不使用运行时大模型生成内容，所有结果来自 JSON 模板和受控枚举效果。

## 8. 测试与验证

当前测试文件包括：

- `CharacterStateTests.cs`
- `FacilityLoopTests.cs`
- `EventInboxTests.cs`

覆盖重点：

- 人物状态、特质、履历和存档往返。
- 设施规则、升级、任务候选、仓库容量和设施行动。
- 事件收件箱、过期、关键事件、确定性和配置交叉引用。

合并功能前至少需要：

1. Unity Editor 编译通过。
2. EditMode 测试通过。
3. Resources JSON 可解析且引用有效。
4. 手动验证新档、派任务、设施升级、事件收件箱、日结、保存读档。
5. `git diff` 中没有 `.vs`、无关资源或未解释的场景改动。

`dotnet build` 当前不能可靠验证 Unity 项目，因为解决方案文件里存在 Unity 生成的同名 `Assembly-CSharp` 项目问题，应以 Unity Editor 编译和 Unity Test Runner 为准。

## 9. 高风险区域

以下区域修改时需要额外谨慎：

- `GameState`、存档版本和迁移。
- 角色稳定 ID。
- `TimeManager.EndDay()` 每日顺序。
- `MissionState`、任务清理和奖励待领。
- `EventManager` 收件箱、过期、关键事件和效果预检。
- NPC 死亡、受伤、任务引用清理和关系引用。
- `Resources` JSON 字段和 ID。
- Unity 场景、Prefab、TMP 字体资产和 ScriptableObject GUID。
- Manager 初始化顺序和 `DontDestroyOnLoad`。

本项目优先保证可玩闭环和稳定运行。不要为了未来可能需求提前引入复杂系统、全局 Manager 或大规模抽象。

## 10. 后续扩展规则

- 新玩法优先复用 `Mission`、`EventManager`、`CharacterState` 和现有设施规则。
- 新事件效果优先扩展受控枚举，并补配置校验和测试。
- 新存档字段必须提供默认值和迁移策略。
- 新任务状态必须覆盖成功、失败、取消、死亡、读档和 UI 路径。
- UI 不直接修改 Manager 内部集合，应通过 Manager 命令接口改变状态。
- 不要把“灵材”和仓库物品“下品灵石”混为同一个资源账户，除非单独设计资源架构迁移。

具体协作规则、风险审批和变更预算以根目录 `AGENTS.md` 为准。
