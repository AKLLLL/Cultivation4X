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
    public int missionRank = 1;
    public int requiredMissionHallLevel = 1;
    public FacilityType requiredFacility;
    public int requiredFacilityLevel;
    public int goldCost;
    public List<ItemReward> itemCosts = new List<ItemReward>();
    public bool isFacilityAction;

    // 完成所需天数
    public int needDays;
    // 奖励金币
    public int goldReward;
    // 经验奖励
    public int expReward;
    //物品奖励
    public List<ItemReward> itemRewards;
    // 所需力量
    public int requiredAttack;
    // 所需智力
    public int requiredIntelligence;
    // 事件节点
    // </summary>
    public List<MissionNodeData> nodes;
}
