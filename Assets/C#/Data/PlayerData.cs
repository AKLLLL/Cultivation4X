using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家数据
/// </summary>
[System.Serializable]
public class PlayerData
{
    /// <summary>
    /// 玩家金币
    /// </summary>
    public int gold = 100;

    public int reputation = 0;
    public int trainingRoomLevel = 1;
    public int missionHallLevel = 1;
    public int infirmaryLevel = 1;
    public int warehouseLevel = 1;
    public int secretRealmLevel = 1;
    public int alchemyRoomLevel = 1;
    public int explorationHallLevel = 1;
    public int protectionArrayLevel = 1;
    public int inheritanceChamberLevel = 1;
    public int forgeRoomLevel = 1;
    public int formationPlatformLevel = 1;
    public FoundingState founding = new FoundingState { initialized = true, completed = true, stage = FoundingStage.Completed };
    public List<ExplorationRegionState> explorationRegions = new List<ExplorationRegionState>();
}
