# Cultivation4X 协作规则

## 项目目标

这是一个由个人业余开发的 Unity 修仙宗门经营游戏。
项目灵感来源于群星、环世界。我希望玩家能在游戏中体会到宗门建设、养成的乐趣，以及享受随机事件带来的未知感。
中文编码使用UTF-8 方式。

## 开发原则

优先级：
1. 可玩闭环
2. 稳定运行
3. 易维护
4. 性能优化
5. 架构合理
6.遇到连接不上主机、断网、申请工具多次未成功等情况，应立即停止运行，以免重复请求浪费token
7.不考虑旧档兼容，每次更新内容都会删除旧档从新档开始测试
8.根据方案判断是否启用子Agent，并说明原因。
9.只有得到我的命令才会控制windows桌面程序进行手测。
10.不要为了未来可能需求提前设计复杂系统。
11.世界地图与旧玩法已经融合为同一个整体：地图不是演示层，而是核心玩法入口；新增功能应优先挂接到 WorldLocation 与既有 Mission/Event/TimeManager 流程上，而不是另起一套地图玩法系统。

## 高风险区域

以下区域属于高风险区：

- 存档结构、存档版本和迁移逻辑
- GameState 及角色稳定 ID
- 时间推进顺序
- Mission 和 Event 状态机
- NPC 死亡、任务清理和关系引用
- Unity 场景、Prefab 和 ScriptableObject GUID
- Resources 配置格式
- 全局 Manager 的初始化顺序
- Package、ProjectSettings 和 Unity 版本
- 大规模文件编码或换行符转换
- 世界地图生成语义（WorldGenerator / TerrainRenderer / Camera）
- WorldLocation ↔ MapSiteData 门面一致性：`sourceMapSiteId`、`WorldCell.locationId`、`WorldLocation.position` 三者的绑定关系
- 地点行动入口路由：`WorldMapHudController` 的 action/mission 按钮、`WorldMap3DController.HandleLocationAction`、`MissionPanel.OpenLocationMissions`
- WorldLocation 与既有 Mission 模板的引用（`availableMissionIds` 必须指向真实存在的 Mission 模板）
- 旧 2D/旧场景兼容层：`WorldMapPresenter`、`LegacyWorldUiGate`、SampleScene 中仍被隐藏的旧按钮/旧物件
- 弟子成长存档语义：`CharacterState.cultivation`（当日灵气）、`naqiProgress`、`techniqueMastery`、灵气紊乱状态
- 月度计划与预算记账：月份边界、三项比例、usedDays、训练预算转自由、AI Mission 剩余自由日
- 弟子自主行为与 Mission 桥接：Utility 评分、AI 专用 Mission、玩家无损取消 `MissionState.Cancelled`
- 统一灵气入口及结算顺序：计划修炼、事件 `AddCultivation`、地图每日灵气来源必须聚合后增量结算
- 可重复关键事件去重：同一 `eventId + actor.CharacterId` 在 active/inbox/pending 中只能存在一份
- 资源生态运行态：`resourceNodes`、`spiritualVeins` 派生、`lastSettledMonth`、小数余量和仓库满额损失
- 宗务人日余数 `sectDutyWorkCredit` 及其与仓库产出的原子性

修改高风险区域前必须：

1. 说明为什么必须修改。
2. 列出受影响的数据和调用方。
3. 说明旧存档和旧配置是否兼容。
4. 给出回滚方式。
5. 获得用户明确确认后才能实施。

诊断、读取和测试不需要确认。

## 变更预算

默认单次任务预算：

- 不进行与需求无关的重命名。
- 不移动目录。
- 不批量重命名公开字段、类型或 JSON 字段。
- 不引入新第三方依赖。
- 不进行全项目格式化或编码转换。
- 不重写现有系统，优先局部修复。

如果预计超出预算，必须先暂停，解释原因，并拆分任务。

## 架构限制

未经用户确认，不得：

