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

        // 金币奖励
        reward.Gold = data.goldReward;

        // 经验奖励
        reward.Exp = data.expReward;

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
        if (data.isFacilityAction && reward.Items.Count == 1)
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
        State = MissionState.Active;
        // 从配置读取耗时
        RemainingDays = Data.isFacilityAction
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

        ApplyEffects(
            option
        );

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
    //效果执行
    private void ApplyEffects(
    MissionOptionData option)
    {
        if (option.effects == null)
            return;
        foreach (var effect in option.effects)
        {

            switch (effect.type)
            {

                //增加金币
                case "Reward":
                case "AddGold":

                    Reward.Gold += effect.value;

                    Debug.Log(
                    $"获得灵材:{effect.value}"
                    );

                    break;

                //增加经验
                case "AddExp":

                    Reward.Exp += effect.value;

                    Debug.Log(
                    $"获得修为:{effect.value}"
                    );

                    break;

                //获得物品
                case "AddItem":

                    WarehouseManager.Instance.AddItem(
                        effect.itemId,
                        effect.count
                    );

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
                        return;
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

    }
   
    //节点选择后继续任务
    private void ContinueMission()
    {

        CurrentNodeIndex++;


        State = MissionState.Active;


        CurrentNode = null;
        CheckNode();

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
                3
            );

            Debug.Log(
                $"NPC {AssignedNPC.Data.npcName} 任务失败，受伤3天！"
            );
        }

        Debug.Log($"任务失败：{Data.name}");
        MissionManager.Instance?.NotifyMissionFailed(this);
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
            reward = reward
        };
    }
}
