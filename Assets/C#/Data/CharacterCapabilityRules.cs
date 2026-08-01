using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum MissionResultTier
{
    Insufficient,
    Qualified,
    Excellent
}

[Serializable]
public class MissionCapabilityEvaluation
{
    public int score;
    public MissionResultTier tier;
    public bool techniqueMatched;
    public bool traitMatched;
}

public static class CharacterCapabilityRules
{
    public static int CalculateCombatPower(NPCRuntime npc, int equipmentBonus = 0)
    {
        if (npc == null) return 0;
        return CombatPowerCalculator.Calculate(new CombatPowerInput
        {
            attack = npc.Attack,
            agility = npc.Agility,
            physique = npc.Physique,
            combatComprehension = npc.CombatComprehension,
            combatExperience = npc.CombatExperience,
            realm = npc.Realm,
            techniqueBonus = FoundingRules.GetActiveEffectTotal(TechniqueEffectType.CombatPowerFlat),
            artifactBonus = equipmentBonus
        });
    }

    public static int CalculateCandidateCombatPower(FounderCandidateData candidate)
    {
        if (candidate == null) return 0;
        return Mathf.Max(0,
            candidate.attack * 2 +
            candidate.agility +
            candidate.physique * 2 +
            candidate.combatComprehension +
            20);
    }

    public static MissionCapabilityEvaluation EvaluateMission(MissionData data, NPCRuntime npc)
    {
        MissionCapabilityEvaluation result = new MissionCapabilityEvaluation { score = 100, tier = MissionResultTier.Qualified };
        if (data == null || npc == null)
        {
            result.score = 0;
            result.tier = MissionResultTier.Insufficient;
            return result;
        }

        List<float> ratios = new List<float>();
        bool requirementsPass = AddRatio(ratios, npc.Attack, data.requiredAttack);
        requirementsPass &= AddRatio(ratios, npc.Intelligence, data.requiredIntelligence);
        requirementsPass &= AddRatio(ratios, npc.CombatPower, data.requiredCombatPower);
        result.score = ratios.Count == 0 ? 100 : Mathf.FloorToInt(ratios.Average());

        FoundingState founding = PlayerManager.Instance?.playerData?.founding;
        result.techniqueMatched = FoundingRules.HasAnyTag(founding?.selectedTechniqueId, data.preferredTechniqueTags);
        result.traitMatched = (data.preferredTraitIds ?? new List<string>()).Any(npc.Character.HasTrait);
        if (result.techniqueMatched) result.score += 10;
        if (result.traitMatched) result.score += 10;
        if (result.techniqueMatched)
            result.score += FoundingRules.GetActiveEffectTotal(TechniqueEffectType.MatchedMissionScoreFlat);

        if (!requirementsPass)
        {
            result.tier = MissionResultTier.Insufficient;
            return result;
        }

        int excellentScore = data.excellentScore <= 0 ? 130 : data.excellentScore;
        result.tier = result.score >= excellentScore ? MissionResultTier.Excellent : MissionResultTier.Qualified;
        return result;
    }

    private static bool AddRatio(List<float> ratios, int actual, int required)
    {
        if (required <= 0) return true;
        ratios.Add(Mathf.Min(200f, Mathf.Max(0, actual) * 100f / required));
        return actual >= required;
    }
}
