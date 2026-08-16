using Cultivation4X.WorldMap;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 山体 Mesh 调试菜单：用于定位黑色三角面来源。
/// FaceColors 只改写连续地形生成时的顶点色，不改材质与 Shader。
/// </summary>
public static class MountainMeshDebugTools
{
    [MenuItem("Tools/Map System/Mountain Debug/Face Colors: None")]
    public static void SetFaceColorsNone() => SetFaceColors(ContinuousTerrainSurfaceBuilder.MountainFaceDebugMode.None);

    [MenuItem("Tools/Map System/Mountain Debug/Face Colors: Top")]
    public static void SetFaceColorsTop() => SetFaceColors(ContinuousTerrainSurfaceBuilder.MountainFaceDebugMode.Top);

    [MenuItem("Tools/Map System/Mountain Debug/Face Colors: Side")]
    public static void SetFaceColorsSide() => SetFaceColors(ContinuousTerrainSurfaceBuilder.MountainFaceDebugMode.Side);

    [MenuItem("Tools/Map System/Mountain Debug/Face Colors: RisingSide")]
    public static void SetFaceColorsRising() => SetFaceColors(ContinuousTerrainSurfaceBuilder.MountainFaceDebugMode.RisingSide);

    [MenuItem("Tools/Map System/Mountain Debug/Face Colors: CornerClosure")]
    public static void SetFaceColorsCorner() => SetFaceColors(ContinuousTerrainSurfaceBuilder.MountainFaceDebugMode.CornerClosure);

    [MenuItem("Tools/Map System/Mountain Debug/Toggle Rising Vertical Sides")]
    public static void ToggleRising()
    {
        ContinuousTerrainSurfaceBuilder.EnableRisingVerticalSides =
            !ContinuousTerrainSurfaceBuilder.EnableRisingVerticalSides;
        Debug.Log($"EnableRisingVerticalSides = {ContinuousTerrainSurfaceBuilder.EnableRisingVerticalSides}");
    }

    [MenuItem("Tools/Map System/Mountain Debug/Toggle Corner Closures")]
    public static void ToggleCorner()
    {
        ContinuousTerrainSurfaceBuilder.EnableCornerClosures =
            !ContinuousTerrainSurfaceBuilder.EnableCornerClosures;
        Debug.Log($"EnableCornerClosures = {ContinuousTerrainSurfaceBuilder.EnableCornerClosures}");
    }

    private static void SetFaceColors(ContinuousTerrainSurfaceBuilder.MountainFaceDebugMode mode)
    {
        ContinuousTerrainSurfaceBuilder.DebugFaceMode = mode;
        Debug.Log($"Mountain Face Debug Colors = {mode}");
    }
}
