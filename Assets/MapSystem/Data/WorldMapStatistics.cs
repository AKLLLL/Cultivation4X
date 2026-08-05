using System;
using System.Collections.Generic;
using System.Linq;

namespace Cultivation4X.WorldMap
{
    [Serializable]
    public class LandformStatistic
    {
        public LandformType type;
        public int count;
        public float percentage;
    }

    [Serializable]
    public class BiomeStatistic
    {
        public BiomeType type;
        public int count;
        public float percentage;
    }

    [Serializable]
    public class ScalarSummary
    {
        public float min;
        public float median;
        public float max;
    }

    [Serializable]
    public class HistogramBin
    {
        public float lowerInclusive;
        public float upperExclusive;
        public int count;
        public float percentage;
    }

    [Serializable]
    public class WorldMapStatistics
    {
        public List<LandformStatistic> landforms = new List<LandformStatistic>();
        public List<BiomeStatistic> biomes = new List<BiomeStatistic>();
        public ScalarSummary height = new ScalarSummary();
        public List<HistogramBin> moisture = new List<HistogramBin>();
        public List<HistogramBin> aura = new List<HistogramBin>();
        public int auraAtCapCount;
        public float auraAtCapPercentage;
        public int spiritVeinPathReferenceCount;
        public int spiritVeinDistinctPathCellCount;
        public float spiritVeinPathDuplicateRate;
        public int spiritVeinInfluenceCellCount;
        public float spiritVeinInfluencePercentage;
    }

    public static class WorldMapStatisticsCalculator
    {
        private const int HistogramBinCount = 10;
        private const float AuraCapEpsilon = 0.000001f;

        public static WorldMapStatistics Calculate(WorldMap map)
        {
            if (map?.cells == null || map.cells.Length == 0)
                throw new ArgumentException("地图必须包含至少一个格子。", nameof(map));

            int total = map.cells.Length;
            WorldMapStatistics result = new WorldMapStatistics
            {
                height = Summarize(map.cells.Select(cell => cell.height)),
                moisture = BuildHistogram(map.cells.Select(cell => cell.moisture), total),
                aura = BuildHistogram(map.cells.Select(cell => cell.totalAura), total)
            };

            foreach (LandformType type in Enum.GetValues(typeof(LandformType)))
            {
                int count = map.cells.Count(cell => cell.landform == type);
                result.landforms.Add(new LandformStatistic
                {
                    type = type,
                    count = count,
                    percentage = Percentage(count, total)
                });
            }
            foreach (BiomeType type in Enum.GetValues(typeof(BiomeType)))
            {
                int count = map.cells.Count(cell => cell.biome == type);
                result.biomes.Add(new BiomeStatistic
                {
                    type = type,
                    count = count,
                    percentage = Percentage(count, total)
                });
            }

            result.auraAtCapCount = map.cells.Count(cell => cell.totalAura >= 1f - AuraCapEpsilon);
            result.auraAtCapPercentage = Percentage(result.auraAtCapCount, total);
            CalculateSpiritVeinCoverage(map, result);
            return result;
        }

        private static ScalarSummary Summarize(IEnumerable<float> source)
        {
            float[] values = source.OrderBy(value => value).ToArray();
            int middle = values.Length / 2;
            return new ScalarSummary
            {
                min = values[0],
                median = values.Length % 2 == 0
                    ? (values[middle - 1] + values[middle]) * 0.5f
                    : values[middle],
                max = values[values.Length - 1]
            };
        }

        private static List<HistogramBin> BuildHistogram(IEnumerable<float> source, int total)
        {
            int[] counts = new int[HistogramBinCount];
            foreach (float value in source)
            {
                float normalized = Math.Max(0f, Math.Min(1f, value));
                int index = Math.Min(HistogramBinCount - 1, (int)(normalized * HistogramBinCount));
                counts[index]++;
            }

            List<HistogramBin> bins = new List<HistogramBin>(HistogramBinCount);
            for (int index = 0; index < HistogramBinCount; index++)
            {
                bins.Add(new HistogramBin
                {
                    lowerInclusive = index / (float)HistogramBinCount,
                    upperExclusive = (index + 1) / (float)HistogramBinCount,
                    count = counts[index],
                    percentage = Percentage(counts[index], total)
                });
            }
            return bins;
        }

        private static void CalculateSpiritVeinCoverage(WorldMap map, WorldMapStatistics result)
        {
            HashSet<int> distinctPathCells = new HashSet<int>();
            HashSet<int> influenceCells = new HashSet<int>();
            foreach (SpiritVein vein in map.spiritVeins ?? new List<SpiritVein>())
            {
                if (vein?.pathCellIndices == null) continue;
                foreach (int index in vein.pathCellIndices)
                {
                    result.spiritVeinPathReferenceCount++;
                    if (index >= 0 && index < map.cells.Length) distinctPathCells.Add(index);
                }

                Queue<int> queue = new Queue<int>();
                Dictionary<int, int> distance = new Dictionary<int, int>();
                foreach (int index in vein.pathCellIndices.Distinct())
                {
                    if (index < 0 || index >= map.cells.Length || distance.ContainsKey(index)) continue;
                    distance[index] = 0;
                    queue.Enqueue(index);
                }
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    influenceCells.Add(current);
                    int currentDistance = distance[current];
                    if (currentDistance >= vein.influenceRadius) continue;
                    foreach (int neighbor in map.GetNeighborIndices(current))
                    {
                        if (distance.ContainsKey(neighbor)) continue;
                        distance[neighbor] = currentDistance + 1;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            result.spiritVeinDistinctPathCellCount = distinctPathCells.Count;
            result.spiritVeinPathDuplicateRate = result.spiritVeinPathReferenceCount == 0
                ? 0f
                : 1f - distinctPathCells.Count / (float)result.spiritVeinPathReferenceCount;
            result.spiritVeinInfluenceCellCount = influenceCells.Count;
            result.spiritVeinInfluencePercentage = Percentage(influenceCells.Count, map.cells.Length);
        }

        private static float Percentage(int count, int total) =>
            total <= 0 ? 0f : count * 100f / total;
    }
}
