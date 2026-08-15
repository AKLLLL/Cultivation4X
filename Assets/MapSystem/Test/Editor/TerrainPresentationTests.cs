using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cultivation4X.WorldMap;
using NUnit.Framework;
using UnityEngine;

public class TerrainPresentationTests
{
    private static WorldMap BuildMap(params WorldCell[] cells)
    {
        WorldMap map = new WorldMap
        {
            width = cells.Length,
            height = 1,
            cells = cells
        };
        return map;
    }

    private static WorldCell Cell(int index, LandformType landform, BiomeType biome = BiomeType.Grassland,
        float height = 0.5f, float totalAura = 0f)
    {
        return new WorldCell
        {
            index = index,
            coord = new HexCoord(index, 0),
            landform = landform,
            biome = biome,
            height = height,
            totalAura = totalAura,
            elementalAura = new ElementalAura()
        };
    }

    [Test]
    public void MapIconRenderer_RendersOneIconPerSiteAndClearRemovesAll()
    {
        WorldMap map = BuildMap(
            Cell(0, LandformType.Plain),
            Cell(1, LandformType.Hill),
            Cell(2, LandformType.Mountain));
        WorldMapProgressState progress = new WorldMapProgressState();
        progress.mapSites.Add(new MapSiteData { siteId = "village", cellIndex = 0, siteType = MapSiteType.Village });
        progress.mapSites.Add(new MapSiteData { siteId = "cave", cellIndex = 1, siteType = MapSiteType.CaveResidence });
        progress.mapSites.Add(new MapSiteData { siteId = "mine", cellIndex = 2, siteType = MapSiteType.SpiritMine });
        progress.mapSites.Add(new MapSiteData { siteId = "invalid", cellIndex = -1, siteType = MapSiteType.Ruin });

        GameObject root = new GameObject("MapIconRendererTest");
        MapIconRenderer renderer = root.AddComponent<MapIconRenderer>();
        try
        {
            renderer.Render(map, progress);
            Assert.AreEqual(3, renderer.IconCount);
            Assert.AreEqual(3, root.transform.childCount);
            GameObject firstIcon = root.transform.GetChild(0).gameObject;
            renderer.SetFarViewVisible(true);
            Assert.IsFalse(firstIcon.activeSelf);
            renderer.SetFarViewVisible(false);
            Assert.IsTrue(firstIcon.activeSelf);
            renderer.Clear();
            Assert.AreEqual(0, renderer.IconCount);
            Assert.AreEqual(0, root.transform.childCount);
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void TerrainLabel_SetWritesTextAndColor()
    {
        GameObject root = new GameObject("TerrainLabelTest");
        try
        {
            TerrainLabel label = root.AddComponent<TerrainLabel>();
            Color color = new Color(1f, 0f, 0f);
            label.Set("灵气 1.0 · 木", color);
            TextMesh textMesh = root.GetComponent<TextMesh>();
            Assert.NotNull(textMesh);
            Assert.AreEqual("灵气 1.0 · 木", textMesh.text);
            Assert.AreEqual(color, textMesh.color);
            Assert.Greater(textMesh.characterSize, 0.1f);
            Assert.Greater(textMesh.GetComponent<MeshRenderer>().sharedMaterial.renderQueue, 3100);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void PresentationModels_ColorsAndLabelsAreDistinct()
    {
        foreach (MapSiteType siteType in Enum.GetValues(typeof(MapSiteType)))
        {
            Assert.AreNotEqual(default(Color), TerrainPresentationModels.ColorForSite(siteType));
            Assert.IsFalse(string.IsNullOrEmpty(TerrainPresentationModels.SiteLabel(siteType)));
        }

        Color plain = TerrainPresentationModels.ColorForCell(Cell(0, LandformType.Plain, BiomeType.Grassland));
        Color forest = TerrainPresentationModels.ColorForCell(Cell(0, LandformType.Plain, BiomeType.TemperateForest));
        Color mountain = TerrainPresentationModels.ColorForCell(Cell(0, LandformType.Mountain, BiomeType.Alpine));
        Color coast = TerrainPresentationModels.ColorForCell(Cell(0, LandformType.Coast, BiomeType.Coast));
        Color desert = TerrainPresentationModels.ColorForCell(Cell(0, LandformType.Plain, BiomeType.Desert));
        Assert.AreNotEqual(plain, forest);
        Assert.AreNotEqual(plain, mountain);
        float coastDesertDistance = Mathf.Abs(coast.r - desert.r) +
                                    Mathf.Abs(coast.g - desert.g) +
                                    Mathf.Abs(coast.b - desert.b);
        Assert.Greater(coastDesertDistance, 0.25f,
            "海岸与沙漠即使共用沙地纹理，也必须保持可辨认的色相差异");
    }

    [Test]
    public void ClimateDebugColors_ExposeBiomeTemperatureMoistureAndElevation()
    {
        WorldCell dryLowCold = Cell(0, LandformType.Plain, BiomeType.Desert, height: 0.1f);
        dryLowCold.temperature = 0.1f;
        dryLowCold.moisture = 0.1f;
        WorldCell wetHighHot = Cell(1, LandformType.Hill, BiomeType.Rainforest, height: 0.9f);
        wetHighHot.temperature = 0.9f;
        wetHighHot.moisture = 0.9f;

        Assert.AreNotEqual(ClimateDebugColor(dryLowCold, "Biome"),
            ClimateDebugColor(wetHighHot, "Biome"));
        Assert.AreNotEqual(ClimateDebugColor(dryLowCold, "Temperature"),
            ClimateDebugColor(wetHighHot, "Temperature"));
        Assert.AreNotEqual(ClimateDebugColor(dryLowCold, "Moisture"),
            ClimateDebugColor(wetHighHot, "Moisture"));
        Color lowElevation = ClimateDebugColor(dryLowCold, "Elevation");
        Color highElevation = ClimateDebugColor(wetHighHot, "Elevation");
        Assert.Greater(highElevation.grayscale, lowElevation.grayscale);
        Assert.AreEqual(TerrainPresentationModels.ColorForCell(dryLowCold),
            ClimateDebugColor(dryLowCold, "Normal"));
    }

    private static Color ClimateDebugColor(WorldCell cell, string viewName)
    {
        System.Reflection.Assembly assembly = typeof(TerrainPresentationModels).Assembly;
        Type viewType = assembly.GetType("Cultivation4X.WorldMap.WorldMapClimateDebugView");
        Assert.NotNull(viewType);
        System.Reflection.MethodInfo method = typeof(TerrainPresentationModels).GetMethod(
            "ColorForClimateDebug",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        object view = Enum.Parse(viewType, viewName);
        return (Color)method.Invoke(null, new[] { (object)cell, view });
    }

    [Test]
    public void TerrainMeshGenerator_HexCenterMatchesOddRowOffset()
    {
        Vector2 even = TerrainMeshGenerator.HexCenter(new HexCoord(2, 0));
        Vector2 odd = TerrainMeshGenerator.HexCenter(new HexCoord(2, 1));
        Assert.AreEqual(Mathf.Sqrt(3f) * 2f, even.x, 0.0001f);
        Assert.AreEqual(0f, even.y, 0.0001f);
        Assert.AreEqual(Mathf.Sqrt(3f) * 2.5f, odd.x, 0.0001f);
        Assert.AreEqual(1.5f, odd.y, 0.0001f);
    }

    [Test]
    public void TerrainMeshGenerator_BaseHeightsKeepClearHierarchy()
    {
        Assert.Greater(TerrainMeshGenerator.BaseHeight(LandformType.Mountain),
            TerrainMeshGenerator.BaseHeight(LandformType.Hill));
        Assert.Greater(TerrainMeshGenerator.BaseHeight(LandformType.Hill),
            TerrainMeshGenerator.BaseHeight(LandformType.Plain));
        Assert.Greater(TerrainMeshGenerator.BaseHeight(LandformType.Plain),
            TerrainMeshGenerator.BaseHeight(LandformType.Coast));
        Assert.Greater(TerrainMeshGenerator.BaseHeight(LandformType.Coast),
            TerrainMeshGenerator.BaseHeight(LandformType.ShallowWater));
        Assert.Greater(TerrainMeshGenerator.BaseHeight(LandformType.ShallowWater),
            TerrainMeshGenerator.BaseHeight(LandformType.DeepWater));
    }

    [Test]
    public void PresentationModels_DataHeightDoesNotChangeStrategicSurfaceColor()
    {
        Color low = TerrainPresentationModels.ColorForCell(
            Cell(0, LandformType.Plain, BiomeType.Grassland, height: 0.10f));
        Color high = TerrainPresentationModels.ColorForCell(
            Cell(0, LandformType.Plain, BiomeType.Grassland, height: 0.95f));
        Assert.AreEqual(low, high);
    }

    [Test]
    public void MapTestManager_RegenerateCreatesNewMapWithRequestedSeed()
    {
        GameObject root = new GameObject("MapTestManagerRegenerateTest");
        MapTestManager manager = root.AddComponent<MapTestManager>();
        try
        {
            manager.Regenerate(12345);
            WorldMap first = WorldMapSession.Current;
            Assert.NotNull(first);
            Assert.AreEqual(128 * 96, first.cells.Length);

            manager.Regenerate(54321);
            WorldMap second = WorldMapSession.Current;
            Assert.NotNull(second);
            Assert.AreNotEqual(first.effectiveSeed, second.effectiveSeed);
        }
        finally
        {
            WorldMapSession.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void RegionNameRenderer_CreatesOneLabelPerRegionAndTogglesVisibility()
    {
        WorldCell[] cells = new WorldCell[24];
        for (int index = 0; index < cells.Length; index++)
            cells[index] = Cell(index, LandformType.Plain);
        WorldMap map = BuildMap(cells);
        List<int> rangeCells = new List<int>();
        List<int> valleyCells = new List<int>();
        for (int index = 0; index < cells.Length; index++)
        {
            if (index < 12)
            {
                map.cells[index].regionId = "range";
                rangeCells.Add(index);
            }
            else
            {
                map.cells[index].regionId = "valley";
                valleyCells.Add(index);
            }
        }
        map.regions = new List<MapRegionData>
        {
            new MapRegionData
            {
                regionId = "range",
                regionType = MapRegionType.MountainRange,
                regionName = "苍梧山脉",
                centerCellIndex = 0,
                cellIndices = rangeCells
            },
            new MapRegionData
            {
                regionId = "valley",
                regionType = MapRegionType.Valley,
                centerCellIndex = 12,
                cellIndices = valleyCells
            }
        };
        GameObject root = new GameObject("RegionNameRendererTest");
        RegionNameRenderer renderer = root.AddComponent<RegionNameRenderer>();
        try
        {
            renderer.Render(map);
            Assert.AreEqual(2, renderer.LabelCount);
            Assert.IsTrue(root.GetComponentsInChildren<TextMesh>()
                .Any(textMesh => textMesh.text == "苍梧山脉"));
            foreach (TerrainLabel label in root.GetComponentsInChildren<TerrainLabel>())
            {
                Assert.IsTrue(label.IsGroundFixed);
                Assert.Greater(label.GetComponent<TextMesh>().characterSize, 0.2f);
            }
            Assert.Greater(root.GetComponentsInChildren<TerrainLabel>()[0].transform.position.y, 0.2f);

            renderer.SetLabelsActive(false);
            foreach (Transform child in root.transform)
                Assert.IsFalse(child.gameObject.activeSelf);
            renderer.SetLabelsActive(true);
            foreach (Transform child in root.transform)
                Assert.IsTrue(child.gameObject.activeSelf);
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void OverlayShader_IsAvailableAndSupported()
    {
        Shader shader = Shader.Find("Unlit/VertexColor");
        Assert.NotNull(shader);
        Assert.IsTrue(shader.isSupported);
    }

    [Test]
    public void GeneratedMap_NoNaNInHeightsOrTerrainMesh()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
        {
            width = 128,
            height = 96,
            seed = 20260806
        });
        int nanHeights = 0;
        int nanVisual = 0;
        int landCells = 0;
        float maxLandTop = 0f;
        foreach (WorldCell cell in map.cells)
        {
            if (cell == null) continue;
            if (float.IsNaN(cell.height)) nanHeights++;
            float top = TerrainMeshGenerator.VisualTopHeight(map, cell);
            if (float.IsNaN(top)) nanVisual++;
            if (cell.landform != LandformType.DeepWater &&
                cell.landform != LandformType.ShallowWater)
            {
                landCells++;
                maxLandTop = Mathf.Max(maxLandTop, top);
            }
        }
        Assert.AreEqual(0, nanHeights, "cell.height 存在 NaN");
        Assert.AreEqual(0, nanVisual, "VisualTopHeight 存在 NaN");
        Assert.Greater(landCells, 1000, "陆地格子过少");
        Assert.That(maxLandTop, Is.InRange(0.09f, 0.11f),
            "Base terrain stays strategically flat; mountain height is a Region presentation mesh.");

        Mesh mesh = TerrainMeshGenerator.CreateTerrainChunk(map);
        try
        {
            int nanVertices = 0;
            foreach (Vector3 vertex in mesh.vertices)
            {
                if (float.IsNaN(vertex.x) || float.IsNaN(vertex.y) || float.IsNaN(vertex.z))
                    nanVertices++;
            }
            Assert.AreEqual(0, nanVertices, "地形网格存在 NaN 顶点");
            Assert.AreEqual(TerrainMeshGenerator.LandStrategicHeight, mesh.bounds.max.y, 0.0001f);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void RegionOverlayRenderer_BuildsTranslucentOverlayPerLandRegion()
    {
        WorldMap map = BuildMap(
            Cell(0, LandformType.Plain),
            Cell(1, LandformType.Mountain),
            Cell(2, LandformType.Plain),
            Cell(3, LandformType.Hill),
            Cell(4, LandformType.Plain),
            Cell(5, LandformType.DeepWater));
        map.cells[0].regionId = "range";
        map.cells[1].regionId = "range";
        map.cells[2].regionId = "range";
        map.cells[3].regionId = "forest";
        map.cells[4].regionId = "forest";
        map.cells[5].regionId = "sea";
        map.regions = new List<MapRegionData>
        {
            new MapRegionData
            {
                regionId = "range",
                regionType = MapRegionType.MountainRange,
                centerCellIndex = 1,
                cellIndices = new List<int> { 0, 1, 2 }
            },
            new MapRegionData
            {
                regionId = "forest",
                regionType = MapRegionType.Forest,
                centerCellIndex = 3,
                cellIndices = new List<int> { 3, 4 }
            },
            new MapRegionData
            {
                regionId = "sea",
                regionType = MapRegionType.OpenWater,
                centerCellIndex = 5,
                cellIndices = new List<int> { 5 }
            }
        };
        GameObject root = new GameObject("RegionOverlayRendererBuildTest");
        RegionOverlayRenderer renderer = root.AddComponent<RegionOverlayRenderer>();
        try
        {
            renderer.Render(map);
            Assert.AreEqual(2, renderer.OverlayObjectCount, "大海区域应被跳过");
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
                Assert.Less((int)filter.sharedMesh.colors32[0].a, 255, "覆盖层应为半透明");

            MeshFilter rangeFilter = root.transform.Find("RegionOverlay_range").GetComponent<MeshFilter>();
            Color32 borderColor = new Color32(40, 32, 28, 235);
            Vector3[] vertices = rangeFilter.sharedMesh.vertices;
            Color32[] colors = rangeFilter.sharedMesh.colors32;
            int boundaryCount = 0;
            int domeCount = 0;
            for (int index = 0; index < vertices.Length; index++)
            {
                Assert.GreaterOrEqual(vertices[index].y, renderer.OverlayHeight - 0.0001f);
                Assert.LessOrEqual(vertices[index].y, renderer.OverlayHeight + renderer.DomeHeight + 0.0001f);
                if (Mathf.Abs(vertices[index].y - renderer.OverlayHeight) < 0.0001f) boundaryCount++;
                else if (vertices[index].y > renderer.OverlayHeight + 0.1f) domeCount++;
            }
            Assert.Greater(boundaryCount, 0);
            Assert.GreaterOrEqual(boundaryCount, 8, "区域覆盖层应有完整轮廓顶点");
            Assert.Greater(domeCount, 0, "覆盖层应向上拱起成气泡");

            float maxBoundaryLuminance = 0f;
            float maxInteriorLuminance = 0f;
            for (int index = 0; index < vertices.Length; index++)
            {
                float luminance = (colors[index].r + colors[index].g + colors[index].b) / 765f;
                if (Mathf.Abs(vertices[index].y - renderer.OverlayHeight) < 0.0001f)
                    maxBoundaryLuminance = Mathf.Max(maxBoundaryLuminance, luminance);
                else if (vertices[index].y > renderer.OverlayHeight + 0.1f)
                    maxInteriorLuminance = Mathf.Max(maxInteriorLuminance, luminance);
            }
            Assert.Greater(maxInteriorLuminance, maxBoundaryLuminance, "气泡内部应比边界更亮");

            MeshFilter borderFilter = root.transform.Find("RegionBorders").GetComponent<MeshFilter>();
            Assert.Greater(borderFilter.sharedMesh.vertexCount, 0);
            int borderCount = 0;
            foreach (Color32 color in borderFilter.sharedMesh.colors32)
                if (SameColor(color, borderColor)) borderCount++;
            Assert.Greater(borderCount, 0, "统一边界网格应包含深色边界线");
            bool hasHighestBorder = false;
            float strategicBorderHeight = TerrainMeshGenerator.LandStrategicHeight +
                                          TerrainPresentationModels.RegionOverlayBaseOffset + 0.05f;
            foreach (Vector3 vertex in borderFilter.sharedMesh.vertices)
            {
                Assert.GreaterOrEqual(vertex.y, strategicBorderHeight - 0.0001f,
                    "边界线应贴近战略扁平地表");
                Assert.LessOrEqual(vertex.y, renderer.OverlayHeight + 0.05f + 0.0001f);
                if (Mathf.Abs(vertex.y - (renderer.OverlayHeight + 0.05f)) < 0.0001f)
                    hasHighestBorder = true;
            }
            Assert.IsTrue(hasHighestBorder, "应存在贴近统一战略地表的区域边界线");
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void RegionOverlayRenderer_DoesNotModifyTerrainColors()
    {
        WorldMap map = BuildMap(
            Cell(0, LandformType.Plain),
            Cell(1, LandformType.Mountain),
            Cell(2, LandformType.Plain));
        map.cells[0].regionId = "range";
        map.cells[1].regionId = "range";
        map.cells[2].regionId = "range";
        map.regions = new List<MapRegionData>
        {
            new MapRegionData
            {
                regionId = "range",
                regionType = MapRegionType.MountainRange,
                centerCellIndex = 1,
                cellIndices = new List<int> { 0, 1, 2 }
            }
        };
        GameObject terrainRoot = new GameObject("TerrainRootOverlayTest");
        TerrainRenderer terrain = terrainRoot.AddComponent<TerrainRenderer>();
        GameObject overlayRoot = new GameObject("OverlayRootTest");
        RegionOverlayRenderer overlay = overlayRoot.AddComponent<RegionOverlayRenderer>();
        try
        {
            terrain.Render(map);
            Color32[] before =
                terrainRoot.transform.GetChild(0).GetComponent<MeshFilter>().sharedMesh.colors32;
            overlay.Render(map);
            Color32[] after =
                terrainRoot.transform.GetChild(0).GetComponent<MeshFilter>().sharedMesh.colors32;
            Assert.AreEqual(before.Length, after.Length);
            for (int index = 0; index < before.Length; index++)
                Assert.AreEqual(before[index], after[index], "区域覆盖层不得修改地形顶点颜色");
        }
        finally
        {
            terrain.Clear();
            overlay.Clear();
            UnityEngine.Object.DestroyImmediate(terrainRoot);
            UnityEngine.Object.DestroyImmediate(overlayRoot);
        }
    }

    [Test]
    public void RegionOverlayRenderer_TogglesVisibility()
    {
        WorldMap map = BuildMap(Cell(0, LandformType.Plain), Cell(1, LandformType.Plain));
        map.cells[0].regionId = "range";
        map.cells[1].regionId = "range";
        map.regions = new List<MapRegionData>
        {
            new MapRegionData
            {
                regionId = "range",
                regionType = MapRegionType.Hills,
                centerCellIndex = 0,
                cellIndices = new List<int> { 0, 1 }
            }
        };
        GameObject root = new GameObject("RegionOverlayRendererToggleTest");
        RegionOverlayRenderer renderer = root.AddComponent<RegionOverlayRenderer>();
        try
        {
            renderer.Render(map);
            GameObject overlayObject = root.transform.GetChild(0).gameObject;
            Assert.IsFalse(overlayObject.activeSelf);
            renderer.SetOverlayVisible(true);
            Assert.IsTrue(overlayObject.activeSelf);
            renderer.SetOverlayVisible(false);
            Assert.IsFalse(overlayObject.activeSelf);
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void RegionOverlayRenderer_FiltersToLargerHalf()
    {
        WorldMap map = BuildMap(
            Cell(0, LandformType.Plain),
            Cell(1, LandformType.Plain),
            Cell(2, LandformType.Plain),
            Cell(3, LandformType.Plain),
            Cell(4, LandformType.Plain),
            Cell(5, LandformType.Plain),
            Cell(6, LandformType.Plain),
            Cell(7, LandformType.Plain));
        string[] regionIds = { "tiny", "small", "large" };
        int[] sizes = { 1, 2, 5 };
        int cellCursor = 0;
        for (int regionIndex = 0; regionIndex < regionIds.Length; regionIndex++)
        {
            List<int> cells = new List<int>();
            for (int count = 0; count < sizes[regionIndex]; count++)
            {
                map.cells[cellCursor].regionId = regionIds[regionIndex];
                cells.Add(cellCursor);
                cellCursor++;
            }
            map.regions.Add(new MapRegionData
            {
                regionId = regionIds[regionIndex],
                regionType = regionIndex == 2 ? MapRegionType.Hills : MapRegionType.SmallHill,
                centerCellIndex = cells[0],
                cellIndices = cells
            });
        }
        GameObject root = new GameObject("RegionOverlayRendererFilterTest");
        RegionOverlayRenderer renderer = root.AddComponent<RegionOverlayRenderer>();
        try
        {
            renderer.Render(map);
            Assert.AreEqual(2, renderer.OverlayObjectCount, "应只覆盖面积较大的约一半区域");
            Assert.NotNull(root.transform.Find("RegionOverlay_large"));
            Assert.NotNull(root.transform.Find("RegionOverlay_small"));
            Assert.IsNull(root.transform.Find("RegionOverlay_tiny"));
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void RegionOverlayRenderer_CutsHolesInsideEnclosingRegion()
    {
        const int width = 3;
        const int height = 3;
        WorldMap map = new WorldMap
        {
            width = width,
            height = height,
            cells = new WorldCell[width * height]
        };
        List<int> ringCells = new List<int>();
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
                    height = 0.5f
                };
                if (index != 4)
                {
                    map.cells[index].regionId = "ring";
                    ringCells.Add(index);
                }
                else
                {
                    map.cells[index].regionId = "lake";
                }
            }
        }
        map.regions = new List<MapRegionData>
        {
            new MapRegionData
            {
                regionId = "ring",
                regionType = MapRegionType.MountainRange,
                centerCellIndex = 0,
                cellIndices = ringCells
            },
            new MapRegionData
            {
                regionId = "lake",
                regionType = MapRegionType.OpenWater,
                centerCellIndex = 4,
                cellIndices = new List<int> { 4 }
            }
        };
        GameObject root = new GameObject("RegionOverlayRendererHoleTest");
        RegionOverlayRenderer renderer = root.AddComponent<RegionOverlayRenderer>();
        try
        {
            renderer.Render(map);
            Assert.AreEqual(1, renderer.OverlayObjectCount, "湖泊区域（大海类型）应被跳过");
            MeshFilter ringFilter = root.transform.Find("RegionOverlay_ring").GetComponent<MeshFilter>();
            Vector3[] vertices = ringFilter.sharedMesh.vertices;
            int[] triangles = ringFilter.sharedMesh.triangles;
            Vector2 holeCenter = TerrainMeshGenerator.HexCenter(new HexCoord(1, 1));
            for (int index = 0; index + 2 < triangles.Length; index += 3)
            {
                Vector2 a = new Vector2(vertices[triangles[index]].x, vertices[triangles[index]].z);
                Vector2 b = new Vector2(vertices[triangles[index + 1]].x, vertices[triangles[index + 1]].z);
                Vector2 c = new Vector2(vertices[triangles[index + 2]].x, vertices[triangles[index + 2]].z);
                Assert.IsFalse(PointInTriangle(holeCenter, a, b, c), "孔洞中心不应被覆盖");
            }
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void RegionOverlayRenderer_MergesSharedBorderSegments()
    {
        WorldMap map = BuildMap(
            Cell(0, LandformType.Plain),
            Cell(1, LandformType.Plain));
        map.cells[0].regionId = "a";
        map.cells[1].regionId = "b";
        map.regions = new List<MapRegionData>
        {
            new MapRegionData
            {
                regionId = "a",
                regionType = MapRegionType.Hills,
                centerCellIndex = 0,
                cellIndices = new List<int> { 0 }
            },
            new MapRegionData
            {
                regionId = "b",
                regionType = MapRegionType.Valley,
                centerCellIndex = 1,
                cellIndices = new List<int> { 1 }
            }
        };
        GameObject root = new GameObject("RegionOverlayRendererBorderMergeTest");
        RegionOverlayRenderer renderer = root.AddComponent<RegionOverlayRenderer>();
        try
        {
            renderer.Render(map);
            MeshFilter borderFilter = root.transform.Find("RegionBorders").GetComponent<MeshFilter>();
            // 两个相邻六边形：12 条轮廓边，共享边去重后 11 条，每条 4 个顶点。
            Assert.AreEqual(11 * 4, borderFilter.sharedMesh.vertexCount);
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void TerrainMeshGenerator_ProjectedHexWidthTracksCameraHeading()
    {
        System.Reflection.MethodInfo method = typeof(TerrainMeshGenerator).GetMethod(
            "ProjectedHexWidth", System.Reflection.BindingFlags.Static |
                                 System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        float straight = (float)method.Invoke(null, new object[] { Vector3.right });
        Vector3 rotatedRight = Quaternion.Euler(0f, 30f, 0f) * Vector3.right;
        float rotated = (float)method.Invoke(null, new object[] { rotatedRight });
        Assert.AreEqual(Mathf.Sqrt(3f), straight, 0.0001f);
        Assert.AreEqual(TerrainMeshGenerator.HexDiameter, rotated, 0.0001f);
    }

    [Test]
    public void MapTestManager_StrategicFocusIsBuildableAndNearLargeTerrain()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
        {
            width = 48,
            height = 36,
            seed = 20260806
        });
        System.Reflection.MethodInfo selector = typeof(MapTestManager).GetMethod(
            "SelectStrategicFocusCell", System.Reflection.BindingFlags.Static |
                                        System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(selector);
        int focus = (int)selector.Invoke(null, new object[] { map });
        Assert.GreaterOrEqual(focus, 0);
        WorldCell cell = map.cells[focus];
        Assert.NotNull(cell);
        Assert.IsTrue(cell.isBuildable);
        Assert.AreNotEqual(LandformType.Mountain, cell.landform);
        Assert.IsTrue(map.cells.Any(other => other != null &&
            HexCoord.Distance(cell.coord, other.coord) <= 4 &&
            (other.landform == LandformType.Mountain || other.landform == LandformType.Hill)),
            "初始经营构图应让可建宗位置与山势同时进入画面");
    }

    [Test]
    public void RegionOverlayRenderer_PoliticalModeSwitchHidesOverlay()
    {
        WorldMap map = BuildMap(Cell(0, LandformType.Plain), Cell(1, LandformType.Plain));
        map.cells[0].regionId = "a";
        map.cells[1].regionId = "a";
        map.regions = new List<MapRegionData>
        {
            new MapRegionData
            {
                regionId = "a",
                regionType = MapRegionType.Hills,
                centerCellIndex = 0,
                cellIndices = new List<int> { 0, 1 }
            }
        };
        GameObject root = new GameObject("RegionOverlayModeSwitchTest");
        RegionOverlayRenderer renderer = root.AddComponent<RegionOverlayRenderer>();
        try
        {
            renderer.Render(map);
            GameObject overlayObject = root.transform.GetChild(0).gameObject;
            renderer.SetOverlayVisible(true);
            Assert.IsTrue(overlayObject.activeSelf);

            renderer.SetPoliticalMapEnabled(false);
            Assert.IsFalse(overlayObject.activeSelf, "政治地图关闭时覆盖层应隐藏");

            renderer.SetPoliticalMapEnabled(true);
            Assert.IsFalse(overlayObject.activeSelf, "无相机时按缩放规则保持隐藏");
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void RegionNameRenderer_PoliticalModeSwitchHidesLabels()
    {
        WorldCell[] cells = new WorldCell[24];
        for (int index = 0; index < cells.Length; index++)
            cells[index] = Cell(index, LandformType.Plain);
        WorldMap map = BuildMap(cells);
        List<int> rangeCells = new List<int>();
        for (int index = 0; index < cells.Length; index++)
        {
            map.cells[index].regionId = "range";
            rangeCells.Add(index);
        }
        map.regions = new List<MapRegionData>
        {
            new MapRegionData
            {
                regionId = "range",
                regionType = MapRegionType.MountainRange,
                centerCellIndex = 0,
                cellIndices = rangeCells
            }
        };
        GameObject root = new GameObject("RegionNameModeSwitchTest");
        RegionNameRenderer renderer = root.AddComponent<RegionNameRenderer>();
        try
        {
            renderer.Render(map);
            Assert.AreEqual(1, renderer.LabelCount);
            GameObject labelObject = root.transform.GetChild(0).gameObject;
            renderer.SetLabelsActive(true);
            Assert.IsTrue(labelObject.activeSelf);

            renderer.SetPoliticalMapEnabled(false);
            Assert.IsFalse(labelObject.activeSelf, "政治地图关闭时区域名应隐藏");

            renderer.SetPoliticalMapEnabled(true);
            Assert.IsFalse(labelObject.activeSelf, "无相机时按缩放规则保持隐藏");
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void MapIconRenderer_PoliticalModeSwitchKeepsIconsVisible()
    {
        WorldMap map = BuildMap(Cell(0, LandformType.Plain));
        WorldMapProgressState progress = new WorldMapProgressState();
        progress.mapSites.Add(new MapSiteData { siteId = "village", cellIndex = 0, siteType = MapSiteType.Village });
        GameObject root = new GameObject("MapIconModeSwitchTest");
        MapIconRenderer renderer = root.AddComponent<MapIconRenderer>();
        try
        {
            renderer.Render(map, progress);
            GameObject iconObject = root.transform.GetChild(0).gameObject;
            renderer.SetPoliticalMapEnabled(false);
            Assert.IsTrue(iconObject.activeSelf, "政治地图关闭时图标应保持可见");
            renderer.SetPoliticalMapEnabled(true);
            Assert.IsTrue(iconObject.activeSelf, "无相机时图标保持可见");
        }
        finally
        {
            renderer.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void WorldMap3DPolicy_TierThresholdsAndDisplayRules()
    {
        Assert.AreEqual(WorldMap3DZoomTier.Near, WorldMap3DPresentationPolicy.GetZoomTier(0f));
        Assert.AreEqual(WorldMap3DZoomTier.Near, WorldMap3DPresentationPolicy.GetZoomTier(11.99f));
        Assert.AreEqual(WorldMap3DZoomTier.Mid, WorldMap3DPresentationPolicy.GetZoomTier(12f));
        Assert.AreEqual(WorldMap3DZoomTier.Mid, WorldMap3DPresentationPolicy.GetZoomTier(23.99f));
        Assert.AreEqual(WorldMap3DZoomTier.Far, WorldMap3DPresentationPolicy.GetZoomTier(24f));
        Assert.AreEqual(WorldMap3DZoomTier.Far, WorldMap3DPresentationPolicy.GetZoomTier(120f));

        Assert.IsTrue(WorldMap3DPresentationPolicy.ShowRegionLabels(WorldMap3DZoomTier.Far));
        Assert.IsFalse(WorldMap3DPresentationPolicy.ShowRegionLabels(WorldMap3DZoomTier.Near));
        Assert.IsTrue(WorldMap3DPresentationPolicy.ShowRegionOverlays(WorldMap3DZoomTier.Far));
        Assert.IsFalse(WorldMap3DPresentationPolicy.ShowRegionOverlays(WorldMap3DZoomTier.Mid));
        Assert.IsTrue(WorldMap3DPresentationPolicy.ShowSiteIcons(WorldMap3DZoomTier.Near));
        Assert.IsFalse(WorldMap3DPresentationPolicy.ShowSiteIcons(WorldMap3DZoomTier.Far));
        Assert.AreEqual(0f, WorldMap3DPresentationPolicy.TerrainMarkerOpacity(22f));
        Assert.That(WorldMap3DPresentationPolicy.TerrainMarkerOpacity(28f), Is.InRange(0.45f, 0.55f));
        Assert.AreEqual(1f, WorldMap3DPresentationPolicy.TerrainMarkerOpacity(34f));
        Assert.AreEqual(1f, WorldMap3DPresentationPolicy.TerrainStructureOpacity(26f));
        Assert.That(WorldMap3DPresentationPolicy.TerrainStructureOpacity(32f), Is.InRange(0.45f, 0.55f));
        Assert.AreEqual(0f, WorldMap3DPresentationPolicy.TerrainStructureOpacity(38f));
        Assert.AreEqual(1f, WorldMap3DPresentationPolicy.TerrainDetailOpacity(10f));
        Assert.That(WorldMap3DPresentationPolicy.TerrainDetailOpacity(14f), Is.InRange(0.45f, 0.55f));
        Assert.AreEqual(0f, WorldMap3DPresentationPolicy.TerrainDetailOpacity(18f));
    }

    [Test]
    public void WorldMap3DPolicy_SelectsLargestRegionsWithinFarLimit()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
        {
            width = 128,
            height = 96,
            seed = 20260806
        });
        int minimum = TerrainPresentationModels.RegionOverlayMinimumCells(map);
        List<MapRegionData> selected = WorldMap3DPresentationPolicy.SelectRegionLabels(map, map.regions,
            minimum, WorldMapRegionPresentationPolicy.FarRegionLabelLimit);

        Assert.Greater(selected.Count, 0);
        Assert.LessOrEqual(selected.Count, WorldMapRegionPresentationPolicy.FarRegionLabelLimit);
        Assert.IsTrue(selected.All(region =>
            region.regionType != MapRegionType.OpenWater &&
            region.cellIndices.Count >= minimum));
        for (int index = 1; index < selected.Count; index++)
            Assert.GreaterOrEqual(selected[index - 1].cellIndices.Count, selected[index].cellIndices.Count,
                "应按区域面积降序选择");
    }

    [Test]
    public void RegionRenderers_CapFarViewLabelsAndOverlaysToPolicyLimit()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
        {
            width = 128,
            height = 96,
            seed = 20260806
        });
        GameObject nameRoot = new GameObject("RegionNameCapTest");
        RegionNameRenderer names = nameRoot.AddComponent<RegionNameRenderer>();
        GameObject overlayRoot = new GameObject("RegionOverlayCapTest");
        RegionOverlayRenderer overlays = overlayRoot.AddComponent<RegionOverlayRenderer>();
        try
        {
            names.Render(map);
            overlays.Render(map);
            Assert.Greater(names.LabelCount, 0);
            Assert.LessOrEqual(names.LabelCount, WorldMapRegionPresentationPolicy.FarRegionLabelLimit);
            Assert.Greater(overlays.OverlayObjectCount, 0);
            Assert.LessOrEqual(overlays.OverlayObjectCount,
                WorldMapRegionPresentationPolicy.FarRegionLabelLimit);
        }
        finally
        {
            names.Clear();
            overlays.Clear();
            UnityEngine.Object.DestroyImmediate(nameRoot);
            UnityEngine.Object.DestroyImmediate(overlayRoot);
        }
    }

    [Test]
    public void ColorForCell_DistinguishesBuildableMountainPlateau()
    {
        WorldCell ordinary = Cell(0, LandformType.Mountain, BiomeType.Alpine);
        WorldCell plateau = Cell(1, LandformType.Mountain, BiomeType.Alpine);
        plateau.isBuildable = true;

        Color ordinaryColor = TerrainPresentationModels.ColorForCell(ordinary);
        Color plateauColor = TerrainPresentationModels.ColorForCell(plateau);

        Assert.AreNotEqual(ordinaryColor, plateauColor, "台地必须与普通山地有颜色差异");
        Assert.Greater(plateauColor.r, ordinaryColor.r, "台地应更暖");
        Assert.Less(plateauColor.b, ordinaryColor.b, "台地应更暖");
    }

    [Test]
    public void LandformFillColor_UsesUnifiedWarmPlateauTone()
    {
        MethodInfo method = typeof(WorldMapPresenter).GetMethod("LandformFillColor",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        WorldCell ordinaryAlpine = Cell(0, LandformType.Mountain, BiomeType.Alpine);
        WorldCell plateauAlpine = Cell(1, LandformType.Mountain, BiomeType.Alpine);
        plateauAlpine.isBuildable = true;
        WorldCell plateauSnowfield = Cell(2, LandformType.Mountain, BiomeType.Snowfield);
        plateauSnowfield.isBuildable = true;

        Color ordinaryColor = (Color)method.Invoke(null, new object[] { ordinaryAlpine });
        Color alpinePlateauColor = (Color)method.Invoke(null, new object[] { plateauAlpine });
        Color snowfieldPlateauColor = (Color)method.Invoke(null, new object[] { plateauSnowfield });

        Assert.AreNotEqual(ordinaryColor, alpinePlateauColor, "2D 台地色必须区别于普通山地");
        Assert.AreEqual(alpinePlateauColor, snowfieldPlateauColor,
            "2D 台地必须使用统一暖岩色，不受 Alpine/Snowfield 群系染色影响");
    }

    [Test]
    public void IsSelectablePlateau_AcceptsOnlyBuildableMountain()
    {
        MethodInfo method = typeof(WorldMapPresenter).GetMethod("IsSelectablePlateau",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        WorldCell plateau = Cell(0, LandformType.Mountain, BiomeType.Alpine);
        plateau.isBuildable = true;
        WorldCell ordinaryMountain = Cell(1, LandformType.Mountain, BiomeType.Alpine);
        WorldCell buildablePlain = Cell(2, LandformType.Plain, BiomeType.Grassland);
        buildablePlain.isBuildable = true;

        Assert.IsTrue((bool)method.Invoke(null, new object[] { plateau }));
        Assert.IsFalse((bool)method.Invoke(null, new object[] { ordinaryMountain }));
        Assert.IsFalse((bool)method.Invoke(null, new object[] { buildablePlain }));
        Assert.IsFalse((bool)method.Invoke(null, new object[] { null }));
    }

    private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross2(a, b, point);
        float d2 = Cross2(b, c, point);
        float d3 = Cross2(c, a, point);
        bool hasNegative = d1 < -1e-5f || d2 < -1e-5f || d3 < -1e-5f;
        bool hasPositive = d1 > 1e-5f || d2 > 1e-5f || d3 > 1e-5f;
        return !(hasNegative && hasPositive);
    }

    private static float Cross2(Vector2 a, Vector2 b, Vector2 c) =>
        (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

    private static bool SameColor(Color32 a, Color32 b) =>
        a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

}
