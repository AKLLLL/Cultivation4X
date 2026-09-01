using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

/// <summary>
/// 统一生成弟子结构化经历。领域状态变化仍由其唯一写入入口记录，
/// 本类只消费已经发生的日结果与组织变更，不重复计算成长或行动成功率。
/// </summary>
public static class ExperienceGenerator
{
    public static bool WriteDecisionRecord(NPCRuntime npc, ActionDefinition action,
        string reasonLabel, int day)
    {
        if (npc?.Character == null || action == null) return false;
        string reason = string.IsNullOrWhiteSpace(reasonLabel) ? action.displayName : reasonLabel;
        ExperienceRecordRules.Add(npc.Character, day, ExperienceType.InternalDecision,
            ExperienceImportance.Internal, "decision", $"因{reason}，决定{action.displayName}", action.id);
        return true;
    }

    /// <summary>当日是否已写决策履历（读档/重复结算的幂等防护）。</summary>
    public static bool HasDecisionRecordOn(NPCRuntime npc, int day)
    {
        return npc?.Character?.lifeRecords != null &&
            npc.Character.lifeRecords.Any(record =>
                record != null && record.day == day && record.category == "Decision");
    }

    public static void GenerateSettledDayExperiences(PlayerData player, CharacterState character,
        DiscipleDayResult result, DiscipleMonthlyStats stats)
    {
        if (player == null || character == null || result == null || stats == null) return;
        stats.narrative = stats.narrative ?? new DiscipleMonthlyNarrativeState();
        stats.narrative.repeatedPatternActivities = stats.narrative.repeatedPatternActivities ??
            new List<string>();

        GenerateCultivationMilestones(character, result, stats);
        GeneratePlanPhases(player, character, result, stats);
        GenerateRepeatedAction(character, result, stats);
        GenerateFatigueNarrative(character, result, stats);
        GenerateTechniqueNarrative(character, result, stats);
    }

    public static LifeRecord WriteDepartmentChange(CharacterState character, int day,
        SectDepartmentState department, string descriptionKey)
    {
        if (character == null || department == null || string.IsNullOrWhiteSpace(descriptionKey)) return null;
        return ExperienceRecordRules.Add(character, day, ExperienceType.DepartmentChange,
            ExperienceImportance.Major, descriptionKey, null,
            $"{department.departmentId}:{descriptionKey}", new[]
            {
                ExperienceRecordRules.Value("department", department.name)
            }, ExperienceRetention.Chronicle);
    }

    private static void GenerateCultivationMilestones(CharacterState character,
        DiscipleDayResult result, DiscipleMonthlyStats stats)
    {
        if (result.techniqueStageAfter > result.techniqueStageBefore &&
            !string.IsNullOrWhiteSpace(result.techniqueId))
        {
            string name = TechniqueRules.Get(result.techniqueId)?.name ?? result.techniqueId;
            ExperienceRecordRules.Add(character, result.worldDay, ExperienceType.TechniqueMilestone,
                ExperienceImportance.Major, "technique_stage", null, result.techniqueId, new[]
                {
                    ExperienceRecordRules.Value("technique", name),
                    ExperienceRecordRules.Value("before", TechniqueRules.PersonalStageName(result.techniqueStageBefore)),
                    ExperienceRecordRules.Value("after", TechniqueRules.PersonalStageName(result.techniqueStageAfter))
                });
            stats.narrative.techniqueUnderstandingRecorded = true;
        }

        DailyCultivationResult cultivation = result.cultivationResult;
        CultivationActionDefinition action = DailyCultivationSimulator.Definitions
            .FirstOrDefault(item => item?.id == cultivation?.selectedActionId);
        if (action?.controlCheck == true)
        {
            bool success = cultivation.outcome != CultivationActionOutcome.Failed;
            string key = success ? "control_first_success" : "control_first_failure";
            bool seen = character.lifeRecords.Any(record => record != null && record.descriptionKey == key &&
                record.sourceId == action.id && record.day < result.worldDay);
            if (!seen)
                ExperienceRecordRules.Add(character, result.worldDay, ExperienceType.AuraControlMilestone,
                    ExperienceImportance.Minor, key, null, action.id,
                    new[] { ExperienceRecordRules.Value("action", action.displayName) },
                    ExperienceRetention.Chronicle);
        }

        float threshold = new[] { 25f, 50f, 75f, 100f }.Where(value =>
            result.auraControlBefore < value && result.auraControlAfter >= value).DefaultIfEmpty(-1f).Max();
        if (threshold > 0f)
            ExperienceRecordRules.Add(character, result.worldDay, ExperienceType.AuraControlMilestone,
                ExperienceImportance.Minor, "control_threshold", null, $"control_{threshold:0}",
                new[]
                {
                    ExperienceRecordRules.Value("threshold", threshold.ToString("0", CultureInfo.InvariantCulture)),
                    ExperienceRecordRules.Value("before", result.auraControlBefore.ToString("0.#", CultureInfo.InvariantCulture)),
                    ExperienceRecordRules.Value("after", result.auraControlAfter.ToString("0.#", CultureInfo.InvariantCulture))
                }, ExperienceRetention.Chronicle);
    }

