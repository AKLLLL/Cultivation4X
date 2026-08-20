using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class DiscipleAITests
{
    private readonly List<Object> objects = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        PlayerManager.Instance = null;
        NPCManager.Instance = null;
        MissionManager.Instance = null;
        WarehouseManager.Instance = null;
        RewardManager.Instance = null;
        TimeManager.Instance = null;
        if (DiscipleDecisionManager.Instance != null)
            Object.DestroyImmediate(DiscipleDecisionManager.Instance.gameObject);
        DiscipleAIConfigLoader.ResetForTests();
        DiscipleAIDebug.EnableLog = false;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in objects) if (item != null) Object.DestroyImmediate(item);
        objects.Clear();
        PlayerManager.Instance = null;
        NPCManager.Instance = null;
        MissionManager.Instance = null;
        WarehouseManager.Instance = null;
        RewardManager.Instance = null;
        TimeManager.Instance = null;
        if (DiscipleDecisionManager.Instance != null)
            Object.DestroyImmediate(DiscipleDecisionManager.Instance.gameObject);
        DiscipleAIConfigLoader.ResetForTests();
    }

    // ---------------------------------------------------------------
    // 条件与曲线
    // ---------------------------------------------------------------

    [Test]
    public void HealthIs_InjuredWildcard_MatchesLightHeavySeriousOnly()
    {
        NPCRuntime npc = CreateRuntime("t_health", "测试弟子");
        GoalCondition condition = new GoalCondition { type = GoalConditionType.HealthIs, value = "Injured" };

        npc.Character.health = HealthState.LightInjury;
        Assert.IsTrue(DiscipleAIEvaluator.PassesGoalCondition(npc, condition));
        npc.Character.health = HealthState.HeavyInjury;
        Assert.IsTrue(DiscipleAIEvaluator.PassesGoalCondition(npc, condition));
        npc.Character.health = HealthState.SeriousInjury;
        Assert.IsTrue(DiscipleAIEvaluator.PassesGoalCondition(npc, condition));
        npc.Character.health = HealthState.Healthy;
        Assert.IsFalse(DiscipleAIEvaluator.PassesGoalCondition(npc, condition));
        npc.Character.health = HealthState.PermanentTrauma;
        Assert.IsFalse(DiscipleAIEvaluator.PassesGoalCondition(npc, condition));
    }

    [Test]
    public void ResponseCurve_InterpolatesAndClampsToEnds()
    {
        List<float[]> curve = new List<float[]>
        {
            new float[] { 0f, 0f },
            new float[] { 0.5f, 2f },
            new float[] { 1f, 1f }
        };
        Assert.AreEqual(0f, DiscipleAIEvaluator.EvaluateCurve(-1f, curve), 0.001f);
        Assert.AreEqual(1f, DiscipleAIEvaluator.EvaluateCurve(0.25f, curve), 0.001f);
        Assert.AreEqual(2f, DiscipleAIEvaluator.EvaluateCurve(0.5f, curve), 0.001f);
        Assert.AreEqual(1.5f, DiscipleAIEvaluator.EvaluateCurve(0.75f, curve), 0.001f);
        Assert.AreEqual(1f, DiscipleAIEvaluator.EvaluateCurve(2f, curve), 0.001f);

        Assert.AreEqual(0.7f, DiscipleAIEvaluator.EvaluateCurve(0.7f, new List<float[]>()), 0.001f);
        Assert.AreEqual(0f, DiscipleAIEvaluator.EvaluateCurve(-0.5f, new List<float[]>()), 0.001f);
    }

    [Test]
    public void GoalConditions_RealmTraitRelationshipAndWarehouse()
    {
        NPCRuntime npc = CreateRuntime("t_cond", "测试弟子",
            new CharacterState { characterId = "t_cond", displayName = "测试弟子", realm = CultivationRealm.QiRefining });
        npc.Character.traitIds.Add("diligent");

        Assert.IsTrue(DiscipleAIEvaluator.PassesGoalCondition(npc,
            new GoalCondition { type = GoalConditionType.RealmAtLeast, value = "QiRefining" }));
        Assert.IsFalse(DiscipleAIEvaluator.PassesGoalCondition(npc,
            new GoalCondition { type = GoalConditionType.RealmAtLeast, value = "Foundation" }));
        Assert.IsTrue(DiscipleAIEvaluator.PassesGoalCondition(npc,
            new GoalCondition { type = GoalConditionType.RealmAtMost, value = "Foundation" }));
        Assert.IsTrue(DiscipleAIEvaluator.PassesGoalCondition(npc,
            new GoalCondition { type = GoalConditionType.HasTrait, value = "diligent" }));
        Assert.IsFalse(DiscipleAIEvaluator.PassesGoalCondition(npc,
            new GoalCondition { type = GoalConditionType.HasTrait, value = "lazy" }));
        Assert.IsTrue(DiscipleAIEvaluator.PassesGoalCondition(npc,
            new GoalCondition { type = GoalConditionType.RelationshipCountBelow, intValue = 2 }));

        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        WarehouseManager.Instance = warehouse;
        warehouse.warehouseData.items.Clear();
        warehouse.warehouseData.items.Add(new ItemStack { itemId = "material_001", count = 3 });
        Assert.IsTrue(DiscipleAIEvaluator.PassesGoalCondition(npc,
            new GoalCondition { type = GoalConditionType.WarehouseItemBelow, value = "material_001", intValue = 5 }));
        Assert.IsFalse(DiscipleAIEvaluator.PassesGoalCondition(npc,
            new GoalCondition { type = GoalConditionType.WarehouseItemBelow, value = "material_001", intValue = 3 }));
    }

    // ---------------------------------------------------------------
    // Goal 生成
    // ---------------------------------------------------------------

    [Test]
    public void GoalGeneration_AppliesThresholdHysteresisAndTop3()
    {
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse"); // 默认 material_001 x5，资源不稀缺
        WarehouseManager.Instance = warehouse;
        warehouse.warehouseData = new WarehouseData();
        Assert.AreEqual(5, WarehouseManager.Instance.GetItemCount("material_001"), "测试前置：仓库应有 5 个基础材料");
        DiscipleAIDebug.EnableLog = true;
        NPCRuntime npc = CreateRuntime("t_goal", "测试弟子");

        List<GoalDefinition> definitions = new List<GoalDefinition>
        {
            new GoalDefinition
            {
                id = "improve_cultivation", displayName = "提升修为", baseIntensity = 4,
                conditions = new List<GoalCondition> { new GoalCondition { type = GoalConditionType.Always } }
            },
            new GoalDefinition
            {
                id = "obtain_resources", displayName = "获取资源", baseIntensity = 2,
                weightTerms = new List<ScoreTerm>
                {
                    new ScoreTerm
                    {
                        source = ScoreSource.Environment, key = "WarehouseScarcity.material_001",
                        weight = 6, threshold = 5,
                        curve = new List<float[]> { new float[] { 0f, 0f }, new float[] { 1f, 1f } }
                    }
                }
            },
            new GoalDefinition
            {
                id = "improve_relationship", displayName = "改善关系", baseIntensity = 4,
                conditions = new List<GoalCondition> { new GoalCondition { type = GoalConditionType.RelationshipCountBelow, intValue = 2 } }
            },
            new GoalDefinition
            {
                id = "recover_state", displayName = "恢复状态", baseIntensity = 8,
                conditions = new List<GoalCondition> { new GoalCondition { type = GoalConditionType.HealthIs, value = "Injured" } }
            }
        };

        List<GoalInstance> goals = DiscipleAIEvaluator.GenerateGoals(npc, definitions, null);
        string goalDump = string.Join(", ", goals.Select(goal => $"{goal.Definition.id}:{goal.Intensity}"));
        Assert.AreEqual(2, goals.Count, goalDump);
        Assert.AreEqual("improve_cultivation", goals[0].Definition.id); // 同分字典序最小
        Assert.AreEqual("improve_relationship", goals[1].Definition.id);
        Assert.IsTrue(goals.All(goal => goal.Intensity >= DiscipleAIEvaluator.GoalActivationThreshold));

        // 滞后：旧值 9 时，新值 4 应取 max(4, 4.5) = 4.5
        List<GoalInstance> previous = new List<GoalInstance>
        {
            new GoalInstance
            {
                Definition = definitions[0], Intensity = 9f
            }
        };
        List<GoalInstance> lagged = DiscipleAIEvaluator.GenerateGoals(npc, definitions, previous);
        GoalInstance cultivation = lagged.First(goal => goal.Definition.id == "improve_cultivation");
        Assert.AreEqual(4.5f, cultivation.Intensity, 0.001f);
    }

    // ---------------------------------------------------------------
    // Action 过滤与选平
    // ---------------------------------------------------------------

    [Test]
    public void ActionFiltering_ZeroCostNoNodesAndPlayerAssignability()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        Add<WarehouseManager>("Warehouse");
        MissionManager missions = Add<MissionManager>("Missions");
        MissionManager.Instance = missions;
        missions.LoadMissionsFromJson();

        NPCRuntime npc = CreateRuntime("t_filter", "测试弟子");
        IdentityDefinition identity = DiscipleAIConfigLoader.Load().GetIdentity("inner_disciple");
        List<ActionDefinition> actions = DiscipleAIConfigLoader.Load().Actions;
        List<GoalInstance> goals = new List<GoalInstance>();

        List<ActionScoreResult> results = DiscipleAIEvaluator.EvaluateActions(npc, identity, goals, actions);

        ActionScoreResult cultivate = results.First(item => item.Action.id == "cultivate");
        Assert.IsTrue(cultivate.Eligible, cultivate.FilterReason);
        ActionScoreResult rest = results.First(item => item.Action.id == "rest");
        Assert.IsTrue(rest.Eligible, rest.FilterReason);
        ActionScoreResult social = results.First(item => item.Action.id == "social");
        Assert.IsTrue(social.Eligible, social.FilterReason);
        ActionScoreResult explore = results.First(item => item.Action.id == "explore");
        Assert.IsTrue(explore.Eligible, explore.FilterReason);
        ActionScoreResult alchemy = results.First(item => item.Action.id == "alchemy");
        Assert.AreEqual("需要消耗资源", alchemy.FilterReason);

        // AI 模板 isPlayerAssignable=false 不阻断 AI，但必须对玩家隐藏。
        MissionData cultivateMission = MissionManager.Instance.GetMissionData("disciple_ai_cultivate_001");
        Assert.IsNotNull(cultivateMission);
        Assert.IsFalse(cultivateMission.isPlayerAssignable);
        Assert.IsFalse(MissionManager.Instance.IsMissionVisible(cultivateMission));
        Assert.IsTrue(MissionManager.Instance.IsMissionVisible(MissionManager.Instance.GetMissionData("cultivation_001")));
    }

    [Test]
    public void ActionFiltering_FacilityOccupiedByActiveMission()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        Add<WarehouseManager>("Warehouse");
        NPCManager npcManager = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcManager;
        MissionManager missions = Add<MissionManager>("Missions");
        MissionManager.Instance = missions;
        missions.LoadMissionsFromJson();
        List<FounderCandidateData> founders = FoundingRules.GenerateCandidates(77).Take(3).ToList();
        Assert.IsTrue(NPCManager.Instance.CreateFounders(founders));

        NPCRuntime npc = NPCManager.Instance.GetLivingNPC()[0];
        IdentityDefinition identity = DiscipleAIConfigLoader.Load().GetIdentity("inner_disciple");

        MissionData conflictData = new MissionData
        {
            id = "facility_conflict_test",
            name = "设施占用测试",
            missionType = MissionType.Exploration,
            isFacilityAction = true,
            requiredFacility = FacilityType.SecretRealm,
            requiredFacilityLevel = 1,
            needDays = 3,
            nodes = new List<MissionNodeData>(),
            itemRewards = new List<ItemReward>()
        };
        Mission conflict = new Mission(conflictData);
        conflict.StartMission(npc);
        MissionManager.Instance.AddActiveMission(conflict);

        // 占用设施的是 npc[0]，换一名仍然空闲的弟子来评估候选。
        NPCRuntime observer = NPCManager.Instance.GetLivingNPC()[1];
        List<ActionScoreResult> results = DiscipleAIEvaluator.EvaluateActions(
            observer, identity, new List<GoalInstance>(), DiscipleAIConfigLoader.Load().Actions);
        ActionScoreResult explore = results.First(item => item.Action.id == "explore");
        Assert.AreEqual("设施正在使用", explore.FilterReason);
    }

    [Test]
    public void ChooseAction_PicksHighestScoreThenSmallestActionId()
    {
        ActionDefinition first = new ActionDefinition { id = "a", displayName = "甲" };
        ActionDefinition second = new ActionDefinition { id = "b", displayName = "乙" };
        ActionDefinition blocked = new ActionDefinition { id = "c", displayName = "丙" };

        List<ActionScoreResult> results = new List<ActionScoreResult>
        {
            new ActionScoreResult { Action = second, Score = 3f },
            new ActionScoreResult { Action = first, Score = 3f },
            new ActionScoreResult { Action = blocked, Score = 100f, FilterReason = "需要消耗资源" }
        };

        DiscipleDecisionResult decision = DiscipleAIEvaluator.ChooseAction(results);
        Assert.AreSame(first, decision.Selected);
    }

    // ---------------------------------------------------------------
    // 突破（成长系统自闭环）
    // ---------------------------------------------------------------

    [Test]
    public void TryBreakthrough_FailureWritesFailureRecordInsideGrowthSystem()
    {
        NPCRuntime npc = CreateRuntime("t_break_fail", "测试弟子",
            new CharacterState { characterId = "t_break_fail", displayName = "测试弟子", cultivation = 100, realm = CultivationRealm.QiRefining });

        Assert.IsFalse(npc.TryBreakthrough(-1f)); // chance 被 clamp 到 0，必失败
        Assert.AreEqual(0, npc.Cultivation);
        Assert.IsTrue(npc.Character.lifeRecords.Any(record =>
            record.category == "Breakthrough" && record.text.Contains("突破失败")));
    }

    [Test]
    public void TryBreakthrough_SuccessDoesNotWriteFailureRecord()
    {
        NPCRuntime npc = CreateRuntime("t_break_ok", "测试弟子",
            new CharacterState { characterId = "t_break_ok", displayName = "测试弟子", cultivation = 100, realm = CultivationRealm.QiRefining });

        Assert.IsTrue(npc.TryBreakthrough(1f)); // chance 被 clamp 到 1，必成功
        Assert.AreEqual(CultivationRealm.Foundation, npc.Realm);
        Assert.IsFalse(npc.Character.lifeRecords.Any(record => record.text.Contains("突破失败")));
        Assert.IsTrue(npc.Character.lifeRecords.Any(record => record.text.Contains("突破至")));
    }

    // ---------------------------------------------------------------
    // 关系入口（既有 NPCManager 扩展）
    // ---------------------------------------------------------------

    [Test]
    public void AddRelationship_WritesBothRecordsAndRejectsDuplicates()
    {
        NPCManager manager = Add<NPCManager>("NPCs");
        NPCManager.Instance = manager;
        manager.CreateFounders(FoundingRules.GenerateCandidates(7).Take(3).ToList());
        List<NPCRuntime> npcs = NPCManager.Instance.GetLivingNPC()
            .OrderBy(npc => npc.CharacterId, System.StringComparer.Ordinal).ToList();

        Assert.IsTrue(manager.AddRelationship(npcs[0].CharacterId, npcs[1].CharacterId,
            RelationshipTag.Friend, "与乙结为好友，心境渐安", "与甲结为好友"));
        Assert.AreEqual(1, npcs[0].Character.lifeRecords.Count(record =>
            record.category == "Relationship" && record.text.Contains("心境渐安")));
        Assert.AreEqual(1, npcs[1].Character.lifeRecords.Count(record =>
            record.category == "Relationship" && record.text == "与甲结为好友"));

        int recordsBefore = npcs[0].Character.lifeRecords.Count + npcs[1].Character.lifeRecords.Count;
        Assert.IsFalse(manager.AddRelationship(npcs[0].CharacterId, npcs[1].CharacterId, RelationshipTag.Friend));
        Assert.AreEqual(recordsBefore, npcs[0].Character.lifeRecords.Count + npcs[1].Character.lifeRecords.Count);

        Assert.IsTrue(manager.AddRelationship(npcs[0].CharacterId, npcs[2].CharacterId, RelationshipTag.MasterApprentice));
        Assert.IsTrue(npcs[0].Character.lifeRecords.Any(record => record.text.Contains("结为师徒")));
        Assert.IsTrue(npcs[2].Character.lifeRecords.Any(record => record.text.Contains("结为师徒")));
    }

    [Test]
    public void RecordRelationshipOutcome_WritesFallbackOnlyForLiving()
    {
        NPCManager manager = Add<NPCManager>("NPCs");
        NPCManager.Instance = manager;
        manager.CreateFounders(FoundingRules.GenerateCandidates(8).Take(3).ToList());
        NPCRuntime npc = NPCManager.Instance.GetLivingNPC()[0];

        manager.RecordRelationshipOutcome(npc.CharacterId, "独自静思，心境渐平");
        Assert.IsTrue(npc.Character.lifeRecords.Any(record =>
            record.category == "Relationship" && record.text == "独自静思，心境渐平"));
    }

    [Test]
    public void FindSocialTarget_IsDeterministicAndSkipsExistingRelations()
    {
        NPCManager manager = Add<NPCManager>("NPCs");
        NPCManager.Instance = manager;
        manager.CreateFounders(FoundingRules.GenerateCandidates(9).Take(3).ToList());
        List<NPCRuntime> npcs = NPCManager.Instance.GetLivingNPC()
            .OrderBy(npc => npc.CharacterId, System.StringComparer.Ordinal).ToList();

        NPCRuntime expectedForFirst = npcs[1];
        Assert.AreSame(expectedForFirst, DiscipleMissionBridge.FindSocialTarget(npcs[0]));

        manager.AddRelationship(npcs[0].CharacterId, npcs[1].CharacterId, RelationshipTag.Friend);
        Assert.AreSame(npcs[2], DiscipleMissionBridge.FindSocialTarget(npcs[0]));

        // 反向关系同样排除
        Assert.AreSame(npcs[2], DiscipleMissionBridge.FindSocialTarget(npcs[1]));
    }

    [Test]
    public void SocialCompletion_WritesRelationshipResultOnceAndHasFallback()
    {
        TimeManager time = Add<TimeManager>("Time");
        TimeManager.Instance = time;
        NPCManager manager = Add<NPCManager>("NPCs");
        NPCManager.Instance = manager;
        manager.CreateFounders(FoundingRules.GenerateCandidates(10).Take(3).ToList());
        time.RestoreDay(3);

        List<NPCRuntime> npcs = NPCManager.Instance.GetLivingNPC()
            .OrderBy(npc => npc.CharacterId, System.StringComparer.Ordinal).ToList();
        const string socialMissionId = "disciple_ai_social_001";

        npcs[0].Character.AddLifeRecord(3, "Mission", "达标：同门往来", socialMissionId);
        Assert.IsTrue(DiscipleMissionBridge.TryProcessCompletedSocial(npcs[0], 3));
        RelationshipRecord relation = npcs[0].Character.relationships.Single(record =>
            record.tag == RelationshipTag.Friend && record.createdDay == 3);
        Assert.IsTrue(npcs[0].Character.lifeRecords.Any(record =>
            record.day == 3 && record.category == "Relationship" && record.text.Contains("心境渐安")));
        Assert.IsTrue(npcs[0].Character.lifeRecords.Any(record =>
            record.day == 3 && record.category == "Relationship"));

        int recordsBefore = npcs[0].Character.lifeRecords.Count;
        Assert.IsFalse(DiscipleMissionBridge.TryProcessCompletedSocial(npcs[0], 3));
        Assert.AreEqual(recordsBefore, npcs[0].Character.lifeRecords.Count);

        // 无目标兜底：第 3 天补齐与另一名弟子的关系后，第 4 天找不到目标。
        string unchosenId = NPCManager.Instance.GetLivingNPC()
            .First(npc => npc.CharacterId != npcs[0].CharacterId && npc.CharacterId != relation.targetCharacterId).CharacterId;
        manager.AddRelationship(npcs[0].CharacterId, unchosenId, RelationshipTag.Friend);
        time.RestoreDay(4);
        npcs[0].Character.AddLifeRecord(4, "Mission", "达标：同门往来", socialMissionId);
        Assert.IsTrue(DiscipleMissionBridge.TryProcessCompletedSocial(npcs[0], 4));
        Assert.IsTrue(npcs[0].Character.lifeRecords.Any(record =>
            record.day == 4 && record.category == "Relationship" && record.text == "独自静思，心境渐平"));
    }

    // ---------------------------------------------------------------
    // 配置与经历
    // ---------------------------------------------------------------

    [Test]
    public void AutonomousMissionEndDetection_IgnoresPlayerAssignableMissions()
    {
        MissionManager missions = Add<MissionManager>("Missions");
        MissionManager.Instance = missions;
        missions.LoadMissionsFromJson();
        NPCRuntime npc = CreateRuntime("t_end_detect", "测试弟子");

        npc.Character.AddLifeRecord(5, "Mission", "达标：炼制疗伤丹", "production_001");
        Assert.IsFalse(DiscipleMissionBridge.HadAutonomousMissionEndToday(npc, 5));

        npc.Character.AddLifeRecord(5, "Mission", "达标：同门往来", "disciple_ai_social_001");
        Assert.IsTrue(DiscipleMissionBridge.HadAutonomousMissionEndToday(npc, 5));
    }

    [Test]
    public void ConfigLoader_LoadsAllAndFindsNoMissingMissionReferences()
    {
        Add<PlayerManager>("Player");
        MissionManager missions = Add<MissionManager>("Missions");
        MissionManager.Instance = missions;
        missions.LoadMissionsFromJson();

        DiscipleAIConfig config = DiscipleAIConfigLoader.Load();
        Assert.AreEqual(4, config.Goals.Count);
        Assert.AreEqual(5, config.Actions.Count);
        Assert.AreEqual(1, config.Identities.Count);
        Assert.IsNotNull(config.GetIdentity("inner_disciple"));
        Assert.AreEqual(0, DiscipleAIConfigLoader.FindMissingMissionReferences().Count);
    }

    [Test]
    public void ExperienceGenerator_WritesDecisionRecordWithReasonAndAction()
    {
        NPCRuntime npc = CreateRuntime("t_exp", "楚禾");
        ActionDefinition action = new ActionDefinition { id = "cultivate", displayName = "自由修炼" };

        Assert.IsTrue(ExperienceGenerator.WriteDecisionRecord(npc, action, "提升修为", 7));
        LifeRecord record = npc.Character.lifeRecords.Single(item => item.category == "Decision");
        Assert.AreEqual("因提升修为，决定自由修炼", record.text);
        Assert.AreEqual("cultivate", record.sourceId);
        Assert.IsTrue(ExperienceGenerator.HasDecisionRecordOn(npc, 7));
        Assert.IsFalse(ExperienceGenerator.HasDecisionRecordOn(npc, 8));
    }

    // ---------------------------------------------------------------
    // 工具
    // ---------------------------------------------------------------

    private NPCRuntime CreateRuntime(string id, string displayName, CharacterState state = null)
    {
        NPCData data = ScriptableObject.CreateInstance<NPCData>();
        objects.Add(data);
        data.npcID = id;
        data.npcName = displayName;
        state = state ?? new CharacterState();
        state.characterId = id;
        state.displayName = displayName;
        return new NPCRuntime(data, state);
    }

    private T Add<T>(string name) where T : Component
    {
        GameObject go = new GameObject(name);
        objects.Add(go);
        return go.AddComponent<T>();
    }
}
