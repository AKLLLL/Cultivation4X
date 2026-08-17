using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;

public class MissionPanel : MonoBehaviour
{
    private enum MissionPage
    {
        Repair,
        Labor,
        VillageThreat,
        Other
    }

    [Header("UI引用")]
    [SerializeField] private GameObject missionPanel;
    [SerializeField] private GameObject npcSelectPanel;


    // 当前选择的任务
    private MissionData selectedMissionData;
    // 当前派遣的NPC
    [Header("NPC选择")]
    private NPCRuntime selectedNPC;
    public TMP_Text npcNameText;

    public Button selectNPCButton;
    private RectTransform dynamicList;
    private RectTransform dynamicScrollRoot;
    private RectTransform rewardArea;
    private RectTransform missionTabs;
    private TMP_Text statusText;
    private TMP_Text mapContextText;
    private string dynamicFeedback;
    private MissionPage currentPage;
    private readonly Dictionary<MissionPage, Button> pageButtons = new Dictionary<MissionPage, Button>();
    private MapMissionContext selectedMapContext;
    private WorldLocation selectedLocation;
    private void Start()
    {
        EnsureDynamicUI();
    }

    public  void SelectMission(string id)
    {
        selectedMapContext = null;
        selectedLocation = null;
        RefreshMissionMode();
        // 根据任务ID获取Mission对象
        selectedMissionData = MissionManager.Instance.GetMissionData(id);

        if (selectedMissionData == null)
        {
            Debug.LogError($"找不到任务：{id}");
            return;
        }

        Debug.Log($"已选择任务：{selectedMissionData.name}");
        if (!selectedMissionData.isStoryAction && !selectedMissionData.isFacilityAction)
        {
            Debug.Log(
                $"任务门槛【{selectedMissionData.name}】：力量≥{selectedMissionData.requiredAttack}，" +
                $"智力≥{selectedMissionData.requiredIntelligence}，战力≥{selectedMissionData.requiredCombatPower}");
        }
        RefreshSelectionStatus();
    }
   
    /// <summary>
    /// 设置当前派遣NPC
    /// </summary>
    public void SetSelectedNPC(NPCRuntime npc)
    {
        selectedNPC = npc;

        if (npc == null)
        {
            npcNameText.text = "请选择NPC";
        }
        else
        {
            string displayName = npc.Character?.displayName ?? npc.Data?.npcName ?? "未知弟子";
            npcNameText.text = displayName;
            Debug.Log($"已选择NPC：{displayName}");
            RefreshSelectionStatus();
        }
        
    }


    public void OnStartButtonClick()
    {
        if (selectedMissionData == null)
        {
            Debug.Log("请选择任务");
            return;
        }
        if (selectedNPC == null)
        {
            Debug.Log("请选择NPC");
            return;
        }
        if (selectedMapContext != null)
        {
            if (!MissionManager.Instance.TryStartMapMission(selectedMapContext, selectedNPC, out string mapReason))
            { if (statusText != null) statusText.text = mapReason; return; }
        }
        else
        {
            if (!MissionManager.Instance.CanTriggerMission(selectedMissionData.id, selectedNPC, out string reason))
            { if (statusText != null) statusText.text = reason; return; }
            MissionManager.Instance.TriggerMission(selectedMissionData.id, selectedNPC);
        }

        // 关闭任务面板
        UIManager.Instance.ClosePanel(missionPanel);
    }


    public void OnSelectNPCButtonClick()
    {
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(npcSelectPanel);
    }

    public void OnCancelButtonClick()
    {
        selectedMissionData = null;
        selectedLocation = null;
        UIManager.Instance.ClosePanel(missionPanel);
    }

    private void OnEnable()
    {
        transform.SetAsLastSibling();
        selectedMissionData = null;
        selectedMapContext = null;
        selectedLocation = null;
        EnsureDynamicUI();
        RefreshMissionMode();
        RefreshDynamicList();
        SetSelectedNPC(null);
       
    }

