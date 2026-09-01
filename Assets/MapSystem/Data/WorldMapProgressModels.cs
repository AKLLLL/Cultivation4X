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
        SectBase = 0,
        Village = 1,
        SpiritSpring = 2,
        SpiritMine = 3,
        CaveResidence = 4,
        BeastLair = 5,
        Ruin = 6,
        ResourceNode = 7
    }

    public enum MapContentRevealState
    {
        Hidden = 0,
        Hinted = 1,
        Discovered = 2
    }

    public enum MapSiteState
    {
        None = 0,
        Investigated = 1,
        Developed = 2
    }

    public enum MapActionType
    {
        None = 0,
        Explore = 1,
        InvestigateSpiritSpring = 2,
        DevelopSpiritSpring = 3,
        EstablishVillageRelation = 4,
        DevelopSpiritMine = 5,
        BuildCaveResidenceOutpost = 6,
        ClearBeastLair = 7,
        InvestigateRuin = 8,
        DevelopResourceNode = 9
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
        public MapContentRevealState revealState;
        public MapSiteState siteState;
        public string ownerSectId;
        public int discoveredDay = -1;
        public int lastUpdatedDay = -1;
        public List<string> tags = new List<string>();
        public List<string> availableActionIds = new List<string>();
    }

    [Serializable]
    public sealed class WorldMapProgressState
    {
        public List<int> revealedCellIndices = new List<int>();
        public List<int> exploredCellIndices = new List<int>();
        public List<MapSiteData> mapSites = new List<MapSiteData>();
        public List<InfluenceSourceData> influenceSources = new List<InfluenceSourceData>();
        public List<CellInfluenceState> cellInfluences = new List<CellInfluenceState>();
        public List<ResourceNodeRuntime> resourceNodes = new List<ResourceNodeRuntime>();
        public List<SpiritualVeinRuntime> spiritualVeins = new List<SpiritualVeinRuntime>();
        public List<SectFunctionalZoneState> functionalZones = new List<SectFunctionalZoneState>();
        public bool isInfluenceDirty;
    }

    public static class WorldMapProgressRules
    {
        public const string PlayerSectBaseId = "player_sect_base";
        public const string PlayerSectOwnerId = "player_sect";

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
