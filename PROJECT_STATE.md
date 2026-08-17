# Cultivation4X 项目当前状态（2026-08-17 更新）

本文档记录 3D 世界地图接入 SampleScene、以及旧玩法与新地图“双边融合”完成后的项目状态。

## 1. 概览

- 当前分支：`agent/influence-integration`，远端：`https://github.com/AKLLLL/Cultivation4X.git`
- 本里程碑提交：`feat: 融合3D世界地图与旧玩法为统一世界对象驱动体验`（见 git log）
- 全量 EditMode：**382/382 通过**（failed=0 / skipped=0）
- 工作区应只保留本文件更新、AGENTS.md 协作规则沉淀与代码一起提交；本地 `TestOutput/` 不入库。

## 2. 当前完成内容

### 2.1 新地图接入（已完成基线）

- 旧 2D `WorldMapPresenter` 不再运行时自举，仅作为兼容层保留。
- `WorldMap3DController` + `WorldMapRenderPipeline` + `WorldMapHudController` +
  `WorldMapInteractionController` 接管 SampleScene 表现层。
- 地形渲染复用既有 `TerrainRenderer` / `ContinuousTerrainSurfaceBuilder` / `HexGeometry`；
  世界地图是唯一地图数据源，选址/探索只切换 `WorldMapViewMode`。
- `GameFlowStateManager` 路由：`MainMenu → CharacterSetup → SectPlacement → WorldMap`。
- 存档版本 `SaveDataVersion.Current = 15`，地图生成版本 `WorldMapGenerationVersion.Current = 6`。

### 2.2 新地图与旧玩法融合

- **WorldLocation 门面统一**：`WorldLocationRules.SynchronizeFromMapSites` 将
  MapSiteData（灵泉/灵矿/洞府/兽巢/遗迹/村庄）同步为 WorldLocation；
  `sourceMapSiteId`、`WorldCell.locationId`、`WorldLocation.position` 保持一致。
- **地点行动入口统一**：HUD“行动”页只由 `WorldLocation.availableActions` 与
  `availableMissionIds` 驱动；移除旧“调查”按钮、普通格默认动作数组。
- **任务闭环**：地点任务从 WorldLocation 进入 `MissionPanel.OpenLocationMissions`，
  复用既有 MissionManager / RewardManager / TimeManager / NPCManager。
- **村庄劳动力**：`VillageLaborPanel` 接入村庄 WorldLocation，劳动力任务复用既有 Mission。
- **宗门管理**：宗门 WorldLocation 的 `ManageSect` 打开 `SectWorldInterface.OpenSectLayout`，
  以卡片形式进入藏经阁/炼丹房/修炼室/库藏/任务堂/宗门建设等既有面板。
- **隐藏信息保护**：Hidden/Hinted 的 MapSite 不会通过 WorldLocation 泄露到 HUD 或地图图标；
  只有 `Discovered` 的内容门面对玩家可见。
- **状态反馈**：WorldLocation.state 会随 MapSiteData 状态更新（如清理后的兽巢标记为
  `Inactive`）；村庄详情显示青石兽潮排程/活跃/平息状态。
- **旧 UI 清理**：删除 `WorldInfoPanel`，移除 HUD 中旧的调查/探索死路径；
  保留 `LegacyWorldUiGate` 与旧场景物件作为兼容层，待确认后统一物理清理。

### 2.3 新游戏流程

```text
MainMenu
  → CharacterSetup（选3名真传弟子 → 初始功法 → 宗门名称）
  → SectPlacement（同一张世界地图的建宗选址状态）
  → 确认建宗（短文字过场）
  → WorldExplore（正式世界地图）
```

## 3. 测试与验证

| 项目 | 状态 |
|---|---|
| EditMode 全量 | **382/382 通过**（failed=0 / skipped=0） |
| 编译 | Assembly-CSharp / Assembly-CSharp-Editor 0 错误 |
| diff 清洁度 | `git diff --check` 通过 |
| 地图数据 | 未修改 Hex 拓扑、WorldCell 结构或 Terrain 生成语义 |

## 4. 已冻结的架构决策

1. 世界地图数据是唯一数据源；任何新视图只能切换表现模式，不得复制地图数据或另建 Renderer。
2. 建宗选址是 `WorldMapViewMode.SectPlacement`，不是新场景、不是新地图系统。
3. 世界空间 Mesh 不得挂在 ScreenSpaceOverlay Canvas 根节点下；UI Canvas 与地图 Renderer 必须分离节点。
4. GameFlow UI 启停集中在 `WorldMap3DController` 的状态路由中，不在各 UI 自行监听流程状态。
5. MapSiteData 是地点玩法真实数据；WorldLocation 是地图/交互门面。
6. 玩家交互以 WorldLocation 为对象；普通格不再承载具体玩法按钮。
7. 地点任务必须复用既有 Mission/Event/TimeManager 流程，不新增第三套任务/行动系统。

## 5. 已知问题与待办

1. SampleScene 仍保留旧按钮/旧物件（`Button (Legacy)`、`Button_Sect`、`Button_Warehouse`、
   `Button_AlchemyRoom`、`Day`、`Plane`），当前由 `LegacyWorldUiGate` 运行时隐藏；
   后续需在确认后从场景中物理删除，并同步删除 gate。
2. `SectManagement` 仍只是枚举预留；宗门管理目前通过宗门 WorldLocation 行动进入卡片布局。
3. 手测清单：
   - 新游戏完整流程：选弟子/功法/宗门名 → 世界地图选址 → 确认 → 短过场 → 正式地图。
   - 点击村庄：查看人口/关系/劳动力/威胁，派遣村庄任务，进入劳动力面板。
   - 点击宗门：进入宗门管理卡片，打开既有设施/任务/仓库面板。
   - 探索并发现灵泉/灵矿/洞府/兽巢/遗迹后，从地点行动页发起对应 Mission。
   - 青石兽潮排程/激活/平息状态是否在村庄详情中正确显示。
   - 隐藏地点在 HUD 与地图图标中均不泄露。

## 6. 下一步建议

1. 在确认后清理 SampleScene 旧物件，删除 `LegacyWorldUiGate`。
2. 继续补全各地点类型的完整“发现 → 调查/开发 → 后果 → 每日产出”手测闭环。
3. 将事件收件箱与具体 WorldLocation 做更深的绑定展示（当前威胁已绑定到村庄，普通事件仍为全局收件箱）。
4. 若进入 `SectManagement` 正式开发，先明确它相对现有宗门卡片布局的增量，避免重复建设。
