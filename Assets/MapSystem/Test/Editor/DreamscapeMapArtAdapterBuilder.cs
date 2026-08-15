using System;
using System.Collections.Generic;
using System.IO;
using Cultivation4X.WorldMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds project-owned map adapter prefabs without modifying publisher assets, then performs
/// a surgical TerrainTest scene migration that preserves all existing serialized settings.
/// </summary>
public static class DreamscapeMapArtAdapterBuilder
{
    private const string AdapterRoot = "Assets/MapSystem/Art/Dreamscape/Prefabs";
    private const string MaterialRoot = "Assets/MapSystem/Art/Dreamscape/Materials/Static";
    private const string StaticGrassShader = "Cultivation4X/Map/Static Grass";
    private const string StaticTreeShader = "Cultivation4X/Map/Static Tree Foliage";
    private const string VendorRoot = "Assets/Polyart/PolyartStudio/DreamscapeMeadows/Prefabs";
    private const string TerrainTestScenePath = "Assets/Scenes/TerrainTest.unity";
    private const string TerrainTextureRoot =
        "Assets/Polyart/PolyartStudio/DreamscapeMeadows/Textures/Terrain";

    private sealed class AdapterDefinition
    {
        public readonly string name;
        public readonly string sourcePath;
        public readonly float scale;

        public AdapterDefinition(string name, string sourcePath, float scale)
        {
            this.name = name;
            this.sourcePath = sourcePath;
            this.scale = scale;
        }

        public string TargetPath => AdapterRoot + "/" + name + ".prefab";
    }

    [MenuItem("Tools/Map System/Build Dreamscape Map Adapters")]
    public static void BuildAdapters()
    {
        EnsureFolder(AdapterRoot);
        EnsureFolder(MaterialRoot);
        foreach (AdapterDefinition definition in Definitions()) BuildAdapter(definition);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("DREAMSCAPE_MAP_ADAPTERS_SUCCESS " + AdapterRoot);
    }

