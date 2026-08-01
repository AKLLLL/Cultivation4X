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
        None = 0,
        Outer = 1,
        Influence = 2,
        Core = 3
    }

    public enum InfluenceSourceType
    {
        SectBase = 0
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
        public List<InfluenceSourceData> influenceSources = new List<InfluenceSourceData>();
        public List<CellInfluenceState> cellInfluences = new List<CellInfluenceState>();
        public bool isInfluenceDirty;
    }

    public static class WorldMapProgressRules
    {
        public const string PlayerSectBaseId = "player_sect_base";

        public static bool RevealCell(WorldMap map, WorldMapProgressState progress, int cellIndex)
        {
            if (!IsValidCell(map, cellIndex) || progress == null) return false;
            if (progress.revealedCellIndices == null) progress.revealedCellIndices = new List<int>();
            if (!progress.revealedCellIndices.Contains(cellIndex))
                progress.revealedCellIndices.Add(cellIndex);
            return true;
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

        internal static bool IsValidCell(WorldMap map, int cellIndex) =>
            map?.cells != null && cellIndex >= 0 && cellIndex < map.cells.Length;
    }
}
