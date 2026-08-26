using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using System.Linq;
using Cultivation4X.WorldMap;

/// <summary>
/// 任务管理器
///
/// 职责：
/// 1. 加载任务模板 MissionData
/// 2. 根据模板创建 Mission实例
/// 3. 管理正在运行的任务
/// 4. 推进任务时间
/// 5. 判断任务结果
/// </summary>
public class MissionManager : MonoBehaviour
{

    public static MissionManager Instance;



    /// <summary>
    /// 固定任务模板库
    /// </summary>
    private Dictionary<string, MissionData> missionTemplates
        =
        new Dictionary<string, MissionData>();
    /// <summary>
    /// 根据任务类型获取任务列表
    /// 给UI入口使用
    /// </summary>
    public List<MissionData> GetMissionByType(MissionType type)
    {

        List<MissionData> result =
            new List<MissionData>();


        foreach (MissionData mission in missionTemplates.Values)
        {

            if (mission.missionType == type)
            {
                result.Add(mission);
            }

        }


        return result;

    }


    /// <summary>
    /// 当前正在进行的任务
    /// </summary>
    private List<Mission> activeMissions =
        new List<Mission>();
    private readonly List<string> dailyMissionCandidateIds = new List<string>();
    private int missionCandidateDay = -1;
    private readonly List<MissionDayResult> dailyResults = new List<MissionDayResult>();


    public MissionNodePanel missionNodePanel;