    public bool OpenMapMission(MapMissionContext context, out string reason)
    {
        if (context == null || MissionManager.Instance == null)
        { reason = "地图任务系统尚未初始化"; return false; }
        if (!WorldMapContentRules.CanStartAction(WorldMapSession.Current, WorldMapSession.Progress,
                context, out reason)) return false;
        string missionId = WorldMapContentRules.MissionIdFor(context.actionType);
        MissionData data = MissionManager.Instance.GetMissionData(missionId);
        if (data == null) { reason = "地图任务模板不存在"; return false; }
        if (UIManager.Instance == null) { reason = "界面系统尚未初始化"; return false; }
        UIManager.Instance.OpenPanel(missionPanel == null ? gameObject : missionPanel);
        selectedMapContext = new MapMissionContext
        {
            actionType = context.actionType,
            targetCellIndex = context.targetCellIndex,
            targetSiteId = context.targetSiteId
        };
        selectedMissionData = data;
        RefreshMissionMode();
        SetSelectedNPC(null);
        RefreshSelectionStatus();
        reason = null;
        return true;
    }

    public bool OpenLocationMissions(WorldLocation location)
    {
        if (location == null || MissionManager.Instance == null)
        { return false; }
        if (UIManager.Instance == null) return false;
        UIManager.Instance.OpenPanel(missionPanel == null ? gameObject : missionPanel);
        selectedMapContext = null;
        selectedLocation = location;
        selectedMissionData = null;
        RefreshMissionMode();
        RefreshDynamicList();
        SetSelectedNPC(null);
        return true;
    }

    private void EnsureDynamicUI()
    {
        if (dynamicList != null || missionPanel == null) return;
        RectTransform container = RuntimeUIFactory.Panel(missionPanel.transform, "SectAffairs", new Vector2(0.52f, 0.08f), new Vector2(0.97f, 0.92f));
        statusText = RuntimeUIFactory.Text(container, string.Empty, 17, 52);
        mapContextText = RuntimeUIFactory.Text(container, string.Empty, 18, 84);
        mapContextText.gameObject.SetActive(false);
        rewardArea = CreateFixedArea(container, "AwaitingRewards");
        missionTabs = RuntimeUIFactory.TabBar(container, "MissionTabs");
        AddPageTab(missionTabs, MissionPage.Repair, "洞府修复");
        AddPageTab(missionTabs, MissionPage.Labor, "劳动力任务");
        AddPageTab(missionTabs, MissionPage.VillageThreat, "村庄与威胁");
        AddPageTab(missionTabs, MissionPage.Other, "其他任务");
        dynamicList = RuntimeUIFactory.ScrollContent(container, "MissionScroll");
        dynamicScrollRoot = dynamicList.parent.parent as RectTransform;
    }

    private void RefreshMissionMode()
    {
        bool mapMode = selectedMapContext != null;
        bool locationMode = selectedLocation != null;
        if (missionTabs != null) missionTabs.gameObject.SetActive(!mapMode && !locationMode);
        if (dynamicScrollRoot != null) dynamicScrollRoot.gameObject.SetActive(!mapMode);
        if (rewardArea != null) rewardArea.gameObject.SetActive(!mapMode && !locationMode);
        if (mapContextText == null) return;
        mapContextText.gameObject.SetActive(mapMode || locationMode);
        if (mapMode) mapContextText.text = FormatMapActionContext(selectedMapContext);
        else if (locationMode) mapContextText.text = FormatLocationContext(selectedLocation);
        else mapContextText.text = string.Empty;
    }

    private static string FormatLocationContext(WorldLocation location)
    {
        if (location == null) return string.Empty;
        return $"地点任务：{location.name}\n请选择任务并派遣弟子执行；任务进度由时间推进。";
    }

