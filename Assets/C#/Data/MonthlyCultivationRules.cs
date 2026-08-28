using System;
using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;
using UnityEngine;

public enum MonthlyActivityType { Training, SectDuty, Free }

[Serializable]
public class MonthlyPlanTemplate
{
    public string id;
    public string name;
    public List<MonthlyActivityType> days = Enumerable.Repeat(MonthlyActivityType.Free, GameCalendarRules.DaysPerMonth).ToList();
    public List<string> discipleIds = new List<string>();
}

public static class MonthlyPlanRules
{
    public const int DaysPerMonth = GameCalendarRules.DaysPerMonth;
    public static int MonthIndex(int day) => day <= 0 ? 1 : (day - 1) / DaysPerMonth + 1;
    public static int DayOfMonth(int day) => day <= 0 ? 0 : (day - 1) % DaysPerMonth + 1;
    public static IReadOnlyList<MonthlyPlanTemplate> GetTemplates() =>
        PlayerManager.Instance?.playerData?.monthlyPlanTemplates ?? (IReadOnlyList<MonthlyPlanTemplate>)Array.Empty<MonthlyPlanTemplate>();

    public static MonthlyPlanTemplate GetTemplate(string templateId) =>
        GetTemplates().FirstOrDefault(template => template != null && template.id == templateId);

    public static MonthlyPlanTemplate GetTemplateFor(string characterId) =>
        string.IsNullOrWhiteSpace(characterId) ? null :
        GetTemplates().FirstOrDefault(template => template?.discipleIds?.Contains(characterId) == true);

    public static MonthlyPlanTemplate CreateTemplate(string name = null)
    {
        PlayerData player = PlayerManager.Instance?.playerData;
        if (player == null || player.founding == null || !player.founding.sectCreated) return null;
        player.monthlyPlanTemplates = player.monthlyPlanTemplates ?? new List<MonthlyPlanTemplate>();
        MonthlyPlanTemplate template = new MonthlyPlanTemplate
        {
            id = Guid.NewGuid().ToString("N"),
            name = string.IsNullOrWhiteSpace(name) ? $"计划模板 {player.monthlyPlanTemplates.Count + 1}" : name.Trim()
        };
        player.monthlyPlanTemplates.Add(template);
        return template;
    }

    public static MonthlyPlanTemplate CopyTemplate(string templateId)
    {
        MonthlyPlanTemplate source = GetTemplate(templateId);
        if (source == null) return null;
        Normalize(source);
        MonthlyPlanTemplate copy = CreateTemplate($"{source.name} 副本");
        if (copy != null) copy.days = new List<MonthlyActivityType>(source.days);
        return copy;
    }

    public static bool RenameTemplate(string templateId, string name)
    {
        MonthlyPlanTemplate template = GetTemplate(templateId);
        if (template == null || string.IsNullOrWhiteSpace(name)) return false;
        template.name = name.Trim();
        return true;
    }

    public static bool DeleteTemplate(string templateId)
    {
        PlayerData player = PlayerManager.Instance?.playerData;
        MonthlyPlanTemplate template = GetTemplate(templateId);
        return player?.monthlyPlanTemplates != null && template != null && player.monthlyPlanTemplates.Remove(template);
    }

    public static bool BindDisciple(string templateId, string characterId)
    {
        MonthlyPlanTemplate target = GetTemplate(templateId);
        if (target == null || string.IsNullOrWhiteSpace(characterId)) return false;
        foreach (MonthlyPlanTemplate template in GetTemplates().Where(item => item != null))
        {
            template.discipleIds = template.discipleIds ?? new List<string>();
            template.discipleIds.RemoveAll(id => id == characterId);
        }
        target.discipleIds.Add(characterId);
        return true;
    }

    public static bool UnbindDisciple(string templateId, string characterId)
    {
        MonthlyPlanTemplate template = GetTemplate(templateId);
        return template?.discipleIds != null && template.discipleIds.Remove(characterId);
    }

