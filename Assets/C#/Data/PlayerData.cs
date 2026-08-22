using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家数据
/// </summary>
[System.Serializable]
public class PlayerData
{
    public string sectId;
    public string sectName;
    public int foundedDay;
    public int influenceRadius;

    public int reputation = 0;
    public int trainingRoomLevel = 1;
    public int missionHallLevel = 1;
    public int infirmaryLevel = 1;
    public int warehouseLevel = 1;
    public int secretRealmLevel = 1;
    public int alchemyRoomLevel = 1;
    public int protectionArrayLevel = 1;
    public int inheritanceChamberLevel = 1;
    public int forgeRoomLevel = 1;
    public int formationPlatformLevel = 1;
    public int sectDutyWorkCredit;
    public List<MonthlyDisciplePlan> monthlyPlans = new List<MonthlyDisciplePlan>();
    public FoundingState founding = new FoundingState { initialized = true, sectCreated = true, completed = true, stage = FoundingStage.Completed };
}
