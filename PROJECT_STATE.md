# Cultivation4X 项目当前状态（2026-08-01）

本文档为只读调查结论，记录截至 2026-08-01 的项目状态：分支、最新提交、未提交工作区改动、已执行的测试证据与未验证项。不修改任何代码。

## 1. 概览

- 当前分支：`agent/world-map-migration`（与 `origin/agent/world-map-migration` 同步，均指向 `1b1c86e`）。
- 最近提交：`1b1c86e Add world map generation and observability`（2026-07-31 01:12，+3017 行 / 27 文件）。
- 工作区：存在一批未提交的世界地图进度与立宗确认 WIP，约 11.4k 插入 / 6.1k 删除（其中 1.7 万行来自 TMP 字体资产生成的噪音 diff）。
- 游戏主循环不变：洞府立宗 → 建设/修炼 → 探索/事件/威胁 → 宗门发展；当前工作把"选址"从洞府界面推进到了世界地图。

## 2. 版本控制状态

远端：`https://github.com/AKLLLL/Cultivation4X.git`

```text
1b1c86e (HEAD -> agent/world-map-migration, origin/agent/world-map-migration) Add world map generation and observability
b49c419 (origin/codex/exploration-discovery-slice, codex/exploration-discovery-slice) Improve runtime UI pagination
1a82987 Add Qingshi external threat slice
c8dc24e feat: deepen sect vitality progression
1feab0e Add cave founding vertical slice
c2b0d55 Add exploration discovery slice
d6d4dce (origin/main, main) Implement sect loop vertical slice
```

### 工作区改动清单

已修改（跟踪文件）：

- `Assets/C#/Data/FoundingModels.cs`（新增 `SectConfirmation` 阶段与 `HasReachedCave`）
- `Assets/C#/Data/GameState.cs`（存档版本 8 → 9，新增 `worldMapProgress`）
- `Assets/C#/Data/PlayerData.cs`（新增 `sectId`/`sectName`/`foundedDay`/`influenceRadius`）
- `Assets/C#/Manager/MissionManager.cs`（立宗阶段判断改用 `HasReachedCave`）
- `Assets/C#/Manager/PlayerManager.cs`（功法选择后进入 `SectConfirmation`，新增 `ConfirmSectFounding`）
- `Assets/C#/Manager/SaveManager.cs`（进度捕获/恢复、扩展存档校验）
- `Assets/C#/UI/AlchemyPanel.cs`、`ExplorationPanel.cs`、`ExternalThreatPanel.cs`、`FoundingPanel.cs`、`SectDevelopmentPanel.cs`（入口隐藏、`OpenFromSectLayout`、UIManager 栈路由、立宗确认界面）
- `Assets/C#/WorldMap/WorldMapModels.cs`、`Runtime/WorldMapIconGeometry.cs`、`Runtime/WorldMapPresentationModels.cs`、`Runtime/WorldMapPresenter.cs`、`Runtime/WorldMapPresenterObservability.cs`（认知遮蔽、影响范围覆盖层、宗门简报按钮、调试开关）
- `Assets/Tests/Editor/WorldMapIntegrationTests.cs`（补充候选快照）
- `AGENTS.md`（新增第 9 条：只有得到命令才控制 Windows 桌面程序手测）
- `Assets/Resources/SourceHanSansHWSC-Bold SDF.asset`（TMP 字体生成噪音，非本次逻辑改动）

新增（未跟踪，含 .meta）：

- `Assets/C#/WorldMap/WorldMapProgressModels.cs`：认知/影响/地点/危险等级模型与无状态规则
- `Assets/C#/UI/SectWorldInterface.cs`：宗门世界界面（资源栏、宗门简报、宗门布局、任务堂入口）
- `Assets/C#/UI/LegacyWorldUiGate.cs`：隐藏旧场景按钮与平面物体
- `Assets/Tests/Editor/SectFoundingIntegrationTests.cs`（4 个测试）
- `Assets/Tests/Editor/WorldMapProgressTests.cs`（4 个测试）

`git diff --check` 无格式错误。Resources 配置、场景、Packages、ProjectSettings 均无改动。Logs 目录被 gitignore 忽略。

## 3. 已提交切片：世界地图生成与可观测性（1b1c86e）

### 3.1 内容

新增 `Assets/C#/WorldMap/`：

- `WorldGenerator.cs`：确定性六边形网格生成，顺序为地形 → 气候 → 河流 → 灵脉 → 灵气；含参数校验、水域距离、POI 分配和自定义确定性随机数。
- `WorldMapModels.cs`：`HexCoord`、`WorldCell`、`RiverSegment`、`SpiritVein`、`WorldPointOfInterest`、`WorldMap`、生成参数快照与 `WorldMapSession`（静态会话）。
- `WorldMapStatistics.cs`：地形成分、生物群系、高度/湿度/灵气直方图等只读统计。
- `Runtime/`：`WorldMapPresenter`（网格渲染、选址确认、HUD）、`WorldMapIconGeometry`（图标网格几何）、`WorldMapLegendGraphic`、`WorldMapPresentationModels`（视图模式、标记、图例文案）、`WorldMapPresenterObservability`（地形/温度/湿度/五行/灵脉调试视图与统计页）。

### 3.2 数据流改动

