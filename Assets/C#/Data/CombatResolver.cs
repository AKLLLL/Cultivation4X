using System;
using System.Collections.Generic;
using System.Linq;

public static class CombatResolver
{
    public static int AggregatePartyPower(IEnumerable<CombatantPower> combatants)
    {
        List<CombatantPower> ordered = (combatants ?? Enumerable.Empty<CombatantPower>())
            .Where(item => item != null)
            .OrderByDescending(item => item.power)
            .ThenBy(item => item.characterId, StringComparer.Ordinal)
            .Take(3)
            .ToList();
        if (ordered.Count == 0) return 0;
        long total = Math.Max(0, ordered[0].power);
        for (int i = 1; i < ordered.Count; i++)
            total += Math.Max(0, ordered[i].power) / 2;
        return (int)Math.Min(int.MaxValue, total);
    }

    public static CombatResolution Resolve(CombatRequest request)
    {
        CombatResolution result = new CombatResolution();
        if (request == null) return result;

        result.partyPower = AggregatePartyPower(request.combatants);
        result.threatPower = Math.Max(1, request.threatPower);
        result.intelligence = Math.Min(100, Math.Max(0, request.intelligence));
        result.intelligenceModifier = 1f + 0.0025f * result.intelligence;
        result.initiativeModifier = result.intelligence < 25 ? 0.9f : result.intelligence < 50 ? 1f : 1.1f;
        result.preparationModifier = Math.Max(0f, request.preparationModifier);
        result.firstExchangePower = result.partyPower * result.initiativeModifier;
        result.firstExchangeRatio = result.firstExchangePower / result.threatPower;

        if (result.firstExchangeRatio >= 1.5f)
        {
            result.endedAfterFirstExchange = true;
            result.finalPower = result.firstExchangePower;
            result.finalRatio = result.firstExchangeRatio;
            result.resultTier = CombatResultTier.PerfectVictory;
            return result;
        }
        if (result.firstExchangeRatio < 0.6f)
        {
            result.endedAfterFirstExchange = true;
            result.finalPower = result.firstExchangePower;
            result.finalRatio = result.firstExchangeRatio;
            result.resultTier = CombatResultTier.CatastrophicDefeat;
            SetRetreat(result);
            return result;
        }

        result.firstExchangeModifier = result.firstExchangeRatio >= 1.1f ? 1.1f :
            result.firstExchangeRatio < 0.9f ? 0.9f : 1f;
        result.finalPower = result.partyPower * result.intelligenceModifier *
            result.preparationModifier * result.firstExchangeModifier;
        result.finalRatio = result.finalPower / result.threatPower;
        result.resultTier = TierForRatio(result.finalRatio);
        if (result.resultTier == CombatResultTier.Failure ||
            result.resultTier == CombatResultTier.CatastrophicDefeat)
            SetRetreat(result);
        return result;
    }

    public static CombatResultTier TierForRatio(float ratio)
    {
        if (ratio >= 1.5f) return CombatResultTier.PerfectVictory;
        if (ratio >= 1f) return CombatResultTier.Victory;
        if (ratio >= 0.8f) return CombatResultTier.CostlyVictory;
        if (ratio >= 0.6f) return CombatResultTier.Failure;
        return CombatResultTier.CatastrophicDefeat;
    }

    private static void SetRetreat(CombatResolution result)
    {
        result.retreatAttempted = true;
        result.retreatRatio = result.partyPower * result.intelligenceModifier / result.threatPower;
        result.retreatSucceeded = result.retreatRatio >= 0.7f;
    }
}