- 新增全局单例 Manager。
- 更改公共 API。
- 更改序列化字段名。
- 删除兼容层。
- 修改存档格式。
- 把 JSON 数据改成其他存储方案。
- 将现有系统整体替换为新架构。
- 创建超过当前需求所需的抽象层。

针对当前“新地图 + 旧玩法融合”状态，以下限制同样适用：

- MapSiteData 仍是地点玩法真实数据；WorldLocation 是地图/交互门面。不要把新的玩法状态塞进 WorldLocation 而绕过 MapSiteData；也不要把表现字段塞进 MapSiteData。
- WorldLocation 的 id 规则必须保持稳定：
  - 手建地点：`world_location_qingshi_village`、`world_location_player_sect`
  - 内容门面：`world_location_` + `MapSiteData.siteId`
- 地点行动必须复用 `LocationAction`/`LocationActionType` 与 `availableMissionIds` 进入既有系统；禁止在 HUD 里为具体玩法硬编码新按钮分支。
- 隐藏内容不得通过 WorldLocation 泄露：Hidden/Hinted 的 MapSite 不应出现在 HUD 地点详情、行动页或地图图标中；只有 `Discovered` 的内容门面对玩家可见。
- 不新增 Mission/WorldAction/LocationAction 之外的第三套任务/行动系统。
- 不修改 `WorldGenerator`、`TerrainRenderer`、Camera、Mission 核心、`TimeManager`、`RewardManager`、`NPCManager` 的既有语义，除非单独确认。
- 清理旧 UI 时，`LegacyWorldUiGate` 与 SampleScene 旧物件必须一起处理：只删 gate 而场景旧按钮仍在，或只改场景而 gate 仍隐藏新 UI，都会造成状态不一致。

针对“弟子个性 + 资源生态 + 炼气成长”当前基线：

- `DiscipleDecisionManager` 只负责日结后调度；Goal/Utility 计算放在 `DiscipleAIEvaluator`，行动必须经 `DiscipleMissionBridge` 进入既有 Mission。禁止 AI 直接发奖励、写关系、加纳气或自动突破。
- AI 专用行动必须是零成本、无节点、`isPlayerAssignable=false` 的既有 Mission；只消费自由日，且不得跨月。玩家手动 Mission 始终优先。
- 无损中止 AI Mission 必须使用独立 `Cancelled` 路径：不得复用 Fail，不受伤、不触发 `MissionFailed`、不写失败日结、不返还已使用自由日。
- 月计划每月固定 30 天；当前月锁定，只编排下月；第 0 天可排第 1 月；未制定计划与新入门弟子全部自由。比例必须 10% 步进且总和 100%。
- `CharacterState.cultivation` 是当日灵气 0–100，不再是长期修为。所有灵气写入必须通过 `NPCRuntime.AddCultivation` / `NaqiGrowthRules.AddDailyAura`；禁止直接累加字段或另建成长入口。
- 长期炼气成长只写 `naqiProgress`；个人传承进度只写 `techniqueMastery`；宗门传承 ID 只读 `FoundingState.selectedTechniqueId`，不得复制到角色状态。
- 炼气 V1 不自动突破，不新增筑基及后续境界结构。旧 `expReward` 当前冻结，不得重新映射为纳气；若启用等级经验需单独确认。
- `WarehouseData.items` 是库存唯一真源；资源、丹药、任务成本和宗务产出都必须通过 `WarehouseManager`，不得增加第二套货币或物品容器。
- `MapSiteData` 决定地点开发状态，`WorldMapProgressState.resourceNodes` 保存资源节点运行态，`WorldLocation` 不保存资源库存或生产进度。
- 天然灵脉必须从既有 `SpiritVein` 路径和 Region 派生；不得复制或维护第二份世界灵脉网络。
- 资源月结必须幂等：只在 30 天边界结算，使用 `lastSettledMonth` 防重复；结算日开发的节点从下月生产；仓库拒收的当月产出记为损失，不延迟补发。
- 宗务产出按实际人日累计，每 5 人日生成 1 基础材料；余数必须持久化，仓库拒收时本次产出损失且人日额度已消费。
- 灵气紊乱必须独立于 `mentalState`；稳定随机必须包含存档种子、日期和角色稳定 ID，读档不得重掷。
- 调息丹继续复用 Item/Warehouse、AlchemyRoom 与 Mission 设施互斥；固定产量配方必须显式 `usesFacilityLevelScaling=false`。

