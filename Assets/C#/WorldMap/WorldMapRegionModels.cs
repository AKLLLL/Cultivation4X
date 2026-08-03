using System;
using System.Collections.Generic;

namespace Cultivation4X.WorldMap
{
    public enum MapRegionType
    {
        SmallHill = 0,
        MountainRange = 1,
        Hills = 2,
        Plain = 3,
        Forest = 4,
        Valley = 5,
        Desert = 6,
        Swamp = 7,
        Lake = 8,
        OpenWater = 9
    }

    public enum MapInternalPositionTag
    {
        None = 0,
        MountainFoot = 1,
        Mountainside = 2,
        Ridge = 3,
        HillFoot = 4,
        Hilltop = 5,
        ForestEdge = 6,
        DeepForest = 7,
        ValleyEntrance = 8,
        ValleyFloor = 9,
        OpenPlain = 10,
        Riverbank = 11,
        DesertEdge = 12,
        Dune = 13,
        MarshEdge = 14,
        DeepMarsh = 15,
        Lakeshore = 16,
        Shallows = 17,
        Coastline = 18,
        DeepWater = 19,
        Summit = 20,
        MountainPass = 21,
        Cliff = 22,
        CaveMouth = 23,
        ForestClearing = 24,
        AncientGrove = 25,
        BeastTrail = 26,
        HerbSlope = 27,
        LakeCenter = 28,
        ReedShore = 29,
        WaterInlet = 30,
        WaterOutlet = 31
    }

    public enum MapRegionTrend { Low = 0, Normal = 1, High = 2 }

    [Serializable]
    public sealed class MapRegionData
    {
        public string regionId;
        public string regionName;
        public MapRegionType regionType;
        public List<int> cellIndices = new List<int>();
        public int centerCellIndex = -1;
        public LandformType dominantLandform;
        public BiomeType dominantBiome;
        public SpiritElement hiddenElementBias;
        public MapRegionTrend auraTrend;
        public MapRegionTrend dangerTrend;
        public float averageAura;
        public float averageDanger;
        public int displayPriority;
    }
}
