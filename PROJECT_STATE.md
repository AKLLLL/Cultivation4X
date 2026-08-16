# Cultivation4X 项目当前状态（2026-08-16 更新）

本文档记录 3D 世界地图接入 SampleScene、建宗选址流程重构完成时的项目状态。

## 1. 概览

- 当前分支：`agent/influence-integration`，远端：`https://github.com/AKLLLL/Cultivation4X.git`
- 本次提交：`6b34d5f`
  `feat: 接入3D世界地图并重构建宗选址新游戏流程`
- 全量 EditMode：**375/375 通过**（`Logs/worldmap3d-editmode-results.xml`，failed=0 / skipped=0）
- 工作区应仅保留本文件更新与 AGENTS.md 的协作规则沉淀，随后与代码一起提交。

## 2. 本轮完成内容

### 2.1 SampleScene 3D 世界地图接入

- 旧 2D `WorldMapPresenter` 不再运行时自举，仅作为兼容层保留。
- `WorldMap3DController` + `WorldMapRenderPipeline` + `WorldMapHudController` +
  `WorldMapInteractionController` 接管 SampleScene 表现层，由 `Resources/Prefab/WorldMap3D.prefab` 实例化。
- 地形渲染复用既有 `TerrainRenderer` / `ContinuousTerrainSurfaceBuilder` / `HexGeometry`；
  `WorldMapRenderPipeline.SetPresentationsActive` 统一启停真实 renderer 节点。
- 覆盖层统一使用 `MapPresentationLayer` 高度服务与 `Unlit/VertexColorOverlay`，避免各自采样高度。

### 2.2 山体网格与旧档校验

- Mountain 使用 LEGO 平顶 + 垂直侧壁；新增 PlateauMask，把被 Mountain 围住的内陆低地压平。
- 增加 winding 与跨角大三角测试，侧壁/角点封口逻辑保持不变。
- 地图快照版本 `WorldMapGenerationVersion.Current = 5`；旧 `generationVersion=4` 存档按规则自动舍弃并创建新档。
- 存档版本 `SaveDataVersion.Current = 15`：弟子/功法/宗门名称先于选址完成，选址确认后才创建驻地。

### 2.3 新游戏流程

```text
MainMenu
  → CharacterSetup（选3名真传弟子 → 初始功法 → 宗门名称）
  → SectPlacement（同一张世界地图的建宗选址状态）
  → 确认建宗（短文字过场）
  → WorldExplore（正式世界地图）
```

- 新增 `GameFlowStateManager`：`MainMenu / CharacterSetup / SectPlacement / WorldMap`。
- 新增 `WorldMapViewMode` 流程值：`SectPlacement / WorldExplore / SectManagement`。
- `SectPlacement` 复用 `WorldMapRenderer + WorldMapData + Terrain生成 + Hex Grid + 世界相机`，
  **不创建独立地图、独立 Renderer、独立 Camera**。
- 选址相机固定接近垂直俯仰（78°），仅支持滚轮缩放与 WASD 平移；鼠标拖拽不拉扯镜头，点击格子不自动聚焦。
- 选址面板只显示地点/地貌/环境描述与“建立宗门”，隐藏灵气、灵脉、资源、推荐指数、五行。
- 顶部资源栏与事件收件箱 UI 在 MainMenu/CharacterSetup/SectPlacement 隐藏，成功建宗进入 WorldExplore 后恢复。

### 2.4 UI 分层

| GameFlowState | UI |
|---|---|
| MainMenu | MainMenuPanel |
| CharacterSetup | FoundingPanel |
| SectPlacement | 世界地形 + Hex Grid + 精简 PlacementPanel（WorldHUD 的选址模式） |
| WorldExplore | 完整 WorldHUD + ResourceBar + 事件收件箱 + 探索/影响/灵脉覆盖层 |

## 3. 测试与验证

| 项目 | 状态 |
|---|---|
| EditMode 全量 | **375/375 通过**（`Logs/worldmap3d-editmode-results.xml`） |
| 编译 | Assembly-CSharp / Assembly-CSharp-Editor 0 错误 |
| diff 清洁度 | `git diff --check` 通过 |
| 地图数据 | 未修改 Hex 拓扑、WorldCell 结构或 Terrain 生成语义 |

## 4. 已冻结的架构决策

1. 世界地图数据是唯一数据源；任何新视图只能切换表现模式，不得复制地图数据或另建 Renderer。
2. 建宗选址是 `WorldMapViewMode.SectPlacement`，不是新场景、不是新地图系统。
3. 世界空间 Mesh 不得挂在 ScreenSpaceOverlay Canvas 根节点下；UI Canvas 与地图 Renderer 必须分离节点。
4. GameFlow UI 启停集中在 `WorldMap3DController` 的状态路由中，不在各 UI 自行监听流程状态。

## 5. 已知问题与待办

1. 选址阶段地形可见、数据层隐藏；尚未做“全图未知”的信息模糊表现（如隐藏区域名外的地形细节），当前仅隐藏数据覆盖层。
2. 建宗短过场为固定 2.4 秒文字遮罩，未做可跳过/分镜。
3. `SectManagement` 仅枚举预留，无实现。
4. 手测清单：
   - 新游戏主菜单 → 选弟子/功法 → 宗门名称 → 同地图选址 → WASD/滚轮观察 → 点击格显示简单信息 → 确认 → 短过场 → 正式地图完整 HUD。
   - 旧存档删除/自动舍弃后能直接进入新流程。

## 6. 下一步建议

1. 选址信息层的“未知天地”表现：确认是否需要对未认知区域做更明显的未知化处理。
2. 建宗过场支持点击跳过，并记录首次建宗事件。
3. 在 `WorldExplore` 恢复探索/事件/影响力完整闭环手测后，再考虑 `SectManagement`。