    private static string FormatMapActionContext(MapMissionContext context)
    {
        if (context == null) return string.Empty;
        string action = context.actionType == MapActionType.Explore ? "派遣探索" :
            context.actionType == MapActionType.InvestigateSpiritSpring ? "调查灵泉" :
            context.actionType == MapActionType.DevelopSpiritSpring ? "开发灵泉" :
            context.actionType == MapActionType.EstablishVillageRelation ? "建立村庄关系" :
            context.actionType == MapActionType.DevelopSpiritMine ? "开发灵矿" :
            context.actionType == MapActionType.BuildCaveResidenceOutpost ? "建立洞府据点规则" :
            context.actionType == MapActionType.ClearBeastLair ? "清理兽巢" :
            context.actionType == MapActionType.InvestigateRuin ? "调查遗迹" : "地图行动";
        WorldMap map = WorldMapSession.Current;
        string target = map?.cells != null && context.targetCellIndex >= 0 && context.targetCellIndex < map.cells.Length
            ? $"{map.cells[context.targetCellIndex].coord.col},{map.cells[context.targetCellIndex].coord.row}（格 {context.targetCellIndex}）"
            : $"格 {context.targetCellIndex}";
        return $"地图行动：{action}\n目标：{target}\n请选择执行弟子；普通宗门事务已暂时隐藏。";
    }

    private void RefreshDynamicList()
    {
        if (dynamicList == null || MissionManager.Instance == null) return;
        ClearChildren(dynamicList);
        ClearChildren(rewardArea);
        if (selectedLocation != null)
        {
            RefreshLocationMissions();
            return;
        }
        RefreshPageTabs();
        int reputation = PlayerManager.Instance == null ? 0 : PlayerManager.Instance.playerData.reputation;
        string header = $"宗门事务　声望 {reputation}　开放至 {FacilityRules.MaxMissionRankForReputation(reputation)} 阶任务";
        statusText.text = string.IsNullOrEmpty(dynamicFeedback) ? header : $"{header}\n{dynamicFeedback}";
        dynamicFeedback = null;
        List<MissionData> visible = MissionManager.Instance.GetVisibleMissions()
            .ToList();
        List<MissionData> pageMissions = visible.Where(data => GetMissionPage(data) == currentPage).ToList();
        if (currentPage == MissionPage.Repair)
            AddSection("洞府修复", pageMissions);
        else if (currentPage == MissionPage.Labor)
            AddLaborSection(pageMissions);
        else if (currentPage == MissionPage.VillageThreat)
        {
            AddSection("村庄行动", pageMissions.Where(data => data.threatMissionKind == ThreatMissionKind.None));
            AddSection("外部威胁调查", pageMissions.Where(data => data.threatMissionKind == ThreatMissionKind.Investigation));
        }
        else
        {
            AddSection("宗门路线", pageMissions.Where(IsRouteAction));
            AddSection("常规任务", pageMissions.Where(data => !data.isStoryAction && !data.isFacilityAction &&
                data.threatMissionKind == ThreatMissionKind.None && !IsRouteAction(data)));
            AddSection("设施行动", pageMissions.Where(data => data.isFacilityAction && !data.isStoryAction));
        }
        if (pageMissions.Count == 0)
            RuntimeUIFactory.Text(dynamicList, "当前分页暂无可用任务。", 18, 42);

        List<Mission> awaitingRewards = MissionManager.Instance.GetActiveMissions()
            .Where(item => item.State == MissionState.AwaitingReward).ToList();
        if (awaitingRewards.Count > 0)
            RuntimeUIFactory.Text(rewardArea, $"待领奖任务（{awaitingRewards.Count}）", 18, 32);
        foreach (Mission mission in awaitingRewards)
        {
            Button claim = RuntimeUIFactory.Button(rewardArea, $"领取：{mission.Data.name}", 38);
            claim.onClick.AddListener(() =>
            {
                dynamicFeedback = MissionManager.Instance.TryClaimReward(mission, out string reason)
                    ? $"已领取：{mission.Data.name}"
                    : reason;
                RefreshDynamicList();
            });
        }
    }

    private void RefreshLocationMissions()
    {
        if (selectedLocation == null) return;
        statusText.text = $"地点任务：{selectedLocation.name}";
        List<MissionData> missions = (selectedLocation.availableMissionIds ?? new List<string>())
            .Select(MissionManager.Instance.GetMissionData)
            .Where(data => data != null)
            .ToList();
        if (missions.Count == 0)
        {
            RuntimeUIFactory.Text(dynamicList, "该地点暂无可接任务。", 18, 42);
            return;
        }
        AddSection("地点任务", missions);
    }

