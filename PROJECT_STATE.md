# Cultivation4X 项目当前状态（2026-08-16）

本文档记录地图生成系统优化完成时的项目状态：分支、提交、测试证据、已冻结的技术方案与遗留问题。

## 1. 概览

- 当前分支：`agent/influence-integration`，与 `origin/agent/influence-integration` 同步。
- 本次工作产出两个提交：
  - `e32a422` fix: 修复每日推进/存档/任务结算/威胁面板稳定性并补测试
  - `a88f2fe` feat: 完成地图生成系统优化并冻结表现层技术方案
- 地图生成系统优化已成功完成，下述技术方案**冻结**。后续除非提出专门方案并获得确认，不再随意调整六边形拓扑、山体形态、相机曲线、细节分层与森林表现策略。
- 工作区：仅保留本文件更新，随后单独提交。

## 2. 版本控制状态

远端：`https://github.com/AKLLLL/Cultivation4X.git`

```text
a88f2fe (HEAD -> agent/influence-integration, origin/agent/influence-integration) feat: 完成地图生成系统优化并冻结表现层技术方案
e32a422 fix: 修复每日推进/存档/任务结算/威胁面板稳定性并补测试
a1ba38c fix: 修复集成测试引用已重命名的地形详情格式化类
0afa77a chore: 停止跟踪生成的 TMP 字体资产并配置 Git LFS 规则
f91e317 feat: 世界地图地形渲染与美术呈现，引入 Polyart 资源并扩充测试
50749a6 refactor: organize world map into MapSystem module and add TerrainTest scene
```

`main` 分支为 `9345061 Merge branch 'agent/influence-integration' into main`。

## 3. 本轮已完成内容

### 3.1 P0 稳定性修复（e32a422）

- `TimeManager.EndDay()` 防重入；`SaveManager` 初始化失败保护。
- 任务结算：节点 `RemoveItem` 失败保持 `WaitingNode` 并重新弹出面板；弟子死亡取消其全部 AwaitingReward 任务；最终日结算保护。
- `ExternalThreatPanel` 按钮可见性修正。
- 新增 `Assets/Tests/Editor/StabilityFixTests.cs` 覆盖上述场景。

### 3.2 地图生成系统优化（a88f2fe，方案已冻结）

**山体**
- 山体采用台阶式“LEGO 山”：Mountain 格顶面平坦、侧壁垂直，宽厚台地可生成多层平台。
- 前脸朝向相机（行号更小 / -Z 一侧），前宽后窄非对称；山体总高控制在约 2 个六角格。
- 连续地表构建器对 Mountain 使用平顶 + 垂直侧墙，同级台地高度一致。

**相机与细节分层**
- Civ6 风格相机：归一化 zoom 0..1，高度/俯仰 AnimationCurve，固定 FOV，仅 WASD 平移，焦点平滑与地面跟随。
- 三层细节：Near(<0.25) / Mid(0.25–0.60) / Far(≥0.60)。
- Far = 高亮纯地形色块 + 区域名，不显示纹理、网格与模型；Mid = 正常地形；Near 预留给高细节。

**地表与标签**
- 地表低饱和、哑光，带宏观色斑变化与大气透视，叠加轻量六边形网格；山体区域跳过网格。
- 区域名固定在地面（`SetGroundFixed`），只在 Far 显示。
- 曲率保持关闭（`nearRadialCurvature = 0`）。

**森林表现（本轮重点）**
- 森林树模型簇只在 `WorldMapDecorationRenderer` 中程序化生成，不再依赖 Dreamscape 高模树预制体。
- 单树为 16 三角面的低模“圆锥树冠 + 粗树干”，`Unlit/VertexColor` 平涂。
- 每个森林格对应 1 个树簇，每簇 8～19 棵；簇按区域中心权重衰减（中心密、边缘疏），簇中心随机偏移 0.12～0.85 格半径，允许跨格分布。
- 每棵缩放 `0.22～0.36`（世界高度约 0.24～0.40 格）。
- 每个森林区域合并为**单一 Mesh + 单一 GameObject**，全图森林渲染对象从约 1443 降到约 104。
- `MapTestManager.TerrainOnlyEvaluationMode` 仍为 true，纯地形验收只选择性加回森林树簇，其余模型/图标/区域覆盖保持关闭。

**地图拓扑**
- 保持 odd-r pointy-top 六边形；Flat-top 转换已回滚并推迟，除非有专门重构提案否则不再尝试。

## 4. 测试与验证

| 项目 | 状态 |
|---|---|
| EditMode 全量 | **350/350 通过**（`Logs/forest-region-merged-results.xml`，failed=0 / skipped=0） |
| 场景批处理渲染 | TerrainTest 生成 + 渲染成功（`Logs/forest-region-merged-preview.log`） |
| diff 清洁度 | `git diff --check` 通过，无 TMP/Logs 噪音 |
| 配置/场景/包 | 本轮未修改存档结构、场景、Prefab GUID 或 Resources 配置 |

## 5. 已知问题与未验证项

1. **生物群系疑似问题（暂不处理）**：当前生成结果中森林格明显集中出现在海边/湖边，疑为温度/湿度/淡水距离权重使然。用户确认本轮不修，留作下一轮独立排查。
2. 手动视觉验收未做：打开 TerrainTest 确认森林中心密边缘疏、树簇跨格、树干可见、远景隐藏；确认帧率改善。
3. 玩法闭环手测（选址 → 立宗 → 世界地图 → 探索 → 事件/威胁）仍按 PROJECT_ARCHITECTURE.md 的既有清单执行。

## 6. 下一步建议

1. 单独排查生物群系分类中“森林贴水”的问题：检查 `WorldMapBiomeRules`/温度-湿度-淡水距离权重，以及区域归并时对海岸/湖岸格的吸收策略。
2. 地图表现层冻结期内，只在 TerrainTest 做数值调参（树缩放、簇密度、中心衰减权重），不引入新渲染架构。