    public static MonthlyActivityType ActivityFor(NPCRuntime npc, int day) => ActivityFor(npc?.CharacterId, day);
    public static MonthlyActivityType ActivityFor(string characterId, int day)
    {
        MonthlyPlanTemplate plan = GetTemplateFor(characterId);
        int index = DayOfMonth(day) - 1;
        if (plan == null || index < 0 || index >= DaysPerMonth) return MonthlyActivityType.Free;
        Normalize(plan);
        return plan.days[index];
    }

    public static bool TrySetDay(MonthlyPlanTemplate plan, int dayOfMonth, MonthlyActivityType activity, out string reason)
    {
        if (plan == null) { reason = "计划不存在"; return false; }
        if (dayOfMonth < 1 || dayOfMonth > DaysPerMonth || !Enum.IsDefined(typeof(MonthlyActivityType), activity))
        { reason = "日期或活动类型无效"; return false; }
        Normalize(plan);
        plan.days[dayOfMonth - 1] = activity;
        reason = null;
        return true;
    }

    public static void Fill(MonthlyPlanTemplate plan, MonthlyActivityType activity)
    {
        if (plan == null || !Enum.IsDefined(typeof(MonthlyActivityType), activity)) return;
        plan.days = Enumerable.Repeat(activity, DaysPerMonth).ToList();
    }

    public static void ApplyTemplate(MonthlyPlanTemplate plan, int trainingDays = 15, int dutyDays = 6)
    {
        if (plan == null) return;
        trainingDays = Mathf.Clamp(trainingDays, 0, DaysPerMonth);
        dutyDays = Mathf.Clamp(dutyDays, 0, DaysPerMonth - trainingDays);
        plan.days = new List<MonthlyActivityType>(DaysPerMonth);
        for (int index = 0; index < DaysPerMonth; index++)
        {
            float progress = (index + 1f) / DaysPerMonth;
            int expectedTraining = Mathf.RoundToInt(trainingDays * progress);
            int expectedDuty = Mathf.RoundToInt(dutyDays * progress);
            int actualTraining = plan.days.Count(item => item == MonthlyActivityType.Training);
            int actualDuty = plan.days.Count(item => item == MonthlyActivityType.SectDuty);
            plan.days.Add(actualTraining < expectedTraining ? MonthlyActivityType.Training :
                actualDuty < expectedDuty ? MonthlyActivityType.SectDuty : MonthlyActivityType.Free);
        }
    }

    public static bool CanStartAutonomousMission(NPCRuntime npc, int needDays, int decisionDay, out string reason)
    {
        if (npc == null || needDays <= 0) { reason = "任务耗时无效"; return false; }
        int firstMonth = MonthIndex(decisionDay + 1);
        for (int offset = 1; offset <= needDays; offset++)
        {
            int targetDay = decisionDay + offset;
            if (MonthIndex(targetDay) != firstMonth) { reason = "任务会跨越月末"; return false; }
            if (ActivityFor(npc, targetDay) != MonthlyActivityType.Free) { reason = "未来连续自由日不足"; return false; }
        }
        reason = null;
        return true;
    }

    public static void Normalize(MonthlyPlanTemplate plan)
    {
        if (plan == null) return;
        plan.days = plan.days ?? new List<MonthlyActivityType>();
        if (plan.days.Count > DaysPerMonth) plan.days.RemoveRange(DaysPerMonth, plan.days.Count - DaysPerMonth);
        while (plan.days.Count < DaysPerMonth) plan.days.Add(MonthlyActivityType.Free);
        plan.discipleIds = plan.discipleIds ?? new List<string>();
        plan.discipleIds = plan.discipleIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
    }
}

public static class SpiritRootRules
{
    public static float AbsorptionMultiplier(SpiritRootQuality quality)
    {
        switch (quality)
        {
            case SpiritRootQuality.Mixed: return 0.2f;
            case SpiritRootQuality.Low: return 0.6f;
            case SpiritRootQuality.Medium: return 1f;
            case SpiritRootQuality.High: return 1.8f;
            case SpiritRootQuality.Supreme: return 3f;
            case SpiritRootQuality.Heavenly: return 6f;
            default: return 1f;
        }
    }

