using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 仅供生成器测试与 TerrainTest 调试视图使用的运行期中间量。
    /// 不挂在 WorldMap/WorldCell 上，因此不会进入存档，也不改变玩法数据结构。
    /// </summary>
    internal sealed class WorldGenerationDiagnostics
    {
        public float[] rainfall = Array.Empty<float>();
        public float[] transportedMoisture = Array.Empty<float>();
        public int[] drainageParent = Array.Empty<int>();
        public float[] filledHeight = Array.Empty<float>();
        public float[] runoffInput = Array.Empty<float>();
        public float[] accumulatedFlow = Array.Empty<float>();
        public int[] freshWaterDistance = Array.Empty<int>();
        public float[] finalMoisture = Array.Empty<float>();
        public bool[] mountainRidgeCore = Array.Empty<bool>();
        public bool[] mountainPeaks = Array.Empty<bool>();
        public bool[] mountainPasses = Array.Empty<bool>();
        public float[] mountainRidgeStrength = Array.Empty<float>();
        public float[] mountainInfluence = Array.Empty<float>();
        public float[] terrainSlope = Array.Empty<float>();
        public float maximumAccumulatedFlow;
        public int maximumFiniteFreshWaterDistance;
    }

    internal static class WorldGenerationDiagnosticsStore
    {
        private static readonly ConditionalWeakTable<WorldMap, WorldGenerationDiagnostics> ByMap =
            new ConditionalWeakTable<WorldMap, WorldGenerationDiagnostics>();

        public static WorldGenerationDiagnostics GetOrCreate(WorldMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return ByMap.GetValue(map, _ => new WorldGenerationDiagnostics());
        }

        public static bool TryGet(WorldMap map, out WorldGenerationDiagnostics diagnostics)
        {
            diagnostics = null;
            return map != null && ByMap.TryGetValue(map, out diagnostics);
        }

        public static void RecordClimate(WorldMap map, float[] rainfall, float[] transportedMoisture)
        {
            WorldGenerationDiagnostics diagnostics = GetOrCreate(map);
            diagnostics.rainfall = CloneSized(rainfall, map.cells.Length);
            diagnostics.transportedMoisture = CloneSized(transportedMoisture, map.cells.Length);
        }

        public static void RecordDrainage(WorldMap map, int[] parent, float[] filledHeight,
            float[] runoffInput, float[] flow)
        {
            WorldGenerationDiagnostics diagnostics = GetOrCreate(map);
            diagnostics.drainageParent = CloneSized(parent, map.cells.Length);
            diagnostics.filledHeight = CloneSized(filledHeight, map.cells.Length);
            diagnostics.runoffInput = CloneSized(runoffInput, map.cells.Length);
            diagnostics.accumulatedFlow = CloneSized(flow, map.cells.Length);
            diagnostics.maximumAccumulatedFlow = diagnostics.accumulatedFlow.Length == 0
                ? 0f
                : diagnostics.accumulatedFlow.Max();
        }

        public static void RecordMountainField(WorldMap map, bool[] ridgeCore, bool[] peaks,
            bool[] passes, float[] ridgeStrength, float[] influence, float[] slope)
        {
            WorldGenerationDiagnostics diagnostics = GetOrCreate(map);
            diagnostics.mountainRidgeCore = CloneSized(ridgeCore, map.cells.Length);
            diagnostics.mountainPeaks = CloneSized(peaks, map.cells.Length);
            diagnostics.mountainPasses = CloneSized(passes, map.cells.Length);
            diagnostics.mountainRidgeStrength = CloneSized(ridgeStrength, map.cells.Length);
            diagnostics.mountainInfluence = CloneSized(influence, map.cells.Length);
            diagnostics.terrainSlope = CloneSized(slope, map.cells.Length);
        }

        public static void FinalizeMap(WorldMap map)
        {
            WorldGenerationDiagnostics diagnostics = GetOrCreate(map);
            diagnostics.finalMoisture = map.cells.Select(cell => cell.moisture).ToArray();
            diagnostics.freshWaterDistance = BuildFreshWaterDistance(map);
            diagnostics.maximumFiniteFreshWaterDistance = diagnostics.freshWaterDistance
                .Where(distance => distance != int.MaxValue)
                .DefaultIfEmpty(0)
                .Max();
        }

        private static int[] BuildFreshWaterDistance(WorldMap map)
        {
            int count = map.cells.Length;
            bool[] oceanConnected = new bool[count];
            Queue<int> oceanQueue = new Queue<int>();
            foreach (WorldCell cell in map.cells)
            {
                if (!IsWater(cell) || !IsMapBoundary(map, cell.coord)) continue;
                oceanConnected[cell.index] = true;
                oceanQueue.Enqueue(cell.index);
            }
            while (oceanQueue.Count > 0)
            {
                int current = oceanQueue.Dequeue();
                foreach (int neighbor in map.GetNeighborIndices(current))
                {
                    if (oceanConnected[neighbor] || !IsWater(map.cells[neighbor])) continue;
                    oceanConnected[neighbor] = true;
                    oceanQueue.Enqueue(neighbor);
                }
            }

            int[] distance = Enumerable.Repeat(int.MaxValue, count).ToArray();
            Queue<int> queue = new Queue<int>();
            void AddSource(int index)
            {
                if (index < 0 || index >= count || distance[index] == 0) return;
                distance[index] = 0;
                queue.Enqueue(index);
            }

            foreach (RiverSegment segment in map.rivers ?? new List<RiverSegment>())
            {
                if (segment == null) continue;
                if (segment.fromCellIndex < 0 || segment.fromCellIndex >= count ||
                    segment.toCellIndex < 0 || segment.toCellIndex >= count) continue;
                AddSource(segment.fromCellIndex);
                if (!IsWater(map.cells[segment.toCellIndex])) AddSource(segment.toCellIndex);
            }
            foreach (WorldCell cell in map.cells)
                if (IsWater(cell) && !oceanConnected[cell.index]) AddSource(cell.index);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int neighbor in map.GetNeighborIndices(current))
                {
                    if (distance[neighbor] <= distance[current] + 1) continue;
                    distance[neighbor] = distance[current] + 1;
                    queue.Enqueue(neighbor);
                }
            }
            return distance;
        }

        private static bool IsMapBoundary(WorldMap map, HexCoord coord) =>
            coord.col == 0 || coord.row == 0 || coord.col == map.width - 1 || coord.row == map.height - 1;

        private static bool IsWater(WorldCell cell) => cell != null &&
            (cell.landform == LandformType.DeepWater || cell.landform == LandformType.ShallowWater);

        private static float[] CloneSized(float[] source, int count) =>
            source != null && source.Length == count ? (float[])source.Clone() : new float[count];

        private static int[] CloneSized(int[] source, int count) =>
            source != null && source.Length == count ? (int[])source.Clone() : new int[count];

        private static bool[] CloneSized(bool[] source, int count) =>
            source != null && source.Length == count ? (bool[])source.Clone() : new bool[count];
    }

    internal sealed class WorldGenerationBenchmarkSnapshot
    {
        public int userSeed;
        public int effectiveSeed;
        public int cellCount;
        public int landCount;
        public int riverSegmentCount;
        public int riverSourceCount;
        public float meanTemperature;
        public float meanMoisture;
        public float meanRainfall;
        public float meanLandFreshWaterDistance;
        public float maximumFlow;
        public Dictionary<BiomeType, int> biomeCounts = new Dictionary<BiomeType, int>();
    }

    internal static class WorldGenerationBenchmark
    {
        public static readonly int[] DefaultSeeds =
        {
            48621, 20260806, 1486022881, 7171, 8181, 9191,
            104729, 271828, 314159, 8675309, 1357911, 2468022
        };

        public static WorldGenerationBenchmarkSnapshot Capture(WorldMap map)
        {
            if (map?.cells == null || map.cells.Length == 0)
                throw new ArgumentException("地图必须包含至少一个格子。", nameof(map));
            if (!WorldGenerationDiagnosticsStore.TryGet(map, out WorldGenerationDiagnostics diagnostics))
                throw new InvalidOperationException("地图缺少生成诊断数据。");

            WorldCell[] land = map.cells.Where(cell => !IsWater(cell)).ToArray();
            HashSet<int> downstreamCells = new HashSet<int>((map.rivers ?? new List<RiverSegment>())
                .Where(segment => segment != null)
                .Select(segment => segment.toCellIndex));
            int[] finiteLandFreshWaterDistances = land
                .Select(cell => diagnostics.freshWaterDistance[cell.index])
                .Where(distance => distance != int.MaxValue)
                .ToArray();
            WorldGenerationBenchmarkSnapshot snapshot = new WorldGenerationBenchmarkSnapshot
            {
                userSeed = map.userSeed,
                effectiveSeed = map.effectiveSeed,
                cellCount = map.cells.Length,
                landCount = land.Length,
                riverSegmentCount = map.rivers?.Count ?? 0,
                riverSourceCount = (map.rivers ?? new List<RiverSegment>()).Count(segment =>
                    segment != null && !downstreamCells.Contains(segment.fromCellIndex)),
                meanTemperature = map.cells.Average(cell => cell.temperature),
                meanMoisture = land.Length == 0 ? 0f : land.Average(cell => cell.moisture),
                meanRainfall = land.Length == 0 ? 0f : land.Average(cell => diagnostics.rainfall[cell.index]),
                meanLandFreshWaterDistance = finiteLandFreshWaterDistances.Length == 0
                    ? float.PositiveInfinity
                    : (float)finiteLandFreshWaterDistances.Average(),
                maximumFlow = diagnostics.maximumAccumulatedFlow
            };
            foreach (BiomeType biome in Enum.GetValues(typeof(BiomeType)))
                snapshot.biomeCounts[biome] = map.cells.Count(cell => cell.biome == biome);
            return snapshot;
        }

        private static bool IsWater(WorldCell cell) => cell != null &&
            (cell.landform == LandformType.DeepWater || cell.landform == LandformType.ShallowWater);
    }
}
