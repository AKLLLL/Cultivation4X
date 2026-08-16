using Cultivation4X.WorldMap;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using System.Linq;

/// <summary>
/// Minimal map-only test entry used by TerrainTest.unity.
/// Generates a fixed-seed world map and publishes it through WorldMapSession
/// for WorldMapPresenter to render. Does not touch save data or game systems.
/// </summary>
public sealed class MapTestManager : MonoBehaviour
{
    [SerializeField] private int width = 128;
    [SerializeField] private int height = 96;
    [SerializeField] private int seed = 20260806;
    [SerializeField] private KeyCode regenerateKey = KeyCode.R;
    [SerializeField] private KeyCode politicalMapKey = KeyCode.M;
    [SerializeField] private KeyCode climateDebugKey = KeyCode.C;
    [SerializeField] private TerrainRenderer terrainRenderer;
    [SerializeField] private WorldMapDecorationRenderer decorationRenderer;
    [SerializeField] private HexGridOverlayRenderer hexGridOverlayRenderer;
    [SerializeField] private MapIconRenderer mapIconRenderer;
    [SerializeField] private RegionNameRenderer regionNameRenderer;
    [SerializeField] private RegionOverlayRenderer regionOverlayRenderer;
    private static Font guiFont;
    private bool politicalMapEnabled;
    private int selectedCellIndex = -1;
    private bool selectionPointerDown;
    private Vector3 selectionPointerStart;
    private const float DefaultDebugCurvature = 0f;
    private const float DefaultDebugNearFieldOfView = 45f;
    private const float DefaultTextureStrength = 0.82f;
    private const float DefaultTextureContrast = 1.55f;
    private const float DefaultTextureTiling = 0.46f;
    private const float DefaultTerrainReliefScale = 1f;
    // 当前里程碑验证生成的高度场。纯地形模式只选择性加回森林树模型簇，
    // 其余美术模型、地图图标与区域覆盖仍保持关闭，避免遮挡地形问题。
    private const bool TerrainOnlyEvaluationMode = true;
    internal static bool TerrainOnlyEvaluationEnabled => TerrainOnlyEvaluationMode;
    private static readonly Rect DebugPanelRect = new Rect(10f, 10f, 720f, 635f);
    private string curvatureInput = "0";
    private string fieldOfViewInput = "45";
    private string textureStrengthInput = "0.82";
    private string textureContrastInput = "1.55";
    private string textureTilingInput = "0.46";
    private string terrainReliefInput = "1.00";
    private bool curvatureOverride;
    private bool fieldOfViewOverride;
    private float debugCurvature = DefaultDebugCurvature;
    private float debugNearFieldOfView = DefaultDebugNearFieldOfView;
    private bool hasSavedCameraView;
    private Vector3 savedCameraPivot;
    private float savedVisibleHexes;
    private string terrainStatisticsText = "地形统计：尚未生成地图";

    private void Awake()
    {
        Regenerate(seed);
    }

    private void Update()
    {
        if (terrainRenderer != null)
            terrainRenderer.SetPointerInputBlocked(IsPointerOverDebugPanel());
        if (Input.GetKeyDown(regenerateKey))
            Regenerate(Random.Range(1, int.MaxValue));
        if (Input.GetKeyDown(politicalMapKey))
            TogglePoliticalMap();
        if (Input.GetKeyDown(climateDebugKey))
            CycleClimateDebugView();
        HandleTerrainSelection();
    }

    private void OnGUI()
    {
        Font font = GuiFont();
        if (font != null) GUI.skin.label.font = font;
        GUI.skin.label.fontSize = 18;
        GUI.skin.button.fontSize = 16;
        GUI.skin.textField.fontSize = 16;
        GUILayout.BeginArea(DebugPanelRect, GUI.skin.box);
        GUILayout.Label($"按 {regenerateKey} 重新随机生成地图 · 当前种子 {seed}");
        if (TerrainOnlyEvaluationMode) GUILayout.Label("纯地形验收：仅保留基础地表、区域名与森林树簇");
        GUILayout.Label($"按 {politicalMapKey} 切换政治地图模式 · 当前 {(politicalMapEnabled ? "开启" : "关闭")}");
        DrawClimateDebugControls();
        DrawTextureControls();
        DrawTerrainSurfaceControls();
        DrawPerspectiveControls();
        DrawCurvatureControls();
        GUILayout.Label(terrainStatisticsText);
        if (selectedCellIndex >= 0 && WorldMapSession.Current?.cells != null &&
            selectedCellIndex < WorldMapSession.Current.cells.Length)
        {
            WorldCell cell = WorldMapSession.Current.cells[selectedCellIndex];
            GUILayout.Label($"已选 Hex {selectedCellIndex} ({cell.coord.col}, {cell.coord.row}) · " +
                            $"{cell.landform} / {cell.biome} · {(cell.isBuildable ? "可建宗" : "不可建宗")}");
            GUILayout.Label($"温度 {cell.temperature:F3} · 湿度 {cell.moisture:F3} · " +
                            $"归一化高度 {cell.height:F3}");
        }
        else
        {
            GUILayout.Label("单击地形选择内部 Hex；拖动仍用于平移地图");
        }
        GUILayout.EndArea();
    }

