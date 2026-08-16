using System;
using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;
using NUnit.Framework;
using UnityEngine;

public class TerrainRenderingTests
{
    [Test]
    public void StrategicTerrainShader_ExposesTextureVisibilityControls()
    {
        Shader shader = Shader.Find("Cultivation4X/StrategicTerrain");
        Assert.NotNull(shader);
        Material material = new Material(shader);
        try
        {
            Assert.IsTrue(material.HasProperty("_TextureStrength"));
            Assert.IsTrue(material.HasProperty("_TextureContrast"));
            Assert.IsTrue(material.HasProperty("_TextureOnly"));
            Assert.IsTrue(material.HasProperty("_WorldTiling"));
            Assert.IsTrue(material.HasProperty("_MacroStrength"));
            Assert.IsTrue(material.HasProperty("_MacroScale"));
            Assert.IsTrue(material.HasProperty("_TextureColorBlend"));
            Assert.IsTrue(material.HasProperty("_TerrainLightingStrength"));
            Assert.IsTrue(material.HasProperty("_SandTex"));
            Assert.IsTrue(material.HasProperty("_GrassTex"));
            Assert.IsTrue(material.HasProperty("_DirtTex"));
            Assert.IsTrue(material.HasProperty("_StoneTex"));
            Assert.IsTrue(material.HasProperty("_UseTerrainBlend"));
            Assert.IsTrue(material.HasProperty("_SandNormal"));
            Assert.IsTrue(material.HasProperty("_GrassNormal"));
            Assert.IsTrue(material.HasProperty("_DirtNormal"));
            Assert.IsTrue(material.HasProperty("_StoneNormal"));
            Assert.IsTrue(material.HasProperty("_TerrainNormalStrength"));
            string shaderSource = System.IO.File.ReadAllText(
                "Assets/MapSystem/Presentation/StrategicTerrain.shader");
            StringAssert.Contains("climateMoisture", shaderSource,
                "连续地表仍应把实际湿度传给材质，用于群系内部的局部修饰");
            StringAssert.Contains("grassToDirt", shaderSource,
                "湿度与噪声应参与草地与泥地的局部分布");
            StringAssert.Contains("dirtPatchMask", shaderSource,
                "草泥不能再均匀混成草绿色，必须形成可辨识的泥土地块");
            string builderSource = System.IO.File.ReadAllText(
                "Assets/MapSystem/Rendering/ContinuousTerrainSurfaceBuilder.cs");
            StringAssert.Contains("case BiomeType.TemperateForest", builderSource,
                "基础材质权重必须由生物群系主导，而不是只按地貌分类");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static WorldMap BuildMap(params LandformType[] landforms)
    {
        WorldMap map = new WorldMap
        {
            width = landforms.Length,
            height = 1,
            cells = new WorldCell[landforms.Length]
        };
        for (int index = 0; index < landforms.Length; index++)
        {
            map.cells[index] = new WorldCell
            {
                index = index,
                coord = new HexCoord(index, 0),
                landform = landforms[index],
                height = 0.5f
            };
        }
        return map;
    }

    private static WorldMap BuildHexPlateauHoleMap()
    {
        const int width = 3;
        const int height = 3;
        WorldMap map = new WorldMap
        {
            width = width,
            height = height,
            cells = new WorldCell[width * height]
        };
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                bool hole = col == 1 && row == 1;
                bool openPlain = col == 0 && row == 0;
                map.cells[row * width + col] = new WorldCell
                {
                    index = row * width + col,
                    coord = new HexCoord(col, row),
                    landform = hole || openPlain ? LandformType.Plain : LandformType.Mountain,
                    height = hole || openPlain ? 0.5f : 0.8f
                };
            }
        }
        return map;
    }

