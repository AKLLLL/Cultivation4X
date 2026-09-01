using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

public static class ExperienceRecordRules
{
    public static LifeRecord AddLegacy(CharacterState character, int day, string category, string text,
        string sourceId = null)
    {
        ExperienceType type = TypeForCategory(category);
        ExperienceImportance importance = type == ExperienceType.Death || type == ExperienceType.NearDeath ||
            type == ExperienceType.Relationship ? ExperienceImportance.Major :
            type == ExperienceType.InternalDecision ? ExperienceImportance.Internal : ExperienceImportance.Minor;
        return Add(character, day, type, importance, null, text, sourceId, null,
            importance == ExperienceImportance.Major ? ExperienceRetention.Chronicle : ExperienceRetention.Recent);
    }

    public static LifeRecord Add(CharacterState character, int day, ExperienceType type,
        ExperienceImportance importance, string descriptionKey, string fallbackText, string sourceId = null,
        IEnumerable<ExperienceValue> values = null, ExperienceRetention retention = ExperienceRetention.Recent)
    {
        if (character == null) return null;
        if (importance == ExperienceImportance.Major) retention = ExperienceRetention.Chronicle;
        if (importance == ExperienceImportance.Internal) retention = ExperienceRetention.Recent;
        character.lifeRecords = character.lifeRecords ?? new List<LifeRecord>();
        List<ExperienceValue> valueList = (values ?? Enumerable.Empty<ExperienceValue>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.key))
            .Select(item => new ExperienceValue { key = item.key, value = item.value ?? string.Empty })
            .OrderBy(item => item.key, StringComparer.Ordinal).ToList();
        string id = BuildId(character.characterId, day, type, sourceId, descriptionKey, fallbackText, valueList);
        LifeRecord existing = character.lifeRecords.FirstOrDefault(item => item?.id == id);
        if (existing != null) return existing;
        LifeRecord record = new LifeRecord
        {
            id = id,
            day = Mathf.Max(0, day),
            category = CategoryForType(type),
            text = fallbackText ?? string.Empty,
            sourceId = sourceId,
            type = type,
            importance = importance,
            retention = retention,
            descriptionKey = descriptionKey,
            values = valueList
        };
        character.lifeRecords.Add(record);
        return record;
    }

    public static string Format(LifeRecord record)
    {
        if (record == null) return string.Empty;
        string Value(string key) => record.values?.FirstOrDefault(item => item?.key == key)?.value ?? string.Empty;
        switch (record.descriptionKey)
        {
            case "qi_layer": return $"经过长期纳气修行，顺利突破至练气{Value("layer")}层。";
            case "qi_complete": return "纳气渐趋圆满，练气阶段的修行已臻当前极限。";
            case "technique_stage": return FormatTechniqueStage(Value("technique"), Value("after"));
            case "technique_understanding": return $"长期修习《{Value("technique")}》，对其中的灵气运行方式理解更深。";
            case "control_first_success": return $"第一次成功完成{Value("action")}，对天地灵气的掌控有了新的体会。";
            case "control_first_failure": return $"第一次尝试{Value("action")}未能成功，但从中积累了经验。";
            case "control_threshold": return FormatControlMilestone(Value("threshold"));
            case "plan_training_started": return "按照月度计划，开始本月修炼安排。";
            case "plan_training_completed": return FormatTrainingCompletion(Value("grade"), Value("completion"));
            case "plan_duty_started": return string.IsNullOrWhiteSpace(Value("department"))
                ? "按照月度安排，开始处理本月宗门事务。"
                : $"按照宗门安排，本月开始参与{Value("department")}事务。";
            case "plan_duty_completed": return FormatDutyCompletion(Value("department"), Value("completion"));
            case "repeated_action_pattern": return FormatRepeatedAction(Value("activity"), Value("action"), record.sourceId);
            case "fatigue_accumulated": return Value("severity") == "severe"
                ? "近期修炼与事务过于频繁，身心疲惫，需要一段时间缓解。"
                : "连续多日忙碌后渐感疲惫，开始有意调整自己的节奏。";
            case "fatigue_recovered": return "经过一段时间调整，先前积累的疲惫逐渐消退。";
            case "department_joined": return $"加入{Value("department")}，开始承担新的宗门职责。";
            case "department_left": return $"离开{Value("department")}，结束了在其中承担的事务。";
            case "department_leader_started": return $"被任命为{Value("department")}负责人，开始统筹相关事务。";
            case "department_leader_ended": return $"不再担任{Value("department")}负责人。";
            case "injury": return "修行途中受伤，不得不暂缓行动进行休养。";
            case "permanent_trauma": return "遭受永久创伤";
            default: return record.text ?? string.Empty;
        }
    }

    public static ExperienceValue Value(string key, object value) => new ExperienceValue
    {
        key = key,
        value = Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    public static string BuildId(string characterId, int day, ExperienceType type, string sourceId,
        string descriptionKey, string text, IEnumerable<ExperienceValue> values)
    {
        List<ExperienceValue> identityValues = (values ?? Enumerable.Empty<ExperienceValue>()).ToList();
        string identityText = identityValues.Count > 0 ? string.Empty : text ?? string.Empty;
        string payload = string.Join("|", characterId ?? string.Empty, day.ToString(CultureInfo.InvariantCulture),
            type.ToString(), sourceId ?? string.Empty, descriptionKey ?? string.Empty, identityText,
            string.Join(";", identityValues.Select(item =>
                $"{item?.key ?? string.Empty}={item?.value ?? string.Empty}")));
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char character in payload) hash = (hash ^ character) * 16777619u;
            return $"experience_{hash:x8}";
        }
    }

    public static ExperienceType TypeForCategory(string category)
    {
        switch (category)
        {
            case "Decision": return ExperienceType.InternalDecision;
            case "Recruit": return ExperienceType.Recruitment;
            case "Cultivation": return ExperienceType.CultivationMilestone;
            case "Mission": return ExperienceType.Mission;
            case "Event": return ExperienceType.Event;
            case "Relationship": return ExperienceType.Relationship;
            case "Injury": return ExperienceType.Injury;
            case "NearDeath": return ExperienceType.NearDeath;
            case "Death": return ExperienceType.Death;
            case "Plan": return ExperienceType.PlanPhaseCompleted;
            case "Department": return ExperienceType.DepartmentChange;
            case "Fatigue": return ExperienceType.FatigueAccumulated;
            default: return ExperienceType.Other;
        }
    }

    public static string CategoryForType(ExperienceType type)
    {
        switch (type)
        {
            case ExperienceType.InternalDecision: return "Decision";
            case ExperienceType.Recruitment: return "Recruit";
            case ExperienceType.CultivationMilestone: return "Cultivation";
            case ExperienceType.TechniqueMilestone: return "Technique";
            case ExperienceType.AuraControlMilestone: return "Cultivation";
            case ExperienceType.Mission: return "Mission";
            case ExperienceType.Event: return "Event";
            case ExperienceType.Relationship: return "Relationship";
            case ExperienceType.Injury: return "Injury";
            case ExperienceType.NearDeath: return "NearDeath";
            case ExperienceType.Death: return "Death";
            case ExperienceType.PlanPhaseStarted:
            case ExperienceType.PlanPhaseCompleted: return "Plan";
            case ExperienceType.RepeatedActionPattern: return "Cultivation";
            case ExperienceType.FatigueAccumulated:
            case ExperienceType.FatigueRecovered: return "Fatigue";
            case ExperienceType.TechniqueUnderstanding: return "Technique";
            case ExperienceType.DepartmentChange: return "Department";
            default: return "Other";
        }
    }

    private static string FormatTechniqueStage(string technique, string stage)
    {
        if (stage == "理解") return $"对《{technique}》的理解更进一步，已经不再只是照本宣科地运转灵气。";
        if (stage == "融汇") return $"逐渐能够将《{technique}》的运行方式融入自己的修炼习惯。";
        if (stage == "圆满") return $"对《{technique}》的理解已十分深入，开始产生属于自己的见解。";
        return $"对《{technique}》的理解迈入了新的阶段。";
    }

    private static string FormatControlMilestone(string threshold)
    {
        if (threshold == "100") return "对天地灵气的掌控已臻当前阶段的圆熟。";
        if (threshold == "75") return "操控天地灵气时愈发从容。";
        if (threshold == "50") return "对灵气运转已有了较为稳固的把握。";
        return "对天地灵气的掌控更加熟练。";
    }

    private static string FormatTrainingCompletion(string grade, string completion)
    {
        if (completion == "partial") return "本月修炼安排告一段落，期间部分安排未能践行。";
        if (grade == "significant") return "本月修炼安排完成，修为较此前已有明显进境。";
        if (grade == "normal") return "本月修炼安排完成，境界有所精进。";
        return "本月修炼安排完成，修行略有所得。";
    }

    private static string FormatDutyCompletion(string department, string completion)
    {
        if (completion == "partial") return string.IsNullOrWhiteSpace(department)
            ? "本月宗门事务安排告一段落，期间部分事务未能完成。"
            : $"本月{department}事务告一段落，期间部分安排未能完成。";
        return string.IsNullOrWhiteSpace(department)
            ? "本月宗门事务安排告一段落。"
            : $"本月{department}事务告一段落，对相关事务愈发熟悉。";
    }

    private static string FormatRepeatedAction(string activity, string action, string sourceId)
    {
        if (sourceId == "meditate_refine") return "近期多次选择打坐炼化，逐渐习惯这种稳健的修炼方式。";
        if (sourceId == "basic_practice") return "近来的修炼多以基础练功为主，对主动调用灵气愈发熟练。";
        if (sourceId == "aura_circulation") return "连续多次尝试灵气运转，对灵气控制逐渐形成了自己的体会。";
        return activity == "sect_duty"
            ? $"近期多次参与{action}，对相关宗门事务愈发熟悉。"
            : $"近期多次选择{action}，逐渐形成了自己的修行习惯。";
    }
}