    private void HandleTerrainSelection()
    {
        if (IsPointerOverDebugPanel())
        {
            selectionPointerDown = false;
            return;
        }
        if (Input.GetMouseButtonDown(0))
        {
            selectionPointerDown = true;
            selectionPointerStart = Input.mousePosition;
        }
        if (!selectionPointerDown || !Input.GetMouseButtonUp(0)) return;
        selectionPointerDown = false;
        if ((Input.mousePosition - selectionPointerStart).sqrMagnitude > 36f || terrainRenderer == null) return;
        if (terrainRenderer.TryPickCell(Camera.main, Input.mousePosition, out int index))
        {
            selectedCellIndex = index;
            WorldCell cell = WorldMapSession.Current?.cells?[index];
            Debug.Log($"TerrainTest selected Hex {index}: {cell?.landform}, buildable={cell?.isBuildable}");
        }
    }

    private void TogglePoliticalMap()
    {
        politicalMapEnabled = !politicalMapEnabled;
        ApplyPoliticalMapMode();
        Debug.Log($"政治地图模式：{(politicalMapEnabled ? "开启" : "关闭")}");
    }

    private void ApplyPoliticalMapMode()
    {
        if (TerrainOnlyEvaluationMode)
        {
            politicalMapEnabled = false;
            // 森林树簇是纯地形模式的唯一保留模型，政治地图切换不得清空它；
            // 其余模型未生成，只需清理覆盖层与图标。
            if (regionOverlayRenderer != null) regionOverlayRenderer.Clear();
            if (mapIconRenderer != null) mapIconRenderer.Clear();
            // 区域名属于远景选址层，TerrainOnly 下仍保留。
            if (regionNameRenderer != null) regionNameRenderer.SetPoliticalMapEnabled(true);
            return;
        }
        if (regionOverlayRenderer != null)
            regionOverlayRenderer.SetPoliticalMapEnabled(politicalMapEnabled);
        if (regionNameRenderer != null)
            regionNameRenderer.SetPoliticalMapEnabled(politicalMapEnabled);
        if (mapIconRenderer != null)
            mapIconRenderer.SetPoliticalMapEnabled(politicalMapEnabled);
    }

    private void DrawClimateDebugControls()
    {
        if (terrainRenderer == null) return;
        GUILayout.BeginHorizontal();
        GUILayout.Label($"按 {climateDebugKey} 切换气候视图 · 当前 " +
                        ClimateDebugLabel(terrainRenderer.ClimateDebugView), GUILayout.Width(300f));
        DrawClimateDebugButton("正常", WorldMapClimateDebugView.Normal);
        DrawClimateDebugButton("群系", WorldMapClimateDebugView.Biome);
        DrawClimateDebugButton("温度", WorldMapClimateDebugView.Temperature);
        DrawClimateDebugButton("湿度", WorldMapClimateDebugView.Moisture);
        DrawClimateDebugButton("降雨", WorldMapClimateDebugView.Rainfall);
        DrawClimateDebugButton("淡水距", WorldMapClimateDebugView.FreshWaterDistance);
        DrawClimateDebugButton("汇流", WorldMapClimateDebugView.DrainageFlow);
        DrawClimateDebugButton("海拔", WorldMapClimateDebugView.Elevation);
        DrawClimateDebugButton("灵气", WorldMapClimateDebugView.Aura);
        DrawClimateDebugButton("五行", WorldMapClimateDebugView.DominantElement);
        DrawClimateDebugButton("灵脉", WorldMapClimateDebugView.SpiritVeinPaths);
        GUILayout.EndHorizontal();
    }

    private void DrawClimateDebugButton(string label, WorldMapClimateDebugView view)
    {
        if (GUILayout.Button(label, GUILayout.Width(72f)))
            terrainRenderer.SetClimateDebugView(view);
    }

    private void CycleClimateDebugView()
    {
        if (terrainRenderer == null) return;
        int count = System.Enum.GetValues(typeof(WorldMapClimateDebugView)).Length;
        WorldMapClimateDebugView next = (WorldMapClimateDebugView)
            (((int)terrainRenderer.ClimateDebugView + 1) % count);
        terrainRenderer.SetClimateDebugView(next);
    }

