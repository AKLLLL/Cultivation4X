using System;
using System.Collections.Generic;
using System.Linq;

namespace Cultivation4X.WorldMap
{
    public sealed class MapRegionBuildResult
    {
        public List<MapRegionData> regions = new List<MapRegionData>();
        public string[] regionIds = Array.Empty<string>();
        public MapInternalPositionTag[] internalPositionTags = Array.Empty<MapInternalPositionTag>();
    }

    public static class WorldMapRegionRules
    {
        private const int OrdinaryTargetSize = 14;
        private const int OpenWaterTargetSize = 56;
        private const int MinimumRegionSize = 4;
        private const int OrdinaryMaximumSize = 32;
        private const int OpenWaterMaximumSize = 110;
        private const int SmallMountainComponentThreshold = 8;

        private static readonly string[] NamePrefixes =
        {
            "青", "苍", "玄", "赤", "白", "紫", "碧", "云", "雾", "霜", "星", "月", "日", "风", "雨", "雷",
            "灵", "幽", "明", "静", "长", "落", "朝", "暮", "玉", "金", "木", "水", "火", "土", "天", "地"
        };
        private static readonly string[] NameInfixes =
        {
            "华", "霞", "渊", "澜", "隐", "虚", "真", "清", "寂", "流", "归", "望", "栖", "鸣", "照", "凝",
            "扶", "凌", "沉", "浮", "丹", "翠", "素", "衡", "朔", "离", "坤", "乾", "太", "元", "妙", "道"
        };

        public static void Assign(WorldMap map)
        {
            MapRegionBuildResult result = Build(map);
            map.regions = result.regions;
            for (int index = 0; index < map.cells.Length; index++)
            {
                map.cells[index].regionId = result.regionIds[index];
                map.cells[index].internalPositionTag = result.internalPositionTags[index];
            }
        }

        public static MapRegionBuildResult Build(WorldMap map)
        {
            if (map?.cells == null) throw new ArgumentNullException(nameof(map));
            MapRegionType[] baseTypes = ClassifyCells(map);
            List<RegionSeed> seeds = SplitConnectedComponents(map, baseTypes);
            MergeSmallRegions(map, seeds);
            seeds = seeds.Where(seed => seed.cells.Count > 0)
                .OrderBy(seed => seed.cells.Min()).ToList();

            float mapAura = map.cells.Average(cell => cell.totalAura);
            float mapDanger = map.cells.Average(cell => (float)WorldMapProgressRules.GetDanger(cell));
            MapRegionBuildResult result = new MapRegionBuildResult
            {
                regionIds = new string[map.cells.Length],
                internalPositionTags = new MapInternalPositionTag[map.cells.Length]
            };
            HashSet<string> usedNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> usedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int order = 0; order < seeds.Count; order++)
            {
                RegionSeed seed = seeds[order];
                seed.cells.Sort();
                int center = SelectCenter(map, seed.cells);
                string id = UniqueRegionId(map.effectiveSeed, seed.type, center, usedIds);
                usedIds.Add(id);
                MapRegionData region = CreateRegion(map, seed.type, seed.cells, center, id, mapAura, mapDanger);
                region.regionName = UniqueName(map.effectiveSeed, seed.type, center, usedNames);
                usedNames.Add(region.regionName);
                result.regions.Add(region);
                foreach (int index in seed.cells) result.regionIds[index] = id;
            }
            HashSet<int> riverCells = RiverCells(map);
            HashSet<int> riverFromCells = new HashSet<int>((map.rivers ?? new List<RiverSegment>())
                .Where(segment => segment != null).Select(segment => segment.fromCellIndex));
            HashSet<int> riverToCells = new HashSet<int>((map.rivers ?? new List<RiverSegment>())
                .Where(segment => segment != null).Select(segment => segment.toCellIndex));
            HashSet<int> veinCells = new HashSet<int>((map.spiritVeins ?? new List<SpiritVein>())
                .Where(vein => vein?.pathCellIndices != null).SelectMany(vein => vein.pathCellIndices));
            foreach (MapRegionData region in result.regions)
            {
                HashSet<int> members = new HashSet<int>(region.cellIndices);
                float averageHeight = region.cellIndices.Average(index => map.cells[index].height);
                foreach (int index in region.cellIndices)
                    result.internalPositionTags[index] = PositionTag(map, region, index, members, riverCells,
                        riverFromCells, riverToCells, veinCells, averageHeight);
            }
            return result;
        }

