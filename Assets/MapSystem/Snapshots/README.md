# MapSystem/Snapshots

本目录为未来 **MapSnapshot** 数据层预留，当前不包含任何实现。

## 为什么需要 MapSnapshot

未来的战争迷雾、NPC 探索、时间变化、灵气变化、地形变化与地点变化会让
“渲染时直接拼装 WorldMap + WorldMapProgressState + 全局玩家状态”变得脆弱。
MapSnapshot 的目标是：**每个渲染帧/渲染批次使用一份只读、可重建、与玩法
状态解耦的表现快照**。Renderer 永远只读 Snapshot，不再关心数据从哪里来。

预期快照至少包含：

- 静态地形：格索引、坐标、表现高度、地貌/群系表现色、连续地表高度查询；
- 认知：Known/Unknown（战争迷雾）；
- 影响：Core/Influence/Outer 与来源摘要；
- 灵气：totalAura、五行主属性、灵脉路径；
- 地点：Hidden/Hinted/Discovered、图标样式、可用行动摘要；
- 调试视图：当前视图模式与图例。

## 当前已落实的架构接缝（不提前实现快照）

1. 所有 3D 渲染器（TerrainRenderer、HexGridOverlayRenderer、RegionNameRenderer、
   RegionOverlayRenderer、MapIconRenderer 及新增的 KnowledgeMask / Influence /
   Selection / Vein 覆盖层）都只接收显式传入的 `WorldMap` / `WorldMapProgressState`，
   **不直接读取** `WorldMapSession`、`PlayerManager`、`UIManager`。
2. `WorldMapRenderPipeline` 是所有渲染器的唯一分发入口；未来把它的参数
   `WorldMap + WorldMapProgressState` 换成 `MapSnapshot` 即可，渲染器不用改。
3. `WorldMap3DController` 是唯一读取全局状态并准备数据的位置；
   MapSnapshot 落地时只替换该类的数据准备段。
4. 认知集合只有 `WorldMapInfluenceRules.CollectKnownCellIndices` 一个计算入口，
   避免各渲染器各自推导迷雾规则。
5. 本 README 之外**不新增任何 Snapshot 类、接口或序列化字段**，避免为未来需求
   提前设计复杂系统（遵守 AGENTS.md）。

## 迁移时需要注意

- MapSnapshot 必须保持确定性可重建，并延续 SaveManager 对静态地图/区域的校验语义。
- 表现快照与存档数据分离：快照可丢弃，存档不可丢失。
- 新字段/新枚举按项目规则先过结构评审与测试。
