using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;
using NUnit.Framework;
using UnityEngine;

public class WorldMap3DOverlayTests
{
    [Test]
    public void HexGeometry_IsSingleSourceForTerrainAndOverlays()
    {
        HexCoord even = new HexCoord(2, 0);
        HexCoord odd = new HexCoord(2, 1);
        Assert.AreEqual(TerrainMeshGenerator.HexCenter(even), HexGeometry.GetCenter(even));
        Assert.AreEqual(TerrainMeshGenerator.HexCenter(odd), HexGeometry.GetCenter(odd));

        foreach (HexCoord coord in new[] { even, odd })
        {
            Vector2 center = HexGeometry.GetCenter(coord);
            Vector2[] corners = HexGeometry.GetCorners(coord);
            Assert.AreEqual(6, corners.Length);
            foreach (Vector2 corner in corners)
                Assert.AreEqual(HexGeometry.GetRadius(),
                    Vector2.Distance(center, corner), 0.0001f);
            HexCoord restored = HexGeometry.GetCoordFromWorld(
                new Vector3(center.x, 0.5f, center.y));
            Assert.AreEqual(coord, restored);
        }

        WorldMap map = CreateSmallMap();
        int centerIndex = map.cells.Length / 2;
        Vector3 world = new Vector3(
            HexGeometry.GetCenter(map.cells[centerIndex].coord).x, 0f,
            HexGeometry.GetCenter(map.cells[centerIndex].coord).y);
        Assert.IsTrue(TerrainMeshGenerator.TryGetCellIndex(map, world, out int terrainIndex));
        Assert.IsTrue(HexGeometry.TryGetCellIndex(map, world, out int geometryIndex));
        Assert.AreEqual(centerIndex, terrainIndex);
        Assert.AreEqual(terrainIndex, geometryIndex);
    }

    [Test]
    public void MapPresentationLayer_ProvidesHeightAboveCellTerrain()
    {
        WorldMap map = CreateSmallMap(7304);
        WorldCell cell = map.cells[map.cells.Length / 2];
        float radiusScale = 0.92f;
        float height = MapPresentationLayer.GetCellHeight(map, cell, radiusScale);
        Vector2 center = HexGeometry.GetCenter(cell);
        Assert.GreaterOrEqual(height,
            TerrainRenderer.PresentationSurfaceHeightAt(map, center, cell) +
            MapPresentationLayer.TerrainClearance - 0.0001f);
        foreach (Vector2 corner in HexGeometry.GetCorners(center, HexGeometry.GetRadius() * radiusScale))
            Assert.GreaterOrEqual(height,
                TerrainRenderer.PresentationSurfaceHeightAt(map, corner, cell) +
                MapPresentationLayer.TerrainClearance - 0.0001f);
        Assert.GreaterOrEqual(MapPresentationLayer.GetIconHeight(map, cell),
            MapPresentationLayer.GetHeight(map, cell));
    }

    [Test]
    public void OverlayMaterial_UsesOverlayShaderWithZTestAlways()
    {
        Material material = WorldMapHexOverlayGeometry.CreateVertexColorMaterial("OverlayTest", true);
        try
        {
            Assert.IsNotNull(material.shader);
            Assert.AreEqual("Unlit/VertexColorOverlay", material.shader.name);
            Assert.AreEqual(4000, material.renderQueue);
            Assert.IsTrue(material.HasProperty("_ZTest"));
            Assert.AreEqual((int)UnityEngine.Rendering.CompareFunction.Always, material.GetInt("_ZTest"));
        }
        finally
        {
            Object.DestroyImmediate(material);
        }
    }

    [Test]
    public void MapOverlayMeshBuilder_BuildsSurfaceFollowingHexOverlay()
    {
        WorldMap map = CreateSmallMap(7305);
        WorldCell cell = map.cells[map.cells.Length / 2];
        Mesh mesh = MapOverlayMeshBuilder.BuildHexOverlay(map, cell, 0.92f,
            new Color(1f, 0f, 0f, 0.5f), new Color(1f, 1f, 1f, 1f), 0.1f,
            "SurfaceFollowingOverlay");
        try
        {
            Assert.IsNotNull(mesh);
            // 中心 1 + 6 角点 + 6 条边 * 4 顶点 = 31。
            Assert.AreEqual(31, mesh.vertexCount);
            Vector2[] corners = MapOverlayMeshBuilder.GetHexCorners(cell, 0.92f);
            float[] heights = MapOverlayMeshBuilder.SampleTerrainHeights(map, cell, corners);
            Assert.AreEqual(6, heights.Length);
            foreach (Vector2 corner in corners)
                Assert.GreaterOrEqual(heights[System.Array.IndexOf(corners, corner)],
                    TerrainRenderer.PresentationSurfaceHeightAt(map, corner, cell) +
                    MapPresentationLayer.TerrainClearance - 0.0001f);
        }
        finally
        {
            Object.DestroyImmediate(mesh);
        }
    }