public static class DiscipleCurrentStateBuilder
{
    public static DiscipleCurrentState Build(NPCRuntime npc)
    {
        if (npc?.Character == null) return null;
        CharacterState character = npc.Character;
        DiscipleDailySchedule schedule = TimeManager.Instance?.GetSchedule(npc.CharacterId);
        float hour = TimeManager.Instance?.CurrentHour ?? 6f;
        DailyScheduleSegment segment = schedule?.SegmentAt(hour);
        DiscipleActivityKind actual = ActivityKind(schedule?.activity ?? MonthlyActivityType.Free);
        string actionId = segment?.actionId;
        string actionName = segment?.label;
        Mission mission = npc.CurrentMission;
        if (mission != null && (mission.State == MissionState.Active || mission.State == MissionState.WaitingNode))
        {
            actual = DiscipleActivityKind.Mission;
            actionId = mission.Data?.id;
            actionName = mission.Data?.name ?? "执行任务";
        }
        else if (!character.IsAlive || npc.State != NPCState.Idle || character.health != HealthState.Healthy)
        {
            actual = DiscipleActivityKind.Recovery;
            actionId = "recovery";
            actionName = character.IsAlive ? HealthName(character.health) : "已故";
        }
        else if (hour < 6f || hour >= 20f)
        {
            actual = DiscipleActivityKind.Recovery;
            actionId = "rest";
            actionName = "休息";
        }
        else if (string.IsNullOrWhiteSpace(actionName))
        {
            actionId = schedule?.cultivationActionId;
            actionName = actual == DiscipleActivityKind.Training ? DailyCultivationSimulator.ActionName(actionId) :
                actual == DiscipleActivityKind.SectDuty ? "宗门事务" : "自由活动";
        }
        TechniqueDefinition technique = TechniqueRules.MainTechnique(character);
        float understanding = TechniqueRules.MainUnderstanding(character);
        return new DiscipleCurrentState
        {
            discipleId = npc.CharacterId,
            displayName = character.displayName,
            realmDisplayName = character.realm == CultivationRealm.QiRefining ? $"练气{character.realmLayer}层" : character.realm.ToString(),
            naqiProgress = character.naqiProgress,
            scheduledActivity = schedule?.activity ?? MonthlyPlanRules.ActivityFor(npc, TimeManager.Instance?.ActiveDay ?? 1),
            actualActivity = actual,
            currentActionId = actionId,
            currentActionDisplayName = actionName,
            currentAura = character.currentAura,
            auraCapacity = DailyCultivationSimulator.AuraCapacity(npc),
            auraControl = character.auraControl,
            fatigue = character.fatigue,
            mainTechniqueId = character.mainTechniqueId,
            mainTechniqueName = technique?.name,
            techniqueUnderstanding = understanding,
            techniqueStage = TechniqueRules.PersonalStage(understanding)
        };
    }

