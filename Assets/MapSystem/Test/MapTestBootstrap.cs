using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene gate used by runtime bootstraps so TerrainTest stays isolated
/// from the main game UI, managers, and save pipeline.
/// </summary>
public static class MapTestBootstrap
{
    public const string SceneName = "TerrainTest";

    public static bool IsTestScene => SceneManager.GetActiveScene().name == SceneName;
}
