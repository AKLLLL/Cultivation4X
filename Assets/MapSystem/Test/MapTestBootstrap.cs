using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene gate used by runtime bootstraps so TerrainTest stays isolated
/// from the main game UI, managers, and save pipeline.
/// </summary>
public static class MapTestBootstrap
{
    public const string SceneName = "TerrainTest";
    public const string ArtAuditionSceneName = "DreamscapeArtAudition";

    public static bool IsTestScene
    {
        get
        {
            return IsIsolatedSceneName(SceneManager.GetActiveScene().name);
        }
    }

    private static bool IsIsolatedSceneName(string sceneName)
    {
        return sceneName == SceneName || sceneName == ArtAuditionSceneName;
    }
}