    public static DiscipleActivityKind ActivityKind(MonthlyActivityType activity)
    {
        switch (activity)
        {
            case MonthlyActivityType.Training: return DiscipleActivityKind.Training;
            case MonthlyActivityType.SectDuty: return DiscipleActivityKind.SectDuty;
            default: return DiscipleActivityKind.Free;
        }
    }

    public static string ActivityName(DiscipleActivityKind activity)
    {
        switch (activity)
        {
            case DiscipleActivityKind.Training: return "修炼";
            case DiscipleActivityKind.SectDuty: return "宗务";
            case DiscipleActivityKind.Mission: return "任务";
            case DiscipleActivityKind.Recovery: return "休养";
            default: return "自由";
        }
    }

    private static string HealthName(HealthState health)
    {
        switch (health)
        {
            case HealthState.LightInjury: return "轻伤休养";
            case HealthState.HeavyInjury: return "重伤休养";
            case HealthState.SeriousInjury: return "严重受伤";
            case HealthState.PermanentTrauma: return "永久创伤";
            case HealthState.Dead: return "已故";
            default: return "休养";
        }
    }
}

public static class GrowthFeedbackRules
{
    public const int ReportRetentionCount = 12;
    public const int MinorRetentionDays = 360;
    public const int InternalRetentionDays = 2;
    public static void ProcessSettledDay(PlayerData player, int day, DaySettlementSummary summary)
    {
        if (player == null || summary == null || day <= 0) return;
        Normalize(player);
        SectGrowthFeedbackState state = player.growthFeedback;
        if (state.lastProcessedDay >= day) return;
        int monthIndex = MonthlyPlanRules.MonthIndex(day);
        if (state.currentStats.Count == 0) state.activeMonthIndex = monthIndex;
        if (state.activeMonthIndex != monthIndex)
            throw new InvalidOperationException($"成长反馈月份错位: {state.activeMonthIndex} != {monthIndex}");

        foreach (DiscipleDayResult result in summary.discipleResults ?? new List<DiscipleDayResult>())
        {
            if (result == null || string.IsNullOrWhiteSpace(result.discipleId)) continue;
            NPCRuntime npc = NPCManager.Instance?.GetRuntime(result.discipleId);
            DiscipleMonthlyStats stats = AddDay(state, result, monthIndex);
            ExperienceGenerator.GenerateSettledDayExperiences(player, npc?.Character, result, stats);
            result.newExperienceIds = npc?.Character?.lifeRecords?
                .Where(record => record != null && record.day == day && record.importance != ExperienceImportance.Internal)
                .Select(record => record.id).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList()
                ?? new List<string>();
            foreach (string id in result.newExperienceIds)
                if (!stats.experienceIds.Contains(id)) stats.experienceIds.Add(id);
        }
        AddExperienceOnlyParticipants(state, day, monthIndex);
        foreach (ItemDayChange change in summary.itemChanges ?? new List<ItemDayChange>())
            AddItemChange(state.currentItemChanges, change.itemId, change.countChange);
        state.lastProcessedDay = day;
        PruneExperiences(day);
        if (day % GameCalendarRules.DaysPerMonth == 0) FinalizeMonth(state, day, summary.resourceProduction);
    }

