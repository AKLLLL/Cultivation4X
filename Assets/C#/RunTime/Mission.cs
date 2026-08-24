using System.Linq;
using UnityEngine;

/// <summary>
/// 单个任务运行时实例
/// 保存运行时状态
/// </summary>
public class Mission
{

    public NPCRuntime AssignedNPC { get; private set; }

    // 静态配置
    public MissionData Data { get; private set; }

    // 当前状态
    public MissionState State { get; private set; }
    // 剩余天数
    public int RemainingDays { get; private set; }
    // 已经过天数
    public int ElapsedDays { get; private set; }
    // 当前节点索引
    public int CurrentNodeIndex { get; private set; }
    public MissionNodeData CurrentNode { get; private set; }
    // 当前任务最终奖励。
    // 任务运行过程中可以不断修改。
    private Reward reward;
    private readonly int facilityLevel;
    public int CapabilityScore { get; private set; } = 100;
    public MissionResultTier ResultTier { get; private set; } = MissionResultTier.Qualified;
    private bool hasCapabilitySnapshot;
    public Cultivation4X.WorldMap.MapMissionContext MapContext { get; private set; }
    /// <summary>
    /// 节点选项效果执行失败的提示（例如物品不足）；失败时任务停留在 WaitingNode 并重新弹出面板。
    /// </summary>
    public string NodeFailureReason { get; private set; }

   
    public Mission(MissionData data, int facilityLevel = 1)
    {
        Data = data;
        this.facilityLevel = facilityLevel;
        // 创建奖励
        reward = CreateReward(data);
        State = MissionState.NotStarted;
        CurrentNodeIndex = 0;
        ElapsedDays = 0;
    }
 
    // 获取奖励对象。
    // 用于任务节点修改奖励。
    public Reward Reward
    {
        get
        {
            return reward;
        }
    }
    // 根据 MissionData 创建奖励对象。
    private Reward CreateReward(MissionData data)
    {
        Reward reward = new Reward();

        // 物品奖励
        if (data.itemRewards != null)
        {
            foreach (ItemReward item in data.itemRewards)
            {
                reward.Items.Add(
                    new ItemReward()
                    {
                        itemId = item.itemId,
                        count = item.count
                    });
            }
        }
        if (data.isFacilityAction && data.usesFacilityLevelScaling &&
            FacilityRules.UsesLevelScaledAction(data.requiredFacility) && reward.Items.Count == 1)
            reward.Items[0].count = FacilityRules.ActionOutput(data.requiredFacility, facilityLevel);
        
        return reward;
    }
    /// <summary>
    /// 开始任务
    /// </summary>
    public void StartMission(NPCRuntime npc)
    {
        if (State != MissionState.NotStarted)
            return;
        AssignedNPC = npc;
        CaptureCapabilitySnapshot();
        State = MissionState.Active;
        // 从配置读取耗时
        RemainingDays = Data.isFacilityAction && Data.usesFacilityLevelScaling &&
            FacilityRules.UsesLevelScaledAction(Data.requiredFacility)
            ? FacilityRules.ActionDays(Data.requiredFacility, facilityLevel)
            : Data.needDays;
        // NPC进入任务状态
        NPCManager.Instance.StartMission(npc, this);
        Debug.Log($"NPC {npc.Data.npcName} 已经进入忙碌状态！");
        Debug.Log($"任务开始：{Data.name}，需要 {RemainingDays} 天");
    }
    /// <summary>
    /// 推进一天
    /// </summary>
    public void StartLaborMission()
    {
        if (State != MissionState.NotStarted)
            return;
        AssignedNPC = null;
        State = MissionState.Active;
        RemainingDays = Data.needDays;
        Debug.Log($"Labor mission started: {Data.name}, days {RemainingDays}");
    }