- `SaveDataVersion` 6 → 8；`GameState` 新增世界地图快照。
- `FoundingStage` 新增 `WorldSelection = -1`；`FoundingState` 新增 `worldSeed` 与 `selectedWorldCellIndex`。
- 新档流程改为：生成世界 → 世界格选址 → 候选弟子 → 传承 → 洞府。
- 探索区域 ID 映射到世界地图 `pointsOfInterest`，探索面板显示地图格坐标。
- `SaveManager`：v8 之前旧档直接拒绝并提示删档重开（不再迁移）；新增 `ValidateWorldMapState` 校验地图尺寸、格子索引、覆盖层引用与参数快照。

### 3.3 测试证据

`Logs/world-map-migration-editmode-results.xml`（2026-07-31 00:58）：**103/103 通过**（92 个既有 + 11 个新增 `WorldMapIntegrationTests`），失败 0、跳过 0。该报告内容与提交内容一致。

## 4. 未提交 WIP：世界地图进度 + 立宗确认 + 宗门世界界面

### 4.1 状态机变化

```text
WorldSelection（世界格选址）
  -> CandidateSelection（3 名弟子）
  -> TechniqueSelection（传承）
  -> SectConfirmation（命名并确认立宗）   <- 本次新增
  -> Cave（宗门落点写入世界地图）
  -> Completed
```

- `ConfirmSectFounding`：校验宗门名（去空格后 2–12 字符、无控制字符）、落点可建设、3 名弟子与传承有效；成功后创建 `MapSiteData`（`player_sect_base`）写入 `WorldMapProgressState`，设置 `sectId`/`sectName`/`foundedDay`/`influenceRadius=2`，进入 `Cave` 并自动保存。
- `FoundingPanel` 新增宗门命名确认页（`TMP_InputField`，上限 12 字符）。
- `WorldMapPresenter` 立宗后切换为玩法模式：未知格被认知遮蔽（未知格渲染为暗色、河流/边界/图标只显示已认知区域），核心格与边缘格显示宗门影响覆盖层；选中宗门基地格出现"宗门简报"按钮。

### 4.2 世界界面与旧 UI 关系

- 新增 `SectWorldInterface`：顶部资源栏（灵材/基础材料/弟子/声望/影响格/日期）+ 宗门简报/宗门布局/任务堂入口，全部经 `UIManager` 栈打开，Esc 可回退。
- `LegacyWorldUiGate`：加载后隐藏旧场景中的 `Plane`、数字按钮、`Button (Legacy)`、`Button_AlchemyRoom`、`Button_Sect`、`Button_Warehouse` 等物体。
- 探索、炼丹、建设、外部威胁、洞府整备等面板均改为从宗门布局进入，旧场景内各自的启动按钮被隐藏。

### 4.3 存档与高风险区影响

- 存档版本升至 v9；`Load()` 对 `version < 9` 直接拒绝（延续"每次更新删旧档重开"的项目规则，不做迁移）。
- `ValidateWorldMapState` 扩展：校验认知索引唯一性与越界、地点数据唯一性、立宗阶段与载荷一致性、已选弟子与角色快照一致、宗门驻地与宗门数据一致（`sectId == "player_sect"`、名称与地点名称一致、`influenceRadius == 2` 等）。
- 涉及的高风险区：`GameState`/存档版本、`FoundingStage` 与立宗状态机、`PlayerData` 新字段、`SaveManager` 加载校验。这些改动已由 `SectFoundingIntegrationTests` 与 `WorldMapProgressTests` 覆盖并通过（见第 5 节）。

## 5. 测试与验证现状

| 项目 | 状态 |
|---|---|
| 已提交切片（1b1c86e） | EditMode 103/103 通过（`world-map-migration-editmode-results.xml`） |
| 当前 WIP（含新增 8 个测试） | **EditMode 111/111 通过**（`state-2026-08-01-editmode-results.xml`，2026-08-01 01:08 执行，failed=0 / skipped=0） |
| 配置/场景/包 | 无改动 |
| diff 清洁度 | `git diff --check` 通过；唯一无关改动为 TMP 字体资产（提交时需排除） |

未验证项（残余风险）：

1. 世界地图玩法模式的手动验收未做：认知遮蔽、影响范围覆盖层、宗门简报、Esc 返回、保存后重启恢复。
2. 旧场景按钮隐藏（`LegacyWorldUiGate`）依赖对象名硬编码，场景改名会失效。
3. `SectWorldInterface`/`WorldMapPresenter` 均为运行时自动创建，其与场景内既有 UI 的层级、点击穿透需手测确认。
4. `PROJECT_ARCHITECTURE.md`（2026-07-30）已过时：其中"探索不含世界地图""存档 v6""不为未来需求设计世界地图"等章节与当前代码不符。

## 6. 建议的下一步

1. 运行 EditMode 全量测试（103 + 8），确认 WIP 编译与测试通过，留下 XML 报告。
2. 按需更新 `PROJECT_ARCHITECTURE.md`，同步世界地图切片、新立宗状态机与 v9 存档规则。
3. 手动跑一遍"新档 → 选格 → 弟子 → 传承 → 命名立宗 → 世界地图玩法"闭环，重点验证认知遮蔽与宗门简报。
4. 提交时仅暂存本任务文件，排除 `SourceHanSansHWSC-Bold SDF.asset` 与 Logs。