    private void Awake()
    {

        if (Instance == null)
        {
            Instance = this;

            DontDestroyUtility.MarkPersistent(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }

    }



    private void Start()
    {

        if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed += OnDayPassed;


        LoadMissionsFromJson();
        RefreshDailyCandidates(TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay);

    }




    /// <summary>
    /// 加载所有任务模板
    /// </summary>
    public void LoadMissionsFromJson()
    {

        missionTemplates.Clear();

        TextAsset[] legacyEventFiles = Resources.LoadAll<TextAsset>("Configs/Events");
        TextAsset[] jsonFiles = Resources.LoadAll<TextAsset>("Configs/Missions")
            .Concat(legacyEventFiles)
            .ToArray();

        if (jsonFiles.Length == 0)
        {
            Debug.LogError(
                "Mission Json不存在"
            );

            return;
        }

        foreach (TextAsset json in jsonFiles)
        {

            MissionData data =
                JsonConvert.DeserializeObject<MissionData>(
                    json.text
                );

            if (data == null)
            {
                Debug.LogError(
                    $"解析失败:{json.name}"
                );

                continue;
            }

            if (legacyEventFiles.Contains(json)) data.missionType = MissionType.WorldEvent;

            if (missionTemplates.ContainsKey(data.id))
            {
                Debug.LogError(
                    $"任务ID重复:{data.id}"
                );

                continue;
            }

            missionTemplates.Add(
                data.id,
                data
            );
           // Debug.Log(
           //     $"加载任务模板:{data.id} {data.name}"
           // );

        }
        Debug.Log(
            $"任务模板数量:{missionTemplates.Count}"
        );

        RegisterMapMissionTemplates();

    }


    /// <summary>
    /// 根据任务ID创建一个新的任务实例
    /// </summary>
    public Mission CreateMission(string missionId)
    {

        if (!missionTemplates.ContainsKey(missionId))
        {
            Debug.LogError(
                $"不存在任务模板:{missionId}"
            );

            return null;
        }

        MissionData data = missionTemplates[missionId];
        int facilityLevel = data.isFacilityAction && PlayerManager.Instance != null
            ? PlayerManager.Instance.GetFacilityLevel(data.requiredFacility) : 1;
        Mission mission = new Mission(data, facilityLevel);


        return mission;

    }
    /// <summary>
    /// 添加运行中的任务
    ///
    /// 给随机事件、剧情事件使用
    /// 因为这些任务不是从固定任务列表直接开始
    /// </summary>
    public void AddActiveMission(Mission mission)
    {

        if (mission == null)
        {
            Debug.LogWarning(
                "尝试加入空任务"
            );

            return;
        }


        if (!activeMissions.Contains(mission))
        {
            activeMissions.Add(mission);


            Debug.Log(
                $"加入运行任务:{mission.Data.name}"
            );
        }

    }
    /// <summary>
    /// 任务节点触发
    /// 通知UI显示
    /// </summary>
    public void OnMissionNodeTriggered(Mission mission)
    {

        if (missionNodePanel == null)
        {
            Debug.LogWarning(
            "没有绑定MissionNodePanel"
            );

            return;
        }


        missionNodePanel.Show(mission);

    }
    /// <summary>
    /// UI调用
    /// 开始任务
    /// </summary>
    public void TriggerMission(
        string missionId,
        NPCRuntime npc)
    {
        if (!CanTriggerMission(missionId, npc, out string reason))
        {
            Debug.LogWarning(reason);
            return;
        }
        TryCancelAutonomousMission(npc, out _);
        if (!npc.CanDispatch())
        {
            Debug.Log(
            $"{npc.Data.npcName} 当前无法执行任务"
            );

            return;
        }
        Mission mission =
            CreateMission(missionId);
        if (mission == null)
            return;
        if (!TrySpendMissionCosts(mission.Data, out reason))
        {
            Debug.LogWarning(reason);
            return;
        }
        if (!PlayerManager.Instance.TryReserveLabor(mission.Data.laborCost, out reason))
        {
            RefundMissionCosts(mission.Data);
            Debug.LogWarning(reason);
            return;
        }
        mission.StartMission(npc);
        TriggerMissionSource(mission.Data, npc, false);

        activeMissions.Add(mission);
        Debug.Log(
        $"创建任务实例:{mission.Data.name}"
    );
        Debug.Log(
            $"开始任务:{mission.Data.name}"
        );
        SaveManager.Instance?.AutoSave();
    }

    public void TriggerLaborMission(string missionId)
    {
        if (!CanTriggerLaborMission(missionId, out string reason))
        {
            Debug.LogWarning(reason);
            return;
        }
        Mission mission = CreateMission(missionId);
        if (mission == null)
            return;
        if (!TrySpendMissionCosts(mission.Data, out reason))
        {
            Debug.LogWarning(reason);
            return;
        }
        if (!PlayerManager.Instance.TryReserveLabor(mission.Data.laborCost, out reason))
        {
            RefundMissionCosts(mission.Data);
            Debug.LogWarning(reason);
            return;
        }
        mission.StartLaborMission();
        activeMissions.Add(mission);
        SaveManager.Instance?.AutoSave();
    }

    public bool TryStartMapMission(MapMissionContext context, NPCRuntime npc, out string reason)
    {
        if (npc == null || !CanDispatchOrCancelAutonomous(npc)) { reason = "弟子当前无法执行地图任务"; return false; }
        if (!WorldMapContentRules.CanStartAction(WorldMapSession.Current, WorldMapSession.Progress, context, out reason))
            return false;
        TryCancelAutonomousMission(npc, out _);
        if (activeMissions.Any(item => item?.MapContext != null &&
            (item.State == MissionState.Active || item.State == MissionState.WaitingNode || item.State == MissionState.AwaitingReward) &&
            item.MapContext.actionType == context.actionType &&
            item.MapContext.targetCellIndex == context.targetCellIndex &&
            string.Equals(item.MapContext.targetSiteId, context.targetSiteId, StringComparison.Ordinal)))
        { reason = "该地图行动已经在进行"; return false; }
        string templateId = TemplateIdFor(context.actionType);
        Mission mission = CreateMission(templateId);
        if (mission == null) { reason = "地图任务模板不存在"; return false; }
        mission.ConfigureMapMission(context, WorldMapContentRules.CreateReward(WorldMapSession.Current, context));
        mission.StartMission(npc);
        activeMissions.Add(mission);
        EventManager.Instance?.TryTriggerSource(EventSource.MissionStart, npc);
        SaveManager.Instance?.AutoSave();
        reason = null;
        return true;
    }


    /// <summary>
    /// 任务完成判断
    /// </summary>
    public void EvaluateMission(Mission mission)
    {
        if (mission.MapContext != null)
        {
            EvaluateMapMission(mission);
            return;
        }
        if (mission.Data.threatMissionKind == ThreatMissionKind.Investigation)
        {
            EvaluateThreatInvestigation(mission);
            return;
        }
        NPCRuntime npc =
            mission.AssignedNPC;

        MissionData data =
            mission.Data;

        if (IsLaborOnlyFoundingAction(data.foundingAction))
        {
            if (!RewardManager.Instance.CanGiveReward(mission.Reward))
            {
                mission.WaitForReward();
                RecordResult(mission, MissionState.AwaitingReward);
                Debug.LogWarning($"Labor mission reward is waiting for warehouse space: {data.name}");
                return;
            }
            ApplyFoundingSuccess(data, null);
            mission.CompleteMission();
            GiveSectReward(mission.Reward);
            RecordResult(mission, MissionState.Completed);
            RemoveMission(mission);
            SaveManager.Instance?.AutoSave();
            return;
        }


        if (npc == null)
        {

            Debug.LogWarning(
                "任务没有执行NPC"
            );

            return;

        }

        if (mission.ResultTier != MissionResultTier.Insufficient)
        {

            if (mission.ResultTier == MissionResultTier.Excellent) mission.ApplyExcellentRewardBonus();
            RecordMissionOutcome(npc, data, mission.ResultTier);

            if (!RewardManager.Instance.CanGiveReward(mission.Reward))
            {
                mission.WaitForReward();
                NPCManager.Instance.Recover(npc);
                RecordResult(mission, MissionState.AwaitingReward);
                Debug.LogWarning($"仓库容量不足，任务奖励等待领取: {data.name}");
                return;
            }

            Debug.Log(
                $"【{npc.Data.npcName}】成功完成任务：【{data.name}】"
            );

            ApplyFoundingSuccess(data, npc);
            mission.CompleteMission();

            if (data.missionType == MissionType.Combat)
                npc.AddCombatExperience(mission.ResultTier == MissionResultTier.Excellent ? 5 : 3);

            NPCManager.Instance.Recover(npc);

            RewardManager.Instance.GiveReward(
                npc,
                mission.Reward
            );
            RecordResult(mission, MissionState.Completed);
            TriggerMissionSource(data, npc, true);
            RemoveMission(mission);
            SaveManager.Instance?.AutoSave();

        }
        else
        {

            if (data.missionType == MissionType.Combat)
                npc.AddCombatExperience(1);
            RecordMissionOutcome(npc, data, MissionResultTier.Insufficient);

            Debug.Log(
                $"任务失败：【{npc.Data.npcName}】能力不足"
            );


            mission.FailMission();

        }

    }

    private void EvaluateThreatInvestigation(Mission mission)
    {
        NPCRuntime npc = mission.AssignedNPC;
        if (npc == null || !npc.Character.IsAlive)
        {
            mission.FailMission(false);
            return;
        }
        ActiveThreatState threat = ExternalThreatRules.GetState();
        if (threat == null || threat.status == ExternalThreatStatus.Resolved)
        {
            npc.Character.AddLifeRecord(TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay,
                "ThreatInvestigation", "调查青石村威胁时，威胁已被处理。", mission.Data.id);
            mission.CompleteMission();
            RecordResult(mission, MissionState.Completed);
            SaveManager.Instance?.AutoSave();
            return;
        }
        int gain = ExternalThreatRules.AddIntelligence(npc);
        npc.Character.AddLifeRecord(TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay,
            "ThreatInvestigation", $"调查青石村威胁，情报+{gain}", mission.Data.id);
        mission.CompleteMission();
        RecordResult(mission, MissionState.Completed);
        SaveManager.Instance?.AutoSave();
    }

    private void EvaluateMapMission(Mission mission)
    {
        NPCRuntime npc = mission.AssignedNPC;
        if (npc == null || !npc.Character.IsAlive || mission.ResultTier == MissionResultTier.Insufficient)
        {
            mission.FailMission(npc != null && npc.Character.IsAlive);
            return;
        }
        if (!WorldMapContentRules.CanStartAction(WorldMapSession.Current, WorldMapSession.Progress,
                mission.MapContext, out string reason))
        {
            Debug.LogWarning($"地图任务目标状态已变化：{reason}");
            mission.FailMission(false);
            return;
        }
        if (mission.ResultTier == MissionResultTier.Excellent) mission.ApplyExcellentRewardBonus();
        if (RewardManager.Instance == null || !RewardManager.Instance.CanGiveReward(mission.Reward))
        {
            mission.WaitForReward();
            NPCManager.Instance?.Recover(npc);
            RecordResult(mission, MissionState.AwaitingReward);
            return;
        }
        RewardManager.Instance.GiveReward(npc, mission.Reward);
        WorldMapContentRules.CompleteSuccessfulAction(WorldMapSession.Current, WorldMapSession.Progress,
            mission.MapContext, mission.ResultTier, TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay,
            out _);
        RecordMissionOutcome(npc, mission.Data, mission.ResultTier);
        mission.CompleteMission();
        NPCManager.Instance?.Recover(npc);
        RecordResult(mission, MissionState.Completed);
        EventManager.Instance?.TryTriggerSource(EventSource.Exploration, npc);
        SaveManager.Instance?.AutoSave();
        WorldMapSession.NotifyProgressChanged();
    }


    /// <summary>
    /// 删除已经结束的任务
    /// </summary>
    public void RemoveMission(
        Mission mission)
    {

        if (activeMissions.Contains(mission))
        {

            activeMissions.Remove(mission);

            if (mission.AssignedNPC != null &&
                mission.AssignedNPC.CurrentMission == mission &&
                mission.AssignedNPC.Character.IsAlive &&
                mission.AssignedNPC.State != NPCState.Injured)
            {
                NPCManager.Instance.Recover(mission.AssignedNPC);
            }

            Debug.Log(
                $"移除任务:{mission.Data.name}"
            );

        }

    }

    public bool CanTriggerMission(string missionId, NPCRuntime npc, out string reason)
    {
        MissionData data = GetMissionData(missionId);
        if (data == null) { reason = "任务不存在"; return false; }
        if (npc == null || !CanDispatchOrCancelAutonomous(npc)) { reason = "弟子当前无法执行任务"; return false; }
        if (PlayerManager.Instance == null || WarehouseManager.Instance == null) { reason = "资源系统尚未初始化"; return false; }
        if (!IsMissionVisible(data)) { reason = "当前宗门状态未开放该任务"; return false; }
        if (data.threatMissionKind == ThreatMissionKind.Investigation && ExternalThreatRules.IsInvestigationRunning())
        { reason = "已有弟子正在调查该威胁"; return false; }
        if (data.requiredFacilityLevel > PlayerManager.Instance.GetFacilityLevel(data.requiredFacility)) { reason = "设施等级不足"; return false; }
        if (!CanStartFoundingAction(data, npc, out reason)) return false;
        Func<Mission, bool> running = item => item.State == MissionState.Active || item.State == MissionState.WaitingNode;
        if (data.isFacilityAction)
        {
            if (activeMissions.Any(item => running(item) && item.Data.isFacilityAction && item.Data.requiredFacility == data.requiredFacility))
            { reason = "该设施正在使用"; return false; }
        }
        else if (data.isStoryAction)
        {
            if (activeMissions.Any(item => running(item) && item.Data.isStoryAction &&
                !string.IsNullOrEmpty(data.foundingTargetId) && item.Data.foundingTargetId == data.foundingTargetId))
            { reason = "该剧情行动已经在进行"; return false; }
        }
        if (!TryGetMissionCosts(data, out Dictionary<string, int> costs, out reason)) return false;
        foreach (var cost in costs)
            if (!WarehouseManager.Instance.HasItem(cost.Key, cost.Value)) { reason = "资源不足"; return false; }
        reason = null;
        return true;
    }

    public bool CanTriggerLaborMission(string missionId, out string reason)
    {
        MissionData data = GetMissionData(missionId);
        if (data == null) { reason = "任务不存在"; return false; }
        if (!IsLaborOnlyFoundingAction(data.foundingAction)) { reason = "该任务不是劳动力任务"; return false; }
        if (PlayerManager.Instance == null || WarehouseManager.Instance == null) { reason = "资源系统尚未初始化"; return false; }
        FoundingState state = PlayerManager.Instance.playerData.founding;
        if (!FoundingRules.HasReachedCave(state)) { reason = "立宗剧情尚未进入该阶段"; return false; }
        if (state.village == null || state.village.totalLabor - state.village.reservedLabor < data.laborCost)
        { reason = "可用劳动力不足"; return false; }
        if (activeMissions.Any(item => (item.State == MissionState.Active || item.State == MissionState.WaitingNode) &&
            item.Data.foundingAction == data.foundingAction))
        { reason = "同类劳动力任务已在进行"; return false; }
        if (!TryGetMissionCosts(data, out Dictionary<string, int> costs, out reason)) return false;
        foreach (var cost in costs)
            if (!WarehouseManager.Instance.HasItem(cost.Key, cost.Value)) { reason = "资源不足"; return false; }
        reason = null;
        return true;
    }

    private bool TryGetMissionCosts(MissionData data, out Dictionary<string, int> costs, out string reason)
    {
        costs = new Dictionary<string, int>();
        foreach (ItemReward cost in data.itemCosts ?? new List<ItemReward>())
        {
            if (cost == null || string.IsNullOrWhiteSpace(cost.itemId) || cost.count < 0 || !IsKnownItem(cost.itemId))
            { reason = "任务物品成本无效"; return false; }
            if (cost.count == 0) continue;
            long total = (costs.TryGetValue(cost.itemId, out int current) ? current : 0) + (long)cost.count;
            if (total > int.MaxValue) { reason = "任务物品成本过大"; return false; }
            costs[cost.itemId] = (int)total;
        }
        reason = null;
        return true;
    }

    private bool TrySpendMissionCosts(MissionData data, out string reason)
    {
        if (!TryGetMissionCosts(data, out Dictionary<string, int> costs, out reason)) return false;
        List<KeyValuePair<string, int>> removed = new List<KeyValuePair<string, int>>();
        foreach (var cost in costs)
        {
            if (WarehouseManager.Instance.RemoveItem(cost.Key, cost.Value)) { removed.Add(cost); continue; }
            foreach (var rollback in removed) WarehouseManager.Instance.AddItem(rollback.Key, rollback.Value);
            reason = "资源扣除失败";
            return false;
        }
        reason = null;
        return true;
    }

    private void RefundMissionCosts(MissionData data)
    {
        if (data == null) return;
        foreach (ItemReward cost in data.itemCosts ?? new List<ItemReward>())
            if (cost != null && cost.count > 0) WarehouseManager.Instance?.AddItem(cost.itemId, cost.count);
    }

    private bool CanStartFoundingAction(MissionData data, NPCRuntime npc, out string reason)
    {
        if (data.foundingAction == FoundingActionKind.None) { reason = null; return true; }
        FoundingState state = PlayerManager.Instance.playerData.founding;
        bool routeAction = data.foundingAction == FoundingActionKind.RouteAlchemy ||
                           data.foundingAction == FoundingActionKind.RouteForge ||
                           data.foundingAction == FoundingActionKind.RouteFormation;
        if (!FoundingRules.HasReachedCave(state))
        { reason = "立宗剧情尚未进入该阶段"; return false; }
        if ((data.foundingAction == FoundingActionKind.RepairFacility ||
             data.foundingAction == FoundingActionKind.BuildRouteFacility) &&
            npc.Realm < CultivationRealm.QiRefining)
        { reason = "只有修士可以执行该行动"; return false; }

        if (data.foundingAction == FoundingActionKind.RepairFacility)
        {
            if (!Enum.TryParse(data.foundingTargetId, out FacilityType facility)) { reason = "修复目标配置无效"; return false; }
            if (PlayerManager.Instance.GetFacilityLevel(facility) > 0) { reason = "该遗迹已经修复"; return false; }
        }
        if (data.foundingAction == FoundingActionKind.BuildRouteFacility)
        {
            FoundingTechniqueOption option = FoundingRules.GetTechniqueOption(state.selectedTechniqueId);
            if (option == null || option.buildMissionId != data.id) { reason = "该设施不属于当前传承路线"; return false; }
            if (state.inheritancePreparationProgress < FoundingRules.MaxUnderstanding) { reason = "传承整理尚未达到100%"; return false; }
            if (PlayerManager.Instance.GetFacilityLevel(option.unlockFacility) > 0) { reason = "路线设施已经建成"; return false; }
            if (state.village == null || state.village.totalLabor - state.village.reservedLabor < data.laborCost)
            { reason = "可用劳动力不足"; return false; }
        }
        reason = null;
        return true;
    }

    private void ApplyFoundingSuccess(MissionData data, NPCRuntime actor)
    {
        if (data == null || data.foundingAction == FoundingActionKind.None) return;
        switch (data.foundingAction)
        {
            case FoundingActionKind.RepairFacility:
                if (Enum.TryParse(data.foundingTargetId, out FacilityType repaired))
                    PlayerManager.Instance.SetFacilityLevelForStory(repaired, 1);
                break;
            case FoundingActionKind.VillagePreach:
                PlayerManager.Instance.AddVillageRelation(10, actor);
                break;
            case FoundingActionKind.VillageHelp:
                PlayerManager.Instance.AddVillageRelation(20, actor);
                ExternalThreatRules.RestoreLaborAfterVillageHelp();
                break;
            case FoundingActionKind.BuildRouteFacility:
                if (Enum.TryParse(data.foundingTargetId, out FacilityType built))
                    PlayerManager.Instance.SetFacilityLevelForStory(built, 1);
                PlayerManager.Instance.ReleaseLabor(data.laborCost);
                PlayerManager.Instance.EvaluateFoundingCompletion();
                break;
            case FoundingActionKind.LaborGather:
            case FoundingActionKind.LaborBuild:
            case FoundingActionKind.LaborCultivate:
                PlayerManager.Instance.ReleaseLabor(data.laborCost);
                break;
            case FoundingActionKind.RouteFormation:
                EventManager.Instance?.TryEnqueueEventById("founding_formation_action", actor);
                break;
        }
    }

    private static void TriggerMissionSource(MissionData data, NPCRuntime actor, bool completed)
    {
        if (EventManager.Instance == null || data == null) return;
        if (data.requiredFacility == FacilityType.SecretRealm && data.isFacilityAction)
            EventManager.Instance.TryTriggerSource(EventSource.SecretRealm, actor);
        else if (data.requiredFacility == FacilityType.AlchemyRoom && data.isFacilityAction)
            EventManager.Instance.TryTriggerSource(EventSource.Alchemy, actor);
        else
            EventManager.Instance.TryTriggerSource(completed ? EventSource.MissionComplete : EventSource.MissionStart, actor);
    }

    private static bool IsKnownItem(string itemId)
    {
        if (ItemDatabase.Instance != null) return ItemDatabase.Instance.GetItem(itemId) != null;
        foreach (TextAsset file in Resources.LoadAll<TextAsset>("Configs/Items"))
        {
            try
            {
                if (JsonConvert.DeserializeObject<ItemData>(file.text)?.itemId == itemId) return true;
            }
            catch (JsonException) { }
        }
        return false;
    }

    public IReadOnlyList<Mission> GetActiveMissions()
    {
        return activeMissions.AsReadOnly();
    }

    public bool TryClaimReward(Mission mission, out string reason)
    {
        if (mission == null || mission.State != MissionState.AwaitingReward) { reason = "任务没有待领取奖励"; return false; }
        if (!RewardManager.Instance.CanGiveReward(mission.Reward)) { reason = "仓库容量不足"; return false; }
        bool mapProgressChanged = mission.MapContext != null;
        if (mapProgressChanged)
        {
            if (!WorldMapContentRules.CanStartAction(WorldMapSession.Current, WorldMapSession.Progress,
                    mission.MapContext, out reason)) return false;
            NPCRuntime actor = mission.AssignedNPC;
            RewardManager.Instance.GiveReward(actor, mission.Reward);
            WorldMapContentRules.CompleteSuccessfulAction(WorldMapSession.Current, WorldMapSession.Progress,
                mission.MapContext, mission.ResultTier, TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay,
                out _);
            EventManager.Instance?.TryTriggerSource(EventSource.Exploration, actor);
        }
        else if (IsLaborOnlyFoundingAction(mission.Data.foundingAction))
        {
            ApplyFoundingSuccess(mission.Data, null);
            GiveSectReward(mission.Reward);
        }
        else
        {
            NPCRuntime npc = mission.AssignedNPC;
            ApplyFoundingSuccess(mission.Data, npc);
            RewardManager.Instance.GiveReward(npc, mission.Reward);
            if (mission.Data.missionType == MissionType.Combat)
                npc?.AddCombatExperience(mission.ResultTier == MissionResultTier.Excellent ? 5 : 3);
            TriggerMissionSource(mission.Data, npc, true);
        }
        mission.CompleteMission();
        RecordResult(mission, MissionState.Completed);
        SaveManager.Instance?.AutoSave();
        if (mapProgressChanged) WorldMapSession.NotifyProgressChanged();
        reason = null;
        return true;
    }

    public int CancelAwaitingMissionsForCharacter(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId)) return 0;
        List<Mission> cancelled = activeMissions.Where(mission => mission?.AssignedNPC?.CharacterId == characterId &&
            mission.State == MissionState.AwaitingReward).ToList();
        foreach (Mission mission in cancelled)
        {
            if (!mission.CancelAwaitingReward()) continue;
            RecordResult(mission, MissionState.Failed);
            RemoveMission(mission);
        }
        return cancelled.Count;
    }

    private void RegisterMapMissionTemplates()
    {
        RegisterMapTemplate(WorldMapContentRules.ExploreMissionId, "派遣探索", 2, 8, 8, 55);
        RegisterMapTemplate(WorldMapContentRules.InvestigateSpiritSpringMissionId, "调查灵泉", 2, 6, 10, 50);
        RegisterMapTemplate(WorldMapContentRules.DevelopSpiritSpringMissionId, "开发灵泉", 3, 8, 12, 60);
        RegisterMapTemplate(WorldMapContentRules.EstablishVillageRelationMissionId, "建立村庄关系", 3, 7, 11, 55);
        RegisterMapTemplate(WorldMapContentRules.DevelopSpiritMineMissionId, "开发灵矿", 3, 8, 12, 60);
        RegisterMapTemplate(WorldMapContentRules.DevelopResourceNodeMissionId, "开发资源节点", 3, 8, 12, 60);
        RegisterMapTemplate(WorldMapContentRules.BuildCaveResidenceOutpostMissionId, "建立洞府据点规则", 3, 9, 10, 62);
        RegisterMapTemplate(WorldMapContentRules.ClearBeastLairMissionId, "清理兽巢", 2, 11, 7, 65);
        RegisterMapTemplate(WorldMapContentRules.InvestigateRuinMissionId, "调查遗迹", 2, 6, 12, 58);
    }

    private void RegisterMapTemplate(string id, string name, int days,
        int requiredAttack, int requiredIntelligence, int requiredCombatPower)
    {
        missionTemplates[id] = new MissionData
        {
            id = id, name = name, description = name, missionType = MissionType.Exploration,
            generatedByMap = true,
            needDays = days, itemRewards = new List<ItemReward>(), nodes = new List<MissionNodeData>(),
            requiredAttack = requiredAttack, requiredIntelligence = requiredIntelligence,
            requiredCombatPower = requiredCombatPower, excellentScore = 135
        };
    }

    private static string TemplateIdFor(MapActionType action)
    {
        switch (action)
        {
            case MapActionType.Explore: return WorldMapContentRules.ExploreMissionId;
            case MapActionType.InvestigateSpiritSpring: return WorldMapContentRules.InvestigateSpiritSpringMissionId;
            case MapActionType.DevelopSpiritSpring: return WorldMapContentRules.DevelopSpiritSpringMissionId;
            case MapActionType.EstablishVillageRelation: return WorldMapContentRules.EstablishVillageRelationMissionId;
            case MapActionType.DevelopSpiritMine: return WorldMapContentRules.DevelopSpiritMineMissionId;
            case MapActionType.DevelopResourceNode: return WorldMapContentRules.DevelopResourceNodeMissionId;
            case MapActionType.BuildCaveResidenceOutpost: return WorldMapContentRules.BuildCaveResidenceOutpostMissionId;
            case MapActionType.ClearBeastLair: return WorldMapContentRules.ClearBeastLairMissionId;
            case MapActionType.InvestigateRuin: return WorldMapContentRules.InvestigateRuinMissionId;
            default: return null;
        }
    }

    private static void RecordMissionOutcome(NPCRuntime npc, MissionData data, MissionResultTier tier)
    {
        if (npc?.Character == null || data == null) return;
        string result = tier == MissionResultTier.Excellent ? "优秀" :
            tier == MissionResultTier.Insufficient ? "能力不足" : "达标";
        npc.Character.AddLifeRecord(TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay,
            "Mission", $"{result}：{data.name}", data.id);
        if (tier != MissionResultTier.Insufficient && data.techniqueUnderstandingReward > 0f)
            npc.AddTechniqueUnderstanding(data.techniqueUnderstandingReward);
        DiscipleMentalStateRules.ApplyMissionResult(npc, data.id, tier);
    }

    public static bool IsAutonomousMission(MissionData data)
    {
        return data != null && !data.isPlayerAssignable && !string.IsNullOrWhiteSpace(data.id) &&
            data.id.StartsWith("disciple_ai_", StringComparison.Ordinal);
    }

    public bool CanDispatchOrCancelAutonomous(NPCRuntime npc)
    {
        if (npc == null || !npc.Character.IsAlive) return false;
        if (npc.CanDispatch()) return true;
        Mission mission = npc.CurrentMission;
        return mission != null && mission.State == MissionState.Active && IsAutonomousMission(mission.Data) &&
            (mission.Data.nodes == null || mission.Data.nodes.Count == 0);
    }

    public bool TryCancelAutonomousMission(NPCRuntime npc, out string reason)
    {
        if (npc == null) { reason = "弟子不存在"; return false; }
        if (npc.CanDispatch()) { reason = null; return true; }
        Mission mission = npc.CurrentMission;
        if (mission == null || !mission.CancelAutonomousByPlayer())
        { reason = "当前行动不能无损中止"; return false; }
        npc.Character.AddLifeRecord(TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay,
            "Decision", $"玩家安排中止自由行动：{mission.Data.name}", mission.Data.id);
        SaveManager.Instance?.AutoSave();
        reason = null;
        return true;
    }

    private void RecordResult(Mission mission, MissionState state)
    {
        string tier = state == MissionState.Completed
            ? (mission.ResultTier == MissionResultTier.Excellent ? "优秀" : "达标")
            : state == MissionState.Failed ? "能力不足" : string.Empty;
        dailyResults.Add(new MissionDayResult
        {
            missionId = mission.Data.id,
            missionName = string.IsNullOrEmpty(tier) ? mission.Data.name : $"{mission.Data.name}（{tier}）",
            state = state
        });
    }

    public void NotifyMissionFailed(Mission mission)
    {
        if (mission?.Data != null && mission.Data.laborCost > 0)
            PlayerManager.Instance?.ReleaseLabor(mission.Data.laborCost);
        RecordResult(mission, MissionState.Failed);
        EventManager.Instance?.TryTriggerSource(EventSource.MissionFailed, mission.AssignedNPC);
    }

    public void NotifyVillageThreatCancellation(Mission mission)
    {
        if (mission?.Data == null) return;
        if (mission.Data.laborCost > 0) PlayerManager.Instance?.ReleaseLabor(mission.Data.laborCost);
        dailyResults.Add(new MissionDayResult
        {
            missionId = mission.Data.id,
            missionName = $"{mission.Data.name}（青石村受袭中止）",
            state = MissionState.Failed
        });
        TimeManager.Instance?.RecordThreatNotice($"青石村受袭中止任务：{mission.Data.name}");
    }

    public int CancelLaborMissionsUntilValid()
    {
        VillageState village = PlayerManager.Instance?.playerData?.founding?.village;
        if (village == null) return 0;
        int cancelled = 0;
        while (village.reservedLabor > village.totalLabor)
        {
            Mission target = activeMissions
                .Where(item => item?.Data != null && item.Data.laborCost > 0 &&
                    (item.State == MissionState.Active || item.State == MissionState.WaitingNode))
                .OrderByDescending(item => item.RemainingDays)
                .ThenBy(item => item.Data.id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (target == null) break;
            target.CancelForVillageThreat();
            cancelled++;
        }
        return cancelled;
    }

    public List<MissionDayResult> ConsumeDailyResults()
    {
        List<MissionDayResult> result = new List<MissionDayResult>(dailyResults);
        dailyResults.Clear();
        return result;
    }

    public void RestoreMissions(IEnumerable<MissionSaveData> savedMissions)
    {
        activeMissions.Clear();
        foreach (MissionSaveData saved in savedMissions ?? Enumerable.Empty<MissionSaveData>())
        {
            if (saved == null || string.IsNullOrWhiteSpace(saved.missionId))
            {
                Debug.LogWarning("跳过缺少任务ID的存档任务");
                continue;
            }
            MissionData data = GetMissionData(saved.missionId);
            NPCRuntime npc = NPCManager.Instance?.GetRuntime(saved.assignedCharacterId);
            bool laborOnly = data != null && IsLaborOnlyFoundingAction(data.foundingAction);
            if (data == null || (!laborOnly && (npc == null || !npc.Character.IsAlive)))
            {
                Debug.LogWarning($"跳过无法恢复的任务: {saved.missionId}");
                if (npc != null && npc.State == NPCState.Busy && npc.CurrentMission == null) npc.SetState(NPCState.Idle);
                continue;
            }
            activeMissions.Add(new Mission(data, saved, npc));
        }
        if (NPCManager.Instance == null) return;
        foreach (NPCRuntime npc in NPCManager.Instance.GetLivingNPC()
            .Where(item => item.State == NPCState.Busy && item.CurrentMission == null))
        {
            Debug.LogWarning($"清理没有可恢复任务的忙碌弟子: {npc.CharacterId}");
            npc.SetState(NPCState.Idle);
        }
    }


    /// <summary>
    /// 每天推进任务
    /// </summary>
    private void OnDayPassed(int currentDay)
    {
        //复制列表
        //避免任务完成后删除导致foreach异常
        List<Mission> missions =
            new List<Mission>(
                activeMissions
            );



        foreach (Mission mission in missions)
        {
            if (mission.Data.isFacilityAction && mission.State == MissionState.Active)
            {
                if (mission.Data.requiredFacility == FacilityType.SecretRealm)
                    EventManager.Instance?.TryTriggerSource(EventSource.SecretRealm, mission.AssignedNPC);
                else if (mission.Data.requiredFacility == FacilityType.AlchemyRoom)
                    EventManager.Instance?.TryTriggerSource(EventSource.Alchemy, mission.AssignedNPC);
            }
            mission.PassOneDay();

        }

        RefreshDailyCandidates(currentDay);

    }

    public IReadOnlyList<string> GetDailyMissionCandidateIds() => dailyMissionCandidateIds.AsReadOnly();

    public void RefreshDailyCandidates(int day, bool force = false)
    {
        dailyMissionCandidateIds.Clear();
        dailyMissionCandidateIds.AddRange(missionTemplates.Values
            .Where(data => !data.isStoryAction && !data.isFacilityAction && IsMissionVisible(data))
            .OrderBy(data => data.id)
            .Select(data => data.id));
        missionCandidateDay = day;
    }

    public void RestoreDailyCandidates(int day, IEnumerable<string> ids)
    {
        RefreshDailyCandidates(day, true);
    }

    public IReadOnlyList<MissionData> GetVisibleMissions()
    {
        return missionTemplates.Values.Where(IsMissionVisible).OrderBy(data => data.missionType).ThenBy(data => data.id).ToList();
    }

    public bool IsMissionVisible(MissionData data)
    {
        if (data == null || !data.isPlayerAssignable || data.missionType == MissionType.WorldEvent) return false;
        if (data.generatedByMap) return false;
        if (data.threatMissionKind == ThreatMissionKind.Investigation)
        {
            ActiveThreatState threat = ExternalThreatRules.GetState();
            return threat != null && threat.status == ExternalThreatStatus.Active && threat.intelligence < 100;
        }
        FoundingState founding = PlayerManager.Instance?.playerData?.founding;
        if (data.isStoryAction)
        {
            if (!GameFlowPermission.HasReachedCave(founding)) return false;
            if (data.foundingAction == FoundingActionKind.RepairFacility &&
                Enum.TryParse(data.foundingTargetId, out FacilityType repaired) &&
                PlayerManager.Instance.GetFacilityLevel(repaired) > 0) return false;
            if (data.foundingAction == FoundingActionKind.BuildRouteFacility ||
                data.foundingAction == FoundingActionKind.RouteAlchemy ||
                data.foundingAction == FoundingActionKind.RouteForge ||
                data.foundingAction == FoundingActionKind.RouteFormation)
            {
                FoundingTechniqueOption option = FoundingRules.GetTechniqueOption(founding.selectedTechniqueId);
                return option != null && (option.buildMissionId == data.id || option.actionMissionId == data.id);
            }
            return true;
        }

        if (!GameFlowPermission.IsSectEstablished(founding)) return false;
        int reputation = PlayerManager.Instance == null ? 0 : PlayerManager.Instance.playerData.reputation;
        return data.missionRank <= FacilityRules.MaxMissionRankForReputation(reputation) &&
               data.requiredFacilityLevel <= (PlayerManager.Instance == null ? 0 : PlayerManager.Instance.GetFacilityLevel(data.requiredFacility));
    }

    public int MissionCandidateDay => missionCandidateDay;


    /// <summary>
    /// 获取任务模板
    ///
    /// 给UI显示使用
    /// </summary>
    public MissionData GetMissionData(
        string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        if (
            missionTemplates.TryGetValue(
                id,
                out MissionData data
            ))
        {
            return data;
        }


        return null;

    }


    public void TestCreate()
    {
        Mission m = CreateMission("combat_001");

        Debug.Log(
            m == null ? "失败" : "成功创建"
        );
    }


    /// <summary>
    /// 获取所有任务模板
    ///
    /// 给任务列表使用
    /// </summary>
    public List<MissionData> GetMissionPool()
    {

        return new List<MissionData>(
            missionTemplates.Values
        );

    }

    private static bool IsLaborOnlyFoundingAction(FoundingActionKind action)
    {
        return action == FoundingActionKind.LaborGather ||
               action == FoundingActionKind.LaborBuild ||
               action == FoundingActionKind.LaborCultivate;
    }

    private static void GiveSectReward(Reward reward)
    {
        if (reward == null) return;
        foreach (ItemReward item in reward.Items ?? new List<ItemReward>())
            if (item != null && item.count > 0) WarehouseManager.Instance?.AddItem(item.itemId, item.count);
    }




    private void OnDisable()
    {

        if (TimeManager.Instance != null)
        {

            TimeManager.Instance.OnDayPassed -= OnDayPassed;

        }

    }

}
