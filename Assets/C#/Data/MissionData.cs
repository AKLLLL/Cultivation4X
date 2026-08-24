using System;
using System.Collections.Generic;

[Serializable]
public class MissionData
{
   
    // 任务ID
    public string id;
    // 任务名称
    public string name;
    // 描述
    public string description;
    // 目标类型
    public MissionType missionType;
    /// <summary>是否允许玩家在任务面板主动派遣。旧配置缺省时默认为 true，行为不变。</summary>
    public bool isPlayerAssignable = true;
    public int missionRank = 1;
    public int requiredMissionHallLevel = 1;
    public FacilityType requiredFacility;
    public int requiredFacilityLevel;
    public List<ItemReward> itemCosts = new List<ItemReward>();
    public bool isFacilityAction;
    public bool usesFacilityLevelScaling = true;
    public bool isStoryAction;
    public FoundingActionKind foundingAction;
    public string foundingTargetId;
    public int laborCost;
    public ThreatMissionKind threatMissionKind;
    public bool generatedByMap;

    // 完成所需天数
    public int needDays;
    public float techniqueMasteryReward;
    //物品奖励
    public List<ItemReward> itemRewards;
    // 所需力量
    public int requiredAttack;
    // 所需智力
    public int requiredIntelligence;
    public int requiredCombatPower;
    public List<string> preferredTechniqueTags = new List<string>();
    public List<string> preferredTraitIds = new List<string>();
    public int excellentScore = 130;
    // 事件节点
    // </summary>
    public List<MissionNodeData> nodes;
}
