using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds a self-contained visual audition scene for the curated Dreamscape Meadows assets.
/// It does not reference or modify world-map data, TerrainRenderer, or gameplay scenes.
/// </summary>
public static class DreamscapeArtAuditionSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/DreamscapeArtAudition.unity";
    private const string SupportRoot = "Assets/MapSystem/Test/ArtAudition";
    private const string HexMeshPath = SupportRoot + "/HexPreviewTile.asset";
    private const string HexMaterialPath = SupportRoot + "/HexPreviewTile.mat";

    private sealed class Sample
    {
        public readonly string label;
        public readonly string prefabPath;
        public readonly Vector3 position;
        public readonly float maxFootprint;
        public readonly float maxHeight;

        public Sample(string label, string prefabPath, Vector3 position, float maxFootprint, float maxHeight)
        {
            this.label = label;
            this.prefabPath = prefabPath;
            this.position = position;
            this.maxFootprint = maxFootprint;
            this.maxHeight = maxHeight;
        }
    }

    [MenuItem("Tools/Map System/Rebuild Dreamscape Art Audition Scene")]
    public static void Build()
    {
        EnsureSupportAssets(out Mesh hexMesh, out Material hexMaterial);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Camera camera = CreateCamera();
        CreateLighting();

        var root = new GameObject("Dreamscape Art Audition");
        CreateReferenceGuide(root.transform);

        foreach (Sample sample in Samples())
        {
            CreateSample(root.transform, sample, hexMesh, hexMaterial, camera.transform.rotation);
        }

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.52f, 0.56f, 0.61f);
        RenderSettings.fog = false;

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("DREAMSCAPE_AUDITION_SCENE_SUCCESS " + ScenePath);
    }

    public static void RenderPreview()
    {
        RenderPreviewAt("Logs/DreamscapeArtAudition-Mid.png", ViewDistance.Mid);
    }

    public static void RenderAllZoomPreviews()
    {
        RenderPreviewAt("Logs/DreamscapeArtAudition-Near.png", ViewDistance.Near);
        RenderPreviewAt("Logs/DreamscapeArtAudition-Mid.png", ViewDistance.Mid);
        RenderPreviewAt("Logs/DreamscapeArtAudition-Far.png", ViewDistance.Far);
    }

    private static void RenderPreviewAt(string outputPath, ViewDistance viewDistance)
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Camera camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("Audition scene has no Main Camera.");

        var controller = camera.GetComponent<DreamscapeArtAuditionCameraController>();
        if (controller == null) throw new InvalidOperationException("Audition camera has no zoom controller.");
        if (viewDistance == ViewDistance.Near) controller.ApplyNearView();
        else if (viewDistance == ViewDistance.Far) controller.ApplyFarView();
        else controller.ApplyMidView();

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
            Debug.Log("DREAMSCAPE_AUDITION_RENDER_SUCCESS " + absolutePath);
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(screenshot);
            UnityEngine.Object.DestroyImmediate(renderTexture);
        }
    }

    public static void RebuildAndRenderPreview()
    {
        Build();
        RenderAllZoomPreviews();
    }

    private enum ViewDistance
    {
        Near,
        Mid,
        Far
    }

    private static IEnumerable<Sample> Samples()
    {
        const string root = "Assets/Polyart/PolyartStudio/DreamscapeMeadows/Prefabs";
        yield return new Sample("Large Tree", root + "/Trees/Prefab_TreeLarge_01.prefab",
            new Vector3(-4.2f, 0f, 1.4f), 1.55f, 2.25f);
        yield return new Sample("Birch", root + "/Trees/Prefab_Birch_03.prefab",
            new Vector3(-1.4f, 0f, 1.4f), 1.45f, 2.10f);
        yield return new Sample("Rock Formation", root + "/Rocks/Prefab_RockFormation_01.prefab",
            new Vector3(1.4f, 0f, 1.4f), 1.55f, 1.35f);
        yield return new Sample("Flower Bush", root + "/Foliage/Prefab_Bush_04_Flowers.prefab",
            new Vector3(4.2f, 0f, 1.4f), 1.50f, 1.20f);
        yield return new Sample("Grass Cluster", root + "/Grass/Prefab_Grass_Group_01_Detail.prefab",
            new Vector3(-2.8f, 0f, -1.4f), 1.45f, 0.65f);
        yield return new Sample("Crate", root + "/Props/Prefab_Crate_01.prefab",
            new Vector3(0f, 0f, -1.4f), 1.35f, 1.10f);
        yield return new Sample("Lake Surface", root + "/Water/Prefab_WaterLake.prefab",
            new Vector3(2.8f, 0f, -1.4f), 1.85f, 0.25f);
    }

    private static Camera CreateCamera()
    {
        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = false;
        camera.fieldOfView = 50f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
        camera.backgroundColor = new Color(0.16f, 0.20f, 0.25f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        cameraObject.AddComponent<AudioListener>();

        var controller = cameraObject.AddComponent<DreamscapeArtAuditionCameraController>();
        controller.Configure(new Vector3(0f, 0.65f, 0f), 40f, 12f, 10.5f);
        return camera;
    }

    private static void CreateLighting()
    {
        var lightObject = new GameObject("Directional Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.94f, 0.84f);
        light.intensity = 1.05f;
        light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private static void CreateReferenceGuide(Transform parent)
    {
        var guide = new GameObject("Reference: Hex radius 1, diameter 2");
        guide.transform.SetParent(parent, false);
    }

    private static void CreateSample(Transform parent, Sample sample, Mesh hexMesh, Material hexMaterial,
        Quaternion labelRotation)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(sample.prefabPath);
        if (prefab == null) throw new InvalidOperationException("Missing audition prefab: " + sample.prefabPath);

        var sampleRoot = new GameObject(sample.label);
        sampleRoot.transform.SetParent(parent, false);
        sampleRoot.transform.position = sample.position;

        var tile = new GameObject("Hex Diameter 2", typeof(MeshFilter), typeof(MeshRenderer));
        tile.transform.SetParent(sampleRoot.transform, false);
        tile.GetComponent<MeshFilter>().sharedMesh = hexMesh;
        tile.GetComponent<MeshRenderer>().sharedMaterial = hexMaterial;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, sampleRoot.transform);
        instance.name = Path.GetFileNameWithoutExtension(sample.prefabPath) + " (Auto-scaled)";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.Euler(0f, -12f, 0f);
        instance.transform.localScale = Vector3.one;

        Bounds bounds = CalculateBounds(instance);
        float footprint = Mathf.Max(bounds.size.x, bounds.size.z);
        float scale = Mathf.Min(
            footprint > 0.0001f ? sample.maxFootprint / footprint : 1f,
            bounds.size.y > 0.0001f ? sample.maxHeight / bounds.size.y : 1f);
        scale = Mathf.Clamp(scale, 0.01f, 10f);
        instance.transform.localScale = Vector3.one * scale;

        bounds = CalculateBounds(instance);
        instance.transform.position += Vector3.up * (0.055f - bounds.min.y);
        instance.name += " scale=" + scale.ToString("0.###");

        var labelObject = new GameObject(sample.label + " Label");
        labelObject.transform.SetParent(sampleRoot.transform, false);
        labelObject.transform.position = new Vector3(sample.position.x, Mathf.Max(0.8f, bounds.max.y + 0.25f), sample.position.z);
        labelObject.transform.rotation = labelRotation;
        var text = labelObject.AddComponent<TextMesh>();
        text.text = sample.label;
        text.anchor = TextAnchor.LowerCenter;
        text.alignment = TextAlignment.Center;
        text.characterSize = 0.035f;
        text.fontSize = 48;
        text.color = Color.white;
    }

    private static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
        return bounds;
    }

    private static void EnsureSupportAssets(out Mesh mesh, out Material material)
    {
        if (!AssetDatabase.IsValidFolder(SupportRoot))
        {
            Directory.CreateDirectory(SupportRoot);
            AssetDatabase.Refresh();
        }

        mesh = AssetDatabase.LoadAssetAtPath<Mesh>(HexMeshPath);
        if (mesh == null)
        {
            mesh = BuildHexPrism();
            AssetDatabase.CreateAsset(mesh, HexMeshPath);
        }

        material = AssetDatabase.LoadAssetAtPath<Material>(HexMaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Standard");
            material = new Material(shader) { name = "HexPreviewTile" };
            material.color = new Color(0.24f, 0.30f, 0.24f);
            material.SetFloat("_Glossiness", 0.08f);
            AssetDatabase.CreateAsset(material, HexMaterialPath);
        }
    }

    private static Mesh BuildHexPrism()
    {
        const float radius = 1f;
        const float bottomY = 0f;
        const float topY = 0.05f;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        vertices.Add(new Vector3(0f, topY, 0f));
        vertices.Add(new Vector3(0f, bottomY, 0f));
        for (int index = 0; index < 6; index++)
        {
            float angle = (30f + index * 60f) * Mathf.Deg2Rad;
            vertices.Add(new Vector3(Mathf.Cos(angle) * radius, topY, Mathf.Sin(angle) * radius));
            vertices.Add(new Vector3(Mathf.Cos(angle) * radius, bottomY, Mathf.Sin(angle) * radius));
        }

        for (int index = 0; index < 6; index++)
        {
            int next = (index + 1) % 6;
            int top = 2 + index * 2;
            int bottom = top + 1;
            int nextTop = 2 + next * 2;
            int nextBottom = nextTop + 1;
            triangles.AddRange(new[] { 0, nextTop, top, 1, bottom, nextBottom });
            triangles.AddRange(new[] { top, nextTop, nextBottom, top, nextBottom, bottom });
        }

        var result = new Mesh { name = "HexPreviewTile" };
        result.SetVertices(vertices);
        result.SetTriangles(triangles, 0);
        result.RecalculateNormals();
        result.RecalculateBounds();
        return result;
    }
}
