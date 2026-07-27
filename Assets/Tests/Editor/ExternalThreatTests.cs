using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

public class ExternalThreatTests
{
    private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

    [SetUp]
    public void SetUp()
    {
        PlayerManager.Instance = null;
        WarehouseManager.Instance = null;
        MissionManager.Instance = null;
        NPCManager.Instance = null;
        RewardManager.Instance = null;
        TimeManager.Instance = null;
        UIManager.Instance = null;
        ExternalThreatPanel.Instance = null;
        ExternalThreatRules.ResetForTests();
        FoundingRules.ResetCatalogForTests();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (UnityEngine.Object item in created)
            if (item != null) UnityEngine.Object.DestroyImmediate(item);
        created.Clear();
        PlayerManager.Instance = null;
        WarehouseManager.Instance = null;
        MissionManager.Instance = null;
        NPCManager.Instance = null;
        RewardManager.Instance = null;
        TimeManager.Instance = null;
        UIManager.Instance = null;
        ExternalThreatPanel.Instance = null;
        ExternalThreatRules.ResetForTests();
        FoundingRules.ResetCatalogForTests();
    }

    [Test]
    public void CombatPowerCalculator_PreservesFrozenFormulaAndCapsExperience()
    {
        CombatPowerInput input = new CombatPowerInput
        {
            attack = 10,
            agility = 8,
            physique = 9,
            combatComprehension = 7,
            combatExperience = 140,
            realm = CultivationRealm.QiRefining,
            techniqueBonus = 20,
            artifactBonus = 6
        };
        Assert.AreEqual(10 * 2 + 8 + 9 * 2 + 7 + 100 + 20 + 20 + 6,
            CombatPowerCalculator.Calculate(input));
    }

    [Test]
    public void CombatResolver_AggregatesPartyAndHonorsTierBoundaries()
    {
        Assert.AreEqual(151, CombatResolver.AggregatePartyPower(new[]
        {
            new CombatantPower { characterId = "a", power = 101 },
            new CombatantPower { characterId = "b", power = 99 },
            new CombatantPower { characterId = "c", power = 2 }
        }));

        Assert.AreEqual(CombatResultTier.PerfectVictory, ResolvePower(140).resultTier);
        Assert.AreEqual(CombatResultTier.Victory, ResolvePower(100).resultTier);
        Assert.AreEqual(CombatResultTier.CostlyVictory, ResolvePower(72).resultTier);
        Assert.AreEqual(CombatResultTier.Failure, ResolvePower(60).resultTier);
        Assert.AreEqual(CombatResultTier.CatastrophicDefeat, ResolvePower(50).resultTier);
        Assert.IsTrue(ResolvePower(60).retreatSucceeded);
        Assert.IsFalse(ResolvePower(50).retreatSucceeded);
    }

    [Test]
    public void CombatResolver_UsesFrozenIntelligenceAndFirstExchangeBoundaries()
    {
        CombatResolution intel24 = Resolve(100, 100, 24);
        CombatResolution intel25 = Resolve(100, 100, 25);
        CombatResolution intel50 = Resolve(100, 100, 50);
        Assert.AreEqual(0.9f, intel24.initiativeModifier, 0.0001f);
        Assert.AreEqual(1f, intel25.initiativeModifier, 0.0001f);
        Assert.AreEqual(1.1f, intel50.initiativeModifier, 0.0001f);
        Assert.AreEqual(1.06f, intel24.intelligenceModifier, 0.0001f);
        Assert.AreEqual(1.125f, intel50.intelligenceModifier, 0.0001f);

        CombatResolution directPerfect = Resolve(150, 100, 25);
        CombatResolution notDirectPerfect = Resolve(149, 100, 25);
        CombatResolution notDirectDefeat = Resolve(60, 100, 25);
        CombatResolution directDefeat = Resolve(59, 100, 25);
        Assert.IsTrue(directPerfect.endedAfterFirstExchange);
        Assert.IsFalse(notDirectPerfect.endedAfterFirstExchange);
        Assert.IsFalse(notDirectDefeat.endedAfterFirstExchange);
        Assert.IsTrue(directDefeat.endedAfterFirstExchange);
    }

    [Test]
    public void ThreatSchedule_ActivatesAfterFiveDaysAndRaidsEveryFiveDays()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding.village.relation = 20;
        player.playerData.founding.village.totalLabor = 12;