    public static float LeakageRate(SpiritRootQuality quality)
    {
        switch (quality)
        {
            case SpiritRootQuality.Mixed: return 0.95f;
            case SpiritRootQuality.Low: return 0.90f;
            case SpiritRootQuality.Medium: return 0.70f;
            case SpiritRootQuality.High: return 0.50f;
            case SpiritRootQuality.Supreme: return 0.30f;
            case SpiritRootQuality.Heavenly: return 0f;
            default: return 0.70f;
        }
    }

    public static float RefiningMultiplier(SpiritRootQuality quality)
    {
        switch (quality)
        {
            case SpiritRootQuality.Mixed: return 0.90f;
            case SpiritRootQuality.Low: return 0.95f;
            case SpiritRootQuality.Medium: return 1f;
            case SpiritRootQuality.High: return 1.05f;
            case SpiritRootQuality.Supreme: return 1.10f;
            case SpiritRootQuality.Heavenly: return 1.15f;
            default: return 1f;
        }
    }

    public static void Normalize(SpiritRootData root)
    {
        if (root == null) return;
        root.gold = Mathf.Max(0f, root.gold); root.wood = Mathf.Max(0f, root.wood);
        root.water = Mathf.Max(0f, root.water); root.fire = Mathf.Max(0f, root.fire); root.earth = Mathf.Max(0f, root.earth);
        float total = root.gold + root.wood + root.water + root.fire + root.earth;
        if (total <= 0.0001f) { root.gold = root.wood = root.water = root.fire = root.earth = 0.2f; return; }
        root.gold /= total; root.wood /= total; root.water /= total; root.fire /= total; root.earth /= total;
    }
}

[Serializable]
public class CultivationActionDefinition
{
    public string id;
    public string displayName;
    public float auraCost;
    public float cultivationEfficiency;
    public float fatigueGain;
    public float auraControlGain;
    public bool controlCheck;
    public float techniqueDifficulty;
    public List<string> tags = new List<string>();
}

public static class DailyCultivationSimulator
{
    private const float BaseAbsorption = 20f;
    private const float DailyFatigueRecovery = 8f;
    private static readonly CultivationActionDefinition[] Actions =
    {
        new CultivationActionDefinition { id = "meditate_refine", displayName = "打坐炼化", auraCost = 20f, cultivationEfficiency = 0.3435f, fatigueGain = 18f, techniqueDifficulty = 0.6f, tags = new List<string> { "cultivation", "refining" } },
        new CultivationActionDefinition { id = "basic_practice", displayName = "基础练功", auraCost = 20f, cultivationEfficiency = 0.3335f, fatigueGain = 10f, auraControlGain = 1f, techniqueDifficulty = 0.5f, tags = new List<string> { "cultivation", "body" } },
        new CultivationActionDefinition { id = "aura_circulation", displayName = "灵气运转尝试", auraCost = 20f, cultivationEfficiency = 0.3235f, fatigueGain = 8f, auraControlGain = 2f, controlCheck = true, techniqueDifficulty = 0.7f, tags = new List<string> { "cultivation", "control" } }
    };
    public static IReadOnlyList<CultivationActionDefinition> Definitions => Actions;

    public static float AuraCapacity(NPCRuntime npc)
    {
        int layer = Mathf.Clamp(npc?.Character?.realmLayer ?? 1, 1, 3);
        float baseCapacity = layer == 1 ? 50f : layer == 2 ? 75f : 100f;
        int physique = npc?.Physique ?? 12;
        float multiplier = physique <= 7 ? 0.7f : physique <= 13 ? 1f : physique <= 17 ? 1.3f : physique <= 19 ? 1.6f : 2f;
        return baseCapacity * multiplier;
    }

    public static void StartDay(NPCRuntime npc)
    {
        if (npc?.Character == null) return;
        npc.Character.fatigue = Mathf.Max(0f, npc.Character.fatigue - DailyFatigueRecovery);
        npc.Character.currentAura = Mathf.Clamp(npc.Character.currentAura, 0f, AuraCapacity(npc));
    }

