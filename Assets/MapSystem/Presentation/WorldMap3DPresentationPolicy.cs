using System;
using System.Collections.Generic;
using System.Linq;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 3D 表现层的缩放档位：近景只保留少量信息，远景才显示区域覆盖层与区域名，
    /// 并对区域标签/覆盖层数量做上限与避让，避免整图变成满屏碎块命名的调试视图。
    /// </summary>
    public enum WorldMap3DZoomTier
    {
        Near = 0,
        Mid = 1,
        Far = 2
    }

    public static class WorldMap3DPresentationPolicy
    {
        /// <summary>中景与近景的分界：横向可见格数达到该值后进入中景。</summary>
        public const float MidViewMinHexes = 12f;

        /// <summary>远景最小横向可见格数，与 2D 表现层的远景阈值保持一致。</summary>
        public const float FarViewMinHexes = TerrainPresentationModels.FarViewHexes;

        /// <summary>远景区域标签中心之间的最小六角格距离，避免名字糊在一起。</summary>
        public const float RegionLabelMinCenterDistanceHexes = 5f;

        public static WorldMap3DZoomTier GetZoomTier(float visibleHexes)
        {
            if (visibleHexes >= FarViewMinHexes) return WorldMap3DZoomTier.Far;
            if (visibleHexes >= MidViewMinHexes) return WorldMap3DZoomTier.Mid;
            return WorldMap3DZoomTier.Near;
        }

        public static bool ShowRegionLabels(WorldMap3DZoomTier tier) =>
            tier == WorldMap3DZoomTier.Far;

        public static bool ShowRegionOverlays(WorldMap3DZoomTier tier) =>
            tier == WorldMap3DZoomTier.Far;

        public static bool ShowSiteIcons(WorldMap3DZoomTier tier) =>
            tier != WorldMap3DZoomTier.Far;

        /// <summary>
        /// Region 地形标识从中景开始淡入，远景保持清晰；不与离散档位同时跳变。
        /// </summary>
        public static float TerrainMarkerOpacity(float visibleHexes)
        {
            const float fadeStart = 22f;
            const float opaqueAt = 34f;
            if (visibleHexes <= fadeStart) return 0f;
            if (visibleHexes >= opaqueAt) return 1f;
            float t = (visibleHexes - fadeStart) / (opaqueAt - fadeStart);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Continuous Region meshes are the mid-view bridge, not far-view full models.</summary>
        public static float TerrainStructureOpacity(float visibleHexes)
        {
            const float fadeStart = 26f;
            const float invisibleAt = 38f;
            if (visibleHexes <= fadeStart) return 1f;
            if (visibleHexes >= invisibleAt) return 0f;
            float t = (visibleHexes - fadeStart) / (invisibleAt - fadeStart);
            t = t * t * (3f - 2f * t);
            return 1f - t;
        }

        /// <summary>Individual trees, rocks and CC0 key modules belong to the near view only.</summary>
        public static float TerrainDetailOpacity(float visibleHexes)
        {
            const float fadeStart = 10f;
            const float invisibleAt = 18f;
            if (visibleHexes <= fadeStart) return 1f;
            if (visibleHexes >= invisibleAt) return 0f;
            float t = (visibleHexes - fadeStart) / (invisibleAt - fadeStart);
            t = t * t * (3f - 2f * t);
            return 1f - t;
        }

        public static int RegionLabelLimit(WorldMap3DZoomTier tier)
        {
            switch (tier)
            {
                case WorldMap3DZoomTier.Far:
                    return WorldMapRegionPresentationPolicy.FarRegionLabelLimit;
                case WorldMap3DZoomTier.Mid:
                    return WorldMapRegionPresentationPolicy.MidRegionLabelLimit;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 从全部区域中选出远景要显示的区域：排除海洋、只保留不小于最小面积的区域，
        /// 按显示优先级/面积降序，受数量上限与中心间距约束。minimumCenterDistanceHexes
        /// 为 0 时只做数量限流（覆盖层使用），大于 0 时额外做名字避让（区域名使用）。
        /// </summary>
        public static List<MapRegionData> SelectRegionLabels(WorldMap map,
            IEnumerable<MapRegionData> regions, int minimumCellCount, int limit,
            float minimumCenterDistanceHexes = RegionLabelMinCenterDistanceHexes)
        {
            var result = new List<MapRegionData>();
            if (map?.cells == null || regions == null || limit <= 0) return result;

            var selectedCenters = new List<HexCoord>();
            foreach (MapRegionData region in regions
                .Where(region => region != null &&
                                 region.regionType != MapRegionType.OpenWater &&
                                 region.cellIndices != null &&
                                 region.cellIndices.Count >= minimumCellCount &&
                                 region.centerCellIndex >= 0 &&
                                 region.centerCellIndex < map.cells.Length &&
                                 map.cells[region.centerCellIndex] != null)
                .OrderByDescending(region => region.displayPriority)
                .ThenByDescending(region => region.cellIndices.Count)
                .ThenBy(region => region.regionId, StringComparer.Ordinal))
            {
                if (result.Count >= limit) break;
                HexCoord center = map.cells[region.centerCellIndex].coord;
                if (minimumCenterDistanceHexes > 0f &&
                    selectedCenters.Any(existing => HexCoord.Distance(existing, center) < minimumCenterDistanceHexes))
                    continue;
                result.Add(region);
                selectedCenters.Add(center);
            }
            return result;
        }
    }
}
