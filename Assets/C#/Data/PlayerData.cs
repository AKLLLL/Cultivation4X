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
    public int gold = 0;

    public int reputation = 0;
    public int trainingRoomLevel = 1;
    public int missionHallLevel = 1;
    public int infirmaryLevel = 1;
}