    private void AddPageTab(Transform tabs, MissionPage page, string label)
    {
        Button button = RuntimeUIFactory.TabButton(tabs, label, currentPage == page);
        pageButtons[page] = button;
        button.onClick.AddListener(() =>
        {
            if (currentPage == page) return;
            currentPage = page;
            RefreshDynamicList();
        });
    }

    private void RefreshPageTabs()
    {
        foreach (KeyValuePair<MissionPage, Button> item in pageButtons)
        {
            if (item.Value == null) continue;
            item.Value.GetComponent<Image>().color = item.Key == currentPage
                ? new Color(0.55f, 0.36f, 0.13f, 1f)
                : new Color(0.20f, 0.17f, 0.13f, 1f);
        }
    }

    private static RectTransform CreateFixedArea(Transform parent, string name)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        obj.transform.SetParent(parent, false);
        VerticalLayoutGroup layout = obj.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 4;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        obj.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return obj.GetComponent<RectTransform>();
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    private static MissionPage GetMissionPage(MissionData data)
    {
        if (data.foundingAction == FoundingActionKind.RepairFacility)
            return MissionPage.Repair;
        if (IsLaborAction(data.foundingAction))
            return MissionPage.Labor;
        if (data.foundingAction == FoundingActionKind.VillagePreach ||
            data.foundingAction == FoundingActionKind.VillageHelp ||
            data.threatMissionKind == ThreatMissionKind.Investigation)
            return MissionPage.VillageThreat;
        return MissionPage.Other;
    }

    private static bool IsRouteAction(MissionData data)
    {
        return data.foundingAction == FoundingActionKind.BuildRouteFacility ||
            data.foundingAction == FoundingActionKind.RouteAlchemy ||
            data.foundingAction == FoundingActionKind.RouteForge ||
            data.foundingAction == FoundingActionKind.RouteFormation;
    }

    private void AddMissionButton(MissionData data)
    {
        if (data == null) return;
        string reason = GeneralLockReason(data);
        int days = data.isFacilityAction && FacilityRules.UsesLevelScaledAction(data.requiredFacility) && PlayerManager.Instance != null
            ? FacilityRules.ActionDays(data.requiredFacility, PlayerManager.Instance.GetFacilityLevel(data.requiredFacility)) : data.needDays;
        Button button = RuntimeUIFactory.Button(dynamicList, string.IsNullOrEmpty(reason)
            ? $"{data.name}　{days}天　消耗 {FormatMissionCosts(data)}" : $"{data.name}（{reason}）", 48);
        button.interactable = string.IsNullOrEmpty(reason);
        button.onClick.AddListener(() => SelectMission(data.id));
    }

    private static string FormatMissionCosts(MissionData data)
    {
        List<string> costs = new List<string>();
        if (data.goldCost > 0) costs.Add($"{data.goldCost}灵材");
        foreach (IGrouping<string, ItemReward> group in (data.itemCosts ?? new List<ItemReward>())
                     .Where(item => item != null && item.count > 0 && !string.IsNullOrWhiteSpace(item.itemId))
                     .GroupBy(item => item.itemId))
        {
            int count = group.Sum(item => item.count);
            costs.Add($"{ItemDisplayName(group.Key)}×{count}");
        }
        if (data.laborCost > 0) costs.Add($"{data.laborCost}劳动力");
        return costs.Count == 0 ? "无" : string.Join("、", costs);
    }

    private static string FormatMissionOutputs(MissionData data)
    {
        List<string> outputs = new List<string>();
        if (data.goldReward > 0) outputs.Add($"{data.goldReward}灵材");
        if (data.expReward > 0) outputs.Add($"{data.expReward}修为");
        foreach (IGrouping<string, ItemReward> group in (data.itemRewards ?? new List<ItemReward>())
                     .Where(item => item != null && item.count > 0 && !string.IsNullOrWhiteSpace(item.itemId))
                     .GroupBy(item => item.itemId))
        {
            outputs.Add($"{ItemDisplayName(group.Key)}×{group.Sum(item => item.count)}");
        }
        return outputs.Count == 0 ? "无" : string.Join("、", outputs);
    }

