using System;
using System.Collections.Generic;

public enum HealthState
{
    Healthy,
    LightInjury,
    HeavyInjury,
    SeriousInjury,
    PermanentTrauma,
    Dead
}

public enum CultivationRealm
{
    Mortal = -1,
    QiRefining,
    Foundation,
    GoldenCore
}

public enum SpiritRootQuality
{
    Mixed,
    Low,
    Medium,
    High,
    Supreme,
    Heavenly
}

[Serializable]
public class SpiritRootData
{
    public SpiritRootQuality quality = SpiritRootQuality.Medium;
    public float gold = 0.2f;
    public float wood = 0.2f;
    public float water = 0.2f;
    public float fire = 0.2f;
    public float earth = 0.2f;
}

public enum CultivationActionOutcome
{
    Failed,
    Qualified,
    Excellent
}

[Serializable]
public class DailyCultivationResult
{
    public string npcId;
    public int date;
    public float absorbedAura;
    public float consumedAura;
    public float leakedAura;
    public string selectedActionId;
    public string selectedActionName;
    public CultivationActionOutcome outcome;
    public float naqiGain;
    public float auraControlGain;
    public float fatigueChange;
    public int layerBefore;
    public int layerAfter;
    public string eventDescription;
}

public enum RelationshipTag
{
    MasterApprentice,
    Friend,
    Rival,
    LifeSaver
}

[Serializable]
public class CharacterTraitData
{
    public string id;
    public string displayName;
    public string description;
    public int attackModifier;
    public int intelligenceModifier;
    public float eventWeightModifier;
    public bool isExperience;
}

[Serializable]
public class RelationshipRecord
{
    public string sourceCharacterId;
    public string targetCharacterId;
    public RelationshipTag tag;
    public int createdDay;
}

[Serializable]
public class LifeRecord
{
    public int day;
    public string category;
    public string text;
    public string sourceId;
}

[Serializable]
public class CharacterState
{
    public string characterId;
    public string templateId;
    public string displayName;
    public int age = 16;
    public int realmLayer = 1;
    public float naqiProgress;
    public float currentAura;
    public float auraControl = 10f;
    public float fatigue;
    public SpiritRootData spiritRoot = new SpiritRootData();
    public DailyCultivationResult latestCultivationResult;
    public string mainTechniqueId;
    public List<string> activeAuxiliaryTechniqueIds = new List<string>();
    public List<PersonalTechniqueProgress> techniqueProgresses = new List<PersonalTechniqueProgress>();
    public int mentalState = DiscipleMentalStateRules.MaxMentalState;
    public CultivationRealm realm;
    public HealthState health = HealthState.Healthy;
    public NPCState activityState = NPCState.Idle;
    public int stateRemainDays;
    public bool hasGeneratedProfile;
    public int baseAttack;
    public int baseIntelligence;
    public int baseAgility;
    public int baseComprehension;
    public int baseCombatComprehension;
    public int basePhysique;
    public int combatExperience;
    public string initialFeatureId;
    public List<string> traitIds = new List<string>();
    public List<RelationshipRecord> relationships = new List<RelationshipRecord>();
    public List<LifeRecord> lifeRecords = new List<LifeRecord>();

    public bool IsAlive => health != HealthState.Dead;

    public bool HasTrait(string traitId)
    {
        return !string.IsNullOrEmpty(traitId) && traitIds.Contains(traitId);
    }

    public void AddTrait(string traitId)
    {
        if (!string.IsNullOrWhiteSpace(traitId) && !traitIds.Contains(traitId))
            traitIds.Add(traitId);
    }

    public void AddLifeRecord(int day, string category, string text, string sourceId = null)
    {
        lifeRecords.Add(new LifeRecord
        {
            day = day,
            category = category,
            text = text,
            sourceId = sourceId
        });
    }
}