    public static void Normalize(PlayerData player)
    {
        if (player == null) return;
        player.growthFeedback = player.growthFeedback ?? new SectGrowthFeedbackState();
        SectGrowthFeedbackState state = player.growthFeedback;
        state.activeMonthIndex = Mathf.Max(1, state.activeMonthIndex);
        state.currentStats = (state.currentStats ?? new List<DiscipleMonthlyStats>()).Where(item => item != null).ToList();
        state.currentItemChanges = (state.currentItemChanges ?? new List<ItemMonthChange>()).Where(item => item != null).ToList();
        List<SectMonthlyReport> orderedReports = (state.reports ?? new List<SectMonthlyReport>())
            .Where(item => item != null).OrderBy(item => item.monthIndex).ToList();
        state.reports = orderedReports.Skip(Mathf.Max(0, orderedReports.Count - ReportRetentionCount)).ToList();
        foreach (DiscipleMonthlyStats stats in state.currentStats) NormalizeStats(stats);
        foreach (SectMonthlyReport report in state.reports)
        {
            report.disciples = (report.disciples ?? new List<DiscipleMonthlyStats>()).Where(item => item != null).ToList();
            report.highlightDiscipleIds = report.highlightDiscipleIds ?? new List<string>();
            report.highlightExperienceIds = report.highlightExperienceIds ?? new List<string>();
            report.itemChanges = report.itemChanges ?? new List<ItemMonthChange>();
            report.resourceProduction = report.resourceProduction ?? new List<ResourceProductionRecord>();
            foreach (DiscipleMonthlyStats stats in report.disciples) NormalizeStats(stats);
        }
    }

