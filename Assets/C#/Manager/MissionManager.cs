using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using System.Linq;

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
    public string LastExplorationNotice { get; private set; }
    private string lastExplorationNoticeRegionId;


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
        mission.StartMission(npc);
        if (mission.Data.explorationKind == ExplorationMissionKind.None)
            EventManager.Instance?.TryTriggerSource(mission.Data.isFacilityAction
            ? (mission.Data.requiredFacility == FacilityType.SecretRealm ? EventSource.SecretRealm : EventSource.Alchemy)
            : EventSource.MissionStart, npc);

        activeMissions.Add(mission); 
        if (mission.Data.explorationKind == ExplorationMissionKind.Ongoing)
            RetryRegionDiscoveryEvent(mission.Data.explorationRegionId, npc);
        Debug.Log(
        $"创建任务实例:{mission.Data.name}"
    );
        Debug.Log(
            $"开始任务:{mission.Data.name}"
        );

    }


    /// <summary>
    /// 任务完成判断
    /// </summary>
    public void EvaluateMission(Mission mission)
    {
        if (mission.Data.explorationKind == ExplorationMissionKind.Ongoing)
        {
            EvaluateOngoingExploration(mission);
            return;
        }
        if (mission.Data.explorationKind == ExplorationMissionKind.Survey ||
            mission.Data.explorationKind == ExplorationMissionKind.Progress)
        {
            EvaluateExploration(mission);
            return;
        }
        NPCRuntime npc =
            mission.AssignedNPC;

        MissionData data =
            mission.Data;


        if (npc == null)
        {

            Debug.LogWarning(
                "任务没有执行NPC"
            );

            return;

        }

        bool attackPass =
            npc.Attack >= data.requiredAttack;

        bool intelligencePass =
            npc.Intelligence >= data.requiredIntelligence;

        if (attackPass && intelligencePass)
        {

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

            mission.CompleteMission();

            NPCManager.Instance.Recover(npc);

            RewardManager.Instance.GiveReward(
                npc,
                mission.Reward
            );
            RecordResult(mission, MissionState.Completed);
            EventManager.Instance?.TryTriggerSource(data.isFacilityAction
                ? (data.requiredFacility == FacilityType.SecretRealm ? EventSource.SecretRealm : EventSource.Alchemy)
                : EventSource.MissionComplete, npc);
            RemoveMission(mission);

        }
        else
        {

            Debug.Log(
                $"任务失败：【{npc.Data.npcName}】能力不足"
            );


            mission.FailMission();

        }

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

    private void EvaluateExploration(Mission mission)
    {
        NPCRuntime npc = mission.AssignedNPC;
        bool succeeded = false;
        if (mission.Data.explorationKind == ExplorationMissionKind.Survey)
        {
            succeeded = ExplorationRules.DiscoverNextRegion() != null;
        }
        else if (ExplorationRules.TryAdvance(mission.Data.explorationRegionId, out ExplorationRegionState state))
        {
            succeeded = true;
            if (state.stage > 0) RetryRegionDiscoveryEvent(state.regionId, npc);
        }

        if (!succeeded)
        {
            Debug.LogWarning($"探索状态已变化，无法结算任务: {mission.Data.name}");
            mission.FailMission(false);
            return;
        }

        mission.CompleteMission();
        RecordResult(mission, MissionState.Completed);
        SaveManager.Instance?.AutoSave();
    }

    private void EvaluateOngoingExploration(Mission mission)
    {
        if (mission.State != MissionState.Active || mission.RemainingDays > 0) return;
        if (RewardManager.Instance == null || !RewardManager.Instance.CanGiveReward(mission.Reward))
        {
            Debug.LogWarning($"仓库容量不足，持续探索暂停结算: {mission.Data.name}");
            return;
        }
        RewardManager.Instance.GiveReward(mission.AssignedNPC, mission.Reward);
        RetryRegionDiscoveryEvent(mission.Data.explorationRegionId, mission.AssignedNPC);
        mission.RestartCycle();
        SaveManager.Instance?.AutoSave();
    }

    private bool RetryRegionDiscoveryEvent(string regionId, NPCRuntime actor)
    {
        ExplorationRegionDefinition region = ExplorationRules.GetRegion(regionId);
        if (region == null || string.IsNullOrEmpty(region.firstProgressEventId)) return true;
        bool enqueued = EventManager.Instance != null && EventManager.Instance.TryEnqueueEventById(region.firstProgressEventId, actor);
        if (enqueued)
        {
            if (lastExplorationNoticeRegionId == regionId)
            {
                LastExplorationNotice = null;
                lastExplorationNoticeRegionId = null;
            }
        }
        else
        {
            LastExplorationNotice = $"{region.name}的探索发现暂未进入事件收件箱，将在后续区域行动时重试。";
            lastExplorationNoticeRegionId = regionId;
        }
        return enqueued;
    }

    public bool CanTriggerMission(string missionId, NPCRuntime npc, out string reason)
    {
        MissionData data = GetMissionData(missionId);
        if (data == null) { reason = "任务不存在"; return false; }
        if (npc == null || !npc.CanDispatch()) { reason = "弟子当前无法执行任务"; return false; }
        if (PlayerManager.Instance == null || WarehouseManager.Instance == null) { reason = "资源系统尚未初始化"; return false; }
        int hallLevel = PlayerManager.Instance.GetFacilityLevel(FacilityType.MissionHall);
        if (data.missionRank > hallLevel || data.requiredMissionHallLevel > hallLevel) { reason = "任务堂等级不足"; return false; }
        if (data.requiredFacilityLevel > PlayerManager.Instance.GetFacilityLevel(data.requiredFacility)) { reason = "设施等级不足"; return false; }
        if (data.explorationKind == ExplorationMissionKind.Survey && !ExplorationRules.HasUndiscoveredRegion())
        { reason = "所有预设区域均已发现"; return false; }
        if (data.explorationKind == ExplorationMissionKind.Progress)
        {
            ExplorationRegionState state = ExplorationRules.GetState(data.explorationRegionId);
            if (state == null) { reason = "区域尚未发现"; return false; }
            if (state.stage >= ExplorationRules.MaxStage) { reason = "区域探索已经完成"; return false; }
        }
        if (data.explorationKind == ExplorationMissionKind.Ongoing)
        {
            ExplorationRegionState state = ExplorationRules.GetState(data.explorationRegionId);
            if (state == null || state.stage < ExplorationRules.MaxStage) { reason = "尚未发现区域的特殊存在"; return false; }
        }
        if (data.explorationKind == ExplorationMissionKind.None && !data.isFacilityAction && !dailyMissionCandidateIds.Contains(missionId))
        { reason = "该任务不在今日候选中"; return false; }

        Func<Mission, bool> running = item => item.State == MissionState.Active || item.State == MissionState.WaitingNode;
        if (data.explorationKind == ExplorationMissionKind.Survey)
        {
            if (activeMissions.Any(item => running(item) && item.Data.explorationKind == ExplorationMissionKind.Survey))
            { reason = "已有未知区域勘察正在进行"; return false; }
        }
        else if (data.explorationKind == ExplorationMissionKind.Progress)
        {
            if (activeMissions.Any(item => running(item) && item.Data.explorationKind == ExplorationMissionKind.Progress &&
                item.Data.explorationRegionId == data.explorationRegionId))
            { reason = "该区域已有弟子正在探索"; return false; }
        }
        else if (data.explorationKind == ExplorationMissionKind.Ongoing)
        {
            if (activeMissions.Any(item => running(item) && item.Data.explorationKind == ExplorationMissionKind.Ongoing &&
                item.Data.explorationRegionId == data.explorationRegionId))
            { reason = "该区域已有弟子驻守"; return false; }
        }
        else if (data.isFacilityAction)
        {
            if (activeMissions.Any(item => running(item) && item.Data.isFacilityAction && item.Data.requiredFacility == data.requiredFacility))
            { reason = "该设施正在使用"; return false; }
        }
        else if (activeMissions.Count(item => running(item) && !item.Data.isFacilityAction && missionTemplates.ContainsKey(item.Data.id)) >= FacilityRules.MissionConcurrency(hallLevel))
        { reason = "任务堂并行槽已满"; return false; }

        if (!TryGetMissionCosts(data, out Dictionary<string, int> costs, out reason)) return false;
        if (PlayerManager.Instance.playerData.gold < data.goldCost) { reason = "灵材不足"; return false; }
        foreach (var cost in costs)
            if (WarehouseManager.Instance.GetItemCount(cost.Key) < cost.Value) { reason = "材料不足"; return false; }
        reason = null;
        return true;
    }

    private bool TryGetMissionCosts(MissionData data, out Dictionary<string, int> costs, out string reason)
    {
        costs = new Dictionary<string, int>();
        if (data.goldCost < 0) { reason = "任务灵材成本无效"; return false; }
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
        if (!PlayerManager.Instance.SpendGold(data.goldCost)) { reason = "灵材不足"; return false; }
        List<KeyValuePair<string, int>> removed = new List<KeyValuePair<string, int>>();
        foreach (var cost in costs)
        {
            if (WarehouseManager.Instance.RemoveItem(cost.Key, cost.Value)) { removed.Add(cost); continue; }
            PlayerManager.Instance.AddGold(data.goldCost);
            foreach (var rollback in removed) WarehouseManager.Instance.AddItem(rollback.Key, rollback.Value);
            reason = "材料扣除失败";
            return false;
        }
        int basicMaterialCost = costs.TryGetValue(FacilityRules.BasicMaterialId, out int material) ? material : 0;
        TimeManager.Instance?.RecordPreAdvanceResourceChange(-data.goldCost, -basicMaterialCost);
        reason = null;
        return true;
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

    public bool TryRecallExplorationMission(string regionId, out string reason)
    {
        Mission mission = activeMissions.FirstOrDefault(item => item.Data.explorationKind == ExplorationMissionKind.Ongoing &&
            item.Data.explorationRegionId == regionId && item.State == MissionState.Active);
        if (mission == null) { reason = "该区域没有驻守弟子"; return false; }
        mission.CompleteMission();
        reason = null;
        SaveManager.Instance?.AutoSave();
        return true;
    }

    public bool TryClaimReward(Mission mission, out string reason)
    {
        if (mission == null || mission.State != MissionState.AwaitingReward) { reason = "任务没有待领取奖励"; return false; }
        if (!RewardManager.Instance.CanGiveReward(mission.Reward)) { reason = "仓库容量不足"; return false; }
        RewardManager.Instance.GiveReward(mission.AssignedNPC, mission.Reward);
        mission.CompleteMission();
        RecordResult(mission, MissionState.Completed);
        reason = null;
        return true;
    }

    private void RecordResult(Mission mission, MissionState state)
    {
        dailyResults.Add(new MissionDayResult { missionId = mission.Data.id, missionName = mission.Data.name, state = state });
    }

    public void NotifyMissionFailed(Mission mission)
    {
        RecordResult(mission, MissionState.Failed);
        EventManager.Instance?.TryTriggerSource(EventSource.MissionFailed, mission.AssignedNPC);
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
            MissionData data = GetMissionData(saved.missionId);
            NPCRuntime npc = NPCManager.Instance?.GetRuntime(saved.assignedCharacterId);
            if (data == null || npc == null || !npc.Character.IsAlive)
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
            if (mission.Data.isFacilityAction && mission.Data.explorationKind == ExplorationMissionKind.None && mission.State == MissionState.Active)
                EventManager.Instance?.TryTriggerSource(mission.Data.requiredFacility == FacilityType.SecretRealm
                    ? EventSource.SecretRealm : EventSource.Alchemy, mission.AssignedNPC);
            mission.PassOneDay();

        }

        RefreshDailyCandidates(currentDay);

    }

    public IReadOnlyList<string> GetDailyMissionCandidateIds() => dailyMissionCandidateIds.AsReadOnly();

    public void RefreshDailyCandidates(int day, bool force = false)
    {
        if (!force && missionCandidateDay == day && dailyMissionCandidateIds.Count > 0) return;
        int hallLevel = PlayerManager.Instance == null ? 1 : PlayerManager.Instance.GetFacilityLevel(FacilityType.MissionHall);
        List<MissionData> pool = missionTemplates.Values
            .Where(data => !data.isFacilityAction && data.missionType != MissionType.WorldEvent &&
                           data.missionRank <= hallLevel && data.requiredMissionHallLevel <= hallLevel &&
                           data.requiredFacilityLevel <= (PlayerManager.Instance == null ? 1 : PlayerManager.Instance.GetFacilityLevel(data.requiredFacility)))
            .OrderBy(data => data.id).ToList();
        int seed = (EventManager.Instance == null ? 48621 : EventManager.Instance.RandomSeed) ^ day;
        System.Random random = new System.Random(seed);
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int swap = random.Next(i + 1);
            MissionData value = pool[i]; pool[i] = pool[swap]; pool[swap] = value;
        }
        dailyMissionCandidateIds.Clear();
        dailyMissionCandidateIds.AddRange(pool.Take(FacilityRules.MissionCandidateCount(hallLevel)).Select(data => data.id));
        missionCandidateDay = day;
    }

    public void RestoreDailyCandidates(int day, IEnumerable<string> ids)
    {
        missionCandidateDay = day;
        dailyMissionCandidateIds.Clear();
        dailyMissionCandidateIds.AddRange((ids ?? Enumerable.Empty<string>()).Where(id => missionTemplates.ContainsKey(id)));
        if (dailyMissionCandidateIds.Count == 0) RefreshDailyCandidates(day, true);
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




    private void OnDisable()
    {

        if (TimeManager.Instance != null)
        {

            TimeManager.Instance.OnDayPassed -= OnDayPassed;

        }

    }

}
