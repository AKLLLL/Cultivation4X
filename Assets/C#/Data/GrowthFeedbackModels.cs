using System;
using System.Collections.Generic;

public enum DiscipleActivityKind
{
    Training,
    SectDuty,
    Free,
    Mission,
    Recovery
}

public enum ExperienceType
{
    Other,
    InternalDecision,
    Recruitment,
    CultivationMilestone,
    TechniqueMilestone,
    AuraControlMilestone,
    Mission,
    Event,
    Relationship,
    Injury,
    NearDeath,
    Death,
    PlanPhaseStarted,
    PlanPhaseCompleted,
    RepeatedActionPattern,
    FatigueAccumulated,
    FatigueRecovered,
    TechniqueUnderstanding,
    DepartmentChange
}

public enum ExperienceImportance
{
    Internal,
    Minor,
    Major
}

public enum ExperienceRetention
{
    Recent,
    Chronicle
}

[Serializable]
public class ExperienceValue
{
    public string key;
    public string value;
}

[Serializable]
public class DiscipleDayExecutionResult
{
    public string discipleId;
    public MonthlyActivityType scheduledActivity;
    public DiscipleActivityKind actualActivity;
    public string actionId;
    public string actionDisplayName;
    public string targetId;
    public string targetDisplayName;
    public string departmentId;
    public bool executed;
    public string failureReason;
    public DailyCultivationResult cultivationResult;
}

[Serializable]
public class DiscipleDayResult
{
    public string discipleId;
    public string displayName;
    public int worldDay;
    public MonthlyActivityType scheduledActivity;
    public DiscipleActivityKind actualActivity;
    public string actionId;
    public string actionDisplayName;
    public string targetId;
    public string targetDisplayName;
    public string departmentId;
    public bool executed;
    public string failureReason;
    public float naqiGain;
    public float auraControlGain;
    public float techniqueProgressGain;
    public float fatigueChange;
    public float fatiguePeak;
    public float fatigueBefore;
    public float fatigueAfter;
    public float naqiBefore;
    public float naqiAfter;
    public float auraControlBefore;
    public float auraControlAfter;
    public int realmLayerBefore;
    public int realmLayerAfter;
    public string techniqueId;
    public float techniqueUnderstandingBefore;
    public float techniqueUnderstandingAfter;
    public TechniqueUnderstandingStage techniqueStageBefore;
    public TechniqueUnderstandingStage techniqueStageAfter;
    public DailyCultivationResult cultivationResult;
    public List<string> newExperienceIds = new List<string>();
}

[Serializable]
public class DiscipleMonthlyNarrativeState
{
    public bool trainingStarted;
    public bool trainingCompleted;
    public bool sectDutyStarted;
    public bool sectDutyCompleted;
    public int fatigueStageRecorded;
    public bool fatigueRecoveryRecorded;
    public bool techniqueUnderstandingRecorded;
    public List<string> repeatedPatternActivities = new List<string>();
}

[Serializable]
public class MonthlyActionCount
{
    public string actionId;
    public string displayName;
    public int count;
}

[Serializable]
public class DiscipleMonthlyStats
{
    public string discipleId;
    public string displayName;
    public int monthIndex;
    public int firstDay;
    public int lastDay;
    public int settledDays;
    public int plannedTrainingDays;
    public int plannedSectDutyDays;
    public int plannedFreeDays;
    public int actualTrainingDays;
    public int actualSectDutyDays;
    public int actualFreeDays;
    public int missionDays;
    public int recoveryDays;
    public float naqiGain;
    public float auraControlGain;
    public float techniqueProgressGain;
    public float maxFatigue;
    public int realmLayerStart;
    public int realmLayerEnd;
    public float naqiProgressStart;
    public float naqiProgressEnd;
    public string techniqueIdStart;
    public string techniqueIdEnd;
    public TechniqueUnderstandingStage techniqueStageStart;
    public TechniqueUnderstandingStage techniqueStageEnd;
    public List<MonthlyActionCount> actionCounts = new List<MonthlyActionCount>();
    public List<string> experienceIds = new List<string>();
    public DiscipleMonthlyNarrativeState narrative = new DiscipleMonthlyNarrativeState();
}

[Serializable]
public class ItemMonthChange
{
    public string itemId;
    public int countChange;
}

[Serializable]
public class SectMonthlyReport
{
    public string id;
    public int monthIndex;
    public int year;
    public int month;
    public int startDay;
    public int endDay;
    public List<DiscipleMonthlyStats> disciples = new List<DiscipleMonthlyStats>();
    public List<string> highlightDiscipleIds = new List<string>();
    public List<string> highlightExperienceIds = new List<string>();
    public List<ItemMonthChange> itemChanges = new List<ItemMonthChange>();
    public List<ResourceProductionRecord> resourceProduction = new List<ResourceProductionRecord>();
}

[Serializable]
public class SectGrowthFeedbackState
{
    public int activeMonthIndex = 1;
    public int lastProcessedDay;
    public int lastFinalizedMonthIndex;
    public List<DiscipleMonthlyStats> currentStats = new List<DiscipleMonthlyStats>();
    public List<ItemMonthChange> currentItemChanges = new List<ItemMonthChange>();
    public List<SectMonthlyReport> reports = new List<SectMonthlyReport>();
}

public sealed class DiscipleCurrentState
{
    public string discipleId;
    public string displayName;
    public string realmDisplayName;
    public float naqiProgress;
    public MonthlyActivityType scheduledActivity;
    public DiscipleActivityKind actualActivity;
    public string currentActionId;
    public string currentActionDisplayName;
    public float currentAura;
    public float auraCapacity;
    public float auraControl;
    public float fatigue;
    public string mainTechniqueId;
    public string mainTechniqueName;
    public float techniqueUnderstanding;
    public TechniqueUnderstandingStage techniqueStage;
}
