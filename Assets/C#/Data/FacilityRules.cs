using System;
//设施枚举、升级规则、各设施等级效果查询。
public enum FacilityType
{
    MissionHall,
    Warehouse,
    TrainingRoom,
    SecretRealm,
    AlchemyRoom,
    ProtectionArray,
    InheritanceChamber,
    ForgeRoom,
    FormationPlatform
}

[Serializable]
public class FacilityUpgradeResult
{
    public bool success;
    public string reason;
    public int newLevel;
}

public static class FacilityRules
{
    public const int MaxLevel = 3;
    public const string SpiritStoneId = "LingShi_001";
    public const string BasicMaterialId = "material_001";

    public static int UpgradeSpiritStoneCost(int currentLevel) => currentLevel == 1 ? 100 : currentLevel == 2 ? 250 : 0;
    public static int UpgradeMaterialCost(int currentLevel) => currentLevel == 1 ? 5 : currentLevel == 2 ? 12 : 0;
    public static int MissionCandidateCount(int level) => LevelValue(level, 2, 3, 4);
    public static int MissionConcurrency(int level) => LevelValue(level, 1, 2, 3);
    public static int WarehouseSlots(int level) => LevelValue(level, 10, 15, 20);
    public static int TrainingGain(int level) => LevelValue(level, 1, 2, 3);
    public static int SecretRealmDays(int level) => LevelValue(level, 5, 4, 3);
    public static int SecretRealmMaterialReward(int level) => LevelValue(level, 3, 5, 8);
    public static int AlchemyDays(int level) => LevelValue(level, 3, 2, 2);
    public static int AlchemyPillReward(int level) => LevelValue(level, 1, 1, 2);
    public static int FailureInjuryDays(int protectionArrayLevel) => protectionArrayLevel > 0 ? 1 : 3;
    public static int MaxMissionRankForReputation(int reputation) => reputation >= 300 ? 3 : reputation >= 100 ? 2 : 1;

    public static int ActionDays(FacilityType facility, int level)
    {
        return facility == FacilityType.SecretRealm ? SecretRealmDays(level) :
               facility == FacilityType.AlchemyRoom ? AlchemyDays(level) : 1;
    }

    public static int ActionOutput(FacilityType facility, int level)
    {
        return facility == FacilityType.SecretRealm ? SecretRealmMaterialReward(level) :
               facility == FacilityType.AlchemyRoom ? AlchemyPillReward(level) : 0;
    }

    public static bool UsesLevelScaledAction(FacilityType facility)
    {
        return facility == FacilityType.SecretRealm || facility == FacilityType.AlchemyRoom;
    }

    private static int LevelValue(int level, int one, int two, int three)
    {
        if (level <= 0) return 0;
        if (level <= 1) return one;
        return level == 2 ? two : three;
    }
}
