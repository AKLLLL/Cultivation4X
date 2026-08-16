using Cultivation4X.WorldMap;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 生成正式游戏使用的 3D 世界地图预制体：Assets/Resources/Prefab/WorldMap3D.prefab。
/// 运行方式：
/// - 菜单 Tools/Map System/Build WorldMap3D Prefab
/// - batchmode: -executeMethod WorldMap3DPrefabBuilder.Build -quit
/// 预制体只包含表现层组件；WorldMap3DController 在 SampleScene 运行时实例化它。
/// </summary>
public static class WorldMap3DPrefabBuilder
{
    public const string PrefabPath = "Assets/Resources/Prefab/WorldMap3D.prefab";

    [MenuItem("Tools/Map System/Build WorldMap3D Prefab")]
    public static void Build()
    {
        GameObject root = new GameObject("WorldMap3D");
        WorldMap3DController controller = root.AddComponent<WorldMap3DController>();

        GameObject pipelineObject = new GameObject("RenderPipeline");
        pipelineObject.transform.SetParent(root.transform, false);
        WorldMapRenderPipeline pipeline = pipelineObject.AddComponent<WorldMapRenderPipeline>();

        GameObject interactionObject = new GameObject("Interaction");
        interactionObject.transform.SetParent(root.transform, false);
        WorldMapInteractionController interaction = interactionObject.AddComponent<WorldMapInteractionController>();

        GameObject hudObject = new GameObject("HUD");
        hudObject.transform.SetParent(root.transform, false);
        WorldMapHudController hud = hudObject.AddComponent<WorldMapHudController>();

        GameObject terrainObject = new GameObject("TerrainRenderer");
        terrainObject.transform.SetParent(root.transform, false);
        TerrainRenderer terrainRenderer = terrainObject.AddComponent<TerrainRenderer>();
        DreamscapeMapArtAdapterBuilder.ConfigureTerrainTextures(terrainRenderer);

        GameObject gridObject = new GameObject("HexGridOverlayRenderer");
        gridObject.transform.SetParent(root.transform, false);
        HexGridOverlayRenderer gridRenderer = gridObject.AddComponent<HexGridOverlayRenderer>();
        DreamscapeMapArtAdapterBuilder.ConfigureGridRenderer(gridRenderer, terrainRenderer);

        GameObject decorationObject = new GameObject("WorldMapDecorationRenderer");
        decorationObject.transform.SetParent(root.transform, false);
        WorldMapDecorationRenderer decorationRenderer =
            decorationObject.AddComponent<WorldMapDecorationRenderer>();
        DreamscapeMapArtAdapterBuilder.ConfigureRenderer(decorationRenderer, terrainRenderer);

        GameObject iconObject = new GameObject("MapIconRenderer");
        iconObject.transform.SetParent(root.transform, false);
        MapIconRenderer iconRenderer = iconObject.AddComponent<MapIconRenderer>();
        SerializedObject iconSerialized = new SerializedObject(iconRenderer);
        iconSerialized.FindProperty("terrainRenderer").objectReferenceValue = terrainRenderer;
        iconSerialized.FindProperty("respectKnowledgeMask").boolValue = true;
        iconSerialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject regionNameObject = new GameObject("RegionNameRenderer");
        regionNameObject.transform.SetParent(root.transform, false);
        RegionNameRenderer regionNameRenderer = regionNameObject.AddComponent<RegionNameRenderer>();

        GameObject maskObject = new GameObject("WorldMapKnowledgeMaskRenderer");
        maskObject.transform.SetParent(root.transform, false);
        WorldMapKnowledgeMaskRenderer maskRenderer = maskObject.AddComponent<WorldMapKnowledgeMaskRenderer>();

        GameObject influenceObject = new GameObject("WorldMapInfluenceOverlayRenderer");
        influenceObject.transform.SetParent(root.transform, false);
        WorldMapInfluenceOverlayRenderer influenceRenderer =
            influenceObject.AddComponent<WorldMapInfluenceOverlayRenderer>();

        GameObject selectionObject = new GameObject("WorldMapSelectionOverlayRenderer");
        selectionObject.transform.SetParent(root.transform, false);
        WorldMapSelectionOverlayRenderer selectionRenderer =
            selectionObject.AddComponent<WorldMapSelectionOverlayRenderer>();

        GameObject veinObject = new GameObject("WorldMapVeinOverlayRenderer");
        veinObject.transform.SetParent(root.transform, false);
        WorldMapVeinOverlayRenderer veinRenderer = veinObject.AddComponent<WorldMapVeinOverlayRenderer>();

        SerializedObject pipelineSerialized = new SerializedObject(pipeline);
        AssignReference(pipelineSerialized, "terrainRenderer", terrainRenderer);
        AssignReference(pipelineSerialized, "gridRenderer", gridRenderer);
        AssignReference(pipelineSerialized, "decorationRenderer", decorationRenderer);
        AssignReference(pipelineSerialized, "iconRenderer", iconRenderer);
        AssignReference(pipelineSerialized, "regionNameRenderer", regionNameRenderer);
        AssignReference(pipelineSerialized, "knowledgeMaskRenderer", maskRenderer);
        AssignReference(pipelineSerialized, "influenceOverlayRenderer", influenceRenderer);
        AssignReference(pipelineSerialized, "selectionOverlayRenderer", selectionRenderer);
        AssignReference(pipelineSerialized, "veinOverlayRenderer", veinRenderer);
        pipelineSerialized.FindProperty("renderKnowledgeMask").boolValue = true;
        pipelineSerialized.FindProperty("renderInfluenceOverlay").boolValue = true;
        pipelineSerialized.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject interactionSerialized = new SerializedObject(interaction);
        AssignReference(interactionSerialized, "terrainRenderer", terrainRenderer);
        interactionSerialized.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject controllerSerialized = new SerializedObject(controller);
        AssignReference(controllerSerialized, "renderPipeline", pipeline);
        AssignReference(controllerSerialized, "interaction", interaction);
        AssignReference(controllerSerialized, "hud", hud);
        controllerSerialized.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"WORLD_MAP_3D_PREFAB_SUCCESS {PrefabPath}");
    }

    private static void AssignReference(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null) throw new System.InvalidOperationException("缺少序列化字段 " + propertyName);
        property.objectReferenceValue = value;
    }
}
