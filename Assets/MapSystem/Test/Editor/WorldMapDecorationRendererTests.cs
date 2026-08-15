using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cultivation4X.WorldMap;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class WorldMapDecorationRendererTests
{
    private const string AdapterRoot = "Assets/MapSystem/Art/Dreamscape/Prefabs";

    [Test]
    public void Render_BuildsEveryRegionRegardlessOfFocusCell()
    {
        GameObject root = new GameObject("DecorationRendererTest");
        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            WorldMapDecorationRenderer renderer = root.AddComponent<WorldMapDecorationRenderer>();
            Configure(renderer, template);
            WorldMap map = BuildMap(30, 6, LandformType.Mountain);
            AddRegion(map, "west", MapRegionType.MountainRange,
                map.cells.Where(cell => cell.coord.col < 5).Select(cell => cell.index));
            AddRegion(map, "east", MapRegionType.MountainRange,
                map.cells.Where(cell => cell.coord.col >= 25).Select(cell => cell.index));

            renderer.Render(map, 0);

            Transform structural = root.transform.Find("Structural Region Decorations");
            Assert.That(structural, Is.Not.Null);
            Assert.That(renderer.StructuralCount, Is.EqualTo(2));
            Assert.That(structural.Cast<Transform>().Any(item => item.name.Contains(" east ")), Is.True,
                "焦点在最西侧时，最东侧 Region 仍必须生成模型");
            Assert.That(structural.Cast<Transform>().All(item => item.childCount == 0), Is.True,
                "每个 Region 应合并成单一表现对象，而不是保留逐格子对象");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(template);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void Render_IsDeterministicAtRegionLevel()
    {
        GameObject root = new GameObject("DecorationRendererTest");
        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            WorldMapDecorationRenderer renderer = root.AddComponent<WorldMapDecorationRenderer>();
            Configure(renderer, template);
            WorldMap map = BuildMap(17, 9, LandformType.Hill);
            AddRegion(map, "hills", MapRegionType.Hills,
                map.cells.Select(cell => cell.index));

            renderer.Render(map, 0);
            string[] first = Snapshot(root);
            renderer.Render(map, map.cells.Length - 1);

            Assert.That(Snapshot(root), Is.EqualTo(first),
                "同一地图的 Region 模型不应随焦点格变化");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(template);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ApplyTier_ChangesRegionLayerVisibility()
    {
        GameObject root = new GameObject("DecorationRendererTest");
        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            WorldMapDecorationRenderer renderer = root.AddComponent<WorldMapDecorationRenderer>();
            Configure(renderer, template);
            WorldMap map = BuildMap(11, 11, LandformType.Hill);
            AddRegion(map, "hills", MapRegionType.Hills,
                map.cells.Select(cell => cell.index));
            renderer.Render(map, 60);
            Transform structural = root.transform.Find("Structural Region Decorations");
            Transform detail = root.transform.Find("Near Region Details");
            Assert.That(structural.childCount, Is.Zero,
                "丘陵只使用基础地表轻微起伏，不得生成逐格圆片结构");
            Assert.That(detail.childCount, Is.EqualTo(1));

            renderer.ApplyTier(WorldMap3DZoomTier.Near);
            Assert.That(structural.gameObject.activeSelf, Is.True);
            Assert.That(detail.gameObject.activeSelf, Is.True);
            renderer.ApplyTier(WorldMap3DZoomTier.Mid);
            Assert.That(structural.gameObject.activeSelf, Is.True);
            Assert.That(detail.gameObject.activeSelf, Is.False);
            renderer.ApplyTier(WorldMap3DZoomTier.Far);
            Assert.That(structural.gameObject.activeSelf, Is.False,
                "远景仍应保留 Region 结构模型，避免进入中景时整片模型突然出现");
            Assert.That(detail.gameObject.activeSelf, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(template);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void FarView_KeepsStructuralModelsAndAddsTerrainMarkers()
    {
        GameObject root = new GameObject("FarTerrainMarkerTest");
        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            WorldMapDecorationRenderer renderer = root.AddComponent<WorldMapDecorationRenderer>();
            Configure(renderer, template);
            WorldMap map = BuildMap(13, 7, LandformType.Mountain);
            AddRegion(map, "range", MapRegionType.MountainRange,
                map.cells.Select(cell => cell.index));
            renderer.Render(map, 0);
            renderer.ApplyTier(WorldMap3DZoomTier.Far);

            Assert.That(renderer.StructuralCount, Is.EqualTo(1));
            Assert.That(renderer.TerrainMarkerCount, Is.EqualTo(1));
            Transform markers = root.transform.Find("Far Terrain Markers");
            Assert.That(markers, Is.Not.Null);
            Assert.That(markers.gameObject.activeSelf, Is.True);
            Assert.That(markers.GetChild(0).GetComponent<MeshFilter>()?.sharedMesh.vertexCount,
                Is.GreaterThanOrEqualTo(6));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(template);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void Forest_CombinesWholeRegionIntoOneRenderable()
    {
        GameObject root = new GameObject("ForestDecorationRendererTest");
        GameObject largeTree = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject birch = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        try
        {
            WorldMapDecorationRenderer renderer = root.AddComponent<WorldMapDecorationRenderer>();
            Configure(renderer, largeTree);
            SetField(renderer, "birchPrefab", birch);
            WorldMap map = BuildMap(9, 9, LandformType.Hill);
            foreach (WorldCell cell in map.cells) cell.biome = BiomeType.TemperateForest;
            AddRegion(map, "forest", MapRegionType.Forest,
                map.cells.Select(cell => cell.index));

            renderer.Render(map, 0);

            Transform structural = root.transform.Find("Structural Region Decorations");
            Assert.That(structural.childCount, Is.Zero,
                "不得再生成覆盖地表的 Region Forest Canopy 圆盘结构");
            Transform detail = root.transform.Find("Near Region Details");
            Assert.That(detail.childCount, Is.EqualTo(1),
                "整片森林 Region 应保留一个合并树簇模型");
            Transform forest = detail.GetChild(0);
            MeshRenderer visual = forest.GetComponent<MeshRenderer>();
            Assert.That(forest.name, Does.Contain("Forest forest Cells 81 Items 81"));
            Assert.That(forest.childCount, Is.Zero);
            Assert.That(forest.GetComponent<MeshFilter>()?.sharedMesh, Is.Not.Null);
            Assert.That(forest.GetComponent<MeshFilter>().sharedMesh.bounds.size.x, Is.GreaterThan(8f));
            Assert.That(forest.GetComponent<MeshFilter>().sharedMesh.bounds.size.z, Is.GreaterThan(8f));
            Assert.That(forest.localScale.x, Is.LessThanOrEqualTo(1f));
            var block = new MaterialPropertyBlock();
            visual.GetPropertyBlock(block);
            Assert.That(block.isEmpty, Is.False);
            Assert.That(block.GetFloat("_WindIntensity"), Is.EqualTo(0f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(largeTree);
            UnityEngine.Object.DestroyImmediate(birch);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void MountainRange_CombinesConnectedCliffModulesAndDoesNotChangeCells()
    {
        GameObject root = new GameObject("MountainDecorationRendererTest");
        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            WorldMapDecorationRenderer renderer = root.AddComponent<WorldMapDecorationRenderer>();
            Configure(renderer, template);
            WorldMap map = BuildMap(9, 9, LandformType.Mountain);
            AddRegion(map, "range", MapRegionType.MountainRange,
                map.cells.Select(cell => cell.index));

            renderer.Render(map, 40);

            Transform structural = root.transform.Find("Structural Region Decorations");
            Assert.That(structural.childCount, Is.EqualTo(1));
            Assert.That(structural.GetChild(0).name, Does.Contain("MountainRange range"));
            Mesh mesh = structural.GetChild(0).GetComponent<MeshFilter>()?.sharedMesh;
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.bounds.size.x, Is.GreaterThan(8f), "山脉组合应沿区域主轴连续覆盖多格");
            Assert.That(mesh.vertexCount, Is.GreaterThan(250),
                "山脉必须由多个悬崖模块合并，而不是回退为单格 RockFormation");
            Assert.That(mesh.bounds.size.y, Is.GreaterThan(2.50f),
                "山脉高度必须明显超过树木和普通岩石装饰");
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Material[] materials = structural.GetChild(0).GetComponent<MeshRenderer>().sharedMaterials;
            Assert.That(materials.Length, Is.EqualTo(1));
            Assert.That(materials[0], Is.Not.Null);
            Assert.That(materials[0].shader.name,
                Is.EqualTo("Cultivation4X/Map/Static Cliff Module"));
            Transform detail = root.transform.Find("Near Region Details");
            Assert.That(detail, Is.Not.Null);
            Assert.That(detail.childCount, Is.Zero,
                "本轮山体模块不得混入旧高度场上的随机树石采样");
            Assert.That(structural.GetChild(0).name, Does.Not.Contain("RockFormation"));
            Assert.That(map.cells.All(cell => cell.landform == LandformType.Mountain), Is.True,
                "山脉连续形状属于表现层，不得写回格子数据");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(template);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void Valley_BuildsTwoLowWallsAndKeepsCenterReadable()
    {
        GameObject root = new GameObject("ValleyDecorationRendererTest");
        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            WorldMapDecorationRenderer renderer = root.AddComponent<WorldMapDecorationRenderer>();
            Configure(renderer, template);
            WorldMap map = BuildMap(11, 3, LandformType.Hill);
            foreach (WorldCell cell in map.cells) cell.internalPositionTag = MapInternalPositionTag.ValleyFloor;
            AddRegion(map, "valley", MapRegionType.Valley, map.cells.Select(cell => cell.index));

            renderer.Render(map, 0);

            Transform structural = root.transform.Find("Structural Region Decorations");
            Transform valley = structural.GetChild(0);
            Mesh mesh = valley.GetComponent<MeshFilter>()?.sharedMesh;
            Assert.That(valley.name, Does.Contain("Modular Valley Corridor"));
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.bounds.size.x, Is.GreaterThan(10f));
            Assert.That(mesh.bounds.size.z, Is.GreaterThan(1.4f), "双侧谷壁之间必须留下可读谷底宽度");
            Assert.That(mesh.bounds.size.y, Is.GreaterThan(2.0f), "谷壁必须形成清楚的双侧高差");
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(valley.GetComponent<MeshRenderer>().sharedMaterial.shader.name,
                Is.EqualTo("Cultivation4X/Map/Static Cliff Module"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(template);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void AdapterPrefabs_AreProjectOwnedRenderableAndColliderFree()
    {
        string[] names =
        {
            "Dreamscape_LargeTree_Map", "Dreamscape_Birch_Map", "Dreamscape_RockFormation_Map",
            "Dreamscape_RockFormation_02_Map", "Dreamscape_RockFormation_03_Map",
            "Dreamscape_RockFormation_04_Map",
            "Dreamscape_FlowerBush_Map", "Dreamscape_GrassCluster_Map"
        };
        foreach (string name in names)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AdapterRoot + "/" + name + ".prefab");
            Assert.That(prefab, Is.Not.Null, name);
            Assert.That(prefab.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(0), name);
            if (name != "Dreamscape_GrassCluster_Map")
                Assert.That(prefab.GetComponentsInChildren<LODGroup>(true).Length, Is.GreaterThan(0), name);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true).Length, Is.Zero, name);
        }
    }

    [Test]
    public void TerrainTestScene_DependsOnAllDreamscapeAdapters()
    {
        HashSet<string> dependencies = new HashSet<string>(
            AssetDatabase.GetDependencies("Assets/Scenes/TerrainTest.unity", true),
            StringComparer.Ordinal);
        Assert.That(dependencies.Count(path => path.StartsWith(AdapterRoot, StringComparison.Ordinal)),
            Is.EqualTo(8));
    }

    [Test]
    public void FoliageAdapters_UseOnlyProjectStaticShaders()
    {
        string[] names =
        {
            "Dreamscape_LargeTree_Map", "Dreamscape_Birch_Map",
            "Dreamscape_FlowerBush_Map", "Dreamscape_GrassCluster_Map"
        };
        foreach (string name in names)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AdapterRoot + "/" + name + ".prefab");
            Assert.That(prefab, Is.Not.Null, name);
            Material[] materials = prefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null).ToArray();
            Assert.That(materials.Any(material => material.shader.name.StartsWith("Cultivation4X/Map/Static",
                StringComparison.Ordinal)), Is.True, name + " 缺少项目自有静态材质");
            Assert.That(materials.Any(material => material.shader.name.IndexOf("Wind",
                StringComparison.OrdinalIgnoreCase) >= 0), Is.False, name + " 仍引用风动画 Shader");
        }
    }

    private static WorldMap BuildMap(int width, int height, LandformType landform)
    {
        var map = new WorldMap
        {
            width = width,
            height = height,
            effectiveSeed = 24680,
            cells = new WorldCell[width * height],
            regions = new List<MapRegionData>()
        };
        for (int row = 0; row < height; row++)
        for (int col = 0; col < width; col++)
        {
            int index = row * width + col;
            map.cells[index] = new WorldCell
            {
                index = index,
                coord = new HexCoord(col, row),
                landform = landform,
                biome = BiomeType.Grassland,
                height = 0.6f
            };
        }
        return map;
    }

    private static void AddRegion(WorldMap map, string id, MapRegionType type,
        IEnumerable<int> indices)
    {
        List<int> cells = indices.OrderBy(index => index).ToList();
        map.regions.Add(new MapRegionData
        {
            regionId = id,
            regionType = type,
            centerCellIndex = cells[cells.Count / 2],
            cellIndices = cells
        });
        foreach (int index in cells) map.cells[index].regionId = id;
    }

    private static void Configure(WorldMapDecorationRenderer renderer, GameObject template)
    {
        foreach (string fieldName in new[]
                 {
                     "largeTreePrefab", "birchPrefab", "rockPrefab", "flowerBushPrefab", "grassPrefab"
                 })
            SetField(renderer, fieldName, template);
        foreach (string fieldName in new[] { "rockPrefab02", "rockPrefab03", "rockPrefab04" })
            SetField(renderer, fieldName, template);
        Texture2D[] mountains = Enumerable.Range(0, 6)
            .Select(index => AssetDatabase.LoadAssetAtPath<Texture2D>(
                $"Assets/MapSystem/Art/PaintedMountains/Textures/decorMountain{index:00}.png"))
            .Where(texture => texture != null).ToArray();
        SetField(renderer, "paintedMountainTextures", mountains);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(target, value);
    }

    private static string[] Snapshot(GameObject root)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .Where(item => item.parent != null && item.parent.parent == root.transform)
            .Select(item => item.name + "|" + item.localPosition.ToString("F4") + "|" +
                            item.GetComponent<MeshFilter>()?.sharedMesh?.vertexCount)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }
}
