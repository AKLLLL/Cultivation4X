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
    /// 3D 世界地图 HUD：WorldMapInfoPanel（环境/地点/行动分页）、选址确认、
    /// 地图行动与宗门简报入口。只组装展示数据并转发按钮回调，不修改玩法状态。
    /// </summary>
    public sealed class WorldMapHudController : MonoBehaviour
    {
        public Action OnConfirmSite;
        public event Action<WorldLocation, LocationAction> LocationActionRequested;
        public event Action<WorldLocation> LocationMissionsRequested;

        private Canvas hudCanvas;
        private RectTransform hudControls;
        private GameObject selectionBlocker;
        private TMP_Text title;
        private RectTransform infoTabs;
        private readonly List<Button> infoTabButtons = new List<Button>();
        private RectTransform infoContent;
        private readonly List<GameObject> infoDynamicItems = new List<GameObject>();
        private GameObject foundingTransition;
        private Button confirmButton;
        private int selectedInfoTab;
        private int lastSelectedCellIndex = int.MinValue;

        internal Canvas HudCanvas => hudCanvas;
        internal string HudCanvasName => hudCanvas != null ? hudCanvas.gameObject.name : "null";

        private void Awake()
        {
            CreateHud();
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            GameDebugConfig.LogWorldMap($"[GameFlowDiag] WorldMapHudController.SetVisible({visible}) " +
                      $"hudGameObject={gameObject.name} canvas={HudCanvasName} " +
                      $"canvasActiveBefore={(hudCanvas != null && hudCanvas.gameObject.activeSelf)}");
            if (hudCanvas != null) hudCanvas.gameObject.SetActive(visible);
            GameDebugConfig.LogWorldMap($"[GameFlowDiag] WorldMapHudController.SetVisible({visible}) " +
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
            infoTabs = RuntimeUIFactory.TabBar(hudControls, "WorldInfoTabs", 44);
            infoTabButtons.Add(RuntimeUIFactory.TabButton(infoTabs, "环境", true));
            infoTabButtons.Add(RuntimeUIFactory.TabButton(infoTabs, "地点", false));
            infoTabButtons.Add(RuntimeUIFactory.TabButton(infoTabs, "行动", false));
            for (int index = 0; index < infoTabButtons.Count; index++)
            {
                int captured = index;
                infoTabButtons[index].onClick.AddListener(() => SelectInfoTab(captured));
            }

            RectTransform detailsContent = RuntimeUIFactory.ScrollContent(hudControls, "MapInfoContent");
            infoContent = detailsContent;
            RectTransform detailsScrollRoot = detailsContent.parent.parent as RectTransform;
            LayoutElement detailsAreaLayout = detailsScrollRoot.GetComponent<LayoutElement>();
            detailsAreaLayout.minHeight = 32f;
            detailsAreaLayout.preferredHeight = 190f;
            detailsAreaLayout.flexibleHeight = 1f;

            confirmButton = RuntimeUIFactory.Button(hudControls, "建立宗门", 42);
            confirmButton.onClick.AddListener(() => OnConfirmSite?.Invoke());
            confirmButton.gameObject.SetActive(false);

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
            if (title != null) title.text = "世界地图";
            if (selectionBlocker != null) selectionBlocker.SetActive(selecting);
            if (hudControls != null) hudControls.gameObject.SetActive(selecting || hasSectBase);
            if (infoTabs != null) infoTabs.gameObject.SetActive(!selecting && selectedCellIndex >= 0);
            if (confirmButton != null)
            {
                confirmButton.gameObject.SetActive(selecting);
                confirmButton.interactable = selecting && map?.cells != null &&
                                             selectedCellIndex >= 0 && selectedCellIndex < map.cells.Length &&
                                             map.cells[selectedCellIndex].isBuildable;
            }

            RefreshInfoContent(map, progress, selectedCellIndex, selecting, revealAll, sect);
        }

        private void RefreshInfoContent(WorldMap map, WorldMapProgressState progress,
            int selectedCellIndex, bool selecting, bool revealAll, PlayerData sect)
        {
            ClearInfoDynamicItems();
            if (selecting)
            {
                AddInfoText(selectedCellIndex < 0
                    ? "请观察山川地势，点击六角格选择宗门落点。"
                    : PlacementLocationText(map, selectedCellIndex), 16);
                return;
            }
            if (selectedCellIndex < 0 || map?.cells == null ||
                selectedCellIndex >= map.cells.Length)
            {
                AddInfoText("点击地图格查看详情。", 16);
                return;
            }

            if (lastSelectedCellIndex != selectedCellIndex)
            {
                lastSelectedCellIndex = selectedCellIndex;
                selectedInfoTab = 0;
                RefreshInfoTabVisuals();
            }

            if (selectedInfoTab == 0)
                ShowEnvironmentTab(map, progress, selectedCellIndex, revealAll, sect);
            else if (selectedInfoTab == 1)
                ShowLocationTab(map, progress, selectedCellIndex, sect);
            else
                ShowActionTab(map, progress, selectedCellIndex);
        }

        private void ShowEnvironmentTab(WorldMap map, WorldMapProgressState progress,
            int selectedCellIndex, bool revealAll, PlayerData sect)
        {
            WorldCell cell = map.cells[selectedCellIndex];
            MapRegionData region = map.regions?.FirstOrDefault(item => item != null &&
                string.Equals(item.regionId, cell.regionId, StringComparison.Ordinal));
            CellInfluenceRuntimeState influence = WorldMapInfluenceRules.GetCellState(
                map, progress, selectedCellIndex);
            string regionName = region == null || string.IsNullOrWhiteSpace(region.regionName)
                ? "未划分区域"
                : region.regionName;
            string regionType = region == null
                ? "未知"
                : WorldMapRegionRules.RegionTypeLabel(region.regionType);
            string markers = string.Join("、",
                (WorldMapPresentationMarkerFactory.CreateMapPresentationMarkers(
                    map, progress, sect) ?? Enumerable.Empty<WorldMapPresentationMarker>())
                .Where(marker => marker != null && marker.cellIndex == selectedCellIndex)
                .Select(marker => marker.label));
            if (string.IsNullOrEmpty(markers)) markers = "无";
            bool known = influence.knowledge == KnowledgeState.Known;
            string control = influence.controllerSectId ?? "无";
            string veins = map.spiritVeins != null &&
                           map.spiritVeins.Any(vein => vein?.pathCellIndices?.Contains(selectedCellIndex) == true)
                ? "有"
                : "无";

            AddInfoRow("坐标",
                $"{cell.coord.col},{cell.coord.row} | {WorldMapCellDetailsFormatter.LandformLabel(cell.landform)}/" +
                $"{WorldMapCellDetailsFormatter.BiomeLabel(cell.biome)}");
            AddInfoRow("区域", $"{regionName} | 类型：{regionType}");
            AddInfoRow("位置", WorldMapRegionRules.PositionLabel(cell.internalPositionTag));
            AddInfoRow("气候", ClimateLabel(cell));
            AddInfoRow("灵气", $"{WorldMapCellDetailsFormatter.AuraBand(cell.totalAura)} | 灵脉：{veins}");
            AddInfoRow("危险", $"{DangerLabel(WorldMapProgressRules.GetDanger(cell))} | 地点：{markers}");
            AddInfoRow("认知", known ? "已知" : "未知");
            AddInfoRow("控制关系", control);
        }

        private void ShowLocationTab(WorldMap map, WorldMapProgressState progress,
            int selectedCellIndex, PlayerData sect)
        {
            WorldCell cell = map.cells[selectedCellIndex];
            WorldLocation location = map.GetLocationAt(cell);
            if (location == null || !WorldLocationRules.IsLocationRevealed(location, progress))
            {
                AddInfoRow("地点", "暂无地点");
                AddInfoRow("未来支持", "建设 / 改造 / 发现地点");
                return;
            }

            AddInfoRow("名称", location.name);
            AddInfoRow("类型", location.type == LocationType.Sect
                ? "玩家宗门"
                : LocationTypeLabel(location.type));
            if (location.type == LocationType.Village)
            {
                VillageState village = sect?.founding?.village ?? new VillageState();
                AddInfoRow("人口", village.population.ToString());
                AddInfoRow("关系", VillageRelationLabel(village.relation));
                AddInfoRow("劳动力",
                    $"{village.totalLabor - village.reservedLabor}/{village.totalLabor}");
                string threatText = ThreatSummary(sect?.founding?.externalThreat);
                if (!string.IsNullOrEmpty(threatText))
                    AddInfoRow("威胁", threatText);
            }
            else if (location.type == LocationType.Sect)
            {
                int disciples = NPCManager.Instance == null
                    ? 0
                    : NPCManager.Instance.GetAllNPC().Count;
                int materials = WarehouseManager.Instance == null
                    ? 0
                    : WarehouseManager.Instance.GetItemCount(FacilityRules.BasicMaterialId);
                AddInfoRow("弟子", disciples.ToString());
                AddInfoRow("灵气", $"{cell.totalAura:0.000}");
                AddInfoRow("灵材", (sect?.gold ?? 0).ToString());
                AddInfoRow("基础材料", materials.ToString());
                AddInfoRow("影响范围", InfluenceSummary(map, progress, selectedCellIndex));
            }
            AddInfoRow("状态", LocationStateLabel(location.state));
        }

        private void ShowActionTab(WorldMap map, WorldMapProgressState progress, int selectedCellIndex)
        {
            WorldCell cell = map.cells[selectedCellIndex];
            WorldLocation location = map.GetLocationAt(cell);
            if (location == null || !WorldLocationRules.IsLocationRevealed(location, progress))
            {
                AddInfoText("该格没有地点，暂无可执行行动。", 15);
                return;
            }

            bool hasContent = false;
            if (location.availableActions != null && location.availableActions.Count > 0)
            {
                hasContent = true;
                foreach (LocationAction action in location.availableActions)
                {
                    if (action == null) continue;
                    AddActionButton(location, action);
                }
            }

            if (location.availableMissionIds != null && location.availableMissionIds.Count > 0)
            {
                hasContent = true;
                AddInfoText("地点任务", 15);
                foreach (string missionId in location.availableMissionIds)
                {
                    MissionData data = MissionManager.Instance == null
                        ? null
                        : MissionManager.Instance.GetMissionData(missionId);
                    if (data == null) continue;
                    AddLocationMissionButton(location, data);
                }
            }

            if (!hasContent)
                AddInfoText("该地点暂无可执行行动。", 15);
        }

        private void AddLocationMissionButton(WorldLocation location, MissionData data)
        {
            Button button = RuntimeUIFactory.Button(infoContent,
                $"{data.name}　{data.needDays}天", 46);
            WorldLocation captured = location;
            button.onClick.AddListener(() => LocationMissionsRequested?.Invoke(captured));
            infoDynamicItems.Add(button.gameObject);
        }

        private void AddActionButton(WorldLocation location, LocationAction action)
        {
            Button button = RuntimeUIFactory.Button(infoContent,
                $"{action.displayName}\n消耗：{action.cost}", 52);
            button.interactable = action.available;
            WorldLocation capturedLocation = location;
            LocationAction capturedAction = action;
            button.onClick.AddListener(() =>
                LocationActionRequested?.Invoke(capturedLocation, capturedAction));
            infoDynamicItems.Add(button.gameObject);
        }

        private void AddInfoRow(string titleValue, string contentValue)
        {
            WorldMapInfoRow row = WorldMapInfoRow.Create(infoContent, titleValue, contentValue);
            infoDynamicItems.Add(row.gameObject);
        }

        private void AddInfoText(string textValue, int fontSize)
        {
            GameObject textObject = new GameObject("InfoText",
                typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textObject.transform.SetParent(infoContent, false);
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.text = textValue;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.enableWordWrapping = true;
            LayoutElement layout = textObject.GetComponent<LayoutElement>();
            layout.minHeight = 40f;
            layout.preferredHeight = 100f;
            layout.flexibleHeight = 0f;
            infoDynamicItems.Add(textObject);
        }

        private void ClearInfoDynamicItems()
        {
            foreach (GameObject item in infoDynamicItems)
            {
                if (item == null) continue;
                if (Application.isPlaying) Destroy(item);
                else DestroyImmediate(item);
            }
            infoDynamicItems.Clear();
        }

        private void SelectInfoTab(int index)
        {
            if (index < 0 || index >= infoTabButtons.Count || index == selectedInfoTab) return;
            selectedInfoTab = index;
            RefreshInfoTabVisuals();
            RefreshPresentationWithCurrentSelection();
        }

        private void RefreshInfoTabVisuals()
        {
            for (int index = 0; index < infoTabButtons.Count; index++)
                infoTabButtons[index].GetComponent<Image>().color = index == selectedInfoTab
                    ? new Color(0.55f, 0.36f, 0.13f, 1f)
                    : new Color(0.20f, 0.17f, 0.13f, 1f);
        }

        private void RefreshPresentationWithCurrentSelection()
        {
            if (WorldMapSession.Current == null || lastSelectedCellIndex < 0) return;
            Refresh(WorldMapSession.Current, WorldMapSession.Progress, lastSelectedCellIndex,
                false, WorldMapProgressRules.GetSectBase(WorldMapSession.Progress) != null,
                true, PlayerManager.Instance?.playerData);
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

        private static string LocationTypeLabel(LocationType type)
        {
            switch (type)
            {
                case LocationType.Village: return "村庄";
                case LocationType.Sect: return "宗门";
                case LocationType.ResourceNode: return "资源点";
                case LocationType.Ruins: return "遗迹";
                case LocationType.MonsterNest: return "妖兽巢穴";
                default: return "未知";
            }
        }

        private static string LocationStateLabel(LocationState state)
        {
            switch (state)
            {
                case LocationState.Active: return "正常";
                case LocationState.Inactive: return "关闭";
                case LocationState.Locked: return "封锁";
                default: return "未知";
            }
        }

        private static string InfluenceSummary(WorldMap map, WorldMapProgressState progress,
            int cellIndex)
        {
            if (progress == null) return "未知";
            WorldMapInfluenceRules.EnsureCurrent(map, progress);
            CellInfluenceRuntimeState state = WorldMapInfluenceRules.GetCellState(map, progress, cellIndex);
            return state.level == InfluenceLevel.Core ? "核心区域" :
                state.level == InfluenceLevel.Influence ? "影响区域" :
                state.level == InfluenceLevel.Outer ? "外缘区域" : "无影响";
        }

        private static string ClimateLabel(WorldCell cell)
        {
            if (cell == null) return "未知";
            if (cell.temperature < 0.32f) return "寒冷";
            if (cell.temperature < 0.45f) return "温凉";
            if (cell.temperature < 0.60f) return "温暖";
            if (cell.temperature < 0.75f) return "炎热";
            return "酷热";
        }

        private static string DangerLabel(WorldDangerLevel level)
        {
            switch (level)
            {
                case WorldDangerLevel.Low: return "低";
                case WorldDangerLevel.Medium: return "中";
                case WorldDangerLevel.High: return "高";
                default: return "低";
            }
        }

        private static string ThreatSummary(ActiveThreatState threat)
        {
            if (threat == null || string.IsNullOrEmpty(threat.threatId) ||
                threat.status == ExternalThreatStatus.None) return null;
            switch (threat.status)
            {
                case ExternalThreatStatus.Scheduled:
                    return $"青石兽潮（第 {threat.scheduledDay} 天来袭）";
                case ExternalThreatStatus.Active:
                    return $"青石兽潮（活跃，下次袭击第 {threat.nextRaidDay} 天）";
                case ExternalThreatStatus.Resolved:
                    return "青石兽潮（已平息）";
                default:
                    return "外部威胁";
            }
        }

        private static string VillageRelationLabel(int relation)
        {
            if (relation >= FoundingRules.VillageSupportRelation) return "信赖";
            if (relation >= FoundingRules.VillageFamiliarRelation) return "熟悉";
            return "陌生";
        }

        private void OnDestroy()
        {
            if (hudCanvas != null)
            {
                if (Application.isPlaying) Destroy(hudCanvas.gameObject);
                else DestroyImmediate(hudCanvas.gameObject);
            }
            hudCanvas = null;
            hudControls = null;
            selectionBlocker = null;
            ClearInfoDynamicItems();
        }
    }
}