    private static void GeneratePlanPhases(PlayerData player, CharacterState character,
        DiscipleDayResult result, DiscipleMonthlyStats stats)
    {
        DiscipleMonthlyNarrativeState narrative = stats.narrative;
        int month = stats.monthIndex;
        if (result.executed && result.actualActivity == DiscipleActivityKind.Training && !narrative.trainingStarted)
        {
            ExperienceRecordRules.Add(character, result.worldDay, ExperienceType.PlanPhaseStarted,
                ExperienceImportance.Minor, "plan_training_started", null,
                $"month_{month}:training:start", new[]
                {
                    ExperienceRecordRules.Value("phase", "training")
                });
            narrative.trainingStarted = true;
        }
        if (result.executed && result.actualActivity == DiscipleActivityKind.SectDuty && !narrative.sectDutyStarted)
        {
            SectDepartmentState department = SectOrganizationRules.DepartmentFor(player, character.characterId);
            List<ExperienceValue> values = DepartmentValues(department).ToList();
            values.Add(ExperienceRecordRules.Value("phase", "sect_duty"));
            ExperienceRecordRules.Add(character, result.worldDay, ExperienceType.PlanPhaseStarted,
                ExperienceImportance.Minor, "plan_duty_started", null,
                $"month_{month}:duty:start", values);
            narrative.sectDutyStarted = true;
        }

        if (!narrative.trainingCompleted && result.scheduledActivity == MonthlyActivityType.Training &&
            !MonthlyPlanRules.HasScheduledActivityAfter(player, character.characterId, result.worldDay,
                MonthlyActivityType.Training) && stats.actualTrainingDays > 0)
        {
            string completion = stats.actualTrainingDays == stats.plannedTrainingDays ? "complete" : "partial";
            ExperienceRecordRules.Add(character, result.worldDay, ExperienceType.PlanPhaseCompleted,
                ExperienceImportance.Minor, "plan_training_completed", null,
                $"month_{month}:training:complete", new[]
                {
                    ExperienceRecordRules.Value("grade", TrainingGrade(stats)),
                    ExperienceRecordRules.Value("completion", completion)
                }, ExperienceRetention.Chronicle);
            narrative.trainingCompleted = true;
        }
        if (!narrative.sectDutyCompleted && result.scheduledActivity == MonthlyActivityType.SectDuty &&
            !MonthlyPlanRules.HasScheduledActivityAfter(player, character.characterId, result.worldDay,
                MonthlyActivityType.SectDuty) && stats.actualSectDutyDays > 0)
        {
            SectDepartmentState department = SectOrganizationRules.DepartmentFor(player, character.characterId);
            string completion = stats.actualSectDutyDays == stats.plannedSectDutyDays ? "complete" : "partial";
            List<ExperienceValue> values = DepartmentValues(department).ToList();
            values.Add(ExperienceRecordRules.Value("completion", completion));
            ExperienceRecordRules.Add(character, result.worldDay, ExperienceType.PlanPhaseCompleted,
                ExperienceImportance.Minor, "plan_duty_completed", null,
                $"month_{month}:duty:complete", values, ExperienceRetention.Chronicle);
            narrative.sectDutyCompleted = true;
        }
    }

