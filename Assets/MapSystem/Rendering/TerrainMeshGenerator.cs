using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 地形网格的表现参数。heightScale 仅为旧连续高度接口保留；战略扁平地表
    /// 不再使用它抬高陆地。sideDarkenFactor 继续控制海岸与地图外缘收边。
    /// </summary>
    public struct TerrainMeshAppearance
    {
        public float heightScale;
        public float sideDarkenFactor;

        public static TerrainMeshAppearance Default => new TerrainMeshAppearance
        {
            heightScale = 1f,
            sideDarkenFactor = 0.85f
        };

        public bool IsValid => heightScale > 0f &&
                               sideDarkenFactor >= 0f &&
                               sideDarkenFactor <= 1f;
    }

    /// <summary>
    /// 根据 WorldMap 生成共面陆地的战略六边形地表网格。
    /// 支持整张地图单 chunk 与按固定尺寸分块两种输出，
    /// 分块逻辑为以后做剔除、LOD 等预留扩展点。
    /// </summary>
    public static class TerrainMeshGenerator
    {
        public const int SubmeshCount = 5;
        public const int WaterSubmesh = 0;
        public const int CoastSubmesh = 1;
        public const int PlainSubmesh = 2;
        public const int HillSubmesh = 3;
        public const int MountainSubmesh = 4;

        /// <summary>六边形外接圆直径（世界单位）。唯一实现见 HexGeometryService。</summary>
        public const float HexDiameter = HexGeometry.Diameter;
        private const float WaterBaseHeight = 0.04f;
        private const float WaterTopHeight = 0.12f;
        public const float DeepWaterStrategicHeight = 0.02f;
        public const float ShallowWaterStrategicHeight = 0.05f;
        public const float LandStrategicHeight = 0.10f;
        private const float LandRiseBase = 0.40f;
        private const float LandRiseExponent = 1.5f;
        private const float LandHeightScale = 12f;
        private const float LandHeightFloor = 0.15f;

        private sealed class TerrainBuildContext
        {
            public readonly WorldMap map;
            public readonly float[] visualHeights;
            public readonly bool[] macroCells;

            public TerrainBuildContext(WorldMap map)
            {
                this.map = map;
                int count = map?.cells?.Length ?? 0;
                visualHeights = new float[count];
                macroCells = new bool[count];
                if (count == 0) return;

                float[] rawHeights = new float[count];
                for (int index = 0; index < count; index++)
                {
                    WorldCell cell = map.cells[index];
                    if (cell == null) continue;
                    rawHeights[index] = StrategicSurfaceHeight(cell);
                    macroCells[index] = false;
                }

                for (int index = 0; index < count; index++)
                {
                    if (!macroCells[index])
                    {
                        visualHeights[index] = rawHeights[index];
                        continue;
                    }

                    float total = rawHeights[index] * 2f;
                    int weight = 2;
                    foreach (int neighbor in map.GetNeighborIndices(index))
                    {
                        if (!macroCells[neighbor]) continue;
                        total += rawHeights[neighbor];
                        weight++;
                    }
                    visualHeights[index] = total / weight;
                }
            }

            public float Height(int index) =>
                index >= 0 && index < visualHeights.Length ? visualHeights[index] : 0f;

            public bool IsMacro(int index) =>
                index >= 0 && index < macroCells.Length && macroCells[index];

            public float CornerHeight(int cellIndex, int corner)
            {
                CornerContributors(cellIndex, corner, out int first, out int second,
                    out int third, out int count);
                if (count == 0) return Height(cellIndex);
                float total = Height(first);
                if (count > 1) total += Height(second);
                if (count > 2) total += Height(third);
                return total / count;
            }

            public Color32? CornerColor(int cellIndex, int corner, Func<WorldCell, Color32> colorFor)
            {
                if (colorFor == null) return null;
                CornerContributors(cellIndex, corner, out int first, out int second,
                    out int third, out int count);
                if (count == 0) return colorFor(map.cells[cellIndex]);
                int red = 0;
                int green = 0;
                int blue = 0;
                int alpha = 0;
                for (int position = 0; position < count; position++)
                {
                    int contributor = position == 0 ? first : position == 1 ? second : third;
                    Color32 color = colorFor(map.cells[contributor]);
                    red += color.r;
                    green += color.g;
                    blue += color.b;
                    alpha += color.a;
                }
                return new Color32((byte)(red / count), (byte)(green / count),
                    (byte)(blue / count), (byte)(alpha / count));
            }

            private void CornerContributors(int cellIndex, int corner, out int first,
                out int second, out int third, out int count)
            {
                first = -1;
                second = -1;
                third = -1;
                count = 0;
                if (map?.cells == null || cellIndex < 0 || cellIndex >= map.cells.Length ||
                    map.cells[cellIndex] == null) return;
                first = cellIndex;
                count = 1;
                HexCoord coord = map.cells[cellIndex].coord;
                int neighborA = map.GetIndex(map.GetNeighbor(coord, corner));
                int neighborB = map.GetIndex(map.GetNeighbor(coord, corner + 5));
                if (neighborA >= 0)
                {
                    second = neighborA;
                    count = 2;
                }
                if (neighborB >= 0 && neighborB != neighborA)
                {
                    if (count == 1) second = neighborB;
                    else third = neighborB;
                    count++;
                }
            }
        }

        private readonly struct VertexKey : IEquatable<VertexKey>
        {
            private const float Precision = 10000f;
            private readonly int x;
            private readonly int z;

            public VertexKey(float x, float z)
            {
                this.x = Mathf.RoundToInt(x * Precision);
                this.z = Mathf.RoundToInt(z * Precision);
            }

            public bool Equals(VertexKey other) => x == other.x && z == other.z;
            public override bool Equals(object obj) => obj is VertexKey other && Equals(other);
            public override int GetHashCode() => unchecked((x * 397) ^ z);
        }

        /// <summary>旧版独立山峰比例，保留常量仅避免破坏已有调用；连续地形不再使用。</summary>
        public const float MountainPeakHeightRatio = 1.3f;

        /// <summary>地类基础高度（扰动前的世界单位高度）。</summary>
        public static float BaseHeight(LandformType landform)
        {
            switch (landform)
            {
                case LandformType.DeepWater: return 0.04f;
                case LandformType.ShallowWater: return 0.10f;
                case LandformType.Coast: return 0.18f;
                case LandformType.Plain: return 0.50f;
                case LandformType.Hill: return 2.20f;
                case LandformType.Mountain: return 5.00f;
                default: return 0.50f;
            }
        }

        /// <summary>
        /// 连续高度场：按 cell.height 平滑映射，不再按地类跳变。
        /// 水保持低位；陆地用非线性曲线让低地平缓、高地陡升。
        /// </summary>
        public static float TopHeight(WorldCell cell)
        {
            if (cell == null) return 0f;
            float height = Mathf.Clamp01(cell.height);
            if (IsWater(cell.landform))
                return Mathf.Lerp(WaterBaseHeight, WaterTopHeight, height);
            return LandHeightFloor +
                   Mathf.Pow(Mathf.Max(0f, height - LandRiseBase), LandRiseExponent) * LandHeightScale;
        }

        /// <summary>顶面中心高度。连续山体不再为每个山地 Hex 单独制造尖顶。</summary>
        public static float TopPeakHeight(WorldCell cell)
        {
            return TopHeight(cell);
        }

        /// <summary>
        /// 战略地图实际渲染表面。所有陆地共面，数据高度不再直接抬高 Hex；
        /// 水体只保留很小的层级差，用于岸线识别。
        /// </summary>
        public static float StrategicSurfaceHeight(LandformType landform)
        {
            switch (landform)
            {
                case LandformType.DeepWater: return DeepWaterStrategicHeight;
                case LandformType.ShallowWater: return ShallowWaterStrategicHeight;
                default: return LandStrategicHeight;
            }
        }

        public static float StrategicSurfaceHeight(WorldCell cell) =>
            cell == null ? 0f : StrategicSurfaceHeight(cell.landform);

        /// <summary>
        /// Region 级山脉、丘陵与山谷由独立合并模型表达；基础地表不再承担抬升语义。
        /// 保留公开接口，避免破坏已有诊断与调用方。
        /// </summary>
        public static float RegionUplift(WorldMap map, WorldCell cell)
        {
            return 0f;
        }

        /// <summary>
        /// 宏观地貌已移至 Region 模型层，基础地表不再包含宏观抬升格。
        /// </summary>
        public static bool IsMacroTerrain(WorldMap map, WorldCell cell)
        {
            return false;
        }

        /// <summary>基础地表顶面高度；陆地保持战略共面，宏观地貌由模型覆盖。</summary>
        public static float VisualTopHeight(WorldMap map, WorldCell cell)
        {
            return StrategicSurfaceHeight(cell);
        }

        /// <summary>带宏观区域起伏的顶面中心高度。</summary>
        public static float VisualTopPeakHeight(WorldMap map, WorldCell cell)
        {
            return VisualTopHeight(map, cell);
        }

        /// <summary>区域内最高地形顶面高度，用于区域覆盖层基准高度计算。</summary>
        public static float RegionMaxVisualTopHeight(WorldMap map, MapRegionData region)
        {
            float maximum = 0f;
            if (map?.cells == null || region?.cellIndices == null) return maximum;
            foreach (int index in region.cellIndices)
            {
                if (index < 0 || index >= map.cells.Length) continue;
                WorldCell cell = map.cells[index];
                if (cell == null) continue;
                maximum = Mathf.Max(maximum, VisualTopHeight(map, cell));
            }
            return maximum;
        }

        /// <summary>区域在战略扁平地表上的最高显示高度，供覆盖层对齐。</summary>
        public static float RegionMaxStrategicSurfaceHeight(WorldMap map, MapRegionData region)
        {
            float maximum = 0f;
            if (map?.cells == null || region?.cellIndices == null) return maximum;
            foreach (int index in region.cellIndices)
            {
                if (index < 0 || index >= map.cells.Length) continue;
                maximum = Mathf.Max(maximum, StrategicSurfaceHeight(map.cells[index]));
            }
            return maximum;
        }

        private static bool IsWater(LandformType landform) =>
            landform == LandformType.DeepWater || landform == LandformType.ShallowWater;

        /// <summary>
        /// 旧地貌 → submesh 分组。公开接口保留给现有调用方；实际战略地表生成会进一步
        /// 根据生物群系选择同一组五个纹理槽。
        /// </summary>
        public static int SubmeshFor(LandformType landform)
        {
            switch (landform)
            {
                case LandformType.DeepWater:
                case LandformType.ShallowWater: return WaterSubmesh;
                case LandformType.Coast: return CoastSubmesh;
                case LandformType.Plain: return PlainSubmesh;
                case LandformType.Hill: return HillSubmesh;
                case LandformType.Mountain: return MountainSubmesh;
                default: return PlainSubmesh;
            }
        }

        /// <summary>
        /// 表现层纹理槽映射：地貌负责水面、海岸与实体山体的强制语义，
        /// 其余陆地由生物群系决定草、泥、沙、石纹理。未分类或异常群系回退到旧地貌映射。
        /// </summary>
        internal static int SurfaceSubmeshFor(WorldCell cell)
        {
            if (cell == null) return PlainSubmesh;
            switch (cell.landform)
            {
                case LandformType.DeepWater:
                case LandformType.ShallowWater:
                    return WaterSubmesh;
                case LandformType.Coast:
                    return CoastSubmesh;
                case LandformType.Mountain:
                    return MountainSubmesh;
            }

            switch (cell.biome)
            {
                case BiomeType.Coast:
                case BiomeType.Desert:
                    return CoastSubmesh;
                case BiomeType.Grassland:
                case BiomeType.TemperateForest:
                case BiomeType.Rainforest:
                    return PlainSubmesh;
                case BiomeType.Wetland:
                case BiomeType.Tundra:
                    return HillSubmesh;
                case BiomeType.Snowfield:
                case BiomeType.Alpine:
                    return MountainSubmesh;
                case BiomeType.Ocean:
                default:
                    return SubmeshFor(cell.landform);
            }
        }

        /// <summary>生成整张地图的单 chunk 网格，按五种战略地表纹理槽拆分 submesh。</summary>
        public static Mesh CreateTerrainChunk(WorldMap map) =>
            CreateTerrainChunk(map, null, TerrainMeshAppearance.Default);

        /// <summary>生成整张地图的单 chunk 网格；colorFor 非空时为每个顶点写入颜色。</summary>
        public static Mesh CreateTerrainChunk(WorldMap map, Func<WorldCell, Color32> colorFor)
            => CreateTerrainChunk(map, colorFor, TerrainMeshAppearance.Default);

        /// <summary>按表现参数生成整张地图的单 chunk 网格。</summary>
        public static Mesh CreateTerrainChunk(WorldMap map, Func<WorldCell, Color32> colorFor,
            TerrainMeshAppearance appearance)
        {
            if (map == null || map.cells == null || map.cells.Length == 0)
                return CreateEmptyChunkMesh("TerrainChunk");
            return BuildChunkMesh(map, 0, 0, map.width, map.height, colorFor,
                appearance.IsValid ? appearance : TerrainMeshAppearance.Default);
        }

        /// <summary>
        /// 按 chunkSize 把地图切分为多个 chunk 网格；最后一个 chunk 取剩余行列。
        /// chunkSize 小于等于 0 时按 1 处理。
        /// </summary>
        public static List<Mesh> CreateTerrainChunks(WorldMap map, int chunkSize) =>
            CreateTerrainChunks(map, chunkSize, null, TerrainMeshAppearance.Default);

        /// <summary>分块生成网格；colorFor 非空时为每个顶点写入颜色。</summary>
        public static List<Mesh> CreateTerrainChunks(WorldMap map, int chunkSize, Func<WorldCell, Color32> colorFor)
            => CreateTerrainChunks(map, chunkSize, colorFor, TerrainMeshAppearance.Default);

        /// <summary>按表现参数分块生成网格；colorFor 非空时为每个顶点写入颜色。</summary>
        public static List<Mesh> CreateTerrainChunks(WorldMap map, int chunkSize,
            Func<WorldCell, Color32> colorFor, TerrainMeshAppearance appearance)
        {
            List<Mesh> chunks = new List<Mesh>();
            if (map == null || map.cells == null || map.cells.Length == 0) return chunks;
            TerrainMeshAppearance effective = appearance.IsValid ? appearance : TerrainMeshAppearance.Default;
            int size = Mathf.Max(1, chunkSize);
            for (int startRow = 0; startRow < map.height; startRow += size)
            {
                for (int startCol = 0; startCol < map.width; startCol += size)
                {
                    int columns = Mathf.Min(size, map.width - startCol);
                    int rows = Mathf.Min(size, map.height - startRow);
                    Mesh chunk = BuildChunkMesh(map, startCol, startRow, columns, rows, colorFor, effective);
                    chunk.name = $"TerrainChunk_{startCol}_{startRow}";
                    chunks.Add(chunk);
                }
            }
            return chunks;
        }

        /// <summary>构建地图上 [startCol, startRow) 起、columns×rows 范围内的一个 chunk 网格。</summary>
        private static Mesh BuildChunkMesh(WorldMap map, int startCol, int startRow, int columns, int rows,
            Func<WorldCell, Color32> colorFor, TerrainMeshAppearance appearance)
        {
            Mesh mesh = new Mesh
            {
                name = "TerrainChunk",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            mesh.subMeshCount = SubmeshCount;
            List<Vector3>[] verticesBySubmesh = new List<Vector3>[SubmeshCount];
            List<int>[] trianglesBySubmesh = new List<int>[SubmeshCount];
            List<Color32>[] colorsBySubmesh = new List<Color32>[SubmeshCount];
            for (int submesh = 0; submesh < SubmeshCount; submesh++)
            {
                verticesBySubmesh[submesh] = new List<Vector3>();
                trianglesBySubmesh[submesh] = new List<int>();
                colorsBySubmesh[submesh] = new List<Color32>();
            }

            for (int row = startRow; row < startRow + rows; row++)
            {
                for (int col = startCol; col < startCol + columns; col++)
                {
                    WorldCell cell = map.cells[row * map.width + col];
                    if (cell == null) continue;
                    int submesh = SurfaceSubmeshFor(cell);
                    AddStrategicCellSurface(map, cell, verticesBySubmesh[submesh],
                        trianglesBySubmesh[submesh], colorsBySubmesh[submesh], colorFor,
                        appearance.sideDarkenFactor);
                }
            }

            List<Vector3> allVertices = new List<Vector3>();
            List<Color32> allColors = new List<Color32>();
            List<int[]> offsetTriangles = new List<int[]>();
            bool hasColors = colorFor != null;
            for (int submesh = 0; submesh < SubmeshCount; submesh++)
            {
                List<int> chunkTriangles = trianglesBySubmesh[submesh];
                if (chunkTriangles.Count == 0)
                {
                    offsetTriangles.Add(null);
                    continue;
                }
                int baseIndex = allVertices.Count;
                allVertices.AddRange(verticesBySubmesh[submesh]);
                if (hasColors) allColors.AddRange(colorsBySubmesh[submesh]);
                int[] triangles = new int[chunkTriangles.Count];
                for (int i = 0; i < chunkTriangles.Count; i++)
                    triangles[i] = chunkTriangles[i] + baseIndex;
                offsetTriangles.Add(triangles);
            }

            if (allVertices.Count > 0)
            {
                mesh.SetVertices(allVertices);
                var worldUvs = new List<Vector2>(allVertices.Count);
                foreach (Vector3 vertex in allVertices)
                    worldUvs.Add(new Vector2(vertex.x, vertex.z));
                mesh.SetUVs(0, worldUvs);
                if (hasColors) mesh.SetColors(allColors);
                for (int submesh = 0; submesh < SubmeshCount; submesh++)
                {
                    if (offsetTriangles[submesh] == null) continue;
                    mesh.SetTriangles(offsetTriangles[submesh], submesh);
                }
                mesh.RecalculateNormals();
                if (hasColors) ApplyDirectionalSurfaceLighting(mesh);
                mesh.RecalculateBounds();
            }
            return mesh;
        }

        internal static void ApplyDirectionalSurfaceLighting(Mesh mesh)
        {
            Vector3[] normals = mesh.normals;
            Color32[] colors = mesh.colors32;
            if (normals == null || colors == null || normals.Length != colors.Length) return;
            Vector3 lightDirection = new Vector3(-0.45f, 0.82f, -0.35f).normalized;
            for (int index = 0; index < colors.Length; index++)
            {
                float facing = Mathf.Clamp01(Vector3.Dot(normals[index], lightDirection));
                float factor = Mathf.Lerp(0.76f, 1.08f, facing);
                Color32 color = colors[index];
                color.r = (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * factor), 0, 255);
                color.g = (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * factor), 0, 255);
                color.b = (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * factor), 0, 255);
                colors[index] = color;
            }
            mesh.colors32 = colors;
        }

        private static Mesh CreateEmptyChunkMesh(string name)
        {
            Mesh mesh = new Mesh
            {
                name = name,
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            mesh.subMeshCount = SubmeshCount;
            return mesh;
        }

        /// <summary>六边形中心的 XZ 坐标。唯一实现见 HexGeometryService。</summary>
        public static Vector2 HexCenter(HexCoord coord) => HexGeometry.GetCenter(coord);

        /// <summary>
        /// 六边形沿屏幕水平方向投影后的真实宽度。镜头旋转后不能继续把外接圆直径
        /// 当作屏幕宽度，否则可见格数与玩家实际看到的大小不一致。
        /// </summary>
        internal static float ProjectedHexWidth(Vector3 screenRight)
        {
            Vector2 direction = new Vector2(screenRight.x, screenRight.z);
            if (direction.sqrMagnitude < 0.0001f) return HexDiameter;
            direction.Normalize();
            float extent = 0f;
            foreach (Vector2 vertex in HexGeometry.GetCorners(Vector2.zero, HexGeometry.Radius))
                extent = Mathf.Max(extent, Mathf.Abs(Vector2.Dot(vertex, direction)));
            return extent * 2f;
        }

        /// <summary>
        /// 把连续地形表面的世界坐标还原为原始 Hex 索引。地形只改变 Y，XZ 网格仍由 HexCoord 决定。
        /// </summary>
        public static bool TryGetCellIndex(WorldMap map, Vector3 worldPosition, out int cellIndex) =>
            HexGeometry.TryGetCellIndex(map, worldPosition, out cellIndex);

        private static void AddMacroCellSurface(WorldMap map, TerrainBuildContext context, WorldCell cell,
            List<Vector3> vertices, List<int> triangles, List<Color32> colors,
            Dictionary<VertexKey, int> sharedVertices, Func<WorldCell, Color32> colorFor,
            TerrainMeshAppearance appearance)
        {
            Vector2 center = HexCenter(cell.coord);
            Vector2[] geometryCorners = HexGeometry.GetCorners(center);
            int centerIndex = vertices.Count;
            vertices.Add(new Vector3(center.x, context.Height(cell.index) * appearance.heightScale, center.y));
            if (colorFor != null) colors.Add(colorFor(cell));

            int[] corners = new int[6];
            for (int corner = 0; corner < 6; corner++)
            {
                float y = context.CornerHeight(cell.index, corner) * appearance.heightScale;
                corners[corner] = GetOrAddSharedVertex(vertices, colors, sharedVertices,
                    new Vector3(geometryCorners[corner].x, y, geometryCorners[corner].y),
                    context.CornerColor(cell.index, corner, colorFor));
            }

            for (int corner = 0; corner < 6; corner++)
            {
                triangles.Add(centerIndex);
                triangles.Add(corners[(corner + 1) % 6]);
                triangles.Add(corners[corner]);
            }

            for (int edge = 0; edge < 6; edge++)
            {
                int neighbor = map.GetIndex(map.GetNeighbor(cell.coord, edge));
                if (neighbor >= 0 && context.IsMacro(neighbor)) continue;
                float bottom = neighbor >= 0 ? context.Height(neighbor) * appearance.heightScale : 0f;
                AddBoundarySkirt(vertices, triangles, colors,
                    vertices[corners[edge]], vertices[corners[(edge + 1) % 6]], bottom,
                    colorFor != null ? context.CornerColor(cell.index, edge, colorFor) : null,
                    appearance.sideDarkenFactor);
            }
        }

        private static void AddStrategicCellSurface(WorldMap map, WorldCell cell,
            List<Vector3> vertices, List<int> triangles, List<Color32> colors,
            Func<WorldCell, Color32> colorFor, float sideDarkenFactor)
        {
            Vector2 center = HexCenter(cell.coord);
            Vector2[] geometryCorners = HexGeometry.GetCorners(center);
            float top = StrategicSurfaceHeight(cell);
            Color32? color = colorFor != null ? colorFor(cell) : (Color32?)null;
            int centerIndex = vertices.Count;
            vertices.Add(new Vector3(center.x, top, center.y));
            if (color.HasValue) colors.Add(color.Value);

            int cornerStart = vertices.Count;
            for (int corner = 0; corner < 6; corner++)
            {
                vertices.Add(new Vector3(geometryCorners[corner].x, top, geometryCorners[corner].y));
                if (color.HasValue) colors.Add(color.Value);
            }
            for (int corner = 0; corner < 6; corner++)
            {
                triangles.Add(centerIndex);
                triangles.Add(cornerStart + (corner + 1) % 6);
                triangles.Add(cornerStart + corner);
            }

            for (int edge = 0; edge < 6; edge++)
            {
                int neighbor = map.GetIndex(map.GetNeighbor(cell.coord, edge));
                float neighborHeight = neighbor >= 0
                    ? StrategicSurfaceHeight(map.cells[neighbor])
                    : 0f;
                if (top <= neighborHeight + 0.0001f) continue;
                Vector2 cornerA = geometryCorners[edge];
                Vector2 cornerB = geometryCorners[(edge + 1) % 6];
                Vector3 topA = new Vector3(cornerA.x, top, cornerA.y);
                Vector3 topB = new Vector3(cornerB.x, top, cornerB.y);
                AddBoundarySkirt(vertices, triangles, colors, topA, topB, neighborHeight,
                    color, sideDarkenFactor);
            }
        }

        private static int GetOrAddSharedVertex(List<Vector3> vertices, List<Color32> colors,
            Dictionary<VertexKey, int> sharedVertices, Vector3 position, Color32? color)
        {
            var key = new VertexKey(position.x, position.z);
            if (sharedVertices.TryGetValue(key, out int existing)) return existing;
            int index = vertices.Count;
            vertices.Add(position);
            if (color.HasValue) colors.Add(color.Value);
            sharedVertices.Add(key, index);
            return index;
        }

        private static void AddBoundarySkirt(List<Vector3> vertices, List<int> triangles,
            List<Color32> colors, Vector3 topA, Vector3 topB, float bottomY,
            Color32? color, float sideDarkenFactor)
        {
            int start = vertices.Count;
            vertices.Add(topA);
            vertices.Add(topB);
            vertices.Add(new Vector3(topA.x, bottomY, topA.z));
            vertices.Add(new Vector3(topB.x, bottomY, topB.z));
            if (color.HasValue)
            {
                Color32 side = color.Value;
                side.r = (byte)(side.r * sideDarkenFactor);
                side.g = (byte)(side.g * sideDarkenFactor);
                side.b = (byte)(side.b * sideDarkenFactor);
                colors.Add(side);
                colors.Add(side);
                colors.Add(side);
                colors.Add(side);
            }
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
        }

        /// <summary>
        /// 向指定 submesh 追加一个六边形棱柱：顶面 + 6 个侧面（第一版不生成底面）。
        /// peakY 是顶面中心高度；当前普通格传入与 topY 相同，保留参数以兼容既有调用。
        /// </summary>
        private static void AddCellPrism(List<Vector3> vertices, List<int> triangles, Vector2 center,
            float topY, float peakY, Color32? cellColor, float sideDarkenFactor, List<Color32> colors)
        {
            int topCenterIndex = vertices.Count;
            vertices.Add(new Vector3(center.x, peakY, center.y));
            if (cellColor.HasValue) colors.Add(cellColor.Value);
            Vector2[] geometryCorners = HexGeometry.GetCorners(center);
            int topStart = vertices.Count;
            for (int corner = 0; corner < 6; corner++)
            {
                vertices.Add(new Vector3(geometryCorners[corner].x, topY, geometryCorners[corner].y));
                if (cellColor.HasValue) colors.Add(cellColor.Value);
            }
            for (int corner = 0; corner < 6; corner++)
            {
                triangles.Add(topCenterIndex);
                triangles.Add(topStart + (corner + 1) % 6);
                triangles.Add(topStart + corner);
            }

            for (int corner = 0; corner < 6; corner++)
            {
                Vector2 cornerA = geometryCorners[corner];
                Vector2 cornerB = geometryCorners[(corner + 1) % 6];
                Vector3 topA = new Vector3(cornerA.x, topY, cornerA.y);
                Vector3 topB = new Vector3(cornerB.x, topY, cornerB.y);
                Vector3 bottomA = new Vector3(topA.x, 0f, topA.z);
                Vector3 bottomB = new Vector3(topB.x, 0f, topB.z);
                int topAIndex = vertices.Count;
                vertices.Add(topA);
                vertices.Add(topB);
                vertices.Add(bottomA);
                vertices.Add(bottomB);
                if (cellColor.HasValue)
                {
                    Color32 sideColor = cellColor.Value;
                    sideColor.r = (byte)(sideColor.r * sideDarkenFactor);
                    sideColor.g = (byte)(sideColor.g * sideDarkenFactor);
                    sideColor.b = (byte)(sideColor.b * sideDarkenFactor);
                    colors.Add(sideColor);
                    colors.Add(sideColor);
                    colors.Add(sideColor);
                    colors.Add(sideColor);
                }
                triangles.Add(topAIndex);
                triangles.Add(topAIndex + 1);
                triangles.Add(topAIndex + 2);
                triangles.Add(topAIndex + 1);
                triangles.Add(topAIndex + 3);
                triangles.Add(topAIndex + 2);
            }
        }
    }
}
