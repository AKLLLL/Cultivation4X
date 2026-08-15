using UnityEditor;
using UnityEditor.SceneManagement;
using Cultivation4X.WorldMap;
using UnityEngine;

/// <summary>
/// Regenerates Assets/Scenes/TerrainTest.unity with only Camera, Light,
/// MapTestManager and TerrainRenderer. Run via Tools/Map System/Rebuild TerrainTest Scene
/// or batchmode: -executeMethod TerrainTestSceneBuilder.Build -quit
/// </summary>
public static class TerrainTestSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/TerrainTest.unity";

    [MenuItem("Tools/Map System/Rebuild TerrainTest Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = false;
        camera.fieldOfView = 30f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.AddComponent<AudioListener>();

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject mapObject = new GameObject("MapTestManager");
        MapTestManager mapManager = mapObject.AddComponent<MapTestManager>();

        GameObject rendererObject = new GameObject("TerrainRenderer");
        TerrainRenderer terrainRenderer = rendererObject.AddComponent<TerrainRenderer>();
        DreamscapeMapArtAdapterBuilder.ConfigureTerrainTextures(terrainRenderer);

        GameObject gridObject = new GameObject("HexGridOverlayRenderer");
        HexGridOverlayRenderer hexGridOverlayRenderer = gridObject.AddComponent<HexGridOverlayRenderer>();
        DreamscapeMapArtAdapterBuilder.ConfigureGridRenderer(hexGridOverlayRenderer, terrainRenderer);

        GameObject decorationObject = new GameObject("WorldMapDecorationRenderer");
        WorldMapDecorationRenderer decorationRenderer =
            decorationObject.AddComponent<WorldMapDecorationRenderer>();
        DreamscapeMapArtAdapterBuilder.ConfigureRenderer(decorationRenderer, terrainRenderer);

        GameObject iconObject = new GameObject("MapIconRenderer");
        MapIconRenderer mapIconRenderer = iconObject.AddComponent<MapIconRenderer>();
        SerializedObject iconSerialized = new SerializedObject(mapIconRenderer);
        iconSerialized.FindProperty("terrainRenderer").objectReferenceValue = terrainRenderer;
        iconSerialized.ApplyModifiedProperties();

        GameObject regionNameObject = new GameObject("RegionNameRenderer");
        RegionNameRenderer regionNameRenderer = regionNameObject.AddComponent<RegionNameRenderer>();

        GameObject regionOverlayObject = new GameObject("RegionOverlayRenderer");
        RegionOverlayRenderer regionOverlayRenderer = regionOverlayObject.AddComponent<RegionOverlayRenderer>();

        SerializedObject mapManagerSerialized = new SerializedObject(mapManager);
        mapManagerSerialized.FindProperty("terrainRenderer").objectReferenceValue = terrainRenderer;
        mapManagerSerialized.FindProperty("decorationRenderer").objectReferenceValue = decorationRenderer;
        mapManagerSerialized.FindProperty("hexGridOverlayRenderer").objectReferenceValue = hexGridOverlayRenderer;
        mapManagerSerialized.FindProperty("mapIconRenderer").objectReferenceValue = mapIconRenderer;
        mapManagerSerialized.FindProperty("regionNameRenderer").objectReferenceValue = regionNameRenderer;
        mapManagerSerialized.FindProperty("regionOverlayRenderer").objectReferenceValue = regionOverlayRenderer;
        mapManagerSerialized.ApplyModifiedProperties();

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"Saved {ScenePath}");
    }
}
