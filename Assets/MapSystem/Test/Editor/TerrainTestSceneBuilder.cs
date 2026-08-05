using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Regenerates Assets/Scenes/TerrainTest.unity with only Camera, Light,
/// and MapTestManager. Run via Tools/Map System/Rebuild TerrainTest Scene
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
        camera.orthographic = true;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.AddComponent<AudioListener>();

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject mapObject = new GameObject("MapTestManager");
        mapObject.AddComponent<MapTestManager>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"Saved {ScenePath}");
    }
}
