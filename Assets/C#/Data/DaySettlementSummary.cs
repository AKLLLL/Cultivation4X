using System;
using System.Collections.Generic;

[Serializable]
public class CharacterDayChange
{
    public string characterId;
    public string displayName;
    public float currentAuraChange;
    public float currentAura;
    public float naqiProgressChange;
    public float techniqueUnderstandingChange;
    public float auraControlChange;
    public float fatigueChange;
    public int realmLayerBefore;
    public int realmLayerAfter;
    public DailyCultivationResult cultivationResult;
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
public class ItemDayChange
{
    public string itemId;
    public int countChange;
}

[Serializable]
public class ResourceProductionRecord
{
    public string nodeId;
    public string siteName;
    public string itemId;
    public int calculated;
    public int received;
    public int lost;
}
//每日结算数据模型：资源变化、任务结果、弟子状态变化与新事件。
[Serializable]
public class DaySettlementSummary
{
    public int day;
    public bool isMonthEnd;
    public int monthIndex;
    public int reputationChange;
    public List<ItemDayChange> itemChanges = new List<ItemDayChange>();
    public List<ResourceProductionRecord> resourceProduction = new List<ResourceProductionRecord>();
    public List<MissionDayResult> missionResults = new List<MissionDayResult>();
    public List<CharacterDayChange> characterChanges = new List<CharacterDayChange>();
    public List<DiscipleDayResult> discipleResults = new List<DiscipleDayResult>();
    public List<string> newEventTitles = new List<string>();
}
