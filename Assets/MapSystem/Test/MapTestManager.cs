using Cultivation4X.WorldMap;
using UnityEngine;

/// <summary>
/// Minimal map-only test entry used by TerrainTest.unity.
/// Generates a fixed-seed world map and publishes it through WorldMapSession
/// for WorldMapPresenter to render. Does not touch save data or game systems.
/// </summary>
public sealed class MapTestManager : MonoBehaviour
{
    [SerializeField] private int width = 96;
    [SerializeField] private int height = 64;
    [SerializeField] private int seed = 20260806;

    private void Awake()
    {
        MapGenerationSettings settings = new MapGenerationSettings
        {
            width = width,
            height = height,
            seed = seed
        };
        WorldMap map = WorldGenerator.Generate(settings);
        WorldMapSession.Set(map, new WorldMapProgressState());
        Debug.Log($"MapTestManager generated {map.width}x{map.height} seed={map.effectiveSeed} cells={map.cells.Length}");
    }
}