    public static SectMonthlyReport LatestReport(PlayerData player) =>
        player?.growthFeedback?.reports?.Where(item => item != null).OrderByDescending(item => item.monthIndex).FirstOrDefault();

    private static DiscipleMonthlyStats AddDay(SectGrowthFeedbackState state, DiscipleDayResult result, int monthIndex)
    {
        DiscipleMonthlyStats stats = state.currentStats.FirstOrDefault(item => item.discipleId == result.discipleId);
        if (stats == null)
        {
            stats = new DiscipleMonthlyStats
            {
                discipleId = result.discipleId, displayName = result.displayName, monthIndex = monthIndex,
                firstDay = result.worldDay, realmLayerStart = result.realmLayerBefore,
                naqiProgressStart = result.naqiBefore, techniqueIdStart = result.techniqueId,
                techniqueStageStart = result.techniqueStageBefore
            };
            state.currentStats.Add(stats);
        }
        if (stats.lastDay >= result.worldDay) return stats;
        stats.lastDay = result.worldDay;
        stats.settledDays++;
        stats.displayName = result.displayName;
        if (result.scheduledActivity == MonthlyActivityType.Training) stats.plannedTrainingDays++;
        else if (result.scheduledActivity == MonthlyActivityType.SectDuty) stats.plannedSectDutyDays++;
        else stats.plannedFreeDays++;
        switch (result.actualActivity)
        {
            case DiscipleActivityKind.Training: stats.actualTrainingDays++; break;
            case DiscipleActivityKind.SectDuty: stats.actualSectDutyDays++; break;
            case DiscipleActivityKind.Mission: stats.missionDays++; break;
            case DiscipleActivityKind.Recovery: stats.recoveryDays++; break;
            default: stats.actualFreeDays++; break;
        }
        stats.naqiGain += Mathf.Max(0f, result.naqiGain);
        stats.auraControlGain += result.auraControlGain;
        stats.techniqueProgressGain += result.techniqueProgressGain;
        stats.maxFatigue = Mathf.Max(stats.maxFatigue, result.fatiguePeak);
        stats.realmLayerEnd = result.realmLayerAfter;
        stats.naqiProgressEnd = result.naqiAfter;
        stats.techniqueIdEnd = result.techniqueId;
        stats.techniqueStageEnd = result.techniqueStageAfter;
        if (!string.IsNullOrWhiteSpace(result.actionId))
        {
            MonthlyActionCount count = stats.actionCounts.FirstOrDefault(item => item.actionId == result.actionId);
            if (count == null)
            {
                count = new MonthlyActionCount { actionId = result.actionId, displayName = result.actionDisplayName };
                stats.actionCounts.Add(count);
            }
            count.count++;
        }
        foreach (string id in result.newExperienceIds ?? new List<string>())
            if (!stats.experienceIds.Contains(id)) stats.experienceIds.Add(id);
        return stats;
    }

    private static void AddExperienceOnlyParticipants(SectGrowthFeedbackState state, int day, int monthIndex)
    {
        foreach (NPCRuntime npc in NPCManager.Instance?.GetAllNPC() ?? new List<NPCRuntime>())
        {
            CharacterState character = npc?.Character;
            List<string> ids = character?.lifeRecords?
                .Where(record => record != null && record.day == day &&
                    record.importance != ExperienceImportance.Internal && !string.IsNullOrWhiteSpace(record.id))
                .Select(record => record.id).Distinct().ToList() ?? new List<string>();
            if (ids.Count == 0) continue;
            DiscipleMonthlyStats stats = state.currentStats.FirstOrDefault(item => item.discipleId == npc.CharacterId);
            if (stats == null)
            {
                float understanding = TechniqueRules.MainUnderstanding(character);
                stats = new DiscipleMonthlyStats
                {
                    discipleId = npc.CharacterId, displayName = character.displayName, monthIndex = monthIndex,
                    firstDay = day, lastDay = day, realmLayerStart = character.realmLayer,
                    realmLayerEnd = character.realmLayer, naqiProgressStart = character.naqiProgress,
                    naqiProgressEnd = character.naqiProgress, techniqueIdStart = character.mainTechniqueId,
                    techniqueIdEnd = character.mainTechniqueId,
                    techniqueStageStart = TechniqueRules.PersonalStage(understanding),
                    techniqueStageEnd = TechniqueRules.PersonalStage(understanding)
                };
                state.currentStats.Add(stats);
            }
            foreach (string id in ids)
                if (!stats.experienceIds.Contains(id)) stats.experienceIds.Add(id);
        }
    }

