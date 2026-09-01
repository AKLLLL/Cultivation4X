// 宗门既有功能的开放状态与固定效果。V24 起不再存在设施等级或升级。
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

public static class FacilityRules
{
    public const string SpiritStoneId = "LingShi_001";
    public const string BasicMaterialId = "material_001";
    public const int MissionCandidateCount = 2;
    public const int MissionConcurrency = 1;
    public const int WarehouseSlots = 10;
    public const int SecretRealmDays = 5;
    public const int SecretRealmMaterialReward = 3;
    public const int AlchemyDays = 3;
    public const int AlchemyPillReward = 1;

    public static float TrainingMultiplier(bool available) => available ? 1f : 0.8f;
    public static int FailureInjuryDays(bool protectionArrayAvailable) => protectionArrayAvailable ? 1 : 3;
    public static int MaxMissionRankForReputation(int reputation) => reputation >= 300 ? 3 : reputation >= 100 ? 2 : 1;

    public static int ActionDays(FacilityType facility)
    {
        return facility == FacilityType.SecretRealm ? SecretRealmDays :
               facility == FacilityType.AlchemyRoom ? AlchemyDays : 1;
    }

    public static int ActionOutput(FacilityType facility)
    {
        return facility == FacilityType.SecretRealm ? SecretRealmMaterialReward :
               facility == FacilityType.AlchemyRoom ? AlchemyPillReward : 0;
    }

    public static bool UsesFixedFacilityAction(FacilityType facility)
    {
        return facility == FacilityType.SecretRealm || facility == FacilityType.AlchemyRoom;
    }

}