    [MenuItem("Tools/Map System/Migrate TerrainTest Dreamscape Decorations")]
    public static void MigrateTerrainTest()
    {
        Scene scene = EditorSceneManager.OpenScene(TerrainTestScenePath, OpenSceneMode.Single);
        MapTestManager mapManager = UnityEngine.Object.FindObjectOfType<MapTestManager>();
        TerrainRenderer terrainRenderer = UnityEngine.Object.FindObjectOfType<TerrainRenderer>();
        if (mapManager == null || terrainRenderer == null)
            throw new InvalidOperationException("TerrainTest is missing MapTestManager or TerrainRenderer.");

        ConfigureTerrainTextures(terrainRenderer);

        HexGridOverlayRenderer gridRenderer = UnityEngine.Object.FindObjectOfType<HexGridOverlayRenderer>();
        if (gridRenderer == null)
        {
            var gridObject = new GameObject("HexGridOverlayRenderer");
            gridRenderer = gridObject.AddComponent<HexGridOverlayRenderer>();
        }
        ConfigureGridRenderer(gridRenderer, terrainRenderer);

        WorldMapDecorationRenderer decorationRenderer =
            UnityEngine.Object.FindObjectOfType<WorldMapDecorationRenderer>();
        if (decorationRenderer == null)
        {
            var rendererObject = new GameObject("WorldMapDecorationRenderer");
            decorationRenderer = rendererObject.AddComponent<WorldMapDecorationRenderer>();
        }

        ConfigureRenderer(decorationRenderer, terrainRenderer);

        MapIconRenderer mapIconRenderer = UnityEngine.Object.FindObjectOfType<MapIconRenderer>();
        if (mapIconRenderer != null)
        {
            var iconSerialized = new SerializedObject(mapIconRenderer);
            iconSerialized.FindProperty("terrainRenderer").objectReferenceValue = terrainRenderer;
            iconSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        var managerSerialized = new SerializedObject(mapManager);
        managerSerialized.FindProperty("decorationRenderer").objectReferenceValue = decorationRenderer;
        managerSerialized.FindProperty("hexGridOverlayRenderer").objectReferenceValue = gridRenderer;
        managerSerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, TerrainTestScenePath))
            throw new InvalidOperationException("Failed to save " + TerrainTestScenePath);
        Debug.Log("DREAMSCAPE_TERRAIN_TEST_MIGRATION_SUCCESS " + TerrainTestScenePath);
    }

    internal static void ConfigureRenderer(WorldMapDecorationRenderer decorationRenderer,
        TerrainRenderer terrainRenderer)
    {
        if (decorationRenderer == null) throw new ArgumentNullException(nameof(decorationRenderer));
        var decorationSerialized = new SerializedObject(decorationRenderer);
        Assign(decorationSerialized, "largeTreePrefab", "Dreamscape_LargeTree_Map");
        Assign(decorationSerialized, "birchPrefab", "Dreamscape_Birch_Map");
        Assign(decorationSerialized, "rockPrefab", "Dreamscape_RockFormation_Map");
        Assign(decorationSerialized, "rockPrefab02", "Dreamscape_RockFormation_02_Map");
        Assign(decorationSerialized, "rockPrefab03", "Dreamscape_RockFormation_03_Map");
        Assign(decorationSerialized, "rockPrefab04", "Dreamscape_RockFormation_04_Map");
        Assign(decorationSerialized, "flowerBushPrefab", "Dreamscape_FlowerBush_Map");
        Assign(decorationSerialized, "grassPrefab", "Dreamscape_GrassCluster_Map");
        AssignExternal(decorationSerialized, "hillKeyPrefabA",
            CC0RegionLandformAdapterBuilder.AdapterRoot + "/Hills_A_Map.prefab");
        AssignExternal(decorationSerialized, "hillKeyPrefabB",
            CC0RegionLandformAdapterBuilder.AdapterRoot + "/Hills_B_Map.prefab");
        AssignExternal(decorationSerialized, "hillKeyPrefabC",
            CC0RegionLandformAdapterBuilder.AdapterRoot + "/Hills_C_Map.prefab");
        decorationSerialized.FindProperty("terrainRenderer").objectReferenceValue = terrainRenderer;
        decorationSerialized.ApplyModifiedPropertiesWithoutUndo();
        PaintedMountainArtBuilder.ConfigureRenderer(decorationRenderer);
    }

    private static void AssignExternal(SerializedObject serialized, string propertyName, string path)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null) throw new InvalidOperationException("Missing property " + propertyName);
        property.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    internal static void ConfigureTerrainTextures(TerrainRenderer terrainRenderer)
    {
        if (terrainRenderer == null) throw new ArgumentNullException(nameof(terrainRenderer));
        var serialized = new SerializedObject(terrainRenderer);
        serialized.FindProperty("useContinuousTerrainSurface").boolValue = true;
        serialized.FindProperty("continuousSurfaceSubdivisions").intValue = 2;
        serialized.FindProperty("terrainReliefScale").floatValue = 1f;
        serialized.FindProperty("blendContinuousTerrainMaterials").boolValue = true;
        serialized.FindProperty("nearHeightScale").floatValue = 1.15f;
        AssignTexture(serialized, "grassTexture", "T_Grass_Ground_C2.png");
        AssignTexture(serialized, "dirtTexture", "T_DirtGround_C.png");
        AssignTexture(serialized, "stoneTexture", "T_StoneGround_C.tga");
        AssignTexture(serialized, "sandTexture", "T_SandGround_C.tga");
        AssignTexture(serialized, "grassNormal", "T_Grass_Ground_N.png");
        AssignTexture(serialized, "dirtNormal", "T_DirtGround_N.png");
        AssignTexture(serialized, "stoneNormal", "T_StoneGround_N.tga");
        AssignTexture(serialized, "sandNormal", "T_SandGround_N.tga");
        serialized.FindProperty("groundTextureStrength").floatValue = 0.82f;
        serialized.FindProperty("groundTextureContrast").floatValue = 1.55f;
        serialized.FindProperty("groundTextureTiling").floatValue = 0.46f;
        serialized.FindProperty("groundMacroStrength").floatValue = 0.16f;
        serialized.FindProperty("groundMacroScale").floatValue = 0.065f;
        serialized.FindProperty("groundTextureColorBlend").floatValue = 0.10f;
        serialized.FindProperty("groundNormalStrength").floatValue = 0.55f;
        serialized.FindProperty("groundTextureOnly").boolValue = false;
        serialized.FindProperty("groundBrightness").floatValue = 1f;
        serialized.FindProperty("groundLinearColorLift").floatValue = 0.30f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    internal static void ConfigureGridRenderer(HexGridOverlayRenderer gridRenderer,
        TerrainRenderer terrainRenderer)
    {
        if (gridRenderer == null) throw new ArgumentNullException(nameof(gridRenderer));
        var serialized = new SerializedObject(gridRenderer);
        serialized.FindProperty("terrainRenderer").objectReferenceValue = terrainRenderer;
        serialized.FindProperty("nearColor").colorValue = new Color(0.07f, 0.09f, 0.07f, 0.12f);
        serialized.FindProperty("midColor").colorValue = new Color(0.07f, 0.09f, 0.07f, 0.23f);
        serialized.FindProperty("farColor").colorValue = new Color(0.07f, 0.09f, 0.07f, 0.54f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void BuildAdaptersAndMigrateTerrainTest()
    {
        BuildAdapters();
        MigrateTerrainTest();
    }

    public static void RenderStrategicTerrainPreview()
    {
        const string outputPath = "Logs/StrategicFlatTerrainPreview.png";
        EditorSceneManager.OpenScene(TerrainTestScenePath, OpenSceneMode.Single);
        MapTestManager mapManager = UnityEngine.Object.FindObjectOfType<MapTestManager>();
        Camera camera = Camera.main;
        if (mapManager == null || camera == null)
            throw new InvalidOperationException("TerrainTest is missing MapTestManager or Main Camera.");
        mapManager.Regenerate(20260806);

        var renderTexture = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        var screenshot = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        try
        {
            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;
            screenshot.ReadPixels(new Rect(0f, 0f, 1600f, 900f), 0, 0);
            screenshot.Apply();
            string absolutePath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllBytes(absolutePath, screenshot.EncodeToPNG());
            Debug.Log("STRATEGIC_FLAT_TERRAIN_RENDER_SUCCESS " + absolutePath);
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(screenshot);
            UnityEngine.Object.DestroyImmediate(renderTexture);
        }
    }

    private static AdapterDefinition[] Definitions()
    {
        return new[]
        {
            new AdapterDefinition("Dreamscape_LargeTree_Map",
                VendorRoot + "/Trees/Prefab_TreeLarge_01.prefab", 0.059f),
            new AdapterDefinition("Dreamscape_Birch_Map",
                VendorRoot + "/Trees/Prefab_Birch_03.prefab", 0.122f),
            new AdapterDefinition("Dreamscape_RockFormation_Map",
                VendorRoot + "/Rocks/Prefab_RockFormation_01.prefab", 0.260f),
            new AdapterDefinition("Dreamscape_RockFormation_02_Map",
                VendorRoot + "/Rocks/Prefab_RockFormation_02.prefab", 0.260f),
            new AdapterDefinition("Dreamscape_RockFormation_03_Map",
                VendorRoot + "/Rocks/Prefab_RockFormation_03.prefab", 0.260f),
            new AdapterDefinition("Dreamscape_RockFormation_04_Map",
                VendorRoot + "/Rocks/Prefab_RockFormation_04.prefab", 0.260f),
            new AdapterDefinition("Dreamscape_FlowerBush_Map",
                VendorRoot + "/Foliage/Prefab_Bush_04_Flowers.prefab", 0.261f),
            new AdapterDefinition("Dreamscape_GrassCluster_Map",
                VendorRoot + "/Grass/Prefab_Grass_Group_01_Detail.prefab", 0.601f)
        };
    }

    private static void BuildAdapter(AdapterDefinition definition)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(definition.sourcePath);
        if (source == null) throw new InvalidOperationException("Missing vendor prefab: " + definition.sourcePath);

        var wrapper = new GameObject(definition.name);
        try
        {
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
            visual.name = "Visual";
            visual.transform.SetParent(wrapper.transform, false);
            visual.transform.localScale = Vector3.one * definition.scale;

            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);

            ApplyStaticFoliageMaterials(visual);

            Bounds bounds = CalculateBounds(visual);
            visual.transform.localPosition += Vector3.up * -bounds.min.y;
            PrefabUtility.SaveAsPrefabAsset(wrapper, definition.TargetPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(wrapper);
        }
    }

    private static void ApplyStaticFoliageMaterials(GameObject visual)
    {
        var cache = new Dictionary<Material, Material>();
        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int index = 0; index < materials.Length; index++)
            {
                Material source = materials[index];
                if (source == null || source.shader == null) continue;
                string shaderName = source.shader.name;
                string staticShaderName = shaderName.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) >= 0
                    ? StaticGrassShader
                    : shaderName.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      shaderName.IndexOf("Foliage", StringComparison.OrdinalIgnoreCase) >= 0
                        ? StaticTreeShader : null;
                if (staticShaderName == null) continue;
                if (!cache.TryGetValue(source, out Material replacement))
                {
                    replacement = BuildStaticMaterial(source, staticShaderName);
                    cache.Add(source, replacement);
                }
                materials[index] = replacement;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
        }
    }

    private static Material BuildStaticMaterial(Material source, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null) throw new InvalidOperationException("Missing project shader: " + shaderName);
        string sourcePath = AssetDatabase.GetAssetPath(source);
        string guid = AssetDatabase.AssetPathToGUID(sourcePath);
        string fileName = Sanitize(source.name) + "_" +
                          (guid.Length >= 8 ? guid.Substring(0, 8) : "Local") + "_MapStatic.mat";
        string path = MaterialRoot + "/" + fileName;
        Material target = AssetDatabase.LoadAssetAtPath<Material>(path);
        var replacement = new Material(source) { shader = shader, name = Path.GetFileNameWithoutExtension(path) };
        if (target == null)
        {
            AssetDatabase.CreateAsset(replacement, path);
            return replacement;
        }
        EditorUtility.CopySerialized(replacement, target);
        target.shader = shader;
        UnityEngine.Object.DestroyImmediate(replacement);
        EditorUtility.SetDirty(target);
        return target;
    }

    private static string Sanitize(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value.Replace(' ', '_');
    }


    private static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
        return bounds;
    }

    private static void Assign(SerializedObject target, string propertyName, string adapterName)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AdapterRoot + "/" + adapterName + ".prefab");
        if (prefab == null) throw new InvalidOperationException("Missing adapter prefab: " + adapterName);
        target.FindProperty(propertyName).objectReferenceValue = prefab;
    }

    private static void AssignTexture(SerializedObject target, string propertyName, string fileName)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TerrainTextureRoot + "/" + fileName);
        if (texture == null) throw new InvalidOperationException("Missing terrain texture: " + fileName);
        target.FindProperty(propertyName).objectReferenceValue = texture;
    }


    private static void EnsureFolder(string assetPath)
    {
        string[] parts = assetPath.Split('/');
        string current = parts[0];
        for (int index = 1; index < parts.Length; index++)
        {
            string next = current + "/" + parts[index];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[index]);
            current = next;
        }
    }
}