    public static DailyCultivationResult SimulateTrainingDay(NPCRuntime npc, int day,
        string lockedActionId = null)
    {
        if (npc?.Character == null || !npc.Character.IsAlive) return null;
        CharacterState state = npc.Character;
        int layerBefore = state.realmLayer;
        float fatigueBefore = state.fatigue;
        float absorbed = Absorb(npc);
        CultivationActionDefinition action = string.IsNullOrWhiteSpace(lockedActionId)
            ? SelectAction(npc, day)
            : Actions.FirstOrDefault(item => item.id == lockedActionId) ?? SelectAction(npc, day);
        float consumed = Mathf.Min(state.currentAura, action.auraCost);
        state.currentAura -= consumed;
        CultivationActionOutcome outcome = ResolveOutcome(npc, action, day);
        float resultMultiplier = outcome == CultivationActionOutcome.Failed ? 0.25f : outcome == CultivationActionOutcome.Excellent ? 1.2f : 1f;
        TechniqueDefinition technique = TechniqueRules.MainTechnique(state);
        float refining = SpiritRootRules.RefiningMultiplier(state.spiritRoot.quality) * (technique?.refiningMultiplier ?? 1f);
        float fatigueEfficiency = 1f - 0.3f * Mathf.Clamp01(fatigueBefore / 100f);
        float gain = consumed * action.cultivationEfficiency * refining * resultMultiplier * fatigueEfficiency;
        if (state.realmLayer >= 3 && state.naqiProgress >= 100f) gain = 0f;
        float actualGain = RealmProgressionRules.AddNaqi(npc, gain, day);
        float controlGain = action.auraControlGain * (outcome == CultivationActionOutcome.Failed ? 0.25f : outcome == CultivationActionOutcome.Excellent ? 1.5f : 1f);
        state.auraControl = Mathf.Clamp(state.auraControl + controlGain, 0f, 100f);
        state.fatigue = Mathf.Clamp(state.fatigue + action.fatigueGain, 0f, 100f);
        ApplyTechniqueLearning(npc, action, outcome);
        DailyCultivationResult result = new DailyCultivationResult
        {
            npcId = npc.CharacterId, date = day, absorbedAura = absorbed, consumedAura = consumed,
            selectedActionId = action.id, selectedActionName = action.displayName, outcome = outcome,
            naqiGain = actualGain, auraControlGain = controlGain, fatigueChange = state.fatigue - fatigueBefore,
            layerBefore = layerBefore, layerAfter = state.realmLayer,
            eventDescription = $"{action.displayName}：{(outcome == CultivationActionOutcome.Failed ? "受挫" : outcome == CultivationActionOutcome.Excellent ? "优秀" : "完成")}，纳气 +{actualGain:0.00}%"
        };
        state.latestCultivationResult = result;
        if (outcome == CultivationActionOutcome.Failed)
            state.AddLifeRecord(day, "Cultivation", $"{action.displayName}受挫，但仍积累了少量经验", action.id);
        EventManager.Instance?.TryTriggerSource(EventSource.Training, npc);
        return result;
    }

    public static string SelectActionId(NPCRuntime npc, int day) =>
        npc?.Character == null ? null : SelectAction(npc, day).id;

    public static string ActionName(string actionId) =>
        Actions.FirstOrDefault(item => item.id == actionId)?.displayName ?? "打坐修行";

    public static bool IsActionId(string actionId) =>
        !string.IsNullOrWhiteSpace(actionId) && Actions.Any(item => item.id == actionId);

    public static float ApplyNightLeak(NPCRuntime npc)
    {
        if (npc?.Character == null) return 0f;
        float before = npc.Character.currentAura;
        npc.Character.currentAura = Mathf.Max(0f, before * (1f - SpiritRootRules.LeakageRate(npc.Character.spiritRoot.quality)));
        float leaked = before - npc.Character.currentAura;
        if (npc.Character.latestCultivationResult?.date == (TimeManager.Instance?.CurrentDay ?? -1)) npc.Character.latestCultivationResult.leakedAura = leaked;
        return leaked;
    }