        public static string RegionTypeLabel(MapRegionType type)
        {
            switch (type)
            {
                case MapRegionType.SmallHill: return "小山";
                case MapRegionType.MountainRange: return "山脉";
                case MapRegionType.Hills: return "丘陵";
                case MapRegionType.Plain: return "平原";
                case MapRegionType.Forest: return "林海";
                case MapRegionType.Valley: return "山谷";
                case MapRegionType.Desert: return "荒原";
                case MapRegionType.Swamp: return "泽地";
                case MapRegionType.Lake: return "湖";
                default: return "海";
            }
        }

        public static string PositionLabel(MapInternalPositionTag tag)
        {
            switch (tag)
            {
                case MapInternalPositionTag.MountainFoot: return "山脚";
                case MapInternalPositionTag.Mountainside: return "山腰";
                case MapInternalPositionTag.Ridge: return "山脊";
                case MapInternalPositionTag.HillFoot: return "丘脚";
                case MapInternalPositionTag.Hilltop: return "丘顶";
                case MapInternalPositionTag.ForestEdge: return "林缘";
                case MapInternalPositionTag.DeepForest: return "密林";
                case MapInternalPositionTag.ValleyEntrance: return "谷口";
                case MapInternalPositionTag.ValleyFloor: return "谷底";
                case MapInternalPositionTag.OpenPlain: return "原野";
                case MapInternalPositionTag.Riverbank: return "河岸";
                case MapInternalPositionTag.DesertEdge: return "荒原边缘";
                case MapInternalPositionTag.Dune: return "沙丘";
                case MapInternalPositionTag.MarshEdge: return "泽畔";
                case MapInternalPositionTag.DeepMarsh: return "深泽";
                case MapInternalPositionTag.Lakeshore: return "湖岸";
                case MapInternalPositionTag.Shallows: return "浅滩";
                case MapInternalPositionTag.Coastline: return "海岸";
                case MapInternalPositionTag.DeepWater: return "远海";
                case MapInternalPositionTag.Summit: return "山顶";
                case MapInternalPositionTag.MountainPass: return "山坳";
                case MapInternalPositionTag.Cliff: return "峭壁";
                case MapInternalPositionTag.CaveMouth: return "洞口";
                case MapInternalPositionTag.ForestClearing: return "林间空地";
                case MapInternalPositionTag.AncientGrove: return "古树区";
                case MapInternalPositionTag.BeastTrail: return "兽道";
                case MapInternalPositionTag.HerbSlope: return "药草坡";
                case MapInternalPositionTag.LakeCenter: return "湖心";
                case MapInternalPositionTag.ReedShore: return "芦苇岸";
                case MapInternalPositionTag.WaterInlet: return "入水口";
                case MapInternalPositionTag.WaterOutlet: return "出水口";
                default: return "腹地";
            }
        }