    private static string ClimateDebugLabel(WorldMapClimateDebugView view)
    {
        switch (view)
        {
            case WorldMapClimateDebugView.Biome: return "生物群系";
            case WorldMapClimateDebugView.Temperature: return "温度";
            case WorldMapClimateDebugView.Moisture: return "湿度（东→西）";
            case WorldMapClimateDebugView.Rainfall: return "地形降雨";
            case WorldMapClimateDebugView.FreshWaterDistance: return "淡水距离（河流/内陆水体）";
            case WorldMapClimateDebugView.DrainageFlow: return "排水累计汇流（对数）";
            case WorldMapClimateDebugView.Elevation: return "海拔";
            case WorldMapClimateDebugView.Aura: return "灵气浓度";
            case WorldMapClimateDebugView.DominantElement: return "五行主属性";
            case WorldMapClimateDebugView.SpiritVeinPaths: return "灵脉路径";
            default: return "正常地图";
        }
    }

    private void DrawTextureControls()
    {
        if (terrainRenderer == null) return;
        GUILayout.BeginHorizontal();
        GUILayout.Label($"地表纹理：强度 {terrainRenderer.GroundTextureStrength:F2} · " +
                        $"对比 {terrainRenderer.GroundTextureContrast:F2} · " +
                        $"平铺 {terrainRenderer.GroundTextureTiling:F2}", GUILayout.Width(365f));
        bool textureOnly = GUILayout.Toggle(terrainRenderer.GroundTextureOnly, "纯纹理检查",
            GUILayout.Width(125f));
        if (textureOnly != terrainRenderer.GroundTextureOnly)
            terrainRenderer.SetGroundTextureDebug(terrainRenderer.GroundTextureStrength,
                terrainRenderer.GroundTextureContrast, terrainRenderer.GroundTextureTiling, textureOnly);
        if (GUILayout.Button("重置纹理", GUILayout.Width(105f)))
            ApplyTextureDebug(DefaultTextureStrength, DefaultTextureContrast, DefaultTextureTiling, false);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("强度", GUILayout.Width(48f));
        textureStrengthInput = GUILayout.TextField(textureStrengthInput, GUILayout.Width(72f));
        GUILayout.Label("对比", GUILayout.Width(48f));
        textureContrastInput = GUILayout.TextField(textureContrastInput, GUILayout.Width(72f));
        GUILayout.Label("平铺", GUILayout.Width(48f));
        textureTilingInput = GUILayout.TextField(textureTilingInput, GUILayout.Width(72f));
        if (GUILayout.Button("应用纹理参数", GUILayout.Width(135f)) &&
            TryParseFloat(textureStrengthInput, out float strength) &&
            TryParseFloat(textureContrastInput, out float contrast) &&
            TryParseFloat(textureTilingInput, out float tiling))
            ApplyTextureDebug(strength, contrast, tiling, terrainRenderer.GroundTextureOnly);
        GUILayout.Label("范围：0～1 / 0.5～2.5 / 0.05～2", GUILayout.Width(210f));
        GUILayout.EndHorizontal();
    }

    private void ApplyTextureDebug(float strength, float contrast, float tiling, bool textureOnly)
    {
        float clampedStrength = Mathf.Clamp01(strength);
        float clampedContrast = Mathf.Clamp(contrast, 0.5f, 2.5f);
        float clampedTiling = Mathf.Clamp(tiling, 0.05f, 2f);
        textureStrengthInput = clampedStrength.ToString("0.00", CultureInfo.InvariantCulture);
        textureContrastInput = clampedContrast.ToString("0.00", CultureInfo.InvariantCulture);
        textureTilingInput = clampedTiling.ToString("0.00", CultureInfo.InvariantCulture);
        terrainRenderer.SetGroundTextureDebug(clampedStrength, clampedContrast, clampedTiling, textureOnly);
    }

    private void DrawTerrainSurfaceControls()
    {
        if (terrainRenderer == null) return;
        GUILayout.BeginHorizontal();
        GUILayout.Label($"连续地形：起伏倍率 {terrainRenderer.TerrainReliefScale:F2}",
            GUILayout.Width(225f));
        terrainReliefInput = GUILayout.TextField(terrainReliefInput, GUILayout.Width(72f));
        if (GUILayout.Button("应用起伏", GUILayout.Width(90f)) &&
            TryParseFloat(terrainReliefInput, out float relief))
            ApplyTerrainRelief(relief);
        if (GUILayout.Button("-0.25", GUILayout.Width(65f)))
            ApplyTerrainRelief(terrainRenderer.TerrainReliefScale - 0.25f);
        if (GUILayout.Button("+0.25", GUILayout.Width(65f)))
            ApplyTerrainRelief(terrainRenderer.TerrainReliefScale + 0.25f);
        if (GUILayout.Button("重置", GUILayout.Width(65f)))
            ApplyTerrainRelief(DefaultTerrainReliefScale);
        GUILayout.EndHorizontal();

        if (hexGridOverlayRenderer == null) return;
        bool showGrid = GUILayout.Toggle(hexGridOverlayRenderer.GridVisible,
            "显示六边形操作网格（关闭后检查裸地形起伏）");
        if (showGrid != hexGridOverlayRenderer.GridVisible)
            hexGridOverlayRenderer.SetGridVisible(showGrid);
    }

