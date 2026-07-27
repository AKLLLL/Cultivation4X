using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class MissionPanel : MonoBehaviour
{
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
    private TMP_Text statusText;
    private string dynamicFeedback;
    private void Start()
    {
        EnsureDynamicUI();
    }

  public  void SelectMission(string id)
    {
        // 根据任务ID获取Mission对象
        selectedMissionData = MissionManager.Instance.GetMissionData(id);

        if (selectedMissionData == null)
        {
            Debug.LogError($"找不到任务：{id}");
            return;
        }

        Debug.Log($"已选择任务：{selectedMissionData.name}");
        if (!selectedMissionData.isStoryAction && !selectedMissionData.isFacilityAction &&
            selectedMissionData.explorationKind == ExplorationMissionKind.None)
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
        if (!MissionManager.Instance.CanTriggerMission(selectedMissionData.id, selectedNPC, out string reason))
        { if (statusText != null) statusText.text = reason; return; }
        MissionManager.Instance.TriggerMission(selectedMissionData.id, selectedNPC);

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

        UIManager.Instance.ClosePanel(missionPanel);
    }

    private void OnEnable()
    {
        transform.SetAsLastSibling();
        EnsureDynamicUI();
        RefreshDynamicList();
        selectedMissionData = null;
        SetSelectedNPC(null);
       
    }

    private void EnsureDynamicUI()
    {
        if (dynamicList != null || missionPanel == null) return;
        RectTransform container = RuntimeUIFactory.Panel(missionPanel.transform, "SectAffairs", new Vector2(0.52f, 0.08f), new Vector2(0.97f, 0.92f));
        dynamicList = RuntimeUIFactory.ScrollContent(container, "MissionScroll");
        statusText = RuntimeUIFactory.Text(dynamicList, string.Empty, 17, 52);
    }

    private void RefreshDynamicList()
    {
        if (dynamicList == null || MissionManager.Instance == null) return;
        for (int i = dynamicList.childCount - 1; i >= 1; i--) Destroy(dynamicList.GetChild(i).gameObject);
        int reputation = PlayerManager.Instance == null ? 0 : PlayerManager.Instance.playerData.reputation;
        string header = $"宗门事务　声望 {reputation}　开放至 {FacilityRules.MaxMissionRankForReputation(reputation)} 阶任务";
        statusText.text = string.IsNullOrEmpty(dynamicFeedback) ? header : $"{header}\n{dynamicFeedback}";
        dynamicFeedback = null;
        // 探索保持由原探索面板负责，避免在本次“宗门事务”入口中提前暴露未来系统。
        List<MissionData> visible = MissionManager.Instance.GetVisibleMissions()
            .Where(data => data.explorationKind == ExplorationMissionKind.None)
            .ToList();
        AddSection("洞府修复", visible.Where(data => data.foundingAction == FoundingActionKind.RepairFacility));
        AddSection("村庄行动", visible.Where(data => data.foundingAction == FoundingActionKind.VillagePreach || data.foundingAction == FoundingActionKind.VillageHelp));
        AddSection("外部威胁调查", visible.Where(data => data.threatMissionKind == ThreatMissionKind.Investigation));
        AddLaborSection(visible.Where(data => IsLaborAction(data.foundingAction)));
        AddSection("宗门路线", visible.Where(data => data.foundingAction == FoundingActionKind.BuildRouteFacility ||
            data.foundingAction == FoundingActionKind.RouteAlchemy || data.foundingAction == FoundingActionKind.RouteForge ||
            data.foundingAction == FoundingActionKind.RouteFormation));
        AddSection("常规任务", visible.Where(data => !data.isStoryAction && !data.isFacilityAction &&
            data.threatMissionKind == ThreatMissionKind.None));
        AddSection("设施行动", visible.Where(data => data.isFacilityAction && !data.isStoryAction));
        foreach (Mission mission in MissionManager.Instance.GetActiveMissions().Where(item => item.State == MissionState.AwaitingReward).ToList())
        {
            Button claim = RuntimeUIFactory.Button(dynamicList, $"领取：{mission.Data.name}");
            claim.onClick.AddListener(() =>
            {
                dynamicFeedback = MissionManager.Instance.TryClaimReward(mission, out string reason)
                    ? $"已领取：{mission.Data.name}"
                    : reason;
                RefreshDynamicList();
            });
        }
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