    private static void FinalizeMonth(SectGrowthFeedbackState state, int day,
        IEnumerable<ResourceProductionRecord> production)
    {
        int monthIndex = MonthlyPlanRules.MonthIndex(day);
        string reportId = $"growth_month_{monthIndex}";
        if (state.lastFinalizedMonthIndex >= monthIndex || state.reports.Any(item => item?.id == reportId)) return;
        GameDateTime date = GameCalendarRules.FromActiveDay(day, 0f);
        SectMonthlyReport report = new SectMonthlyReport
        {
            id = reportId, monthIndex = monthIndex, year = date.year, month = date.month,
            startDay = day - GameCalendarRules.DaysPerMonth + 1, endDay = day,
            disciples = state.currentStats.Select(CloneStats).ToList(),
            itemChanges = state.currentItemChanges.Select(item => new ItemMonthChange
                { itemId = item.itemId, countChange = item.countChange }).ToList(),
            resourceProduction = (production ?? Enumerable.Empty<ResourceProductionRecord>()).Select(CloneProduction).ToList()
        };
        report.highlightDiscipleIds = RankHighlights(report).Take(8).ToList();
        HashSet<string> highlighted = new HashSet<string>(report.highlightDiscipleIds);
        report.highlightExperienceIds = report.disciples.Where(item => highlighted.Contains(item.discipleId))
            .SelectMany(item => item.experienceIds).Distinct().ToList();
        state.reports.Add(report);
        state.reports = state.reports.OrderBy(item => item.monthIndex).ToList();
        if (state.reports.Count > ReportRetentionCount)
            state.reports.RemoveRange(0, state.reports.Count - ReportRetentionCount);
        state.lastFinalizedMonthIndex = monthIndex;
        state.activeMonthIndex = monthIndex + 1;
        state.currentStats.Clear();
        state.currentItemChanges.Clear();
    }

    private static IEnumerable<string> RankHighlights(SectMonthlyReport report)
    {
        Dictionary<string, LifeRecord> records = NPCManager.Instance?.GetAllNPC()
            .Where(npc => npc?.Character?.lifeRecords != null)
            .SelectMany(npc => npc.Character.lifeRecords)
            .Where(record => record != null && record.day >= report.startDay && record.day <= report.endDay)
            .GroupBy(record => record.id).ToDictionary(group => group.Key, group => group.First())
            ?? new Dictionary<string, LifeRecord>();
        return report.disciples.Select(stats =>
            {
                List<LifeRecord> experiences = stats.experienceIds.Where(records.ContainsKey).Select(id => records[id]).ToList();
                int priority = experiences.Any(item => item.type == ExperienceType.Death || item.type == ExperienceType.NearDeath ||
                    item.type == ExperienceType.CultivationMilestone) ? 1 :
                    experiences.Any(item => item.importance == ExperienceImportance.Major) ? 2 :
                    experiences.Any(item => item.type == ExperienceType.AuraControlMilestone) ? 3 :
                    stats.naqiGain >= 20f || stats.techniqueProgressGain >= 10f || stats.auraControlGain >= 5f ? 4 : 99;
                int recent = experiences.Count == 0 ? stats.lastDay : experiences.Max(item => item.day);
                float growth = stats.naqiGain + stats.techniqueProgressGain + stats.auraControlGain;
                return new { stats.discipleId, priority, recent, growth };
            })
            .Where(item => item.priority < 99)
            .OrderBy(item => item.priority).ThenByDescending(item => item.recent)
            .ThenByDescending(item => item.growth).ThenBy(item => item.discipleId, StringComparer.Ordinal)
            .Select(item => item.discipleId);
    }

