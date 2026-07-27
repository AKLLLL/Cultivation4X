using System;

public static class CombatPowerCalculator
{
    public static int Calculate(CombatPowerInput input)
    {
        if (input == null) return 0;
        int realmBonus;
        switch (input.realm)
        {
            case CultivationRealm.QiRefining: realmBonus = 20; break;
            case CultivationRealm.Foundation: realmBonus = 60; break;
            case CultivationRealm.GoldenCore: realmBonus = 120; break;
            default: realmBonus = 0; break;
        }

        long value =
            (long)input.attack * 2 +
            input.agility +
            (long)input.physique * 2 +
            input.combatComprehension +
            Math.Min(Math.Max(0, input.combatExperience), 100) +
            realmBonus +
            input.techniqueBonus +
            Math.Max(0, input.artifactBonus);
        return (int)Math.Min(int.MaxValue, Math.Max(0L, value));
    }
}
