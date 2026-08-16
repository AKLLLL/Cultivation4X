using System.Collections.Generic;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 贴地 Overlay 的统一网格构建器。
    ///
    /// 所有需要贴合连续地表的六边形覆盖物（选中框、影响范围、探索遮罩、
    /// 未来宗门边界/建筑占地等）都必须通过本类生成，构建流程固定为：
    ///   1. HexGeometry.GetCorners 取角点（不自己算 Hex 几何）；
    ///   2. TerrainRenderer.PresentationSurfaceHeightAt 逐角采样地表高度；
    ///   3. MapPresentationLayer 统一加偏移；
    ///   4. 生成 fill/outline mesh。
    ///
    /// 覆盖物材质统一使用 Unlit/VertexColorOverlay（ZTest Always），
    /// 因此既能贴地形显示，也不会被地形网格裁剪。
    /// </summary>
    public static class MapOverlayMeshBuilder
    {
        public static Vector2[] GetHexCorners(WorldCell cell, float radiusScale = 1f) =>
            HexGeometry.GetCorners(cell, HexGeometry.GetRadius() * radiusScale);

        /// <summary>逐角采样“地形高度 + 统一偏移”，中心也走同一服务。</summary>
        public static float[] SampleTerrainHeights(WorldMap map, WorldCell cell,
            Vector2[] corners)
        {
            float[] heights = new float[corners != null ? corners.Length : 0];
            if (cell == null || corners == null) return heights;
            for (int corner = 0; corner < corners.Length; corner++)
                heights[corner] = MapPresentationLayer.GetHeightAt(map, corners[corner], cell);
            return heights;
        }

        public static void AppendHexOverlay(WorldMap map, WorldCell cell, float radiusScale,
            Color32 fillColor, Color32 outlineColor, float outlineWidth,
            List<Vector3> vertices, List<Color32> colors, List<int> triangles)
        {
            if (cell == null) return;
            Vector2 center = HexGeometry.GetCenter(cell);
            Vector2[] corners = GetHexCorners(cell, radiusScale);
            float[] heights = SampleTerrainHeights(map, cell, corners);
            float centerHeight = MapPresentationLayer.GetHeightAt(map, center, cell);
            WorldMapHexOverlayGeometry.AppendHexCap(vertices, colors, triangles,
                center, corners, centerHeight, heights, fillColor);
            if (outlineWidth > 0f)
                WorldMapHexOverlayGeometry.AppendHexRing(vertices, colors, triangles,
                    corners, heights, outlineWidth, outlineColor);
        }

        public static void AppendHexRing(WorldMap map, WorldCell cell, float radiusScale,
            float width, Color32 color,
            List<Vector3> vertices, List<Color32> colors, List<int> triangles)
        {
            if (cell == null || width <= 0f) return;
            Vector2[] corners = GetHexCorners(cell, radiusScale);
            float[] heights = SampleTerrainHeights(map, cell, corners);
            WorldMapHexOverlayGeometry.AppendHexRing(vertices, colors, triangles,
                corners, heights, width, color);
        }

        /// <summary>把一个格子的贴地 Overlay 构建为独立 Mesh，便于测试与单格缓存。</summary>
        public static Mesh BuildHexOverlay(WorldMap map, WorldCell cell, float radiusScale = 1f,
            Color? fillColor = null, Color? outlineColor = null, float outlineWidth = 0f,
            string meshName = "MapHexOverlay")
        {
            if (map?.cells == null || cell == null) return null;
            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            AppendHexOverlay(map, cell, radiusScale,
                fillColor ?? Color.clear, outlineColor ?? Color.white, outlineWidth,
                vertices, colors, triangles);
            return WorldMapHexOverlayGeometry.CreateMesh(meshName, vertices, colors, triangles);
        }
    }
}