    public void PassOneDay()
    {
        
        if (State != MissionState.Active)
            return;
        //经过一天

        ElapsedDays++;

        CheckNode();
        RemainingDays--;
        // Debug.Log($"{Data.name} 剩余 {RemainingDays} 天");

        if (RemainingDays <= 0)
        {
            if (State == MissionState.Active)
            {
                MissionManager.Instance.EvaluateMission(this);
            }
        }
        
    }
    /// <summary>
    /// 检查任务节点
    /// </summary>
    private void CheckNode()
    {
        if (Data.nodes == null)
            return;

        if (CurrentNodeIndex >= Data.nodes.Count)
            return;

        MissionNodeData node =
            Data.nodes[CurrentNodeIndex];
        //目前只做按天触发
        if (node.triggerType == "Day")
        {
            if (ElapsedDays >= node.triggerValue)
            {
                TriggerNode(node);
            }
        }
    }
    /// <summary>
     /// 触发节点
     /// </summary>
    private void TriggerNode(MissionNodeData node){
        CurrentNode = node;

        Debug.Log($"任务节点：{Data.name} / {node.title} / 执行弟子：{AssignedNPC?.Data.npcName ?? "未知"}");
        Debug.Log(node.description);

        //暂停任务流程

        //等待玩家选择

        State = MissionState.WaitingNode;
        //通知任务管理器
        MissionManager.Instance.OnMissionNodeTriggered(this);
        EventManager.Instance?.TryTriggerSource(EventSource.MissionNode, AssignedNPC);


    }
    /// <summary>
    /// 玩家选择任务节点选项
    /// </summary>
    public void SelectOption(int index)
    {
        if (State != MissionState.WaitingNode)
            return;

        if (CurrentNode == null)
            return;

        if (index < 0 ||
            index >= CurrentNode.options.Count)
            return;

        MissionOptionData option =
            CurrentNode.options[index];
        Debug.Log(
            $"选择：{option.text}"
         );
        if (!CheckRequirement(option))
        {
            Debug.Log(
            $"选择失败：{option.text} 条件不足"
            );
            FailMission();
            return;
        }

        if (!PreflightEffects(option))
        {
            // 效果预检失败：不执行任何效果，任务保持等待并重新弹出节点面板展示原因。
            MissionManager.Instance?.OnMissionNodeTriggered(this);
            return;
        }

        if (!ApplyEffects(option))
        {
            // 效果执行失败（如物品不足）：不结算任务、不关闭选择，重新弹出节点面板并展示原因。
            MissionManager.Instance?.OnMissionNodeTriggered(this);
            return;
        }

        ContinueMission();


    }
    // 检查选项条件
    private bool CheckRequirement(
    MissionOptionData option)
    {

        if (option.requirementType == "None")
            return true;


        if (AssignedNPC == null)
            return false;



        switch (option.requirementType)
        {

            case "Attack":

                return AssignedNPC.Attack
                    >= option.requirementValue;


            case "Intelligence":

                return AssignedNPC.Intelligence
                    >= option.requirementValue;


        }


        return true;

    }
    // 效果预检；用仓库物品投影按效果顺序模拟 RemoveItem/AddItem，不修改真实数据。
    private bool PreflightEffects(
    MissionOptionData option)
    {
        if (option.effects == null)
            return true;

        WarehouseManager warehouse = WarehouseManager.Instance;
        if (warehouse == null)
        {
            foreach (MissionEffectData effect in option.effects)
            {
                if (effect == null)
                {
                    NodeFailureReason = "节点效果配置无效";
                    return false;
                }
                if (effect.type == "RemoveItem" || effect.type == "AddItem")
                {
                    NodeFailureReason = "仓库系统尚未初始化，无法执行节点效果";
                    return false;
                }
            }
            return true;
        }

        System.Collections.Generic.Dictionary<string, int> projection =
            new System.Collections.Generic.Dictionary<string, int>();
        foreach (ItemStack item in warehouse.warehouseData == null
            ? new System.Collections.Generic.List<ItemStack>()
            : warehouse.warehouseData.items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId) || item.count <= 0)
                continue;
            projection.TryGetValue(item.itemId, out int current);
            projection[item.itemId] = current + item.count;
        }

        foreach (MissionEffectData effect in option.effects)
        {
            if (effect == null)
            {
                NodeFailureReason = "节点效果配置无效";
                return false;
            }

            if (effect.type == "RemoveItem")
            {
                if (string.IsNullOrWhiteSpace(effect.itemId) || effect.count <= 0 ||
                    !projection.TryGetValue(effect.itemId, out int available) || available < effect.count)
                {
                    NodeFailureReason = $"材料不足，无法执行：{effect.itemId}";
                    return false;
                }
                available -= effect.count;
                if (available <= 0) projection.Remove(effect.itemId);
                else projection[effect.itemId] = available;
            }
            else if (effect.type == "AddItem")
            {
                if (string.IsNullOrWhiteSpace(effect.itemId) || effect.count <= 0)
                {
                    NodeFailureReason = $"仓库无法添加物品：{effect.itemId} x {effect.count}";
                    return false;
                }
                if (ItemDatabase.Instance != null && ItemDatabase.Instance.GetItem(effect.itemId) == null)
                {
                    NodeFailureReason = $"仓库无法添加未知物品：{effect.itemId}";
                    return false;
                }
                projection.TryGetValue(effect.itemId, out int available);
                if (available <= 0)
                {
                    int level = PlayerManager.Instance == null ? 1 :
                        PlayerManager.Instance.GetFacilityLevel(FacilityType.Warehouse);
                    if (!WarehouseManager.IsCapacityExemptItem(effect.itemId) &&
                        projection.Count(item => !WarehouseManager.IsCapacityExemptItem(item.Key)) >= FacilityRules.WarehouseSlots(level))
                    {
                        NodeFailureReason = $"仓库容量不足，无法添加物品：{effect.itemId}";
                        return false;
                    }
                }
                projection[effect.itemId] = available + effect.count;
            }
        }

        return true;
    }
    //效果执行；返回是否全部成功。
    private bool ApplyEffects(
    MissionOptionData option)
    {
        if (option.effects == null)
            return true;
        foreach (var effect in option.effects)
        {

            switch (effect.type)
            {

                //获得物品
                case "AddItem":

                    if (!WarehouseManager.Instance.TryAddItem(
                        effect.itemId,
                        effect.count
                    ))
                    {
                        Debug.LogWarning($"任务效果失败，无法添加物品: {effect.itemId} x {effect.count}");
                        NodeFailureReason = $"仓库无法添加物品：{effect.itemId} x {effect.count}";
                        return false;
                    }

                    Debug.Log(
                    $"获得物品:{effect.itemId} x {effect.count}"
                    );

                    break;

                //消耗物品
                case "RemoveItem":

                    bool removed = WarehouseManager.Instance.RemoveItem(
                        effect.itemId,
                        effect.count
                    );

                    if (!removed)
                    {
                        Debug.LogWarning($"任务效果失败，物品不足: {effect.itemId}");
                        NodeFailureReason = $"材料不足，无法执行：{effect.itemId}";
                        return false;
                    }

                    Debug.Log(
                    $"消耗物品:{effect.itemId} x {effect.count}"
                    );

                    break;

                //增加任务时间
                case "Delay":

                    RemainingDays += effect.value;

                    Debug.Log(
                    $"任务增加时间:{effect.value}天"
                    );

                    break;

                //NPC受伤
                case "Injury":

                    NPCManager.Instance.Injured(
                        AssignedNPC,
                        effect.value
                    );

                    break;

                //触发事件
                case "TriggerEvent":

                    RandomEventManager.Instance.TriggerEvent(
                        effect.eventId,
                        AssignedNPC
                    );

                    break;
            }

        }

        return true;
    }
   
    //节点选择后继续任务
    private void ContinueMission()
    {

        NodeFailureReason = null;
        CurrentNodeIndex++;


        State = MissionState.Active;


        CurrentNode = null;
        CheckNode();

        if (State == MissionState.Active && RemainingDays <= 0)
        {
            MissionManager.Instance.EvaluateMission(this);
            return;
        }

    }
    /// <summary>
    /// 完成任务
    /// </summary>
    public void CompleteMission()
    {
        if (
        State != MissionState.Active &&
        State != MissionState.WaitingNode &&
        State != MissionState.AwaitingReward
    )
            return;
        
        State = MissionState.Completed;

        Debug.Log($"任务完成：{Data.name}");

        //RewardManager.Instance.GiveReward(AssignedNPC, reward);
        MissionManager.Instance.RemoveMission(this);

        if (AssignedNPC != null)
        {
        
            Debug.Log($"NPC {AssignedNPC.Data.npcName} 任务完成，恢复空闲！");
        }

        CurrentNode = null;
    }
    //任务失败
    public void FailMission(bool applyInjury = true)
    {
        if (
         State != MissionState.Active &&
         State != MissionState.WaitingNode
         )
            return;

        State = MissionState.Failed;

        if (AssignedNPC != null && applyInjury)
        {
            NPCManager.Instance.Injured(
                AssignedNPC,
                FacilityRules.FailureInjuryDays(PlayerManager.Instance == null ? 0 :
                    PlayerManager.Instance.GetFacilityLevel(FacilityType.ProtectionArray))
            );

            Debug.Log(
                $"NPC {AssignedNPC.Data.npcName} 任务失败，受伤3天！"
            );
        }

        Debug.Log($"任务失败：{Data.name}");
        MissionManager.Instance?.NotifyMissionFailed(this);
        MissionManager.Instance?.RemoveMission(this);
    }

    public void CancelForVillageThreat()
    {
        if (State != MissionState.Active && State != MissionState.WaitingNode) return;
        State = MissionState.Failed;
        CurrentNode = null;
        MissionManager.Instance?.NotifyVillageThreatCancellation(this);
        MissionManager.Instance?.RemoveMission(this);
    }

    public void WaitForReward()
    {
        if (State != MissionState.Active && State != MissionState.WaitingNode) return;
        State = MissionState.AwaitingReward;
        CurrentNode = null;
    }

    public Mission(MissionData data, MissionSaveData saved, NPCRuntime npc)
    {
        Data = data;
        facilityLevel = 1;
        reward = saved.reward ?? CreateReward(data);
        State = saved.state;
        RemainingDays = saved.remainingDays;
        ElapsedDays = saved.elapsedDays;
        CurrentNodeIndex = saved.currentNodeIndex;
        AssignedNPC = npc;
        hasCapabilitySnapshot = saved.hasCapabilitySnapshot;
        CapabilityScore = saved.capabilityScore;
        ResultTier = saved.resultTier;
        MapContext = saved.mapContext;
        if (!hasCapabilitySnapshot && npc != null) CaptureCapabilitySnapshot();
        if (Data.nodes != null && CurrentNodeIndex < Data.nodes.Count && State == MissionState.WaitingNode)
            CurrentNode = Data.nodes[CurrentNodeIndex];
        if (npc != null && (State == MissionState.Active || State == MissionState.WaitingNode))
        {
            npc.CurrentMission = this;
            npc.SetState(NPCState.Busy);
        }
    }

    public MissionSaveData ToSaveData()
    {
        return new MissionSaveData
        {
            missionId = Data.id,
            assignedCharacterId = AssignedNPC?.CharacterId,
            state = State,
            remainingDays = RemainingDays,
            elapsedDays = ElapsedDays,
            currentNodeIndex = CurrentNodeIndex,
            reward = reward,
            hasCapabilitySnapshot = hasCapabilitySnapshot,
            capabilityScore = CapabilityScore,
            resultTier = ResultTier
            ,mapContext = MapContext
        };
    }

    public bool CancelAwaitingReward()
    {
        if (State != MissionState.AwaitingReward) return false;
        State = MissionState.Failed;
        CurrentNode = null;
        return true;
    }

    public void ConfigureMapMission(Cultivation4X.WorldMap.MapMissionContext context, Reward mapReward)
    {
        if (State != MissionState.NotStarted || context == null) return;
        MapContext = new Cultivation4X.WorldMap.MapMissionContext
        {
            actionType = context.actionType,
            targetCellIndex = context.targetCellIndex,
            targetSiteId = context.targetSiteId
        };
        reward = mapReward ?? new Reward();
    }

    public void CaptureCapabilitySnapshot()
    {
        if (hasCapabilitySnapshot || AssignedNPC == null) return;
        MissionCapabilityEvaluation evaluation = CharacterCapabilityRules.EvaluateMission(Data, AssignedNPC);
        CapabilityScore = evaluation.score;
        ResultTier = evaluation.tier;
        hasCapabilitySnapshot = true;
    }

    public void ApplyExcellentRewardBonus()
    {
        if (ResultTier != MissionResultTier.Excellent) return;
        ItemReward spiritStones = reward.Items.Find(item => item != null && item.itemId == FacilityRules.SpiritStoneId);
        if (spiritStones != null) spiritStones.count += Mathf.FloorToInt(spiritStones.count * 0.5f);
    }

    public bool CancelAutonomousByPlayer()
    {
        if (State != MissionState.Active || !MissionManager.IsAutonomousMission(Data) ||
            (Data.nodes != null && Data.nodes.Count > 0)) return false;
        State = MissionState.Cancelled;
        CurrentNode = null;
        MissionManager.Instance?.RemoveMission(this);
        return true;
    }
}
