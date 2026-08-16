using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 3D 世界地图 HUD：详情文本、选址确认、地图行动、宗门简报入口与调试视图。
    /// 只组装展示数据并转发按钮回调，不直接修改任何玩法状态。
    /// </summary>
    public sealed class WorldMapHudController : MonoBehaviour
    {
        public Action OnConfirmSite;
        public Action OnExplore;
        public Action OnSiteAction;
        public Action OnOpenSectBrief;
        internal Action<WorldMapClimateDebugView> OnDebugViewSelected;

        private Canvas hudCanvas;
        private RectTransform hudControls;
        private GameObject selectionBlocker;
        private TMP_Text title;
        private TMP_Text details;
        private LayoutElement detailsTextLayout;
        private GameObject foundingTransition;
        private Button confirmButton;
        private Button exploreButton;
        private Button siteActionButton;
        private Button sectBriefButton;
        private Button debugToggle;
        private GameObject debugPanel;
        private TMP_Text legendText;
        private readonly List<Button> debugViewButtons = new List<Button>();

        public bool DebugViewEnabled => debugPanel != null && debugPanel.activeSelf;
        internal WorldMapClimateDebugView CurrentDebugView { get; private set; } =
            WorldMapClimateDebugView.Normal;
        internal Canvas HudCanvas => hudCanvas;
        internal string HudCanvasName => hudCanvas != null ? hudCanvas.gameObject.name : "null";

        private void Awake()
        {
            CreateHud();
            // WorldMap HUD 只在 GameFlowState.WorldMap 显示；
            // MainMenu / CharacterSetup / SectPlacement 一律隐藏。
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            Debug.Log($"[GameFlowDiag] WorldMapHudController.SetVisible({visible}) " +
                      $"hudGameObject={gameObject.name} canvas={HudCanvasName} " +
                      $"canvasActiveBefore={(hudCanvas != null && hudCanvas.gameObject.activeSelf)}");
            if (hudCanvas != null) hudCanvas.gameObject.SetActive(visible);
            Debug.Log($"[GameFlowDiag] WorldMapHudController.SetVisible({visible}) " +
                      $"canvasActiveAfter={(hudCanvas != null && hudCanvas.gameObject.activeSelf)}");
        }

        private void CreateHud()
        {
            hudCanvas = RuntimeUIFactory.Canvas(gameObject, 100);
            selectionBlocker = new GameObject("WorldSelectionBlocker", typeof(RectTransform), typeof(Image));
            selectionBlocker.transform.SetParent(hudCanvas.transform, false);
            RectTransform blockerRect = selectionBlocker.GetComponent<RectTransform>();
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.offsetMin = blockerRect.offsetMax = Vector2.zero;
            selectionBlocker.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
            selectionBlocker.SetActive(false);

            hudControls = RuntimeUIFactory.Panel(hudCanvas.transform, "MapControls",
                new Vector2(0.70f, 0f), new Vector2(0.99f, 1f));
            hudControls.offsetMin = new Vector2(0f, 12f);
            hudControls.offsetMax = new Vector2(0f, -64f);
            VerticalLayoutGroup controlsLayout = hudControls.GetComponent<VerticalLayoutGroup>();
            controlsLayout.padding = new RectOffset(12, 12, 8, 8);
            controlsLayout.spacing = 4f;

            title = RuntimeUIFactory.Text(hudControls, "世界地图", 25, 38);
            RectTransform detailsContent = RuntimeUIFactory.ScrollContent(hudControls, "MapDetailsScroll");
            RectTransform detailsScrollRoot = detailsContent.parent.parent as RectTransform;
            LayoutElement detailsAreaLayout = detailsScrollRoot.GetComponent<LayoutElement>();
            detailsAreaLayout.minHeight = 32f;
            detailsAreaLayout.preferredHeight = 190f;
            detailsAreaLayout.flexibleHeight = 1f;
            details = RuntimeUIFactory.Text(detailsContent, "点击地图格查看详情。", 16, 40);
            details.alignment = TextAlignmentOptions.TopLeft;
            detailsTextLayout = details.GetComponent<LayoutElement>();
            detailsTextLayout.minHeight = 40f;
            detailsTextLayout.preferredHeight = 40f;

            confirmButton = RuntimeUIFactory.Button(hudControls, "建立宗门", 42);
            confirmButton.onClick.AddListener(() => OnConfirmSite?.Invoke());
            confirmButton.gameObject.SetActive(false);
            exploreButton = RuntimeUIFactory.Button(hudControls, "派遣探索", 38);
            exploreButton.onClick.AddListener(() => OnExplore?.Invoke());
            exploreButton.gameObject.SetActive(false);
            siteActionButton = RuntimeUIFactory.Button(hudControls, "调查", 38);
            siteActionButton.onClick.AddListener(() => OnSiteAction?.Invoke());
            siteActionButton.gameObject.SetActive(false);

            sectBriefButton = RuntimeUIFactory.Button(hudCanvas.transform, "宗门简报", 38);
            RectTransform briefRect = sectBriefButton.GetComponent<RectTransform>();
            briefRect.anchorMin = briefRect.anchorMax = new Vector2(0f, 0f);
            briefRect.pivot = new Vector2(0f, 0f);
            briefRect.anchoredPosition = new Vector2(12f, 12f);
            briefRect.sizeDelta = new Vector2(130f, 38f);
            sectBriefButton.onClick.AddListener(() => OnOpenSectBrief?.Invoke());
            sectBriefButton.gameObject.SetActive(false);

            CreateDebugHud();
            CreateFoundingTransitionOverlay();
        }

        private void CreateFoundingTransitionOverlay()
        {
            foundingTransition = new GameObject("FoundingTransitionOverlay",
                typeof(RectTransform), typeof(Image));
            foundingTransition.transform.SetParent(hudCanvas.transform, false);
            RectTransform rect = foundingTransition.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            foundingTransition.GetComponent<Image>().color = new Color(0.03f, 0.03f, 0.02f, 0.94f);
            RuntimeUIFactory.Text(foundingTransition.transform,
                "三名少年来到此处。\n\n他们发现一处废弃洞府。\n\n整理洞府，布下聚灵阵。\n\n宗门初立。",
                26, 260).alignment = TextAlignmentOptions.Center;
            foundingTransition.SetActive(false);
        }

        public void ShowFoundingTransition(bool visible)
        {
            if (foundingTransition != null) foundingTransition.SetActive(visible);
        }

        private void CreateDebugHud()
        {
            debugToggle = RuntimeUIFactory.Button(hudCanvas.transform, "地图调试", 38);
            RectTransform toggleRect = debugToggle.GetComponent<RectTransform>();
            toggleRect.anchorMin = toggleRect.anchorMax = new Vector2(0f, 0f);
            toggleRect.pivot = new Vector2(0f, 0f);
            toggleRect.anchoredPosition = new Vector2(152f, 12f);
            toggleRect.sizeDelta = new Vector2(130f, 38f);
            debugToggle.onClick.AddListener(() => SetDebugViewEnabled(!DebugViewEnabled));

            debugPanel = RuntimeUIFactory.Panel(hudCanvas.transform, "WorldMapDebugViews",
                new Vector2(0f, 0f), new Vector2(0f, 1f)).gameObject;
            RectTransform panelRect = debugPanel.GetComponent<RectTransform>();
            panelRect.pivot = new Vector2(0f, 0.5f);
            panelRect.anchoredPosition = new Vector2(12f, 0f);
            panelRect.sizeDelta = new Vector2(360f, -24f);
            panelRect.offsetMin = new Vector2(panelRect.offsetMin.x, 58f);
            panelRect.offsetMax = new Vector2(panelRect.offsetMax.x, -64f);
            RuntimeUIFactory.Text(debugPanel.transform, "地图视图", 24, 36);
            RectTransform viewContent = RuntimeUIFactory.ScrollContent(debugPanel.transform, "MapDebugViewsScroll");
            RectTransform viewScroll = viewContent.parent.parent as RectTransform;
            LayoutElement viewLayout = viewScroll.GetComponent<LayoutElement>();
            viewLayout.flexibleHeight = 1f;
            viewLayout.minHeight = 120f;
            foreach (WorldMapClimateDebugView view in Enum.GetValues(typeof(WorldMapClimateDebugView)))
            {
                Button button = RuntimeUIFactory.Button(viewContent, DebugViewLabel(view), 34);
                button.onClick.AddListener(() => SelectDebugView(view));
                debugViewButtons.Add(button);
            }
            legendText = RuntimeUIFactory.Text(debugPanel.transform, string.Empty, 15, 72);
            RefreshDebugPanelVisuals();
            debugPanel.SetActive(false);
        }

        public bool IsPointerOverHudControl()
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
                        !result.gameObject.transform.IsChildOf(hudCanvas.transform)) continue;
                    if (result.gameObject == selectionBlocker) continue;
                    return true;
                }
            }
            return hudControls != null &&
                   RectTransformUtility.RectangleContainsScreenPoint(hudControls, Input.mousePosition);
        }

        internal void Refresh(WorldMap map, WorldMapProgressState progress, int selectedCellIndex,
            bool siteSelectionMode, bool hasSectBase, bool revealAll, PlayerData sect)
        {
            bool selecting = siteSelectionMode;
            if (hudCanvas != null) hudCanvas.sortingOrder = selecting ? 1000 : 100;
            if (title != null) title.text = selecting ? "宗门选址" : "世界地图";
            if (selectionBlocker != null) selectionBlocker.SetActive(selecting);
            if (hudControls != null) hudControls.gameObject.SetActive(selecting || hasSectBase);
            if (confirmButton != null)
            {
                confirmButton.gameObject.SetActive(selecting);
                confirmButton.interactable = selecting && map?.cells != null &&
                                             selectedCellIndex >= 0 && selectedCellIndex < map.cells.Length &&
                                             map.cells[selectedCellIndex].isBuildable;
            }

            bool established = !selecting && hasSectBase && selectedCellIndex >= 0 &&
                               selectedCellIndex < (map?.cells?.Length ?? 0);
            MapSiteData site = established
                ? progress?.mapSites?.FirstOrDefault(item => item != null &&
                    item.cellIndex == selectedCellIndex && item.siteType != MapSiteType.SectBase)
                : null;
            MapActionType siteAction = WorldMapContentRules.ActionForSite(site);
            bool showExplore = established && (site == null ||
                                               site.revealState != MapContentRevealState.Discovered);
            bool showSiteAction = established && site != null &&
                                  site.revealState == MapContentRevealState.Discovered &&
                                  siteAction != MapActionType.None;
            if (exploreButton != null)
            {
                exploreButton.gameObject.SetActive(showExplore);
                if (showExplore)
                    exploreButton.interactable = WorldMapContentRules.CanStartAction(map, progress,
                        new MapMissionContext { actionType = MapActionType.Explore, targetCellIndex = selectedCellIndex },
                        out _);
            }
            if (siteActionButton != null)
            {
                siteActionButton.gameObject.SetActive(showSiteAction);
                if (showSiteAction)
                {
                    SetButtonLabel(siteActionButton, ActionLabel(siteAction));
                    siteActionButton.interactable = WorldMapContentRules.CanStartAction(map, progress,
                        new MapMissionContext
                        {
                            actionType = siteAction,
                            targetCellIndex = selectedCellIndex,
                            targetSiteId = site?.siteId
                        }, out _);
                }
            }

            if (sectBriefButton != null)
            {
                MapSiteData sectBase = WorldMapProgressRules.GetSectBase(progress);
                sectBriefButton.gameObject.SetActive(!selecting && sectBase != null &&
                                                    sectBase.cellIndex == selectedCellIndex);
            }
            if (debugToggle != null) debugToggle.gameObject.SetActive(!selecting && hasSectBase);
            if (!debugToggle.gameObject.activeSelf) SetDebugViewEnabled(false);

            RefreshDetails(map, progress, selectedCellIndex, selecting, revealAll, sect);
        }

        private void RefreshDetails(WorldMap map, WorldMapProgressState progress,
            int selectedCellIndex, bool selecting, bool revealAll, PlayerData sect)
        {
            if (details == null) return;
            string text;
            if (selecting)
            {
                text = selecting && selectedCellIndex < 0
                    ? "请观察山川地势，点击六角格选择宗门落点。"
                    : PlacementLocationText(map, selectedCellIndex);
            }
            else if (selectedCellIndex < 0)
            {
                text = "点击地图格查看详情。";
            }
            else
            {
                HashSet<int> known = WorldMapInfluenceRules.CollectKnownCellIndices(
                    map, progress, revealAll);
                IEnumerable<WorldMapPresentationMarker> markers =
                    WorldMapPresentationMarkerFactory.CreateMapPresentationMarkers(map, progress, sect)
                        .Where(marker => marker != null && known.Contains(marker.cellIndex));
                text = WorldMapCellDetailsFormatter.Format(map, selectedCellIndex,
                    WorldMapViewMode.Landform, false, markers, progress, sect);
            }
            details.text = text;
            float availableWidth = details.rectTransform.rect.width;
            if (availableWidth <= 1f)
                availableWidth = Mathf.Max(120f, (hudControls == null ? 240f : hudControls.rect.width) - 64f);
            detailsTextLayout.preferredHeight = Mathf.Max(40f,
                details.GetPreferredValues(details.text, availableWidth, 0f).y + 8f);
            RectTransform detailsScrollRoot = details.transform.parent?.parent?.parent as RectTransform;
            if (detailsScrollRoot != null) LayoutRebuilder.MarkLayoutForRebuild(detailsScrollRoot);
        }

        public void SetDebugViewEnabled(bool enabled)
        {
            if (debugPanel != null) debugPanel.SetActive(enabled);
            if (!enabled && CurrentDebugView != WorldMapClimateDebugView.Normal)
                SelectDebugView(WorldMapClimateDebugView.Normal);
        }

        private void SelectDebugView(WorldMapClimateDebugView view)
        {
            CurrentDebugView = view;
            RefreshDebugPanelVisuals();
            OnDebugViewSelected?.Invoke(view);
        }

        private void RefreshDebugPanelVisuals()
        {
            for (int index = 0; index < debugViewButtons.Count; index++)
            {
                WorldMapClimateDebugView view = (WorldMapClimateDebugView)index;
                Button button = debugViewButtons[index];
                if (button != null)
                    button.GetComponent<Image>().color = view == CurrentDebugView
                        ? new Color(0.55f, 0.36f, 0.13f, 1f)
                        : new Color(0.20f, 0.17f, 0.13f, 1f);
            }
            if (legendText != null) legendText.text = DebugViewLegend(CurrentDebugView);
        }

        private static string PlacementLocationText(WorldMap map, int cellIndex)
        {
            if (map?.cells == null || cellIndex < 0 || cellIndex >= map.cells.Length)
                return "请观察山川地势，点击六角格选择宗门落点。";
            WorldCell cell = map.cells[cellIndex];
            MapRegionData region = map.regions?.FirstOrDefault(item => item != null &&
                string.Equals(item.regionId, cell.regionId, StringComparison.Ordinal));
            string placeName = region != null && !string.IsNullOrWhiteSpace(region.regionName)
                ? region.regionName
                : "无名之地";
            if (cell.internalPositionTag != MapInternalPositionTag.None)
                placeName += "·" + WorldMapRegionRules.PositionLabel(cell.internalPositionTag);
            string terrain = cell.landform == LandformType.Mountain && cell.isBuildable
                ? "山地台地"
                : WorldMapCellDetailsFormatter.LandformLabel(cell.landform);
            string environment = PlacementEnvironmentText(cell);
            return $"地点：{placeName}\n地貌：{terrain}/{WorldMapCellDetailsFormatter.BiomeLabel(cell.biome)}\n" +
                   $"环境：{environment}\n是否建立宗门：\n[确认]";
        }

        private static string PlacementEnvironmentText(WorldCell cell)
        {
            if (cell == null) return "荒无人烟";
            switch (cell.biome)
            {
                case BiomeType.TemperateForest: return "林木幽深";
                case BiomeType.Rainforest: return "雨林茂密";
                case BiomeType.Wetland: return "水泽弥漫";
                case BiomeType.Desert: return "黄沙千里";
                case BiomeType.Tundra: return "寒风凛冽";
                case BiomeType.Snowfield: return "冰雪覆盖";
                case BiomeType.Alpine: return "高山苍茫";
                case BiomeType.Coast: return "水岸相接";
                default:
                    if (cell.landform == LandformType.DeepWater ||
                        cell.landform == LandformType.ShallowWater)
                        return "水域辽阔";
                    if (cell.landform == LandformType.Mountain) return "山势险峻";
                    if (cell.landform == LandformType.Hill) return "丘陵起伏";
                    return "地势开阔";
            }
        }

        private static void SetButtonLabel(Button button, string label)
        {
            TMP_Text text = button.GetComponentInChildren<TMP_Text>();
            if (text != null) text.text = label;
        }

        private static string ActionLabel(MapActionType action)
        {
            switch (action)
            {
                case MapActionType.InvestigateSpiritSpring: return "调查灵泉";
                case MapActionType.DevelopSpiritSpring: return "开发灵泉";
                case MapActionType.EstablishVillageRelation: return "建立村庄关系";
                case MapActionType.DevelopSpiritMine: return "开发灵矿";
                case MapActionType.BuildCaveResidenceOutpost: return "建立洞府据点";
                case MapActionType.ClearBeastLair: return "清理兽巢";
                case MapActionType.InvestigateRuin: return "调查遗迹";
                default: return "调查";
            }
        }

        private static string DebugViewLabel(WorldMapClimateDebugView view)
        {
            switch (view)
            {
                case WorldMapClimateDebugView.Normal: return "正常地图";
                case WorldMapClimateDebugView.Biome: return "生物群系";
                case WorldMapClimateDebugView.Temperature: return "温度";
                case WorldMapClimateDebugView.Moisture: return "湿度";
                case WorldMapClimateDebugView.Rainfall: return "地形降雨";
                case WorldMapClimateDebugView.FreshWaterDistance: return "淡水距离";
                case WorldMapClimateDebugView.DrainageFlow: return "排水汇流";
                case WorldMapClimateDebugView.Elevation: return "海拔";
                case WorldMapClimateDebugView.Aura: return "灵气浓度";
                case WorldMapClimateDebugView.DominantElement: return "五行主属性";
                case WorldMapClimateDebugView.SpiritVeinPaths: return "灵脉路径";
                default: return view.ToString();
            }
        }

        private static string DebugViewLegend(WorldMapClimateDebugView view)
        {
            switch (view)
            {
                case WorldMapClimateDebugView.Aura:
                    return "固定 0–1：暗蓝→紫；颜色越亮总灵气越高。";
                case WorldMapClimateDebugView.DominantElement:
                    return "金米白、木绿、水蓝、火红、土黄；明暗表示总灵气。";
                case WorldMapClimateDebugView.SpiritVeinPaths:
                    return "中性背景；路径颜色表示五行，大型灵脉更宽。";
                case WorldMapClimateDebugView.Temperature:
                    return "蓝→青→黄→红。";
                case WorldMapClimateDebugView.Moisture:
                    return "褐→绿→蓝。";
                case WorldMapClimateDebugView.Elevation:
                    return "黑色（低）→白色（高）。";
                default:
                    return "正常地表颜色。";
            }
        }

        private void OnDestroy()
        {
            if (hudCanvas != null)
            {
                if (Application.isPlaying) Destroy(hudCanvas.gameObject);
                else DestroyImmediate(hudCanvas.gameObject);
            }
            hudCanvas = null;
            details = null;
            detailsTextLayout = null;
            hudControls = null;
            selectionBlocker = null;
            debugPanel = null;
            legendText = null;
            debugViewButtons.Clear();
        }
    }
}