        private static MapInternalPositionTag DerivePositionTag(WorldMap map, MapRegionData region, int cellIndex)
        {
            if (map?.cells == null || region?.cellIndices == null || !region.cellIndices.Contains(cellIndex))
                throw new ArgumentException("区域或格子无效");
            var members = new HashSet<int>(region.cellIndices);
            var riverCells = RiverCells(map);
            var riverFromCells = new HashSet<int>((map.rivers ?? new List<RiverSegment>())
                .Where(segment => segment != null).Select(segment => segment.fromCellIndex));
            var riverToCells = new HashSet<int>((map.rivers ?? new List<RiverSegment>())
                .Where(segment => segment != null).Select(segment => segment.toCellIndex));
            var veinCells = new HashSet<int>((map.spiritVeins ?? new List<SpiritVein>())
                .Where(vein => vein?.pathCellIndices != null).SelectMany(vein => vein.pathCellIndices));
            float averageHeight = region.cellIndices.Average(index => map.cells[index].height);
            return PositionTag(map, region, cellIndex, members, riverCells, riverFromCells,
                riverToCells, veinCells, averageHeight);
        }

        private static MapRegionType[] ClassifyCells(WorldMap map)
        {
            MapRegionType[] types = new MapRegionType[map.cells.Length];
            bool[] openWater = FindEdgeWater(map);
            HashSet<int> riverCells = RiverCells(map);
            foreach (WorldCell cell in map.cells)
            {
                if (IsWater(cell)) types[cell.index] = openWater[cell.index] ? MapRegionType.OpenWater : MapRegionType.Lake;
                else if (cell.biome == BiomeType.TemperateForest || cell.biome == BiomeType.Rainforest) types[cell.index] = MapRegionType.Forest;
                else if (cell.biome == BiomeType.Desert) types[cell.index] = MapRegionType.Desert;
                else if (cell.biome == BiomeType.Wetland) types[cell.index] = MapRegionType.Swamp;
                else if (cell.landform == LandformType.Mountain) types[cell.index] = MapRegionType.MountainRange;
                else if (cell.landform == LandformType.Hill) types[cell.index] = MapRegionType.Hills;
                else types[cell.index] = IsValley(map, cell, riverCells) ? MapRegionType.Valley : MapRegionType.Plain;
            }
            foreach (List<int> component in Components(map, types, MapRegionType.MountainRange))
                if (component.Count < SmallMountainComponentThreshold)
                    foreach (int index in component) types[index] = MapRegionType.SmallHill;
            return types;
        }

