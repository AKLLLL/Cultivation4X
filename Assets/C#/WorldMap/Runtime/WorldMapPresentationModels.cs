using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    public enum WorldMapTerrainIconKind { Water, Plain, Hill, Mountain, Forest, Snow }
    public enum WorldMapMarkerKind { FactionSeat, Village, Cave, PointOfInterest }
    public enum WorldMapIconDensityTier { Hidden, Sparse, Medium, Dense, Full }

    public static class WorldMapInfluencePresentation
    {
        public const string LegendText = "影响力：外缘（冷色细边）　影响（蓝色）　核心（金色）";

        public static bool TryGetOverlayStyle(InfluenceLevel level, out Color color, out float width)
        {
            switch (level)
            {
                case InfluenceLevel.Outer:
                    color = new Color(0.45f, 0.70f, 0.88f, 0.34f); width = 0.035f; return true;
                case InfluenceLevel.Influence:
                    color = new Color(0.30f, 0.66f, 1f, 0.62f); width = 0.065f; return true;
                case InfluenceLevel.Core:
                    color = new Color(1f, 0.74f, 0.18f, 0.95f); width = 0.11f; return true;
                default:
                    color = default(Color); width = 0f; return false;
            }
        }
    }

    [Serializable]
    public sealed class WorldMapPresentationMarker
    {
        public string id;
        public string label;
        public WorldMapMarkerKind kind;
        public int cellIndex = -1;
        public bool isDemo;
    }

    public struct WorldMapTerrainIconPlacement
    {
        public int cellIndex;
        public WorldMapTerrainIconKind kind;

        public WorldMapTerrainIconPlacement(int cellIndex, WorldMapTerrainIconKind kind)
        {
            this.cellIndex = cellIndex;
            this.kind = kind;
        }
    }

    public static class WorldMapPresentationPolicy
    {
        public static WorldMapIconDensityTier GetDensityTier(float projectedHexDiameter)
        {
            if (projectedHexDiameter < 18f) return WorldMapIconDensityTier.Hidden;
            if (projectedHexDiameter < 28f) return WorldMapIconDensityTier.Sparse;
            if (projectedHexDiameter < 44f) return WorldMapIconDensityTier.Medium;
            if (projectedHexDiameter < 70f) return WorldMapIconDensityTier.Dense;
            return WorldMapIconDensityTier.Full;
        }

        public static bool TerrainIconsVisible(WorldMapViewMode mode, WorldMapIconDensityTier tier)
        {
            if (tier == WorldMapIconDensityTier.Hidden ||
                mode == WorldMapViewMode.AuraConcentration ||
                mode == WorldMapViewMode.DominantElement ||
                mode == WorldMapViewMode.SpiritVeinPaths)
                return false;
            if (mode == WorldMapViewMode.Height ||
                mode == WorldMapViewMode.Temperature ||
                mode == WorldMapViewMode.Moisture)
                return tier >= WorldMapIconDensityTier.Dense;
            return true;
        }

        public static int GetStride(WorldMapIconDensityTier tier)
        {
            switch (tier)
            {
                case WorldMapIconDensityTier.Sparse: return 8;
                case WorldMapIconDensityTier.Medium: return 4;
                case WorldMapIconDensityTier.Dense: return 2;
                case WorldMapIconDensityTier.Full: return 1;
                default: return int.MaxValue;
            }
        }

        public static float MarkerAlpha(WorldMapViewMode mode)
        {
            return mode == WorldMapViewMode.AuraConcentration ||
                   mode == WorldMapViewMode.DominantElement ? 0.52f : 1f;
        }

        public static bool MarkerVisible(WorldMapPresentationMarker marker, WorldMapViewMode mode,
            WorldMapIconDensityTier tier, int seed)
        {
            if (marker == null) return false;
            if (mode == WorldMapViewMode.SpiritVeinPaths)
                return marker.kind == WorldMapMarkerKind.Cave;
            if (tier != WorldMapIconDensityTier.Hidden || marker.kind != WorldMapMarkerKind.Village)
                return true;
            return (StableUnsigned(seed, "overview-village-" + marker.id) % 3u) == 0u;
        }

        public static WorldMapTerrainIconKind GetTerrainIconKind(WorldCell cell)
        {
            if (cell.biome == BiomeType.Snowfield) return WorldMapTerrainIconKind.Snow;
            if (cell.biome == BiomeType.TemperateForest || cell.biome == BiomeType.Rainforest)
                return WorldMapTerrainIconKind.Forest;
            switch (cell.landform)
            {
                case LandformType.DeepWater:
                case LandformType.ShallowWater: return WorldMapTerrainIconKind.Water;
                case LandformType.Hill: return WorldMapTerrainIconKind.Hill;
                case LandformType.Mountain: return WorldMapTerrainIconKind.Mountain;
                default: return WorldMapTerrainIconKind.Plain;
            }
        }

        public static List<WorldMapTerrainIconPlacement> BuildTerrainIconPlacements(WorldMap map,
            WorldMapViewMode mode, float projectedHexDiameter, IEnumerable<WorldMapPresentationMarker> markers)
        {
            var result = new List<WorldMapTerrainIconPlacement>();
            if (map?.cells == null) return result;
            WorldMapIconDensityTier tier = GetDensityTier(projectedHexDiameter);
            if (!TerrainIconsVisible(mode, tier)) return result;

            int stride = GetStride(tier);
            var protectedCells = new HashSet<int>();
            foreach (WorldMapPresentationMarker marker in markers ?? Enumerable.Empty<WorldMapPresentationMarker>())
            {
                if (marker == null || marker.cellIndex < 0 || marker.cellIndex >= map.cells.Length) continue;
                int radius = marker.kind == WorldMapMarkerKind.FactionSeat || marker.kind == WorldMapMarkerKind.Cave ? 2 : 1;
                foreach (WorldCell cell in map.cells)
                    if (HexCoord.Distance(map.cells[marker.cellIndex].coord, cell.coord) <= radius)
                        protectedCells.Add(cell.index);
            }

            foreach (IGrouping<string, WorldCell> block in map.cells
                         .Where(cell => !protectedCells.Contains(cell.index))
                         .GroupBy(cell => (cell.coord.col / stride) + ":" + (cell.coord.row / stride)))
            {
                WorldCell selected = block
                    .OrderByDescending(cell => TerrainPriority(GetTerrainIconKind(cell)))
                    .ThenBy(cell => StableUnsigned(map.effectiveSeed, $"terrain-icon-{tier}-{cell.index}"))
                    .First();
                result.Add(new WorldMapTerrainIconPlacement(selected.index, GetTerrainIconKind(selected)));
            }
            return result.OrderBy(item => item.cellIndex).ToList();
        }

        private static int TerrainPriority(WorldMapTerrainIconKind kind)
        {
            switch (kind)
            {
                case WorldMapTerrainIconKind.Snow: return 6;
                case WorldMapTerrainIconKind.Mountain: return 5;
                case WorldMapTerrainIconKind.Forest: return 4;
                case WorldMapTerrainIconKind.Hill: return 3;
                case WorldMapTerrainIconKind.Water: return 2;
                default: return 1;
            }
        }

        internal static uint StableUnsigned(int seed, string label) => unchecked((uint)SeedDerivation.Derive(seed, label));
    }

    public static class WorldMapPresentationMarkerFactory
    {
        public static List<WorldMapPresentationMarker> CreatePointOfInterestMarkers(WorldMap map)
        {
            var result = new List<WorldMapPresentationMarker>();
            if (map?.pointsOfInterest == null) return result;
            foreach (WorldPointOfInterest point in map.pointsOfInterest.Where(point => point != null))
            {
                result.Add(new WorldMapPresentationMarker
                {
                    id = point.id,
                    label = PointLabel(point.id),
                    kind = WorldMapMarkerKind.PointOfInterest,
                    cellIndex = point.cellIndex
                });
            }
            return result;
        }

        private static string PointLabel(string id)
        {
            switch (id)
            {
                case "qingyun_outskirts": return "青云外围";
                case "mistwood": return "迷雾林";
                case "chixia_ridge": return "赤霞岭";
                default: return string.IsNullOrEmpty(id) ? "地点" : id;
            }
        }
    }

    public static class WorldMapCellDetailsFormatter
    {
        public static string Format(WorldMap map, int cellIndex, WorldMapViewMode mode, bool siteSelectionMode,
            IEnumerable<WorldMapPresentationMarker> markers, WorldMapProgressState progress, PlayerData sect)
        {
            if (siteSelectionMode || sect?.founding == null ||
                !FoundingRules.HasReachedCave(sect.founding))
                return Format(map, cellIndex, mode, siteSelectionMode, markers);
            if (map?.cells == null || cellIndex < 0 || cellIndex >= map.cells.Length)
                return "点击地图格查看详情。";

            CellInfluenceRuntimeState influence = WorldMapInfluenceRules.GetCellState(map, progress, cellIndex);
            WorldCell cell = map.cells[cellIndex];
            if (influence.knowledge == KnowledgeState.Unknown)
                return $"坐标 {cell.coord.col},{cell.coord.row}\n认知：未知";

            string terrain = $"{LandformLabel(cell.landform)}/{BiomeLabel(cell.biome)}";
            string markerText = string.Join("、", (markers ?? Enumerable.Empty<WorldMapPresentationMarker>())
                .Where(marker => marker != null && marker.cellIndex == cellIndex).Select(marker => marker.label));
            if (string.IsNullOrEmpty(markerText)) markerText = "无";
            string veinSummary = map.spiritVeins?.Any(vein =>
                vein?.pathCellIndices?.Contains(cellIndex) == true) == true ? "有" : "无";
            string danger = DangerLabel(WorldMapProgressRules.GetDanger(cell));

            string influenceSummary = $"认知：已知｜影响值：{influence.value}｜等级：{InfluenceLabel(influence.level)}｜" +
                                      $"控制宗门：{influence.controllerSectId ?? "无"}｜来源：" +
                                      (influence.sourceIds.Count == 0 ? "无" : string.Join("、", influence.sourceIds));
            if (influence.level != InfluenceLevel.Core)
                return $"坐标 {cell.coord.col},{cell.coord.row}｜{terrain}\n" +
                       $"灵气：{AuraBand(cell.totalAura)}｜主五行：{DominantElementLabel(cell)}｜灵脉：{veinSummary}\n" +
                       $"危险：{danger}｜地点：{markerText}\n{influenceSummary}";

            string veins = string.Join("、", (map.spiritVeins ?? new List<SpiritVein>())
                .Where(vein => vein?.pathCellIndices?.Contains(cellIndex) == true)
                .Select(vein => $"{vein.id}({vein.primaryElement}/{vein.size})"));
            if (string.IsNullOrEmpty(veins)) veins = "无";
            return $"坐标 {cell.coord.col},{cell.coord.row}｜{terrain}\n{influenceSummary}\n" +
                   $"高度 {cell.height:0.000}｜温度 {cell.temperature:0.000}｜湿度 {cell.moisture:0.000}\n" +
                   $"基础灵气 {cell.baseAura:0.000}｜总灵气 {cell.totalAura:0.000}\n" +
                   $"金 {cell.elementalAura.metal:0.000} 木 {cell.elementalAura.wood:0.000} 水 {cell.elementalAura.water:0.000} 火 {cell.elementalAura.fire:0.000} 土 {cell.elementalAura.earth:0.000}\n" +
                   $"危险：{danger}｜地点：{markerText}｜灵脉：{veins}";
        }

        private static string InfluenceLabel(InfluenceLevel level)
        {
            switch (level)
            {
                case InfluenceLevel.Outer: return "外缘";
                case InfluenceLevel.Influence: return "影响";
                case InfluenceLevel.Core: return "核心";
                default: return "无";
            }
        }

        public static string Format(WorldMap map, int cellIndex, WorldMapViewMode mode, bool siteSelectionMode,
            IEnumerable<WorldMapPresentationMarker> markers)
        {
            if (map?.cells == null || cellIndex < 0 || cellIndex >= map.cells.Length)
                return "点击地图格查看详情。";
            WorldCell cell = map.cells[cellIndex];
            string terrain = $"{LandformLabel(cell.landform)}/{BiomeLabel(cell.biome)}";
            if (siteSelectionMode)
            {
                return $"格 {cell.coord.col},{cell.coord.row}｜{terrain}\n灵气：{AuraBand(cell.totalAura)}｜可建设：{(cell.isBuildable ? "是" : "否")}";
            }

            int incoming = map.rivers?.Count(segment => segment.toCellIndex == cellIndex) ?? 0;
            RiverSegment outgoing = map.rivers?.FirstOrDefault(segment => segment.fromCellIndex == cellIndex);
            string river = incoming == 0 && outgoing == null ? "无" :
                $"入流{incoming}，出流{(outgoing == null ? "无" : outgoing.flow.ToString("0.0"))}";
            string markerText = string.Join("、", (markers ?? Enumerable.Empty<WorldMapPresentationMarker>())
                .Where(marker => marker != null && marker.cellIndex == cellIndex).Select(marker => marker.label));
            if (string.IsNullOrEmpty(markerText)) markerText = "无";

            var text = new StringBuilder();
            text.AppendLine($"索引 {cell.index}｜坐标 {cell.coord.col},{cell.coord.row}｜{terrain}｜可建设：{(cell.isBuildable ? "是" : "否")}");
            text.AppendLine($"高度 {cell.height:0.000}｜温度 {cell.temperature:0.000}｜湿度 {cell.moisture:0.000}");
            text.AppendLine($"基础灵气 {cell.baseAura:0.000}｜总灵气 {cell.totalAura:0.000}");
            text.AppendLine($"金 {cell.elementalAura.metal:0.000} 木 {cell.elementalAura.wood:0.000} 水 {cell.elementalAura.water:0.000} 火 {cell.elementalAura.fire:0.000} 土 {cell.elementalAura.earth:0.000}");
            text.AppendLine($"河流：{river}｜标记：{markerText}");
            if (mode == WorldMapViewMode.SpiritVeinPaths)
            {
                string veins = string.Join("、", (map.spiritVeins ?? new List<SpiritVein>())
                    .Where(vein => vein.pathCellIndices.Contains(cellIndex))
                    .Select(vein => $"{vein.id}({vein.primaryElement}/{vein.size})"));
                text.Append("灵脉：" + (string.IsNullOrEmpty(veins) ? "无" : veins));
            }
            return text.ToString().TrimEnd();
        }

        public static string LandformLabel(LandformType landform)
        {
            switch (landform)
            {
                case LandformType.DeepWater: return "深水";
                case LandformType.ShallowWater: return "浅水";
                case LandformType.Coast: return "海岸";
                case LandformType.Plain: return "平原";
                case LandformType.Hill: return "丘陵";
                case LandformType.Mountain: return "山地";
                default: return landform.ToString();
            }
        }

        public static string BiomeLabel(BiomeType biome)
        {
            switch (biome)
            {
                case BiomeType.Ocean: return "海洋";
                case BiomeType.Coast: return "海岸";
                case BiomeType.Grassland: return "草原";
                case BiomeType.TemperateForest: return "温带森林";
                case BiomeType.Rainforest: return "雨林";
                case BiomeType.Wetland: return "湿地";
                case BiomeType.Desert: return "荒漠";
                case BiomeType.Tundra: return "冻原";
                case BiomeType.Snowfield: return "雪原";
                case BiomeType.Alpine: return "高山";
                default: return biome.ToString();
            }
        }

        public static string AuraBand(float aura) =>
            aura < 0.25f ? "低" : aura < 0.5f ? "普通" : aura < 0.75f ? "浓郁" : "极高";

        public static string DominantElementLabel(WorldCell cell)
        {
            if (cell == null) return "无";
            string label = "金";
            float maximum = cell.elementalAura.metal;
            if (cell.elementalAura.wood > maximum) { label = "木"; maximum = cell.elementalAura.wood; }
            if (cell.elementalAura.water > maximum) { label = "水"; maximum = cell.elementalAura.water; }
            if (cell.elementalAura.fire > maximum) { label = "火"; maximum = cell.elementalAura.fire; }
            if (cell.elementalAura.earth > maximum) label = "土";
            return label;
        }

        public static string DangerLabel(WorldDangerLevel level)
        {
            switch (level)
            {
                case WorldDangerLevel.High: return "高";
                case WorldDangerLevel.Medium: return "中";
                default: return "低";
            }
        }
    }
}
