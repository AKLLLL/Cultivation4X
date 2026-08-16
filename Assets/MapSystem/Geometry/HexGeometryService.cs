using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 全项目唯一的六边形几何服务。所有表现层（地形、网格、覆盖层、图标、标签）
    /// 必须通过本服务取得 Hex 中心、角点、半径与坐标转换，禁止各自实现
    /// odd-r pointy-top 公式或复制 HexRadius/OuterRadius。
    /// 六边形数据拓扑仍由 WorldMap.GetNeighbor/HexCoord.Distance 提供。
    /// </summary>
    public static class HexGeometry
    {
        /// <summary>六边形外接圆半径（世界单位）。表现层覆盖物只能按该半径缩放。</summary>
        public const float Radius = 1f;

        /// <summary>六边形外接圆直径（世界单位），相机缩放按可见格数换算时使用。</summary>
        public const float Diameter = Radius * 2f;

        private const float CornerAngleOffsetDegrees = -30f;
        private static readonly float Sqrt3 = Mathf.Sqrt(3f);

        public static float GetRadius() => Radius;

        /// <summary>odd-r pointy-top 六边形的 XZ 中心坐标（X=col，Z=row）。</summary>
        public static Vector2 GetCenter(HexCoord coord) =>
            new Vector2(Sqrt3 * (coord.col + ((coord.row & 1) == 1 ? 0.5f : 0f)),
                1.5f * coord.row);

        public static Vector2 GetCenter(WorldCell cell) =>
            cell == null ? Vector2.zero : GetCenter(cell.coord);

        /// <summary>以给定中心与外接圆半径生成 6 个角点，顺序与 TerrainMeshGenerator 完全一致。</summary>
        public static Vector2[] GetCorners(Vector2 center, float radius = Radius,
            float angleOffsetDegrees = CornerAngleOffsetDegrees)
        {
            Vector2[] corners = new Vector2[6];
            for (int corner = 0; corner < 6; corner++)
            {
                float angle = Mathf.Deg2Rad * (corner * 60f + angleOffsetDegrees);
                corners[corner] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return corners;
        }

        public static Vector2[] GetCorners(HexCoord coord, float radius = Radius) =>
            GetCorners(GetCenter(coord), radius);

        public static Vector2[] GetCorners(WorldCell cell, float radius = Radius) =>
            cell == null ? new Vector2[6] : GetCorners(cell.coord, radius);

        /// <summary>世界坐标（Y 为高度，XZ 为平面）到 odd-r pointy-top 六边形坐标的反算。</summary>
        public static HexCoord GetCoordFromWorld(Vector3 worldPosition)
        {
            int row = Mathf.RoundToInt(worldPosition.z / 1.5f);
            int col = Mathf.RoundToInt(worldPosition.x / Sqrt3 -
                                       ((row & 1) == 1 ? 0.5f : 0f));
            return new HexCoord(col, row);
        }

        /// <summary>
        /// 把世界坐标还原为底层 WorldCell 索引。地形只改变 Y，XZ 网格由 HexGeometry 决定；
        /// 所有射线命中、点击选格与覆盖层定位都必须经由此方法，不能自算反投影。
        /// </summary>
        public static bool TryGetCellIndex(WorldMap map, Vector3 worldPosition, out int cellIndex)
        {
            cellIndex = -1;
            if (map?.cells == null || map.cells.Length == 0) return false;
            HexCoord guess = GetCoordFromWorld(worldPosition);
            Vector2 position = new Vector2(worldPosition.x, worldPosition.z);
            float bestDistance = float.MaxValue;
            for (int row = guess.row - 1; row <= guess.row + 1; row++)
            {
                for (int col = guess.col - 1; col <= guess.col + 1; col++)
                {
                    int candidate = map.GetIndex(new HexCoord(col, row));
                    if (candidate < 0 || candidate >= map.cells.Length || map.cells[candidate] == null) continue;
                    Vector2 center = GetCenter(map.cells[candidate].coord);
                    float distance = (position - center).sqrMagnitude;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    cellIndex = candidate;
                }
            }
            if (cellIndex >= 0 && bestDistance <= Radius * Radius + 0.0001f) return true;
            cellIndex = -1;
            return false;
        }
    }
}