    private void ApplyTerrainRelief(float value)
    {
        float clamped = Mathf.Clamp(value, 0.25f, 3f);
        terrainReliefInput = clamped.ToString("0.00", CultureInfo.InvariantCulture);
        terrainRenderer.SetTerrainReliefScale(clamped);
        WorldMap map = WorldMapSession.Current;
        if (map?.cells == null) return;
        if (hexGridOverlayRenderer != null)
        {
            bool visible = hexGridOverlayRenderer.GridVisible;
            hexGridOverlayRenderer.Render(map);
            hexGridOverlayRenderer.SetGridVisible(visible);
        }
        int focusCellIndex = SelectStrategicFocusCell(map);
        if (TerrainOnlyEvaluationMode)
        {
            if (decorationRenderer != null) decorationRenderer.RenderForestTreeClusters(map);
        }
        else if (decorationRenderer != null)
        {
            decorationRenderer.Render(map, focusCellIndex);
            if (regionOverlayRenderer != null) regionOverlayRenderer.Render(map);
            if (mapIconRenderer != null) mapIconRenderer.Render(map, WorldMapSession.Progress);
            if (regionNameRenderer != null) regionNameRenderer.Render(map);
        }
        else if (regionNameRenderer != null)
        {
            regionNameRenderer.Render(map);
        }
        ApplyPoliticalMapMode();
    }

    /// <summary>用指定种子重新生成地图并刷新全部表现层；测试场景按 R 时使用随机种子。</summary>
    public void Regenerate(int nextSeed)
    {
        ClearOptionalPresentationLayers();
        seed = nextSeed;
        selectedCellIndex = -1;
        hasSavedCameraView = false;
        MapGenerationSettings settings = new MapGenerationSettings
        {
            width = width,
            height = height,
            seed = seed
        };
        WorldMap map = WorldGenerator.Generate(settings);
        terrainStatisticsText = BuildTerrainStatisticsText(map);
        WorldMapSession.Set(map, new WorldMapProgressState());
        Debug.Log($"MapTestManager generated {map.width}x{map.height} seed={map.effectiveSeed} cells={map.cells.Length}");
        if (terrainRenderer != null)
        {
            int focusCellIndex = SelectStrategicFocusCell(map);
            terrainRenderer.ApplyWorldMapVisualProfile(focusCellIndex);
            terrainRenderer.Render(map);
            if (fieldOfViewOverride)
                terrainRenderer.SetNearFieldOfView(debugNearFieldOfView);
            if (curvatureOverride)
                terrainRenderer.SetNearRadialCurvature(debugCurvature);
            if (hexGridOverlayRenderer != null)
                hexGridOverlayRenderer.Render(map);
            if (TerrainOnlyEvaluationMode)
            {
                if (decorationRenderer != null)
                    decorationRenderer.RenderForestTreeClusters(map);
            }
            else if (decorationRenderer != null)
            {
                decorationRenderer.Render(map, focusCellIndex);
            }
        }
        else
            Debug.LogWarning("MapTestManager 未关联 TerrainRenderer，跳过 3D 渲染");
        if (regionNameRenderer != null)
            regionNameRenderer.Render(map);
        if (!TerrainOnlyEvaluationMode)
        {
            AddDemoSites(map);
            if (regionOverlayRenderer != null)
                regionOverlayRenderer.Render(map);
            if (mapIconRenderer != null)
                mapIconRenderer.Render(map, WorldMapSession.Progress);
        }
        ApplyPoliticalMapMode();
    }

    private void ClearOptionalPresentationLayers()
    {
        if (decorationRenderer != null) decorationRenderer.Clear();
        if (regionOverlayRenderer != null) regionOverlayRenderer.Clear();
        if (mapIconRenderer != null) mapIconRenderer.Clear();
        if (regionNameRenderer != null) regionNameRenderer.Clear();
    }

