using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 连续区块地表。WorldCell 仍是数据和交互单位；显示网格在每个 Hex 内细分，
    /// 并从同一世界坐标高度场采样，因此相邻格边缘连续而不依赖逐格棱柱。
    /// </summary>
    internal static class ContinuousTerrainSurfaceBuilder
    {
        private const float RidgeVerticalMultiplier = 1.50f;
        private const float PeakToAdjacentRidgeLimit = 1.25f;
        private const float RidgeShoulderBlend = 0.30f;

        // Z-Fighting 定位开关：测试/诊断时可分别关闭反向侧壁与角点封口。
        // CornerClosure 为同一 XZ 的竖直退化三角，默认关闭，避免产生黑色条带。
        internal static bool EnableRisingVerticalSides = true;
        internal static bool EnableCornerClosures = false;

        internal enum MountainFaceDebugMode
        {
            None,
            Top,
            Side,
            RisingSide,
            CornerClosure
        }

        // 面来源调试色：只改顶点色，不改材质与 Shader。
        internal static MountainFaceDebugMode DebugFaceMode = MountainFaceDebugMode.None;

        private static Color32 DebugFaceColor(MountainFaceDebugMode mode)
        {
            switch (mode)
            {
                case MountainFaceDebugMode.Top: return new Color32(255, 220, 0, 255);
                case MountainFaceDebugMode.Side: return new Color32(255, 40, 40, 255);
                case MountainFaceDebugMode.RisingSide: return new Color32(40, 255, 60, 255);
                case MountainFaceDebugMode.CornerClosure: return new Color32(40, 160, 255, 255);
                default: return Color.white;
            }
        }

        internal sealed class BuildResult
        {
            public readonly List<Mesh> meshes;
            public readonly float[] centerHeights;
            public readonly Func<Vector2, bool, float> heightAt;

            public BuildResult(List<Mesh> meshes, float[] centerHeights,
                Func<Vector2, bool, float> heightAt)
            {
                this.meshes = meshes;
                this.centerHeights = centerHeights;
                this.heightAt = heightAt;
            }
        }

        private readonly struct SampleKey : IEquatable<SampleKey>
        {
            private const float Precision = 10000f;
            private readonly int x;
            private readonly int z;
            private readonly byte surfaceClass;

            public SampleKey(Vector2 position, bool water)
            {
                x = Mathf.RoundToInt(position.x * Precision);
                z = Mathf.RoundToInt(position.y * Precision);
                surfaceClass = water ? (byte)1 : (byte)0;
            }

            public bool Equals(SampleKey other) =>
                x == other.x && z == other.z && surfaceClass == other.surfaceClass;
            public override bool Equals(object obj) => obj is SampleKey other && Equals(other);
            public override int GetHashCode() => unchecked(((x * 397) ^ z) * 397 ^ surfaceClass);
        }

        private readonly struct SurfaceSample
        {
            public readonly float height;
            public readonly Vector3 normal;

            public SurfaceSample(float height, Vector3 normal)
            {
                this.height = height;
                this.normal = normal;
            }
        }

        private readonly struct VisualSample
        {
            public readonly Color32 color;
            public readonly Vector4 materialWeights;
            public readonly float moisture;

            public VisualSample(Color32 color, Vector4 materialWeights, float moisture)
            {
                this.color = color;
                this.materialWeights = materialWeights;
                this.moisture = moisture;
            }
        }

        /// <summary>
        /// 仅供连续地表使用的表现采样场。数据仍按 Hex 保存，但顶点颜色和四种陆地材质
        /// 权重按世界坐标采样，使地貌交界不再沿单个 Hex 的六条边硬切。
        /// </summary>
        private sealed class VisualField
        {
            private const int SearchRadius = 2;
            private readonly WorldMap map;
            private readonly Func<WorldCell, Color32> colorFor;
            private readonly bool blendLandMaterials;
            private readonly Dictionary<SampleKey, VisualSample> cache =
                new Dictionary<SampleKey, VisualSample>();

            public VisualField(WorldMap map, Func<WorldCell, Color32> colorFor,
                bool blendLandMaterials)
            {
                this.map = map;
                this.colorFor = colorFor;
                this.blendLandMaterials = blendLandMaterials;
            }

            public VisualSample Sample(Vector2 position, WorldCell owningCell)
            {
                if (owningCell == null)
                    return new VisualSample(Color.white, new Vector4(0f, 1f, 0f, 0f), 0.5f);
                if (!blendLandMaterials || IsWater(owningCell)) return CellSample(owningCell);

                var key = new SampleKey(position, false);
                if (cache.TryGetValue(key, out VisualSample cached)) return cached;

                Vector2 warped = position + BoundaryWarp(position);
                int rowGuess = Mathf.RoundToInt(warped.y / 1.5f);
                int colGuess = Mathf.RoundToInt(warped.x / Mathf.Sqrt(3f) -
                                                ((rowGuess & 1) == 1 ? 0.5f : 0f));
                Color blendedColor = Color.clear;
                Vector4 blendedWeights = Vector4.zero;
                float blendedMoisture = 0f;
                float totalWeight = 0f;
                for (int row = rowGuess - SearchRadius; row <= rowGuess + SearchRadius; row++)
                {
                    for (int col = colGuess - SearchRadius; col <= colGuess + SearchRadius; col++)
                    {
                        int index = map.GetIndex(new HexCoord(col, row));
                        if (index < 0 || index >= map.cells.Length) continue;
                        WorldCell cell = map.cells[index];
                        if (cell == null || IsWater(cell)) continue;
                        Vector2 center = TerrainMeshGenerator.HexCenter(cell.coord);
                        float distanceSquared = (warped - center).sqrMagnitude;
                        if (distanceSquared > 12f) continue;
                        float weight = Mathf.Exp(-distanceSquared * 2.15f);
                        blendedColor += (Color)ColorFor(cell) * weight;
                        blendedWeights += MaterialWeightsFor(cell) * weight;
                        blendedMoisture += Mathf.Clamp01(cell.moisture) * weight;
                        totalWeight += weight;
                    }
                }

                VisualSample sample;
                if (totalWeight <= 0.0001f)
                {
                    sample = CellSample(owningCell);
                }
                else
                {
                    blendedColor /= totalWeight;
                    blendedColor.a = 1f;
                    blendedWeights /= totalWeight;
                    blendedMoisture /= totalWeight;
                    sample = new VisualSample((Color32)blendedColor, blendedWeights,
                        blendedMoisture);
                }
                cache.Add(key, sample);
                return sample;
            }

            private VisualSample CellSample(WorldCell cell) =>
                new VisualSample(ColorFor(cell), MaterialWeightsFor(cell),
                    Mathf.Clamp01(cell.moisture));

            private Color32 ColorFor(WorldCell cell) => colorFor != null
                ? colorFor(cell)
                : (Color32)Color.white;

            private static Vector2 BoundaryWarp(Vector2 position)
            {
                float x = ValueNoise(position * 0.19f + new Vector2(8.3f, 31.7f)) - 0.5f;
                float y = ValueNoise(position * 0.19f + new Vector2(47.1f, 5.9f)) - 0.5f;
                return new Vector2(x, y) * 0.34f;
            }

            private static Vector4 MaterialWeightsFor(WorldCell cell)
            {
                // 通道语义固定为沙 / 草 / 泥 / 石。生物群系决定基础地表，地貌只做
                // 二次修正；湿度仅在群系允许的范围内轻微移动草和泥的比例。
                Vector4 weights;
                switch (cell.biome)
                {
                    case BiomeType.Coast:
                        weights = new Vector4(0.82f, 0.05f, 0.11f, 0.02f);
                        break;
                    case BiomeType.TemperateForest:
                        weights = new Vector4(0.01f, 0.40f, 0.51f, 0.08f);
                        break;
                    case BiomeType.Rainforest:
                        weights = new Vector4(0.01f, 0.58f, 0.37f, 0.04f);
                        break;
                    case BiomeType.Wetland:
                        weights = new Vector4(0.02f, 0.38f, 0.57f, 0.03f);
                        break;
                    case BiomeType.Desert:
                        weights = new Vector4(0.78f, 0.01f, 0.16f, 0.05f);
                        break;
                    case BiomeType.Tundra:
                        weights = new Vector4(0.01f, 0.22f, 0.46f, 0.31f);
                        break;
                    case BiomeType.Snowfield:
                        weights = new Vector4(0.02f, 0.04f, 0.24f, 0.70f);
                        break;
                    case BiomeType.Alpine:
                        weights = new Vector4(0f, 0.04f, 0.18f, 0.78f);
                        break;
                    default:
                        weights = new Vector4(0.02f, 0.47f, 0.43f, 0.08f);
                        break;
                }

                if (cell.landform == LandformType.Coast)
                {
                    float coastSand = Mathf.Max(weights.x, 0.68f);
                    float remaining = 1f - coastSand;
                    float nonSand = Mathf.Max(0.0001f, weights.y + weights.z + weights.w);
                    weights = new Vector4(coastSand,
                        remaining * weights.y / nonSand,
                        remaining * weights.z / nonSand,
                        remaining * weights.w / nonSand);
                }
                else if (cell.landform == LandformType.Hill)
                {
                    Transfer(ref weights.y, ref weights.z, weights.y * 0.16f);
                    Transfer(ref weights.y, ref weights.w, weights.y * 0.10f);
                    Transfer(ref weights.x, ref weights.z, weights.x * 0.10f);
                }
                else if (cell.landform == LandformType.Mountain)
                {
                    Transfer(ref weights.x, ref weights.w, weights.x * 0.72f);
                    Transfer(ref weights.y, ref weights.w, weights.y * 0.72f);
                    Transfer(ref weights.z, ref weights.w, weights.z * 0.42f);
                }

                if (cell.landform == LandformType.Mountain && cell.isBuildable)
                {
                    // 山地台地：表面更接近平台岩面，保持与普通山地的材质差异。
                    Transfer(ref weights.y, ref weights.w, weights.y * 0.20f);
                    Transfer(ref weights.z, ref weights.w, weights.z * 0.10f);
                }

                bool moistureSensitive = cell.biome != BiomeType.Desert &&
                                         cell.biome != BiomeType.Snowfield &&
                                         cell.biome != BiomeType.Alpine;
                if (moistureSensitive)
                {
                    float moistureShift = (Mathf.Clamp01(cell.moisture) - 0.5f) * 0.16f;
                    if (moistureShift >= 0f)
                        Transfer(ref weights.z, ref weights.y,
                            Mathf.Min(weights.z, moistureShift));
                    else
                        Transfer(ref weights.y, ref weights.z,
                            Mathf.Min(weights.y, -moistureShift));
                }

                float total = weights.x + weights.y + weights.z + weights.w;
                return total > 0.0001f ? weights / total : new Vector4(0f, 1f, 0f, 0f);
            }

            private static void Transfer(ref float source, ref float destination, float amount)
            {
                amount = Mathf.Clamp(amount, 0f, source);
                source -= amount;
                destination += amount;
            }
        }

        /// <summary>
        /// 山体平顶遮罩：LandformType.Mountain 以及被山体完全围住的非水陆地格。
        /// 这些格按同一“乐高”规则平顶渲染，避免山峰之间的低谷/湖岸被连续
        /// 高度场插值出尖锐凸起；WorldCell.landform 保持数据语义不变。
        /// </summary>
        internal static bool[] BuildPlateauMask(WorldMap map)
        {
            int count = map?.cells?.Length ?? 0;
            var mask = new bool[count];
            if (count == 0) return mask;
            for (int index = 0; index < count; index++)
            {
                WorldCell cell = map.cells[index];
                if (cell == null || IsWater(cell)) continue;
                if (cell.landform == LandformType.Mountain) mask[index] = true;
            }

            // 与地图边缘相通的水域会向海洋开放；围住一片内陆水面的陆地仍算山内低地。
            bool[] edgeWater = new bool[count];
            Queue<int> waterQueue = new Queue<int>();
            for (int index = 0; index < count; index++)
            {
                WorldCell cell = map.cells[index];
                if (cell == null || !IsWater(cell)) continue;
                if (cell.coord.col != 0 && cell.coord.row != 0 &&
                    cell.coord.col != map.width - 1 && cell.coord.row != map.height - 1) continue;
                edgeWater[index] = true;
                waterQueue.Enqueue(index);
            }
            while (waterQueue.Count > 0)
            {
                int current = waterQueue.Dequeue();
                foreach (int neighbor in map.GetNeighborIndices(current))
                {
                    if (edgeWater[neighbor] || map.cells[neighbor] == null || !IsWater(map.cells[neighbor])) continue;
                    edgeWater[neighbor] = true;
                    waterQueue.Enqueue(neighbor);
                }
            }

            bool[] visited = new bool[count];
            for (int start = 0; start < count; start++)
            {
                WorldCell startCell = map.cells[start];
                if (startCell == null || IsWater(startCell) || mask[start] || visited[start]) continue;
                var component = new List<int>();
                var queue = new Queue<int>();
                bool touchesMapEdge = false;
                bool touchesEdgeWater = false;
                visited[start] = true;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    component.Add(current);
                    WorldCell cell = map.cells[current];
                    if (cell.coord.col == 0 || cell.coord.row == 0 ||
                        cell.coord.col == map.width - 1 || cell.coord.row == map.height - 1)
                        touchesMapEdge = true;
                    foreach (int neighbor in map.GetNeighborIndices(current))
                    {
                        WorldCell neighborCell = map.cells[neighbor];
                        if (neighborCell == null) continue;
                        if (edgeWater[neighbor]) touchesEdgeWater = true;
                        if (visited[neighbor] || IsWater(neighborCell) || mask[neighbor]) continue;
                        visited[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }
                if (touchesMapEdge || touchesEdgeWater) continue;
                foreach (int index in component) mask[index] = true;
            }
            return mask;
        }

        private sealed class HeightField
        {
            private const int SearchRadius = 2;
            private const float NormalSampleDistance = 0.18f;
            private readonly WorldMap map;
            private readonly float[] sourceHeights;
            private readonly float[] detailAmplitudes;
            private readonly bool[] ridgeCore;
            private readonly bool[] peaks;
            private readonly bool[] passes;
            private readonly bool[] plateauMask;
            private readonly bool preserveMountainSkeleton;
            private readonly TerrainMeshAppearance appearance;
            private readonly Dictionary<SampleKey, SurfaceSample> cache =
                new Dictionary<SampleKey, SurfaceSample>();
            private readonly float waterHeight;

            public HeightField(WorldMap map, TerrainMeshAppearance appearance)
            {
                this.map = map;
                this.appearance = appearance;
                int count = map?.cells?.Length ?? 0;
                sourceHeights = new float[count];
                detailAmplitudes = new float[count];
                ridgeCore = new bool[count];
                peaks = new bool[count];
                passes = new bool[count];
                if (WorldGenerationDiagnosticsStore.TryGet(map,
                        out WorldGenerationDiagnostics diagnostics) &&
                    diagnostics.mountainRidgeCore.Length == count &&
                    diagnostics.mountainPeaks.Length == count &&
                    diagnostics.mountainPasses.Length == count)
                {
                    Array.Copy(diagnostics.mountainRidgeCore, ridgeCore, count);
                    Array.Copy(diagnostics.mountainPeaks, peaks, count);
                    Array.Copy(diagnostics.mountainPasses, passes, count);
                    preserveMountainSkeleton = ridgeCore.Any(value => value);
                }
                waterHeight = TerrainMeshGenerator.StrategicSurfaceHeight(LandformType.ShallowWater);
                plateauMask = BuildPlateauMask(map);
                var rawHeights = new float[count];
                for (int index = 0; index < count; index++)
                {
                    WorldCell cell = map.cells[index];
                    if (cell == null || IsWater(cell)) continue;
                    rawHeights[index] = RawLandHeight(map, cell, appearance);
                    detailAmplitudes[index] = DetailAmplitude(cell) * Mathf.Min(appearance.heightScale, 1.25f);
                }
                for (int index = 0; index < count; index++)
                {
                    WorldCell cell = map.cells[index];
                    if (cell == null || IsWater(cell)) continue;
                    sourceHeights[index] = ApplyMountainVerticalEmphasis(rawHeights[index],
                        ridgeCore[index], peaks[index], passes[index]);
                }
                for (int index = 0; index < count; index++)
                {
                    if (!peaks[index] || passes[index]) continue;
                    float highestAdjacentRidge = float.NegativeInfinity;
                    foreach (int neighbor in map.GetNeighborIndices(index))
                    {
                        if (!ridgeCore[neighbor] || peaks[neighbor] || passes[neighbor]) continue;
                        highestAdjacentRidge = Mathf.Max(highestAdjacentRidge, sourceHeights[neighbor]);
                    }
                    if (!float.IsNegativeInfinity(highestAdjacentRidge))
                    {
                        sourceHeights[index] = ClampPeakToAdjacentRidge(sourceHeights[index],
                            highestAdjacentRidge);
                    }
                }
                ApplyOneRingRidgeShoulders(map, sourceHeights, ridgeCore, passes);
                ApplyMountainTerraceFlattening(map, sourceHeights, detailAmplitudes);
            }

            public SurfaceSample Sample(Vector2 position, bool water)
            {
                var key = new SampleKey(position, water);
                if (cache.TryGetValue(key, out SurfaceSample sample)) return sample;
                if (water)
                {
                    sample = new SurfaceSample(waterHeight, Vector3.up);
                    cache.Add(key, sample);
                    return sample;
                }

                int rowGuess = Mathf.RoundToInt(position.y / 1.5f);
                int colGuess = Mathf.RoundToInt(position.x / Mathf.Sqrt(3f) -
                                                ((rowGuess & 1) == 1 ? 0.5f : 0f));
                int nearest = NearestLandCell(position, colGuess, rowGuess);
                if (nearest >= 0 && plateauMask[nearest])
                {
                    sample = new SurfaceSample(PlateauLayerHeight(map.cells[nearest]), Vector3.up);
                    cache.Add(key, sample);
                    return sample;
                }

                float height = SampleLandHeight(position);
                float left = SampleLandHeight(position + Vector2.left * NormalSampleDistance);
                float right = SampleLandHeight(position + Vector2.right * NormalSampleDistance);
                float down = SampleLandHeight(position + Vector2.down * NormalSampleDistance);
                float up = SampleLandHeight(position + Vector2.up * NormalSampleDistance);
                float dx = (right - left) / (NormalSampleDistance * 2f);
                float dz = (up - down) / (NormalSampleDistance * 2f);
                sample = new SurfaceSample(height, new Vector3(-dx, 1f, -dz).normalized);
                cache.Add(key, sample);
                return sample;
            }

            public float HeightAt(Vector2 position, bool water)
            {
                if (!water)
                {
                    int rowGuess = Mathf.RoundToInt(position.y / 1.5f);
                    int colGuess = Mathf.RoundToInt(position.x / Mathf.Sqrt(3f) -
                                                    ((rowGuess & 1) == 1 ? 0.5f : 0f));
                    int nearest = NearestLandCell(position, colGuess, rowGuess);
                    if (nearest >= 0 && plateauMask[nearest])
                        return PlateauLayerHeight(map.cells[nearest]);
                }
                return Sample(position, water).height;
            }

            /// <summary>山体平顶与山内低地遮罩格使用数据层高度，保证台阶顶面平行。</summary>
            public float PlateauLayerHeight(WorldCell cell)
            {
                if (cell == null || IsWater(cell)) return 0f;
                return RawLandHeight(map, cell, appearance);
            }

            public bool IsPlateau(int index) =>
                index >= 0 && index < plateauMask.Length && plateauMask[index];


            public float[] CopyCenterHeights()
            {
                var result = new float[sourceHeights.Length];
                for (int index = 0; index < result.Length; index++)
                {
                    WorldCell cell = map.cells[index];
                    if (cell == null) continue;
                    Vector2 center = TerrainMeshGenerator.HexCenter(cell.coord);
                    result[index] = plateauMask[index]
                        ? PlateauLayerHeight(cell)
                        : Sample(center, IsWater(cell)).height;
                }
                return result;
            }

            private float SampleLandHeight(Vector2 position)
            {
                int rowGuess = Mathf.RoundToInt(position.y / 1.5f);
                int colGuess = Mathf.RoundToInt(position.x / Mathf.Sqrt(3f) -
                                                ((rowGuess & 1) == 1 ? 0.5f : 0f));
                int anchor = NearestLandCell(position, colGuess, rowGuess);
                float weightedHeight = 0f;
                float weightedAmplitude = 0f;
                float totalWeight = 0f;
                for (int row = rowGuess - SearchRadius; row <= rowGuess + SearchRadius; row++)
                {
                    for (int col = colGuess - SearchRadius; col <= colGuess + SearchRadius; col++)
                    {
                        int index = map.GetIndex(new HexCoord(col, row));
                        if (index < 0 || index >= sourceHeights.Length) continue;
                        WorldCell cell = map.cells[index];
                        if (cell == null || IsWater(cell)) continue;
                        Vector2 center = TerrainMeshGenerator.HexCenter(cell.coord);
                        float distanceSquared = (position - center).sqrMagnitude;
                        if (distanceSquared > 12f) continue;
                        float weight = Mathf.Exp(-distanceSquared * 1.35f);
                        weight *= SkeletonBlendWeight(anchor, index);
                        // 阶梯山体：非脊/峰/山口的山体平台格只与同层格混合，
                        // 避免高斯插值把量化台阶重新抹成连续陡坡。
                        if (preserveMountainSkeleton && anchor >= 0 && anchor < sourceHeights.Length &&
                            index >= 0 && index < sourceHeights.Length &&
                            map.cells[anchor].landform == LandformType.Mountain &&
                            map.cells[index].landform == LandformType.Mountain &&
                            !ridgeCore[anchor] && !peaks[anchor] && !passes[anchor] &&
                            Mathf.Abs(sourceHeights[index] - sourceHeights[anchor]) > 0.02f)
                        {
                            weight *= 0.03f;
                        }
                        weightedHeight += sourceHeights[index] * weight;
                        weightedAmplitude += detailAmplitudes[index] * weight;
                        totalWeight += weight;
                    }
                }

                if (totalWeight <= 0.0001f) return TerrainMeshGenerator.LandStrategicHeight;
                float baseHeight = weightedHeight / totalWeight;
                float amplitude = weightedAmplitude / totalWeight;
                return baseHeight + DetailNoise(position) * amplitude;
            }

            private int NearestLandCell(Vector2 position, int colGuess, int rowGuess)
            {
                int nearest = -1;
                float nearestDistance = float.MaxValue;
                for (int row = rowGuess - 1; row <= rowGuess + 1; row++)
                for (int col = colGuess - 1; col <= colGuess + 1; col++)
                {
                    int index = map.GetIndex(new HexCoord(col, row));
                    if (index < 0 || index >= sourceHeights.Length || IsWater(map.cells[index])) continue;
                    float distance = (position - TerrainMeshGenerator.HexCenter(map.cells[index].coord)).sqrMagnitude;
                    if (distance > nearestDistance ||
                        (Mathf.Approximately(distance, nearestDistance) && index >= nearest)) continue;
                    nearest = index;
                    nearestDistance = distance;
                }
                return nearest;
            }

            private float SkeletonBlendWeight(int anchor, int source)
            {
                if (!preserveMountainSkeleton || anchor < 0 || source == anchor) return 1f;
                if (peaks[anchor])
                {
                    if (peaks[source]) return 0.85f;
                    if (ridgeCore[source] && !passes[source]) return 0.62f;
                    return 0.06f;
                }
                if (passes[anchor])
                {
                    if (passes[source]) return 0.85f;
                    if (ridgeCore[source]) return 0.07f;
                    return 0.58f;
                }
                if (ridgeCore[anchor])
                    return ridgeCore[source] && !passes[source] ? 0.72f : 0.08f;
                return ridgeCore[source] ? 0.10f : 1f;
            }
        }

        public static BuildResult CreateTerrainChunks(WorldMap map, int chunkSize,
            Func<WorldCell, Color32> colorFor, TerrainMeshAppearance appearance,
            int subdivisions = 2, bool blendLandMaterials = true)
        {
            var meshes = new List<Mesh>();
            if (map?.cells == null || map.cells.Length == 0)
                return new BuildResult(meshes, Array.Empty<float>(), null);
            TerrainMeshAppearance effective = appearance.IsValid
                ? appearance
                : TerrainMeshAppearance.Default;
            var field = new HeightField(map, effective);
            var visualField = new VisualField(map, colorFor, blendLandMaterials);
            int size = Mathf.Max(1, chunkSize);
            int detail = Mathf.Clamp(subdivisions, 1, 4);
            for (int startRow = 0; startRow < map.height; startRow += size)
            {
                for (int startCol = 0; startCol < map.width; startCol += size)
                {
                    int columns = Mathf.Min(size, map.width - startCol);
                    int rows = Mathf.Min(size, map.height - startRow);
                    Mesh mesh = BuildChunk(map, field, visualField, startCol, startRow, columns,
                        rows, colorFor != null, effective.sideDarkenFactor, detail,
                        blendLandMaterials);
                    mesh.name = $"ContinuousTerrainChunk_{startCol}_{startRow}";
                    meshes.Add(mesh);
                }
            }
            return new BuildResult(meshes, field.CopyCenterHeights(), field.HeightAt);
        }

        private static Mesh BuildChunk(WorldMap map, HeightField field, VisualField visualField,
            int startCol, int startRow, int columns, int rows, bool writeColors,
            float sideDarkenFactor, int subdivisions, bool blendLandMaterials)
        {
            var mesh = new Mesh
            {
                name = "ContinuousTerrainChunk",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                subMeshCount = TerrainMeshGenerator.SubmeshCount
            };
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color32>();
            var materialWeights = new List<Vector4>();
            var climateData = new List<Vector2>();
            var triangles = new List<int>[TerrainMeshGenerator.SubmeshCount];
            for (int submesh = 0; submesh < triangles.Length; submesh++)
                triangles[submesh] = new List<int>();
            var sharedSurfaceVertices = new Dictionary<SampleKey, int>();

            for (int row = startRow; row < startRow + rows; row++)
            {
                for (int col = startCol; col < startCol + columns; col++)
                {
                    WorldCell cell = map.cells[row * map.width + col];
                    if (cell == null) continue;
                    int surfaceSubmesh = blendLandMaterials && !IsWater(cell)
                        ? TerrainMeshGenerator.PlainSubmesh
                        : TerrainMeshGenerator.SurfaceSubmeshFor(cell);
                    AddCell(map, field, visualField, cell, vertices, normals, colors,
                        materialWeights, climateData, triangles[surfaceSubmesh], writeColors,
                        sideDarkenFactor, subdivisions, sharedSurfaceVertices);
                }
            }

            if (vertices.Count == 0) return mesh;
            mesh.SetVertices(vertices);
            var uvs = new List<Vector2>(vertices.Count);
            foreach (Vector3 vertex in vertices) uvs.Add(new Vector2(vertex.x, vertex.z));
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, materialWeights);
            mesh.SetUVs(2, climateData);
            if (writeColors) mesh.SetColors(colors);
            for (int submesh = 0; submesh < triangles.Length; submesh++)
                mesh.SetTriangles(triangles[submesh], submesh);
            // 法线在全部顶点/三角形/UV 就绪后由 Mesh 重算，
            // 保证山体侧壁与台地顶面的光照来自真实几何 winding。
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddCell(WorldMap map, HeightField field, VisualField visualField,
            WorldCell cell, List<Vector3> vertices, List<Vector3> normals, List<Color32> colors,
            List<Vector4> materialWeights, List<Vector2> climateData, List<int> triangles,
            bool writeColors,
            float sideDarkenFactor, int subdivisions,
            Dictionary<SampleKey, int> sharedSurfaceVertices)
        {
            bool water = IsWater(cell);
            // 用户定稿方向2：所有山体遮罩格（Mountain + 被山体围住的内陆低地）
            // 都用平顶，层间由随机角度侧壁构成“乐高山”；遮罩外保持连续地表。
            bool flatCell = !water && field.IsPlateau(cell.index);
            Vector2 center = TerrainMeshGenerator.HexCenter(cell.coord);
            float flatTopHeight = flatCell ? field.PlateauLayerHeight(cell) : 0f;
            // 连续地表跨格共享角点，消除邻格浮点采样差异造成的裂缝；
            // Mountain 平顶仍使用局部顶点，以保留台阶侧壁。
            var localVertices = flatCell
                ? new Dictionary<SampleKey, int>()
                : sharedSurfaceVertices;

            for (int sector = 0; sector < 6; sector++)
            {
                Vector2 next = Corner(center, sector + 1);
                Vector2 current = Corner(center, sector);
                for (int first = 0; first < subdivisions; first++)
                {
                    for (int second = 0; second < subdivisions - first; second++)
                    {
                        int a = AddSurfaceVertex(center, next, current, first, second, subdivisions,
                            cell, water, visualField, field, vertices, normals, colors,
                            materialWeights, climateData, writeColors, localVertices, flatCell, flatTopHeight);
                        int b = AddSurfaceVertex(center, next, current, first + 1, second, subdivisions,
                            cell, water, visualField, field, vertices, normals, colors,
                            materialWeights, climateData, writeColors, localVertices, flatCell, flatTopHeight);
                        int c = AddSurfaceVertex(center, next, current, first, second + 1, subdivisions,
                            cell, water, visualField, field, vertices, normals, colors,
                            materialWeights, climateData, writeColors, localVertices, flatCell, flatTopHeight);
                        triangles.Add(a);
                        triangles.Add(b);
                        triangles.Add(c);

                        if (first + second > subdivisions - 2) continue;
                        int d = AddSurfaceVertex(center, next, current, first + 1, second + 1,
                            subdivisions, cell, water, visualField, field, vertices, normals,
                            colors, materialWeights, climateData, writeColors, localVertices, flatCell, flatTopHeight);
                        triangles.Add(b);
                        triangles.Add(d);
                        triangles.Add(c);
                    }
                }
            }

            if (water) return;
            if (!flatCell)
            {
                // 非山体保持连续地表；只给水面边补裙边。
                for (int edge = 0; edge < 6; edge++)
                {
                    int neighborIndex = map.GetIndex(map.GetNeighbor(cell.coord, edge));
                    WorldCell neighbor = neighborIndex >= 0 && neighborIndex < map.cells.Length
                        ? map.cells[neighborIndex]
                        : null;
                    if (neighbor != null && !IsWater(neighbor)) continue;
                    Vector2 from = Corner(center, edge);
                    Vector2 to = Corner(center, edge + 1);
                    float bottom = neighbor != null
                        ? field.Sample((from + to) * 0.5f, true).height
                        : 0f;
                    for (int segment = 0; segment < subdivisions; segment++)
                    {
                        Vector2 topA = Vector2.Lerp(from, to, segment / (float)subdivisions);
                        Vector2 topB = Vector2.Lerp(from, to, (segment + 1f) / subdivisions);
                        AddSkirt(field, visualField, cell, topA, topB, bottom, vertices, normals,
                            colors, materialWeights, climateData, triangles, writeColors,
                            sideDarkenFactor);
                    }
                }
                return;
            }

            // 山体格平顶：每个 Mountain 格使用单一高度，与相邻格的高度差走垂直侧壁。
            for (int edge = 0; edge < 6; edge++)
            {
                int neighborIndex = map.GetIndex(map.GetNeighbor(cell.coord, edge));
                WorldCell neighbor = neighborIndex >= 0 && neighborIndex < map.cells.Length
                    ? map.cells[neighborIndex]
                    : null;
                Vector2 from = Corner(center, edge);
                Vector2 to = Corner(center, edge + 1);
                if (neighbor == null || IsWater(neighbor))
                {
                    AddVerticalSide(visualField, field, cell, neighbor, subdivisions, center,
                        from, to, flatTopHeight, vertices, normals, colors, materialWeights,
                        climateData, triangles, writeColors, sideDarkenFactor);
                    continue;
                }
                float fromTop = SideBottomHeight(field, neighbor, from);
                float toTop = SideBottomHeight(field, neighbor, to);
                float maximumNeighborTop = Mathf.Max(fromTop, toTop);
                float minimumNeighborTop = Mathf.Min(fromTop, toTop);
                if (field.IsPlateau(neighbor.index))
                {
                    // 平顶-平顶相邻只允许较高格生成向下的侧壁；
                    // 较低格不得再生成反向侧壁，避免同一竖直面重复绘制。
                    if (flatTopHeight > maximumNeighborTop + 0.002f)
                    {
                        AddVerticalSide(visualField, field, cell, neighbor, subdivisions, center,
                            from, to, flatTopHeight, vertices, normals, colors, materialWeights,
                            climateData, triangles, writeColors, sideDarkenFactor);
                    }
                    continue;
                }
                if (flatTopHeight > maximumNeighborTop + 0.002f)
                {
                    AddVerticalSide(visualField, field, cell, neighbor, subdivisions, center,
                        from, to, flatTopHeight, vertices, normals, colors, materialWeights,
                        climateData, triangles, writeColors, sideDarkenFactor);
                    continue;
                }
                if (EnableRisingVerticalSides && flatTopHeight < minimumNeighborTop - 0.002f)
                {
                    // 读档/诊断缺失时可能出现邻格连续地表高于 Mountain 平顶。
                    // 反向侧壁：顶边采样邻格地表，底边落在 Mountain 平顶，封住竖直裂缝。
                    AddRisingVerticalSide(visualField, field, cell, neighbor, subdivisions, center,
                        from, to, flatTopHeight, vertices, normals, colors, materialWeights,
                        climateData, triangles, writeColors, sideDarkenFactor);
                }
            }

            // 角点封口：相邻两条侧壁底边在同一个角点可能有不同高度，
            // 用竖直三角面连接 top / 前一边底点 / 后一边底点，消除山角黑缝。
            if (EnableCornerClosures)
            {
                for (int cornerIndex = 0; cornerIndex < 6; cornerIndex++)
                {
                    Vector2 cornerPosition = Corner(center, cornerIndex);
                    int previousEdge = (cornerIndex + 5) % 6;
                    int nextEdge = cornerIndex;
                    WorldCell previousNeighbor = map.GetIndex(map.GetNeighbor(cell.coord, previousEdge)) is int prevIndex &&
                                                 prevIndex >= 0 && prevIndex < map.cells.Length
                        ? map.cells[prevIndex]
                        : null;
                    WorldCell nextNeighbor = map.GetIndex(map.GetNeighbor(cell.coord, nextEdge)) is int nextIndex &&
                                             nextIndex >= 0 && nextIndex < map.cells.Length
                        ? map.cells[nextIndex]
                        : null;
                    float previousBottom = SideBottomHeight(field, previousNeighbor, cornerPosition);
                    float nextBottom = SideBottomHeight(field, nextNeighbor, cornerPosition);
                    if (Mathf.Abs(previousBottom - nextBottom) <= 0.002f) continue;
                    AddCornerClosure(visualField, cell, center, cornerPosition, flatTopHeight,
                        previousBottom, nextBottom, vertices, normals, colors, materialWeights,
                        climateData, triangles, writeColors, sideDarkenFactor);
                }
            }
        }

        private static int AddSurfaceVertex(Vector2 center, Vector2 next, Vector2 current,
            int first, int second, int subdivisions, WorldCell cell, bool water,
            VisualField visualField, HeightField field, List<Vector3> vertices,
            List<Vector3> normals, List<Color32> colors, List<Vector4> materialWeights,
            List<Vector2> climateData, bool writeColors,
            Dictionary<SampleKey, int> localVertices, bool flatCell, float flatTopHeight)
        {
            float firstWeight = first / (float)subdivisions;
            float secondWeight = second / (float)subdivisions;
            Vector2 position = center + (next - center) * firstWeight +
                               (current - center) * secondWeight;
            var key = new SampleKey(position, water);
            if (localVertices.TryGetValue(key, out int existing)) return existing;
            SurfaceSample sample = flatCell
                ? new SurfaceSample(flatTopHeight, Vector3.up)
                : field.Sample(position, water);
            int index = vertices.Count;
            vertices.Add(new Vector3(position.x, sample.height, position.y));
            normals.Add(sample.normal);
            VisualSample visual = visualField.Sample(position, cell);
            if (writeColors)
                colors.Add(flatCell && DebugFaceMode == MountainFaceDebugMode.Top
                    ? DebugFaceColor(DebugFaceMode)
                    : visual.color);
            materialWeights.Add(ApplySlopeMaterial(visual.materialWeights, sample));
            climateData.Add(new Vector2(visual.moisture, 0f));
            localVertices.Add(key, index);
            return index;
        }

        private static Vector4 ApplySlopeMaterial(Vector4 weights, SurfaceSample sample)
        {
            float horizontal = Mathf.Sqrt(sample.normal.x * sample.normal.x +
                                          sample.normal.z * sample.normal.z);
            float gradient = horizontal / Mathf.Max(0.05f, sample.normal.y);
            float steepness = Mathf.Clamp01(Mathf.InverseLerp(0.28f, 0.95f, gradient));
            steepness = steepness * steepness * (3f - 2f * steepness);
            float elevationRock = Mathf.Clamp01(Mathf.InverseLerp(0.62f, 2.45f, sample.height)) * 0.74f;
            float targetRock = Mathf.Max(weights.w, Mathf.Max(steepness * 0.92f, elevationRock));
            if (targetRock <= weights.w + 0.0001f) return weights;

            float nonRock = Mathf.Max(0.0001f, weights.x + weights.y + weights.z);
            float remaining = 1f - targetRock;
            return new Vector4(remaining * weights.x / nonRock,
                remaining * weights.y / nonRock,
                remaining * weights.z / nonRock,
                targetRock);
        }

        private static void AddVerticalSide(VisualField visualField, HeightField field,
            WorldCell cell, WorldCell neighbor, int subdivisions,
            Vector2 cellCenter, Vector2 positionA, Vector2 positionB, float top,
            List<Vector3> vertices, List<Vector3> normals, List<Color32> colors,
            List<Vector4> materialWeights, List<Vector2> climateData, List<int> triangles,
            bool writeColors, float darkenFactor)
        {
            // 侧壁按低层地表实际分段采样建底边：每段底边与相邻格的细分顶点
            // 使用同一 HeightField 采样，避免“中点高度”造成缝隙或低地穿模。
            Vector2 edgeMid = (positionA + positionB) * 0.5f;
            Vector2 outwardXZ = neighbor != null
                ? TerrainMeshGenerator.HexCenter(neighbor.coord) - cellCenter
                : edgeMid - cellCenter;
            if (outwardXZ.sqrMagnitude < 0.0000001f) outwardXZ = edgeMid - cellCenter;
            Vector3 sideNormal = new Vector3(outwardXZ.x, 0f, outwardXZ.y).normalized;
            if (sideNormal.sqrMagnitude < 0.5f) sideNormal = Vector3.down;

            VisualSample visual = visualField.Sample(edgeMid, cell);
            float wallShade = 0.88f + 0.22f * Mathf.Clamp01(
                Mathf.PerlinNoise(positionA.x * 1.7f + 13.1f,
                    positionA.y * 1.7f - 7.3f));
            float factor = darkenFactor * wallShade;
            for (int segment = 0; segment < subdivisions; segment++)
            {
                Vector2 segmentA = Vector2.Lerp(positionA, positionB,
                    segment / (float)subdivisions);
                Vector2 segmentB = Vector2.Lerp(positionA, positionB,
                    (segment + 1f) / (float)subdivisions);
                float bottomA = SideBottomHeight(field, neighbor, segmentA);
                float bottomB = SideBottomHeight(field, neighbor, segmentB);

                Vector3 topA = new Vector3(segmentA.x, top, segmentA.y);
                Vector3 topB = new Vector3(segmentB.x, top, segmentB.y);
                Vector3 bottomPointA = new Vector3(segmentA.x, bottomA, segmentA.y);
                Vector3 bottomPointB = new Vector3(segmentB.x, bottomB, segmentB.y);
                int start = vertices.Count;
                vertices.Add(topA);
                vertices.Add(topB);
                vertices.Add(bottomPointA);
                vertices.Add(bottomPointB);
                for (int index = 0; index < 4; index++) normals.Add(sideNormal);
                for (int index = 0; index < 4; index++)
                    materialWeights.Add(new Vector4(0f, 0f, 0f, 1f));
                for (int index = 0; index < 4; index++)
                    climateData.Add(new Vector2(visual.moisture, 0f));
                if (writeColors)
                {
                    Color32 side = visual.color;
                    side.r = (byte)Mathf.RoundToInt(side.r * factor);
                    side.g = (byte)Mathf.RoundToInt(side.g * factor);
                    side.b = (byte)Mathf.RoundToInt(side.b * factor);
                    if (DebugFaceMode == MountainFaceDebugMode.Side ||
                        DebugFaceMode == MountainFaceDebugMode.RisingSide)
                        side = DebugFaceColor(DebugFaceMode);
                    for (int index = 0; index < 4; index++) colors.Add(side);
                }
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start + 1);
                triangles.Add(start + 3);
                triangles.Add(start + 2);
            }
        }

        private static float SideBottomHeight(HeightField field, WorldCell neighbor, Vector2 position)
        {
            if (neighbor == null) return 0f;
            if (field.IsPlateau(neighbor.index))
                return field.PlateauLayerHeight(neighbor);
            return field.Sample(position, IsWater(neighbor)).height;
        }

        private static void AddRisingVerticalSide(VisualField visualField, HeightField field,
            WorldCell cell, WorldCell neighbor, int subdivisions,
            Vector2 cellCenter, Vector2 positionA, Vector2 positionB, float mountainTop,
            List<Vector3> vertices, List<Vector3> normals, List<Color32> colors,
            List<Vector4> materialWeights, List<Vector2> climateData, List<int> triangles,
            bool writeColors, float darkenFactor)
        {
            // 邻格连续地表高于 Mountain 平顶：顶边逐点采样邻格地表，
            // 底边落在当前 Mountain 平顶，闭合反向竖直面。
            Vector2 edgeMid = (positionA + positionB) * 0.5f;
            Vector2 outwardXZ = TerrainMeshGenerator.HexCenter(neighbor.coord) - cellCenter;
            if (outwardXZ.sqrMagnitude < 0.0000001f) outwardXZ = edgeMid - cellCenter;
            Vector3 sideNormal = new Vector3(outwardXZ.x, 0f, outwardXZ.y).normalized;
            if (sideNormal.sqrMagnitude < 0.5f) sideNormal = Vector3.down;

            VisualSample visual = visualField.Sample(edgeMid, cell);
            float wallShade = 0.88f + 0.22f * Mathf.Clamp01(
                Mathf.PerlinNoise(positionA.x * 1.7f + 13.1f,
                    positionA.y * 1.7f - 7.3f));
            float factor = darkenFactor * wallShade;
            for (int segment = 0; segment < subdivisions; segment++)
            {
                Vector2 segmentA = Vector2.Lerp(positionA, positionB,
                    segment / (float)subdivisions);
                Vector2 segmentB = Vector2.Lerp(positionA, positionB,
                    (segment + 1f) / (float)subdivisions);
                float topA = SideBottomHeight(field, neighbor, segmentA);
                float topB = SideBottomHeight(field, neighbor, segmentB);

                Vector3 topPointA = new Vector3(segmentA.x, topA, segmentA.y);
                Vector3 topPointB = new Vector3(segmentB.x, topB, segmentB.y);
                Vector3 bottomPointA = new Vector3(segmentA.x, mountainTop, segmentA.y);
                Vector3 bottomPointB = new Vector3(segmentB.x, mountainTop, segmentB.y);
                int start = vertices.Count;
                vertices.Add(topPointA);
                vertices.Add(topPointB);
                vertices.Add(bottomPointA);
                vertices.Add(bottomPointB);
                for (int index = 0; index < 4; index++) normals.Add(sideNormal);
                for (int index = 0; index < 4; index++)
                    materialWeights.Add(new Vector4(0f, 0f, 0f, 1f));
                for (int index = 0; index < 4; index++)
                    climateData.Add(new Vector2(visual.moisture, 0f));
                if (writeColors)
                {
                    Color32 side = visual.color;
                    side.r = (byte)Mathf.RoundToInt(side.r * factor);
                    side.g = (byte)Mathf.RoundToInt(side.g * factor);
                    side.b = (byte)Mathf.RoundToInt(side.b * factor);
                    if (DebugFaceMode == MountainFaceDebugMode.Side ||
                        DebugFaceMode == MountainFaceDebugMode.RisingSide)
                        side = DebugFaceColor(DebugFaceMode);
                    for (int index = 0; index < 4; index++) colors.Add(side);
                }
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start + 1);
                triangles.Add(start + 3);
                triangles.Add(start + 2);
            }
        }

        private static void AddCornerClosure(VisualField visualField, WorldCell cell,
            Vector2 cellCenter, Vector2 cornerPosition, float mountainTop,
            float previousBottom, float nextBottom,
            List<Vector3> vertices, List<Vector3> normals, List<Color32> colors,
            List<Vector4> materialWeights, List<Vector2> climateData, List<int> triangles,
            bool writeColors, float darkenFactor)
        {
            Vector3 top = new Vector3(cornerPosition.x, mountainTop, cornerPosition.y);
            Vector3 previous = new Vector3(cornerPosition.x, previousBottom, cornerPosition.y);
            Vector3 next = new Vector3(cornerPosition.x, nextBottom, cornerPosition.y);
            Vector3 cornerNormal = new Vector3(cornerPosition.x - cellCenter.x, 0f,
                cornerPosition.y - cellCenter.y).normalized;
            if (cornerNormal.sqrMagnitude < 0.5f) cornerNormal = Vector3.down;
            Vector3 faceNormal = Vector3.Cross(previous - top, next - top);
            int firstOrder = 1;
            int secondOrder = 2;
            if (Vector3.Dot(faceNormal, cornerNormal) < 0f)
            {
                firstOrder = 2;
                secondOrder = 1;
            }

            int start = vertices.Count;
            vertices.Add(top);
            vertices.Add(previous);
            vertices.Add(next);
            for (int index = 0; index < 3; index++) normals.Add(cornerNormal);
            VisualSample visual = visualField.Sample(cornerPosition, cell);
            for (int index = 0; index < 3; index++)
                materialWeights.Add(new Vector4(0f, 0f, 0f, 1f));
            for (int index = 0; index < 3; index++)
                climateData.Add(new Vector2(visual.moisture, 0f));
            if (writeColors)
            {
                Color32 side = visual.color;
                side.r = (byte)Mathf.RoundToInt(side.r * darkenFactor);
                side.g = (byte)Mathf.RoundToInt(side.g * darkenFactor);
                side.b = (byte)Mathf.RoundToInt(side.b * darkenFactor);
                if (DebugFaceMode == MountainFaceDebugMode.CornerClosure)
                    side = DebugFaceColor(DebugFaceMode);
                for (int index = 0; index < 3; index++) colors.Add(side);
            }
            triangles.Add(start);
            triangles.Add(start + firstOrder);
            triangles.Add(start + secondOrder);
        }

        private static void AddSkirt(HeightField field, VisualField visualField, WorldCell cell,            Vector2 positionA, Vector2 positionB, float bottom, List<Vector3> vertices,
            List<Vector3> normals, List<Color32> colors, List<Vector4> materialWeights,
            List<Vector2> climateData, List<int> triangles, bool writeColors,
            float darkenFactor)
        {
            SurfaceSample sampleA = field.Sample(positionA, false);
            SurfaceSample sampleB = field.Sample(positionB, false);
            Vector3 topA = new Vector3(positionA.x, sampleA.height, positionA.y);
            Vector3 topB = new Vector3(positionB.x, sampleB.height, positionB.y);
            Vector2 center = TerrainMeshGenerator.HexCenter(cell.coord);
            Vector2 outward = (positionA + positionB) * 0.5f - center;
            Vector3 sideNormal = new Vector3(outward.x, 0f, outward.y).normalized;
            if (sideNormal.sqrMagnitude < 0.5f) sideNormal = Vector3.down;
            int start = vertices.Count;
            vertices.Add(topA);
            vertices.Add(topB);
            vertices.Add(new Vector3(topA.x, bottom, topA.z));
            vertices.Add(new Vector3(topB.x, bottom, topB.z));
            for (int index = 0; index < 4; index++) normals.Add(sideNormal);
            VisualSample visual = visualField.Sample((positionA + positionB) * 0.5f, cell);
            for (int index = 0; index < 4; index++) materialWeights.Add(visual.materialWeights);
            for (int index = 0; index < 4; index++)
                climateData.Add(new Vector2(visual.moisture, 0f));
            if (writeColors)
            {
                Color32 side = visual.color;
                side.r = (byte)Mathf.RoundToInt(side.r * darkenFactor);
                side.g = (byte)Mathf.RoundToInt(side.g * darkenFactor);
                side.b = (byte)Mathf.RoundToInt(side.b * darkenFactor);
                for (int index = 0; index < 4; index++) colors.Add(side);
            }
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
        }

        private static Vector2 Corner(Vector2 center, int corner) =>
            HexGeometry.GetCorners(center)[corner % 6];

        private static float RawLandHeight(WorldMap map, WorldCell cell,
            TerrainMeshAppearance appearance)
        {
            TerrainGenerationParameters terrain = map?.generationSettings?.terrain ??
                                                  new TerrainGenerationParameters();
            float dataHeight = Mathf.Clamp01(cell.height);
            if (dataHeight < terrain.seaLevel)
            {
                // Hand-authored test maps and legacy callers sometimes omit data height. Keep
                // them on land without hiding valid generated heights.
                switch (cell.landform)
                {
                    case LandformType.Mountain:
                        dataHeight = 0.86f;
                        break;
                    case LandformType.Hill:
                        dataHeight = (terrain.plainUpperThreshold + terrain.hillUpperThreshold) * 0.5f;
                        break;
                    default:
                        dataHeight = (terrain.seaLevel + terrain.plainUpperThreshold) * 0.5f;
                        break;
                }
            }

            float scale = Mathf.Max(0.1f, appearance.heightScale);
            if (dataHeight <= terrain.plainUpperThreshold)
            {
                float t = Mathf.InverseLerp(terrain.seaLevel, terrain.plainUpperThreshold, dataHeight);
                t = t * t * (3f - 2f * t);
                return TerrainMeshGenerator.LandStrategicHeight + t * 0.08f;
            }
            if (dataHeight <= terrain.hillUpperThreshold)
            {
                float t = Mathf.InverseLerp(terrain.plainUpperThreshold,
                    terrain.hillUpperThreshold, dataHeight);
                t = t * t * (3f - 2f * t);
                return TerrainMeshGenerator.LandStrategicHeight + 0.08f + t * 0.32f * scale;
            }

            float mountain = Mathf.InverseLerp(terrain.hillUpperThreshold, 1f, dataHeight);
            mountain = Mathf.Pow(mountain, 1.25f);
            return TerrainMeshGenerator.LandStrategicHeight + 0.40f * scale +
                   mountain * 3.00f * scale;
        }

        internal static float ApplyMountainVerticalEmphasis(float rawHeight, bool ridge,
            bool peak, bool mountainPass)
        {
            if (mountainPass || (!ridge && !peak)) return rawHeight;

            float relief = Mathf.Max(0f, rawHeight - TerrainMeshGenerator.LandStrategicHeight);
            return TerrainMeshGenerator.LandStrategicHeight + relief * RidgeVerticalMultiplier;
        }

        internal static float ClampPeakToAdjacentRidge(float peakHeight,
            float highestAdjacentRidgeHeight)
        {
            float ground = TerrainMeshGenerator.LandStrategicHeight;
            float peakRelief = Mathf.Max(0f, peakHeight - ground);
            float ridgeRelief = Mathf.Max(0f, highestAdjacentRidgeHeight - ground);
            float maximumPeakRelief = ridgeRelief * PeakToAdjacentRidgeLimit;
            return ground + Mathf.Min(peakRelief, maximumPeakRelief);
        }

        internal static void ApplyOneRingRidgeShoulders(WorldMap map, float[] heights,
            bool[] ridgeCore, bool[] passes)
        {
            int count = map?.cells?.Length ?? 0;
            if (count == 0 || heights == null || ridgeCore == null || passes == null ||
                heights.Length != count || ridgeCore.Length != count || passes.Length != count)
                return;

            float[] widened = (float[])heights.Clone();
            for (int index = 0; index < count; index++)
            {
                WorldCell cell = map.cells[index];
                if (cell == null || IsWater(cell) || ridgeCore[index] || passes[index]) continue;

                int[] neighbors = map.GetNeighborIndices(index).ToArray();
                if (neighbors.Any(neighbor => passes[neighbor])) continue;

                float highestAdjacentRidge = float.NegativeInfinity;
                foreach (int neighbor in neighbors)
                {
                    if (!ridgeCore[neighbor] || passes[neighbor]) continue;
                    highestAdjacentRidge = Mathf.Max(highestAdjacentRidge, heights[neighbor]);
                }
                if (float.IsNegativeInfinity(highestAdjacentRidge) ||
                    highestAdjacentRidge <= heights[index]) continue;

                widened[index] = heights[index] * (1f - RidgeShoulderBlend) +
                                 highestAdjacentRidge * RidgeShoulderBlend;
            }
            Array.Copy(widened, heights, count);
        }

        internal static void ApplyMountainTerraceFlattening(WorldMap map, float[] heights,
            float[] detailAmplitudes)
        {
            int count = map?.cells?.Length ?? 0;
            if (count == 0 || heights == null || detailAmplitudes == null ||
                heights.Length != count || detailAmplitudes.Length != count)
                return;

            var remaining = new HashSet<int>(map.cells
                .Where(cell => cell != null && cell.landform == LandformType.Mountain && cell.isBuildable)
                .Select(cell => cell.index));
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
                if (component.Count < 2) continue;
                float terraceHeight = component.Average(index => heights[index]);
                foreach (int index in component)
                {
                    heights[index] = terraceHeight;
                    detailAmplitudes[index] = Mathf.Min(detailAmplitudes[index], 0.003f);
                }
            }
        }

        private static float DetailAmplitude(WorldCell cell)
        {
            switch (cell.landform)
            {
                case LandformType.Mountain: return 0.018f;
                case LandformType.Hill: return 0.012f;
                case LandformType.Coast: return 0.006f;
                default: return 0.008f;
            }
        }

        private static float DetailNoise(Vector2 position)
        {
            float broad = ValueNoise(position * 0.16f + new Vector2(13.7f, 4.3f));
            float medium = ValueNoise(position * 0.31f + new Vector2(2.1f, 19.4f));
            return (broad * 0.68f + medium * 0.32f - 0.5f) * 2f;
        }

        private static float ValueNoise(Vector2 position)
        {
            Vector2 cell = new Vector2(Mathf.Floor(position.x), Mathf.Floor(position.y));
            Vector2 local = new Vector2(position.x - cell.x, position.y - cell.y);
            local = new Vector2(local.x * local.x * (3f - 2f * local.x),
                local.y * local.y * (3f - 2f * local.y));
            float a = Hash(cell);
            float b = Hash(cell + Vector2.right);
            float c = Hash(cell + Vector2.up);
            float d = Hash(cell + Vector2.one);
            return Mathf.Lerp(Mathf.Lerp(a, b, local.x), Mathf.Lerp(c, d, local.x), local.y);
        }

        private static float Hash(Vector2 position)
        {
            float value = Mathf.Sin(Vector2.Dot(position, new Vector2(127.1f, 311.7f))) * 43758.5453f;
            return value - Mathf.Floor(value);
        }

        private static bool IsWater(WorldCell cell) => cell != null &&
            (cell.landform == LandformType.DeepWater || cell.landform == LandformType.ShallowWater);
    }
}
