using System;
using System.Collections.Generic;

public enum CombatPlanType
{
    HeadOn,
    SimpleDefense,
    RetreatToCave
}

public enum CombatResultTier
{
    PerfectVictory,
    Victory,
    CostlyVictory,
    Failure,
    CatastrophicDefeat
}

[Serializable]
public class CombatPowerInput
{
    public int attack;
    public int agility;
    public int physique;
    public int combatComprehension;
    public int combatExperience;
    public CultivationRealm realm;
    public int techniqueBonus;
    public int artifactBonus;
}

[Serializable]
public class CombatantPower
{
    public string characterId;
    public int power;
}

[Serializable]
public class CombatRequest
{
    public List<CombatantPower> combatants = new List<CombatantPower>();
    public int threatPower;
    public int intelligence;
    public float preparationModifier = 1f;
}

[Serializable]
public class CombatResolution
{
    public int partyPower;
    public int threatPower;
    public int intelligence;
    public float intelligenceModifier;
    public float initiativeModifier;
    public float firstExchangePower;
    public float firstExchangeRatio;
    public float firstExchangeModifier = 1f;
    public float preparationModifier = 1f;
    public float finalPower;
    public float finalRatio;
    public bool endedAfterFirstExchange;
    public CombatResultTier resultTier;
    public bool retreatAttempted;
    public float retreatRatio;
    public bool retreatSucceeded;
}