    internal static string BuildTerrainStatisticsText(WorldMap map)
    {
        if (map?.cells == null || map.cells.Length == 0)
            return "地形统计：地图为空";

        int land = 0;
        int flat = 0;
        int hills = 0;
        int mountains = 0;
        int dryLand = 0;
        int wetLand = 0;
        int coast = 0;
        int desert = 0;
        int grassland = 0;
        int temperateForest = 0;
        int rainforest = 0;
        int wetland = 0;
        int tundra = 0;
        int snowfield = 0;
        int alpine = 0;
        int terraceGroups = 0;
        int terraceCells = 0;
        foreach (WorldCell cell in map.cells)
        {
            if (cell == null || cell.landform == LandformType.DeepWater ||
                cell.landform == LandformType.ShallowWater) continue;
            land++;
            if (cell.landform == LandformType.Coast) coast++;
            if (cell.biome == BiomeType.Desert) desert++;
            switch (cell.biome)
            {
                case BiomeType.Grassland: grassland++; break;
                case BiomeType.TemperateForest: temperateForest++; break;
                case BiomeType.Rainforest: rainforest++; break;
                case BiomeType.Wetland: wetland++; break;
                case BiomeType.Tundra: tundra++; break;
                case BiomeType.Snowfield: snowfield++; break;
                case BiomeType.Alpine: alpine++; break;
            }
            switch (cell.landform)
            {
                case LandformType.Coast:
                case LandformType.Plain:
                    flat++;
                    break;
                case LandformType.Hill:
                    hills++;
                    break;
                case LandformType.Mountain:
                    mountains++;
                    break;
            }

            if (cell.landform == LandformType.Mountain && cell.isBuildable)
            {
                terraceCells++;
            }

            if (cell.moisture < 0.22f) dryLand++;
            else if (cell.moisture >= 0.66f) wetLand++;
        }

        if (land == 0) return "地形统计：没有陆地";
        terraceGroups = map.cells
            .Where(cell => cell != null && cell.landform == LandformType.Mountain && cell.isBuildable)
            .GroupBy(cell => cell.regionId)
            .Count();
        int largestMountainRange = LargestMountainComponent(map);
        int denseMountainCore = CountDenseMountainCore(map);
        float denseMountainPercentage = mountains > 0 ? Percentage(denseMountainCore, mountains) : 0f;
        int maximumMountainThickness = MaximumMountainThickness(map);
        int moderateLand = land - dryLand - wetLand;
        string hydrology = "";
        if (WorldGenerationDiagnosticsStore.TryGet(map, out WorldGenerationDiagnostics diagnostics))
        {
            int[] finiteLandDistances = map.cells
                .Where(cell => cell != null && cell.landform != LandformType.DeepWater &&
                               cell.landform != LandformType.ShallowWater)
                .Select(cell => diagnostics.freshWaterDistance[cell.index])
                .Where(distance => distance != int.MaxValue)
                .ToArray();
            float meanFreshWaterDistance = finiteLandDistances.Length == 0
                ? -1f
                : (float)finiteLandDistances.Average();
            hydrology = $"\n水文诊断：河段 {map.rivers.Count} · " +
                        $"陆地平均淡水距离 {(meanFreshWaterDistance < 0f ? "无淡水源" : meanFreshWaterDistance.ToString("F1"))} 格 · " +
                        $"最大累计汇流 {diagnostics.maximumAccumulatedFlow:F0}";
        }
        return $"地形统计（占陆地）：平原/海岸 {Percentage(flat, land):F1}% · " +
               $"丘陵 {Percentage(hills, land):F1}% · 高山 {Percentage(mountains, land):F1}% · " +
               $"台地 {terraceGroups} 组 / {terraceCells} 格 · " +
               $"最大连续山脉 {largestMountainRange} 格 · 内核 {denseMountainPercentage:F1}% · " +
               $"最大厚度约 {maximumMountainThickness} 格\n" +
               $"沙地外观：沙漠 {Percentage(desert, land):F1}% · 海岸 {Percentage(coast, land):F1}% · " +
               $"合计 {Percentage(desert + coast, land):F1}%\n" +
               $"陆地湿度：干燥(<0.22) {Percentage(dryLand, land):F1}% · " +
               $"中湿 {Percentage(moderateLand, land):F1}% · 湿润(>=0.66) {Percentage(wetLand, land):F1}%\n" +
               $"温暖群系：草原 {Percentage(grassland, land):F1}% · " +
               $"温带林 {Percentage(temperateForest, land):F1}% · " +
               $"雨林 {Percentage(rainforest, land):F1}% · 湿地 {Percentage(wetland, land):F1}%\n" +
               $"寒冷/高地：苔原 {Percentage(tundra, land):F1}% · " +
               $"雪原 {Percentage(snowfield, land):F1}% · 高山 {Percentage(alpine, land):F1}%" +
               hydrology;
    }

    internal static int LargestMountainComponent(WorldMap map)
    {
        if (map?.cells == null || map.cells.Length == 0) return 0;
        bool[] visited = new bool[map.cells.Length];
        Queue<int> pending = new Queue<int>();
        int largest = 0;
        for (int start = 0; start < map.cells.Length; start++)
        {
            if (visited[start] || map.cells[start] == null ||
                map.cells[start].landform != LandformType.Mountain) continue;
            visited[start] = true;
            pending.Enqueue(start);
            int size = 0;
            while (pending.Count > 0)
            {
                int current = pending.Dequeue();
                size++;
                foreach (int neighbor in map.GetNeighborIndices(current))
                {
                    if (visited[neighbor] || map.cells[neighbor] == null ||
                        map.cells[neighbor].landform != LandformType.Mountain) continue;
                    visited[neighbor] = true;
                    pending.Enqueue(neighbor);
                }
            }
            if (size > largest) largest = size;
        }
        return largest;
    }