    [TearDown]
    public void TearDown()
    {
        WorldMapSession.Clear();
    }

    private static WorldMap CreateSmallMap(int seed = 7301)
    {
        return WorldGenerator.Generate(new MapGenerationSettings
        {
            width = 16,
            height = 16,
            seed = seed
        });
    }

    [Test]
    public void CollectKnownCellIndices_UnionsRevealedAndInfluenceCells()
    {
        WorldMap map = CreateSmallMap();
        var progress = new WorldMapProgressState
        {
            revealedCellIndices = new List<int> { 0 },
            influenceSources = new List<InfluenceSourceData>
            {
                new InfluenceSourceData
                {
                    sourceId = "base", sourceType = InfluenceSourceType.SectBase,
                    cellIndex = 20, controllerSectId = "player_sect",
                    baseStrength = 100, radius = 1, isActive = true
                }
            },
            isInfluenceDirty = true
        };

        HashSet<int> known = WorldMapInfluenceRules.CollectKnownCellIndices(map, progress, false);

        Assert.IsTrue(known.Contains(0), "显式揭示格必须计入已知集合");
        Assert.IsTrue(known.Contains(20), "影响源格必须计入已知集合");
        Assert.IsTrue(known.Count < map.cells.Length, "普通未知格不应计入");
        HashSet<int> all = WorldMapInfluenceRules.CollectKnownCellIndices(map, progress, true);
        Assert.AreEqual(map.cells.Length, all.Count);
    }

