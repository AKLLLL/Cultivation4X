using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cultivation4X.WorldMap
{
    public partial class WorldMapPresenter
    {
        private const float PresentationHexRadius = 0.96f;
        private readonly Dictionary<string, Transform> layerRoots = new Dictionary<string, Transform>();
        private readonly List<WorldMapPresentationMarker> externalPresentationMarkers =
            new List<WorldMapPresentationMarker>();
        private readonly List<GameObject> observabilityPages = new List<GameObject>();
        private List<WorldMapPresentationMarker> presentationMarkers = new List<WorldMapPresentationMarker>();
        private WorldMapViewMode viewMode = WorldMapViewMode.Landform;
        private GameObject observabilityRoot;
        private TMP_Text legendText;
        private TMP_Text statisticsText;
        private TMP_Text parametersText;
        private WorldMapIconDensityTier lastDensityTier = WorldMapIconDensityTier.Hidden;
        private float lastPresentationOrthographicSize = -1f;

        public WorldMapViewMode ViewMode => viewMode;
        public int SelectedCellIndex => selectedCellIndex;

        public void SetViewMode(WorldMapViewMode mode)
        {
            bool selecting = PlayerManager.Instance?.playerData?.founding?.stage == FoundingStage.WorldSelection;
            if (selecting && mode != WorldMapViewMode.Landform) return;
            if (viewMode == mode) return;
            viewMode = mode;
            Rebuild();
            RefreshLegend();
            RefreshDetails();
        }

        public void SetPresentationMarkers(IEnumerable<WorldMapPresentationMarker> markers)
        {
            externalPresentationMarkers.Clear();
            if (markers != null)
                externalPresentationMarkers.AddRange(markers
                    .Where(marker => marker != null)
                    .Select(CloneMarker));
            RefreshPresentationMarkers();
            Rebuild();
            RefreshDetails();
        }

        private static WorldMapPresentationMarker CloneMarker(WorldMapPresentationMarker marker) =>
            new WorldMapPresentationMarker
            {
                id = marker.id,
                label = marker.label,
                kind = marker.kind,
                cellIndex = marker.cellIndex,
                isDemo = false
            };

        private void CreateLayerRoots()
        {
            foreach (string layerName in new[]
            {
                "Terrain", "Boundaries", "TerrainIcons", "Rivers", "SpiritVeins",
                "FactionMarkers", "LocationMarkers", "Selection"
            })
            {
                GameObject root = new GameObject(layerName);
                root.transform.SetParent(transform, false);
                layerRoots[layerName] = root.transform;
            }
        }

        private Transform Layer(string name) =>
            layerRoots.TryGetValue(name, out Transform root) ? root : transform;

        private void RefreshPresentationMarkers()
        {
            presentationMarkers = externalPresentationMarkers.Select(CloneMarker).ToList();
            presentationMarkers.AddRange(WorldMapPresentationMarkerFactory.CreatePointOfInterestMarkers(map));

            int caveIndex = PlayerManager.Instance?.playerData?.founding?.selectedWorldCellIndex ?? -1;
            if (map?.cells != null && caveIndex >= 0 && caveIndex < map.cells.Length)
            {
                presentationMarkers.Add(new WorldMapPresentationMarker
                {
                    id = "player_cave",
                    label = "宗门洞府",
                    kind = WorldMapMarkerKind.Cave,
                    cellIndex = caveIndex
                });
            }
        }

        private void BuildBoundaries()
        {
            if (viewMode != WorldMapViewMode.Landform && viewMode != WorldMapViewMode.Biome) return;
            WorldMapGeometryBuffer buffer = new WorldMapGeometryBuffer();
            foreach (WorldCell cell in map.cells)
            {
                for (int direction = 0; direction < 6; direction++)
                {
                    int neighborIndex = map.GetIndex(map.GetNeighbor(cell.coord, direction));
                    if (neighborIndex <= cell.index) continue;
                    WorldCell neighbor = map.cells[neighborIndex];
                    bool cellWater = IsWater(cell.landform);
                    bool neighborWater = IsWater(neighbor.landform);
                    bool coastBoundary = cellWater != neighborWater;
                    bool landformBoundary = viewMode == WorldMapViewMode.Landform &&
                                             !cellWater && !neighborWater &&
                                             BoundaryClass(cell.landform) != BoundaryClass(neighbor.landform);
                    if (!coastBoundary && !landformBoundary) continue;

                    Vector2 center = Center(cell.coord);
                    float firstAngle = Mathf.Deg2Rad * (60f * direction - 30f);
                    float secondAngle = Mathf.Deg2Rad * (60f * (direction + 1) - 30f);
                    Color color = coastBoundary
                        ? new Color(0.92f, 0.88f, 0.68f, 0.85f)
                        : new Color(0.08f, 0.08f, 0.07f, 0.34f);
                    buffer.AddLine(
                        center + new Vector2(Mathf.Cos(firstAngle), Mathf.Sin(firstAngle)) * PresentationHexRadius,
                        center + new Vector2(Mathf.Cos(secondAngle), Mathf.Sin(secondAngle)) * PresentationHexRadius,
                        coastBoundary ? 0.09f : 0.045f, color);
                }
            }
            AddMeshObject("LandformBoundaries", buffer, Layer("Boundaries"), 1);
        }

        private static bool IsWater(LandformType landform) =>
            landform == LandformType.DeepWater || landform == LandformType.ShallowWater;

        private static int BoundaryClass(LandformType landform)
        {
            switch (landform)
            {
                case LandformType.Plain:
                case LandformType.Coast: return 1;
                case LandformType.Hill: return 2;
                case LandformType.Mountain: return 3;
                default: return 0;
            }
        }

        private void BuildPresentationLayers()
        {
            if (mapCamera == null || map?.cells == null) return;
            float projectedDiameter = ProjectedHexDiameter();
            lastDensityTier = WorldMapPresentationPolicy.GetDensityTier(projectedDiameter);
            lastPresentationOrthographicSize = mapCamera.orthographicSize;

            WorldMapGeometryBuffer terrain = new WorldMapGeometryBuffer();
            foreach (WorldMapTerrainIconPlacement placement in
                     WorldMapPresentationPolicy.BuildTerrainIconPlacements(
                         map, viewMode, projectedDiameter, presentationMarkers))
            {
                WorldMapIconGeometry.AddTerrainIcon(terrain, placement.kind,
                    Center(map.cells[placement.cellIndex].coord), 0.72f, TerrainIconColor(placement.kind));
            }
            AddMeshObject("TerrainIcons", terrain, Layer("TerrainIcons"), 2);

            float pixelsPerWorldUnit = Mathf.Max(0.001f, Screen.height / (2f * mapCamera.orthographicSize));
            float markerSize = Mathf.Clamp(25f / pixelsPerWorldUnit, 0.65f, 2.35f);
            float alpha = WorldMapPresentationPolicy.MarkerAlpha(viewMode);
            WorldMapGeometryBuffer factions = new WorldMapGeometryBuffer();
            WorldMapGeometryBuffer locations = new WorldMapGeometryBuffer();
            WorldMapGeometryBuffer caves = new WorldMapGeometryBuffer();
            foreach (WorldMapPresentationMarker marker in presentationMarkers)
            {
                if (marker.cellIndex < 0 || marker.cellIndex >= map.cells.Length ||
                    !WorldMapPresentationPolicy.MarkerVisible(marker, viewMode, lastDensityTier, map.effectiveSeed))
                    continue;
                WorldMapGeometryBuffer target = marker.kind == WorldMapMarkerKind.Cave
                    ? caves
                    : marker.kind == WorldMapMarkerKind.FactionSeat ? factions : locations;
                Color color = MarkerColor(marker.kind);
                color.a *= alpha;
                WorldMapIconGeometry.AddMarkerIcon(target, marker.kind,
                    Center(map.cells[marker.cellIndex].coord), markerSize, color);
            }
            AddMeshObject("LocationMarkers", locations, Layer("LocationMarkers"), 6);
            AddMeshObject("FactionMarkers", factions, Layer("FactionMarkers"), 7);
            AddMeshObject("CaveMarkers", caves, Layer("LocationMarkers"), 8);
        }

        private void RefreshPresentationForZoom()
        {
            if (map == null || mapCamera == null) return;
            WorldMapIconDensityTier tier =
                WorldMapPresentationPolicy.GetDensityTier(ProjectedHexDiameter());
            bool markerScaleChanged = lastPresentationOrthographicSize <= 0f ||
                                      Mathf.Abs(mapCamera.orthographicSize /
                                                lastPresentationOrthographicSize - 1f) >= 0.12f;
            if (tier != lastDensityTier || markerScaleChanged) Rebuild();
        }

        private float ProjectedHexDiameter() =>
            mapCamera == null
                ? 0f
                : PresentationHexRadius * Screen.height / Mathf.Max(0.001f, mapCamera.orthographicSize);

        private static Color TerrainIconColor(WorldMapTerrainIconKind kind)
        {
            switch (kind)
            {
                case WorldMapTerrainIconKind.Water: return new Color(0.72f, 0.90f, 1f, 0.82f);
                case WorldMapTerrainIconKind.Plain: return new Color(0.10f, 0.22f, 0.08f, 0.72f);
                case WorldMapTerrainIconKind.Hill: return new Color(0.20f, 0.15f, 0.07f, 0.78f);
                case WorldMapTerrainIconKind.Mountain: return new Color(0.12f, 0.13f, 0.15f, 0.86f);
                case WorldMapTerrainIconKind.Forest: return new Color(0.04f, 0.20f, 0.08f, 0.80f);
                default: return new Color(0.96f, 0.99f, 1f, 0.94f);
            }
        }

        private static Color MarkerColor(WorldMapMarkerKind kind)
        {
            switch (kind)
            {
                case WorldMapMarkerKind.FactionSeat: return new Color(0.98f, 0.32f, 0.22f);
                case WorldMapMarkerKind.Village: return new Color(0.98f, 0.82f, 0.32f);
                case WorldMapMarkerKind.Cave: return new Color(1f, 0.72f, 0.08f);
                default: return new Color(0.38f, 0.88f, 1f);
            }
        }

        private void AddMeshObject(string name, WorldMapGeometryBuffer buffer, Transform parent, int order)
        {
            if (buffer == null || buffer.triangles.Count == 0) return;
            Mesh mesh = buffer.CreateMesh(name + "Mesh");
            GameObject obj = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            generatedObjects.Add(obj);
        }

        private Color CellColor(WorldCell cell)
        {
            switch (viewMode)
            {
                case WorldMapViewMode.Landform: return LandformColor(cell.landform);
                case WorldMapViewMode.Height:
                    return Color.Lerp(Color.black, Color.white, Mathf.Clamp01(cell.height));
                case WorldMapViewMode.Temperature: return TemperatureColor(cell.temperature);
                case WorldMapViewMode.Moisture: return MoistureColor(cell.moisture);
                case WorldMapViewMode.Biome: return TerrainColor(cell);
                case WorldMapViewMode.AuraConcentration: return AuraColor(cell.totalAura);
                case WorldMapViewMode.DominantElement: return DominantElementColor(cell);
                default: return new Color(0.12f, 0.13f, 0.14f);
            }
        }

        private static Color LandformColor(LandformType landform)
        {
            switch (landform)
            {
                case LandformType.DeepWater: return new Color(0.04f, 0.15f, 0.32f);
                case LandformType.ShallowWater: return new Color(0.08f, 0.38f, 0.60f);
                case LandformType.Coast: return new Color(0.82f, 0.73f, 0.43f);
                case LandformType.Plain: return new Color(0.40f, 0.66f, 0.28f);
                case LandformType.Hill: return new Color(0.48f, 0.44f, 0.24f);
                default: return new Color(0.48f, 0.49f, 0.51f);
            }
        }

        private static Color TemperatureColor(float value)
        {
            float normalized = Mathf.Clamp01(value);
            if (normalized < 0.33f)
                return Color.Lerp(new Color(0.08f, 0.18f, 0.75f),
                    new Color(0.10f, 0.85f, 0.90f), normalized / 0.33f);
            if (normalized < 0.66f)
                return Color.Lerp(new Color(0.10f, 0.85f, 0.90f),
                    new Color(0.95f, 0.85f, 0.18f), (normalized - 0.33f) / 0.33f);
            return Color.Lerp(new Color(0.95f, 0.85f, 0.18f),
                new Color(0.90f, 0.10f, 0.08f), (normalized - 0.66f) / 0.34f);
        }

        private static Color MoistureColor(float value)
        {
            float normalized = Mathf.Clamp01(value);
            if (normalized < 0.5f)
                return Color.Lerp(new Color(0.58f, 0.32f, 0.12f),
                    new Color(0.28f, 0.70f, 0.28f), normalized * 2f);
            return Color.Lerp(new Color(0.28f, 0.70f, 0.28f),
                new Color(0.10f, 0.35f, 0.92f), (normalized - 0.5f) * 2f);
        }

        private static Color DominantElementColor(WorldCell cell)
        {
            SpiritElement dominant = SpiritElement.Metal;
            float maximum = cell.elementalAura.metal;
            if (cell.elementalAura.wood > maximum)
            { dominant = SpiritElement.Wood; maximum = cell.elementalAura.wood; }
            if (cell.elementalAura.water > maximum)
            { dominant = SpiritElement.Water; maximum = cell.elementalAura.water; }
            if (cell.elementalAura.fire > maximum)
            { dominant = SpiritElement.Fire; maximum = cell.elementalAura.fire; }
            if (cell.elementalAura.earth > maximum) dominant = SpiritElement.Earth;
            return Color.Lerp(new Color(0.025f, 0.025f, 0.03f), ElementColor(dominant),
                0.15f + Mathf.Clamp01(cell.totalAura) * 0.85f);
        }

        private void CreateObservabilityHud(Transform canvas)
        {
            observabilityRoot = new GameObject("WorldMapObservability", typeof(RectTransform),
                typeof(Image), typeof(VerticalLayoutGroup));
            observabilityRoot.transform.SetParent(canvas, false);
            RectTransform rect = observabilityRoot.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(12f, 0f);
            rect.sizeDelta = new Vector2(410f, -24f);
            observabilityRoot.GetComponent<Image>().color = new Color(0.04f, 0.04f, 0.04f, 0.86f);
            VerticalLayoutGroup layout = observabilityRoot.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 7f;
            layout.childForceExpandHeight = false;

            AddObservabilityText(observabilityRoot.transform, "世界地图", 25, 38);
            GameObject tabs = new GameObject("Tabs", typeof(RectTransform),
                typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            tabs.transform.SetParent(observabilityRoot.transform, false);
            tabs.GetComponent<LayoutElement>().preferredHeight = 38f;
            HorizontalLayoutGroup tabLayout = tabs.GetComponent<HorizontalLayoutGroup>();
            tabLayout.spacing = 5f;
            tabLayout.childForceExpandWidth = true;
            AddObservabilityButton(tabs.transform, "视图", () => ShowObservabilityPage(0));
            AddObservabilityButton(tabs.transform, "统计", () => ShowObservabilityPage(1));
            AddObservabilityButton(tabs.transform, "参数", () => ShowObservabilityPage(2));

            Transform viewPage = CreateObservabilityScrollPage("ViewPage");
            AddViewButton(viewPage, "地貌", WorldMapViewMode.Landform);
            AddViewButton(viewPage, "高度", WorldMapViewMode.Height);
            AddViewButton(viewPage, "温度", WorldMapViewMode.Temperature);
            AddViewButton(viewPage, "湿度", WorldMapViewMode.Moisture);
            AddViewButton(viewPage, "群系", WorldMapViewMode.Biome);
            AddViewButton(viewPage, "灵气浓度", WorldMapViewMode.AuraConcentration);
            AddViewButton(viewPage, "五行主属性", WorldMapViewMode.DominantElement);
            AddViewButton(viewPage, "灵脉路径", WorldMapViewMode.SpiritVeinPaths);
            GameObject legendGraphic = new GameObject("MapLegendSymbols", typeof(RectTransform),
                typeof(WorldMapLegendGraphic), typeof(LayoutElement));
            legendGraphic.transform.SetParent(viewPage, false);
            legendGraphic.GetComponent<LayoutElement>().preferredHeight = 92f;
            legendText = AddObservabilityText(viewPage, string.Empty, 15, 210);

            Transform statisticsPage = CreateObservabilityScrollPage("StatisticsPage");
            statisticsText = AddObservabilityText(statisticsPage, "尚未生成地图。", 15, 1200);
            Transform parametersPage = CreateObservabilityScrollPage("ParametersPage");
            parametersText = AddObservabilityText(parametersPage,
                "参数随地图快照保存；正式游戏中只读。", 15, 620);

            ShowObservabilityPage(0);
            SetObservabilityVisible(false);
        }

        private Transform CreateObservabilityScrollPage(string name)
        {
            GameObject page = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(ScrollRect));
            page.transform.SetParent(observabilityRoot.transform, false);
            LayoutElement pageLayout = page.GetComponent<LayoutElement>();
            pageLayout.flexibleHeight = 1f;
            pageLayout.minHeight = 180f;

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(page.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.12f);

            GameObject content = new GameObject("Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            VerticalLayoutGroup contentLayout = content.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 5f;
            contentLayout.padding = new RectOffset(5, 5, 5, 5);
            contentLayout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = page.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            observabilityPages.Add(page);
            return content.transform;
        }

        private void AddViewButton(Transform parent, string label, WorldMapViewMode mode) =>
            AddObservabilityButton(parent, label, () => SetViewMode(mode));

        private void ShowObservabilityPage(int index)
        {
            for (int i = 0; i < observabilityPages.Count; i++)
                observabilityPages[i].SetActive(i == index);
        }

        private void SetObservabilityVisible(bool visible)
        {
            if (observabilityRoot != null) observabilityRoot.SetActive(visible);
        }

        private void RefreshObservability()
        {
            RefreshLegend();
            RefreshStatistics();
            RefreshParameters();
        }

        private void RefreshLegend()
        {
            if (legendText == null) return;
            switch (viewMode)
            {
                case WorldMapViewMode.Landform:
                    legendText.text = "离散地貌色；显示海岸和地貌边界、河流与地貌图标。";
                    break;
                case WorldMapViewMode.Height:
                    legendText.text = "固定 0–1：黑色（低）→白色（高）。";
                    break;
                case WorldMapViewMode.Temperature:
                    legendText.text = "固定 0–1：蓝→青→黄→红。";
                    break;
                case WorldMapViewMode.Moisture:
                    legendText.text = "固定 0–1：褐→绿→蓝。";
                    break;
                case WorldMapViewMode.Biome:
                    legendText.text = "离散群系色；显示海岸边界、河流与地貌图标。";
                    break;
                case WorldMapViewMode.AuraConcentration:
                    legendText.text = "固定 0–1：暗蓝→紫；标记降低透明度。";
                    break;
                case WorldMapViewMode.DominantElement:
                    legendText.text = "金米白、木绿、水蓝、火红、土黄；明暗表示总灵气。";
                    break;
                default:
                    legendText.text = "中性背景；路径颜色表示五行，大型灵脉更宽；仅保留洞府标记。";
                    break;
            }
        }

        private void RefreshStatistics()
        {
            if (statisticsText == null) return;
            if (map?.cells == null || map.cells.Length == 0)
            {
                statisticsText.text = "尚未生成地图。";
                return;
            }
            WorldMapStatistics statistics = WorldMapStatisticsCalculator.Calculate(map);
            StringBuilder text = new StringBuilder();
            text.AppendLine($"种子：{map.userSeed}  实际：{map.effectiveSeed}  版本：{map.generationVersion}");
            text.AppendLine($"尺寸：{map.width}×{map.height}  格数：{map.cells.Length}");
            text.AppendLine("\n地貌");
            foreach (LandformStatistic item in statistics.landforms)
                text.AppendLine($"{WorldMapCellDetailsFormatter.LandformLabel(item.type)}  {item.count}  {item.percentage:0.00}%");
            text.AppendLine("\n群系");
            foreach (BiomeStatistic item in statistics.biomes)
                text.AppendLine($"{WorldMapCellDetailsFormatter.BiomeLabel(item.type)}  {item.count}  {item.percentage:0.00}%");
            text.AppendLine($"\n高度 最小 {statistics.height.min:0.000} 中位 {statistics.height.median:0.000} 最大 {statistics.height.max:0.000}");
            AppendHistogram(text, "湿度分布", statistics.moisture);
            AppendHistogram(text, "灵气分布", statistics.aura);
            text.AppendLine($"灵气到达上限：{statistics.auraAtCapCount}（{statistics.auraAtCapPercentage:0.00}%）");
            text.AppendLine($"灵脉路径引用：{statistics.spiritVeinPathReferenceCount}");
            text.AppendLine($"路径去重格：{statistics.spiritVeinDistinctPathCellCount}");
            text.AppendLine($"路径重复率：{statistics.spiritVeinPathDuplicateRate * 100f:0.00}%");
            text.AppendLine($"影响区去重格：{statistics.spiritVeinInfluenceCellCount}（{statistics.spiritVeinInfluencePercentage:0.00}%）");
            statisticsText.text = text.ToString();
        }

        private static void AppendHistogram(StringBuilder text, string title, IEnumerable<HistogramBin> bins)
        {
            text.AppendLine("\n" + title);
            int index = 0;
            foreach (HistogramBin bin in bins)
            {
                string upperBracket = index == 9 ? "]" : ")";
                text.AppendLine($"[{bin.lowerInclusive:0.0},{bin.upperExclusive:0.0}{upperBracket} {bin.count}  {bin.percentage:0.00}%");
                index++;
            }
        }

        private void RefreshParameters()
        {
            if (parametersText == null) return;
            MapGenerationSettings settings = map?.generationSettings;
            if (settings == null)
            {
                parametersText.text = "地图没有有效的参数快照。";
                return;
            }
            parametersText.text =
                $"正式游戏参数只读\n\n种子 {settings.seed}\n尺寸 {settings.width}×{settings.height}\n生成版本 {settings.generationVersion}\n\n" +
                $"地貌\n深水 {settings.terrain.deepWaterThreshold:0.###}\n海平面 {settings.terrain.seaLevel:0.###}\n" +
                $"平原上限 {settings.terrain.plainUpperThreshold:0.###}\n丘陵上限 {settings.terrain.hillUpperThreshold:0.###}\n\n" +
                $"气候\n纬度降温 {settings.climate.latitudeCoolingStrength:0.###}\n温度噪声 {settings.climate.temperatureNoiseStrength:0.###}\n" +
                $"海拔降温 {settings.climate.elevationCoolingStrength:0.###}\n湿度噪声 {settings.climate.moistureNoiseStrength:0.###}\n" +
                $"距水增湿 {settings.climate.waterProximityMoistureStrength:0.###}\n河流增湿 {settings.climate.riverMoistureBoost:0.###}\n\n" +
                $"河流\n最小汇水量 {settings.rivers.minimumAccumulatedFlow:0.###}\n最低源头高度 {settings.rivers.minimumSourceHeight:0.###}\n" +
                $"最短支流 {settings.rivers.minimumBranchLength}\n\n" +
                $"灵脉\n大型数量 {Range(settings.spiritVeins.largeCount)}\n中型数量 {Range(settings.spiritVeins.mediumCount)}\n" +
                $"大型长度 {Range(settings.spiritVeins.largeLength)}\n中型长度 {Range(settings.spiritVeins.mediumLength)}\n" +
                $"大型半径 {Range(settings.spiritVeins.largeRadius)}\n中型半径 {Range(settings.spiritVeins.mediumRadius)}";
        }

        private static string Range(InclusiveIntRange range) =>
            range == null ? "无" : $"{range.min}–{range.max}";

        private static TMP_Text AddObservabilityText(Transform parent, string value, float size, float height)
        {
            GameObject obj = new GameObject("Text", typeof(RectTransform),
                typeof(TextMeshProUGUI), typeof(LayoutElement));
            obj.transform.SetParent(parent, false);
            TMP_Text text = obj.GetComponent<TMP_Text>();
            text.text = value;
            text.fontSize = size;
            text.color = Color.white;
            text.enableWordWrapping = true;
            obj.GetComponent<LayoutElement>().preferredHeight = height;
            return text;
        }

        private static Button AddObservabilityButton(Transform parent, string label,
            UnityEngine.Events.UnityAction action)
        {
            GameObject obj = new GameObject(label, typeof(RectTransform), typeof(Image),
                typeof(Button), typeof(LayoutElement));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<Image>().color = new Color(0.28f, 0.21f, 0.12f);
            obj.GetComponent<LayoutElement>().preferredHeight = 38f;
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(action);
            TMP_Text text = AddObservabilityText(obj.transform, label, 17, 38f);
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }
    }
}