    internal static int CountDenseMountainCore(WorldMap map)
    {
        if (map?.cells == null || map.cells.Length == 0) return 0;
        int dense = 0;
        foreach (WorldCell cell in map.cells)
        {
            if (cell == null || cell.landform != LandformType.Mountain) continue;
            int[] neighbors = new List<int>(map.GetNeighborIndices(cell.index)).ToArray();
            if (neighbors.Length == 6 &&
                System.Array.TrueForAll(neighbors, index =>
                    map.cells[index] != null && map.cells[index].landform == LandformType.Mountain))
                dense++;
        }
        return dense;
    }

    /// <summary>从山脉边缘向内部做距离场，返回约等于直径的最大局部厚度。</summary>
    internal static int MaximumMountainThickness(WorldMap map)
    {
        if (map?.cells == null || map.cells.Length == 0) return 0;
        int[] depth = new int[map.cells.Length];
        Queue<int> pending = new Queue<int>();
        foreach (WorldCell cell in map.cells)
        {
            if (cell == null || cell.landform != LandformType.Mountain) continue;
            int[] neighbors = new List<int>(map.GetNeighborIndices(cell.index)).ToArray();
            bool boundary = neighbors.Length < 6 ||
                            System.Array.Exists(neighbors, index =>
                                map.cells[index] == null ||
                                map.cells[index].landform != LandformType.Mountain);
            if (!boundary) continue;
            depth[cell.index] = 1;
            pending.Enqueue(cell.index);
        }

        int maximumDepth = 0;
        while (pending.Count > 0)
        {
            int current = pending.Dequeue();
            if (depth[current] > maximumDepth) maximumDepth = depth[current];
            foreach (int neighbor in map.GetNeighborIndices(current))
            {
                if (depth[neighbor] != 0 || map.cells[neighbor] == null ||
                    map.cells[neighbor].landform != LandformType.Mountain) continue;
                depth[neighbor] = depth[current] + 1;
                pending.Enqueue(neighbor);
            }
        }
        return maximumDepth == 0 ? 0 : maximumDepth * 2 - 1;
    }

    private static float Percentage(int count, int total)
    {
        return total > 0 ? count * 100f / total : 0f;
    }

    /// <summary>
    /// 为视觉测试选择可建宗、临近山体并兼顾水面的格子，只决定初始镜头落点。
    /// </summary>
    internal static int SelectStrategicFocusCell(WorldMap map)
    {
        if (map?.cells == null || map.cells.Length == 0) return -1;
        int bestIndex = -1;
        float bestScore = float.NegativeInfinity;
        foreach (WorldCell candidate in map.cells)
        {
            if (candidate == null || !candidate.isBuildable ||
                candidate.landform == LandformType.Mountain ||
                candidate.landform == LandformType.DeepWater ||
                candidate.landform == LandformType.ShallowWater) continue;
            float score = candidate.landform == LandformType.Plain ? 4f : 1f;
            if (candidate.internalPositionTag == MapInternalPositionTag.ValleyFloor) score += 10f;
            for (int row = Mathf.Max(0, candidate.coord.row - 4);
                 row <= Mathf.Min(map.height - 1, candidate.coord.row + 4); row++)
            {
                for (int col = Mathf.Max(0, candidate.coord.col - 4);
                     col <= Mathf.Min(map.width - 1, candidate.coord.col + 4); col++)
                {
                    int index = map.GetIndex(new HexCoord(col, row));
                    if (index < 0 || index >= map.cells.Length || map.cells[index] == null) continue;
                    int distance = HexCoord.Distance(candidate.coord, map.cells[index].coord);
                    if (distance == 0 || distance > 4) continue;
                    float weight = 1f / distance;
                    switch (map.cells[index].landform)
                    {
                        case LandformType.Mountain: score += 8f * weight; break;
                        case LandformType.Hill: score += 2.5f * weight; break;
                        case LandformType.DeepWater:
                        case LandformType.ShallowWater: score += 1.5f * weight; break;
                    }
                }
            }
            if (score <= bestScore) continue;
            bestScore = score;
            bestIndex = candidate.index;
        }
        if (bestIndex >= 0) return bestIndex;
        return Mathf.Clamp((map.height / 2) * map.width + map.width / 2, 0, map.cells.Length - 1);
    }

    private static Font GuiFont()
    {
        if (guiFont != null) return guiFont;
        guiFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
        return guiFont;
    }

