using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 地图表现层统一高度服务。所有浮在地图上的表现物（选中框、影响范围、
    /// 探索遮罩、地点图标、灵脉路径）都必须通过本服务取得高度，不允许
    /// 各自直接采样 TerrainRenderer.PresentationSurfaceHeight + 自定义偏移。
    ///
    /// 规则：
    /// - 单个格子的覆盖物使用 GetCellHeight：取该格中心与 6 个角点的地表高度
    ///   最大值，再叠加 TerrainClearance，得到一个高于格内所有地形的平面高度；
    /// - 沿格边走线/带条使用 GetHeightAt 或两端 GetCellHeight 的较大值；
    /// - 图标使用 GetIconHeight，保持统一的额外视觉高度。
    /// 覆盖物材质统一使用 Unlit/VertexColorOverlay（ZTest Always），
    /// 因此即使 Y 值略低于相邻格地形也不会出现“边段被裁剪”。
    /// </summary>
    public static class MapPresentationLayer
    {
        /// <summary>覆盖层相对地形表面的最小高度间隙。</summary>
        public const float TerrainClearance = 0.03f;

        /// <summary>地点图标在统一覆盖层之上的额外视觉高度。</summary>
        public const float IconExtraHeight = 1.2f;

        public static float GetHeight(WorldMap map, WorldCell cell) =>
            (cell == null ? 0f : TerrainRenderer.PresentationSurfaceHeight(map, cell)) +
            TerrainClearance;

        public static float GetHeightAt(WorldMap map, Vector2 position, WorldCell cell) =>
            TerrainRenderer.PresentationSurfaceHeightAt(map, position, cell) + TerrainClearance;

        /// <summary>格级覆盖物使用的平面高度：格中心与 6 角点地表高度的最大值 + 间隙。</summary>
        public static float GetCellHeight(WorldMap map, WorldCell cell, float radiusScale = 1f)
        {
            if (cell == null) return TerrainClearance;
            Vector2 center = HexGeometry.GetCenter(cell);
            float height = GetHeightAt(map, center, cell);
            if (radiusScale > 0f)
            {
                foreach (Vector2 corner in HexGeometry.GetCorners(
                             center, HexGeometry.GetRadius() * radiusScale))
                    height = Mathf.Max(height, GetHeightAt(map, corner, cell));
            }
            return height;
        }

        public static float GetIconHeight(WorldMap map, WorldCell cell) =>
            GetHeight(map, cell) + IconExtraHeight;
    }
}
