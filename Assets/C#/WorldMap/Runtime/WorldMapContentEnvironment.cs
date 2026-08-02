using System;
using System.Collections.Generic;
using System.Linq;

namespace Cultivation4X.WorldMap
{
    public enum WorldMapEnvironmentHintKind
    {
        Moisture = 0,
        MineralVein = 1,
        BeastTracks = 2,
        RuinedWalls = 3,
        SettlementSigns = 4,
        CaveSigns = 5
    }

    [Serializable]
    public sealed class WorldMapEnvironmentHint
    {
        public string id;
        public string sourceSiteId;
        public int cellIndex = -1;
        public WorldMapEnvironmentHintKind kind;
        public string label;
    }

    /// <summary>
    /// 环境暗示是由地图与地点候选派生出的表现数据，不写入存档。
    /// </summary>
    public static class WorldMapContentEnvironmentRules
    {
        public static IReadOnlyList<WorldMapEnvironmentHint> BuildHints(
            WorldMap map, WorldMapProgressState progress)
        {
            var result = new List<WorldMapEnvironmentHint>();
            if (map?.cells == null || progress?.mapSites == null) return result;

            foreach (MapSiteData site in progress.mapSites
                         .Where(item => item != null && item.siteType != MapSiteType.SectBase)
                         .OrderBy(item => item.siteId, StringComparer.Ordinal))
            {
                if (site.cellIndex < 0 || site.cellIndex >= map.cells.Length || map.cells[site.cellIndex] == null)
                    continue;
                HashSet<int> occupiedSiteCells = new HashSet<int>(progress.mapSites
                    .Where(item => item != null && item.cellIndex >= 0 && item.cellIndex < map.cells.Length)
                    .Select(item => item.cellIndex));
                WorldMapEnvironmentHintKind kind = KindFor(site.siteType);
                List<WorldCell> candidates = map.cells
                    .Where(cell => cell != null && cell.index != site.cellIndex &&
                                   !occupiedSiteCells.Contains(cell.index) &&
                                   HexCoord.Distance(map.cells[site.cellIndex].coord, cell.coord) <= 2)
                    .OrderBy(cell => HexCoord.Distance(map.cells[site.cellIndex].coord, cell.coord))
                    .ThenBy(cell => WorldMapPresentationPolicy.StableUnsigned(map.effectiveSeed,
                        "environment-hint-" + site.siteId + "-" + cell.index))
                    .ThenBy(cell => cell.index)
                    .ToList();
                int count = Math.Min(3, candidates.Count);
                if (count < 2) count = candidates.Count;
                for (int index = 0; index < count; index++)
                {
                    WorldCell cell = candidates[index];
                    // 影响缓存本身授予战略地图认知；环境暗示只需处于
                    // Known 范围即可显示，不把影响范围写入探索真源。
                    if (WorldMapInfluenceRules.GetCellState(map, progress, cell.index).knowledge !=
                        KnowledgeState.Known) continue;
                    result.Add(new WorldMapEnvironmentHint
                    {
                        id = site.siteId + "-environment-" + index,
                        sourceSiteId = site.siteId,
                        cellIndex = cell.index,
                        kind = kind,
                        label = LabelFor(kind)
                    });
                }
            }
            return result.OrderBy(item => item.cellIndex).ThenBy(item => item.id, StringComparer.Ordinal).ToList();
        }

        public static string LabelFor(WorldMapEnvironmentHintKind kind)
        {
            switch (kind)
            {
                case WorldMapEnvironmentHintKind.Moisture: return "水汽痕迹";
                case WorldMapEnvironmentHintKind.MineralVein: return "矿脉露头";
                case WorldMapEnvironmentHintKind.BeastTracks: return "兽迹";
                case WorldMapEnvironmentHintKind.RuinedWalls: return "残垣";
                case WorldMapEnvironmentHintKind.SettlementSigns: return "聚落迹象";
                case WorldMapEnvironmentHintKind.CaveSigns: return "洞穴迹象";
                default: return "环境暗示";
            }
        }

        private static WorldMapEnvironmentHintKind KindFor(MapSiteType type)
        {
            switch (type)
            {
                case MapSiteType.SpiritSpring: return WorldMapEnvironmentHintKind.Moisture;
                case MapSiteType.SpiritMine: return WorldMapEnvironmentHintKind.MineralVein;
                case MapSiteType.BeastLair: return WorldMapEnvironmentHintKind.BeastTracks;
                case MapSiteType.Ruin: return WorldMapEnvironmentHintKind.RuinedWalls;
                case MapSiteType.Village: return WorldMapEnvironmentHintKind.SettlementSigns;
                case MapSiteType.CaveResidence: return WorldMapEnvironmentHintKind.CaveSigns;
                default: return WorldMapEnvironmentHintKind.SettlementSigns;
            }
        }
    }
}