    [Test]
    public void KnowledgeMaskRenderer_CoversOnlyUnknownCellsAndDoesNotBlockRaycasts()
    {
        WorldMap map = CreateSmallMap();
        HashSet<int> known = new HashSet<int> { 0, 1, 2 };
        GameObject root = new GameObject("KnowledgeMaskTest");
        try
        {
            WorldMapKnowledgeMaskRenderer renderer =
                root.AddComponent<WorldMapKnowledgeMaskRenderer>();
            renderer.Render(map, known);

            Assert.AreEqual(map.cells.Length - known.Count, renderer.HiddenCellCount);
            Assert.IsTrue(renderer.MaskVisible);
            MeshRenderer meshRenderer = root.GetComponentInChildren<MeshRenderer>();
            Assert.NotNull(meshRenderer);
            Assert.IsNull(meshRenderer.GetComponent<Collider>(),
                "知识遮罩不能添加碰撞体，否则会挡住 TerrainRenderer.TryPickCell");
            Mesh fogMesh = meshRenderer.GetComponent<MeshFilter>().sharedMesh;
            Assert.NotNull(fogMesh);
            Assert.Less(fogMesh.vertexCount, renderer.HiddenCellCount * 7,
                "认知迷雾应共享相邻格角点，而不是逐格生成独立六边形");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void InfluenceOverlayRenderer_RendersOnlyNonZeroLevelCells()
    {
        WorldMap map = CreateSmallMap();
        var progress = new WorldMapProgressState
        {
            cellInfluences = new List<CellInfluenceState>
            {
                new CellInfluenceState
                {
                    cellIndex = 3, value = 100, level = InfluenceLevel.Core,
                    controllerSectId = "player_sect", sourceIds = new List<string> { "base" }
                },
                new CellInfluenceState
                {
                    cellIndex = 4, value = 20, level = InfluenceLevel.Outer,
                    controllerSectId = "player_sect", sourceIds = new List<string> { "base" }
                },
                new CellInfluenceState
                {
                    cellIndex = 5, value = 0, level = InfluenceLevel.None,
                    controllerSectId = "player_sect", sourceIds = new List<string> { "base" }
                }
            }
        };
        GameObject root = new GameObject("InfluenceOverlayTest");
        try
        {
            WorldMapInfluenceOverlayRenderer renderer =
                root.AddComponent<WorldMapInfluenceOverlayRenderer>();
            renderer.Render(map, progress);

            Assert.AreEqual(2, renderer.OverlayCellCount);
            Assert.NotNull(root.GetComponentInChildren<MeshRenderer>());
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void SelectionOverlayRenderer_HighlightsBuildableMountainPlateaus()
    {
        WorldMap map = CreateSmallMap();
        foreach (WorldCell cell in map.cells)
        {
            cell.landform = LandformType.Plain;
            cell.biome = BiomeType.Grassland;
            cell.isBuildable = false;
        }
        WorldCell plateau = map.cells[10];
        plateau.landform = LandformType.Mountain;
        plateau.biome = BiomeType.Alpine;
        plateau.isBuildable = true;
        WorldCell ordinaryMountain = map.cells[11];
        ordinaryMountain.landform = LandformType.Mountain;
        ordinaryMountain.biome = BiomeType.Alpine;
        ordinaryMountain.isBuildable = false;

        GameObject root = new GameObject("SelectionOverlayTest");
        try
        {
            WorldMapSelectionOverlayRenderer renderer =
                root.AddComponent<WorldMapSelectionOverlayRenderer>();
            renderer.Render(map, -1, true);
            Assert.AreEqual(1, renderer.PlateauCount);

            renderer.Render(map, 7, false);
            Assert.AreEqual(0, renderer.PlateauCount);
            Assert.AreEqual(7, renderer.SelectedCellIndex);
            Assert.GreaterOrEqual(root.GetComponentsInChildren<MeshRenderer>().Length, 1);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void VeinOverlayRenderer_RendersGeneratedVeinSegments()
    {
        WorldMap map = CreateSmallMap(7302);
        GameObject root = new GameObject("VeinOverlayTest");
        try
        {
            WorldMapVeinOverlayRenderer renderer =
                root.AddComponent<WorldMapVeinOverlayRenderer>();
            renderer.Render(map);

            Assert.Greater(renderer.VeinSegmentCount, 0);
            Assert.IsNotNull(root.GetComponentInChildren<MeshRenderer>(true));
            renderer.SetVisible(false);
            Assert.IsFalse(root.GetComponentInChildren<MeshRenderer>(true).gameObject.activeInHierarchy);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void MapIconRenderer_MaskModeHidesUnknownCellsAndHiddenSites()
    {
        WorldMap map = CreateSmallMap(7303);
        var progress = new WorldMapProgressState
        {
            revealedCellIndices = new List<int> { 10 },
            mapSites = new List<MapSiteData>
            {
                new MapSiteData
                {
                    siteId = "sect", siteName = "宗门", cellIndex = 5,
                    siteType = MapSiteType.SectBase, isRevealed = true, canInteract = true,
                    revealState = MapContentRevealState.Discovered, siteState = MapSiteState.Developed
                },
                new MapSiteData
                {
                    siteId = "hidden_village", siteName = "村落", cellIndex = 6,
                    siteType = MapSiteType.Village, revealState = MapContentRevealState.Hidden,
                    siteState = MapSiteState.None
                },
                new MapSiteData
                {
                    siteId = "hinted_ruin", siteName = "真实遗迹名", cellIndex = 7,
                    siteType = MapSiteType.Ruin, revealState = MapContentRevealState.Hinted,
                    siteState = MapSiteState.None
                },
                new MapSiteData
                {
                    siteId = "known_village", siteName = "村落", cellIndex = 10,
                    siteType = MapSiteType.Village, revealState = MapContentRevealState.Discovered,
                    siteState = MapSiteState.None
                }
            }
        };
        GameObject root = new GameObject("MapIconMaskTest");
        try
        {
            MapIconRenderer renderer = root.AddComponent<MapIconRenderer>();
            SetPrivateField(renderer, "respectKnowledgeMask", true);
            renderer.Render(map, progress);

            Assert.AreEqual(3, renderer.IconCount,
                "宗门驻地与已知地点显示，Hidden 隐藏，未知格上的 Hinted 仍显示匿名线索");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void SetPrivateField(object target, string name, object value)
    {
        System.Reflection.FieldInfo field = target.GetType().GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }
}
