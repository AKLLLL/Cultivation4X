using System;
using System.Collections.Generic;

namespace Cultivation4X.WorldMap
{
    [Serializable]
    public struct HexCoord : IEquatable<HexCoord>
    {
        public int col;
        public int row;
        public HexCoord(int col, int row) { this.col = col; this.row = row; }
        public bool Equals(HexCoord other) => col == other.col && row == other.row;
        public override bool Equals(object obj) => obj is HexCoord other && Equals(other);
        public override int GetHashCode() => unchecked((col * 397) ^ row);
        public static int Distance(HexCoord a, HexCoord b)
        {
            int aq = a.col - (a.row - (a.row & 1)) / 2;
            int bq = b.col - (b.row - (b.row & 1)) / 2;
            int ar = a.row;
            int br = b.row;
            return Math.Max(Math.Abs(aq - bq), Math.Max(Math.Abs(ar - br), Math.Abs((-aq - ar) - (-bq - br))));
        }
    }

    public enum LandformType { DeepWater, ShallowWater, Coast, Plain, Hill, Mountain }
    public enum BiomeType { Ocean, Coast, Grassland, TemperateForest, Rainforest, Wetland, Desert, Tundra, Snowfield, Alpine }
    public enum SpiritVeinSize { Medium, Large }
    public enum SpiritElement { Metal, Wood, Water, Fire, Earth }

    [Serializable]
    public class ElementalAura
    {
        public float metal;
        public float wood;
        public float water;
        public float fire;
        public float earth;
        public float Total => metal + wood + water + fire + earth;
        public void Add(SpiritElement element, float amount)
        {
            switch (element)
            {
                case SpiritElement.Metal: metal += amount; break;
                case SpiritElement.Wood: wood += amount; break;
                case SpiritElement.Water: water += amount; break;
                case SpiritElement.Fire: fire += amount; break;
                case SpiritElement.Earth: earth += amount; break;
            }
        }
        public void Scale(float scale)
        {
            metal *= scale; wood *= scale; water *= scale; fire *= scale; earth *= scale;
        }
    }

    [Serializable]
    public class WorldCell
    {
        public int index;
        public HexCoord coord;
        public float height;
        public float temperature;
        public float moisture;
        public LandformType landform;
        public BiomeType biome;
        public bool isBuildable;
        public float baseAura;
        public float totalAura;
        public ElementalAura elementalAura = new ElementalAura();
    }

    [Serializable]
    public class RiverSegment
    {
        public string riverId;
        public int fromCellIndex;
        public int toCellIndex;
        public int edgeDirection;
        public float flow;
    }

    [Serializable]
    public class SpiritVein
    {
        public string id;
        public SpiritVeinSize size;
        public SpiritElement primaryElement;
        public List<int> pathCellIndices = new List<int>();
        public float strength;
        public int influenceRadius;
    }

    [Serializable]
    public class WorldPointOfInterest
    {
        public string id;
        public int cellIndex = -1;
    }

    [Serializable]
    public class InclusiveIntRange
    {
        public int min;
        public int max;

        public InclusiveIntRange() { }
        public InclusiveIntRange(int min, int max) { this.min = min; this.max = max; }
        public InclusiveIntRange Clone() => new InclusiveIntRange(min, max);
    }

    [Serializable]
    public class TerrainGenerationParameters
    {
        public float deepWaterThreshold = 0.31f;
        public float seaLevel = 0.43f;
        public float plainUpperThreshold = 0.57f;
        public float hillUpperThreshold = 0.75f;

        public TerrainGenerationParameters Clone() => new TerrainGenerationParameters
        {
            deepWaterThreshold = deepWaterThreshold,
            seaLevel = seaLevel,
            plainUpperThreshold = plainUpperThreshold,
            hillUpperThreshold = hillUpperThreshold
        };
    }

    [Serializable]
    public class ClimateGenerationParameters
    {
        public float latitudeCoolingStrength = 0.78f;
        public float temperatureNoiseStrength = 0.25f;
        public float elevationCoolingStrength = 0.9f;
        public float moistureNoiseStrength = 0.75f;
        public float waterProximityMoistureStrength = 0.25f;
        public float riverMoistureBoost = 0.16f;

        public ClimateGenerationParameters Clone() => new ClimateGenerationParameters
        {
            latitudeCoolingStrength = latitudeCoolingStrength,
            temperatureNoiseStrength = temperatureNoiseStrength,
            elevationCoolingStrength = elevationCoolingStrength,
            moistureNoiseStrength = moistureNoiseStrength,
            waterProximityMoistureStrength = waterProximityMoistureStrength,
            riverMoistureBoost = riverMoistureBoost
        };
    }

    [Serializable]
    public class RiverGenerationParameters
    {
        public float minimumAccumulatedFlow = 64f;
        public float minimumSourceHeight = 0.55f;
        public int minimumBranchLength = 8;

        public RiverGenerationParameters Clone() => new RiverGenerationParameters
        {
            minimumAccumulatedFlow = minimumAccumulatedFlow,
            minimumSourceHeight = minimumSourceHeight,
            minimumBranchLength = minimumBranchLength
        };
    }

