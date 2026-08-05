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
                TerrainGenerator.Generate(map, SeedDerivation.Derive(effectiveSeed, "terrain"),
                    snapshot.terrain, snapshot.climate);
                RiverGenerator.Generate(map, snapshot.rivers, snapshot.climate.riverMoistureBoost);
                SpiritVeinGenerator.Generate(map, SeedDerivation.Derive(effectiveSeed, "spirit-veins"),
                    snapshot.spiritVeins);
                SpiritCalculator.Calculate(map);
                WorldMapRegionRules.Assign(map);
                ExplorationRegionMapper.Assign(map);
                if (map.cells.Any(cell => cell.isBuildable)) return map;
            }
            throw new InvalidOperationException("连续四次生成均未找到合法洞府选址。");
        }

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
        public static void Generate(WorldMap map, int seed, TerrainGenerationParameters terrain,
            ClimateGenerationParameters climate)
        {
            foreach (WorldCell cell in map.cells)
            {
                float x = cell.coord.col / (float)(map.width - 1);
                float y = cell.coord.row / (float)(map.height - 1);
                float warpX = Noise.Fractal(x * 2.8f + 13.7f, y * 2.8f - 4.2f, seed, 3) - 0.5f;
                float warpY = Noise.Fractal(x * 2.8f - 7.1f, y * 2.8f + 8.9f, seed ^ 0x45d9f3b, 3) - 0.5f;
                float nx = x + warpX * 0.14f;
                float ny = y + warpY * 0.14f;
                float broad = Noise.Fractal(nx * 5.2f, ny * 5.2f, seed ^ 0x632be59b, 5);
                float ridged = 1f - Math.Abs(Noise.Fractal(nx * 9.5f, ny * 9.5f, seed ^ 0x1b873593, 4) * 2f - 1f);
                float localDetail = Noise.Fractal(nx * 14f, ny * 14f, seed ^ 0x4cf5ad2, 3) - 0.5f;
                float dx = (x - 0.5f) / 0.72f;
                float dy = (y - 0.5f) / 0.66f;
                float edge = Math.Max(0f, 1f - (float)Math.Sqrt(dx * dx + dy * dy));
                cell.height = Clamp01(broad * 0.50f + ridged * 0.18f + localDetail * 0.18f +
                                      edge * 0.40f - 0.13f);
            }
            foreach (WorldCell cell in map.cells)
            {
                if (cell.height < terrain.deepWaterThreshold) cell.landform = LandformType.DeepWater;
                else if (cell.height < terrain.seaLevel) cell.landform = LandformType.ShallowWater;
                else if (cell.height < terrain.plainUpperThreshold) cell.landform = LandformType.Plain;
                else if (cell.height < terrain.hillUpperThreshold) cell.landform = LandformType.Hill;
                else cell.landform = LandformType.Mountain;
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
                cell.moisture = Clamp01(moistureNoise * climate.moistureNoiseStrength +
                                        coastalMoisture * climate.waterProximityMoistureStrength);
                ClassifyBiome(cell);
            }
        }

        internal static void ClassifyBiome(WorldCell cell)
        {
            if (cell.landform == LandformType.DeepWater || cell.landform == LandformType.ShallowWater)
                cell.biome = BiomeType.Ocean;
            else if (cell.landform == LandformType.Coast) cell.biome = BiomeType.Coast;
            else if (cell.landform == LandformType.Mountain)
                cell.biome = cell.temperature < 0.38f ? BiomeType.Snowfield : BiomeType.Alpine;
            else if (cell.temperature < 0.17f) cell.biome = BiomeType.Snowfield;
            else if (cell.temperature < 0.32f) cell.biome = BiomeType.Tundra;
            else if (cell.moisture < 0.22f) cell.biome = BiomeType.Desert;
            else if (cell.moisture > 0.78f && cell.landform == LandformType.Plain) cell.biome = BiomeType.Wetland;
            else if (cell.moisture > 0.66f && cell.temperature > 0.68f) cell.biome = BiomeType.Rainforest;
            else if (cell.moisture > 0.48f) cell.biome = BiomeType.TemperateForest;
            else cell.biome = BiomeType.Grassland;
            cell.isBuildable = cell.landform != LandformType.DeepWater &&
                               cell.landform != LandformType.ShallowWater &&
                               cell.landform != LandformType.Mountain &&
                               cell.biome != BiomeType.Wetland &&
                               cell.biome != BiomeType.Snowfield;
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

            float[] flow = Enumerable.Repeat(1f, count).ToArray();
            float[] upstreamMaximumHeight = map.cells.Select(cell => cell.height).ToArray();
            for (int i = order.Count - 1; i >= 0; i--)
            {
                int cell = order[i];
                if (parent[cell] < 0) continue;
                flow[parent[cell]] += flow[cell];
                upstreamMaximumHeight[parent[cell]] = Math.Max(
                    upstreamMaximumHeight[parent[cell]], upstreamMaximumHeight[cell]);
            }

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

            foreach (int index in rivers.SelectMany(segment =>
                new[] { segment.fromCellIndex, segment.toCellIndex }).Distinct())
            {
                WorldCell cell = map.cells[index];
                if (IsWater(cell)) continue;
                cell.moisture = Math.Min(1f, cell.moisture + riverMoistureBoost);
                TerrainGenerator.ClassifyBiome(cell);
            }
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
