using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Cultivation4X.WorldMap
{
    public enum WorldMapViewMode
    {
        Landform,
        Height,
        Temperature,
        Moisture,
        Biome,
        AuraConcentration,
        DominantElement,
        SpiritVeinPaths
    }

    public partial class WorldMapPresenter : MonoBehaviour
    {
        private const int ChunkSize = 16;
        private static readonly float Sqrt3 = Mathf.Sqrt(3f);
        private readonly List<GameObject> generatedObjects = new List<GameObject>();
        private WorldMap map;
        private Material material;
        private Camera mapCamera;
        private bool showGrid;
        private int selectedCellIndex = -1;
        private TMP_Text details;
        private RectTransform detailsScrollRoot;
        private LayoutElement detailsTextLayout;
        private Button auraButton;
        private Button confirmButton;
        private Button sectBriefButton;
        private Button exploreCellButton;
        private Button investigateSpringButton;
        private Button developSpringButton;
        private Canvas hudCanvas;
        private RectTransform hudControls;
        private GameObject selectionBlocker;
        private Vector3 dragOrigin;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // TerrainTest 场景使用 3D 表现，跳过 2D Presenter，保持场景隔离。
            if (MapTestBootstrap.IsTestScene) return;
            if (FindObjectOfType<WorldMapPresenter>() == null)
                new GameObject("WorldMapPresenter").AddComponent<WorldMapPresenter>();
        }

        private void Awake()
        {
            material = new Material(Shader.Find("Sprites/Default"));
            mapCamera = Camera.main;
            if (mapCamera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                mapCamera = cameraObject.AddComponent<Camera>();
            }
            mapCamera.orthographic = true;
            mapCamera.backgroundColor = new Color(0.035f, 0.045f, 0.055f);
            mapCamera.transform.position = new Vector3(0f, 0f, -10f);
            mapCamera.transform.rotation = Quaternion.identity;
            CreateLayerRoots();
            CreateHud();
        }

        private IEnumerator Start()
        {
            while (SaveManager.Instance == null || !SaveManager.Instance.IsInitializationComplete) yield return null;
            if (PlayerManager.Instance != null) PlayerManager.Instance.OnFoundingChanged += RefreshSelectionMode;
            WorldMapSession.ProgressChanged += RefreshMapProgress;
            SetMap(WorldMapSession.Current);
            RefreshSelectionMode();
        }

        private void OnDestroy()
        {
            if (PlayerManager.Instance != null) PlayerManager.Instance.OnFoundingChanged -= RefreshSelectionMode;
            WorldMapSession.ProgressChanged -= RefreshMapProgress;
            ClearGenerated();
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
                material = null;
            }
            GameObject ownedHud = hudCanvas == null ? null : hudCanvas.gameObject;
            hudCanvas = null;
            regionLabelRoot = null;
            nearDetailLabelRoot = null;
            regionLabelPool.Clear();
            nearDetailLabelPool.Clear();
            if (ownedHud != null)
            {
                if (Application.isPlaying) Destroy(ownedHud);
                else DestroyImmediate(ownedHud);
            }
        }

        private void Update()
        {
            if (map == null || mapCamera == null) return;
            bool panelOpen = UIManager.Instance != null && UIManager.Instance.HasOpenPanels;
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && !panelOpen)
            {
                mapCamera.orthographicSize = Mathf.Clamp(mapCamera.orthographicSize * (wheel > 0 ? 0.88f : 1.12f), 5f, 140f);
                RefreshPresentationForZoom();
            }
            if (Input.GetMouseButtonDown(1) && !panelOpen)
                dragOrigin = mapCamera.ScreenToWorldPoint(Input.mousePosition);
            if (Input.GetMouseButton(1) && !panelOpen)
            {
                Vector3 current = mapCamera.ScreenToWorldPoint(Input.mousePosition);
                Vector3 delta = dragOrigin - current;
                mapCamera.transform.position += new Vector3(delta.x, delta.y, 0f);
                dragOrigin = mapCamera.ScreenToWorldPoint(Input.mousePosition);
                RefreshRegionLabels();
            }
            if (Input.GetMouseButtonUp(1) && !panelOpen) RefreshRegionLabels();
            if (Input.GetMouseButtonDown(0))
            {
                bool selecting = PlayerManager.Instance?.playerData?.founding?.stage == FoundingStage.WorldSelection;
                bool overControls = IsPointerOverWorldMapControls();
                bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if ((selecting && !overControls) || (!selecting && !overUi))
                    SelectAtWorld(mapCamera.ScreenToWorldPoint(Input.mousePosition));
            }
        }

        private bool IsPointerOverWorldMapControls()
        {
            if (EventSystem.current != null && hudCanvas != null)
            {
                PointerEventData pointer = new PointerEventData(EventSystem.current)
                {
                    position = Input.mousePosition
                };
                List<RaycastResult> results = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointer, results);
                foreach (RaycastResult result in results)
                {
                    if (result.gameObject == null ||
                        !result.gameObject.transform.IsChildOf(hudCanvas.transform))
                        continue;

                    // The transparent blocker exists only to stop the game's other canvases.
                    // It intentionally still permits selecting a map cell, but it must not
                    // hide a real HUD control that appears later in the raycast results.
                    if (result.gameObject == selectionBlocker) continue;
                    return true;
                }
            }

            return hudControls != null &&
                   RectTransformUtility.RectangleContainsScreenPoint(hudControls, Input.mousePosition);
        }

        public void SetMap(WorldMap worldMap)
        {
            map = worldMap;
            selectedCellIndex = -1;
            RefreshPresentationMarkers();
            Rebuild();
            FitCamera();
            RefreshObservability();
            RefreshDetails();
        }

        private void SelectAtWorld(Vector3 world)
        {
            int rowGuess = Mathf.RoundToInt(world.y / 1.5f);
            int colGuess = Mathf.RoundToInt(world.x / Sqrt3 - ((rowGuess & 1) == 1 ? 0.5f : 0f));
            int bestIndex = -1;
            float bestDistance = float.MaxValue;
            for (int row = rowGuess - 1; row <= rowGuess + 1; row++)
            for (int col = colGuess - 1; col <= colGuess + 1; col++)
            {
                int index = map.GetIndex(new HexCoord(col, row));
                if (index < 0) continue;
                float distance = ((Vector2)world - Center(map.cells[index].coord)).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; bestIndex = index; }
            }
            if (bestIndex < 0 || bestDistance > 1.2f) return;
            selectedCellIndex = bestIndex;
            RebuildSelection();
            RefreshDetails();
        }

        private void Rebuild()
        {
            ClearGenerated();
            RefreshPresentationCaches();
            if (map == null) return;
            lastZoomLevel = WorldMapRegionPresentationPolicy.GetZoomLevel(ProjectedHexDiameter());
            for (int row = 0; row < map.height; row += ChunkSize)
            for (int col = 0; col < map.width; col += ChunkSize)
                BuildChunk(col, row);
            BuildBoundaries();
            if (viewMode == WorldMapViewMode.Landform || viewMode == WorldMapViewMode.Biome) BuildRivers();
            if (viewMode == WorldMapViewMode.SpiritVeinPaths) BuildVeins();
            BuildPresentationLayers();
            BuildInfluenceOverlay();
            RebuildSelection();
            RefreshRegionLabels();
        }

        private void BuildChunk(int startCol, int startRow)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Color> colors = new List<Color>();
            float radius = lastZoomLevel == WorldMapZoomLevel.Far ? 0.92f :
                lastZoomLevel == WorldMapZoomLevel.Mid ? 0.88f : 0.84f;
            if (showGrid) radius -= 0.03f;
            for (int row = startRow; row < Mathf.Min(startRow + ChunkSize, map.height); row++)
            for (int col = startCol; col < Mathf.Min(startCol + ChunkSize, map.width); col++)
            {
                WorldCell cell = map.GetCell(new HexCoord(col, row));
                Vector2 center = Center(cell.coord);
                int start = vertices.Count;
                Color color = CellColor(cell);
                vertices.Add(center); colors.Add(color);
                for (int corner = 0; corner < 6; corner++)
                {
                    float angle = Mathf.Deg2Rad * (corner * 60f - 30f);
                    vertices.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                    colors.Add(color);
                }
                for (int corner = 0; corner < 6; corner++)
                {
                    triangles.Add(start); triangles.Add(start + 1 + corner);
                    triangles.Add(start + 1 + (corner + 1) % 6);
                }
            }
            Mesh mesh = new Mesh { name = $"WorldChunk_{startCol}_{startRow}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetColors(colors); mesh.RecalculateBounds();
            GameObject obj = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(Layer("Terrain"), false);
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            obj.GetComponent<MeshRenderer>().sharedMaterial = material;
            generatedObjects.Add(obj);
        }

        private void BuildRivers()
        {
            WorldMapGeometryBuffer buffer = WorldMapOverlayGeometry.BuildRiverGeometry(
                map, Center, CanShowGameplayCell);
            AddMeshObject("Rivers", buffer, Layer("Rivers"), 3);
        }

        private void BuildVeins()
        {
            foreach (SpiritVein vein in map.spiritVeins)
            {
                LineRenderer line = CreateLine("Vein_" + vein.id, ElementColor(vein.primaryElement),
                    vein.size == SpiritVeinSize.Large ? 0.16f : 0.09f, 4, Layer("SpiritVeins"));
                line.positionCount = vein.pathCellIndices.Count;
                for (int i = 0; i < vein.pathCellIndices.Count; i++)
                    line.SetPosition(i, Center(map.cells[vein.pathCellIndices[i]].coord));
            }
        }

        private void RebuildSelection()
        {
            GameObject old = generatedObjects.FirstOrDefault(obj => obj != null && obj.name == "SelectedCell");
            if (old != null) { generatedObjects.Remove(old); Destroy(old); }
            if (map == null || selectedCellIndex < 0 || selectedCellIndex >= map.cells.Length) return;
            LineRenderer line = CreateLine("SelectedCell", Color.white, 0.13f, 9, Layer("Selection"));
            line.loop = true; line.positionCount = 6;
            Vector2 center = Center(map.cells[selectedCellIndex].coord);
            for (int corner = 0; corner < 6; corner++)
            {
                float angle = Mathf.Deg2Rad * (corner * 60f - 30f);
                line.SetPosition(corner, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 1.02f);
            }
        }

        private LineRenderer CreateLine(string name, Color color, float width, int order, Transform parent = null)
        {
            GameObject obj = new GameObject(name, typeof(LineRenderer));
            obj.transform.SetParent(parent == null ? transform : parent, false);
            LineRenderer line = obj.GetComponent<LineRenderer>();
            line.useWorldSpace = false; line.sharedMaterial = material;
            line.startColor = line.endColor = color; line.startWidth = line.endWidth = width;
            line.sortingOrder = order; line.numCapVertices = 2;
            generatedObjects.Add(obj);
            return line;
        }

        private void CreateHud()
        {
            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("WorldMapEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            GameObject hud = new GameObject("WorldMapHUD");
            hudCanvas = RuntimeUIFactory.Canvas(hud, 100);
            CreateRegionLabelHud(hud.transform);
            selectionBlocker = new GameObject("WorldSelectionBlocker", typeof(RectTransform), typeof(Image));
            selectionBlocker.transform.SetParent(hud.transform, false);
            RectTransform blockerRect = selectionBlocker.GetComponent<RectTransform>();
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.offsetMin = blockerRect.offsetMax = Vector2.zero;
            selectionBlocker.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
            hudControls = RuntimeUIFactory.Panel(hud.transform, "MapControls",
                new Vector2(0.70f, 0f), new Vector2(0.99f, 1f));
            ConfigureHudControlsLayout(hudControls);
            RectTransform panel = hudControls;
            RuntimeUIFactory.Text(panel, "世界地图", 25, 38);
            auraButton = RuntimeUIFactory.Button(panel, "普通 / 灵气视图", 38);
            auraButton.onClick.AddListener(() =>
                SetViewMode(viewMode == WorldMapViewMode.AuraConcentration
                    ? WorldMapViewMode.Landform
                    : WorldMapViewMode.AuraConcentration));
            Button grid = RuntimeUIFactory.Button(panel, "切换六角格网", 38);
            grid.onClick.AddListener(() => { showGrid = !showGrid; Rebuild(); });
            Button fit = RuntimeUIFactory.Button(panel, "回到全图", 38);
            fit.onClick.AddListener(FitCamera);
            RectTransform detailsContent = RuntimeUIFactory.ScrollContent(panel, "MapDetailsScroll");
            detailsScrollRoot = detailsContent.parent.parent as RectTransform;
            LayoutElement detailsAreaLayout = detailsScrollRoot.GetComponent<LayoutElement>();
            detailsAreaLayout.minHeight = 32f;
            detailsAreaLayout.preferredHeight = 190f;
            detailsAreaLayout.flexibleHeight = 1f;
            details = RuntimeUIFactory.Text(detailsContent, "点击地图格查看详情。", 16, 40);
            details.alignment = TextAlignmentOptions.TopLeft;
            detailsTextLayout = details.GetComponent<LayoutElement>();
            detailsTextLayout.minHeight = 40f;
            detailsTextLayout.preferredHeight = 40f;
            confirmButton = RuntimeUIFactory.Button(panel, "建立宗门", 42);
            confirmButton.onClick.AddListener(ConfirmSite);
            confirmButton.gameObject.SetActive(false);
            exploreCellButton = RuntimeUIFactory.Button(panel, "派遣探索", 38);
            exploreCellButton.onClick.AddListener(() => OpenSelectedMapAction(MapActionType.Explore));
            investigateSpringButton = RuntimeUIFactory.Button(panel, "调查灵泉", 38);
            investigateSpringButton.onClick.AddListener(() => OpenSelectedMapAction(MapActionType.InvestigateSpiritSpring));
            developSpringButton = RuntimeUIFactory.Button(panel, "开发灵泉", 38);
            developSpringButton.onClick.AddListener(() => OpenSelectedMapAction(MapActionType.DevelopSpiritSpring));
            CreateObservabilityHud(hud.transform);
            // 宗门简报按钮固定在界面左下角，面板本身仍居中弹出。
            sectBriefButton = RuntimeUIFactory.Button(hud.transform, "宗门简报", 38);
            RectTransform briefRect = sectBriefButton.GetComponent<RectTransform>();
            briefRect.anchorMin = briefRect.anchorMax = new Vector2(0f, 0f);
            briefRect.pivot = new Vector2(0f, 0f);
            briefRect.anchoredPosition = new Vector2(12f, 12f);
            briefRect.sizeDelta = new Vector2(130f, 38f);
            sectBriefButton.onClick.AddListener(() => SectWorldInterface.Instance?.OpenSectBrief());
            sectBriefButton.gameObject.SetActive(false);
        }

        private static void ConfigureHudControlsLayout(RectTransform controls)
        {
            if (controls == null) return;
            controls.anchorMin = new Vector2(0.70f, 0f);
            controls.anchorMax = new Vector2(0.99f, 1f);
            controls.pivot = new Vector2(0.5f, 0.5f);
            controls.offsetMin = new Vector2(0f, 12f);
            controls.offsetMax = new Vector2(0f, -64f);
            VerticalLayoutGroup layout = controls.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.padding = new RectOffset(12, 12, 8, 8);
                layout.spacing = 4f;
            }
        }

        private void ConfirmSite()
        {
            if (PlayerManager.Instance == null || selectedCellIndex < 0) return;
            if (!PlayerManager.Instance.ConfirmWorldSite(selectedCellIndex, out string reason))
                Debug.LogWarning(reason);
        }

        private void RefreshSelectionMode()
        {
            bool selecting = PlayerManager.Instance?.playerData?.founding?.stage == FoundingStage.WorldSelection;
            bool hasSectBase = WorldMapProgressRules.GetSectBase(WorldMapSession.Progress) != null;
            RefreshPresentationMarkers();
            if (selecting && viewMode != WorldMapViewMode.Landform)
                viewMode = WorldMapViewMode.Landform;
            if (hudCanvas != null) hudCanvas.sortingOrder = selecting ? 1000 : 100;
            if (selectionBlocker != null) selectionBlocker.SetActive(selecting);
            if (hudControls != null) hudControls.gameObject.SetActive(selecting || hasSectBase);
            if (auraButton != null) auraButton.gameObject.SetActive(false);
            if (confirmButton != null) confirmButton.gameObject.SetActive(selecting);
            if (sectBriefButton != null) sectBriefButton.gameObject.SetActive(false);
            SetDebugToggleVisible(!selecting && hasSectBase);
            if (selecting) SetDebugViewEnabled(false);
            if (map != null) Rebuild();
            RefreshDetails();
        }

        private void RefreshDetails()
        {
            bool selecting = PlayerManager.Instance?.playerData?.founding?.stage == FoundingStage.WorldSelection;
            if (details != null)
            {
                details.text = selectedCellIndex < 0 && selecting
                    ? "请在地图上选择可建设格作为洞府。"
                    : WorldMapCellDetailsFormatter.Format(map, selectedCellIndex, viewMode, selecting,
                        VisibleMarkersForDetails(), WorldMapSession.Progress, PlayerManager.Instance?.playerData);
                details.text += DebugRegionSummary();
                float availableWidth = details.rectTransform.rect.width;
                if (availableWidth <= 1f)
                    availableWidth = Mathf.Max(120f, (hudControls == null ? 240f : hudControls.rect.width) - 64f);
                detailsTextLayout.preferredHeight = Mathf.Max(40f,
                    details.GetPreferredValues(details.text, availableWidth, 0f).y + 8f);
                if (detailsScrollRoot != null) LayoutRebuilder.MarkLayoutForRebuild(detailsScrollRoot);
            }
            RefreshRegionLabels();
            if (confirmButton != null)
                confirmButton.interactable = selecting && map?.cells != null &&
                                             selectedCellIndex >= 0 && selectedCellIndex < map.cells.Length &&
                                             map.cells[selectedCellIndex].isBuildable;
            RefreshMapActionButtons(selecting);
            if (sectBriefButton != null)
            {
                MapSiteData sectBase = WorldMapProgressRules.GetSectBase(WorldMapSession.Progress);
                sectBriefButton.gameObject.SetActive(!selecting && sectBase != null &&
                                                      sectBase.cellIndex == selectedCellIndex);
            }
        }

        private void RefreshMapProgress()
        {
            RefreshPresentationMarkers();
            Rebuild();
            RefreshDetails();
        }

        private void RefreshMapActionButtons(bool selecting)
        {
            bool established = !selecting && WorldMapProgressRules.GetSectBase(WorldMapSession.Progress) != null &&
                               selectedCellIndex >= 0;
            MapSiteData site = established ? WorldMapSession.Progress?.mapSites?.FirstOrDefault(item =>
                item != null && item.cellIndex == selectedCellIndex && item.siteType != MapSiteType.SectBase) : null;
            SetButtonLabel(investigateSpringButton, site == null ? "调查" : WorldMapContentRules.SiteTypeLabel(site.siteType));
            SetButtonLabel(developSpringButton, "开发灵泉");
            SetActionButton(exploreCellButton, established && site?.revealState != MapContentRevealState.Discovered,
                new MapMissionContext { actionType = MapActionType.Explore, targetCellIndex = selectedCellIndex });
            MapActionType action = WorldMapContentRules.ActionForSite(site);
            MapMissionContext actionContext = new MapMissionContext
            {
                actionType = action, targetCellIndex = selectedCellIndex, targetSiteId = site?.siteId
            };
            bool discovered = established && site?.revealState == MapContentRevealState.Discovered && action != MapActionType.None;
            bool isSpringDevelop = action == MapActionType.DevelopSpiritSpring;
            SetButtonLabel(isSpringDevelop ? developSpringButton : investigateSpringButton,
                action == MapActionType.None ? "调查" : ActionLabel(action));
            SetActionButton(investigateSpringButton, discovered && !isSpringDevelop, actionContext);
            SetActionButton(developSpringButton, discovered && isSpringDevelop, actionContext);
        }

        private static string ActionLabel(MapActionType action)
        {
            switch (action)
            {
                case MapActionType.InvestigateSpiritSpring: return "调查灵泉";
                case MapActionType.DevelopSpiritSpring: return "开发灵泉";
                case MapActionType.EstablishVillageRelation: return "建立村庄关系";
                case MapActionType.DevelopSpiritMine: return "开发灵矿";
                case MapActionType.BuildCaveResidenceOutpost: return "建立洞府据点规则";
                case MapActionType.ClearBeastLair: return "清理兽巢";
                case MapActionType.InvestigateRuin: return "调查遗迹";
                default: return "调查";
            }
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null) return;
            TMPro.TMP_Text text = button.GetComponentInChildren<TMPro.TMP_Text>();
            if (text != null) text.text = label;
        }

        private void SetActionButton(Button button, bool visible, MapMissionContext context)
        {
            if (button == null) return;
            button.gameObject.SetActive(visible);
            if (!visible) return;
            button.interactable = WorldMapContentRules.CanStartAction(map, WorldMapSession.Progress, context, out _);
        }

        private void OpenSelectedMapAction(MapActionType actionType)
        {
            if (selectedCellIndex < 0) return;
            MapSiteData site = WorldMapSession.Progress?.mapSites?.FirstOrDefault(item => item != null &&
                item.cellIndex == selectedCellIndex && item.siteType != MapSiteType.SectBase);
            if (actionType != MapActionType.Explore && site != null && site.siteType != MapSiteType.SpiritSpring)
                actionType = WorldMapContentRules.ActionForSite(site);
            MapMissionContext context = new MapMissionContext
            {
                actionType = actionType,
                targetCellIndex = selectedCellIndex,
                targetSiteId = actionType == MapActionType.Explore ? null : site?.siteId
            };
            MissionPanel panel = FindObjectOfType<MissionPanel>(true);
            if (panel == null) { Debug.LogWarning("找不到任务面板"); return; }
            if (!panel.OpenMapMission(context, out string reason)) Debug.LogWarning(reason);
        }

        private IEnumerable<WorldMapPresentationMarker> VisibleMarkersForDetails() =>
            presentationMarkers.Where(marker => marker != null && CanShowGameplayCell(marker.cellIndex));

        private bool CanShowGameplayCell(int cellIndex)
        {
            return map?.cells != null && cellIndex >= 0 && cellIndex < map.cells.Length &&
                   presentationKnownCellIndices.Contains(cellIndex);
        }

        private void FitCamera()
        {
            if (map == null || mapCamera == null) return;
            float width = (map.width + 0.5f) * Sqrt3;
            float height = map.height * 1.5f;
            mapCamera.transform.position = new Vector3(width * 0.5f, height * 0.5f, -10f);
            mapCamera.transform.rotation = Quaternion.identity;
            mapCamera.orthographicSize = Mathf.Max(height * 0.53f, width / Mathf.Max(1.2f, mapCamera.aspect) * 0.53f);
            RefreshPresentationForZoom();
        }

        private void ClearGenerated()
        {
            foreach (GameObject obj in generatedObjects)
            {
                if (obj == null) continue;
                MeshFilter filter = obj.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) DestroyOwnedObject(filter.sharedMesh);
                DestroyOwnedObject(obj);
            }
            generatedObjects.Clear();
        }

        private static void DestroyOwnedObject(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private static Vector2 Center(HexCoord coord) =>
            new Vector2(Sqrt3 * (coord.col + ((coord.row & 1) == 1 ? 0.5f : 0f)), 1.5f * coord.row);
        private static Color AuraColor(float value) =>
            Color.Lerp(new Color(0.06f, 0.09f, 0.16f), new Color(0.85f, 0.27f, 0.94f), Mathf.Clamp01(value));
        private static Color TerrainColor(WorldCell cell)
        {
            switch (cell.biome)
            {
                case BiomeType.Ocean: return cell.landform == LandformType.DeepWater ? new Color(0.05f, 0.19f, 0.34f) : new Color(0.09f, 0.34f, 0.52f);
                case BiomeType.Coast: return new Color(0.78f, 0.75f, 0.60f);
                case BiomeType.TemperateForest: return new Color(0.16f, 0.42f, 0.20f);
                case BiomeType.Rainforest: return new Color(0.08f, 0.32f, 0.18f);
                case BiomeType.Wetland: return new Color(0.22f, 0.38f, 0.31f);
                case BiomeType.Desert: return new Color(0.74f, 0.48f, 0.20f);
                case BiomeType.Tundra: return new Color(0.50f, 0.56f, 0.48f);
                case BiomeType.Snowfield: return new Color(0.84f, 0.89f, 0.91f);
                case BiomeType.Alpine: return new Color(0.38f, 0.39f, 0.40f);
                default: return cell.landform == LandformType.Hill ? new Color(0.38f, 0.48f, 0.24f) : new Color(0.42f, 0.60f, 0.29f);
            }
        }
        private static Color ElementColor(SpiritElement element)
        {
            switch (element)
            {
                case SpiritElement.Metal: return new Color(0.88f, 0.88f, 0.70f);
                case SpiritElement.Wood: return new Color(0.25f, 0.90f, 0.35f);
                case SpiritElement.Water: return new Color(0.20f, 0.65f, 1f);
                case SpiritElement.Fire: return new Color(1f, 0.25f, 0.12f);
                default: return new Color(0.85f, 0.65f, 0.25f);
            }
        }
    }
}