    private void DrawCurvatureControls()
    {
        if (terrainRenderer == null) return;
        GUILayout.Label($"近景曲率 {terrainRenderer.NearRadialCurvature:F6} · " +
                        $"当前缩放实际强度 {terrainRenderer.ActiveCurveStrength:F6}");
        GUILayout.BeginHorizontal();
        curvatureInput = GUILayout.TextField(curvatureInput, GUILayout.Width(100f));
        if (GUILayout.Button("应用", GUILayout.Width(64f)) &&
            TryParseFloat(curvatureInput, out float parsed))
            ApplyDebugCurvature(parsed, true);
        if (GUILayout.Button("-0.001", GUILayout.Width(78f)))
            ApplyDebugCurvature(terrainRenderer.NearRadialCurvature - 0.001f, true);
        if (GUILayout.Button("-0.0005", GUILayout.Width(88f)))
            ApplyDebugCurvature(terrainRenderer.NearRadialCurvature - 0.0005f, true);
        if (GUILayout.Button("+0.0005", GUILayout.Width(88f)))
            ApplyDebugCurvature(terrainRenderer.NearRadialCurvature + 0.0005f, true);
        if (GUILayout.Button("+0.001", GUILayout.Width(78f)))
            ApplyDebugCurvature(terrainRenderer.NearRadialCurvature + 0.001f, true);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("关闭曲率", GUILayout.Width(120f)))
            ApplyDebugCurvature(0f, true);
        if (GUILayout.Button("重置 0", GUILayout.Width(140f)))
            ApplyDebugCurvature(DefaultDebugCurvature, false);
        GUILayout.Label("允许范围 0 ～ 0.02；建议每次调整 0.0005");
        GUILayout.EndHorizontal();
    }

    private void DrawPerspectiveControls()
    {
        if (terrainRenderer == null) return;
        GUILayout.Label($"Civ Zoom {terrainRenderer.ZoomLevel:F2} (当前 {terrainRenderer.CurrentZoom:F2}) · " +
                        $"层级 {terrainRenderer.CurrentDetailLevel} · " +
                        $"高度 {terrainRenderer.CameraHeightForZoom(terrainRenderer.CurrentZoom):F1} · " +
                        $"俯仰 {terrainRenderer.CameraPitchForZoom(terrainRenderer.CurrentZoom):F1}° · " +
                        "移动：WASD");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-0.1", GUILayout.Width(64f)))
            terrainRenderer.SetZoomLevel(terrainRenderer.ZoomLevel - 0.1f);
        if (GUILayout.Button("+0.1", GUILayout.Width(64f)))
            terrainRenderer.SetZoomLevel(terrainRenderer.ZoomLevel + 0.1f);
        if (GUILayout.Button("最近 0", GUILayout.Width(80f)))
            terrainRenderer.SetZoomLevel(0f);
        if (GUILayout.Button("中景 0.35", GUILayout.Width(110f)))
            terrainRenderer.SetZoomLevel(0.35f);
        if (GUILayout.Button("最远 1", GUILayout.Width(80f)))
            terrainRenderer.SetZoomLevel(1f);
        GUILayout.EndHorizontal();
        GUILayout.Label($"近景 FOV {terrainRenderer.NearFieldOfViewDegrees:F1}° · " +
                        $"当前 FOV {terrainRenderer.ActiveFieldOfViewDegrees:F1}° · " +
                        $"横向可见 {terrainRenderer.ActiveVisibleHexesAcross:F2} 格 · " +
                        $"距离 {terrainRenderer.ActiveCameraDistance:F2}");
        GUILayout.BeginHorizontal();
        fieldOfViewInput = GUILayout.TextField(fieldOfViewInput, GUILayout.Width(100f));
        if (GUILayout.Button("应用", GUILayout.Width(64f)) &&
            TryParseFloat(fieldOfViewInput, out float parsed))
            ApplyDebugFieldOfView(parsed, true);
        if (GUILayout.Button("-5°", GUILayout.Width(64f)))
            ApplyDebugFieldOfView(terrainRenderer.NearFieldOfViewDegrees - 5f, true);
        if (GUILayout.Button("-1°", GUILayout.Width(64f)))
            ApplyDebugFieldOfView(terrainRenderer.NearFieldOfViewDegrees - 1f, true);
        if (GUILayout.Button("+1°", GUILayout.Width(64f)))
            ApplyDebugFieldOfView(terrainRenderer.NearFieldOfViewDegrees + 1f, true);
        if (GUILayout.Button("+5°", GUILayout.Width(64f)))
            ApplyDebugFieldOfView(terrainRenderer.NearFieldOfViewDegrees + 5f, true);
        if (GUILayout.Button("重置 45°", GUILayout.Width(110f)))
            ApplyDebugFieldOfView(DefaultDebugNearFieldOfView, false);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("基准 30° / 平面", GUILayout.Width(150f)))
            ApplyPerspectivePreset(30f, 0f);
        if (GUILayout.Button("中等 40° / 平面", GUILayout.Width(160f)))
            ApplyPerspectivePreset(40f, 0f);
        if (GUILayout.Button("强透视 50° / 平面", GUILayout.Width(170f)))
            ApplyPerspectivePreset(50f, 0f);
        if (GUILayout.Button("混合 45° / 0", GUILayout.Width(175f)))
            ApplyPerspectivePreset(45f, 0f);
        GUILayout.EndHorizontal();
        GUILayout.Label("FOV 范围 20°～70°；改变 FOV 时自动保持当前横向覆盖格数");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("记录当前机位", GUILayout.Width(140f)))
            CaptureCameraView();
        GUI.enabled = hasSavedCameraView;
        if (GUILayout.Button("恢复记录机位", GUILayout.Width(140f)))
            RestoreCameraView();
        GUI.enabled = true;
        GUILayout.Label(hasSavedCameraView
            ? $"已记录横向可见 {savedVisibleHexes:F2} 格"
            : "尚未记录机位；重新生成地图后记录失效");
        GUILayout.EndHorizontal();
    }

    private void CaptureCameraView()
    {
        hasSavedCameraView = terrainRenderer.TryCaptureCameraView(
            out savedCameraPivot, out savedVisibleHexes);
    }

    private void RestoreCameraView()
    {
        if (!hasSavedCameraView) return;
        terrainRenderer.RestoreCameraView(savedCameraPivot, savedVisibleHexes);
    }

    private void ApplyDebugFieldOfView(float value, bool isOverride)
    {
        debugNearFieldOfView = Mathf.Clamp(value, 20f, 70f);
        fieldOfViewOverride = isOverride;
        fieldOfViewInput = debugNearFieldOfView.ToString("0.0", CultureInfo.InvariantCulture);
        terrainRenderer.SetNearFieldOfView(debugNearFieldOfView);
    }

    private void ApplyPerspectivePreset(float fieldOfView, float curvature)
    {
        ApplyDebugFieldOfView(fieldOfView, true);
        ApplyDebugCurvature(curvature, true);
    }

    private void ApplyDebugCurvature(float value, bool isOverride)
    {
        debugCurvature = Mathf.Clamp(value, 0f, 0.02f);
        curvatureOverride = isOverride;
        curvatureInput = debugCurvature.ToString("0.000000", CultureInfo.InvariantCulture);
        terrainRenderer.SetNearRadialCurvature(debugCurvature);
    }

    private static bool TryParseFloat(string text, out float value)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;
        return float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static bool IsPointerOverDebugPanel()
    {
        Vector2 guiPointer = new Vector2(Input.mousePosition.x,
            Screen.height - Input.mousePosition.y);
        return DebugPanelRect.Contains(guiPointer);
    }

    /// <summary>测试场景专用：按固定规则放几个演示地点，便于检查图标表现层。</summary>
    private static void AddDemoSites(WorldMap map)
    {
        if (map?.cells == null || WorldMapSession.Progress == null) return;
        WorldMapProgressState progress = WorldMapSession.Progress;
        progress.mapSites.Clear();

        AddSite(progress, map, "demo_village", MapSiteType.Village, cell =>
            cell.landform == LandformType.Plain && cell.isBuildable);
        AddSite(progress, map, "demo_mine", MapSiteType.SpiritMine, cell =>
            cell.landform == LandformType.Mountain);

        int springIndex = -1;
        float bestAura = -1f;
        for (int index = 0; index < map.cells.Length; index++)
        {
            WorldCell cell = map.cells[index];
            if (cell == null || cell.landform == LandformType.DeepWater ||
                cell.landform == LandformType.Mountain) continue;
            if (cell.totalAura > bestAura)
            {
                bestAura = cell.totalAura;
                springIndex = index;
            }
        }
        if (springIndex >= 0)
        {
            AddSite(progress, map, "demo_spring", MapSiteType.SpiritSpring,
                cell => cell.index == springIndex);
        }
        AddSite(progress, map, "demo_cave", MapSiteType.CaveResidence, cell =>
            cell.landform == LandformType.Hill);
        AddSite(progress, map, "demo_ruin", MapSiteType.Ruin, cell =>
            cell.landform == LandformType.Mountain);
    }

    private static void AddSite(WorldMapProgressState progress, WorldMap map, string siteId,
        MapSiteType siteType, System.Func<WorldCell, bool> predicate)
    {
        int index = -1;
        for (int i = 0; i < map.cells.Length; i++)
        {
            WorldCell cell = map.cells[i];
            bool occupied = false;
            foreach (MapSiteData existing in progress.mapSites)
            {
                if (existing != null && existing.cellIndex == i)
                {
                    occupied = true;
                    break;
                }
            }
            if (cell != null && !occupied && predicate(cell))
            {
                index = i;
                break;
            }
        }
        if (index < 0) return;
        progress.mapSites.Add(new MapSiteData
        {
            siteId = siteId,
            cellIndex = index,
            siteType = siteType,
            siteName = TerrainPresentationModels.SiteLabel(siteType),
            isRevealed = true,
            revealState = MapContentRevealState.Discovered,
            siteState = MapSiteState.None
        });
    }
}