    private static string ItemDisplayName(string itemId)
    {
        return itemId == FacilityRules.BasicMaterialId ? "基础材料" :
            ItemDatabase.Instance?.GetItem(itemId)?.itemName ?? itemId;
    }

    private string GeneralLockReason(MissionData data)
    {
        if (PlayerManager.Instance == null || WarehouseManager.Instance == null) return "系统未初始化";
        if (!MissionManager.Instance.IsMissionVisible(data)) return "当前宗门状态未开放";
        if (data.requiredFacilityLevel > PlayerManager.Instance.GetFacilityLevel(data.requiredFacility)) return "设施等级不足";
        if (data.foundingAction == FoundingActionKind.BuildRouteFacility)
        {
            if (PlayerManager.Instance.playerData.founding.techniqueUnderstanding < FoundingRules.MaxUnderstanding) return "功法理解未达到100%";
            VillageState village = PlayerManager.Instance.playerData.founding.village;
            if (village == null || village.totalLabor - village.reservedLabor < data.laborCost) return "可用劳动力不足";
        }
        if (PlayerManager.Instance.playerData.gold < data.goldCost) return "灵材不足";
        foreach (ItemReward cost in data.itemCosts ?? new List<ItemReward>())
            if (WarehouseManager.Instance.GetItemCount(cost.itemId) < cost.count) return "材料不足";
        if (data.isFacilityAction && MissionManager.Instance.GetActiveMissions().Any(m => m.Data.isFacilityAction &&
            m.Data.requiredFacility == data.requiredFacility && (m.State == MissionState.Active || m.State == MissionState.WaitingNode))) return "设施忙碌";
        return null;
    }

    private void AddSection(string title, IEnumerable<MissionData> missions)
    {
        List<MissionData> list = missions.ToList();
        if (list.Count == 0) return;
        RuntimeUIFactory.Text(dynamicList, title, 20, 34);
        foreach (MissionData data in list) AddMissionButton(data);
    }

    private void AddLaborSection(IEnumerable<MissionData> missions)
    {
        List<MissionData> list = missions.ToList();
        if (list.Count == 0) return;
        RuntimeUIFactory.Text(dynamicList, "劳动力行动", 20, 34);
        foreach (MissionData data in list)
        {
            bool canStart = MissionManager.Instance.CanTriggerLaborMission(data.id, out string reason);
            Button button = RuntimeUIFactory.Button(dynamicList,
                canStart
                    ? $"{data.name}　{data.needDays}天　消耗 {FormatMissionCosts(data)}　产出 {FormatMissionOutputs(data)}"
                    : $"{data.name}（{reason}）　产出 {FormatMissionOutputs(data)}", 48);
            button.interactable = canStart;
            button.onClick.AddListener(() => { MissionManager.Instance.TriggerLaborMission(data.id); RefreshDynamicList(); });
        }
    }

    private static bool IsLaborAction(FoundingActionKind action)
    {
        return action == FoundingActionKind.LaborGather || action == FoundingActionKind.LaborBuild ||
            action == FoundingActionKind.LaborCultivate;
    }

    private static string TierName(MissionResultTier tier)
    {
        return tier == MissionResultTier.Excellent ? "优秀" : tier == MissionResultTier.Insufficient ? "能力不足" : "达标";
    }

    private void RefreshSelectionStatus()
    {
        if (statusText == null || selectedMissionData == null) return;
        if (selectedNPC == null)
        {
            statusText.text = $"已选择：{selectedMissionData.name}　请选择弟子";
            return;
        }
        MissionCapabilityEvaluation evaluation = CharacterCapabilityRules.EvaluateMission(selectedMissionData, selectedNPC);
        statusText.text = $"已选择：{selectedMissionData.name}　预计：{TierName(evaluation.tier)}　评分 {evaluation.score}";
    }
}