    private static void PruneExperiences(int day)
    {
        foreach (NPCRuntime npc in NPCManager.Instance?.GetAllNPC() ?? new List<NPCRuntime>())
        {
            CharacterState character = npc?.Character;
            if (character?.lifeRecords == null) continue;
            character.lifeRecords.RemoveAll(record => record == null ||
                record.importance == ExperienceImportance.Internal && record.day < day - InternalRetentionDays ||
                record.importance == ExperienceImportance.Minor && record.retention == ExperienceRetention.Recent &&
                record.day < day - MinorRetentionDays);
        }
    }

    private static void AddItemChange(List<ItemMonthChange> changes, string itemId, int count)
    {
        if (string.IsNullOrWhiteSpace(itemId) || count == 0) return;
        ItemMonthChange item = changes.FirstOrDefault(entry => entry.itemId == itemId);
        if (item == null) { item = new ItemMonthChange { itemId = itemId }; changes.Add(item); }
        item.countChange += count;
    }

    private static void NormalizeStats(DiscipleMonthlyStats stats)
    {
        stats.actionCounts = (stats.actionCounts ?? new List<MonthlyActionCount>()).Where(item => item != null).ToList();
        stats.experienceIds = (stats.experienceIds ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        stats.narrative = stats.narrative ?? new DiscipleMonthlyNarrativeState();
        stats.narrative.repeatedPatternActivities = (stats.narrative.repeatedPatternActivities ?? new List<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct().ToList();
    }

    private static DiscipleMonthlyStats CloneStats(DiscipleMonthlyStats source) => new DiscipleMonthlyStats
    {
        discipleId = source.discipleId, displayName = source.displayName, monthIndex = source.monthIndex,
        firstDay = source.firstDay, lastDay = source.lastDay, settledDays = source.settledDays,
        plannedTrainingDays = source.plannedTrainingDays, plannedSectDutyDays = source.plannedSectDutyDays,
        plannedFreeDays = source.plannedFreeDays, actualTrainingDays = source.actualTrainingDays,
        actualSectDutyDays = source.actualSectDutyDays, actualFreeDays = source.actualFreeDays,
        missionDays = source.missionDays, recoveryDays = source.recoveryDays, naqiGain = source.naqiGain,
        auraControlGain = source.auraControlGain, techniqueProgressGain = source.techniqueProgressGain,
        maxFatigue = source.maxFatigue, realmLayerStart = source.realmLayerStart, realmLayerEnd = source.realmLayerEnd,
        naqiProgressStart = source.naqiProgressStart, naqiProgressEnd = source.naqiProgressEnd,
        techniqueIdStart = source.techniqueIdStart, techniqueIdEnd = source.techniqueIdEnd,
        techniqueStageStart = source.techniqueStageStart, techniqueStageEnd = source.techniqueStageEnd,
        actionCounts = source.actionCounts.Select(item => new MonthlyActionCount
            { actionId = item.actionId, displayName = item.displayName, count = item.count }).ToList(),
        experienceIds = new List<string>(source.experienceIds),
        narrative = CloneNarrative(source.narrative)
    };

    private static DiscipleMonthlyNarrativeState CloneNarrative(DiscipleMonthlyNarrativeState source) =>
        source == null ? new DiscipleMonthlyNarrativeState() : new DiscipleMonthlyNarrativeState
        {
            trainingStarted = source.trainingStarted,
            trainingCompleted = source.trainingCompleted,
            sectDutyStarted = source.sectDutyStarted,
            sectDutyCompleted = source.sectDutyCompleted,
            fatigueStageRecorded = source.fatigueStageRecorded,
            fatigueRecoveryRecorded = source.fatigueRecoveryRecorded,
            techniqueUnderstandingRecorded = source.techniqueUnderstandingRecorded,
            repeatedPatternActivities = new List<string>(source.repeatedPatternActivities ?? new List<string>())
        };

    private static ResourceProductionRecord CloneProduction(ResourceProductionRecord source) => new ResourceProductionRecord
    {
        nodeId = source.nodeId, siteName = source.siteName, itemId = source.itemId,
        calculated = source.calculated, received = source.received, lost = source.lost
    };
}
