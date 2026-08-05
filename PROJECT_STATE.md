# Cultivation4X 项目当前状态（2026-08-04）

本文档为只读调查结论，记录截至 2026-08-04 的项目状态：分支、最新提交、未提交工作区改动、已执行的测试证据与未验证项。除本文件与 `PROJECT_ARCHITECTURE.md` 的同步外，本次未修改任何代码。

## 1. 概览

- 当前分支：`agent/influence-integration`（与 `origin/agent/influence-integration` 同步，均指向 `301360d`）。
- 最近提交：`301360d 完善世界地图区域表现、认知与小块生成`（2026-08-04 01:19，+2425 行 / 21 文件）。
- 工作区：测试修复、文档同步与 v14 设计改动（探索堂移除/枚举重排/图例删除/四项小修）均未提交；另存在 TMP 字体资产生成的噪音 diff（提交时需排除）。
- 游戏主循环：确定性生成世界并选址 → 洞府立宗（候选/传承/命名） → 建设/修炼 → 探索、地图内容、事件与威胁 → 宗门发展。

## 2. 版本控制状态

远端：`https://github.com/AKLLLL/Cultivation4X.git`

```text
301360d (HEAD -> agent/influence-integration, origin/agent/influence-integration) 完善世界地图区域表现、认知与小块生成
3abe746 完善地图内容、语义图标与地点后果
bfdaf49 Implement sect influence system
65c6fa5 Add world map progress, sect founding confirmation and world UI
1b1c86e Add world map generation and observability
b49c419 (origin/codex/exploration-discovery-slice, codex/exploration-discovery-slice) Improve runtime UI pagination
1a82987 Add Qingshi external threat slice
c8dc24e feat: deepen sect vitality progression
```

其他本地分支：`main` 停在 `65c6fa5`（落后 7 个提交），`agent/world-map-migration` 停在 `65c6fa5`，`codex/exploration-discovery-slice` 停在 `b49c419`。

### 工作区改动清单

本次任务新增/修改（均未提交）：

- `Assets/Tests/Editor/UIPaginationTests.cs`：删除指向已移除方法 `ConfigureInfluenceLegendLayout` 的过时图例测试。
- `Assets/Tests/Editor/WorldMapRegionTests.cs`：EditMode 下显式调用 `Awake` 与 `OnDestroy`，修复 presenter 生命周期测试。
- `PROJECT_ARCHITECTURE.md`：同步存档 v13、地图内容/区域规则、187/187 测试基线、每日推进顺序与图例现状。
- `PROJECT_STATE.md`：本文件重写为 2026-08-04 快照。

随后批准实施的 v14 设计改动（均未提交）：

- 移除探索堂：`FacilityType.ExplorationHall`、`PlayerData.explorationHallLevel` 及相关升级入口/解锁门删除；7 个探索任务 JSON 解除设施门槛；洞府地点开发不再授予设施后果。
- `FoundingStage` 重排为流程顺序（SectConfirmation=2, Cave=3, Completed=4），`SaveDataVersion.Current` 13 → 14，旧档一律拒绝。
- 删除未使用的 `WorldMapInfluencePresentation.LegendText` 常量及对应测试。
- 四项小修：`TimeManager.EndDay()` 防重入；任务节点 `RemoveItem` 失败保持 WaitingNode 并重新弹出面板展示原因；弟子死亡取消其全部 AwaitingReward 任务；`Save()` 改为 .tmp/.bak 备份式原子写入。
- 相应更新测试：删除/改写探索堂、图例、洞府后果相关用例，新增节点失败重选与死亡清理用例。

用户测试反馈后补做的整体移除（均未提交）：

- 旧区域探索系统整体删除：`ExplorationPanel`（探索派遣面板）、`ExplorationModels.cs`（区域/探索规则）、7 个勘察/推进/驻守任务 JSON、`Configs/ExplorationRegions`、`PlayerData.explorationRegions` 状态及 `MissionManager` 中 Survey/Progress/Ongoing 全部分支；宗门简报 → 任务堂/执事堂的"探索派遣"入口删除。探索统一走世界地图探索行动。
- 相应删除 `ExplorationSystemTests` 及 UIPaginationTests/WorldMapIntegrationTests 中的探索用例。

`git diff --check` 无格式错误。`Assets/Resources/SourceHanSansHWSC-Bold SDF.asset` 为 TMP 生成噪音，非逻辑改动，提交时需排除。

## 3. 已提交切片时间线（1b1c86e → 301360d）

| 提交 | 存档版本 | 内容 |
|---|---|---|
| `1b1c86e` | v8 | 世界地图确定性生成、参数快照与可观测性 |
| `65c6fa5` | v9 | 地图进度、立宗确认、宗门世界界面 |
| `bfdaf49` | v10 | 宗门影响力来源、影响缓存与权限查询 |
| `3abe746` | v12 | 地图内容地点/行动/后果、语义图标，生成版本升至 4 |
| `301360d` | v13 | 区域规则与快照校验、区域表现/认知/小块 |
| 工作区（未提交） | v14 | 移除探索堂与旧区域探索系统、FoundingStage 重排、图例常量删除、四项小修 |

存档版本号从 v10 直接跳到 v12，未使用 v11 号段。

## 4. 测试与验证现状

| 项目 | 状态 |
|---|---|
| 2026-08-04 22:04 全量 EditMode | **173/173 通过**（`Logs/exploration-removal-results.xml`，failed=0 / skipped=0，73.4s） |
| 本次验证过程 | v14 首轮 185/187（新测试 NRE、版本门字面量）修正后 187/187；探索系统移除后 173/173 |
| 文档基线 | `PROJECT_ARCHITECTURE.md` 已同步至 v14 与 187/187 |
| 配置/场景/包 | 无改动；66 个 JSON 配置全部可解析 |
| diff 清洁度 | `git diff --check` 通过；唯一无关改动为 TMP 字体资产 |

批处理经验：带 `-quit` 的 batchmode 在本机偶发在测试启动前退出（日志仅出现 “Batchmode quit successfully invoked”）；本次改用省略 `-quit` 的形态，多次运行均正常产出报告。若复现，优先去掉 `-quit` 重试。

## 5. 未验证项（残余风险）

1. 手动玩法验收未做：世界选址 → 命名立宗 → 世界地图认知/影响/地点 → 探索 → 外部威胁 → 地图内容行动闭环，以及 v13 保存后重启恢复。
2. 地图候选地点仅在“探索结算为优秀”时有概率被发现（灵泉必发现，其余 65%）；失败后只能升级为 `Hinted` 提示而无法交互，且已探索格不能重复探索。探索堂与旧区域探索系统移除后不再软锁任何子系统，但地图内容系统仍可能“看得见摸不着”，是否加再发现路径需产品决策。
3. `LegacyWorldUiGate` 依赖场景对象名硬编码，场景改名会失效。
4. 运行时自动创建的 UI（`SectWorldInterface`、`WorldMapPresenter`、各面板）的层级与点击穿透需手测确认。

## 6. 建议的下一步

1. 提交测试修复、文档同步与 v14 设计改动（提交时排除 TMP 字体资产与 Logs）。
2. 手动跑“新档 → 选址 → 立宗 → 世界地图玩法”闭环，重点验证探索任务无需设施即可派遣、洞府地点开发、节点失败重选、v14 保存/读档。
3. 产品确认：`Hinted` 地点是否需要再发现路径。
