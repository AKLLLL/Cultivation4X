using System;
using Cultivation4X.WorldMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PaintedMountainArtBuilder
{
    private const string TextureRoot = "Assets/MapSystem/Art/PaintedMountains/Textures";
    private const string TerrainTestScenePath = "Assets/Scenes/TerrainTest.unity";

    [MenuItem("Tools/Map System/Configure Painted Mountains")]
    public static void ConfigureAndMigrate()
    {
        var textures = new Texture2D[6];
        for (int i = 0; i < textures.Length; i++)
        {
            string path = $"{TextureRoot}/decorMountain{i:00}.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing mountain texture " + path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (textures[i] == null) throw new InvalidOperationException("Failed to import " + path);
        }

        Scene scene = EditorSceneManager.OpenScene(TerrainTestScenePath, OpenSceneMode.Single);
        WorldMapDecorationRenderer renderer = UnityEngine.Object.FindObjectOfType<WorldMapDecorationRenderer>();
        if (renderer == null) throw new InvalidOperationException("TerrainTest has no decoration renderer.");
        ConfigureRenderer(renderer);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, TerrainTestScenePath))
            throw new InvalidOperationException("Failed to save " + TerrainTestScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("PAINTED_MOUNTAIN_TERRAIN_TEST_SUCCESS " + TerrainTestScenePath);
    }

    internal static void ConfigureRenderer(WorldMapDecorationRenderer renderer)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        var serialized = new SerializedObject(renderer);
        SerializedProperty array = serialized.FindProperty("paintedMountainTextures");
        array.arraySize = 6;
        for (int i = 0; i < 6; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureRoot}/decorMountain{i:00}.png");

        // Disconnect obsolete 3D key modules for mountain and valley presentation.
        foreach (string propertyName in new[]
                 {
                     "mountainKeyPrefabA", "mountainKeyPrefabB", "mountainKeyPrefabC",
                     "valleyWallPrefabA", "valleyWallPrefabB"
                 })
            serialized.FindProperty(propertyName).objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
