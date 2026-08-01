using System;
using System.Collections.Generic;
using System.Linq;

namespace Cultivation4X.WorldMap
{
    public enum KnowledgeState
    {
        Unknown,
        Known
    }

    public enum InfluenceLevel
    {
        None,
        Influence,
        Core
    }

    public enum MapSiteType
    {
        SectBase
    }

    public enum WorldDangerLevel
    {
        Low,
        Medium,
        High
    }

    [Serializable]
    public sealed class MapSiteData
    {
        public string siteId;
        public int cellIndex = -1;
        public MapSiteType siteType;
        public string siteName;
        public bool isRevealed;
        public bool canInteract;
    }

    [Serializable]
    public sealed class WorldMapProgressState
    {
        public List<int> revealedCellIndices = new List<int>();
        public List<MapSiteData> mapSites = new List<MapSiteData>();
    }

    public static class WorldMapProgressRules
    {
        public const string PlayerSectBaseId = "player_sect_base";

        public static InfluenceLevel GetInfluence(WorldMap map, int cellIndex, int homeCellIndex, int radius)
        {
            if (!IsValidCell(map, cellIndex) || !IsValidCell(map, homeCellIndex) || radius < 0)
                return InfluenceLevel.None;
            int distance = HexCoord.Distance(map.cells[cellIndex].coord, map.cells[homeCellIndex].coord);
            if (distance == 0) return InfluenceLevel.Core;
            return distance <= radius ? InfluenceLevel.Influence : InfluenceLevel.None;
        }

        public static KnowledgeState GetKnowledge(WorldMap map, WorldMapProgressState progress,
            int cellIndex, int homeCellIndex, int radius)
        {
            if (!IsValidCell(map, cellIndex)) return KnowledgeState.Unknown;
            if (GetInfluence(map, cellIndex, homeCellIndex, radius) != InfluenceLevel.None)
                return KnowledgeState.Known;
            return progress?.revealedCellIndices?.Contains(cellIndex) == true
                ? KnowledgeState.Known
                : KnowledgeState.Unknown;
        }

        public static bool RevealCell(WorldMap map, WorldMapProgressState progress, int cellIndex)
        {
            if (!IsValidCell(map, cellIndex) || progress == null) return false;
            if (progress.revealedCellIndices == null) progress.revealedCellIndices = new List<int>();
            if (!progress.revealedCellIndices.Contains(cellIndex))
                progress.revealedCellIndices.Add(cellIndex);
            return true;
        }

        public static int CountInfluenceCells(WorldMap map, int homeCellIndex, int radius)
        {
            if (map?.cells == null) return 0;
            return map.cells.Count(cell =>
                GetInfluence(map, cell.index, homeCellIndex, radius) != InfluenceLevel.None);
        }

        public static WorldDangerLevel GetDanger(WorldCell cell)
        {
            if (cell == null) return WorldDangerLevel.Low;
            if (cell.landform == LandformType.Mountain ||
                cell.biome == BiomeType.Alpine ||
                cell.biome == BiomeType.Snowfield ||
                cell.biome == BiomeType.Desert)
                return WorldDangerLevel.High;
            if (cell.landform == LandformType.Hill ||
                cell.biome == BiomeType.TemperateForest ||
                cell.biome == BiomeType.Rainforest ||
                cell.biome == BiomeType.Wetland ||
                cell.biome == BiomeType.Tundra)
                return WorldDangerLevel.Medium;
            return WorldDangerLevel.Low;
        }

        public static MapSiteData GetSectBase(WorldMapProgressState progress) =>
            progress?.mapSites?.FirstOrDefault(site =>
                site != null && site.siteType == MapSiteType.SectBase);

        private static bool IsValidCell(WorldMap map, int cellIndex) =>
            map?.cells != null && cellIndex >= 0 && cellIndex < map.cells.Length;
    }
}
