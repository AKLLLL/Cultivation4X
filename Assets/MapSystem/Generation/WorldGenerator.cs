using System;
using System.Collections.Generic;
using System.Linq;

namespace Cultivation4X.WorldMap
{
    public static class MapGenerationSettingsValidator
    {
        public static List<string> Validate(MapGenerationSettings settings)
        {
            List<string> errors = new List<string>();
            if (settings == null)
            {
                errors.Add("生成参数不能为空。");
                return errors;
            }

            if (settings.width < 8 || settings.height < 8)
                errors.Add("地图宽高必须至少为 8。");
            if (settings.terrain == null) errors.Add("地貌参数不能为空。");
            if (settings.climate == null) errors.Add("气候参数不能为空。");
            if (settings.rivers == null) errors.Add("河流参数不能为空。");
            if (settings.spiritVeins == null) errors.Add("灵脉参数不能为空。");

            TerrainGenerationParameters terrain = settings.terrain;
            if (terrain != null)
            {
                ValidateNormalized(errors, "深水上限", terrain.deepWaterThreshold);
                ValidateNormalized(errors, "海平面", terrain.seaLevel);
                ValidateNormalized(errors, "平原上限", terrain.plainUpperThreshold);
                ValidateNormalized(errors, "丘陵上限", terrain.hillUpperThreshold);
                if (!(terrain.deepWaterThreshold < terrain.seaLevel &&
                      terrain.seaLevel < terrain.plainUpperThreshold &&
                      terrain.plainUpperThreshold < terrain.hillUpperThreshold))
                    errors.Add("地貌阈值必须满足：深水上限 < 海平面 < 平原上限 < 丘陵上限。");
            }

            RiverGenerationParameters rivers = settings.rivers;
            if (rivers != null)
            {
                long cellCount = settings.width > 0 && settings.height > 0
                    ? (long)settings.width * settings.height
                    : 0;
                if (float.IsNaN(rivers.minimumAccumulatedFlow) ||
                    float.IsInfinity(rivers.minimumAccumulatedFlow) ||
                    rivers.minimumAccumulatedFlow < 1f ||
                    rivers.minimumAccumulatedFlow > cellCount)
                    errors.Add($"最小汇水量必须位于 1–{cellCount}。");
                ValidateNormalized(errors, "最低上游源头高度", rivers.minimumSourceHeight);
                if (rivers.minimumBranchLength < 1 || rivers.minimumBranchLength > cellCount)
                    errors.Add($"最短支流长度必须位于 1–{cellCount}。");
            }

            ClimateGenerationParameters climate = settings.climate;
            if (climate != null)
            {
                ValidateNormalized(errors, "纬度降温强度", climate.latitudeCoolingStrength);
                ValidateNormalized(errors, "温度噪声强度", climate.temperatureNoiseStrength);
                ValidateNormalized(errors, "海拔降温强度", climate.elevationCoolingStrength);
                ValidateNormalized(errors, "湿度噪声强度", climate.moistureNoiseStrength);
                ValidateNormalized(errors, "距水增湿强度", climate.waterProximityMoistureStrength);
                ValidateNormalized(errors, "河流增湿强度", climate.riverMoistureBoost);
            }

            SpiritVeinGenerationParameters veins = settings.spiritVeins;
            if (veins != null)
            {
                long cellCount = settings.width > 0 && settings.height > 0
                    ? (long)settings.width * settings.height
                    : 0;
                int maximumLength = (int)Math.Min(int.MaxValue, cellCount);
                ValidateRange(errors, "大型灵脉数量", veins.largeCount, 0, maximumLength);
                ValidateRange(errors, "中型灵脉数量", veins.mediumCount, 0, maximumLength);
                ValidateRange(errors, "大型灵脉长度", veins.largeLength, 1, maximumLength);
                ValidateRange(errors, "中型灵脉长度", veins.mediumLength, 1, maximumLength);
                ValidateRange(errors, "大型灵脉半径", veins.largeRadius, 0, int.MaxValue);
                ValidateRange(errors, "中型灵脉半径", veins.mediumRadius, 0, int.MaxValue);
                if (veins.largeCount != null && veins.mediumCount != null &&
                    (long)veins.largeCount.max + veins.mediumCount.max > cellCount)
                    errors.Add("大型与中型灵脉的最大数量之和不得超过地图格数。");
            }
            return errors;
        }

        private static void ValidateNormalized(List<string> errors, string label, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 1f)
                errors.Add(label + "必须位于 0–1。");
        }

