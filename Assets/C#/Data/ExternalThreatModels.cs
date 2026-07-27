using System;
using System.Collections.Generic;

public enum ExternalThreatStatus
{
    None,
    Scheduled,
    Active,
    Resolved
}

public enum ThreatMissionKind
{
    None,
    Investigation
}

[Serializable]
public class ExternalThreatDefinition
{
    public string id;
    public string name;
    public string description;
    public string enemyType;
    public string targetVillageId;
    public int triggerRelation = 20;
    public int activationDelayDays = 5;
    public int threatPower = 150;
    public int raidIntervalDays = 5;
    public int raidLaborLoss = 10;
    public string investigationMissionId;
    public string discoveredEventId;
    public int defenseMaterialCost = 3;
    public string firstExchangeTemplate;
    public string battleTemplate;
    public string retreatTemplate;
}

[Serializable]
public class ThreatResolutionRecord
{
    public int day;
    public CombatPlanType plan;
    public List<string> participantIds = new List<string>();
    public CombatResolution combat;
    public string weakestCharacterId;
    public int populationChange;
    public int laborChange;
    public int relationChange;
    public string narrative;
}

[Serializable]
public class ActiveThreatState
{
    public string threatId;
    public ExternalThreatStatus status;
    public int scheduledDay = -1;
    public int activatedDay = -1;
    public int nextRaidDay = -1;
    public int intelligence;
    public int raidCount;
    public bool discoveryNotificationEnqueued;
    public CombatPlanType selectedPlan;
    public List<string> selectedCharacterIds = new List<string>();
    public ThreatResolutionRecord resolution;
}
