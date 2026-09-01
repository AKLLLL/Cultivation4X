using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    public static class SectFunctionalZoneRules
    {
        public const float PlannedThreshold = 8f;
        public const float DevelopingThreshold = 10f;
        public const float HarvestReadyThreshold = 4f;
        public const string HerbItemId = "herb_qingling";

        public static SectFunctionalZoneState GetZone(WorldMapProgressState progress, int cellIndex) =>
            progress?.functionalZones?.FirstOrDefault(zone => zone != null && zone.cellIndex == cellIndex);

        public static bool CanPlan(WorldMap map, WorldMapProgressState progress, int cellIndex,
            out string reason)
        {
            if (!WorldMapProgressRules.IsValidCell(map, cellIndex) || progress == null)
            { reason = "地图格数据无效"; return false; }
            if (GetZone(progress, cellIndex) != null)
            { reason = "该区域已经规划用途"; return false; }
            if (progress.exploredCellIndices?.Contains(cellIndex) != true)
            { reason = "必须先完成该区域探索"; return false; }
            CellInfluenceRuntimeState influence = WorldMapInfluenceRules.GetCellState(map, progress, cellIndex);
            if (influence.controllerSectId != WorldMapProgressRules.PlayerSectOwnerId ||
                influence.level < InfluenceLevel.Influence)
            { reason = "需要宗门达到稳定影响或核心控制"; return false; }
            WorldCell cell = map.cells[cellIndex];
            if (!cell.isBuildable || cell.landform == LandformType.DeepWater ||
                cell.landform == LandformType.ShallowWater)
            { reason = "该地形不适合规划灵植区"; return false; }
            if (progress.mapSites?.Any(site => site != null && site.cellIndex == cellIndex) == true)
            { reason = "已有地点的区域不能重复规划"; return false; }
            if (progress.resourceNodes?.Any(node => node != null && node.cellIndex == cellIndex) == true)
            { reason = "已有资源节点的区域不能重复规划"; return false; }
            reason = null;
            return true;
        }

        public static bool TryPlan(WorldMap map, WorldMapProgressState progress, int cellIndex,
            out SectFunctionalZoneState zone, out string reason)
        {
            zone = null;
            if (!CanPlan(map, progress, cellIndex, out reason)) return false;
            if (progress.functionalZones == null)
                progress.functionalZones = new List<SectFunctionalZoneState>();
            zone = new SectFunctionalZoneState
            {
                zoneId = ZoneId(cellIndex),
                cellIndex = cellIndex,
                type = FunctionalZoneType.HerbCultivation,
                stage = FunctionalZoneStage.Planned
            };
            progress.functionalZones.Add(zone);
            return true;
        }

        public static bool TryCancel(WorldMapProgressState progress, int cellIndex, out string reason)
        {
            SectFunctionalZoneState zone = GetZone(progress, cellIndex);
            if (zone == null) { reason = "该区域没有功能区规划"; return false; }
            progress.functionalZones.Remove(zone);
            reason = null;
            return true;
        }

        public static string ZoneId(int cellIndex) => $"sect_zone_cell_{cellIndex}";

        public static string DisplayName(WorldMap map, SectFunctionalZoneState zone)
        {
            if (zone == null) return "未知功能区";
            if (!WorldMapProgressRules.IsValidCell(map, zone.cellIndex)) return "灵植区";
            HexCoord coord = map.cells[zone.cellIndex].coord;
            return $"灵植区（{coord.col},{coord.row}）";
        }

        public static string StageName(FunctionalZoneStage stage)
        {
            switch (stage)
            {
                case FunctionalZoneStage.Planned: return "待开垦";
                case FunctionalZoneStage.Developing: return "试种地";
                case FunctionalZoneStage.Operational: return "药圃";
                default: return stage.ToString();
            }
        }

        public static float SuitabilityMultiplier(WorldCell cell)
        {
            if (cell == null) return 1f;
            bool favorableBiome = cell.biome == BiomeType.TemperateForest ||
                                   cell.biome == BiomeType.Rainforest ||
                                   cell.biome == BiomeType.Wetland;
            bool harshBiome = cell.biome == BiomeType.Desert ||
                              cell.biome == BiomeType.Tundra ||
                              cell.biome == BiomeType.Snowfield ||
                              cell.biome == BiomeType.Alpine;
            if (harshBiome || cell.moisture < 0.25f) return 0.8f;
            if (favorableBiome && cell.moisture >= 0.55f) return 1.2f;
            return 1f;
        }

        public static string SuitabilityName(WorldCell cell)
        {
            float multiplier = SuitabilityMultiplier(cell);
            return multiplier > 1f ? "优良" : multiplier < 1f ? "艰难" : "一般";
        }

        public static float ProgressThreshold(SectFunctionalZoneState zone)
        {
            if (zone == null) return 0f;
            switch (zone.stage)
            {
                case FunctionalZoneStage.Planned: return PlannedThreshold;
                case FunctionalZoneStage.Developing: return DevelopingThreshold;
                default: return HarvestReadyThreshold;
            }
        }

        public static string ProgressText(SectFunctionalZoneState zone)
        {
            if (zone == null) return "无";
            if (zone.stage == FunctionalZoneStage.Operational)
                return $"照料 {zone.harvestProgress:0.0}/{HarvestReadyThreshold:0}";
            return $"经营 {zone.phaseProgress:0.0}/{ProgressThreshold(zone):0}";
        }
    }
}
