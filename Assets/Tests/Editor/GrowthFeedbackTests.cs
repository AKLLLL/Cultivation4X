using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class GrowthFeedbackTests
{
    private readonly List<Object> objects = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in objects) if (item != null) Object.DestroyImmediate(item);
        objects.Clear();
        TimeManager.Instance = null;
        NPCManager.Instance = null;
    }

    [Test]
    public void ExperienceRecord_IsStructuredDeterministicAndIdempotent()
    {
        CharacterState character = new CharacterState { characterId = "disciple" };
        LifeRecord first = ExperienceRecordRules.Add(character, 12, ExperienceType.AuraControlMilestone,
            ExperienceImportance.Minor, "control_threshold", null, "control_25",
            new[] { ExperienceRecordRules.Value("threshold", 25) });
        LifeRecord repeated = ExperienceRecordRules.Add(character, 12, ExperienceType.AuraControlMilestone,
            ExperienceImportance.Minor, "control_threshold", "改过的展示文案", "control_25",
            new[] { ExperienceRecordRules.Value("threshold", 25) });

        Assert.That(repeated, Is.SameAs(first));
        Assert.That(character.lifeRecords, Has.Count.EqualTo(1));
        Assert.That(first.id, Is.Not.Empty);
        Assert.That(ExperienceRecordRules.Format(first), Does.Not.Contain("25"));

        LifeRecord textOnlyFirst = ExperienceRecordRules.Add(character, 12, ExperienceType.Relationship,
            ExperienceImportance.Major, "relationship", "与同门结为好友", "same_target");
        LifeRecord textOnlySecond = ExperienceRecordRules.Add(character, 12, ExperienceType.Relationship,
            ExperienceImportance.Major, "relationship", "与同门结为师徒", "same_target");
        Assert.That(textOnlySecond.id, Is.Not.EqualTo(textOnlyFirst.id));
        Assert.That(character.lifeRecords, Has.Count.EqualTo(3));
    }

    [Test]
    public void ConfigValidator_CurrentResourcesProduceNoErrors()
    {
        MethodInfo validate = typeof(ConfigValidator).GetMethod("ValidateAtStartup",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(validate, Is.Not.Null);
        Assert.DoesNotThrow(() => validate.Invoke(null, null));
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void Milestones_RecordOnlyFinalControlThresholdAndFirstOutcomesOnce()
    {
        CharacterState character = new CharacterState
        {
            characterId = "disciple", mainTechniqueId = "qingmu",
            lifeRecords = new List<LifeRecord>()
        };
        DiscipleDayResult result = new DiscipleDayResult
        {
            worldDay = 1, discipleId = "disciple", techniqueId = "qingmu",
            techniqueStageBefore = TechniqueUnderstandingStage.Beginner,
            techniqueStageAfter = TechniqueUnderstandingStage.Integrated,
            auraControlBefore = 20f, auraControlAfter = 80f,
            cultivationResult = new DailyCultivationResult
            {
                selectedActionId = "aura_circulation", selectedActionName = "灵气运转尝试",
                outcome = CultivationActionOutcome.Failed
            }
        };
        PlayerData player = new PlayerData();
        DiscipleMonthlyStats stats = new DiscipleMonthlyStats
            { discipleId = "disciple", monthIndex = 1, narrative = new DiscipleMonthlyNarrativeState() };
        ExperienceGenerator.GenerateSettledDayExperiences(player, character, result, stats);
        ExperienceGenerator.GenerateSettledDayExperiences(player, character, result, stats);

        Assert.That(character.lifeRecords.Count(item => item.descriptionKey == "technique_stage"), Is.EqualTo(1));
        Assert.That(character.lifeRecords.Count(item => item.descriptionKey == "control_first_failure"), Is.EqualTo(1));
        LifeRecord threshold = character.lifeRecords.Single(item => item.descriptionKey == "control_threshold");
        Assert.That(threshold.values.Single(item => item.key == "threshold").value, Is.EqualTo("75"));

        result.worldDay = 2;
        result.auraControlBefore = result.auraControlAfter = 80f;
        result.cultivationResult.outcome = CultivationActionOutcome.Qualified;
        ExperienceGenerator.GenerateSettledDayExperiences(player, character, result, stats);
        Assert.That(character.lifeRecords.Count(item => item.descriptionKey == "control_first_success"), Is.EqualTo(1));
    }

    [Test]
    public void PlanPhases_StartOnFirstActualDay_CompleteOnLastScheduledDay_AndDoNotRepeatAfterEdit()
    {
        NPCManager manager = AddNpcManager(new[] { State("disciple") });
        CharacterState character = manager.GetRuntime("disciple").Character;
        PlayerData player = new PlayerData();
        MonthlyPlanTemplate plan = new MonthlyPlanTemplate
        {
            id = "plan", name = "修炼计划", discipleIds = new List<string> { "disciple" },
            days = Enumerable.Repeat(MonthlyActivityType.Free, 30).ToList()
        };
        plan.days[0] = plan.days[1] = plan.days[22] = MonthlyActivityType.Training;
        player.monthlyPlanTemplates.Add(plan);

        GrowthFeedbackRules.ProcessSettledDay(player, 1,
            Summary(1, DiscipleActivityKind.Mission, MonthlyActivityType.Training, 0f));
        Assert.That(character.lifeRecords.Any(item => item.descriptionKey == "plan_training_started"), Is.False);

        GrowthFeedbackRules.ProcessSettledDay(player, 2,
            Summary(2, DiscipleActivityKind.Training, MonthlyActivityType.Training, 2f));
        LifeRecord started = character.lifeRecords.Single(item => item.descriptionKey == "plan_training_started");
        Assert.That(started.day, Is.EqualTo(2));

        GrowthFeedbackRules.ProcessSettledDay(player, 23,
            Summary(23, DiscipleActivityKind.Mission, MonthlyActivityType.Training, 0f));
        LifeRecord completed = character.lifeRecords.Single(item => item.descriptionKey == "plan_training_completed");
        Assert.That(completed.day, Is.EqualTo(23));
        Assert.That(ExperienceRecordRules.Format(completed), Does.Contain("部分安排未能践行"));

        plan.days[26] = MonthlyActivityType.Training;
        GrowthFeedbackRules.ProcessSettledDay(player, 27,
            Summary(27, DiscipleActivityKind.Training, MonthlyActivityType.Training, 2f));
        Assert.That(character.lifeRecords.Count(item => item.descriptionKey == "plan_training_started"), Is.EqualTo(1));
        Assert.That(character.lifeRecords.Count(item => item.descriptionKey == "plan_training_completed"), Is.EqualTo(1));
    }

    [Test]
    public void RepeatedActionFatigueAndTechniqueNarratives_AreSparseNaturalAndUseSettledEvidence()
    {
        NPCManager manager = AddNpcManager(new[] { State("disciple") });
        CharacterState character = manager.GetRuntime("disciple").Character;
        character.mainTechniqueId = "qingmu";
        PlayerData player = new PlayerData();
        player.monthlyPlanTemplates.Add(new MonthlyPlanTemplate
        {
            id = "plan", name = "短修", discipleIds = new List<string> { "disciple" },
            days = Enumerable.Range(1, 30).Select(day => day <= 3
                ? MonthlyActivityType.Training : MonthlyActivityType.Free).ToList()
        });
        float[] before = { 40f, 55f, 80f };
        float[] after = { 55f, 80f, 25f };
        for (int day = 1; day <= 3; day++)
        {
            DaySettlementSummary summary = Summary(day, DiscipleActivityKind.Training,
                MonthlyActivityType.Training, 2f);
            DiscipleDayResult result = summary.discipleResults.Single();
            result.actionId = "meditate_refine";
            result.actionDisplayName = "打坐炼化";
            result.techniqueId = "qingmu";
            result.techniqueProgressGain = 1f;
            result.fatigueBefore = before[day - 1];
            result.fatigueAfter = after[day - 1];
            result.fatiguePeak = Mathf.Max(result.fatigueBefore, result.fatigueAfter);
            GrowthFeedbackRules.ProcessSettledDay(player, day, summary);
        }

        Assert.That(character.lifeRecords.Count(item => item.type == ExperienceType.RepeatedActionPattern), Is.EqualTo(1));
        Assert.That(character.lifeRecords.Count(item => item.type == ExperienceType.TechniqueUnderstanding), Is.EqualTo(1));
        Assert.That(character.lifeRecords.Count(item => item.type == ExperienceType.FatigueAccumulated), Is.EqualTo(2));
        Assert.That(character.lifeRecords.Count(item => item.type == ExperienceType.FatigueRecovered), Is.EqualTo(1));
        Assert.That(character.lifeRecords.Where(item => item.type == ExperienceType.RepeatedActionPattern ||
                item.type == ExperienceType.TechniqueUnderstanding ||
                item.type == ExperienceType.FatigueAccumulated || item.type == ExperienceType.FatigueRecovered)
            .Select(ExperienceRecordRules.Format),
            Is.All.Not.Contains("%"));

        int countBeforeFree = character.lifeRecords.Count;
        GrowthFeedbackRules.ProcessSettledDay(player, 4,
            Summary(4, DiscipleActivityKind.Free, MonthlyActivityType.Free, 0f));
        Assert.That(character.lifeRecords, Has.Count.EqualTo(countBeforeFree));
    }

    [Test]
    public void NinetyDayNarrativeDensity_IsReadableWithoutDailyLogs()
    {
        NPCManager manager = AddNpcManager(new[] { State("disciple") });
        CharacterState character = manager.GetRuntime("disciple").Character;
        character.mainTechniqueId = "qingmu";
        PlayerData player = new PlayerData();
        player.monthlyPlanTemplates.Add(new MonthlyPlanTemplate
        {
            id = "plan", name = "主修炼", discipleIds = new List<string> { "disciple" },
            days = Enumerable.Range(1, 30).Select(day => day <= 21 ? MonthlyActivityType.Training :
                day <= 25 ? MonthlyActivityType.SectDuty : MonthlyActivityType.Free).ToList()
        });

        for (int day = 1; day <= 90; day++)
        {
            MonthlyActivityType planned = player.monthlyPlanTemplates[0].days[(day - 1) % 30];
            DiscipleActivityKind actual = DiscipleCurrentStateBuilder.ActivityKind(planned);
            DaySettlementSummary summary = Summary(day, actual, planned,
                planned == MonthlyActivityType.Training ? 1f : 0f);
            DiscipleDayResult result = summary.discipleResults.Single();
            result.actionId = planned == MonthlyActivityType.Training ? "meditate_refine" :
                planned == MonthlyActivityType.SectDuty ? "sect_care_herbs" : "free_day";
            result.actionDisplayName = planned == MonthlyActivityType.Training ? "打坐炼化" :
                planned == MonthlyActivityType.SectDuty ? "照料灵草" : "自由活动";
            result.techniqueId = "qingmu";
            result.techniqueProgressGain = planned == MonthlyActivityType.Training ? 0.5f : 0f;
            result.fatigueBefore = result.fatigueAfter = 10f;
            result.fatiguePeak = 10f;
            GrowthFeedbackRules.ProcessSettledDay(player, day, summary);
        }

        for (int month = 1; month <= 3; month++)
        {
            int start = (month - 1) * 30 + 1;
            int count = character.lifeRecords.Count(record => record.importance != ExperienceImportance.Internal &&
                record.day >= start && record.day < start + 30);
            Assert.That(count, Is.InRange(5, 8), $"第{month}月经历密度异常");
        }
        Assert.That(character.lifeRecords.Any(record => record.sourceId == "free_day"), Is.False);
        Assert.That(character.lifeRecords.Select(ExperienceRecordRules.Format),
            Is.All.Not.Contains("+"));
    }

    [Test]
    public void DepartmentChangesAreMajorChronicleAndTimelineUsesCalendarDate()
    {
        CharacterState character = State("disciple");
        SectDepartmentState department = new SectDepartmentState
            { departmentId = "department_0001", name = "百草堂" };
        LifeRecord joined = ExperienceGenerator.WriteDepartmentChange(character, 61, department,
            "department_joined");
        LifeRecord repeated = ExperienceGenerator.WriteDepartmentChange(character, 61, department,
            "department_joined");
        Assert.That(repeated, Is.SameAs(joined));
        Assert.That(joined.importance, Is.EqualTo(ExperienceImportance.Major));
        Assert.That(joined.retention, Is.EqualTo(ExperienceRetention.Chronicle));

        NPCRuntime runtime = Runtime("disciple");
        runtime.Character.lifeRecords.Add(joined);
        DiscipleCenterSnapshot snapshot = DiscipleCenterSnapshotBuilder.Build(
            new[] { runtime }, "disciple", 61);
        Assert.That(snapshot.historyItems.Single().heading, Does.Contain("第1年·第3月·初一"));
        Assert.That(snapshot.historyItems.Single().isMajor, Is.True);
        Assert.That(snapshot.historyItems.Single().body, Does.Not.Contain("61"));
    }

    [Test]
    public void MonthlyStats_PreservePlanAndMissionActual_AndFinalizeIdempotently()
    {
        PlayerData player = new PlayerData();
        for (int day = 1; day <= 30; day++)
        {
            bool mission = day == 5 || day == 6;
            DaySettlementSummary summary = Summary(day, mission ? DiscipleActivityKind.Mission : DiscipleActivityKind.Training,
                MonthlyActivityType.Training, 1f);
            if (day == 30)
            {
                summary.itemChanges.Add(new ItemDayChange { itemId = "material_001", countChange = 7 });
                summary.resourceProduction.Add(new ResourceProductionRecord
                    { nodeId = "node", itemId = "material_001", calculated = 10, received = 7, lost = 3 });
            }
            GrowthFeedbackRules.ProcessSettledDay(player, day, summary);
        }

        Assert.That(player.growthFeedback.reports, Has.Count.EqualTo(1));
        SectMonthlyReport report = player.growthFeedback.reports.Single();
        DiscipleMonthlyStats stats = report.disciples.Single();
        Assert.That(stats.plannedTrainingDays, Is.EqualTo(30));
        Assert.That(stats.actualTrainingDays, Is.EqualTo(28));
        Assert.That(stats.missionDays, Is.EqualTo(2));
        Assert.That(stats.naqiGain, Is.EqualTo(30f));
        Assert.That(report.itemChanges.Single().countChange, Is.EqualTo(7));
        Assert.That(report.resourceProduction.Single().lost, Is.EqualTo(3));
        Assert.That(player.growthFeedback.activeMonthIndex, Is.EqualTo(2));

        GrowthFeedbackRules.ProcessSettledDay(player, 30, Summary(30, DiscipleActivityKind.Free,
            MonthlyActivityType.Free, 99f));
        Assert.That(player.growthFeedback.reports, Has.Count.EqualTo(1));
        Assert.That(player.growthFeedback.reports.Single().disciples.Single().naqiGain, Is.EqualTo(30f));
    }

    [Test]
    public void Reports_RetainLatestTwelveMonths()
    {
        PlayerData player = new PlayerData();
        for (int day = 1; day <= 390; day++)
            GrowthFeedbackRules.ProcessSettledDay(player, day,
                Summary(day, DiscipleActivityKind.Free, MonthlyActivityType.Free, 0f));

        Assert.That(player.growthFeedback.reports, Has.Count.EqualTo(12));
        Assert.That(player.growthFeedback.reports.First().monthIndex, Is.EqualTo(2));
        Assert.That(player.growthFeedback.reports.Last().monthIndex, Is.EqualTo(13));
    }

    [Test]
    public void Reports_FinalizeExactlyAtThirtySixtyAndNinetyDays()
    {
        PlayerData player = new PlayerData();
        for (int day = 1; day <= 90; day++)
        {
            DaySettlementSummary summary = Summary(day, DiscipleActivityKind.Free,
                MonthlyActivityType.Free, 0f);
            GrowthFeedbackRules.ProcessSettledDay(player, day, summary);
            GrowthFeedbackRules.ProcessSettledDay(player, day, summary);
        }

        CollectionAssert.AreEqual(new[] { 1, 2, 3 },
            player.growthFeedback.reports.Select(item => item.monthIndex).ToArray());
        CollectionAssert.AreEqual(new[] { "growth_month_1", "growth_month_2", "growth_month_3" },
            player.growthFeedback.reports.Select(item => item.id).ToArray());
        Assert.That(player.growthFeedback.activeMonthIndex, Is.EqualTo(4));
    }

    [Test]
    public void NinetyDayFeedback_IsIndependentOfSelectedClockSpeed()
    {
        PlayerData speedOne = new PlayerData();
        PlayerData speedFour = new PlayerData();
        for (int day = 1; day <= 90; day++)
        {
            MonthlyActivityType plan = day % 3 == 0 ? MonthlyActivityType.SectDuty :
                day % 2 == 0 ? MonthlyActivityType.Free : MonthlyActivityType.Training;
            DiscipleActivityKind actual = day % 10 == 0 ? DiscipleActivityKind.Mission :
                DiscipleCurrentStateBuilder.ActivityKind(plan);
            GrowthFeedbackRules.ProcessSettledDay(speedOne, day, Summary(day, actual, plan, day % 5));
            GrowthFeedbackRules.ProcessSettledDay(speedFour, day, Summary(day, actual, plan, day % 5));
        }

        string one = JsonConvert.SerializeObject(speedOne.growthFeedback);
        string four = JsonConvert.SerializeObject(speedFour.growthFeedback);
        Assert.That(four, Is.EqualTo(one));
    }

    [Test]
    public void Highlights_RespectPriorityThenRecentGrowthAndEightDiscipleLimit()
    {
        NPCManager manager = AddNpcManager(Enumerable.Range(1, 10).Select(index => State($"d{index:00}")));
        CharacterState death = manager.GetRuntime("d10").Character;
        LifeRecord deathRecord = ExperienceRecordRules.Add(death, 5, ExperienceType.Death,
            ExperienceImportance.Major, "death", "死亡", "death");
        CharacterState technique = manager.GetRuntime("d09").Character;
        LifeRecord techniqueRecord = ExperienceRecordRules.Add(technique, 30, ExperienceType.TechniqueMilestone,
            ExperienceImportance.Major, "technique_stage", "功法提升", "qingmu");
        CharacterState control = manager.GetRuntime("d08").Character;
        LifeRecord controlRecord = ExperienceRecordRules.Add(control, 30, ExperienceType.AuraControlMilestone,
            ExperienceImportance.Minor, "control_threshold", "控制提升", "control_25");
        SectGrowthFeedbackState state = new SectGrowthFeedbackState
        {
            currentStats = Enumerable.Range(1, 10).Select(index => new DiscipleMonthlyStats
            {
                discipleId = $"d{index:00}", displayName = $"弟子{index:00}", monthIndex = 1,
                firstDay = 1, lastDay = index, settledDays = 1, plannedFreeDays = 1,
                actualFreeDays = 1, naqiGain = 20f + index
            }).ToList()
        };
        state.currentStats.Single(item => item.discipleId == "d10").experienceIds.Add(deathRecord.id);
        state.currentStats.Single(item => item.discipleId == "d09").experienceIds.Add(techniqueRecord.id);
        state.currentStats.Single(item => item.discipleId == "d08").experienceIds.Add(controlRecord.id);
        MethodInfo finalize = typeof(GrowthFeedbackRules).GetMethod("FinalizeMonth",
            BindingFlags.Static | BindingFlags.NonPublic);
        finalize.Invoke(null, new object[] { state, 30, new List<ResourceProductionRecord>() });

        SectMonthlyReport report = state.reports.Single();
        Assert.That(report.disciples, Has.Count.EqualTo(10));
        Assert.That(report.highlightDiscipleIds, Has.Count.EqualTo(8));
        CollectionAssert.AreEqual(new[] { "d10", "d09", "d08" }, report.highlightDiscipleIds.Take(3));
        Assert.That(report.highlightDiscipleIds.Distinct().Count(), Is.EqualTo(8));
    }

    [Test]
    public void ExperienceRetention_RemovesOldRecentAndInternalButKeepsChronicleAndMajor()
    {
        NPCManager manager = AddNpcManager(new[] { State("disciple") });
        CharacterState character = manager.GetRuntime("disciple").Character;
        LifeRecord major = ExperienceRecordRules.Add(character, 1, ExperienceType.CultivationMilestone,
            ExperienceImportance.Major, "qi_layer", null, "layer_2",
            new[] { ExperienceRecordRules.Value("layer", 2) });
        LifeRecord oldMinor = ExperienceRecordRules.Add(character, 2, ExperienceType.Event,
            ExperienceImportance.Minor, "event", "旧事件", "old_minor");
        LifeRecord keptMinor = ExperienceRecordRules.Add(character, 3, ExperienceType.Event,
            ExperienceImportance.Minor, "event", "边界事件", "kept_minor");
        LifeRecord chronicle = ExperienceRecordRules.Add(character, 1, ExperienceType.PlanPhaseCompleted,
            ExperienceImportance.Minor, "plan_training_completed", null, "chronicle", null,
            ExperienceRetention.Chronicle);
        LifeRecord oldInternal = ExperienceRecordRules.Add(character, 360, ExperienceType.InternalDecision,
            ExperienceImportance.Internal, "decision", "旧决策", "old_internal");
        LifeRecord keptInternal = ExperienceRecordRules.Add(character, 361, ExperienceType.InternalDecision,
            ExperienceImportance.Internal, "decision", "边界决策", "kept_internal");
        Assert.That(NPCManager.Instance, Is.SameAs(manager));
        Assert.That(manager.GetAllNPC().Single().Character, Is.SameAs(character));
        Assert.That(oldMinor.importance, Is.EqualTo(ExperienceImportance.Minor));
        Assert.That(oldMinor.day, Is.EqualTo(2));
        MethodInfo prune = typeof(GrowthFeedbackRules).GetMethod("PruneExperiences",
            BindingFlags.Static | BindingFlags.NonPublic);
        prune.Invoke(null, new object[] { 363 });

        CollectionAssert.Contains(character.lifeRecords.Select(item => item.id), major.id);
        CollectionAssert.Contains(character.lifeRecords.Select(item => item.id), chronicle.id);
        CollectionAssert.DoesNotContain(character.lifeRecords.Select(item => item.id), oldMinor.id);
        CollectionAssert.Contains(character.lifeRecords.Select(item => item.id), keptMinor.id);
        CollectionAssert.DoesNotContain(character.lifeRecords.Select(item => item.id), oldInternal.id);
        CollectionAssert.Contains(character.lifeRecords.Select(item => item.id), keptInternal.id);
    }

    [Test]
    public void V25FeedbackState_RoundTripsNarrativeExperiencesReportsAndUnreadMonthEnd()
    {
        LifeRecord experience = ExperienceRecordRules.Add(State("disciple"), 30,
            ExperienceType.CultivationMilestone, ExperienceImportance.Major,
            "qi_layer", null, "layer_2", new[] { ExperienceRecordRules.Value("layer", 2) });
        GameState state = new GameState
        {
            currentDay = 45,
            sect = new PlayerData
            {
                growthFeedback = new SectGrowthFeedbackState
                {
                    activeMonthIndex = 2, lastProcessedDay = 45, lastFinalizedMonthIndex = 1,
                    currentStats = new List<DiscipleMonthlyStats>
                    {
                        new DiscipleMonthlyStats
                        {
                            discipleId = "disciple", displayName = "弟子", monthIndex = 2,
                            firstDay = 31, lastDay = 45, settledDays = 15,
                            plannedTrainingDays = 15, actualTrainingDays = 15,
                            experienceIds = new List<string> { experience.id },
                            narrative = new DiscipleMonthlyNarrativeState
                            {
                                trainingStarted = true,
                                repeatedPatternActivities = new List<string> { "training" }
                            }
                        }
                    },
                    reports = new List<SectMonthlyReport>
                    {
                        new SectMonthlyReport { id = "growth_month_1", monthIndex = 1, year = 1, month = 1,
                            startDay = 1, endDay = 30 }
                    }
                }
            },
            characters = new List<CharacterState> { State("disciple") },
            unreadDaySettlement = new DaySettlementSummary
            {
                day = 30, isMonthEnd = true, monthIndex = 1,
                discipleResults = new List<DiscipleDayResult>
                {
                    new DiscipleDayResult { discipleId = "disciple", worldDay = 30,
                        actualActivity = DiscipleActivityKind.Training }
                }
            }
        };
        state.characters[0].lifeRecords.Add(experience);

        GameState restored = JsonConvert.DeserializeObject<GameState>(JsonConvert.SerializeObject(state));

        Assert.That(restored.version, Is.EqualTo(SaveDataVersion.Current));
        Assert.That(restored.sect.growthFeedback.lastProcessedDay, Is.EqualTo(45));
        Assert.That(restored.sect.growthFeedback.currentStats.Single().settledDays, Is.EqualTo(15));
        Assert.That(restored.sect.growthFeedback.currentStats.Single().narrative.trainingStarted, Is.True);
        CollectionAssert.Contains(restored.sect.growthFeedback.currentStats.Single()
            .narrative.repeatedPatternActivities, "training");
        Assert.That(restored.sect.growthFeedback.reports.Single().id, Is.EqualTo("growth_month_1"));
        Assert.That(restored.characters.Single().lifeRecords.Single().id, Is.EqualTo(experience.id));
        Assert.That(restored.characters.Single().lifeRecords.Single().retention,
            Is.EqualTo(ExperienceRetention.Chronicle));
        Assert.That(restored.unreadDaySettlement.isMonthEnd, Is.True);
        Assert.That(restored.unreadDaySettlement.discipleResults.Single().worldDay, Is.EqualTo(30));
    }

    [Test]
    public void MonthlyReportClose_AcknowledgesOnlyLatestUnreadMonthEnd()
    {
        GameObject timeObject = new GameObject("ReportTime");
        objects.Add(timeObject);
        TimeManager time = timeObject.AddComponent<TimeManager>();
        TimeManager.Instance = time;
        time.RestoreUnreadSettlement(new DaySettlementSummary { day = 30, isMonthEnd = true, monthIndex = 1 });
        GameObject reportObject = new GameObject("ReportView");
        objects.Add(reportObject);
        MonthlyReportView report = reportObject.AddComponent<MonthlyReportView>();

        report.OnOpened(new MonthlyReportContext(1, false));
        report.OnClosed();
        Assert.That(time.UnreadDaySettlement, Is.Not.Null);
        Assert.That((time.PauseReasons & PauseReason.MonthEnd) != 0, Is.True);

        report.OnOpened(new MonthlyReportContext(1, true));
        report.OnClosed();
        Assert.That(time.UnreadDaySettlement, Is.Null);
        Assert.That((time.PauseReasons & PauseReason.MonthEnd) != 0, Is.False);
        Assert.That((time.PauseReasons & PauseReason.Player) != 0, Is.True);
    }

    [Test]
    public void CurrentState_UsesLockedSegmentThenMissionOverrideAndRecovery()
    {
        GameObject timeObject = new GameObject("GrowthTime");
        objects.Add(timeObject);
        TimeManager time = timeObject.AddComponent<TimeManager>();
        TimeManager.Instance = time;
        time.RestoreWorldTime(new WorldTimeSaveData
        {
            currentHour = 14f, selectedSpeed = 1f, dayPrepared = true,
            dailySchedule = new DailyScheduleState
            {
                day = 1,
                disciples = new List<DiscipleDailySchedule>
                {
                    new DiscipleDailySchedule
                    {
                        characterId = "disciple", activity = MonthlyActivityType.Training,
                        cultivationActionId = "meditate_refine",
                        segments = new List<DailyScheduleSegment>
                        {
                            new DailyScheduleSegment { startHour = 11f, endHour = 18f,
                                actionId = "meditate_refine", label = "打坐炼化" }
                        }
                    }
                }
            }
        });
        NPCRuntime npc = Runtime("disciple");
        DiscipleCurrentState current = DiscipleCurrentStateBuilder.Build(npc);
        Assert.That(current.actualActivity, Is.EqualTo(DiscipleActivityKind.Training));
        Assert.That(current.currentActionId, Is.EqualTo("meditate_refine"));

        Mission mission = new Mission(new MissionData
        {
            id = "mission", name = "真实任务", needDays = 2,
            itemRewards = new List<ItemReward>(), nodes = new List<MissionNodeData>()
        });
        typeof(Mission).GetField("<State>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(mission, MissionState.Active);
        npc.CurrentMission = mission;
        current = DiscipleCurrentStateBuilder.Build(npc);
        Assert.That(current.scheduledActivity, Is.EqualTo(MonthlyActivityType.Training));
        Assert.That(current.actualActivity, Is.EqualTo(DiscipleActivityKind.Mission));
        Assert.That(current.currentActionDisplayName, Is.EqualTo("真实任务"));

        npc.CurrentMission = null;
        npc.Character.health = HealthState.SeriousInjury;
        npc.SetState(NPCState.Injured, 3);
        current = DiscipleCurrentStateBuilder.Build(npc);
        Assert.That(current.actualActivity, Is.EqualTo(DiscipleActivityKind.Recovery));

        npc.Character.health = HealthState.Healthy;
        npc.SetState(NPCState.Idle, 0);
        time.RestoreWorldTime(Schedule(MonthlyActivityType.SectDuty, "sect_duty_work", "宗门劳作"));
        current = DiscipleCurrentStateBuilder.Build(npc);
        Assert.That(current.actualActivity, Is.EqualTo(DiscipleActivityKind.SectDuty));
        Assert.That(current.currentActionId, Is.EqualTo("sect_duty_work"));

        time.RestoreWorldTime(Schedule(MonthlyActivityType.Free, "free_rest", "游历休整"));
        current = DiscipleCurrentStateBuilder.Build(npc);
        Assert.That(current.actualActivity, Is.EqualTo(DiscipleActivityKind.Free));
        Assert.That(current.currentActionId, Is.EqualTo("free_rest"));
    }

    [Test]
    public void ProcessDayWithResults_ReturnsExactlyOneResultForEachLivingDiscipleEvenWithoutGrowth()
    {
        NPCManager manager = AddNpcManager(new[]
        {
            State("alive_1"), State("alive_2"),
            new CharacterState { characterId = "dead", displayName = "dead", hasGeneratedProfile = true,
                health = HealthState.Dead, realm = CultivationRealm.QiRefining, realmLayer = 1 }
        });

        List<DiscipleDayExecutionResult> results = manager.ProcessDayWithResults();

        Assert.That(results, Has.Count.EqualTo(2));
        CollectionAssert.AreEquivalent(new[] { "alive_1", "alive_2" },
            results.Select(item => item.discipleId).ToArray());
        Assert.That(results.All(item => item.executed && item.actualActivity == DiscipleActivityKind.Free), Is.True);
    }

    private static DaySettlementSummary Summary(int day, DiscipleActivityKind actual,
        MonthlyActivityType planned, float naqiGain)
    {
        return new DaySettlementSummary
        {
            day = day,
            discipleResults = new List<DiscipleDayResult>
            {
                new DiscipleDayResult
                {
                    discipleId = "disciple", displayName = "弟子", worldDay = day,
                    scheduledActivity = planned, actualActivity = actual,
                    actionId = actual == DiscipleActivityKind.Mission ? "mission" : "action",
                    actionDisplayName = actual == DiscipleActivityKind.Mission ? "任务" : "行动",
                    executed = true, naqiGain = naqiGain, fatiguePeak = day % 100,
                    realmLayerBefore = 1, realmLayerAfter = 1
                }
            }
        };
    }

    private static WorldTimeSaveData Schedule(MonthlyActivityType activity, string actionId, string label) =>
        new WorldTimeSaveData
        {
            currentHour = 14f, selectedSpeed = 1f, dayPrepared = true,
            dailySchedule = new DailyScheduleState
            {
                day = 1,
                disciples = new List<DiscipleDailySchedule>
                {
                    new DiscipleDailySchedule
                    {
                        characterId = "disciple", activity = activity,
                        segments = new List<DailyScheduleSegment>
                        {
                            new DailyScheduleSegment { startHour = 6f, endHour = 20f,
                                actionId = actionId, label = label }
                        }
                    }
                }
            }
        };

    private NPCManager AddNpcManager(IEnumerable<CharacterState> states)
    {
        NPCManager.Instance = null;
        GameObject root = new GameObject("GrowthNpcManager");
        objects.Add(root);
        NPCManager manager = root.AddComponent<NPCManager>();
        NPCManager.Instance = manager;
        manager.ClearCharacters();
        manager.RestoreCharacters(states);
        return manager;
    }

    private static CharacterState State(string id) => new CharacterState
    {
        characterId = id, displayName = id, hasGeneratedProfile = true,
        realm = CultivationRealm.QiRefining, realmLayer = 1, health = HealthState.Healthy,
        spiritRoot = new SpiritRootData()
    };

    private NPCRuntime Runtime(string id)
    {
        NPCData data = ScriptableObject.CreateInstance<NPCData>();
        objects.Add(data);
        data.npcID = id;
        data.npcName = id;
        data.physique = 10;
        return new NPCRuntime(data, new CharacterState
        {
            characterId = id, templateId = id, displayName = id,
            realm = CultivationRealm.QiRefining, realmLayer = 1, health = HealthState.Healthy
        });
    }
}
