using System.Collections.Generic;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    internal enum WorldMapClimateDebugView
    {
        Normal,
        Biome,
        Temperature,
        Moisture,
        Rainfall,
        FreshWaterDistance,
        DrainageFlow,
        Elevation,
        // 玩法调试视图：只改变地表配色与灵脉覆盖层，不改变地形几何。
        Aura,
        DominantElement,
        SpiritVeinPaths
    }

    /// <summary>
    /// 3D 地形表现层的颜色与文字模型：颜色按地类 + 生物群系 + 高度组合，
    /// 地点图标按类型区分，与具体渲染组件分离以便测试。
    /// </summary>
    public static class TerrainPresentationModels
    {
        /// <summary>远景/近景切换阈值：横向可见格数达到该值后进入宏观区域视图。</summary>
        public const float FarViewHexes = 24f;

        /// <summary>区域覆盖层基准高度 = 区域最高地形 + 该偏移，贴近地形、不整体飘高。</summary>
        public const float RegionOverlayBaseOffset = 0.4f;

        public static float RegionOverlayBaseHeight(WorldMap map, MapRegionData region) =>
            TerrainMeshGenerator.RegionMaxStrategicSurfaceHeight(map, region) + RegionOverlayBaseOffset;

        /// <summary>
        /// 区域覆盖层的最小面积（下中位数）：只覆盖面积较大的约一半区域，
        /// 丢弃小碎块，避免色块太多混在一起。
        /// </summary>
        public static int RegionOverlayMinimumCells(WorldMap map)
        {
            if (map?.regions == null) return 1;
            List<int> sizes = new List<int>();
            foreach (MapRegionData region in map.regions)
            {
                if (region == null || region.regionType == MapRegionType.OpenWater) continue;
                if (region.cellIndices != null) sizes.Add(region.cellIndices.Count);
            }
            if (sizes.Count == 0) return 1;
            sizes.Sort();
            return sizes[(sizes.Count - 1) / 2];
        }

        /// <summary>
        /// 战略地表自然色：陆地以生物群系为主，丘陵/山地只做轻微土色或岩色混合；
        /// 数据高度不再改变明暗，避免每个 Hex 看起来像独立隆起的色块。
        /// </summary>
        public static Color ColorForCell(WorldCell cell)
        {
            if (cell == null) return Color.magenta;
            if (IsWater(cell.landform))
            {
                Color water = cell.landform == LandformType.DeepWater
                    ? new Color(0.08f, 0.28f, 0.46f)
                    : new Color(0.15f, 0.48f, 0.62f);
                water.a = 1f;
                return water;
            }

            Color color = BiomeBaseColor(cell.biome);
            switch (cell.landform)
            {
                case LandformType.Coast:
                    color = Color.Lerp(color, new Color(0.78f, 0.75f, 0.60f), 0.55f);
                    break;
                case LandformType.Hill:
                    color = Color.Lerp(color, new Color(0.46f, 0.42f, 0.26f), 0.12f);
                    break;
                case LandformType.Mountain:
                    color = Color.Lerp(color, new Color(0.42f, 0.44f, 0.40f), 0.22f);
                    break;
            }
            if (cell.landform == LandformType.Mountain && cell.isBuildable)
                color = Color.Lerp(color, new Color(0.63f, 0.55f, 0.38f), 0.42f);
            color.a = 1f;
            return color;
        }

        /// <summary>
        /// 远景色块专用：高辨识度纯色，不参与邻格混合，让选址阶段一眼可读。
        /// </summary>
        public static Color FarColorForCell(WorldCell cell)
        {
            if (cell == null) return Color.magenta;
            switch (cell.landform)
            {
                case LandformType.DeepWater: return new Color(0.08f, 0.30f, 0.48f, 1f);
                case LandformType.ShallowWater: return new Color(0.16f, 0.48f, 0.62f, 1f);
                case LandformType.Coast: return new Color(0.86f, 0.78f, 0.52f, 1f);
                case LandformType.Plain: return new Color(0.34f, 0.66f, 0.24f, 1f);
                case LandformType.Hill: return new Color(0.54f, 0.48f, 0.24f, 1f);
                case LandformType.Mountain: return new Color(0.52f, 0.44f, 0.36f, 1f);
                default: return Color.magenta;
            }
        }

        private static WorldMap climateDebugMap;

        internal static void SetClimateDebugMap(WorldMap map) => climateDebugMap = map;

        internal static Color ColorForClimateDebug(WorldCell cell, WorldMapClimateDebugView view)
        {
            if (cell == null) return Color.magenta;
            Color color;
            switch (view)
            {
                case WorldMapClimateDebugView.Biome:
                    color = BiomeBaseColor(cell.biome);
                    break;
                case WorldMapClimateDebugView.Temperature:
                    color = ThreeStopGradient(new Color(0.12f, 0.32f, 0.82f),
                        new Color(0.94f, 0.82f, 0.30f), new Color(0.86f, 0.20f, 0.12f),
                        cell.temperature);
                    break;
                case WorldMapClimateDebugView.Moisture:
                    color = ThreeStopGradient(new Color(0.72f, 0.48f, 0.22f),
                        new Color(0.28f, 0.66f, 0.32f), new Color(0.12f, 0.38f, 0.82f),
                        cell.moisture);
                    break;
                case WorldMapClimateDebugView.Rainfall:
                    color = DiagnosticGradient(climateDebugMap, cell.index, view,
                        new Color(0.28f, 0.22f, 0.18f), new Color(0.20f, 0.58f, 0.74f));
                    break;
                case WorldMapClimateDebugView.FreshWaterDistance:
                    color = DiagnosticGradient(climateDebugMap, cell.index, view,
                        new Color(0.10f, 0.62f, 0.92f), new Color(0.72f, 0.48f, 0.20f));
                    break;
                case WorldMapClimateDebugView.DrainageFlow:
                    color = DiagnosticGradient(climateDebugMap, cell.index, view,
                        new Color(0.10f, 0.12f, 0.16f), new Color(0.10f, 0.78f, 1.00f));
                    break;
                case WorldMapClimateDebugView.Elevation:
                    color = Color.Lerp(new Color(0.08f, 0.10f, 0.14f),
                        new Color(0.94f, 0.94f, 0.90f), Mathf.Clamp01(cell.height));
                    break;
                case WorldMapClimateDebugView.Aura:
                    color = Color.Lerp(new Color(0.06f, 0.09f, 0.16f),
                        new Color(0.85f, 0.27f, 0.94f), Mathf.Clamp01(cell.totalAura));
                    break;
                case WorldMapClimateDebugView.DominantElement:
                    color = DominantElementDebugColor(cell);
                    break;
                case WorldMapClimateDebugView.SpiritVeinPaths:
                    color = new Color(0.12f, 0.13f, 0.14f);
                    break;
                default:
                    return ColorForCell(cell);
            }
            color.a = 1f;
            return color;
        }

        /// <summary>五行专用颜色：金米白、木绿、水蓝、火红、土黄。</summary>
        public static Color SpiritElementColor(SpiritElement element)
        {
            switch (element)
            {
                case SpiritElement.Metal: return new Color(0.88f, 0.88f, 0.70f);
                case SpiritElement.Wood: return new Color(0.25f, 0.90f, 0.35f);
                case SpiritElement.Water: return new Color(0.20f, 0.65f, 1f);
                case SpiritElement.Fire: return new Color(1f, 0.25f, 0.12f);
                default: return new Color(0.85f, 0.65f, 0.25f);
            }
        }

        private static Color DominantElementDebugColor(WorldCell cell)
        {
            if (cell?.elementalAura == null) return new Color(0.12f, 0.13f, 0.14f);
            SpiritElement dominant = SpiritElement.Metal;
            float maximum = cell.elementalAura.metal;
            if (cell.elementalAura.wood > maximum)
            {
                dominant = SpiritElement.Wood;
                maximum = cell.elementalAura.wood;
            }
            if (cell.elementalAura.water > maximum)
            {
                dominant = SpiritElement.Water;
                maximum = cell.elementalAura.water;
            }
            if (cell.elementalAura.fire > maximum)
            {
                dominant = SpiritElement.Fire;
                maximum = cell.elementalAura.fire;
            }
            if (cell.elementalAura.earth > maximum) dominant = SpiritElement.Earth;
            return Color.Lerp(new Color(0.025f, 0.025f, 0.03f),
                SpiritElementColor(dominant), 0.15f + Mathf.Clamp01(cell.totalAura) * 0.85f);
        }

        private static Color DiagnosticGradient(WorldMap map, int cellIndex,
            WorldMapClimateDebugView view, Color low, Color high)
        {
            if (!WorldGenerationDiagnosticsStore.TryGet(map, out WorldGenerationDiagnostics diagnostics))
                return Color.magenta;
            float normalized;
            switch (view)
            {
                case WorldMapClimateDebugView.Rainfall:
                    normalized = diagnostics.rainfall.Length > cellIndex
                        ? Mathf.Clamp01(diagnostics.rainfall[cellIndex] / 0.08f)
                        : 0f;
                    break;
                case WorldMapClimateDebugView.FreshWaterDistance:
                    int distance = diagnostics.freshWaterDistance.Length > cellIndex
                        ? diagnostics.freshWaterDistance[cellIndex]
                        : int.MaxValue;
                    normalized = distance == int.MaxValue
                        ? 1f
                        : Mathf.Clamp01(distance / (float)Mathf.Max(1,
                            diagnostics.maximumFiniteFreshWaterDistance));
                    break;
                case WorldMapClimateDebugView.DrainageFlow:
                    float flow = diagnostics.accumulatedFlow.Length > cellIndex
                        ? diagnostics.accumulatedFlow[cellIndex]
                        : 0f;
                    normalized = diagnostics.maximumAccumulatedFlow <= 1f
                        ? 0f
                        : Mathf.Log(1f + flow) / Mathf.Log(1f + diagnostics.maximumAccumulatedFlow);
                    break;
                default:
                    normalized = 0f;
                    break;
            }
            return Color.Lerp(low, high, normalized);
        }

        private static Color ThreeStopGradient(Color low, Color middle, Color high, float value)
        {
            float clamped = Mathf.Clamp01(value);
            return clamped <= 0.5f
                ? Color.Lerp(low, middle, clamped * 2f)
                : Color.Lerp(middle, high, (clamped - 0.5f) * 2f);
        }

        private static Color BiomeBaseColor(BiomeType biome)
        {
            switch (biome)
            {
                case BiomeType.TemperateForest: return new Color(0.22f, 0.40f, 0.18f);
                case BiomeType.Rainforest: return new Color(0.10f, 0.31f, 0.16f);
                case BiomeType.Wetland: return new Color(0.20f, 0.34f, 0.30f);
                case BiomeType.Desert: return new Color(0.72f, 0.48f, 0.22f);
                case BiomeType.Tundra: return new Color(0.48f, 0.55f, 0.43f);
                case BiomeType.Snowfield: return new Color(0.78f, 0.82f, 0.80f);
                case BiomeType.Alpine: return new Color(0.46f, 0.50f, 0.44f);
                case BiomeType.Coast: return new Color(0.72f, 0.72f, 0.58f);
                default: return new Color(0.44f, 0.58f, 0.24f);
            }
        }

        public static Color ColorForSite(MapSiteType siteType)
        {
            switch (siteType)
            {
                case MapSiteType.SectBase: return new Color(1.00f, 0.80f, 0.20f);
                case MapSiteType.Village: return new Color(0.35f, 0.85f, 0.45f);
                case MapSiteType.SpiritSpring: return new Color(0.25f, 0.85f, 1.00f);
                case MapSiteType.SpiritMine: return new Color(0.75f, 0.85f, 1.00f);
                case MapSiteType.ResourceNode: return new Color(0.35f, 0.85f, 0.40f);
                case MapSiteType.CaveResidence: return new Color(0.62f, 0.45f, 0.28f);
                case MapSiteType.BeastLair: return new Color(0.85f, 0.28f, 0.25f);
                case MapSiteType.Ruin: return new Color(0.55f, 0.50f, 0.65f);
                default: return Color.white;
            }
        }

        public static string SiteLabel(MapSiteType siteType)
        {
            switch (siteType)
            {
                case MapSiteType.SectBase: return "宗门基址";
                case MapSiteType.Village: return "村庄";
                case MapSiteType.SpiritSpring: return "灵泉";
                case MapSiteType.SpiritMine: return "灵矿";
                case MapSiteType.ResourceNode: return "青木森林";
                case MapSiteType.CaveResidence: return "洞府";
                case MapSiteType.BeastLair: return "兽穴";
                case MapSiteType.Ruin: return "遗迹";
                default: return "地点";
            }
        }

        public static Color ColorForRegion(MapRegionType regionType)
        {
            switch (regionType)
            {
                case MapRegionType.SmallHill: return new Color(0.74f, 0.60f, 0.30f);
                case MapRegionType.MountainRange: return new Color(0.48f, 0.38f, 0.50f);
                case MapRegionType.Hills: return new Color(0.70f, 0.50f, 0.24f);
                case MapRegionType.Plain: return new Color(0.56f, 0.78f, 0.34f);
                case MapRegionType.Forest: return new Color(0.14f, 0.50f, 0.22f);
                case MapRegionType.Valley: return new Color(0.62f, 0.74f, 0.28f);
                case MapRegionType.Desert: return new Color(0.94f, 0.78f, 0.46f);
                case MapRegionType.Swamp: return new Color(0.22f, 0.46f, 0.30f);
                case MapRegionType.Lake: return new Color(0.16f, 0.60f, 0.88f);
                case MapRegionType.OpenWater: return new Color(0.06f, 0.32f, 0.58f);
                default: return Color.white;
            }
        }

        public static string RegionLabel(MapRegionType regionType)
        {
            switch (regionType)
            {
                case MapRegionType.SmallHill: return "小丘";
                case MapRegionType.MountainRange: return "山脉";
                case MapRegionType.Hills: return "丘陵";
                case MapRegionType.Plain: return "平原";
                case MapRegionType.Forest: return "森林";
                case MapRegionType.Valley: return "山谷";
                case MapRegionType.Desert: return "沙漠";
                case MapRegionType.Swamp: return "沼泽";
                case MapRegionType.Lake: return "湖泊";
                case MapRegionType.OpenWater: return "大海";
                default: return "区域";
            }
        }

        /// <summary>
        /// 当前相机横向可见的六边形格数（只由缩放程度决定，与平移位置无关）。
        /// 使用相机实际右方向计算旋转后的 Hex 投影宽度。
        /// </summary>
        public static float VisibleHexesAcross(Camera camera)
        {
            if (camera == null) return 0f;
            float sinPitch = Mathf.Clamp01(Mathf.Abs(camera.transform.forward.y));
            if (sinPitch < 0.05f) return 0f;
            float distance = camera.transform.position.y / sinPitch;
            float halfHorizontalFov = Mathf.Atan(
                Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * camera.aspect);
            float projectedHexWidth = TerrainMeshGenerator.ProjectedHexWidth(camera.transform.right);
            return Mathf.Max(0f, 2f * distance * Mathf.Tan(halfHorizontalFov) / projectedHexWidth);
        }

        private static bool IsWater(LandformType landform) =>
            landform == LandformType.DeepWater || landform == LandformType.ShallowWater;
    }
}
