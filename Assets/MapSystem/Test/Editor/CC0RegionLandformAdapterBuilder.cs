using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates project-owned near-view adapters from the selectively imported CC0 models.</summary>
public static class CC0RegionLandformAdapterBuilder
{
    internal const string AdapterRoot = "Assets/MapSystem/Art/CC0RegionLandforms/Prefabs";
    private const string SourceRoot = "Assets/MapSystem/Art/CC0RegionLandforms/Source";
    private const string MaterialRoot = "Assets/MapSystem/Art/CC0RegionLandforms/Materials";
    private const string TerrainTestScenePath = "Assets/Scenes/TerrainTest.unity";

    [MenuItem("Tools/Map System/Build CC0 Region Landform Adapters")]
    public static void BuildAdapters()
    {
        EnsureFolder(AdapterRoot);
        EnsureFolder(MaterialRoot);
        Material kayKit = Material("KayKit_Landform", new Color(0.74f, 0.76f, 0.68f, 1f),
            AssetDatabase.LoadAssetAtPath<Texture2D>(SourceRoot + "/hexagons_medieval.png"));
        Material kenney = Material("Kenney_Cliff", new Color(0.39f, 0.37f, 0.32f, 1f), null);

        Build("Mountain_A_Map", "mountain_A.fbx", kayKit, 1.45f);
        Build("Mountain_B_Map", "mountain_B.fbx", kayKit, 1.45f);
        Build("Mountain_C_Map", "mountain_C.fbx", kayKit, 1.45f);
        Build("Hills_A_Map", "hills_A.fbx", kayKit, 1.55f);
        Build("Hills_B_Map", "hills_B.fbx", kayKit, 1.55f);
        Build("Hills_C_Map", "hills_C.fbx", kayKit, 1.55f);
        Build("ValleyWall_Straight_Map", "cliff_large_rock.fbx", kenney, 1.20f);
        Build("ValleyWall_Diagonal_Map", "cliff_diagonal_rock.fbx", kenney, 1.20f);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("CC0_REGION_LANDFORM_ADAPTERS_SUCCESS " + AdapterRoot);
    }

    [MenuItem("Tools/Map System/Migrate TerrainTest CC0 Region Landforms")]
    public static void MigrateTerrainTest()
    {
        Scene scene = EditorSceneManager.OpenScene(TerrainTestScenePath, OpenSceneMode.Single);
        var renderer = UnityEngine.Object.FindObjectOfType<Cultivation4X.WorldMap.WorldMapDecorationRenderer>();
        var terrain = UnityEngine.Object.FindObjectOfType<Cultivation4X.WorldMap.TerrainRenderer>();
        if (renderer == null) throw new InvalidOperationException("TerrainTest has no decoration renderer.");
        DreamscapeMapArtAdapterBuilder.ConfigureRenderer(renderer, terrain);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, TerrainTestScenePath))
            throw new InvalidOperationException("Failed to save " + TerrainTestScenePath);
        Debug.Log("CC0_REGION_LANDFORM_MIGRATION_SUCCESS " + TerrainTestScenePath);
    }

    public static void BuildAdaptersAndMigrateTerrainTest()
    {
        BuildAdapters();
        MigrateTerrainTest();
    }

    private static void Build(string adapterName, string sourceName, Material material, float footprint)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceRoot + "/" + sourceName);
        if (source == null) throw new InvalidOperationException("Missing CC0 source model " + sourceName);
        var root = new GameObject(adapterName);
        GameObject visual = UnityEngine.Object.Instantiate(source, root.transform);
        visual.name = "Visual";
        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
        }
        Bounds bounds = CombinedBounds(visual);
        float scale = footprint / Mathf.Max(0.001f, Mathf.Max(bounds.size.x, bounds.size.z));
        visual.transform.localScale = Vector3.one * scale;
        visual.transform.localPosition = new Vector3(-bounds.center.x * scale,
            -bounds.min.y * scale, -bounds.center.z * scale);
        GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.BatchingStatic);
        PrefabUtility.SaveAsPrefabAsset(root, AdapterRoot + "/" + adapterName + ".prefab");
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static Bounds CombinedBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static Material Material(string name, Color color, Texture texture)
    {
        string path = MaterialRoot + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Standard shader unavailable.");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        material.mainTexture = texture;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
