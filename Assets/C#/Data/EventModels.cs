using System;
using System.Collections.Generic;

public enum EventConditionType
{
    Always,
    HasTrait,
    MissingTrait,
    MinimumRealm,
    HealthIs,
    HasRelationship,
    MinimumItem,
    LivingCharacterCount
}

public enum EventEffectType
{
    AddReputation,
    AddAura,
    AddAuraControl,
    AddTechniqueMastery,
    AddTrait,
    RemoveTrait,
    AddRelationship,
    Injure,
    PermanentTrauma,
    Kill,
    AddItem,
    RemoveItem,
    ScheduleEvent,
    Recruit,
    AddTechniqueUnderstanding,
    AddVillageRelation
}

public enum EventSource
{
    MissionStart,
    MissionNode,
    MissionComplete,
    MissionFailed,
    Training,
    Injury,
    Recovery,
    FacilityUpgrade,
    SecretRealm,
    Alchemy,
    SectDaily,
    Recruitment,
    FollowUp,
    Exploration
}

[Serializable]
public class EventCondition
{
    public EventConditionType type;
    public string participant = "actor";
    public string value;
    public int intValue;
    public RelationshipTag relationshipTag;
}

[Serializable]
public class EventParticipantRule
{
    public string slot = "actor";
    public bool required = true;
    public bool allowDead;
    public List<EventCondition> conditions = new List<EventCondition>();
}

[Serializable]
public class EventEffect
{
    public EventEffectType type;
    public string participant = "actor";
    public string targetParticipant;
    public string value;
    public int amount;
    public int delayDays;
    public RelationshipTag relationshipTag;
}

[Serializable]
public class EventOutcomeDefinition
{
    public string text;
    public int weight = 1;
    public List<EventEffect> effects = new List<EventEffect>();
}

[Serializable]
public class EventOptionDefinition
{
    public string id;
    public string text;
    public string unavailableReason;
    public List<EventCondition> conditions = new List<EventCondition>();
    public List<EventOutcomeDefinition> outcomes = new List<EventOutcomeDefinition>();
}

[Serializable]
public class EventDefinition
{
    public string id;
    public string title;
    public string body;
    public List<string> tags = new List<string>();
    public int baseWeight = 10;
    public int cooldownDays = 10;
    public int maxOccurrences;
    public List<EventSource> sources = new List<EventSource>();
    public float triggerChance = 1f;
    public int expiresAfterDays = 3;
    public string defaultOptionId;
    public bool isCritical;
    public bool directOnly;
    public List<EventCondition> conditions = new List<EventCondition>();
    public List<EventParticipantRule> participants = new List<EventParticipantRule>();
    public List<EventOptionDefinition> options = new List<EventOptionDefinition>();
}

[Serializable]
public class PendingEvent
{
    public string eventId;
    public int dueDay;
    public Dictionary<string, string> participantIds = new Dictionary<string, string>();
}

[Serializable]
public class EventInboxEntry
{
    public string entryId;
    public string eventId;
    public int createdDay;
    public int expiresDay = -1;
    public Dictionary<string, string> participantIds = new Dictionary<string, string>();
}

[Serializable]
public class EventHistoryRecord
{
    public string eventId;
    public int day;
    public string optionId;
    public string resultText;
    public Dictionary<string, string> participantIds = new Dictionary<string, string>();
}

public class ActiveCharacterEvent
{
    public string EntryId { get; set; }
    public EventDefinition Definition { get; set; }
    public Dictionary<string, NPCRuntime> Participants { get; set; }
}
