using UnityEngine;

/// <summary>
/// 玩家管理器
/// </summary>
public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance;

    /// <summary>
    /// 玩家数据
    /// </summary>
    public PlayerData playerData = new PlayerData();

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// 增加金币
    /// </summary>
    public void AddGold(int gold)
    {
        playerData.gold = Mathf.Max(0, playerData.gold + gold);
    }

    public bool SpendGold(int amount)
    {
        if (amount < 0 || playerData.gold < amount) return false;
        playerData.gold -= amount;
        return true;
    }

    public void AddReputation(int amount)
    {
        playerData.reputation = Mathf.Max(0, playerData.reputation + amount);
    }

    public bool UpgradeFacility(string facility, int goldCost)
    {
        if (!SpendGold(goldCost)) return false;
        switch (facility)
        {
            case "TrainingRoom": playerData.trainingRoomLevel++; return true;
            case "MissionHall": playerData.missionHallLevel++; return true;
            case "Infirmary": playerData.infirmaryLevel++; return true;
            default:
                AddGold(goldCost);
                return false;
        }
    }
}
