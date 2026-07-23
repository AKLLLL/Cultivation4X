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
        if (playerData == null) playerData = new PlayerData();
        if (playerData.gold == 0) playerData.gold = 100;
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

    public int GetFacilityLevel(FacilityType facility)
    {
        switch (facility)
        {
            case FacilityType.MissionHall: return playerData.missionHallLevel;
            case FacilityType.Warehouse: return playerData.warehouseLevel;
            case FacilityType.TrainingRoom: return playerData.trainingRoomLevel;
            case FacilityType.SecretRealm: return playerData.secretRealmLevel;
            case FacilityType.AlchemyRoom: return playerData.alchemyRoomLevel;
            case FacilityType.ExplorationHall: return 1;
            default: return 1;
        }
    }

    public bool CanUpgradeFacility(FacilityType facility, out string reason)
    {
        if (facility == FacilityType.ExplorationHall) { reason = "探索堂已建成且不可升级"; return false; }
        int level = GetFacilityLevel(facility);
        if (level >= FacilityRules.MaxLevel) { reason = "设施已达到最高等级"; return false; }
        if (playerData.gold < FacilityRules.UpgradeGoldCost(level)) { reason = "灵材不足"; return false; }
        if (WarehouseManager.Instance == null || WarehouseManager.Instance.GetItemCount(FacilityRules.BasicMaterialId) < FacilityRules.UpgradeMaterialCost(level))
        { reason = "基础材料不足"; return false; }
        reason = null;
        return true;
    }

    public FacilityUpgradeResult TryUpgradeFacility(FacilityType facility)
    {
        if (!CanUpgradeFacility(facility, out string reason))
            return new FacilityUpgradeResult { success = false, reason = reason, newLevel = GetFacilityLevel(facility) };

        int level = GetFacilityLevel(facility);
        int goldCost = FacilityRules.UpgradeGoldCost(level);
        int materialCost = FacilityRules.UpgradeMaterialCost(level);
        playerData.gold -= goldCost;
        WarehouseManager.Instance.RemoveItem(FacilityRules.BasicMaterialId, materialCost);
        SetFacilityLevel(facility, level + 1);
        TimeManager.Instance?.RecordFacilityUpgrade(facility, level + 1);
        TimeManager.Instance?.RecordPreAdvanceResourceChange(-goldCost, -materialCost);
        EventManager.Instance?.TryTriggerSource(EventSource.FacilityUpgrade);
        return new FacilityUpgradeResult { success = true, newLevel = level + 1 };
    }

    private void SetFacilityLevel(FacilityType facility, int level)
    {
        switch (facility)
        {
            case FacilityType.MissionHall: playerData.missionHallLevel = level; break;
            case FacilityType.Warehouse: playerData.warehouseLevel = level; break;
            case FacilityType.TrainingRoom: playerData.trainingRoomLevel = level; break;
            case FacilityType.SecretRealm: playerData.secretRealmLevel = level; break;
            case FacilityType.AlchemyRoom: playerData.alchemyRoomLevel = level; break;
        }
    }

    public bool UpgradeFacility(string facility, int goldCost)
    {
        if (facility == "Infirmary") return false;
        if (!System.Enum.TryParse(facility, out FacilityType type)) return false;
        return TryUpgradeFacility(type).success;
    }
}