        Assert.IsTrue(ExternalThreatRules.TryScheduleFromRelation(0));
        Assert.AreEqual(5, player.playerData.founding.externalThreat.scheduledDay);
        ExternalThreatRules.ProcessDay(4);
        Assert.AreEqual(ExternalThreatStatus.Scheduled, player.playerData.founding.externalThreat.status);
        ExternalThreatRules.ProcessDay(5);
        Assert.AreEqual(ExternalThreatStatus.Active, player.playerData.founding.externalThreat.status);
        Assert.AreEqual(10, player.playerData.founding.externalThreat.nextRaidDay);

        ExternalThreatRules.ProcessDay(10);
        Assert.AreEqual(2, player.playerData.founding.village.totalLabor);
        Assert.AreEqual(100, player.playerData.founding.village.population);
        ExternalThreatRules.ProcessDay(15);
        Assert.AreEqual(0, player.playerData.founding.village.totalLabor);
        Assert.AreEqual(96, player.playerData.founding.village.population);
        Assert.AreEqual(2, player.playerData.founding.externalThreat.raidCount);
    }

    [Test]
    public void DiscoveryNotification_RetriesUntilInboxHasCapacityAndPersistsSuccess()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding.externalThreat = new ActiveThreatState
        {
            threatId = ExternalThreatRules.QingshiThreatId,
            status = ExternalThreatStatus.Scheduled,
            scheduledDay = 5
        };
        NPCManager npcs = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcs;
        NPCRuntime actor = Register(npcs, Runtime("inbox-capacity"));
        EventManager events = Add<EventManager>("Events");
        events.LoadDefinitions();
        List<EventInboxEntry> full = Enumerable.Range(0, EventManager.InboxCapacity)
            .Select(index => new EventInboxEntry
            {
                entryId = "full-" + index,
                eventId = "cultivation_insight",
                createdDay = 1,
                expiresDay = 99,
                participantIds = new Dictionary<string, string> { { "actor", actor.CharacterId } }
            }).ToList();
        events.RestoreState(null, null, 1, 0, full);

        ExternalThreatRules.ProcessDay(5);

        ActiveThreatState state = player.playerData.founding.externalThreat;
        Assert.AreEqual(ExternalThreatStatus.Active, state.status);
        Assert.IsFalse(state.discoveryNotificationEnqueued);
        Assert.IsFalse(events.GetInbox().Any(item => item.eventId == ExternalThreatPanel.DiscoveryEventId));

        events.RestoreState(null, null, 1, events.RandomRollCount, full.Take(4));
        ExternalThreatRules.ProcessDay(6);

        Assert.IsTrue(state.discoveryNotificationEnqueued);
        Assert.AreEqual(5, events.GetInbox().Count);
        Assert.AreEqual(1, events.GetInbox().Count(item => item.eventId == ExternalThreatPanel.DiscoveryEventId));
        ActiveThreatState restored = JsonConvert.DeserializeObject<ActiveThreatState>(JsonConvert.SerializeObject(state));
        Assert.IsTrue(restored.discoveryNotificationEnqueued);

        state.status = ExternalThreatStatus.Resolved;
        state.discoveryNotificationEnqueued = false;
        events.RestoreState(null, null, 1, events.RandomRollCount, full.Take(4));
        ExternalThreatRules.ProcessDay(7);
        Assert.IsFalse(events.GetInbox().Any(item => item.eventId == ExternalThreatPanel.DiscoveryEventId));
    }

    [Test]
    public void DiscoveryNotification_SurvivesEventDailyResetInSettlement()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding.externalThreat = new ActiveThreatState
        {
            threatId = ExternalThreatRules.QingshiThreatId,
            status = ExternalThreatStatus.Scheduled,
            scheduledDay = 1
        };
        Add<EventManager>("Events").LoadDefinitions();
        TimeManager time = Add<TimeManager>("Time");
        TimeManager.Instance = time;

        time.EndDay();

        Assert.IsTrue(player.playerData.founding.externalThreat.discoveryNotificationEnqueued);
        Assert.IsTrue(time.UnreadDaySettlement.newEventTitles.Any(item =>
            item.Contains("发现外部威胁") && item.Contains("青石村")));
    }

    [Test]
    public void RaidPopulationLoss_UsesFloorForOddShortfall()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding.externalThreat = ActiveThreat(0);
        player.playerData.founding.externalThreat.discoveryNotificationEnqueued = true;
        player.playerData.founding.village.totalLabor = 7;
        player.playerData.founding.village.population = 100;

        ExternalThreatRules.ProcessDay(10);

        Assert.AreEqual(0, player.playerData.founding.village.totalLabor);
        Assert.AreEqual(99, player.playerData.founding.village.population,
            "shortfall=3 时应 floor(3/2)=1");
    }

    [Test]
    public void VillageRaid_CancelsLongestLaborMissionWithoutInjury()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        MissionManager missions = Add<MissionManager>("Missions");
        MissionManager.Instance = missions;
        player.playerData.founding.village.totalLabor = 20;
        Assert.IsTrue(player.TryReserveLabor(10, out _));
        Assert.IsTrue(player.TryReserveLabor(5, out _));

        Mission longMission = new Mission(new MissionData { id = "a-long", name = "长任务", needDays = 5, laborCost = 10 });
        Mission shortMission = new Mission(new MissionData { id = "b-short", name = "短任务", needDays = 2, laborCost = 5 });
        longMission.StartLaborMission();
        shortMission.StartLaborMission();
        missions.AddActiveMission(longMission);
        missions.AddActiveMission(shortMission);
        player.playerData.founding.village.totalLabor = 5;

        Assert.AreEqual(1, missions.CancelLaborMissionsUntilValid());
        Assert.AreEqual(MissionState.Failed, longMission.State);
        Assert.AreEqual(MissionState.Active, shortMission.State);
        Assert.AreEqual(5, player.playerData.founding.village.reservedLabor);
        Assert.IsTrue(missions.ConsumeDailyResults().Single().missionName.Contains("青石村受袭中止"));
    }

    [Test]
    public void Investigation_IsDeterministicRepeatableAndCapsAtOneHundred()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding.externalThreat = ActiveThreat(0);
        NPCRuntime npc = Runtime("investigator", intelligence: 12);

        Assert.AreEqual(22, ExternalThreatRules.AddIntelligence(npc));
        Assert.AreEqual(22, player.playerData.founding.externalThreat.intelligence);
        for (int i = 0; i < 10; i++) ExternalThreatRules.AddIntelligence(npc);
        Assert.AreEqual(100, player.playerData.founding.externalThreat.intelligence);
        Assert.AreEqual(0, ExternalThreatRules.AddIntelligence(npc));
    }

    [Test]
    public void InvestigationMission_CompletesAfterEvaluationWithoutInjury()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding.externalThreat = ActiveThreat(0);
        NPCManager npcs = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcs;
        MissionManager missions = Add<MissionManager>("Missions");
        MissionManager.Instance = missions;
        NPCRuntime npc = Runtime("mission-investigator", intelligence: 10);
        Mission mission = new Mission(new MissionData
        {
            id = "investigate",
            name = "调查",
            needDays = 2,
            threatMissionKind = ThreatMissionKind.Investigation
        });
        mission.StartMission(npc);
        missions.AddActiveMission(mission);

        missions.EvaluateMission(mission);

        Assert.AreEqual(MissionState.Completed, mission.State);
        Assert.AreEqual(20, player.playerData.founding.externalThreat.intelligence);
        Assert.AreEqual(HealthState.Healthy, npc.Health);
        Assert.AreEqual(NPCState.Idle, npc.State);
    }

    [Test]
    public void Response_IsAllowedWhileInvestigationMissionIsRunning()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding.externalThreat = ActiveThreat(0);
        NPCManager npcs = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcs;
        MissionManager missions = Add<MissionManager>("Missions");
        MissionManager.Instance = missions;
        NPCRuntime investigator = Runtime("running-investigator", intelligence: 10);
        Mission investigation = new Mission(new MissionData
        {
            id = "investigate",
            name = "调查",
            needDays = 2,
            threatMissionKind = ThreatMissionKind.Investigation
        });
        investigation.StartMission(investigator);
        missions.AddActiveMission(investigation);
        NPCRuntime fighter = Runtime("fighter", attack: 60, intelligence: 20, agility: 20,
            physique: 20, combatComprehension: 20, combatExperience: 100, realm: CultivationRealm.GoldenCore);

        Assert.IsTrue(ExternalThreatRules.IsInvestigationRunning());
        Assert.IsTrue(ExternalThreatRules.CanRespond(new[] { fighter }, CombatPlanType.HeadOn, out string reason), reason);
    }

    [Test]
    public void InvestigationMission_CompletesSafelyWhenThreatAlreadyResolved()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding.externalThreat = ActiveThreat(50);
        player.playerData.founding.externalThreat.status = ExternalThreatStatus.Resolved;
        NPCManager npcs = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcs;
        MissionManager missions = Add<MissionManager>("Missions");
        MissionManager.Instance = missions;
        NPCRuntime npc = Runtime("late-investigator", intelligence: 10);
        Mission mission = new Mission(new MissionData
        {
            id = "investigate",
            name = "调查",
            needDays = 2,
            threatMissionKind = ThreatMissionKind.Investigation
        });
        mission.StartMission(npc);
        missions.AddActiveMission(mission);

        missions.EvaluateMission(mission);

        Assert.AreEqual(MissionState.Completed, mission.State);
        Assert.AreEqual(50, player.playerData.founding.externalThreat.intelligence);
        Assert.AreEqual(NPCState.Idle, npc.State);
    }

    [Test]
    public void VillageLabor_IsGrantedOnceAndHelpRestoresOnlyAfterResolution()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        VillageState village = player.playerData.founding.village;
        village.relation = 49;
        village.totalLabor = 0;

        player.AddVillageRelation(1);
        Assert.AreEqual(50, village.totalLabor);
        Assert.IsTrue(village.supportLaborGranted);
        Assert.AreEqual(ExternalThreatStatus.Scheduled, player.playerData.founding.externalThreat.status);

        village.totalLabor = 20;
        player.AddVillageRelation(-1);
        player.AddVillageRelation(1);
        Assert.AreEqual(20, village.totalLabor);

        ExternalThreatRules.RestoreLaborAfterVillageHelp();
        Assert.AreEqual(20, village.totalLabor);
        player.playerData.founding.externalThreat.status = ExternalThreatStatus.Resolved;
        ExternalThreatRules.RestoreLaborAfterVillageHelp();
        Assert.AreEqual(30, village.totalLabor);
        village.totalLabor = 48;
        ExternalThreatRules.RestoreLaborAfterVillageHelp();
        Assert.AreEqual(50, village.totalLabor);
    }

    [Test]
    public void HeadOnDefenseAndCaveRetreat_ResolveAndPersistActualChanges()
    {
        NPCRuntime strong = Runtime("strong", attack: 60, intelligence: 20, agility: 20,
            physique: 20, combatComprehension: 20, combatExperience: 100, realm: CultivationRealm.GoldenCore);

        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding.externalThreat = ActiveThreat(100);
        ThreatResolutionRecord headOn = ExternalThreatRules.ResolveThreat(new[] { strong }, CombatPlanType.HeadOn, out string reason);
        Assert.IsNotNull(headOn, reason);
        Assert.AreEqual(CombatResultTier.PerfectVictory, headOn.combat.resultTier);
        Assert.AreEqual(ExternalThreatStatus.Resolved, player.playerData.founding.externalThreat.status);

        player.playerData.founding.externalThreat = ActiveThreat(100);
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        WarehouseManager.Instance = warehouse;
        warehouse.warehouseData.items = new List<ItemStack>
            { new ItemStack { itemId = FacilityRules.BasicMaterialId, count = 5 } };
        TimeManager time = Add<TimeManager>("Time");
        TimeManager.Instance = time;
        ThreatResolutionRecord defense = ExternalThreatRules.ResolveThreat(new[] { strong }, CombatPlanType.SimpleDefense, out reason);
        Assert.IsNotNull(defense, reason);
        Assert.AreEqual(2, warehouse.GetItemCount(FacilityRules.BasicMaterialId));
        time.EndDay();
        Assert.AreEqual(-3, time.UnreadDaySettlement.basicMaterialChange);

        player.playerData.founding.externalThreat = ActiveThreat(0);
        VillageState village = player.playerData.founding.village;
        village.population = 3;
        village.totalLabor = 4;
        village.relation = 5;
        ThreatResolutionRecord retreat = ExternalThreatRules.ResolveThreat(null, CombatPlanType.RetreatToCave, out reason);
        Assert.IsNotNull(retreat, reason);
        Assert.AreEqual(-3, retreat.populationChange);
        Assert.AreEqual(-4, retreat.laborChange);
        Assert.AreEqual(-5, retreat.relationChange);
        Assert.AreEqual(0, village.population);
        Assert.AreEqual(0, village.totalLabor);
        Assert.AreEqual(0, village.relation);
    }

    [Test]
    public void ThreatState_JsonRoundTripPreservesFullResolution()
    {
        PlayerData source = new PlayerData();
        source.founding.externalThreat = ActiveThreat(75);
        source.founding.externalThreat.status = ExternalThreatStatus.Resolved;
        source.founding.externalThreat.resolution = new ThreatResolutionRecord
        {
            day = 18,
            plan = CombatPlanType.SimpleDefense,
            participantIds = new List<string> { "a", "b" },
            populationChange = -2,
            narrative = "固定结算文本",
            combat = new CombatResolution
            {
                partyPower = 170,
                threatPower = 150,
                firstExchangeRatio = 1.2f,
                finalRatio = 1.4f,
                resultTier = CombatResultTier.Victory
            }
        };

        PlayerData restored = JsonConvert.DeserializeObject<PlayerData>(JsonConvert.SerializeObject(source));

        Assert.AreEqual(ExternalThreatStatus.Resolved, restored.founding.externalThreat.status);
        Assert.AreEqual(CombatPlanType.SimpleDefense, restored.founding.externalThreat.resolution.plan);
        Assert.AreEqual(170, restored.founding.externalThreat.resolution.combat.partyPower);
        Assert.AreEqual("固定结算文本", restored.founding.externalThreat.resolution.narrative);
    }

    [Test]
    public void EventCadence_AddsOneOrdinaryAtDayTenAndDirectCriticalRemainsImmediate()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding.completed = true;
        player.playerData.founding.stage = FoundingStage.Completed;
        NPCManager npcs = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcs;
        NPCRuntime actor = Register(npcs, Runtime("event-actor"));
        EventManager events = Add<EventManager>("Events");
        events.LoadDefinitions();

        events.ProcessDay(9);
        Assert.IsEmpty(events.GetInbox());
        events.ProcessDay(10);
        Assert.AreEqual(1, events.GetInbox().Count);
        Assert.IsFalse(events.GetInbox().Any(entry => events.GetDefinitions()
            .Single(definition => definition.id == entry.eventId).isCritical));

        int before = events.GetInbox().Count;
        int rollsBefore = events.RandomRollCount;
        Assert.IsFalse(events.TryTriggerSource(EventSource.MissionComplete, actor));
        Assert.AreEqual(rollsBefore + 1, events.RandomRollCount,
            "非显式来源仍应先执行原 SourceChance 掷骰");
        Assert.AreEqual(before, events.GetInbox().Count);
        Assert.IsTrue(events.TryEnqueueEventById("old_enemy", actor));
        Assert.AreEqual(before + 1, events.GetInbox().Count);
        Assert.IsTrue(events.GetDefinitions().Single(item => item.id == "old_enemy").isCritical);
    }

    [Test]
    public void DiscoveryInspectBridge_OpensThroughUiManagerStackAndCanCloseTop()
    {
        UIManager ui = Add<UIManager>("UI");
        UIManager.Instance = ui;
        ExternalThreatPanel threatPanel = Add<ExternalThreatPanel>("ThreatPanel");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(ExternalThreatPanel).GetMethod("Awake", flags).Invoke(threatPanel, null);
        ExternalThreatPanel.Instance = threatPanel;
        RectTransform panel = (RectTransform)typeof(ExternalThreatPanel).GetField("panel", flags).GetValue(threatPanel);

        Assert.AreSame(ui, UIManager.Instance);
        Assert.AreSame(threatPanel, ExternalThreatPanel.Instance);
        Assert.IsNotNull(panel);
        Assert.IsFalse(ExternalThreatPanel.TryOpenFromEvent("other", ExternalThreatPanel.InspectOptionId));
        Assert.IsTrue(ExternalThreatPanel.TryOpenFromEvent(
            ExternalThreatPanel.DiscoveryEventId, ExternalThreatPanel.InspectOptionId));
        Assert.IsTrue(panel.gameObject.activeSelf);
        object stack = typeof(UIManager).GetField("panelStack", flags).GetValue(ui);
        Assert.AreEqual(1, (int)stack.GetType().GetProperty("Count").GetValue(stack));

        ui.CloseTopPanel();
        Assert.IsFalse(panel.gameObject.activeSelf);
        Assert.AreEqual(0, (int)stack.GetType().GetProperty("Count").GetValue(stack));
    }

    [Test]
    public void ThreatConfigs_LoadAndCrossReferencesExist()
    {
        ExternalThreatRules.LoadDefinitions();
        ExternalThreatDefinition threat = ExternalThreatRules.GetDefinition(ExternalThreatRules.QingshiThreatId);
        Assert.IsNotNull(threat);
        Assert.IsTrue(ExternalThreatRules.ValidateDefinition(threat, out string reason), reason);
        Assert.AreEqual(150, threat.threatPower);
        Assert.AreEqual(3, threat.defenseMaterialCost);

        MissionManager missions = Add<MissionManager>("Missions");
        missions.LoadMissionsFromJson();
        MissionData investigation = missions.GetMissionData(threat.investigationMissionId);
        Assert.IsNotNull(investigation);
        Assert.AreEqual(ThreatMissionKind.Investigation, investigation.threatMissionKind);
        Assert.AreEqual(2, investigation.needDays);

        EventManager events = Add<EventManager>("Events");
        events.LoadDefinitions();
        Assert.IsTrue(events.GetDefinitions().Any(item => item.id == threat.discoveredEventId));
        Assert.IsTrue(events.GetDefinitions().Single(item => item.id == threat.discoveredEventId).directOnly);
        MissionData combat = missions.GetMissionData("combat_001");
        Assert.IsFalse(combat.description.Contains("村庄"));
    }

    private CombatResolution ResolvePower(int power)
    {
        return Resolve(power, 100, 100);
    }

    private CombatResolution Resolve(int power, int threatPower, int intelligence)
    {
        return CombatResolver.Resolve(new CombatRequest
        {
            combatants = new List<CombatantPower> { new CombatantPower { characterId = "a", power = power } },
            threatPower = threatPower,
            intelligence = intelligence,
            preparationModifier = 1f
        });
    }

    private ActiveThreatState ActiveThreat(int intelligence)
    {
        return new ActiveThreatState
        {
            threatId = ExternalThreatRules.QingshiThreatId,
            status = ExternalThreatStatus.Active,
            activatedDay = 5,
            nextRaidDay = 10,
            intelligence = intelligence
        };
    }

    private NPCRuntime Runtime(string id, int attack = 10, int intelligence = 10, int agility = 10,
        int physique = 10, int combatComprehension = 10, int combatExperience = 0,
        CultivationRealm realm = CultivationRealm.QiRefining)
    {
        NPCData data = ScriptableObject.CreateInstance<NPCData>();
        created.Add(data);
        data.npcID = id;
        data.npcName = id;
        data.attack = attack;
        data.intelligence = intelligence;
        data.agility = agility;
        data.physique = physique;
        data.combatComprehension = combatComprehension;
        CharacterState state = new CharacterState
        {
            characterId = id,
            templateId = id,
            displayName = id,
            realm = realm,
            combatExperience = combatExperience
        };
        return new NPCRuntime(data, state);
    }

    private NPCRuntime Register(NPCManager manager, NPCRuntime runtime)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ((Dictionary<string, NPCRuntime>)typeof(NPCManager).GetField("npcById", flags).GetValue(manager))
            [runtime.CharacterId] = runtime;
        ((List<NPCRuntime>)typeof(NPCManager).GetField("runtimes", flags).GetValue(manager)).Add(runtime);
        return runtime;
    }

    private T Add<T>(string name) where T : Component
    {
        GameObject item = new GameObject(name);
        created.Add(item);
        T component = item.AddComponent<T>();
        if (component is PlayerManager player) PlayerManager.Instance = player;
        else if (component is WarehouseManager warehouse) WarehouseManager.Instance = warehouse;
        else if (component is MissionManager missions) MissionManager.Instance = missions;
        else if (component is NPCManager npcs) NPCManager.Instance = npcs;
        else if (component is TimeManager time) TimeManager.Instance = time;
        else if (component is UIManager ui) UIManager.Instance = ui;
        return component;
    }
}