        private static void ValidateRange(List<string> errors, string label, InclusiveIntRange range, int lower, int upper)
        {
            if (range == null)
            {
                errors.Add(label + "区间不能为空。");
                return;
            }
            if (range.min > range.max)
                errors.Add(label + "必须满足最小值不大于最大值。");
            if (range.min < lower || range.max > upper)
                errors.Add($"{label}必须位于 {lower}–{upper}。");
        }
    }

    public static class WorldGenerator
    {
        public static WorldMap Generate(MapGenerationSettings settings)
        {
            MapGenerationSettings snapshot = settings?.Clone();
            List<string> errors = MapGenerationSettingsValidator.Validate(snapshot);
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors), nameof(settings));

            for (int attempt = 0; attempt < 4; attempt++)
            {
                int effectiveSeed = SeedDerivation.Derive(snapshot.seed, "attempt-" + attempt);
                WorldMap map = CreateEmptyMap(snapshot, effectiveSeed);
                int terrainSeed = SeedDerivation.Derive(effectiveSeed, "terrain");
                TerrainGenerator.Generate(map, terrainSeed, snapshot.terrain, snapshot.climate);
                RiverGenerator.Generate(map, snapshot.rivers, snapshot.climate.riverMoistureBoost);
                TerrainGenerator.RelaxMoistureField(map, 8);
                foreach (WorldCell cell in map.cells) TerrainGenerator.ClassifyBiome(cell);
                TerrainGenerator.LimitDesertCoverage(map, 0.04f, terrainSeed);
                WorldGenerationDiagnosticsStore.FinalizeMap(map);
                SpiritVeinGenerator.Generate(map, SeedDerivation.Derive(effectiveSeed, "spirit-veins"),
                    snapshot.spiritVeins);
                SpiritCalculator.Calculate(map);
                WorldMapRegionRules.Assign(map);
                // 方案 A 验证阶段：恢复严格候选规则；表现层垂直尺度已放大 2 倍。
                AssignMountainTerraces(map);
                ExplorationRegionMapper.Assign(map);
                if (map.cells.Any(cell => cell.isBuildable)) return map;
            }
            throw new InvalidOperationException("连续四次生成均未找到合法洞府选址。");
        }

        private static void AssignMountainTerraces(WorldMap map)
        {
            if (map?.cells == null || map.regions == null ||
                !WorldGenerationDiagnosticsStore.TryGet(map,
                    out WorldGenerationDiagnostics diagnostics) ||
                diagnostics.mountainRidgeCore.Length != map.cells.Length ||
                diagnostics.mountainPeaks.Length != map.cells.Length ||
                diagnostics.mountainPasses.Length != map.cells.Length ||
                diagnostics.terrainSlope.Length != map.cells.Length)
                return;

            TerrainGenerationParameters terrain = map.generationSettings?.terrain ??
                                                  new TerrainGenerationParameters();
            HashSet<int> mountainSet = new HashSet<int>(Enumerable.Range(0, map.cells.Length)
                .Where(index => map.cells[index] != null &&
                                map.cells[index].landform == LandformType.Mountain));
            foreach (List<int> cluster in CollectTerraceComponents(map, mountainSet))
            {
                if (cluster.Count == 0) continue;
                int area = cluster.Count;
                float baseRadius = (float)Math.Sqrt(area);
                float minimumHeight = cluster.Min(index => map.cells[index].height);
                float maximumHeight = cluster.Max(index => map.cells[index].height);
                float heightRange = maximumHeight - minimumHeight;
                float compactness = baseRadius <= 0f ? 0f : heightRange / baseRadius;
                int roll = (int)((uint)SeedDerivation.Derive(map.effectiveSeed,
                    "mountain-type-cluster-" + cluster[0]) % 100u);

                string mountainType;
                int maximumTerraceGroups;
                if (area < 25)
                {
                    mountainType = "小丘陵";
                    maximumTerraceGroups = 0;
                }
                else if (compactness >= 0.050f)
                {
                    mountainType = "陡峭峰脊";
                    maximumTerraceGroups = 0;
                }
                else if (compactness <= 0.035f && area >= 70 && roll < 70)
                {
                    mountainType = "宽厚台地";
                    maximumTerraceGroups = 5;
                }
                else if (roll < 20)
                {
                    mountainType = "陡峭峰脊";
                    maximumTerraceGroups = 0;
                }
                else
                {
                    mountainType = "普通";
                    maximumTerraceGroups = 1;
                }

                if (maximumTerraceGroups <= 0) continue;

                float averageHeight = cluster.Average(index => map.cells[index].height);
                float averageRow = (float)cluster.Average(index => map.cells[index].coord.row);
                HashSet<int> candidates = new HashSet<int>(cluster.Where(index =>
                {
                    WorldCell cell = map.cells[index];
                    return !diagnostics.mountainRidgeCore[index] &&
                           !diagnostics.mountainPeaks[index] &&
                           !diagnostics.mountainPasses[index] &&
                           diagnostics.terrainSlope[index] <= 0.12f &&
                           cell.coord.row <= averageRow + 1;
                }));
                if (candidates.Count < 2) continue;

                MapRegionData clusterRegion = map.regions?.FirstOrDefault(item =>
                    item != null && item.cellIndices != null && item.cellIndices.Contains(cluster[0]));
                List<List<int>> components = CollectTerraceComponents(map, candidates);
                foreach (List<int> component in components.Where(item => item.Count >= 2)
                    .OrderBy(item => item.Average(index => map.cells[index].coord.row))
                    .ThenBy(item => item.Average(index => diagnostics.terrainSlope[index]))
                    .ThenBy(item => item.Min(index => StableTerraceOrder(map, clusterRegion, index)))
                    .Take(maximumTerraceGroups))
                {
                    int seed = component.OrderBy(index => diagnostics.terrainSlope[index])
                        .ThenBy(index => Math.Abs(map.cells[index].height - averageHeight))
                        .ThenBy(index => StableTerraceOrder(map, clusterRegion, index))
                        .First();
                    int desiredSize = 2 + (int)((uint)StableTerraceOrder(map, clusterRegion, seed) % 4u);
                    List<int> terrace = GrowTerraceCluster(map, component, seed, desiredSize,
                        diagnostics.terrainSlope, averageHeight, clusterRegion);
                    if (terrace.Count < 2) continue;
                    foreach (int index in terrace) map.cells[index].isBuildable = true;
                }
            }
        }
        private static List<List<int>> CollectTerraceComponents(WorldMap map, HashSet<int> candidates)
        {
            var result = new List<List<int>>();
            var remaining = new HashSet<int>(candidates);
            while (remaining.Count > 0)
            {
                int start = remaining.Min();
                var component = new List<int>();
                var queue = new Queue<int>();
                remaining.Remove(start);
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    component.Add(current);
                    foreach (int neighbor in map.GetNeighborIndices(current))
                    {
                        if (!remaining.Remove(neighbor)) continue;
                        queue.Enqueue(neighbor);
                    }
                }
                component.Sort();
                result.Add(component);
            }
            return result;
        }

        private static List<int> GrowTerraceCluster(WorldMap map, List<int> component, int seed,
            int desiredSize, float[] slopes, float targetHeight, MapRegionData region)
        {
            var allowed = new HashSet<int>(component);
            var selected = new List<int> { seed };
            var selectedSet = new HashSet<int> { seed };
            while (selected.Count < desiredSize)
            {
                int next = selected.SelectMany(index => map.GetNeighborIndices(index))
                    .Where(index => allowed.Contains(index) && !selectedSet.Contains(index))
                    .Distinct()
                    .OrderBy(index => slopes[index])
                    .ThenBy(index => Math.Abs(map.cells[index].height - targetHeight))
                    .ThenBy(index => StableTerraceOrder(map, region, index))
                    .DefaultIfEmpty(-1).First();
                if (next < 0) break;
                selected.Add(next);
                selectedSet.Add(next);
            }
            selected.Sort();
            return selected;
        }

        private static int StableTerraceOrder(WorldMap map, MapRegionData region, int index) =>
            SeedDerivation.Derive(map.effectiveSeed,
                $"mountain-terrace-{region?.regionId ?? "cluster"}-{index}");

        private static WorldMap CreateEmptyMap(MapGenerationSettings settings, int effectiveSeed)
        {
            WorldMap map = new WorldMap
            {
                userSeed = settings.seed,
                effectiveSeed = effectiveSeed,
                generationVersion = settings.generationVersion,
                width = settings.width,
                height = settings.height,
                generationSettings = settings.Clone(),
                cells = new WorldCell[settings.width * settings.height]
            };
            for (int row = 0; row < settings.height; row++)
            for (int col = 0; col < settings.width; col++)
            {
                int index = row * settings.width + col;
                map.cells[index] = new WorldCell { index = index, coord = new HexCoord(col, row) };
            }
            return map;
        }
    }

    public static class TerrainGenerator
    {
        internal const float DesertMoistureThreshold = 0.18f;
        private static readonly float[] BiomeTemperatureBreaks =
            { 0.17f, 0.32f, 0.40f, 0.48f, 0.58f, 0.68f };
        private static readonly float[] BiomeMoistureBreaks =
            { 0.18f, 0.20f, 0.40f, 0.42f, 0.58f, 0.60f };
        private static readonly BiomeType[,] LandBiomeMatrix =
        {
            // moisture: <.18       <.20       <.40       <.42       <.58       <.60       >=.60
            { BiomeType.Snowfield,  BiomeType.Snowfield, BiomeType.Snowfield, BiomeType.Snowfield, BiomeType.Snowfield, BiomeType.Snowfield,       BiomeType.Snowfield },
            { BiomeType.Tundra,     BiomeType.Tundra,    BiomeType.Tundra,    BiomeType.Tundra,    BiomeType.Tundra,    BiomeType.TemperateForest, BiomeType.TemperateForest },
            { BiomeType.Tundra,     BiomeType.Tundra,    BiomeType.Tundra,    BiomeType.Tundra,    BiomeType.TemperateForest, BiomeType.TemperateForest, BiomeType.TemperateForest },
            { BiomeType.Grassland,  BiomeType.Grassland, BiomeType.Grassland, BiomeType.Grassland, BiomeType.TemperateForest, BiomeType.TemperateForest, BiomeType.TemperateForest },
            { BiomeType.Grassland,  BiomeType.Grassland, BiomeType.Grassland, BiomeType.TemperateForest, BiomeType.TemperateForest, BiomeType.TemperateForest, BiomeType.TemperateForest },
            { BiomeType.Desert,     BiomeType.Grassland, BiomeType.Grassland, BiomeType.TemperateForest, BiomeType.TemperateForest, BiomeType.TemperateForest, BiomeType.TemperateForest },
            { BiomeType.Desert,     BiomeType.Desert,    BiomeType.Grassland, BiomeType.TemperateForest, BiomeType.TemperateForest, BiomeType.TemperateForest, BiomeType.Rainforest }
        };

        public static void Generate(WorldMap map, int seed, TerrainGenerationParameters terrain,
            ClimateGenerationParameters climate)
        {
            float[] baseHeights = new float[map.cells.Length];
            float[] mountainPotential = new float[map.cells.Length];
            foreach (WorldCell cell in map.cells)
            {
                float x = cell.coord.col / (float)(map.width - 1);
                float y = cell.coord.row / (float)(map.height - 1);
                float warpX = Noise.Fractal(x * 2.8f + 13.7f, y * 2.8f - 4.2f, seed, 3) - 0.5f;
                float warpY = Noise.Fractal(x * 2.8f - 7.1f, y * 2.8f + 8.9f, seed ^ 0x45d9f3b, 3) - 0.5f;
                float nx = x + warpX * 0.14f;
                float ny = y + warpY * 0.14f;
                float broad = Noise.Fractal(nx * 5.2f, ny * 5.2f, seed ^ 0x632be59b, 5);
                float ridged = 1f - Math.Abs(Noise.Fractal(nx * 9.5f, ny * 9.5f,
                    seed ^ 0x1b873593, 4) * 2f - 1f);
                float localDetail = Noise.Fractal(nx * 14f, ny * 14f, seed ^ 0x4cf5ad2, 3) - 0.5f;
                float dx = (x - 0.5f) / 0.72f;
                float dy = (y - 0.5f) / 0.66f;
                float edge = Math.Max(0f, 1f - (float)Math.Sqrt(dx * dx + dy * dy));
                // The continental field deliberately stays broad and quiet. Mountains are added
                // later from an explicit peak/ridge skeleton instead of being hidden in noise.
                cell.height = Clamp01(broad * 0.50f + ridged * 0.18f + localDetail * 0.18f +
                                      edge * 0.40f - 0.13f);
                baseHeights[cell.index] = cell.height;

                float province = Noise.Fractal(nx * 3.1f + 17.3f, ny * 3.1f - 9.1f,
                    seed ^ 0x6d2b79f5, 4);
                float ridgeGuide = 1f - Math.Abs(Noise.Fractal(nx * 7.2f - 3.7f, ny * 7.2f + 11.9f,
                    seed ^ 0x27d4eb2d, 3) * 2f - 1f);
                mountainPotential[cell.index] = Clamp01(province * 0.62f + ridgeGuide * 0.30f +
                    Math.Max(0f, cell.height - terrain.seaLevel) * 0.45f);
            }

            MountainField mountainField = BuildMountainField(map, seed, terrain, baseHeights,
                mountainPotential);
            foreach (WorldCell cell in map.cells)
            {
                if (cell.height < terrain.deepWaterThreshold) cell.landform = LandformType.DeepWater;
                else if (cell.height < terrain.seaLevel) cell.landform = LandformType.ShallowWater;
                else if (mountainField.mountain[cell.index] && !mountainField.valley[cell.index] &&
                         cell.height >= terrain.hillUpperThreshold)
                    cell.landform = LandformType.Mountain;
                else if (cell.height >= terrain.plainUpperThreshold ||
                         mountainField.influence[cell.index] >= 0.08f)
                    cell.landform = LandformType.Hill;
                else cell.landform = LandformType.Plain;
            }
            foreach (WorldCell cell in map.cells)
            {
                if (cell.landform != LandformType.Plain) continue;
                if (map.GetNeighborIndices(cell.index).Any(index =>
                    map.cells[index].landform == LandformType.DeepWater ||
                    map.cells[index].landform == LandformType.ShallowWater))
                    cell.landform = LandformType.Coast;
            }
            int[] waterDistance = MapDistanceFields.WaterDistance(map);
            foreach (WorldCell cell in map.cells)
            {
                float latitude = Math.Abs(cell.coord.row / (float)(map.height - 1) * 2f - 1f);
                float temperatureNoise = Noise.Fractal(cell.coord.col * 0.085f, cell.coord.row * 0.085f,
                    seed ^ 0x27d4eb2d, 3) - 0.5f;
                cell.temperature = Clamp01(1f - latitude * climate.latitudeCoolingStrength +
                                           temperatureNoise * climate.temperatureNoiseStrength -
                                           Math.Max(0f, cell.height - 0.55f) * climate.elevationCoolingStrength);
                float moistureNoise = Noise.Fractal(cell.coord.col * 0.095f, cell.coord.row * 0.095f,
                    seed ^ 0x165667b1, 4);
                float coastalMoisture = 1f - Math.Min(waterDistance[cell.index], 6) / 6f;
                float continentalDryness = Clamp01((waterDistance[cell.index] - 4f) / 20f);
                float aridityNoise = Noise.Fractal(cell.coord.col * 0.028f, cell.coord.row * 0.028f,
                    seed ^ 0x7f4a7c15, 3);
                float broadAridity = Clamp01((aridityNoise - 0.46f) / 0.32f);
                // 距水纵深必须形成可感知的气候梯度；中尺度噪声只负责打散边界，
                // 不能让深内陆仅凭一个高噪声采样就比湖岸更湿。
                float noiseMoisture = 0.14f +
                                      moistureNoise * climate.moistureNoiseStrength * 0.68f;
                cell.moisture = Clamp01(noiseMoisture +
                                        coastalMoisture * climate.waterProximityMoistureStrength -
                                        continentalDryness * 0.13f - broadAridity * 0.06f);
            }
            ApplyEastToWestRainShadow(map);
            foreach (WorldCell cell in map.cells) ClassifyBiome(cell);
        }

        private sealed class MountainField
        {
            public readonly bool[] ridgeCore;
            public readonly bool[] peak;
            public readonly bool[] mountain;
            public readonly bool[] valley;
            public readonly float[] ridgeStrength;
            public readonly float[] influence;

            public MountainField(int count)
            {
                ridgeCore = new bool[count];
                peak = new bool[count];
                mountain = new bool[count];
                valley = new bool[count];
                ridgeStrength = new float[count];
                influence = new float[count];
            }
        }

        /// <summary>
        /// 先选取成片的山系候选区，再在每个候选区内选峰、用最小生成树连接山脊，
        /// 最后从峰脊向外衰减形成山体。所有中间结果都是生成期数组，WorldCell.height
        /// 仍是气候、排水和表现层共用的唯一逻辑高度。
        /// </summary>
        private static MountainField BuildMountainField(WorldMap map, int seed,
            TerrainGenerationParameters terrain, float[] baseHeights, float[] potential)
        {
            int count = map.cells.Length;
            MountainField result = new MountainField(count);
            bool[] candidate = new bool[count];
            for (int index = 0; index < count; index++)
                candidate[index] = baseHeights[index] >= terrain.seaLevel + 0.025f &&
                                   potential[index] >= 0.50f;

            int maximumSystems = Math.Max(1, Math.Min(4, count / 2800));
            List<List<int>> provinces = ConnectedComponents(map, candidate)
                .Where(component => component.Count >= 18)
                .OrderByDescending(component => ProvinceScore(component, potential))
                .ThenBy(component => component[0])
                .Take(maximumSystems)
                .ToList();

            // Sparse seeds still need at least one coherent range. Use the strongest inland area
            // as a fallback province, never a post-hoc quota of disconnected Mountain cells.
            if (provinces.Count == 0)
            {
                int fallbackCenter = Enumerable.Range(0, count)
                    .Where(index => baseHeights[index] >= terrain.seaLevel + 0.04f)
                    .OrderByDescending(index => potential[index])
                    .ThenBy(index => index)
                    .FirstOrDefault();
                List<int> fallback = Enumerable.Range(0, count)
                    .Where(index => baseHeights[index] >= terrain.seaLevel + 0.02f &&
                                    HexCoord.Distance(map.cells[index].coord,
                                        map.cells[fallbackCenter].coord) <= 14)
                    .ToList();
                if (fallback.Count >= 2) provinces.Add(fallback);
            }

            float[] ridgeTarget = new float[count];
            List<int> peakIndices = new List<int>();
            foreach (List<int> province in provinces)
            {
                HashSet<int> provinceSet = new HashSet<int>(province);
                List<int> peaks = SelectPeaks(map, province, potential, baseHeights);
                if (peaks.Count < 2) continue;
                peakIndices.AddRange(peaks);
                foreach (int peak in peaks)
                {
                    result.ridgeCore[peak] = true;
                    result.peak[peak] = true;
                    ridgeTarget[peak] = Math.Max(ridgeTarget[peak],
                        0.87f + potential[peak] * 0.10f);
                }

                foreach (Tuple<int, int> edge in MinimumSpanningTree(map, peaks, potential))
                {
                    List<int> path = TraceRidgePath(map, edge.Item1, edge.Item2, provinceSet,
                        baseHeights, potential, terrain.seaLevel, seed);
                    for (int position = 0; position < path.Count; position++)
                    {
                        int index = path[position];
                        result.ridgeCore[index] = true;
                        float along = path.Count <= 1 ? 0f : position / (float)(path.Count - 1);
                        float endBlend = Math.Abs(along * 2f - 1f);
                        float target = 0.72f + potential[index] * 0.09f + endBlend * 0.025f;
                        ridgeTarget[index] = Math.Max(ridgeTarget[index], target);
                    }

                    // One controlled saddle per connection creates a readable pass without
                    // globally smoothing the ridge into a rubber sheet.
                    if (path.Count >= 7)
                    {
                        int middle = path.Count / 2;
                        for (int offset = -1; offset <= 1; offset++)
                        {
                            int position = middle + offset;
                            if (position <= 0 || position >= path.Count - 1) continue;
                            int index = path[position];
                            result.valley[index] = true;
                            ridgeTarget[index] = Math.Min(ridgeTarget[index],
                                terrain.hillUpperThreshold - 0.012f + Math.Abs(offset) * 0.012f);
                        }
                    }
                }
            }

            List<int> ridgeSources = Enumerable.Range(0, count)
                .Where(index => result.ridgeCore[index])
                .ToList();
            foreach (int source in ridgeSources) result.ridgeStrength[source] = ridgeTarget[source];
            HashSet<int> peakSet = new HashSet<int>(peakIndices);
            for (int index = 0; index < count; index++)
            {
                if (baseHeights[index] < terrain.seaLevel) continue;
                float raised = baseHeights[index];
                foreach (int source in ridgeSources)
                {
                    int distance = HexCoord.Distance(map.cells[index].coord, map.cells[source].coord);
                    // 非对称山体：前坡(+Z，相机朝向)加宽、后坡收紧，用更多格子承担前坡高度。
                    float zDelta = (map.cells[index].coord.row - map.cells[source].coord.row) * 1.5f;
                    float zRatio = distance <= 0 ? 0f :
                        Math.Max(-1f, Math.Min(1f, zDelta / (distance * 1.5f)));
                    // 相机位于 -Z、看向 +Z：row 较小的一侧才是玩家看到的前坡。
                    float frontness = -zRatio * 0.5f + 0.5f;
                    int backRadius = peakSet.Contains(source) ? 4 : 3;
                    int frontRadius = peakSet.Contains(source) ? 8 : 6;
                    float radius = backRadius + (frontRadius - backRadius) * frontness;
                    if (distance > radius) continue;
                    float t = 1f - distance / radius;
                    float exponent = 2.40f + (1.35f - 2.40f) * frontness;
                    float falloff = (float)Math.Pow(t, exponent);
                    float candidateHeight = baseHeights[index] +
                                            (ridgeTarget[source] - baseHeights[index]) * falloff;
                    raised = Math.Max(raised, candidateHeight);
                }
                result.influence[index] = Math.Max(0f, raised - baseHeights[index]);
                map.cells[index].height = Clamp01(raised);
            }

            foreach (int index in Enumerable.Range(0, count).Where(index => result.valley[index]))
            {
                map.cells[index].height = Math.Max(baseHeights[index],
                    Math.Min(map.cells[index].height, terrain.hillUpperThreshold - 0.012f));
            }
            // 前坡方向的距离：只向 +Z 方向扩展，限制前坡最多 3 环。
            int[] frontRidgeDistance = Enumerable.Repeat(int.MaxValue, count).ToArray();
            Queue<int> frontQueue = new Queue<int>(ridgeSources);
            foreach (int source in ridgeSources) frontRidgeDistance[source] = 0;
            while (frontQueue.Count > 0)
            {
                int current = frontQueue.Dequeue();
                foreach (int neighbor in map.GetNeighborIndices(current))
                {
                    if (map.cells[neighbor].coord.row > map.cells[current].coord.row) continue;
                    if (frontRidgeDistance[neighbor] <= frontRidgeDistance[current] + 1) continue;
                    frontRidgeDistance[neighbor] = frontRidgeDistance[current] + 1;
                    frontQueue.Enqueue(neighbor);
                }
            }
            for (int index = 0; index < count; index++)
            {
                if (result.valley[index]) continue;
                bool touchesSpine = result.ridgeCore[index] ||
                                    map.GetNeighborIndices(index).Any(neighbor => result.ridgeCore[neighbor]);
                bool withinFrontReach = frontRidgeDistance[index] <= 2;
                result.mountain[index] = (touchesSpine || withinFrontReach) &&
                                         map.cells[index].height >= terrain.hillUpperThreshold;
            }
            ApplySteppedMountainLayers(map, result, terrain);
            float[] slopes = new float[count];
            for (int index = 0; index < count; index++)
                slopes[index] = map.GetNeighborIndices(index)
                    .Select(neighbor => Math.Abs(map.cells[index].height - map.cells[neighbor].height))
                    .DefaultIfEmpty(0f)
                    .Max();
            WorldGenerationDiagnosticsStore.RecordMountainField(map, result.ridgeCore, result.peak,
                result.valley, result.ridgeStrength, result.influence, slopes);
            return result;
        }

        /// <summary>
        /// 把每座连续山体从连续陡升改成 3～5 个高度层：
        /// 层内保留约 22% 的原坡度（接近平台），层与层之间自然形成短崖。
        /// 峰/脊/山口保持原始高度，保证轮廓仍在。
        /// </summary>
        private static void ApplySteppedMountainLayers(WorldMap map, MountainField mountainField,
            TerrainGenerationParameters terrain)
        {
            if (map?.cells == null || mountainField?.mountain == null) return;
            foreach (List<int> component in ConnectedComponents(map, mountainField.mountain))
            {
                if (component.Count < 8) continue;
                float minimumHeight = component.Min(index => map.cells[index].height);
                float maximumHeight = component.Max(index => map.cells[index].height);
                float heightRange = maximumHeight - minimumHeight;
                if (heightRange < 0.10f) continue;

                int layerCount = component.Count >= 200 ? 5 :
                    component.Count >= 80 ? 4 : 3;
                float[] edges = new float[layerCount + 1];
                for (int layer = 0; layer <= layerCount; layer++)
                    edges[layer] = minimumHeight + heightRange * layer / layerCount;
                foreach (int index in component)
                {
                    if (mountainField.peak[index] || mountainField.valley[index]) continue;
                    float original = map.cells[index].height;
                    int layer = (int)((original - minimumHeight) / Math.Max(0.0001f, heightRange) *
                                      layerCount);
                    layer = Math.Max(0, Math.Min(layerCount - 1, layer));
                    map.cells[index].height = edges[layer];
                }
            }
        }

        private static List<List<int>> ConnectedComponents(WorldMap map, bool[] included)
        {
            bool[] visited = new bool[included.Length];
            List<List<int>> result = new List<List<int>>();
            for (int start = 0; start < included.Length; start++)
            {
                if (!included[start] || visited[start]) continue;
                List<int> component = new List<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(start);
                visited[start] = true;
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    component.Add(current);
                    foreach (int neighbor in map.GetNeighborIndices(current))
                    {
                        if (!included[neighbor] || visited[neighbor]) continue;
                        visited[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }
                component.Sort();
                result.Add(component);
            }
            return result;
        }

        private static float ProvinceScore(List<int> component, float[] potential)
        {
            float maximum = component.Max(index => potential[index]);
            return maximum + Math.Min(component.Count, 240) / 240f;
        }

        private static List<int> SelectPeaks(WorldMap map, List<int> province, float[] potential,
            float[] baseHeights)
        {
            HashSet<int> members = new HashSet<int>(province);
            List<int> candidates = province
                .Where(index => map.GetNeighborIndices(index)
                    .Where(members.Contains)
                    .All(neighbor => potential[index] >= potential[neighbor]))
                .OrderByDescending(index => potential[index] + baseHeights[index] * 0.18f)
                .ThenBy(index => index)
                .ToList();
            if (candidates.Count < 2)
                candidates = province.OrderByDescending(index => potential[index] + baseHeights[index] * 0.18f)
                    .ThenBy(index => index).ToList();

            int maximumPeaks = Math.Min(6, Math.Max(2, province.Count / 52));
            List<int> peaks = new List<int>();
            foreach (int candidate in candidates)
            {
                if (peaks.Any(peak => HexCoord.Distance(map.cells[peak].coord,
                    map.cells[candidate].coord) < 7)) continue;
                peaks.Add(candidate);
                if (peaks.Count >= maximumPeaks) break;
            }
            if (peaks.Count == 1)
            {
                int second = province.OrderByDescending(index =>
                        HexCoord.Distance(map.cells[peaks[0]].coord, map.cells[index].coord) * 2f +
                        potential[index])
                    .ThenBy(index => index).First();
                if (second != peaks[0]) peaks.Add(second);
            }
            return peaks;
        }

        private static List<Tuple<int, int>> MinimumSpanningTree(WorldMap map, List<int> peaks,
            float[] potential)
        {
            List<Tuple<int, int>> edges = new List<Tuple<int, int>>();
            HashSet<int> connected = new HashSet<int> { peaks[0] };
            while (connected.Count < peaks.Count)
            {
                int bestFrom = -1;
                int bestTo = -1;
                float bestWeight = float.MaxValue;
                foreach (int from in connected.OrderBy(index => index))
                foreach (int to in peaks.Where(index => !connected.Contains(index)).OrderBy(index => index))
                {
                    float weight = HexCoord.Distance(map.cells[from].coord, map.cells[to].coord) -
                                   (potential[from] + potential[to]) * 0.75f;
                    if (weight >= bestWeight) continue;
                    bestWeight = weight;
                    bestFrom = from;
                    bestTo = to;
                }
                if (bestTo < 0) break;
                edges.Add(Tuple.Create(bestFrom, bestTo));
                connected.Add(bestTo);
            }
            return edges;
        }

        private static List<int> TraceRidgePath(WorldMap map, int start, int target,
            HashSet<int> province, float[] baseHeights, float[] potential, float seaLevel, int seed)
        {
            int[] parent = Enumerable.Repeat(-1, map.cells.Length).ToArray();
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(start);
            parent[start] = start;
            while (queue.Count > 0 && parent[target] < 0)
            {
                int current = queue.Dequeue();
                foreach (int next in map.GetNeighborIndices(current)
                    .Where(index => parent[index] < 0 && province.Contains(index) &&
                                    baseHeights[index] >= seaLevel)
                    .OrderByDescending(index => (province.Contains(index) ? 1.2f : 0f) +
                                                potential[index] * 0.8f + StableJitter(seed, index))
                    .ThenBy(index => index))
                {
                    parent[next] = current;
                    queue.Enqueue(next);
                }
            }

            if (parent[target] < 0) return new List<int> { start };
            List<int> path = new List<int>();
            for (int current = target;; current = parent[current])
            {
                path.Add(current);
                if (current == start) break;
            }
            path.Reverse();
            return path;
        }

        private static float StableJitter(int seed, int index)
        {
            unchecked
            {
                uint value = (uint)(seed ^ index * 0x45d9f3b);
                value ^= value >> 16;
                value *= 0x7feb352d;
                value ^= value >> 15;
                return (value & 1023u) / 1023f * 0.035f;
            }
        }

        /// <summary>
        /// 保留旧入口供现有测试与工具调用；默认盛行风仍从东向西。
        /// 实际计算使用风向投影排序、海洋蒸发、水汽输送与地形降雨。
        /// </summary>
        internal static void ApplyEastToWestRainShadow(WorldMap map)
        {
            ApplyWindDrivenClimate(map, -1f, 0f);
        }

        /// <summary>
        /// 沿风向投影建立无环处理顺序。每格只读取已经处理的上风邻格，
        /// 海洋补充水汽，陆地按空气含湿量、海拔和迎风坡抬升凝结降雨。
        /// 思路参考 mapgen4 humidity.ts 的风向排序与地形降雨模型。
        /// </summary>
        internal static void ApplyWindDrivenClimate(WorldMap map, float windX, float windY)
        {
            if (map?.cells == null || map.cells.Length == 0) return;
            float windLength = (float)Math.Sqrt(windX * windX + windY * windY);
            if (windLength < 0.0001f)
            {
                windX = -1f;
                windY = 0f;
            }
            else
            {
                windX /= windLength;
                windY /= windLength;
            }

            const float boundaryAirMoisture = 0.48f;
            const float minimumOceanAirMoisture = 0.92f;
            const float landTransportLoss = 0.008f;
            float[] projection = new float[map.cells.Length];
            float[] transportedMoisture = new float[map.cells.Length];
            float[] rainfall = new float[map.cells.Length];
            foreach (WorldCell cell in map.cells)
            {
                float x = cell.coord.col + ((cell.coord.row & 1) == 0 ? 0f : 0.5f);
                float y = cell.coord.row * 0.8660254f;
                projection[cell.index] = x * windX + y * windY;
            }

            WorldCell[] windOrder = map.cells
                .OrderBy(cell => projection[cell.index])
                .ThenBy(cell => cell.index)
                .ToArray();
            foreach (WorldCell cell in windOrder)
            {
                float weightedMoisture = 0f;
                float weightedHeight = 0f;
                float totalWeight = 0f;
                foreach (int neighborIndex in map.GetNeighborIndices(cell.index))
                {
                    float projectionStep = projection[cell.index] - projection[neighborIndex];
                    if (projectionStep <= 0.00001f) continue;
                    // 更正对风向的邻格权重；侧上风邻格仍可扩散水汽，避免单行通道。
                    float weight = projectionStep * projectionStep;
                    weightedMoisture += transportedMoisture[neighborIndex] * weight;
                    weightedHeight += map.cells[neighborIndex].height * weight;
                    totalWeight += weight;
                }

                float carried = totalWeight > 0f
                    ? weightedMoisture / totalWeight
                    : boundaryAirMoisture;
                float meanUpwindHeight = totalWeight > 0f
                    ? weightedHeight / totalWeight
                    : cell.height;

                bool water = cell.landform == LandformType.DeepWater ||
                             cell.landform == LandformType.ShallowWater;
                if (water)
                {
                    cell.moisture = Math.Max(cell.moisture, 0.90f);
                    float waterDepth = Math.Max(0f, 0.43f - cell.height) / 0.43f;
                    float evaporation = 0.24f + waterDepth * 0.18f;
                    transportedMoisture[cell.index] = Math.Min(1.20f,
                        Math.Max(minimumOceanAirMoisture, carried + evaporation));
                    continue;
                }

                float normalizedElevation = Clamp01((cell.height - 0.43f) / 0.57f);
                float rise = Math.Max(0f, cell.height - meanUpwindHeight);
                float saturationCapacity = 0.82f - normalizedElevation * 0.45f;
                float advectiveRain = carried * 0.012f;
                float saturationRain = Math.Max(0f, carried - saturationCapacity) * 0.72f;
                float terrainRain = rise * carried * 0.90f;
                float localRainfall = Math.Min(0.35f,
                    advectiveRain + saturationRain + terrainRain);
                rainfall[cell.index] = localRainfall;
                transportedMoisture[cell.index] = Math.Max(0f,
                    Math.Min(1.20f, carried - localRainfall - landTransportLoss));

                // 原噪声湿度作为土壤底色；输送水汽和实际降雨决定大尺度气候。
                // Explicit high ridges create stronger, longer rain shadows than the former
                // scattered mountain labels. A small soil baseline keeps their lee side
                // semi-arid instead of letting one range turn a third of the continent barren.
                cell.moisture = Clamp01(cell.moisture * 0.54f +
                    Math.Min(1f, carried) * 0.31f + localRainfall * 0.85f + 0.012f);
            }
            WorldGenerationDiagnosticsStore.RecordClimate(map, rainfall, transportedMoisture);
        }

        /// <summary>
        /// 在真实六邻域内做少量受限松弛，消除单格宽水汽通道和局部湿度断崖。
        /// 山地边界降低交换权重，避免把迎风/背风差异整体抹平。
        /// </summary>
        internal static void RelaxMoistureField(WorldMap map, int iterations)
        {
            if (map?.cells == null || map.cells.Length == 0 || iterations <= 0) return;
            const float relaxationStrength = 0.39f;
            const float maximumChangePerIteration = 0.04f;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                float[] current = map.cells.Select(cell => cell.moisture).ToArray();
                float[] next = (float[])current.Clone();
                foreach (WorldCell cell in map.cells)
                {
                    bool water = cell.landform == LandformType.DeepWater ||
                                 cell.landform == LandformType.ShallowWater;
                    if (water) continue;

                    float weightedSum = 0f;
                    float totalWeight = 0f;
                    foreach (int neighborIndex in map.GetNeighborIndices(cell.index))
                    {
                        WorldCell neighbor = map.cells[neighborIndex];
                        if (neighbor.landform == LandformType.DeepWater ||
                            neighbor.landform == LandformType.ShallowWater) continue;
                        float weight = cell.landform == LandformType.Mountain ||
                                       neighbor.landform == LandformType.Mountain
                            ? 0.25f
                            : 1f;
                        weightedSum += current[neighborIndex] * weight;
                        totalWeight += weight;
                    }
                    if (totalWeight <= 0f) continue;

                    float neighborMean = weightedSum / totalWeight;
                    float desiredChange = (neighborMean - current[cell.index]) * relaxationStrength;
                    float limitedChange = Math.Max(-maximumChangePerIteration,
                        Math.Min(maximumChangePerIteration, desiredChange));
                    next[cell.index] = Clamp01(current[cell.index] + limitedChange);
                }

                for (int i = 0; i < map.cells.Length; i++) map.cells[i].moisture = next[i];
            }
        }

        /// <summary>
        /// 限制极端干燥种子的沙漠面积。保留顺序以低频大陆干旱为主、最终湿度为辅，
        /// 避免面积上限反而只留下贴山的雨影最低点；被收缩部分作为半干旱草原表现。
        /// </summary>
        internal static void LimitDesertCoverage(WorldMap map, float maximumLandFraction, int terrainSeed)
        {
            if (map?.cells == null || map.cells.Length == 0 || maximumLandFraction <= 0f) return;
            int landCount = map.cells.Count(cell => cell.landform != LandformType.DeepWater &&
                                                   cell.landform != LandformType.ShallowWater);
            int maximumDeserts = Math.Max(1, (int)Math.Floor(landCount * maximumLandFraction));
            int[] waterDistance = MapDistanceFields.WaterDistance(map);
            int[] mountainDistance = MapDistanceFields.MountainDistance(map);
            var desertCandidates = map.cells
                .Where(cell => cell.biome == BiomeType.Desert)
                .Select(cell => new
                {
                    Cell = cell,
                    Priority = DesertClimatePriority(cell, waterDistance[cell.index], terrainSeed),
                    MountainDistance = mountainDistance[cell.index],
                    WaterDistance = waterDistance[cell.index]
                })
                .ToList();

            // Rain shadow alone should create dry grassland, not automatically a desert.
            // Require a continental or independent low-frequency aridity signal for the
            // high-contrast desert biome. Two complete non-desert rings are required between
            // mountains and desert, while the stronger mountain rain shadow now requires a
            // deeper inland buffer before it may become a high-contrast desert.
            const float minimumDesertClimatePriority = 0.16f;
            foreach (var candidate in desertCandidates)
                ClassifyRejectedDesert(candidate.Cell);

            var eligible = desertCandidates
                .Where(candidate => candidate.MountainDistance >= 4 &&
                                    candidate.WaterDistance >= 8)
                .OrderByDescending(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.Cell.index)
                .ToList();
            var retained = eligible
                .Where(candidate => candidate.Priority >= minimumDesertClimatePriority)
                .Take(maximumDeserts)
                .ToList();
            foreach (var candidate in retained.Take(maximumDeserts))
            {
                candidate.Cell.biome = BiomeType.Desert;
                candidate.Cell.isBuildable = candidate.Cell.landform != LandformType.Mountain;
            }
        }

        private static float DesertClimatePriority(WorldCell cell, int waterDistance, int terrainSeed)
        {
            float continentalDryness = Clamp01((waterDistance - 4f) / 20f);
            float aridityNoise = Noise.Fractal(cell.coord.col * 0.028f, cell.coord.row * 0.028f,
                terrainSeed ^ 0x7f4a7c15, 3);
            float broadAridity = Clamp01((aridityNoise - 0.46f) / 0.32f);
            float moistureDeficit = Clamp01((DesertMoistureThreshold - cell.moisture) /
                                             DesertMoistureThreshold);
            return broadAridity * 0.42f + continentalDryness * 0.43f + moistureDeficit * 0.15f;
        }

        internal static void ClassifyBiome(WorldCell cell)
        {
            if (cell.landform == LandformType.DeepWater || cell.landform == LandformType.ShallowWater)
                cell.biome = BiomeType.Ocean;
            else if (cell.landform == LandformType.Coast) cell.biome = BiomeType.Coast;
            else if (cell.landform == LandformType.Mountain)
                cell.biome = cell.temperature < 0.38f ? BiomeType.Snowfield : BiomeType.Alpine;
            else if (cell.landform == LandformType.Plain &&
                     cell.temperature >= 0.36f &&
                     cell.moisture >= 0.70f &&
                     cell.height <= 0.60f)
                cell.biome = BiomeType.Wetland;
            else
                cell.biome = LandBiomeMatrix[
                    FindBiomeBand(cell.temperature, BiomeTemperatureBreaks),
                    FindBiomeBand(cell.moisture, BiomeMoistureBreaks)];
            cell.isBuildable = cell.landform != LandformType.DeepWater &&
                               cell.landform != LandformType.ShallowWater &&
                               cell.landform != LandformType.Mountain &&
                               cell.biome != BiomeType.Wetland &&
                               cell.biome != BiomeType.Snowfield;
        }

        private static int FindBiomeBand(float value, float[] breaks)
        {
            for (int index = 0; index < breaks.Length; index++)
                if (value < breaks[index]) return index;
            return breaks.Length;
        }

        private static void ClassifyRejectedDesert(WorldCell cell)
        {
            // 沙漠上限淘汰的是高对比荒漠外观，不代表所有格子都应回退为温暖草原。
            // 较冷的半干旱区保留为苔原；其余作为草原/稀树草原语义处理。
            cell.biome = cell.temperature < 0.40f
                ? BiomeType.Tundra
                : BiomeType.Grassland;
            cell.isBuildable = cell.landform != LandformType.Mountain;
        }
        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }

    // Odd-r hex reimplementation informed by mapgen4's priority drainage and reverse
    // flow accumulation. Source: https://github.com/redblobgames/mapgen4
    // commit c1d8cb018a11a8b9e17d59233c36c176429d37eb, Apache-2.0.
    public static class RiverGenerator
    {
        public static void Generate(WorldMap map, RiverGenerationParameters parameters, float riverMoistureBoost)
        {
            int count = map.cells.Length;
            int[] parent = Enumerable.Repeat(-1, count).ToArray();
            float[] filledHeight = Enumerable.Repeat(float.PositiveInfinity, count).ToArray();
            bool[] visited = new bool[count];
            MinHeap heap = new MinHeap();
            foreach (WorldCell cell in map.cells)
            {
                if (!IsWater(cell)) continue;
                filledHeight[cell.index] = cell.height;
                heap.Push(cell.index, cell.height);
            }
            if (heap.Count == 0)
            {
                map.rivers.Clear();
                return;
            }

            List<int> order = new List<int>(count);
            while (heap.Count > 0)
            {
                int current = heap.Pop();
                if (visited[current]) continue;
                visited[current] = true;
                order.Add(current);
                foreach (int neighbor in map.GetNeighborIndices(current))
                {
                    if (visited[neighbor] || filledHeight[neighbor] < float.PositiveInfinity) continue;
                    parent[neighbor] = current;
                    filledHeight[neighbor] = Math.Max(map.cells[neighbor].height,
                        filledHeight[current] + 0.000001f);
                    heap.Push(neighbor, filledHeight[neighbor]);
                }
            }

            float[] runoffInput = BuildRainfallRunoff(map);
            float[] flow = (float[])runoffInput.Clone();
            float[] upstreamMaximumHeight = map.cells.Select(cell => cell.height).ToArray();
            for (int i = order.Count - 1; i >= 0; i--)
            {
                int cell = order[i];
                if (parent[cell] < 0) continue;
                flow[parent[cell]] += flow[cell];
                upstreamMaximumHeight[parent[cell]] = Math.Max(
                    upstreamMaximumHeight[parent[cell]], upstreamMaximumHeight[cell]);
            }
            WorldGenerationDiagnosticsStore.RecordDrainage(map, parent, filledHeight,
                runoffInput, flow);

            bool[] channel = new bool[count];
            int[] channelChildren = new int[count];
            for (int index = 0; index < count; index++)
            {
                int next = parent[index];
                if (next < 0 || IsWater(map.cells[index]) ||
                    flow[index] < parameters.minimumAccumulatedFlow)
                    continue;
                channel[index] = true;
                channelChildren[next]++;
            }

            foreach (int head in Enumerable.Range(0, count)
                .Where(index => channel[index] && channelChildren[index] == 0).ToArray())
            {
                List<int> branch = new List<int>();
                int current = head;
                while (current >= 0 && channel[current])
                {
                    branch.Add(current);
                    int next = parent[current];
                    if (next < 0 || IsWater(map.cells[next]) || channelChildren[next] > 1) break;
                    current = next;
                }
                if (upstreamMaximumHeight[head] >= parameters.minimumSourceHeight &&
                    branch.Count >= parameters.minimumBranchLength)
                    continue;
                foreach (int index in branch) channel[index] = false;
            }

            // A strong ridge network can split every threshold channel into several short head
            // branches. If branch pruning removes all of them, preserve the best complete main
            // stem from high catchment to water instead of returning a riverless valid world.
            if (!channel.Any(value => value))
                RestoreBestMainStem(map, parameters, parent, flow, upstreamMaximumHeight, channel);

            List<RiverSegment> rivers = new List<RiverSegment>();
            for (int index = 0; index < count; index++)
            {
                int next = parent[index];
                if (next < 0 || !channel[index]) continue;
                int mouth = FindMouth(map, parent, index);
                rivers.Add(new RiverSegment
                {
                    riverId = $"river_{map.effectiveSeed:x8}_{mouth:00000}",
                    fromCellIndex = index,
                    toCellIndex = next,
                    edgeDirection = map.GetDirection(index, next),
                    flow = flow[index]
                });
            }
            map.rivers = rivers.OrderBy(segment => segment.fromCellIndex).ToList();

            int[] riverDistance = Enumerable.Repeat(-1, count).ToArray();
            Queue<int> moistureQueue = new Queue<int>();
            foreach (int index in rivers.SelectMany(segment =>
                         new[] { segment.fromCellIndex, segment.toCellIndex }).Distinct())
            {
                if (IsWater(map.cells[index])) continue;
                riverDistance[index] = 0;
                moistureQueue.Enqueue(index);
            }
            while (moistureQueue.Count > 0)
            {
                int current = moistureQueue.Dequeue();
                if (riverDistance[current] >= 2) continue;
                foreach (int neighbor in map.GetNeighborIndices(current))
                {
                    if (riverDistance[neighbor] >= 0 || IsWater(map.cells[neighbor])) continue;
                    riverDistance[neighbor] = riverDistance[current] + 1;
                    moistureQueue.Enqueue(neighbor);
                }
            }
            for (int index = 0; index < count; index++)
            {
                float boostFactor;
                switch (riverDistance[index])
                {
                    case 0: boostFactor = 0.625f; break;
                    case 1: boostFactor = 0.3125f; break;
                    case 2: boostFactor = 0.125f; break;
                    default: continue;
                }
                WorldCell cell = map.cells[index];
                cell.moisture = Math.Min(1f, cell.moisture + riverMoistureBoost * boostFactor);
                TerrainGenerator.ClassifyBiome(cell);
            }
        }

        private static void RestoreBestMainStem(WorldMap map, RiverGenerationParameters parameters,
            int[] parent, float[] flow, float[] upstreamMaximumHeight, bool[] channel)
        {
            List<int> bestPath = null;
            float bestScore = float.MinValue;
            for (int start = 0; start < map.cells.Length; start++)
            {
                if (IsWater(map.cells[start]) || parent[start] < 0 ||
                    flow[start] < parameters.minimumAccumulatedFlow ||
                    upstreamMaximumHeight[start] < parameters.minimumSourceHeight) continue;
                List<int> path = new List<int>();
                HashSet<int> visited = new HashSet<int>();
                int current = start;
                while (current >= 0 && !IsWater(map.cells[current]) && visited.Add(current))
                {
                    path.Add(current);
                    current = parent[current];
                }
                if (path.Count < Math.Max(4, parameters.minimumBranchLength / 2)) continue;
                float score = path.Count * 1000f + upstreamMaximumHeight[start] * 10f + flow[start];
                if (score <= bestScore) continue;
                bestScore = score;
                bestPath = path;
            }
            if (bestPath == null) return;
            foreach (int index in bestPath) channel[index] = true;
        }

        private static float[] BuildRainfallRunoff(WorldMap map)
        {
            int count = map.cells.Length;
            float[] runoff = new float[count];
            if (!WorldGenerationDiagnosticsStore.TryGet(map,
                    out WorldGenerationDiagnostics diagnostics) ||
                diagnostics.rainfall.Length != count)
                return runoff;

            // 以实际降雨量作为初始流量，并按陆地平均降雨归一化为 1。
            // 这样保留 minimumAccumulatedFlow 既有的“平均汇水格数”量级，
            // 同时让湿润流域贡献更多、干旱流域贡献更少。
            float meanRainfall = map.cells
                .Where(cell => !IsWater(cell))
                .Select(cell => Math.Max(0f, diagnostics.rainfall[cell.index]))
                .DefaultIfEmpty(0f)
                .Average();
            if (meanRainfall <= 0.00000001f) return runoff;

            foreach (WorldCell cell in map.cells)
            {
                if (IsWater(cell)) continue;
                float rain = Math.Max(0f, diagnostics.rainfall[cell.index]);
                // 保留少量广布基流，避免低雨量格把连续排水树截成孤立短段；
                // 其余 65% 初始流量仍由本格降雨相对值决定。
                runoff[cell.index] = 2f * (0.35f + 0.65f * rain / meanRainfall);
            }
            return runoff;
        }

        private static int FindMouth(WorldMap map, int[] parent, int start)
        {
            int current = start;
            for (int step = 0; step < parent.Length; step++)
            {
                int next = parent[current];
                if (next < 0) return current;
                if (IsWater(map.cells[next])) return next;
                current = next;
            }
            throw new InvalidOperationException("河流排水树出现无效循环。");
        }

        private static bool IsWater(WorldCell cell) =>
            cell.landform == LandformType.DeepWater || cell.landform == LandformType.ShallowWater;

        private sealed class MinHeap
        {
            private readonly List<Node> nodes = new List<Node>();
            public int Count => nodes.Count;

            public void Push(int index, float priority)
            {
                nodes.Add(new Node { index = index, priority = priority });
                int child = nodes.Count - 1;
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (Compare(nodes[parent], nodes[child]) <= 0) break;
                    Node swap = nodes[parent];
                    nodes[parent] = nodes[child];
                    nodes[child] = swap;
                    child = parent;
                }
            }

            public int Pop()
            {
                Node result = nodes[0];
                int last = nodes.Count - 1;
                nodes[0] = nodes[last];
                nodes.RemoveAt(last);
                int parent = 0;
                while (parent < nodes.Count)
                {
                    int left = parent * 2 + 1;
                    if (left >= nodes.Count) break;
                    int right = left + 1;
                    int child = right < nodes.Count && Compare(nodes[right], nodes[left]) < 0 ? right : left;
                    if (Compare(nodes[parent], nodes[child]) <= 0) break;
                    Node swap = nodes[parent];
                    nodes[parent] = nodes[child];
                    nodes[child] = swap;
                    parent = child;
                }
                return result.index;
            }

            private static int Compare(Node left, Node right)
            {
                int priority = left.priority.CompareTo(right.priority);
                return priority != 0 ? priority : left.index.CompareTo(right.index);
            }

            private struct Node
            {
                public int index;
                public float priority;
            }
        }
    }

    public static class SpiritVeinGenerator
    {
        public static void Generate(WorldMap map, int seed, SpiritVeinGenerationParameters parameters)
        {
            DeterministicRandom random = new DeterministicRandom(seed);
            int largeCount = random.NextInclusive(parameters.largeCount);
            int mediumCount = random.NextInclusive(parameters.mediumCount);
            int total = largeCount + mediumCount;
            List<SpiritElement> elements = new List<SpiritElement>();
            for (int i = 0; i < total; i++) elements.Add((SpiritElement)(i % 5));
            random.Shuffle(elements);
            map.spiritVeins = new List<SpiritVein>(total);
            for (int i = 0; i < total; i++)
            {
                bool large = i < largeCount;
                int desiredLength = large
                    ? random.NextInclusive(parameters.largeLength)
                    : random.NextInclusive(parameters.mediumLength);
                SpiritVein vein = new SpiritVein
                {
                    id = $"vein_{map.effectiveSeed:x8}_{i:000}",
                    size = large ? SpiritVeinSize.Large : SpiritVeinSize.Medium,
                    primaryElement = elements[i],
                    strength = large ? 0.34f : 0.22f,
                    influenceRadius = large
                        ? random.NextInclusive(parameters.largeRadius)
                        : random.NextInclusive(parameters.mediumRadius)
                };
                int current = random.Next(0, map.cells.Length);
                int direction = random.Next(0, 6);
                while (vein.pathCellIndices.Count < desiredLength)
                {
                    vein.pathCellIndices.Add(current);
                    List<int> directions = new List<int>
                    {
                        direction, direction, direction, direction - 1, direction + 1,
                        direction - 2, direction + 2, direction + 3
                    };
                    random.Shuffle(directions);
                    int next = -1;
                    foreach (int candidateDirection in directions)
                    {
                        int candidate = map.GetIndex(map.GetNeighbor(map.cells[current].coord, candidateDirection));
                        if (candidate < 0) continue;
                        next = candidate;
                        direction = ((candidateDirection % 6) + 6) % 6;
                        break;
                    }
                    if (next < 0) { direction = (direction + 3) % 6; continue; }
                    current = next;
                }
                map.spiritVeins.Add(vein);
            }
        }
    }

    public static class SpiritCalculator
    {
        public static void Calculate(WorldMap map)
        {
            foreach (WorldCell cell in map.cells)
            {
                cell.elementalAura = new ElementalAura();
                cell.baseAura = BaseAura(cell);
                float share = cell.baseAura / 5f;
                cell.elementalAura.metal = share; cell.elementalAura.wood = share;
                cell.elementalAura.water = share; cell.elementalAura.fire = share; cell.elementalAura.earth = share;
            }
            foreach (SpiritVein vein in map.spiritVeins)
            {
                Queue<int> queue = new Queue<int>();
                Dictionary<int, int> distance = new Dictionary<int, int>();
                foreach (int index in vein.pathCellIndices.Distinct()) { distance[index] = 0; queue.Enqueue(index); }
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    int currentDistance = distance[current];
                    if (currentDistance >= vein.influenceRadius) continue;
                    foreach (int neighbor in map.GetNeighborIndices(current))
                    {
                        if (distance.ContainsKey(neighbor)) continue;
                        distance[neighbor] = currentDistance + 1;
                        queue.Enqueue(neighbor);
                    }
                }
                foreach (KeyValuePair<int, int> item in distance)
                {
                    float normalized = item.Value / (float)(vein.influenceRadius + 1);
                    map.cells[item.Key].elementalAura.Add(vein.primaryElement,
                        vein.strength * (1f - normalized) * (1f - normalized));
                }
            }
            foreach (WorldCell cell in map.cells)
            {
                float total = cell.elementalAura.Total;
                if (total > 1f) { cell.elementalAura.Scale(1f / total); total = 1f; }
                cell.totalAura = total;
            }
        }
        private static float BaseAura(WorldCell cell)
        {
            switch (cell.landform)
            {
                case LandformType.Mountain: return 0.12f;
                case LandformType.Hill: return 0.09f;
                case LandformType.DeepWater: return 0.07f;
                case LandformType.ShallowWater: return 0.08f;
                case LandformType.Coast: return 0.06f;
                default: return 0.05f;
            }
        }
    }

    public static class ExplorationRegionMapper
    {
        public static void Assign(WorldMap map)
        {
            map.pointsOfInterest = new List<WorldPointOfInterest>
            {
                new WorldPointOfInterest { id = "qingyun_outskirts", cellIndex = Pick(map, cell =>
                    cell.isBuildable && (cell.landform == LandformType.Plain || cell.landform == LandformType.Hill)) },
                new WorldPointOfInterest { id = "mistwood", cellIndex = Pick(map, cell =>
                    cell.isBuildable && (cell.biome == BiomeType.TemperateForest || cell.biome == BiomeType.Rainforest)) },
                new WorldPointOfInterest { id = "chixia_ridge", cellIndex = Pick(map, cell =>
                    cell.isBuildable && cell.landform == LandformType.Hill) }
            };
        }
        private static int Pick(WorldMap map, Func<WorldCell, bool> predicate)
        {
            WorldCell candidate = map.cells.Where(predicate)
                .OrderBy(cell => SeedDerivation.Derive(map.effectiveSeed, cell.index.ToString())).FirstOrDefault();
            return candidate?.index ?? map.cells.FirstOrDefault(cell => cell.isBuildable)?.index ?? -1;
        }
    }

    internal static class SeedDerivation
    {
        public static int Derive(int seed, string label)
        {
            unchecked
            {
                uint hash = 2166136261u ^ (uint)seed;
                foreach (char character in label) { hash ^= character; hash *= 16777619u; }
                hash ^= hash >> 16; hash *= 0x7feb352du; hash ^= hash >> 15;
                return (int)hash;
            }
        }
    }

    internal static class MapDistanceFields
    {
        public static int[] WaterDistance(WorldMap map)
        {
            int[] distance = Enumerable.Repeat(int.MaxValue, map.cells.Length).ToArray();
            Queue<int> queue = new Queue<int>();
            foreach (WorldCell cell in map.cells)
            {
                if (cell.landform != LandformType.DeepWater && cell.landform != LandformType.ShallowWater) continue;
                distance[cell.index] = 0; queue.Enqueue(cell.index);
            }
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int neighbor in map.GetNeighborIndices(current))
                {
                    if (distance[neighbor] <= distance[current] + 1) continue;
                    distance[neighbor] = distance[current] + 1; queue.Enqueue(neighbor);
                }
            }
            return distance;
        }

        public static int[] MountainDistance(WorldMap map)
        {
            int[] distance = Enumerable.Repeat(int.MaxValue, map.cells.Length).ToArray();
            Queue<int> queue = new Queue<int>();
            foreach (WorldCell cell in map.cells)
            {
                if (cell.landform != LandformType.Mountain) continue;
                distance[cell.index] = 0;
                queue.Enqueue(cell.index);
            }
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
    }

    internal sealed class DeterministicRandom
    {
        private uint state;
        public DeterministicRandom(int seed) => state = (uint)seed == 0 ? 0x6d2b79f5u : (uint)seed;
        private uint NextUInt()
        {
            uint value = state; value ^= value << 13; value ^= value >> 17; value ^= value << 5;
            return state = value;
        }
        public int Next(int min, int max) => min + (int)(NextUInt() % (uint)(max - min));
        public int NextInclusive(InclusiveIntRange range)
        {
            if (range.min == range.max) return range.min;
            return Next(range.min, range.max + 1);
        }
        public float NextFloat() => (NextUInt() & 0x00ffffff) / 16777216f;
        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int swap = Next(0, i + 1); T value = list[i]; list[i] = list[swap]; list[swap] = value;
            }
        }
    }

    internal static class Noise
    {
        public static float Fractal(float x, float y, int seed, int octaves)
        {
            float value = 0f, amplitude = 0.5f, total = 0f;
            for (int octave = 0; octave < octaves; octave++)
            {
                value += Value(x, y, seed + octave * 1013) * amplitude;
                total += amplitude; x *= 2f; y *= 2f; amplitude *= 0.5f;
            }
            return value / total;
        }
        private static float Value(float x, float y, int seed)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed);
            float c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
            return Lerp(Lerp(a, b, fx), Lerp(c, d, fx), fy);
        }
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint value = (uint)(x * 374761393 + y * 668265263 + seed * 69069);
                value = (value ^ (value >> 13)) * 1274126177u;
                return ((value ^ (value >> 16)) & 0x00ffffff) / 16777215f;
            }
        }
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