## 工作流程

每项功能分为四个阶段：

1. 调查：只读检查现状、依赖和风险。
2. 计划：给出目标、改动范围、数据流、风险和测试方案。
3. 实施：得到确认后，在变更预算内修改。
4. 验证：编译、测试、检查 diff，并报告残余风险。

计划阶段不得修改代码。

## 跨系统项目共识

1. 个性系统负责“选择做什么”，Mission 负责“如何执行和结束”，成长/资源规则负责“结算什么”；三者不得互相越权。
2. 玩家命令优先于弟子自主选择，但覆盖不会回补已经过去的时间、计划类别或已实际使用的自由日。
3. 日推进顺序属于玩法语义：角色/月计划 → 地图每日效果 → Mission → 威胁/Event → 月结 → 日结/存档 → 日结后自主决策。调整顺序前必须单独审查。
4. 玩法状态必须只有一个真源：地图地点用 MapSiteData，交互用 WorldLocation 门面，库存用 WarehouseData，宗门传承用 FoundingState，角色成长用 CharacterState。
5. 配置 ID 和角色 ID 是跨存档、Mission、Event、经历记录的稳定引用；可以改显示文本，不得随意改 ID。
6. 自动测试通过不等于 Play Mode 闭环通过；必须分别报告编译、XML 测试、配置校验与未执行的手测。
7. 当前开发策略是不兼容旧档；提升 `SaveDataVersion` 后拒绝旧档，不为一次性测试档增加迁移复杂度。

## 完成标准

任何代码任务只有满足以下条件才算完成：

- Unity 编译通过。
- 配置文件可解析且引用有效。
- git diff 中没有无关修改。
- 高风险数据路径经过检查。
- 明确列出未验证的手动测试。
- 未经用户要求，不提交、不推送、不创建 PR。

## Unity 批处理测试约定

1.Unity 路径是 E:\Unity\2022.3.62f3\Editor\Unity.exe；
2.-runTests 时不要加 -quit，否则可能在测试启动前退出；
3.启动 batchmode 前先 Get-Process Unity 检查编辑器是否开着，开着必须先让用户关闭；
4.PowerShell 后台任务返回不代表 Unity 已结束，要以结果 XML 产出为准。

## 地图接入与 UI 分层约定

1. WorldMapData / WorldCell / HexGeometry / Terrain 生成是唯一地图数据源；新视图只能切换 WorldMapViewMode，禁止创建第二套地图数据或独立地形 Renderer。
2. GameFlowState 的 UI 启停必须集中在 WorldMap3DController 路由：MainMenu/CharacterSetup 只留立宗 UI；SectPlacement 复用世界地形 + 精简选址 HUD；WorldExplore 才恢复完整 HUD、资源栏与事件收件箱。
3. 世界空间 Mesh/Renderer 不得挂在 ScreenSpaceOverlay Canvas 根节点下；UI Canvas 与地图 Renderer 必须分属不同节点。
4. 建宗选址模式复用 WorldMapRenderer/TerrainRenderer 与既有相机，只切换表现模式与数据覆盖层，不新增 Camera/Renderer 系统。
5. 玩家交互以 WorldLocation 为对象：点击 Hex 后先解析 WorldLocation，再展示地点信息/行动/任务；普通格不再承载具体玩法按钮。
6. WorldMapHudController 的“行动”页只由 WorldLocation.availableActions 与 availableMissionIds 驱动；隐藏地点和普通格显示“暂无行动”，不要退回旧的地点调查按钮或默认动作数组。
7. 地点任务统一从 WorldLocation 进入 MissionPanel；村庄劳动力、宗门管理、仓库/设施等仍调用既有面板，不在地图层重建。