    [Test]
    public void CreateTerrainChunk_BuildsSingleMeshWithFiveSubmeshes()
    {
        WorldMap map = BuildMap(LandformType.DeepWater, LandformType.ShallowWater, LandformType.Coast,
            LandformType.Plain, LandformType.Hill, LandformType.Mountain);
        Mesh mesh = TerrainMeshGenerator.CreateTerrainChunk(map);
        try
        {
            Assert.AreEqual(5, mesh.subMeshCount);
            Assert.Greater(mesh.vertexCount, 0);
            int totalTriangles = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                totalTriangles += mesh.GetTriangles(submesh).Length;
            Assert.Greater(totalTriangles, 0);
            Assert.AreEqual(UnityEngine.Rendering.IndexFormat.UInt32, mesh.indexFormat);
            Assert.IsTrue(mesh.bounds.size.sqrMagnitude > 0f);
            Assert.IsFalse(float.IsNaN(mesh.bounds.size.x) || float.IsInfinity(mesh.bounds.size.x));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    [TestCase(LandformType.ShallowWater, BiomeType.Desert, TerrainMeshGenerator.WaterSubmesh)]
    [TestCase(LandformType.Coast, BiomeType.Rainforest, TerrainMeshGenerator.CoastSubmesh)]
    [TestCase(LandformType.Plain, BiomeType.Grassland, TerrainMeshGenerator.PlainSubmesh)]
    [TestCase(LandformType.Hill, BiomeType.TemperateForest, TerrainMeshGenerator.PlainSubmesh)]
    [TestCase(LandformType.Plain, BiomeType.Rainforest, TerrainMeshGenerator.PlainSubmesh)]
    [TestCase(LandformType.Plain, BiomeType.Wetland, TerrainMeshGenerator.HillSubmesh)]
    [TestCase(LandformType.Hill, BiomeType.Tundra, TerrainMeshGenerator.HillSubmesh)]
    [TestCase(LandformType.Plain, BiomeType.Desert, TerrainMeshGenerator.CoastSubmesh)]
    [TestCase(LandformType.Plain, BiomeType.Snowfield, TerrainMeshGenerator.MountainSubmesh)]
    [TestCase(LandformType.Hill, BiomeType.Alpine, TerrainMeshGenerator.MountainSubmesh)]
    [TestCase(LandformType.Mountain, BiomeType.Grassland, TerrainMeshGenerator.MountainSubmesh)]
    [TestCase(LandformType.Plain, BiomeType.Ocean, TerrainMeshGenerator.PlainSubmesh)]
    public void TerrainChunk_MapsBiomeAndLandformToExistingTextureSlot(
        LandformType landform, BiomeType biome, int expectedSubmesh)
    {
        WorldMap map = BuildMap(landform);
        map.cells[0].biome = biome;
        Mesh mesh = TerrainMeshGenerator.CreateTerrainChunk(map);
        try
        {
            Assert.Greater(mesh.GetTriangles(expectedSubmesh).Length, 0,
                $"{landform}/{biome} 应生成到纹理槽 {expectedSubmesh}");
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (submesh == expectedSubmesh) continue;
                Assert.AreEqual(0, mesh.GetTriangles(submesh).Length,
                    $"{landform}/{biome} 不应同时写入纹理槽 {submesh}");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void TerrainChunk_UnknownBiomeFallsBackToLandformTextureSlot()
    {
        WorldMap map = BuildMap(LandformType.Hill);
        map.cells[0].biome = (BiomeType)999;
        Mesh mesh = TerrainMeshGenerator.CreateTerrainChunk(map);
        try
        {
            Assert.Greater(mesh.GetTriangles(TerrainMeshGenerator.HillSubmesh).Length, 0);
            Assert.AreEqual(0, mesh.GetTriangles(TerrainMeshGenerator.PlainSubmesh).Length);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void TopHeight_RaisesMountainAboveWaterAndScalesWithCellHeight()
    {
        WorldCell water = new WorldCell { landform = LandformType.DeepWater, height = 0.5f };
        WorldCell mountainHigh = new WorldCell { landform = LandformType.Mountain, height = 1f };
        WorldCell mountainLow = new WorldCell { landform = LandformType.Mountain, height = 0f };

        Assert.Greater(TerrainMeshGenerator.TopHeight(mountainHigh), TerrainMeshGenerator.TopHeight(water));
        Assert.Greater(TerrainMeshGenerator.TopHeight(mountainHigh), TerrainMeshGenerator.TopHeight(mountainLow));

        WorldMap map = BuildMap(LandformType.Mountain);
        map.cells[0].height = 1f;
        Mesh mesh = TerrainMeshGenerator.CreateTerrainChunk(map);
        try
        {
            Assert.AreEqual(TerrainMeshGenerator.LandStrategicHeight, mesh.bounds.max.y, 0.0001f);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void VisualTopHeight_IsSmoothAcrossLandformBoundary()
    {
        WorldMap map = new WorldMap
        {
            width = 2,
            height = 1,
            cells = new WorldCell[2]
        };
        map.cells[0] = new WorldCell
        {
            index = 0,
            coord = new HexCoord(0, 0),
            landform = LandformType.Plain,
            height = 0.56f
        };
        map.cells[1] = new WorldCell
        {
            index = 1,
            coord = new HexCoord(1, 0),
            landform = LandformType.Hill,
            height = 0.58f
        };

        float diff = Mathf.Abs(TerrainMeshGenerator.VisualTopHeight(map, map.cells[0]) -
                               TerrainMeshGenerator.VisualTopHeight(map, map.cells[1]));
        Assert.Less(diff, 1f);
    }

    [Test]
    public void StrategicSurfaceHeight_KeepsAllLandCoplanarAndIgnoresDataHeight()
    {
        WorldCell plain = new WorldCell { landform = LandformType.Plain, height = 0.05f };
        WorldCell hill = new WorldCell { landform = LandformType.Hill, height = 0.55f };
        WorldCell mountain = new WorldCell { landform = LandformType.Mountain, height = 1f };
        Assert.AreEqual(TerrainMeshGenerator.LandStrategicHeight,
            TerrainMeshGenerator.StrategicSurfaceHeight(plain), 0.0001f);
        Assert.AreEqual(TerrainMeshGenerator.StrategicSurfaceHeight(plain),
            TerrainMeshGenerator.StrategicSurfaceHeight(hill), 0.0001f);
        Assert.AreEqual(TerrainMeshGenerator.StrategicSurfaceHeight(hill),
            TerrainMeshGenerator.StrategicSurfaceHeight(mountain), 0.0001f);
        Assert.Less(TerrainMeshGenerator.StrategicSurfaceHeight(LandformType.DeepWater),
            TerrainMeshGenerator.StrategicSurfaceHeight(LandformType.ShallowWater));
        Assert.Less(TerrainMeshGenerator.StrategicSurfaceHeight(LandformType.ShallowWater),
            TerrainMeshGenerator.LandStrategicHeight);
    }

    [Test]
    public void ContinuousTerrainSurface_UsesDataHeightButSmoothsAdjacentLand()
    {
        WorldMap map = BuildMap(LandformType.Plain, LandformType.Plain,
            LandformType.Hill, LandformType.Mountain);
        map.cells[0].height = 0.38f;
        map.cells[1].height = 0.55f;
        map.cells[2].height = 0.72f;
        map.cells[3].height = 0.90f;

        GameObject root = new GameObject("ContinuousTerrainHeightTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            float[] heights = map.cells
                .Select(cell => TerrainRenderer.PresentationSurfaceHeight(map, cell))
                .ToArray();
            Assert.IsTrue(TerrainRenderer.ActiveContinuousSurface);
            Assert.Greater(heights.Max() - heights.Min(), 1.5f,
                "连续地表必须真正显示地图数据层的平原、丘陵与高山高差");
            for (int index = 1; index < heights.Length; index++)
                Assert.Less(Mathf.Abs(heights[index] - heights[index - 1]),
                    3.2f, "相邻陆地允许形成可见山势，但不应超过完整山体高度");
            Mesh mesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.That(mesh.uv.Length, Is.EqualTo(mesh.vertexCount));
            Assert.That(mesh.normals.Length, Is.EqualTo(mesh.vertexCount));
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void PlateauMask_MarksMountainCellsAndEnclosedLandHoles()
    {
        WorldMap map = BuildHexPlateauHoleMap();
        int centerIndex = map.GetIndex(new HexCoord(1, 1));
        bool[] mask = ContinuousTerrainSurfaceBuilder.BuildPlateauMask(map);

        Assert.IsTrue(mask[centerIndex],
            "被 Mountain 完全围住的内陆格必须进入平顶遮罩");
        Assert.IsTrue(mask[map.GetIndex(new HexCoord(0, 1))],
            "Mountain 格自身必须进入平顶遮罩");
        Assert.IsFalse(mask[map.GetIndex(new HexCoord(0, 0))],
            "与地图边缘相连的普通地形不应被误判为山内低地");
    }

    [Test]
    public void ContinuousTerrainSurface_RendersEnclosedMountainHoleFlat()
    {
        WorldMap map = BuildHexPlateauHoleMap();
        WorldCell hole = map.cells[map.GetIndex(new HexCoord(1, 1))];
        GameObject root = new GameObject("PlateauHoleFlatTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            float expected = TerrainRenderer.PresentationSurfaceHeight(map, hole);
            Vector2 center = TerrainMeshGenerator.HexCenter(hole.coord);
            var surfaceHeights = new List<float>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.name.StartsWith("ContinuousTerrainChunk")) continue;
                Vector3[] vertices = mesh.vertices;
                for (int index = 0; index < vertices.Length; index++)
                {
                    Vector2 xz = new Vector2(vertices[index].x, vertices[index].z);
                    if ((xz - center).magnitude <= HexGeometry.Radius * 0.8f)
                        surfaceHeights.Add(vertices[index].y);
                }
            }
            Assert.IsNotEmpty(surfaceHeights);
            Assert.IsTrue(surfaceHeights.All(height => Mathf.Abs(height - expected) <= 0.001f),
                "山内被围住的非 Mountain 格必须整体平顶，不能继续采样连续高度场");
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ContinuousTerrainSurface_KeepsOpenWaterFlat()
    {
        WorldMap map = BuildMap(LandformType.DeepWater, LandformType.DeepWater,
            LandformType.DeepWater);
        map.cells[0].height = 0f;
        map.cells[1].height = 0.5f;
        map.cells[2].height = 1f;
        GameObject root = new GameObject("ContinuousTerrainWaterTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            float first = TerrainRenderer.PresentationSurfaceHeight(map, map.cells[0]);
            Assert.AreEqual(first, TerrainRenderer.PresentationSurfaceHeight(map, map.cells[1]), 0.0001f);
            Assert.AreEqual(first, TerrainRenderer.PresentationSurfaceHeight(map, map.cells[2]), 0.0001f);
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ContinuousTerrainSurface_NearMeshSubdividesEachHexBeyondFarMesh()
    {
        WorldMap map = BuildMap(LandformType.Plain, LandformType.Hill);
        map.cells[0].height = 0.46f;
        map.cells[1].height = 0.76f;
        GameObject root = new GameObject("ContinuousTerrainSubdivisionTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            Mesh[] meshes = root.GetComponentsInChildren<MeshFilter>(true)
                .Select(filter => filter.sharedMesh)
                .Where(mesh => mesh != null)
                .ToArray();
            Assert.AreEqual(2, meshes.Length, "同一 chunk 应分别生成远景和近景网格");
            Assert.Greater(meshes.Max(mesh => mesh.vertexCount),
                meshes.Min(mesh => mesh.vertexCount),
                "近景网格必须具有真实格内细分，不能继续只有中心和六个角点");
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ContinuousTerrainSurface_BlendsLandMaterialsInsteadOfKeepingHexSubmeshes()
    {
        WorldMap map = BuildMap(LandformType.Coast, LandformType.Plain,
            LandformType.Hill, LandformType.Mountain);
        map.cells[0].biome = BiomeType.Coast;
        map.cells[1].biome = BiomeType.Grassland;
        map.cells[2].biome = BiomeType.Wetland;
        map.cells[3].biome = BiomeType.Alpine;
        map.cells[0].moisture = 0.15f;
        map.cells[1].moisture = 0.42f;
        map.cells[2].moisture = 0.68f;
        map.cells[3].moisture = 0.88f;
        GameObject root = new GameObject("ContinuousTerrainMaterialBlendTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            Mesh mesh = root.GetComponentsInChildren<MeshFilter>(true)
                .Select(filter => filter.sharedMesh)
                .Where(candidate => candidate != null)
                .OrderByDescending(candidate => candidate.vertexCount)
                .First();
            Assert.Greater(mesh.GetTriangles(TerrainMeshGenerator.PlainSubmesh).Length, 0);
            Assert.AreEqual(0, mesh.GetTriangles(TerrainMeshGenerator.CoastSubmesh).Length,
                "连续陆地不应继续按沙地 Hex 拆分子材质");
            Assert.AreEqual(0, mesh.GetTriangles(TerrainMeshGenerator.HillSubmesh).Length,
                "连续陆地不应继续按丘陵 Hex 拆分子材质");
            Assert.AreEqual(0, mesh.GetTriangles(TerrainMeshGenerator.MountainSubmesh).Length,
                "连续陆地不应继续按山地 Hex 拆分子材质");

            var weights = new List<Vector4>();
            mesh.GetUVs(1, weights);
            Assert.AreEqual(mesh.vertexCount, weights.Count);
            Assert.IsTrue(weights.All(weight =>
                Mathf.Abs(weight.x + weight.y + weight.z + weight.w - 1f) < 0.001f));
            Assert.IsTrue(weights.Any(weight => weight.x > 0.10f && weight.y > 0.10f),
                "沙地与草地交界应存在混合权重，不能仍沿 Hex 边硬切");

            var climate = new List<Vector2>();
            mesh.GetUVs(2, climate);
            Assert.AreEqual(mesh.vertexCount, climate.Count);
            Assert.IsTrue(climate.All(value => value.x >= 0f && value.x <= 1f),
                "传入材质的湿度必须保持在归一化范围内");
            Assert.Greater(climate.Max(value => value.x) - climate.Min(value => value.x), 0.10f,
                "不同湿度区域传入材质后不应退化为同一个常量");

            Material material = root.GetComponentInChildren<MeshRenderer>(true)
                .sharedMaterials[TerrainMeshGenerator.PlainSubmesh];
            Assert.AreEqual(1f, material.GetFloat("_UseTerrainBlend"), 0.0001f);
            Assert.IsTrue(renderer.BlendsContinuousTerrainMaterials);
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ContinuousTerrainSurface_BiomeDrivesBaseMaterialBeforeMoistureDetail()
    {
        Vector4 grassland = AverageContinuousWeights(BiomeType.Grassland, 0.5f);
        Vector4 forest = AverageContinuousWeights(BiomeType.TemperateForest, 0.5f);
        Vector4 desert = AverageContinuousWeights(BiomeType.Desert, 0.5f);
        Vector4 alpine = AverageContinuousWeights(BiomeType.Alpine, 0.5f);
        Vector4 dryGrassland = AverageContinuousWeights(BiomeType.Grassland, 0.2f);
        Vector4 wetGrassland = AverageContinuousWeights(BiomeType.Grassland, 0.8f);

        Assert.Greater(desert.x, grassland.x + 0.55f,
            "荒漠的沙材质应由生物群系直接主导");
        Assert.Greater(alpine.w, grassland.w + 0.55f,
            "高山群系的岩石材质应由生物群系直接主导");
        Assert.Greater(forest.z, grassland.z,
            "温带森林应保留比草原更多的林下泥土");
        Assert.Less(Mathf.Abs(wetGrassland.y - dryGrassland.y), 0.12f,
            "湿度只能在草原基准附近修饰，不能覆盖生物群系本身");
    }

    [Test]
    public void ContinuousTerrainSurface_DuplicatedCellEdgesShareHeightAndSmoothNormal()
    {
        WorldMap map = BuildMap(LandformType.Plain, LandformType.Hill);
        map.cells[0].height = 0.42f;
        map.cells[1].height = 0.82f;
        GameObject root = new GameObject("ContinuousTerrainEdgeTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            Mesh mesh = root.GetComponentsInChildren<MeshFilter>(true)
                .Select(filter => filter.sharedMesh)
                .Where(candidate => candidate != null)
                .OrderByDescending(candidate => candidate.vertexCount)
                .First();
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            var sharedTopSamples = Enumerable.Range(0, vertices.Length)
                .Where(index => normals[index].y > 0.01f)
                .GroupBy(index => $"{Mathf.RoundToInt(vertices[index].x * 10000f)}:" +
                                  $"{Mathf.RoundToInt(vertices[index].z * 10000f)}")
                .Where(group => group.Count() > 1)
                .ToArray();
            int topVertexCount = 0;
            for (int index = 0; index < vertices.Length; index++)
            {
                if (normals[index].y > 0.01f) topVertexCount++;
            }
            Assert.Less(topVertexCount, 38,
                "两个相邻格应共享同一边缘的表面顶点；未共享时顶面顶点应为 2×(3×2×3+1)=38");
            foreach (IGrouping<string, int> group in sharedTopSamples)
            {
                int first = group.First();
                foreach (int index in group.Skip(1))
                {
                    Assert.AreEqual(vertices[first].y, vertices[index].y, 0.0001f,
                        "相邻格边缘高度必须来自同一连续高度场");
                    Assert.Greater(Vector3.Dot(normals[first], normals[index]), 0.999f,
                        "相邻格边缘法线必须连续，不能重新出现六边形折面");
                }
            }
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ContinuousTerrainSurface_BluntsPeaksAgainstAdjacentRidges()
    {
        const float rawHeight = 2.10f;
        float ridge = ContinuousTerrainSurfaceBuilder.ApplyMountainVerticalEmphasis(
            rawHeight, true, false, false);
        float peak = ContinuousTerrainSurfaceBuilder.ApplyMountainVerticalEmphasis(
            rawHeight, true, true, false);
        float mountainPass = ContinuousTerrainSurfaceBuilder.ApplyMountainVerticalEmphasis(
            rawHeight, true, false, true);
        float shoulder = ContinuousTerrainSurfaceBuilder.ApplyMountainVerticalEmphasis(
            rawHeight, false, false, false);

        Assert.AreEqual(3.10f, ridge, 0.0001f,
            "主山脊只在表现层放大相对地面的垂直落差");
        Assert.AreEqual(ridge, peak, 0.0001f,
            "峰顶不得在主山脊倍率之上继续固定乘高");
        Assert.AreEqual(rawHeight, mountainPass, 0.0001f,
            "山口不得跟随主山脊一起抬高，否则会封死谷地");
        Assert.AreEqual(rawHeight, shoulder, 0.0001f,
            "普通山肩不得被整体拉高成连续灰墙");

        float clamped = ContinuousTerrainSurfaceBuilder.ClampPeakToAdjacentRidge(
            6.10f, ridge);
        float alreadySafe = ContinuousTerrainSurfaceBuilder.ClampPeakToAdjacentRidge(
            3.50f, ridge);
        Assert.AreEqual(3.85f, clamped, 0.0001f,
            "峰顶相对地面的高度不得超过最高相邻主脊的 1.25 倍");
        Assert.AreEqual(3.50f, alreadySafe, 0.0001f,
            "已在邻域上限内的峰顶不得被无条件压低");
    }

    [Test]
    public void ContinuousTerrainSurface_AddsOnlyOneShoulderRingAndPreservesPasses()
    {
        WorldMap map = BuildMap(LandformType.Plain, LandformType.Plain,
            LandformType.Mountain, LandformType.Plain, LandformType.Plain);
        bool[] ridge = { false, false, true, false, false };
        bool[] noPasses = { false, false, false, false, false };
        float[] heights = { 0.20f, 0.40f, 3.40f, 0.40f, 0.20f };

        ContinuousTerrainSurfaceBuilder.ApplyOneRingRidgeShoulders(
            map, heights, ridge, noPasses);

        Assert.AreEqual(0.20f, heights[0], 0.0001f,
            "山肩不得递归扩散到第二圈");
        Assert.AreEqual(1.30f, heights[1], 0.0001f,
            "紧邻主脊的山肩必须按原高度 70% 与主脊高度 30% 混合");
        Assert.AreEqual(3.40f, heights[2], 0.0001f,
            "加宽不得改变主脊自身高度");
        Assert.AreEqual(1.30f, heights[3], 0.0001f,
            "主脊另一侧必须形成对称的一圈山肩");
        Assert.AreEqual(0.20f, heights[4], 0.0001f,
            "另一侧第二圈同样不得被抬高");

        bool[] ridgePass = { false, false, true, false, false };
        float[] passHeights = { 0.20f, 0.40f, 3.40f, 0.40f, 0.20f };
        ContinuousTerrainSurfaceBuilder.ApplyOneRingRidgeShoulders(
            map, passHeights, ridge, ridgePass);
        CollectionAssert.AreEqual(new[] { 0.20f, 0.40f, 3.40f, 0.40f, 0.20f },
            passHeights, "山口及其两侧不得生成表现层山肩");
    }

    [Test]
    public void ContinuousTerrainSurface_FlattensOnlyConnectedBuildableMountainTerrace()
    {
        WorldMap map = BuildMap(LandformType.Mountain, LandformType.Mountain,
            LandformType.Mountain, LandformType.Mountain, LandformType.Mountain);
        map.cells[1].isBuildable = true;
        map.cells[2].isBuildable = true;
        map.cells[3].isBuildable = true;
        float[] heights = { 0.30f, 1.20f, 1.80f, 2.40f, 0.50f };
        float[] detail = { 0.018f, 0.018f, 0.018f, 0.018f, 0.018f };

        ContinuousTerrainSurfaceBuilder.ApplyMountainTerraceFlattening(map, heights, detail);

        Assert.AreEqual(0.30f, heights[0], 0.0001f,
            "台地平坦化不得扩散到相邻的普通山地");
        Assert.AreEqual(1.80f, heights[1], 0.0001f);
        Assert.AreEqual(1.80f, heights[2], 0.0001f);
        Assert.AreEqual(1.80f, heights[3], 0.0001f,
            "同一可建山地台地集群必须使用统一表现高度");
        Assert.AreEqual(0.50f, heights[4], 0.0001f,
            "台地另一侧的普通山地不得被平坦化");
        Assert.IsTrue(new[] { 1, 2, 3 }.All(index => detail[index] <= 0.003f),
            "台地内部只能保留极轻微的表面细节噪声");
        Assert.AreEqual(0.018f, detail[0], 0.0001f);
        Assert.AreEqual(0.018f, detail[4], 0.0001f);
    }

    [Test]
    public void ContinuousTerrainSurface_SteepSlopesReceiveMoreRockMaterial()
    {
        WorldMap map = BuildMap(LandformType.Plain, LandformType.Hill, LandformType.Mountain);
        map.cells[0].height = 0.50f;
        map.cells[1].height = 0.62f;
        map.cells[2].height = 0.94f;
        GameObject root = new GameObject("ContinuousTerrainSlopeMaterialTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            Mesh mesh = root.GetComponentsInChildren<MeshFilter>(true)
                .Select(filter => filter.sharedMesh)
                .Where(candidate => candidate != null)
                .OrderByDescending(candidate => candidate.vertexCount)
                .First();
            var weights = new List<Vector4>();
            mesh.GetUVs(1, weights);
            Vector3[] normals = mesh.normals;
            float steepRock = Enumerable.Range(0, normals.Length)
                .Where(index => normals[index].y > 0.01f && normals[index].y < 0.72f)
                .Select(index => weights[index].w)
                .DefaultIfEmpty(0f)
                .Max();
            float flatRock = Enumerable.Range(0, normals.Length)
                .Where(index => normals[index].y > 0.94f)
                .Select(index => weights[index].w)
                .DefaultIfEmpty(1f)
                .Min();
            Assert.Greater(steepRock, flatRock + 0.18f,
                "陡坡必须明显提高石质权重，不能继续与平缓植被坡平均混色");
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ContinuousTerrainSurface_PreservesGeneratedRidgeAboveItsShoulder()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 64, height = 48, seed = 20260806 });
        Assert.IsTrue(WorldGenerationDiagnosticsStore.TryGet(map,
            out WorldGenerationDiagnostics diagnostics));
        var pair = map.cells
            .Where(cell => diagnostics.mountainRidgeCore[cell.index] &&
                           !diagnostics.mountainPasses[cell.index])
            .SelectMany(cell => map.GetNeighborIndices(cell.index)
                .Where(index => !diagnostics.mountainRidgeCore[index] &&
                                map.cells[index].landform != LandformType.DeepWater &&
                                map.cells[index].landform != LandformType.ShallowWater)
                .Select(index => new { Ridge = cell, Shoulder = map.cells[index] }))
            .OrderByDescending(candidate => candidate.Ridge.height - candidate.Shoulder.height)
            .FirstOrDefault();
        Assert.NotNull(pair, "固定种子必须存在可比较的山脊—山肩横截面");

        GameObject root = new GameObject("ContinuousTerrainSkeletonProtectionTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            float ridge = TerrainRenderer.PresentationSurfaceHeight(map, pair.Ridge);
            float shoulder = TerrainRenderer.PresentationSurfaceHeight(map, pair.Shoulder);
            Assert.Greater(ridge - shoulder, 0.18f,
                "表现层不得再次把窄山脊与相邻山肩平均成同一片软丘陵");
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ContinuousTerrainSurface_KeepsOnlySubtleWithinCellNoise()
    {
        WorldMap map = BuildMap(LandformType.Plain, LandformType.Plain, LandformType.Plain);
        foreach (WorldCell cell in map.cells) cell.height = 0.52f;
        GameObject root = new GameObject("ContinuousTerrainReliefTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            Vector2 centerPosition = TerrainMeshGenerator.HexCenter(map.cells[1].coord);
            Vector2 insideCell = centerPosition + new Vector2(0.18f, 0.12f);
            float center = TerrainRenderer.PresentationSurfaceHeightAt(map, centerPosition, map.cells[1]);
            float detail = TerrainRenderer.PresentationSurfaceHeightAt(map, insideCell, map.cells[1]);
            Assert.Less(Mathf.Abs(center - detail), 0.02f,
                "同高度平原格内只能保留很轻微的细节噪声，不能形成橡皮泥式坡面");

            float before = TerrainRenderer.PresentationSurfaceHeight(map, map.cells[1]);
            typeof(TerrainRenderer).GetMethod("SetTerrainReliefScale",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(renderer, new object[] { 2f });
            float after = TerrainRenderer.PresentationSurfaceHeight(map, map.cells[1]);
            Assert.Less(Mathf.Abs(after - before), 0.03f,
                "平原不得因起伏倍率变成宏观波浪地形");
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void RegionUplift_IsDisabledBecauseLandformsUseRegionModels()
    {
        const int width = 3;
        const int height = 3;
        WorldMap map = new WorldMap
        {
            width = width,
            height = height,
            cells = new WorldCell[width * height]
        };
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                int index = row * width + col;
                map.cells[index] = new WorldCell
                {
                    index = index,
                    coord = new HexCoord(col, row),
                    landform = LandformType.Plain,
                    height = 0.8f,
                    regionId = "range"
                };
            }
        }
        map.regions = new List<MapRegionData>
        {
            new MapRegionData
            {
                regionId = "range",
                regionType = MapRegionType.MountainRange,
                centerCellIndex = 4,
                cellIndices = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8 }
            }
        };

        map.cells[0].internalPositionTag = MapInternalPositionTag.Summit;
        map.cells[4].internalPositionTag = MapInternalPositionTag.MountainPass;
        map.cells[8].internalPositionTag = MapInternalPositionTag.Ridge;

        float summit = TerrainMeshGenerator.RegionUplift(map, map.cells[0]);
        float centerPass = TerrainMeshGenerator.RegionUplift(map, map.cells[4]);
        float ridge = TerrainMeshGenerator.RegionUplift(map, map.cells[8]);
        Assert.AreEqual(0f, summit);
        Assert.AreEqual(0f, ridge);
        Assert.AreEqual(0f, centerPass);
        Assert.AreEqual(0f, TerrainMeshGenerator.RegionUplift(map, null));
        Assert.AreEqual(TerrainMeshGenerator.VisualTopHeight(map, map.cells[0]),
            TerrainMeshGenerator.VisualTopHeight(map, map.cells[4]), 0.0001f);
    }

    [Test]
    public void AdjacentMountains_ShareContinuousSurfaceWithoutInternalPrismWalls()
    {
        WorldMap map = BuildMap(LandformType.Mountain, LandformType.Mountain);
        map.cells[0].height = 0.82f;
        map.cells[1].height = 0.94f;
        Mesh mesh = TerrainMeshGenerator.CreateTerrainChunk(map);
        try
        {
            int triangleCount = mesh.GetTriangles(TerrainMeshGenerator.MountainSubmesh).Length / 3;
            Assert.AreEqual(32, triangleCount,
                "两个相邻山地应有 12 个顶面三角形和 20 个外围收边三角形，不应生成内部侧壁");
            Assert.Less(mesh.vertexCount, 2 * 31,
                "相邻山地应共享角点，而不是各自生成完整六棱柱顶点");
            Assert.AreEqual(TerrainMeshGenerator.VisualTopHeight(map, map.cells[0]),
                TerrainMeshGenerator.VisualTopPeakHeight(map, map.cells[0]), 0.0001f,
                "连续山体不应再把每个 Hex 中心抬成独立尖峰");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void ContinuousTerrainSurface_TriangleWindingsMatchVertexNormals()
    {
        WorldMap map = BuildMap(LandformType.Mountain, LandformType.Plain);
        map.cells[0].height = 0.90f;
        map.cells[1].height = 0.50f;
        GameObject root = new GameObject("ContinuousTerrainWindingTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            int reversed = 0;
            int inspected = 0;
            foreach (MeshFilter filter in filters)
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    int[] triangles = mesh.GetTriangles(submesh);
                    for (int index = 0; index + 2 < triangles.Length; index += 3)
                    {
                        int a = triangles[index];
                        int b = triangles[index + 1];
                        int c = triangles[index + 2];
                        Vector3 faceNormal = Vector3.Cross(
                            vertices[b] - vertices[a],
                            vertices[c] - vertices[a]);
                        Vector3 vertexNormal = (normals[a] + normals[b] + normals[c]) / 3f;
                        inspected++;
                        if (Vector3.Dot(faceNormal.normalized, vertexNormal.normalized) < -0.05f)
                            reversed++;
                    }
                }
            }
            Assert.Greater(inspected, 0);
            Assert.AreEqual(0, reversed,
                "连续地表三角形的 winding 必须与顶点法线一致，否则 addshadow/光照会生成黑色三角面");
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ContinuousTerrainSurface_HasNoCrossCornerOversizedTriangles()
    {
        WorldMap generated = WorldGenerator.Generate(new MapGenerationSettings
        {
            width = 32,
            height = 24,
            seed = 6102
        });
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(generated);
        WorldMap map = Newtonsoft.Json.JsonConvert.DeserializeObject<WorldMap>(json);
        GameObject root = new GameObject("ContinuousTerrainOversizeTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.name.StartsWith("ContinuousTerrainChunk")) continue;
                Vector3[] vertices = mesh.vertices;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    int[] triangles = mesh.GetTriangles(submesh);
                    for (int index = 0; index + 2 < triangles.Length; index += 3)
                    {
                        Vector3 a = vertices[triangles[index]];
                        Vector3 b = vertices[triangles[index + 1]];
                        Vector3 c = vertices[triangles[index + 2]];
                        float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
                        float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                        float minZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z));
                        float maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                        float span = new Vector2(maxX - minX, maxZ - minZ).magnitude;
                        Assert.LessOrEqual(span, HexGeometry.Radius * 2f + 0.05f,
                            $"存在跨越六角角的错误大三角：submesh={submesh} " +
                            $"tri={index / 3} a={a} b={b} c={c}");
                    }
                }
            }
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ValleyRegion_DoesNotDepressBaseSurface()
    {
        WorldMap map = BuildMap(LandformType.Plain, LandformType.Plain, LandformType.Plain,
            LandformType.Plain, LandformType.Plain);
        for (int index = 1; index <= 3; index++)
        {
            map.cells[index].regionId = "valley";
            map.cells[index].internalPositionTag = index == 2
                ? MapInternalPositionTag.ValleyFloor
                : MapInternalPositionTag.ValleyEntrance;
        }
        map.regions = new List<MapRegionData>
        {
            new MapRegionData
            {
                regionId = "valley",
                regionType = MapRegionType.Valley,
                centerCellIndex = 2,
                cellIndices = new List<int> { 1, 2, 3 }
            }
        };

        Assert.IsFalse(TerrainMeshGenerator.IsMacroTerrain(map, map.cells[1]));
        Assert.IsFalse(TerrainMeshGenerator.IsMacroTerrain(map, map.cells[2]));
        Assert.IsFalse(TerrainMeshGenerator.IsMacroTerrain(map, map.cells[0]));
        GameObject root = new GameObject("ValleyContinuousSurfaceTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            float outside = TerrainRenderer.PresentationSurfaceHeight(map, map.cells[0]);
            float entrance = TerrainRenderer.PresentationSurfaceHeight(map, map.cells[1]);
            float floor = TerrainRenderer.PresentationSurfaceHeight(map, map.cells[2]);
            Assert.AreEqual(outside, floor, 0.03f,
                "谷地语义必须由谷壁和负空间表达，不得压低基础地表");
            Assert.AreEqual(entrance, floor, 0.03f);
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void TryGetCellIndex_MapsContinuousTerrainPositionBackToOriginalHex()
    {
        WorldMap map = new WorldMap
        {
            width = 2,
            height = 2,
            cells = new WorldCell[4]
        };
        for (int row = 0; row < 2; row++)
        for (int col = 0; col < 2; col++)
        {
            int index = row * 2 + col;
            map.cells[index] = new WorldCell
            {
                index = index,
                coord = new HexCoord(col, row),
                landform = LandformType.Mountain,
                height = 0.9f,
                isBuildable = false
            };
        }

        for (int index = 0; index < map.cells.Length; index++)
        {
            Vector2 center = TerrainMeshGenerator.HexCenter(map.cells[index].coord);
            Assert.IsTrue(TerrainMeshGenerator.TryGetCellIndex(map,
                new Vector3(center.x, 20f, center.y), out int selected));
            Assert.AreEqual(index, selected);
        }
        Assert.IsFalse(TerrainMeshGenerator.TryGetCellIndex(map,
            new Vector3(-20f, 0f, -20f), out _));
    }

    [Test]
    public void CreateTerrainChunks_SplitsMapAndPreservesTotalGeometry()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
        {
            width = 128,
            height = 96,
            seed = 20260806
        });
        Mesh whole = TerrainMeshGenerator.CreateTerrainChunk(map);
        List<Mesh> chunks = TerrainMeshGenerator.CreateTerrainChunks(map, 16);
        try
        {
            Assert.AreEqual(48, chunks.Count);
            int chunkVertices = 0;
            int chunkTriangles = 0;
            foreach (Mesh chunk in chunks)
            {
                Assert.AreEqual(5, chunk.subMeshCount);
                Assert.AreEqual(UnityEngine.Rendering.IndexFormat.UInt32, chunk.indexFormat);
                Assert.IsTrue(chunk.bounds.size.sqrMagnitude > 0f);
                chunkVertices += chunk.vertexCount;
                for (int submesh = 0; submesh < chunk.subMeshCount; submesh++)
                    chunkTriangles += chunk.GetTriangles(submesh).Length;
            }

            Assert.GreaterOrEqual(chunkVertices, whole.vertexCount,
                "分块边界可以复制共享顶点，但不得丢失整图几何");
            int wholeTriangles = 0;
            for (int submesh = 0; submesh < whole.subMeshCount; submesh++)
                wholeTriangles += whole.GetTriangles(submesh).Length;
            Assert.AreEqual(wholeTriangles, chunkTriangles);
            Assert.AreEqual(chunks.Count, chunks.Select(chunk => chunk.name).Distinct().Count());
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(whole);
            foreach (Mesh chunk in chunks) UnityEngine.Object.DestroyImmediate(chunk);
        }
    }

    [Test]
    public void Renderer_RenderCreatesMultipleChunksAndClearRemovesAll()
    {
        const int mapWidth = 24;
        const int mapHeight = 17;
        WorldMap map = new WorldMap
        {
            width = mapWidth,
            height = mapHeight,
            cells = new WorldCell[mapWidth * mapHeight]
        };
        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                int index = row * mapWidth + col;
                map.cells[index] = new WorldCell
                {
                    index = index,
                    coord = new HexCoord(col, row),
                    landform = LandformType.Plain,
                    height = 0.5f
                };
            }
        }

        GameObject root = new GameObject("TerrainRendererChunkTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            Assert.AreEqual(4, renderer.ChunkCount);
            Assert.AreEqual(8, root.transform.childCount);
            Assert.AreEqual(4, renderer.FarChunkCount);
            Assert.AreEqual(4, renderer.NearChunkCount);
            foreach (MeshRenderer chunkRenderer in root.GetComponentsInChildren<MeshRenderer>())
                Assert.AreEqual(TerrainMeshGenerator.SubmeshCount, chunkRenderer.sharedMaterials.Length);
            Assert.AreEqual(renderer.FarChunkCount + renderer.NearChunkCount,
                root.GetComponentsInChildren<MeshCollider>(true).Length);

            renderer.Clear();
            Assert.AreEqual(0, renderer.ChunkCount);
            Assert.AreEqual(0, root.transform.childCount);
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void MaterialProvider_MapsAllLandformsAndMakesWaterTransparent()
    {
        foreach (LandformType landform in Enum.GetValues(typeof(LandformType)))
        {
            Color color = TerrainMaterialProvider.ColorFor(landform);
            Assert.AreNotEqual(default(Color), color);
        }

        LandformType[] representatives = Enumerable.Range(0, TerrainMeshGenerator.SubmeshCount)
            .Select(TerrainMaterialProvider.RepresentativeLandform)
            .ToArray();
        Assert.AreEqual(TerrainMeshGenerator.SubmeshCount,
            representatives.Select(TerrainMaterialProvider.ColorFor).Distinct().Count());

        Material water = TerrainMaterialProvider.CreateMaterial(LandformType.ShallowWater);
        Material plain = TerrainMaterialProvider.CreateMaterial(LandformType.Plain);
        try
        {
            Assert.NotNull(water);
            Assert.NotNull(plain);
            Assert.AreEqual(3000, water.renderQueue);
            Assert.IsTrue(water.IsKeywordEnabled("_ALPHABLEND_ON"));
            Assert.IsFalse(plain.IsKeywordEnabled("_ALPHABLEND_ON"));
            // renderQueue 赋 -1 时 Unity 会返回 shader 默认队列（Standard 不透明为 2000）。
            Assert.AreEqual(2000, plain.renderQueue);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(water);
            UnityEngine.Object.DestroyImmediate(plain);
        }
    }

    [Test]
    public void Renderer_RenderCreatesChunkAndClearRemovesIt()
    {
        WorldMap map = BuildMap(LandformType.DeepWater, LandformType.Plain, LandformType.Mountain);
        GameObject root = new GameObject("TerrainRendererTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            Assert.AreEqual(1, renderer.ChunkCount);
            Assert.IsTrue(TerrainRenderer.ActiveContinuousSurface);
            Assert.Greater(TerrainRenderer.PresentationSurfaceHeight(map, map.cells[2]),
                TerrainMeshGenerator.StrategicSurfaceHeight(map.cells[2]),
                "连续地表应使用数据高度，而不是把所有陆地压回统一平面");
            Assert.AreEqual(2, root.transform.childCount);
            MeshRenderer meshRenderer = root.GetComponentInChildren<MeshRenderer>();
            Assert.NotNull(meshRenderer);
            Assert.AreEqual(TerrainMeshGenerator.SubmeshCount, meshRenderer.sharedMaterials.Length);
            Assert.NotNull(root.GetComponentInChildren<MeshCollider>());

            Vector2 mountainCenter = TerrainMeshGenerator.HexCenter(map.cells[2].coord);
            Assert.IsTrue(renderer.TryGetCellIndexAtWorldPosition(
                new Vector3(mountainCenter.x, 10f, mountainCenter.y), out int selected));
            Assert.AreEqual(2, selected, "连续山体命中仍应返回底层 Mountain Hex");

            renderer.Clear();
            Assert.AreEqual(0, renderer.ChunkCount);
            Assert.AreEqual(0, root.transform.childCount);
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void Renderer_PrebuildsFarAndNearSetsAndSwitchesByTier()
    {
        WorldMap map = BuildMap(LandformType.Plain, LandformType.Plain, LandformType.Plain);
        GameObject root = new GameObject("TerrainRendererTierTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            Assert.AreEqual(1, renderer.FarChunkCount);
            Assert.AreEqual(1, renderer.NearChunkCount);
            Assert.AreEqual(1, renderer.ChunkCount);

            renderer.ApplyTier(WorldMap3DZoomTier.Near);
            Material plainMaterial = root.transform.GetChild(1).GetComponent<MeshRenderer>()
                .sharedMaterials[TerrainMeshGenerator.PlainSubmesh];
            Assert.AreEqual(WorldMap3DZoomTier.Near, renderer.ActiveZoomTier);
            Assert.AreEqual(1.15f, TerrainRenderer.ActiveAppearance.heightScale, 0.0001f);
            Assert.Greater(TerrainRenderer.ActiveAppearance.sideDarkenFactor, 0.85f);
            Assert.IsTrue(root.transform.GetChild(1).gameObject.activeSelf,
                "近景档位应显示近景弱边网格");
            Assert.AreEqual(0.82f, plainMaterial.GetFloat("_TextureStrength"), 0.0001f);
            Assert.AreEqual(0.121f, plainMaterial.GetFloat("_MacroStrength"), 0.0001f);
            Assert.AreEqual(0.10f, plainMaterial.GetFloat("_TextureColorBlend"), 0.0001f);
            Assert.AreEqual(0.55f, plainMaterial.GetFloat("_TerrainNormalStrength"), 0.0001f);

            renderer.ApplyTier(WorldMap3DZoomTier.Far);
            Assert.AreEqual(WorldMap3DZoomTier.Far, renderer.ActiveZoomTier);
            Assert.AreEqual(1f, TerrainRenderer.ActiveAppearance.heightScale, 0.0001f);
            Assert.IsTrue(root.transform.GetChild(0).gameObject.activeSelf,
                "远景档位应显示完整棱柱网格");
            Assert.AreEqual(0f, plainMaterial.GetFloat("_TextureStrength"), 0.0001f);
            Assert.AreEqual(0f, plainMaterial.GetFloat("_MacroStrength"), 0.0001f);
            Assert.AreEqual(0f, plainMaterial.GetFloat("_TextureColorBlend"), 0.0001f);
            Assert.AreEqual(0f, plainMaterial.GetFloat("_TerrainNormalStrength"), 0.0001f);
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void PerspectiveFieldOfViewChange_PreservesHorizontalVisibleHexes()
    {
        GameObject cameraObject = new GameObject("PerspectiveCoverageCamera");
        GameObject rendererObject = new GameObject("PerspectiveCoverageRenderer");
        Camera camera = cameraObject.AddComponent<Camera>();
        TerrainRenderer renderer = rendererObject.AddComponent<TerrainRenderer>();
        try
        {
            camera.aspect = 16f / 9f;
            camera.fieldOfView = 30f;
            const float visibleHexes = 12f;
            System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                                   System.Reflection.BindingFlags.NonPublic;
            System.Reflection.MethodInfo distanceMethod = typeof(TerrainRenderer)
                .GetMethod("ZoomDistanceForVisibleHexes", flags);
            System.Reflection.MethodInfo visibleMethod = typeof(TerrainRenderer)
                .GetMethod("VisibleHexesForCurrentCamera", flags);
            System.Reflection.MethodInfo preserveMethod = typeof(TerrainRenderer)
                .GetMethod("SetFieldOfViewPreservingCoverage", flags);
            float baselineDistance = (float)distanceMethod.Invoke(renderer,
                new object[] { camera, visibleHexes });
            typeof(TerrainRenderer).GetField("cameraDistance", flags)
                .SetValue(renderer, baselineDistance);
            typeof(TerrainRenderer).GetField("maxCameraDistance", flags)
                .SetValue(renderer, 1000f);

            float before = (float)visibleMethod.Invoke(renderer, new object[] { camera });
            preserveMethod.Invoke(renderer, new object[] { camera, 50f, before });
            float after = (float)visibleMethod.Invoke(renderer, new object[] { camera });

            Assert.AreEqual(12f, before, 0.0001f);
            Assert.AreEqual(before, after, 0.0001f);
            Assert.AreEqual(50f, camera.fieldOfView, 0.0001f);
            Assert.Less(ReadInternalFloat(renderer, "ActiveCameraDistance"), baselineDistance,
                "FOV 增大后应靠近地图，才能在覆盖范围不变时增强透视");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rendererObject);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    [Test]
    public void CivStyleCamera_ZoomClampsAndFocusTargetClampsToMapGround()
    {
        GameObject root = new GameObject("CivCameraContractTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.SetZoomLevel(-0.5f);
            Assert.AreEqual(0f, renderer.ZoomLevel, 0.0001f);
            renderer.SetZoomLevel(1.5f);
            Assert.AreEqual(1f, renderer.ZoomLevel, 0.0001f);
            renderer.SetZoomLevel(0.4f);
            Assert.AreEqual(0.4f, renderer.ZoomLevel, 0.0001f);

            const System.Reflection.BindingFlags detailFlags = System.Reflection.BindingFlags.Instance |
                                                                System.Reflection.BindingFlags.NonPublic;
            typeof(TerrainRenderer).GetField("currentZoom", detailFlags).SetValue(renderer, 0.1f);
            Assert.AreEqual(TerrainRenderer.MapDetailLevel.Near, renderer.CurrentDetailLevel);
            typeof(TerrainRenderer).GetField("currentZoom", detailFlags).SetValue(renderer, 0.30f);
            Assert.AreEqual(TerrainRenderer.MapDetailLevel.Mid, renderer.CurrentDetailLevel);
            typeof(TerrainRenderer).GetField("currentZoom", detailFlags).SetValue(renderer, 0.60f);
            Assert.AreEqual(TerrainRenderer.MapDetailLevel.Far, renderer.CurrentDetailLevel);

            WorldMap map = BuildMap(LandformType.Plain, LandformType.Plain);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                                           System.Reflection.BindingFlags.NonPublic;
            typeof(TerrainRenderer).GetField("map", flags).SetValue(renderer, map);
            typeof(TerrainRenderer).GetField("targetPivot", flags)
                .SetValue(renderer, new Vector3(-50f, 99f, 0.2f));
            typeof(TerrainRenderer).GetMethod("ClampPivotToMap", flags)
                .Invoke(renderer, null);
            Vector3 target = renderer.TargetPivot;
            Assert.GreaterOrEqual(target.x, 0f);
            Assert.GreaterOrEqual(target.z, 0f);
            Assert.AreEqual(TerrainMeshGenerator.StrategicSurfaceHeight(LandformType.Plain),
                target.y, 0.0001f);
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void Renderer_WorldMapVisualProfileUsesSketchCameraAndOperationalScale()
    {
        GameObject root = new GameObject("TerrainRendererVisualProfileTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                                   System.Reflection.BindingFlags.NonPublic;
            typeof(TerrainRenderer).GetMethod("ApplyWorldMapVisualProfile", flags)
                ?.Invoke(renderer, new object[] { 7 });
            float yaw = (float)typeof(TerrainRenderer).GetProperty("CameraYawDegrees", flags)
                .GetValue(renderer);
            float visibleHexes = (float)typeof(TerrainRenderer).GetProperty("InitialVisibleHexes", flags)
                .GetValue(renderer);
            float minimumVisibleHexes = (float)typeof(TerrainRenderer)
                .GetProperty("MinimumVisibleHexes", flags).GetValue(renderer);
            Assert.AreEqual(12f, yaw, 0.0001f);
            Assert.AreEqual(12f, visibleHexes, 0.0001f);
            Assert.AreEqual(5f, minimumVisibleHexes, 0.0001f);
            Assert.AreEqual(40f, ReadInternalFloat(renderer, "CameraFieldOfViewDegrees"), 0.0001f);
            Assert.AreEqual(45f, ReadInternalFloat(renderer, "NearFieldOfViewDegrees"), 0.0001f);
            Assert.AreEqual(0.35f, renderer.ZoomLevel, 0.0001f);
            Assert.AreEqual(40f, renderer.CameraPitchForZoom(0f), 0.0001f);
            Assert.AreEqual(42f, renderer.CameraPitchForZoom(0.5f), 0.0001f);
            Assert.AreEqual(45f, renderer.CameraPitchForZoom(1f), 0.0001f);
            Assert.Greater(renderer.CameraHeightForZoom(1f),
                renderer.CameraHeightForZoom(0f));
            Assert.AreEqual(45f, InvokeInternalFloat(renderer, "FieldOfViewForVisibleHexes", 5f), 0.0001f);
            Assert.AreEqual(40f, InvokeInternalFloat(renderer, "FieldOfViewForVisibleHexes", 24f), 0.0001f);
            Assert.AreEqual(55f, ReadInternalFloat(renderer, "CameraNearPitchDegrees"), 0.0001f);
            Assert.AreEqual(45f, ReadInternalFloat(renderer, "CameraFarPitchDegrees"), 0.0001f);
            Assert.AreEqual(16f, ReadInternalFloat(renderer, "CameraCurveMaxVisibleHexes"), 0.0001f);
            Assert.AreEqual(0f, ReadInternalFloat(renderer, "NearRadialCurvature"), 0.0001f);
            Assert.AreEqual(0.82f, ReadInternalFloat(renderer, "GroundTextureStrength"), 0.0001f);
            Assert.AreEqual(1.55f, ReadInternalFloat(renderer, "GroundTextureContrast"), 0.0001f);
            Assert.AreEqual(0.46f, ReadInternalFloat(renderer, "GroundTextureTiling"), 0.0001f);
            Assert.AreEqual(0.22f, ReadInternalFloat(renderer, "GroundMacroStrength"), 0.0001f);
            Assert.AreEqual(0.055f, ReadInternalFloat(renderer, "GroundMacroScale"), 0.0001f);
            Assert.AreEqual(0.10f, ReadInternalFloat(renderer, "GroundTextureColorBlend"), 0.0001f);
            Assert.AreEqual(0.55f, ReadInternalFloat(renderer, "GroundNormalStrength"), 0.0001f);
            Assert.AreEqual(2, ReadInternalInt(renderer, "ContinuousSurfaceSubdivisions"));
            Assert.AreEqual(1f, ReadInternalFloat(renderer, "TerrainReliefScale"), 0.0001f);
            Assert.AreEqual(5.5f, ReadInternalFloat(renderer, "NearFogStartHeightFactor"), 0.0001f);
            Assert.AreEqual(1300f / 880f, ReadInternalFloat(renderer, "FarFogStartHeightFactor"), 0.0001f);
            Assert.AreEqual(55f, InvokeInternalFloat(renderer, "CameraPitchForVisibleHexes", 5f), 0.0001f);
            Assert.AreEqual(45f, InvokeInternalFloat(renderer, "CameraPitchForVisibleHexes", 16f), 0.0001f);
            Assert.AreEqual(45f, InvokeInternalFloat(renderer, "CameraPitchForVisibleHexes", 24f), 0.0001f);
            Assert.AreEqual(1f, InvokeInternalStaticFloat("CurveWeightForVisibleHexes", 5f), 0.0001f);
            Assert.AreEqual(1f, InvokeInternalStaticFloat("CurveWeightForVisibleHexes", 12f), 0.0001f);
            Assert.That(InvokeInternalStaticFloat("CurveWeightForVisibleHexes", 18f),
                Is.InRange(0f, 1f));
            Assert.AreEqual(0f, InvokeInternalStaticFloat("CurveWeightForVisibleHexes", 24f), 0.0001f);
            Assert.AreEqual(1f, InvokeInternalStaticFloat("PerspectiveWeightForVisibleHexes", 5f), 0.0001f);
            Assert.AreEqual(1f, InvokeInternalStaticFloat("PerspectiveWeightForVisibleHexes", 12f), 0.0001f);
            Assert.That(InvokeInternalStaticFloat("PerspectiveWeightForVisibleHexes", 18f),
                Is.InRange(0f, 1f));
            Assert.AreEqual(0f, InvokeInternalStaticFloat("PerspectiveWeightForVisibleHexes", 24f), 0.0001f);
            InvokeInternalVoidFloat(renderer, "SetNearFieldOfView", 90f);
            Assert.AreEqual(70f, ReadInternalFloat(renderer, "NearFieldOfViewDegrees"), 0.0001f);
            Assert.AreEqual(70f, InvokeInternalFloat(renderer, "FieldOfViewForVisibleHexes", 5f), 0.0001f);
            Assert.AreEqual(40f, InvokeInternalFloat(renderer, "FieldOfViewForVisibleHexes", 24f), 0.0001f);
            InvokeInternalVoidFloat(renderer, "SetNearFieldOfView", 45f);
            InvokeInternalVoidFloat(renderer, "SetNearRadialCurvature", 0.03f);
            Assert.AreEqual(0.02f, ReadInternalFloat(renderer, "NearRadialCurvature"), 0.0001f);
            InvokeInternalVoidFloat(renderer, "SetNearRadialCurvature", 0.0035f);

            typeof(TerrainRenderer).GetMethod("SetGroundTextureDebug", flags)
                ?.Invoke(renderer, new object[] { 2f, 4f, 0f, true });
            Assert.AreEqual(1f, ReadInternalFloat(renderer, "GroundTextureStrength"), 0.0001f);
            Assert.AreEqual(2.5f, ReadInternalFloat(renderer, "GroundTextureContrast"), 0.0001f);
            Assert.AreEqual(0.05f, ReadInternalFloat(renderer, "GroundTextureTiling"), 0.0001f);
            Assert.IsTrue((bool)typeof(TerrainRenderer).GetProperty("GroundTextureOnly", flags)
                .GetValue(renderer));

            renderer.ApplyTier(WorldMap3DZoomTier.Far);
            Assert.Less(TerrainRenderer.ActiveAppearance.heightScale, 1f,
                "经营地图远景也应压低高度，避免山体遮挡主要操作区");
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static float ReadInternalFloat(TerrainRenderer renderer, string propertyName)
    {
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                               System.Reflection.BindingFlags.NonPublic;
        return (float)typeof(TerrainRenderer).GetProperty(propertyName, flags).GetValue(renderer);
    }

    private static int ReadInternalInt(TerrainRenderer renderer, string propertyName)
    {
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                               System.Reflection.BindingFlags.NonPublic;
        return (int)typeof(TerrainRenderer).GetProperty(propertyName, flags).GetValue(renderer);
    }

    private static float InvokeInternalFloat(TerrainRenderer renderer, string methodName, float argument)
    {
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                               System.Reflection.BindingFlags.NonPublic;
        return (float)typeof(TerrainRenderer).GetMethod(methodName, flags)
            .Invoke(renderer, new object[] { argument });
    }

    private static float InvokeInternalStaticFloat(string methodName, float argument)
    {
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static |
                                               System.Reflection.BindingFlags.NonPublic;
        return (float)typeof(TerrainRenderer).GetMethod(methodName, flags)
            .Invoke(null, new object[] { argument });
    }

    private static void InvokeInternalVoidFloat(TerrainRenderer renderer, string methodName,
        float argument)
    {
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                               System.Reflection.BindingFlags.NonPublic;
        typeof(TerrainRenderer).GetMethod(methodName, flags)
            .Invoke(renderer, new object[] { argument });
    }

    [Test]
    public void Renderer_RadialCurveBendsAllDirectionsAndCanBeDisabled()
    {
        GameObject root = new GameObject("TerrainRendererCurveTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            InvokeCurveState(renderer, Vector3.zero, 0.01f);
            Vector3 right = InvokeInternalVector3(renderer, "CurveWorldPosition",
                new Vector3(10f, 0f, 0f));
            Vector3 left = InvokeInternalVector3(renderer, "CurveWorldPosition",
                new Vector3(-10f, 0f, 0f));
            Vector3 forward = InvokeInternalVector3(renderer, "CurveWorldPosition",
                new Vector3(0f, 0f, 10f));
            Assert.AreEqual(-1f, right.y, 0.0001f);
            Assert.AreEqual(right.y, left.y, 0.0001f);
            Assert.AreEqual(right.y, forward.y, 0.0001f);
            Assert.IsTrue(InvokeCurveIntersection(renderer,
                new Ray(new Vector3(10f, 10f, 0f), Vector3.down), 0f,
                out Vector3 intersection));
            Assert.AreEqual(-1f, intersection.y, 0.0001f,
                "曲面拾取应命中与Shader相同的 y=-k*r^2 表面");

            InvokeCurveState(renderer, Vector3.zero, 0f);
            Vector3 flat = InvokeInternalVector3(renderer, "CurveWorldPosition",
                new Vector3(10f, 2f, 0f));
            Assert.AreEqual(2f, flat.y, 0.0001f);
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void InvokeCurveState(TerrainRenderer renderer, Vector3 origin, float strength)
    {
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                               System.Reflection.BindingFlags.NonPublic;
        typeof(TerrainRenderer).GetMethod("SetCurveState", flags)
            .Invoke(renderer, new object[] { origin, strength });
    }

    private static Vector3 InvokeInternalVector3(TerrainRenderer renderer, string methodName,
        Vector3 argument)
    {
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                               System.Reflection.BindingFlags.NonPublic;
        return (Vector3)typeof(TerrainRenderer).GetMethod(methodName, flags)
            .Invoke(renderer, new object[] { argument });
    }

    private static bool InvokeCurveIntersection(TerrainRenderer renderer, Ray ray,
        float baseHeight, out Vector3 point)
    {
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                               System.Reflection.BindingFlags.NonPublic;
        object[] arguments = { ray, baseHeight, Vector3.zero };
        bool result = (bool)typeof(TerrainRenderer).GetMethod("TryIntersectCurvedPlane", flags)
            .Invoke(renderer, arguments);
        point = (Vector3)arguments[2];
        return result;
    }

    [Test]
    public void TerrainMeshAppearance_KeepsStrategicSurfaceFlatAndWeakensSideShading()
    {
        WorldMap map = BuildMap(LandformType.Mountain);
        map.cells[0].height = 1f;
        TerrainMeshAppearance near = new TerrainMeshAppearance
        {
            heightScale = 0.5f,
            sideDarkenFactor = 0.95f
        };
        System.Func<WorldCell, Color32> colorFor = cell => (Color32)TerrainPresentationModels.ColorForCell(cell);
        Mesh full = TerrainMeshGenerator.CreateTerrainChunk(map, colorFor, TerrainMeshAppearance.Default);
        Mesh compressed = TerrainMeshGenerator.CreateTerrainChunk(map, colorFor, near);
        try
        {
            Assert.AreEqual(full.bounds.max.y, compressed.bounds.max.y, 0.0001f,
                "战略陆地表面不应再随表现档位改变高度");
            Assert.AreEqual(full.vertexCount, compressed.vertexCount);

            byte minFullR = 255;
            byte minNearR = 255;
            foreach (Color32 color in full.colors32) minFullR = Math.Min(minFullR, color.r);
            foreach (Color32 color in compressed.colors32) minNearR = Math.Min(minNearR, color.r);
            Assert.Greater(minNearR, minFullR, "近景侧壁暗化更弱，侧面颜色应更亮");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(full);
            UnityEngine.Object.DestroyImmediate(compressed);
        }
    }

    [Test]
    public void TerrainMesh_UsesContinuousWorldSpaceUvsAcrossHexes()
    {
        WorldMap map = BuildMap(LandformType.Plain, LandformType.Plain);
        Mesh mesh = TerrainMeshGenerator.CreateTerrainChunk(map,
            cell => (Color32)TerrainPresentationModels.ColorForCell(cell));
        try
        {
            Assert.That(mesh.uv.Length, Is.EqualTo(mesh.vertexCount));
            for (int index = 0; index < mesh.vertexCount; index++)
            {
                Assert.AreEqual(mesh.vertices[index].x, mesh.uv[index].x, 0.0001f);
                Assert.AreEqual(mesh.vertices[index].z, mesh.uv[index].y, 0.0001f);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private static Vector4 AverageContinuousWeights(BiomeType biome, float moisture)
    {
        WorldMap map = BuildMap(LandformType.Plain, LandformType.Plain,
            LandformType.Plain, LandformType.Plain);
        foreach (WorldCell cell in map.cells)
        {
            cell.biome = biome;
            cell.moisture = moisture;
        }
        GameObject root = new GameObject("BiomeMaterialWeightTest");
        TerrainRenderer renderer = root.AddComponent<TerrainRenderer>();
        try
        {
            renderer.Render(map);
            Mesh mesh = root.GetComponentsInChildren<MeshFilter>(true)
                .Select(filter => filter.sharedMesh)
                .Where(candidate => candidate != null)
                .OrderByDescending(candidate => candidate.vertexCount)
                .First();
            var weights = new List<Vector4>();
            mesh.GetUVs(1, weights);
            Vector4 average = Vector4.zero;
            foreach (Vector4 weight in weights) average += weight;
            return average / Mathf.Max(1, weights.Count);
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

}