    public static float AddAura(NPCRuntime npc, float amount)
    {
        if (npc?.Character == null || !npc.Character.IsAlive || amount <= 0f) return 0f;
        float before = npc.Character.currentAura;
        npc.Character.currentAura = Mathf.Clamp(before + amount, 0f, AuraCapacity(npc));
        return npc.Character.currentAura - before;
    }

    public static float AddAuraControl(NPCRuntime npc, float amount)
    {
        if (npc?.Character == null || !npc.Character.IsAlive || amount <= 0f) return 0f;
        float before = npc.Character.auraControl;
        npc.Character.auraControl = Mathf.Clamp(before + amount, 0f, 100f);
        return npc.Character.auraControl - before;
    }

    private static float Absorb(NPCRuntime npc)
    {
        CharacterState state = npc.Character;
        float capacity = AuraCapacity(npc);
        float occupancy = capacity <= 0f ? 1f : state.currentAura / capacity;
        float marginal = occupancy < 0.70f ? 1f : occupancy < 0.90f ? 0.70f : 0.30f;
        TechniqueDefinition technique = TechniqueRules.MainTechnique(state);
        float techniqueMultiplier = technique?.absorptionMultiplier ?? 1f;
        if (technique != null)
        {
            float rootAffinity = TechniqueRules.RootAffinity(state.spiritRoot, technique.elements);
            techniqueMultiplier *= TechniqueRules.SoftCompatibility(rootAffinity, EnvironmentAffinity(technique.elements));
        }
        float amount = BaseAbsorption * SpiritRootRules.AbsorptionMultiplier(state.spiritRoot.quality) *
            EnvironmentMultiplier() * techniqueMultiplier * marginal;
        return AddAura(npc, amount);
    }

    private static float EnvironmentMultiplier()
    {
        MapSiteData site = WorldMapProgressRules.GetSectBase(WorldMapSession.Progress);
        WorldMap map = WorldMapSession.Current;
        float aura = site != null && map?.cells != null && site.cellIndex >= 0 && site.cellIndex < map.cells.Length ? map.cells[site.cellIndex].totalAura : 0.5f;
        float multiplier = Mathf.Lerp(0.75f, 1.25f, Mathf.Clamp01(aura));
        if (WorldMapContentEffects.HasDevelopedSite(MapSiteType.SpiritSpring)) multiplier += 0.10f;
        return multiplier;
    }

    private static float EnvironmentAffinity(TechniqueElementProfile profile)
    {
        MapSiteData site = WorldMapProgressRules.GetSectBase(WorldMapSession.Progress);
        WorldMap map = WorldMapSession.Current;
        WorldCell cell = site != null && map?.cells != null && site.cellIndex >= 0 && site.cellIndex < map.cells.Length
            ? map.cells[site.cellIndex] : null;
        if (cell?.elementalAura == null || cell.elementalAura.Total <= 0.0001f || profile == null) return 0.2f;
        float total = cell.elementalAura.Total;
        return Mathf.Clamp01((cell.elementalAura.metal * profile.metal + cell.elementalAura.wood * profile.wood +
            cell.elementalAura.water * profile.water + cell.elementalAura.fire * profile.fire +
            cell.elementalAura.earth * profile.earth) / total);
    }