        private static bool[] FindEdgeWater(WorldMap map)
        {
            bool[] result = new bool[map.cells.Length];
            Queue<int> queue = new Queue<int>();
            foreach (WorldCell cell in map.cells)
                if (IsWater(cell) && (cell.coord.col == 0 || cell.coord.row == 0 || cell.coord.col == map.width - 1 || cell.coord.row == map.height - 1))
                { result[cell.index] = true; queue.Enqueue(cell.index); }
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int neighbor in map.GetNeighborIndices(current))
                    if (!result[neighbor] && IsWater(map.cells[neighbor])) { result[neighbor] = true; queue.Enqueue(neighbor); }
            }
            return result;
        }

        private static bool IsWater(WorldCell cell) => cell.landform == LandformType.DeepWater || cell.landform == LandformType.ShallowWater;

        private static bool IsValley(WorldMap map, WorldCell cell, HashSet<int> riverCells)
        {
            List<WorldCell> neighbors = map.GetNeighborIndices(cell.index).Select(index => map.cells[index]).ToList();
            if (neighbors.Count == 0) return false;
            int higher = neighbors.Count(item => item.height >= cell.height + 0.06f);
            bool nearHighland = neighbors.Any(item => item.landform == LandformType.Hill || item.landform == LandformType.Mountain);
            return (higher >= 3 && nearHighland) || (riverCells.Contains(cell.index) && nearHighland);
        }

        private static List<RegionSeed> SplitConnectedComponents(WorldMap map, MapRegionType[] types)
        {
            List<RegionSeed> result = new List<RegionSeed>();
            bool[] visited = new bool[map.cells.Length];
            for (int start = 0; start < map.cells.Length; start++)
            {
                if (visited[start]) continue;
                MapRegionType type = types[start];
                List<int> component = CollectComponent(map, start, index => types[index] == type, visited);
                result.AddRange(BalancedGrow(map, component, type));
            }
            return result;
        }

        private static List<RegionSeed> BalancedGrow(WorldMap map, List<int> component, MapRegionType type)
        {
            int target = type == MapRegionType.OpenWater ? OpenWaterTargetSize : OrdinaryTargetSize;
            int maximum = type == MapRegionType.OpenWater ? OpenWaterMaximumSize : OrdinaryMaximumSize;
            int seedCount = Math.Max(1, (int)Math.Round(component.Count / (double)target, MidpointRounding.AwayFromZero));
            seedCount = Math.Max(seedCount, (component.Count + maximum - 1) / maximum);
            seedCount = Math.Min(seedCount, component.Count);
            List<int> centers = SelectGrowthCenters(map, component, seedCount, type);
            List<RegionSeed> regions = centers.Select(center => new RegionSeed
            {
                type = type,
                cells = new List<int> { center }
            }).ToList();
            HashSet<int> unassigned = new HashSet<int>(component);
            foreach (int center in centers) unassigned.Remove(center);

            while (unassigned.Count > 0)
            {
                bool progressed = false;
                foreach (RegionSeed region in regions.OrderBy(item => item.cells[0]).ToList())
                {
                    if (region.cells.Count >= maximum) continue;
                    int candidate = region.cells.SelectMany(map.GetNeighborIndices).Where(unassigned.Contains)
                        .Distinct()
                        .OrderBy(index => StableUnsigned(map.effectiveSeed,
                            "region-balanced-grow-" + type + "-" + region.cells[0] + "-" + index))
                        .ThenBy(index => index).DefaultIfEmpty(-1).First();
                    if (candidate < 0) continue;
                    region.cells.Add(candidate);
                    unassigned.Remove(candidate);
                    progressed = true;
                }
                if (progressed) continue;

                // A claimed frontier can isolate part of an irregular component. Start a stable
                // supplementary source instead of exceeding the approved size ceiling.
                int supplementary = unassigned.OrderBy(index => StableUnsigned(map.effectiveSeed,
                    "region-supplementary-" + type + "-" + index)).ThenBy(index => index).First();
                regions.Add(new RegionSeed { type = type, cells = new List<int> { supplementary } });
                unassigned.Remove(supplementary);
            }
            return regions;
        }

        private static List<int> SelectGrowthCenters(WorldMap map, List<int> component, int count, MapRegionType type)
        {
            List<int> centers = new List<int>
            {
                component.OrderBy(index => StableUnsigned(map.effectiveSeed, "region-center-" + type + "-" + index))
                    .ThenBy(index => index).First()
            };
            while (centers.Count < count)
            {
                int next = component.Where(index => !centers.Contains(index))
                    .OrderByDescending(index => centers.Min(center => HexCoord.Distance(map.cells[index].coord, map.cells[center].coord)))
                    .ThenBy(index => StableUnsigned(map.effectiveSeed, "region-farthest-" + type + "-" + index))
                    .ThenBy(index => index).First();
                centers.Add(next);
            }
            return centers;
        }

        private static void MergeSmallRegions(WorldMap map, List<RegionSeed> seeds)
        {
            bool changed;
            do
            {
                changed = false;
                RegionSeed small = seeds.Where(seed => seed.cells.Count > 0 && seed.cells.Count < MinimumRegionSize && !seed.mergeExamined)
                    .OrderBy(seed => seed.cells.Count).ThenBy(seed => seed.cells.Min()).FirstOrDefault();
                if (small == null) break;
                HashSet<int> own = new HashSet<int>(small.cells);
                RegionSeed target = seeds.Where(seed => seed != small && seed.cells.Count > 0 &&
                        seed.cells.Any(index => map.GetNeighborIndices(index).Any(own.Contains)) &&
                        seed.cells.Count + small.cells.Count <= MaximumSize(seed.type))
                    .OrderByDescending(seed => TypeSimilarity(small.type, seed.type))
                    .ThenBy(seed => seed.cells.Count)
                    .ThenBy(seed => seed.cells.Min()).FirstOrDefault();
                if (target == null)
                {
                    RegionSeed adjacent = seeds.Where(seed => seed != small && seed.cells.Count > 0 &&
                            seed.cells.Any(index => map.GetNeighborIndices(index).Any(own.Contains)))
                        .OrderByDescending(seed => TypeSimilarity(small.type, seed.type))
                        .ThenBy(seed => seed.cells.Min()).FirstOrDefault();
                    if (adjacent == null)
                    {
                        small.mergeExamined = true;
                        changed = true;
                        continue;
                    }
                    List<int> union = adjacent.cells.Concat(small.cells).Distinct().ToList();
                    List<RegionSeed> rebalanced = BalancedGrow(map, union, adjacent.type);
                    adjacent.cells = rebalanced[0].cells;
                    small.type = adjacent.type;
                    small.cells = rebalanced.Count > 1 ? rebalanced[1].cells : new List<int>();
                    small.mergeExamined = false;
                    for (int index = 2; index < rebalanced.Count; index++) seeds.Add(rebalanced[index]);
                    changed = true;
                    continue;
                }
                target.cells.AddRange(small.cells);
                small.cells.Clear();
                changed = true;
            } while (changed);
        }

        private static int MaximumSize(MapRegionType type) =>
            type == MapRegionType.OpenWater ? OpenWaterMaximumSize : OrdinaryMaximumSize;

        private static int TypeSimilarity(MapRegionType left, MapRegionType right)
        {
            if (left == right) return 100;
            bool leftWater = left == MapRegionType.Lake || left == MapRegionType.OpenWater;
            bool rightWater = right == MapRegionType.Lake || right == MapRegionType.OpenWater;
            if (leftWater || rightWater) return leftWater && rightWater ? 80 : -100;
            bool leftHigh = left == MapRegionType.SmallHill || left == MapRegionType.MountainRange || left == MapRegionType.Hills;
            bool rightHigh = right == MapRegionType.SmallHill || right == MapRegionType.MountainRange || right == MapRegionType.Hills;
            if (leftHigh && rightHigh) return 80;
            if ((left == MapRegionType.Plain || left == MapRegionType.Valley) &&
                (right == MapRegionType.Plain || right == MapRegionType.Valley)) return 70;
            if ((left == MapRegionType.Forest || left == MapRegionType.Swamp) &&
                (right == MapRegionType.Forest || right == MapRegionType.Swamp)) return 60;
            return 20;
        }

        private static List<int> CollectComponent(WorldMap map, int start, Func<int, bool> allowed, bool[] visited)
        {
            List<int> result = new List<int>(); Queue<int> queue = new Queue<int>();
            visited[start] = true; queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue(); result.Add(current);
                foreach (int neighbor in map.GetNeighborIndices(current))
                    if (!visited[neighbor] && allowed(neighbor)) { visited[neighbor] = true; queue.Enqueue(neighbor); }
            }
            return result;
        }

        private static IEnumerable<List<int>> Components(WorldMap map, MapRegionType[] types, MapRegionType type)
        {
            bool[] visited = new bool[map.cells.Length];
            for (int index = 0; index < map.cells.Length; index++)
                if (!visited[index] && types[index] == type) yield return CollectComponent(map, index, i => types[i] == type, visited);
        }

        private static int SelectCenter(WorldMap map, List<int> cells)
        {
            float col = (float)cells.Average(index => map.cells[index].coord.col);
            float row = (float)cells.Average(index => map.cells[index].coord.row);
            return cells.OrderBy(index => Math.Abs(map.cells[index].coord.col - col) + Math.Abs(map.cells[index].coord.row - row))
                .ThenBy(index => index).First();
        }

        private static MapRegionData CreateRegion(WorldMap map, MapRegionType type, List<int> cells, int center, string id, float mapAura, float mapDanger)
        {
            float aura = cells.Average(index => map.cells[index].totalAura);
            float danger = cells.Average(index => (float)WorldMapProgressRules.GetDanger(map.cells[index]));
            return new MapRegionData
            {
                regionId = id, regionType = type, cellIndices = new List<int>(cells), centerCellIndex = center,
                dominantLandform = Mode(cells.Select(index => map.cells[index].landform)),
                dominantBiome = Mode(cells.Select(index => map.cells[index].biome)),
                hiddenElementBias = DominantElement(map, cells),
                averageAura = aura, averageDanger = danger,
                auraTrend = Trend(aura, mapAura, 0.12f), dangerTrend = Trend(danger, mapDanger, 0.25f),
                displayPriority = type == MapRegionType.OpenWater ? 10 : Math.Min(100, 30 + cells.Count)
            };
        }

        private static T Mode<T>(IEnumerable<T> values) => values.GroupBy(value => value)
            .OrderByDescending(group => group.Count()).ThenBy(group => Convert.ToInt32(group.Key)).First().Key;

        private static SpiritElement DominantElement(WorldMap map, List<int> cells)
        {
            float[] values = new float[5];
            foreach (int index in cells)
            {
                ElementalAura aura = map.cells[index].elementalAura;
                values[0] += aura.metal; values[1] += aura.wood; values[2] += aura.water; values[3] += aura.fire; values[4] += aura.earth;
            }
            int best = 0; for (int index = 1; index < values.Length; index++) if (values[index] > values[best]) best = index;
            return (SpiritElement)best;
        }

        private static MapRegionTrend Trend(float value, float baseline, float margin)
        {
            if (value > baseline + margin) return MapRegionTrend.High;
            if (value < baseline - margin) return MapRegionTrend.Low;
            return MapRegionTrend.Normal;
        }

        private static MapInternalPositionTag PositionTag(WorldMap map, MapRegionData region, int index,
            HashSet<int> members, HashSet<int> riverCells, HashSet<int> riverFromCells,
            HashSet<int> riverToCells, HashSet<int> veinCells, float averageHeight)
        {
            WorldCell cell = map.cells[index];
            List<int> neighbors = map.GetNeighborIndices(index).ToList();
            bool edge = neighbors.Count < 6 || neighbors.Any(neighbor => !members.Contains(neighbor));
            bool river = riverCells.Contains(index);
            float minNeighborHeight = neighbors.Count == 0 ? cell.height : neighbors.Min(neighbor => map.cells[neighbor].height);
            float maxNeighborHeight = neighbors.Count == 0 ? cell.height : neighbors.Max(neighbor => map.cells[neighbor].height);
            int centerDistance = HexCoord.Distance(cell.coord, map.cells[region.centerCellIndex].coord);
            switch (region.regionType)
            {
                case MapRegionType.MountainRange:
                    if (maxNeighborHeight - minNeighborHeight >= 0.28f) return MapInternalPositionTag.Cliff;
                    if (edge && veinCells.Contains(index)) return MapInternalPositionTag.CaveMouth;
                    if (cell.height >= averageHeight + 0.12f) return MapInternalPositionTag.Summit;
                    if (!edge && cell.height <= averageHeight - 0.05f && neighbors.Count(n => map.cells[n].height > cell.height + 0.08f) >= 3)
                        return MapInternalPositionTag.MountainPass;
                    if (cell.height >= averageHeight + 0.04f) return MapInternalPositionTag.Ridge;
                    if (edge && cell.height <= averageHeight) return MapInternalPositionTag.MountainFoot;
                    return MapInternalPositionTag.Mountainside;
                case MapRegionType.SmallHill:
                case MapRegionType.Hills: return cell.height >= averageHeight ? MapInternalPositionTag.Hilltop : MapInternalPositionTag.HillFoot;
                case MapRegionType.Forest:
                    if (WorldMapProgressRules.GetDanger(cell) == WorldDangerLevel.High) return MapInternalPositionTag.BeastTrail;
                    if (cell.totalAura >= region.averageAura + 0.08f && cell.moisture >= 0.55f) return MapInternalPositionTag.AncientGrove;
                    if (edge && cell.moisture >= 0.62f && maxNeighborHeight - minNeighborHeight >= 0.08f) return MapInternalPositionTag.HerbSlope;
                    if (!edge && centerDistance <= 2 && cell.moisture <= 0.32f) return MapInternalPositionTag.ForestClearing;
                    return edge ? MapInternalPositionTag.ForestEdge : MapInternalPositionTag.DeepForest;
                case MapRegionType.Valley: return edge ? MapInternalPositionTag.ValleyEntrance : MapInternalPositionTag.ValleyFloor;
                case MapRegionType.Plain: return river ? MapInternalPositionTag.Riverbank : MapInternalPositionTag.OpenPlain;
                case MapRegionType.Desert: return edge ? MapInternalPositionTag.DesertEdge : MapInternalPositionTag.Dune;
                case MapRegionType.Swamp: return edge ? MapInternalPositionTag.MarshEdge : MapInternalPositionTag.DeepMarsh;
                case MapRegionType.Lake:
                    if (riverToCells.Contains(index)) return MapInternalPositionTag.WaterInlet;
                    if (riverFromCells.Contains(index)) return MapInternalPositionTag.WaterOutlet;
                    if (edge && cell.moisture >= 0.72f) return MapInternalPositionTag.ReedShore;
                    if (cell.landform == LandformType.ShallowWater) return MapInternalPositionTag.Shallows;
                    if (!edge || centerDistance <= 1) return MapInternalPositionTag.LakeCenter;
                    return MapInternalPositionTag.Lakeshore;
                default: return edge ? MapInternalPositionTag.Coastline : MapInternalPositionTag.DeepWater;
            }
        }

        private static string UniqueRegionId(int seed, MapRegionType type, int center, HashSet<string> used)
        {
            for (int attempt = 0; attempt < 16; attempt++)
            {
                string id = "region_" + type.ToString().ToLowerInvariant() + "_" +
                    StableUnsigned(seed, "region-id-" + type + "-" + center + "-" + attempt).ToString("x8");
                if (!used.Contains(id)) return id;
            }
            throw new InvalidOperationException("无法生成唯一的区域 ID");
        }

        private static string UniqueName(int seed, MapRegionType type, int center, HashSet<string> used)
        {
            string suffix = RegionTypeLabel(type);
            uint hash = StableUnsigned(seed, "region-name-" + type + "-" + center);
            for (int attempt = 0; attempt < NamePrefixes.Length; attempt++)
            {
                string simple = NamePrefixes[(hash + (uint)attempt) % NamePrefixes.Length] + suffix;
                if (!used.Contains(simple)) return simple;
            }
            for (int attempt = 0; attempt < NamePrefixes.Length * NameInfixes.Length; attempt++)
            {
                string name = NamePrefixes[(hash + (uint)attempt) % NamePrefixes.Length] +
                              NameInfixes[((hash / NamePrefixes.Length) + (uint)(attempt / NamePrefixes.Length)) % NameInfixes.Length] + suffix;
                if (!used.Contains(name)) return name;
            }
            throw new InvalidOperationException("无法生成唯一的区域名称");
        }

        private static HashSet<int> RiverCells(WorldMap map) => new HashSet<int>((map.rivers ?? new List<RiverSegment>())
            .SelectMany(segment => new[] { segment.fromCellIndex, segment.toCellIndex }));
        private static uint StableUnsigned(int seed, string label) => unchecked((uint)SeedDerivation.Derive(seed, label));

        private sealed class RegionSeed
        {
            public MapRegionType type;
            public List<int> cells = new List<int>();
            public bool mergeExamined;
        }
    }
}