    [Serializable]
    public class SpiritVeinGenerationParameters
    {
        public InclusiveIntRange largeCount = new InclusiveIntRange(10, 14);
        public InclusiveIntRange mediumCount = new InclusiveIntRange(32, 48);
        public InclusiveIntRange largeLength = new InclusiveIntRange(36, 64);
        public InclusiveIntRange mediumLength = new InclusiveIntRange(12, 28);
        public InclusiveIntRange largeRadius = new InclusiveIntRange(6, 10);
        public InclusiveIntRange mediumRadius = new InclusiveIntRange(3, 5);

        public SpiritVeinGenerationParameters Clone() => new SpiritVeinGenerationParameters
        {
            largeCount = largeCount?.Clone(),
            mediumCount = mediumCount?.Clone(),
            largeLength = largeLength?.Clone(),
            mediumLength = mediumLength?.Clone(),
            largeRadius = largeRadius?.Clone(),
            mediumRadius = mediumRadius?.Clone()
        };
    }

    [Serializable]
    public class MapGenerationSettings
    {
        public int width = 128;
        public int height = 96;
        public int seed = 48621;
        public int generationVersion = 3;
        public TerrainGenerationParameters terrain = new TerrainGenerationParameters();
        public ClimateGenerationParameters climate = new ClimateGenerationParameters();
        public RiverGenerationParameters rivers = new RiverGenerationParameters();
        public SpiritVeinGenerationParameters spiritVeins = new SpiritVeinGenerationParameters();

        public MapGenerationSettings Clone() => new MapGenerationSettings
        {
            width = width,
            height = height,
            seed = seed,
            generationVersion = generationVersion,
            terrain = terrain?.Clone(),
            climate = climate?.Clone(),
            rivers = rivers?.Clone(),
            spiritVeins = spiritVeins?.Clone()
        };
    }

    [Serializable]
    public class WorldMap
    {
        public int userSeed;
        public int effectiveSeed;
        public int generationVersion;
        public int width;
        public int height;
        public MapGenerationSettings generationSettings;
        public WorldCell[] cells = Array.Empty<WorldCell>();
        public List<RiverSegment> rivers = new List<RiverSegment>();
        public List<SpiritVein> spiritVeins = new List<SpiritVein>();
        public List<WorldPointOfInterest> pointsOfInterest = new List<WorldPointOfInterest>();
        private static readonly int[,] EvenRowDirections =
        {
            { 1, 0 }, { 0, 1 }, { -1, 1 }, { -1, 0 }, { -1, -1 }, { 0, -1 }
        };
        private static readonly int[,] OddRowDirections =
        {
            { 1, 0 }, { 1, 1 }, { 0, 1 }, { -1, 0 }, { 0, -1 }, { 1, -1 }
        };
        public bool IsInBounds(HexCoord coord) =>
            coord.col >= 0 && coord.col < width && coord.row >= 0 && coord.row < height;
        public int GetIndex(HexCoord coord) => IsInBounds(coord) ? coord.row * width + coord.col : -1;
        public WorldCell GetCell(HexCoord coord)
        {
            int index = GetIndex(coord);
            return index < 0 || cells == null || index >= cells.Length ? null : cells[index];
        }
        public HexCoord GetNeighbor(HexCoord coord, int direction)
        {
            int normalized = ((direction % 6) + 6) % 6;
            int[,] directions = (coord.row & 1) == 0 ? EvenRowDirections : OddRowDirections;
            return new HexCoord(coord.col + directions[normalized, 0], coord.row + directions[normalized, 1]);
        }
        public IEnumerable<int> GetNeighborIndices(int cellIndex)
        {
            if (cells == null || cellIndex < 0 || cellIndex >= cells.Length) yield break;
            HexCoord coord = cells[cellIndex].coord;
            for (int direction = 0; direction < 6; direction++)
            {
                int index = GetIndex(GetNeighbor(coord, direction));
                if (index >= 0) yield return index;
            }
        }
        public int GetDirection(int fromCellIndex, int toCellIndex)
        {
            if (cells == null || fromCellIndex < 0 || toCellIndex < 0 ||
                fromCellIndex >= cells.Length || toCellIndex >= cells.Length) return -1;
            HexCoord from = cells[fromCellIndex].coord;
            HexCoord to = cells[toCellIndex].coord;
            for (int direction = 0; direction < 6; direction++)
                if (GetNeighbor(from, direction).Equals(to)) return direction;
            return -1;
        }
    }

    public static class WorldMapSession
    {
        public static WorldMap Current { get; private set; }
        public static WorldMapProgressState Progress { get; private set; }
        public static void Set(WorldMap map) => Set(map, new WorldMapProgressState());
        public static void Set(WorldMap map, WorldMapProgressState progress)
        {
            Current = map;
            Progress = progress ?? new WorldMapProgressState();
        }
        public static void Clear()
        {
            Current = null;
            Progress = null;
        }
    }
}