    private static void GenerateRepeatedAction(CharacterState character, DiscipleDayResult result,
        DiscipleMonthlyStats stats)
    {
        if (!result.executed || string.IsNullOrWhiteSpace(result.actionId) ||
            result.actualActivity != DiscipleActivityKind.Training &&
            result.actualActivity != DiscipleActivityKind.SectDuty) return;
        string activity = result.actualActivity == DiscipleActivityKind.Training ? "training" : "sect_duty";
        if (stats.narrative.repeatedPatternActivities.Contains(activity)) return;
        MonthlyActionCount count = stats.actionCounts.FirstOrDefault(item => item.actionId == result.actionId);
        if (count == null || count.count < 3) return;
        ExperienceRecordRules.Add(character, result.worldDay, ExperienceType.RepeatedActionPattern,
            ExperienceImportance.Minor, "repeated_action_pattern", null, result.actionId, new[]
            {
                ExperienceRecordRules.Value("activity", activity),
                ExperienceRecordRules.Value("action", result.actionDisplayName ?? result.actionId)
            }, ExperienceRetention.Chronicle);
        stats.narrative.repeatedPatternActivities.Add(activity);
    }

    private static void GenerateFatigueNarrative(CharacterState character, DiscipleDayResult result,
        DiscipleMonthlyStats stats)
    {
        int stage = result.fatigueAfter >= 75f ? 2 : result.fatigueAfter >= 50f ? 1 : 0;
        float threshold = stage == 2 ? 75f : 50f;
        if (stage > stats.narrative.fatigueStageRecorded && result.fatigueBefore < threshold)
        {
            ExperienceRecordRules.Add(character, result.worldDay, ExperienceType.FatigueAccumulated,
                ExperienceImportance.Minor, "fatigue_accumulated", null,
                $"month_{stats.monthIndex}:fatigue:{stage}", new[]
                {
                    ExperienceRecordRules.Value("severity", stage == 2 ? "severe" : "tired")
                });
            stats.narrative.fatigueStageRecorded = stage;
        }

        bool hasUnrecoveredFatigue = LastDay(character, ExperienceType.FatigueAccumulated) >
                                     LastDay(character, ExperienceType.FatigueRecovered);
        if (!stats.narrative.fatigueRecoveryRecorded && hasUnrecoveredFatigue &&
            result.fatigueBefore > 30f && result.fatigueAfter <= 30f)
        {
            ExperienceRecordRules.Add(character, result.worldDay, ExperienceType.FatigueRecovered,
                ExperienceImportance.Minor, "fatigue_recovered", null,
                $"month_{stats.monthIndex}:fatigue:recovered", new[]
                {
                    ExperienceRecordRules.Value("state", "recovered")
                });
            stats.narrative.fatigueRecoveryRecorded = true;
        }
    }

    private static void GenerateTechniqueNarrative(CharacterState character, DiscipleDayResult result,
        DiscipleMonthlyStats stats)
    {
        if (stats.narrative.techniqueUnderstandingRecorded || stats.techniqueProgressGain < 3f ||
            string.IsNullOrWhiteSpace(result.techniqueId)) return;
        string technique = TechniqueRules.Get(result.techniqueId)?.name ?? result.techniqueId;
        ExperienceRecordRules.Add(character, result.worldDay, ExperienceType.TechniqueUnderstanding,
            ExperienceImportance.Minor, "technique_understanding", null,
            $"month_{stats.monthIndex}:{result.techniqueId}:understanding", new[]
            {
                ExperienceRecordRules.Value("technique", technique)
            }, ExperienceRetention.Chronicle);
        stats.narrative.techniqueUnderstandingRecorded = true;
    }

    private static IEnumerable<ExperienceValue> DepartmentValues(SectDepartmentState department)
    {
        if (department == null) return Enumerable.Empty<ExperienceValue>();
        return new[] { ExperienceRecordRules.Value("department", department.name) };
    }

    private static string TrainingGrade(DiscipleMonthlyStats stats)
    {
        if (stats.realmLayerEnd > stats.realmLayerStart || stats.naqiGain >= 20f ||
            stats.auraControlGain >= 5f || stats.techniqueProgressGain >= 10f) return "significant";
        if (stats.naqiGain >= 5f || stats.auraControlGain >= 1f || stats.techniqueProgressGain >= 2f)
            return "normal";
        return "slight";
    }

    private static int LastDay(CharacterState character, ExperienceType type) =>
        character?.lifeRecords?.Where(record => record != null && record.type == type)
            .Select(record => record.day).DefaultIfEmpty(-1).Max() ?? -1;
}