    private static CultivationActionDefinition SelectAction(NPCRuntime npc, int day)
    {
        float[] weights = { 1f, 1f, 1f };
        CharacterState state = npc.Character;
        if (state.HasTrait("diligent")) { weights[0] += 1f; weights[1] += 0.5f; }
        if (state.HasTrait("lazy")) weights[1] += 0.5f;
        if (state.HasTrait("reckless")) weights[2] += 1f;
        if (state.HasTrait("ambitious")) weights[2] += 0.75f;
        if (state.HasTrait("cautious")) weights[2] *= 0.5f;
        for (int index = 0; index < Actions.Length; index++)
            weights[index] = Mathf.Max(0.05f, weights[index] + TechniqueRules.ApplicationScore(state, Actions[index].tags));
        weights[2] *= Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(state.auraControl / 100f));
        weights[0] *= Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(state.fatigue / 100f));
        float roll = Stable01(day, npc.CharacterId, "action") * weights.Sum();
        for (int index = 0; index < Actions.Length; index++) { roll -= weights[index]; if (roll <= 0f) return Actions[index]; }
        return Actions[Actions.Length - 1];
    }

    private static CultivationActionOutcome ResolveOutcome(NPCRuntime npc, CultivationActionDefinition action, int day)
    {
        float roll = Stable01(day, npc.CharacterId, action.id);
        if (action.controlCheck)
        {
            float stability = TechniqueRules.MainTechnique(npc.Character)?.stabilityModifier ?? 0f;
            float success = Mathf.Clamp(0.35f + npc.Character.auraControl * 0.005f - npc.Character.fatigue * 0.003f + stability, 0.20f, 0.90f);
            if (roll > success) return CultivationActionOutcome.Failed;
        }
        float excellent = Mathf.Clamp(0.05f + npc.Character.auraControl * 0.001f, 0.05f, 0.15f);
        return roll < excellent ? CultivationActionOutcome.Excellent : CultivationActionOutcome.Qualified;
    }

    private static void ApplyTechniqueLearning(NPCRuntime npc, CultivationActionDefinition action,
        CultivationActionOutcome outcome)
    {
        if (npc?.Character == null || action == null || string.IsNullOrWhiteSpace(npc.Character.mainTechniqueId)) return;
        float quality = outcome == CultivationActionOutcome.Failed ? 0.25f :
            outcome == CultivationActionOutcome.Excellent ? 1.5f : 1f;
        float currentUnderstanding = TechniqueRules.MainUnderstanding(npc.Character);
        SectTechniqueState sectState = TechniqueRules.SectState(PlayerManager.Instance?.playerData, npc.Character.mainTechniqueId);
        float comprehension = Mathf.Clamp(0.75f + npc.Comprehension / 40f, 0.75f, 1.25f);
        float personalGain = 0.5f * quality * comprehension *
            TechniqueRules.LearningAnnotationMultiplier(npc.Character, sectState);
        float contribution = action.techniqueDifficulty * quality *
            TechniqueRules.StageContributionMultiplier(currentUnderstanding);
        PlayerManager.Instance?.AddSectTechniqueMastery(npc.Character.mainTechniqueId, contribution, npc);
        PlayerManager.Instance?.AddTechniqueUnderstanding(personalGain, npc);
    }

    private static float Stable01(int day, string characterId, string salt)
    {
        unchecked
        {
            uint hash = 2166136261u;
            int seed = EventManager.Instance == null ? 48621 : EventManager.Instance.RandomSeed;
            hash = (hash ^ (uint)seed) * 16777619u; hash = (hash ^ (uint)day) * 16777619u;
            foreach (char c in (characterId ?? string.Empty) + ":" + salt) hash = (hash ^ c) * 16777619u;
            return (hash % 1000000u) / 1000000f;
        }
    }
}

public static class RealmProgressionRules
{
    public static float AddNaqi(NPCRuntime npc, float amount, int day)
    {
        if (npc?.Character == null || amount <= 0f) return 0f;
        CharacterState state = npc.Character;
        if (state.realmLayer >= 3 && state.naqiProgress >= 100f) return 0f;
        float remaining = amount, applied = 0f;
        while (remaining > 0f)
        {
            float part = Mathf.Min(100f - state.naqiProgress, remaining);
            state.naqiProgress += part; applied += part; remaining -= part;
            if (state.naqiProgress < 100f) break;
            if (state.realmLayer >= 3)
            {
                state.naqiProgress = 100f;
                state.AddLifeRecord(day, "Cultivation", "练气三层圆满", "qi_refining_complete");
                TimeManager.Instance?.RecordDayNotice($"{state.displayName} 已达练气圆满");
                break;
            }
            state.realmLayer++; state.naqiProgress = 0f;
            state.AddLifeRecord(day, "Cultivation", $"自动进入练气{state.realmLayer}层", "qi_refining_layer");
            TimeManager.Instance?.RecordDayNotice($"{state.displayName} 进入练气{state.realmLayer}层");
        }
        return applied;
    }
}
