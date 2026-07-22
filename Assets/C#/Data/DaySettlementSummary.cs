using System;
using System.Collections.Generic;

[Serializable]
public class CharacterDayChange
{
    public string characterId;
    public string displayName;
    public int cultivationChange;
    public CultivationRealm realmBefore;
    public CultivationRealm realmAfter;
    public HealthState healthBefore;
    public HealthState healthAfter;
}

[Serializable]
public class MissionDayResult
{
    public string missionId;
    public string missionName;
    public MissionState state;
}

[Serializable]
public class FacilityUpgradeRecord
{
    public FacilityType facility;
    public int newLevel;
}
//每日结算数据模型：资源变化、任务结果、弟子状态变化、新事件、设施升级记录。
[Serializable]
public class DaySettlementSummary
{
    public int day;
    public int goldChange;
    public int reputationChange;
    public int basicMaterialChange;
    public List<MissionDayResult> missionResults = new List<MissionDayResult>();
    public List<CharacterDayChange> characterChanges = new List<CharacterDayChange>();
    public List<string> newEventTitles = new List<string>();
    public List<FacilityUpgradeRecord> facilityUpgrades = new List<FacilityUpgradeRecord>();
}
