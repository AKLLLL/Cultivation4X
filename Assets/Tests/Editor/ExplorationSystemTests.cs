using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

public class ExplorationSystemTests
{
    private readonly List<Object> created = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        PlayerManager.Instance = null;
        WarehouseManager.Instance = null;
        MissionManager.Instance = null;
        NPCManager.Instance = null;
        RewardManager.Instance = null;
        ExplorationRules.ClearCacheForTests();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in created) Object.DestroyImmediate(item);
        created.Clear();
        PlayerManager.Instance = null;
        WarehouseManager.Instance = null;
        MissionManager.Instance = null;
        NPCManager.Instance = null;
        RewardManager.Instance = null;
        ExplorationRules.ClearCacheForTests();
    }

    [Test]
    public void Config_HasThreeOrderedRegionsAndReferencedMissions()
    {
        MissionManager missions = Add<MissionManager>("Missions");
        missions.LoadMissionsFromJson();
        IReadOnlyList<ExplorationRegionDefinition> regions = ExplorationRules.GetRegions();
        Assert.AreEqual(3, regions.Count);
        CollectionAssert.AreEqual(new[] { "qingyun_outskirts", "mistwood", "chixia_ridge" }, regions.Select(item => item.id));
        Assert.IsTrue(regions.All(item => item.milestones.Count == ExplorationRules.MaxStage));
        Assert.IsTrue(regions.All(item => missions.GetMissionData(item.progressMissionId)?.explorationKind == ExplorationMissionKind.Progress));
        Assert.IsTrue(regions.All(item => missions.GetMissionData(item.ongoingMissionId)?.explorationKind == ExplorationMissionKind.Ongoing));
        Assert.AreEqual(ExplorationMissionKind.None, missions.GetMissionData("exploration_001").explorationKind);
    }

    [Test]
    public void V2Save_MigratesExplorationStateAndKeepsUnknownIds()
    {
        GameState state = JsonConvert.DeserializeObject<GameState>("{\"version\":2,\"sect\":{\"gold\":7,\"explorationRegions\":[{\"regionId\":\"qingyun_outskirts\",\"stage\":1},{\"regionId\":\"qingyun_outskirts\",\"stage\":8},{\"regionId\":\"future_region\",\"stage\":2}]}}");
        SaveManager.MigrateState(state);
        Assert.AreEqual(3, state.version);
        Assert.AreEqual(7, state.sect.gold);
        Assert.AreEqual(ExplorationRules.MaxStage, state.sect.explorationRegions.Single(item => item.regionId == "qingyun_outskirts").stage);
        Assert.AreEqual(2, state.sect.explorationRegions.Single(item => item.regionId == "future_region").stage);
    }

    [Test]
    public void ExplorationHall_IsBuiltAndCannotUpgrade()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        Assert.AreEqual(1, player.GetFacilityLevel(FacilityType.ExplorationHall));
        Assert.IsFalse(player.CanUpgradeFacility(FacilityType.ExplorationHall, out string reason));
        Assert.IsNotEmpty(reason);
    }

    [Test]
    public void SurveyThenProgress_RevealsFirstRegionAndEnqueuesItsEvent()
    {
        TestContext context = CreateContext();
        context.missions.TriggerMission(ExplorationRules.SurveyMissionId, context.npc);
        Mission survey = context.missions.GetActiveMissions().Single();
        survey.PassOneDay(); survey.PassOneDay(); survey.PassOneDay();
        ExplorationRegionState state = ExplorationRules.GetState("qingyun_outskirts");
        Assert.IsNotNull(state);
        Assert.AreEqual(0, state.stage);

        string progressId = ExplorationRules.GetRegion(state.regionId).progressMissionId;
        context.missions.TriggerMission(progressId, context.npc);
        Mission progress = context.missions.GetActiveMissions().Single();
        progress.PassOneDay(); progress.PassOneDay(); progress.PassOneDay();
        Assert.AreEqual(1, state.stage);
        Assert.IsTrue(context.events.GetInbox().Any(item => item.eventId == "exploration_event_qingyun"));
    }

    [Test]
    public void OngoingItemReward_WaitsAtZeroUntilWarehouseHasSpace()
    {
        TestContext context = CreateContext();
        context.player.playerData.explorationRegions.Add(new ExplorationRegionState { regionId = "chixia_ridge", stage = 3 });
        for (int i = 0; i < 9; i++) Assert.IsTrue(context.warehouse.TryAddItem("filler_" + i, 1));
        context.missions.TriggerMission("exploration_ongoing_chixia", context.npc);
        Mission ongoing = context.missions.GetActiveMissions().Single();
        ongoing.PassOneDay(); ongoing.PassOneDay(); ongoing.PassOneDay();
        Assert.AreEqual(MissionState.Active, ongoing.State);
        Assert.AreEqual(0, ongoing.RemainingDays);
        Assert.AreSame(ongoing, context.npc.CurrentMission);

        Assert.IsTrue(context.warehouse.RemoveItem("filler_0", 1));
        ongoing.PassOneDay();
        Assert.AreEqual(3, ongoing.RemainingDays);
        Assert.AreEqual(1, context.warehouse.GetItemCount("LingShi_001"));
    }

    [Test]
    public void SurveyOrderAndFinalStage_UnlockOngoingInsteadOfProgress()
    {
        TestContext context = CreateContext();
        CollectionAssert.AreEqual(new[] { "qingyun_outskirts", "mistwood", "chixia_ridge" },
            new[] { ExplorationRules.DiscoverNextRegion().regionId, ExplorationRules.DiscoverNextRegion().regionId,
                ExplorationRules.DiscoverNextRegion().regionId });
        ExplorationRegionState qingyun = ExplorationRules.GetState("qingyun_outskirts");
        qingyun.stage = 2;
        MissionData progress = context.missions.GetMissionData("exploration_progress_qingyun");
        context.missions.TriggerMission(progress.id, context.npc);
        PassDays(context.missions.GetActiveMissions().Single(), 3);
        Assert.AreEqual(3, qingyun.stage);
        Assert.IsFalse(context.missions.CanTriggerMission(progress.id, context.npc, out _));
        Assert.IsTrue(context.missions.CanTriggerMission("exploration_ongoing_qingyun", context.npc, out _));
    }

    [Test]
    public void SurveyAndKnownRegionProgressCanRunTogetherButSameKnownRegionIsUnique()
    {
        TestContext context = CreateContext();
        NPCRuntime second = CreateRuntime(context.npcs, "second");
        NPCRuntime third = CreateRuntime(context.npcs, "third");
        NPCRuntime fourth = CreateRuntime(context.npcs, "fourth");
        context.player.playerData.explorationRegions.Add(new ExplorationRegionState { regionId = "qingyun_outskirts", stage = 1 });
        context.player.playerData.explorationRegions.Add(new ExplorationRegionState { regionId = "mistwood", stage = 0 });
        context.player.playerData.explorationRegions.Add(new ExplorationRegionState { regionId = "chixia_ridge", stage = 0 });

        context.missions.TriggerMission(ExplorationRules.SurveyMissionId, context.npc);
        Assert.IsTrue(context.missions.CanTriggerMission("exploration_progress_qingyun", second, out _));
        context.missions.TriggerMission("exploration_progress_qingyun", second);
        Assert.IsTrue(context.missions.CanTriggerMission("exploration_progress_mistwood", third, out _));
        context.missions.TriggerMission("exploration_progress_mistwood", third);

        Assert.AreEqual(3, context.missions.GetActiveMissions().Count);
        Assert.IsFalse(context.missions.CanTriggerMission("exploration_progress_qingyun", fourth, out string sameRegionReason));
        StringAssert.Contains("该区域", sameRegionReason);
        Assert.IsFalse(context.missions.CanTriggerMission(ExplorationRules.SurveyMissionId, fourth, out string surveyReason));
        StringAssert.Contains("未知区域勘察", surveyReason);
    }

    [Test]
    public void OngoingCyclesStayActiveAndDoNotRecordOrdinaryCompletion()
    {
        TestContext context = CreateContext();
        context.player.playerData.explorationRegions.Add(new ExplorationRegionState { regionId = "mistwood", stage = 3 });
        context.missions.TriggerMission("exploration_ongoing_mistwood", context.npc);
        Mission ongoing = context.missions.GetActiveMissions().Single();
        PassDays(ongoing, 6);
        Assert.AreEqual(MissionState.Active, ongoing.State);
        Assert.AreEqual(3, ongoing.RemainingDays);
        Assert.AreEqual(40, context.npc.Cultivation);
        Assert.IsEmpty(context.missions.ConsumeDailyResults());
    }

    [Test]
    public void RecallReleasesNpcAndRedispatchStartsFreshCycle()
    {
        TestContext context = CreateContext();
        context.player.playerData.explorationRegions.Add(new ExplorationRegionState { regionId = "qingyun_outskirts", stage = 3 });
        context.missions.TriggerMission("exploration_ongoing_qingyun", context.npc);
        Mission first = context.missions.GetActiveMissions().Single();
        first.PassOneDay();
        Assert.AreEqual(2, first.RemainingDays);
        Assert.IsTrue(context.missions.TryRecallExplorationMission("qingyun_outskirts", out _));
        Assert.IsTrue(context.npc.CanDispatch());
        Assert.IsNull(context.npc.CurrentMission);
        context.missions.TriggerMission("exploration_ongoing_qingyun", context.npc);
        Assert.AreEqual(3, context.missions.GetActiveMissions().Single().RemainingDays);
    }

    [Test]
    public void OngoingAtZero_RestoresAsActiveAndBusy()
    {
        TestContext context = CreateContext();
        context.player.playerData.explorationRegions.Add(new ExplorationRegionState { regionId = "chixia_ridge", stage = 3 });
        context.missions.RestoreMissions(new[]
        {
            new MissionSaveData { missionId = "exploration_ongoing_chixia", assignedCharacterId = context.npc.CharacterId,
                state = MissionState.Active, remainingDays = 0, elapsedDays = 3 }
        });
        Mission restored = context.missions.GetActiveMissions().Single();
        Assert.AreEqual(MissionState.Active, restored.State);
        Assert.AreEqual(0, restored.RemainingDays);
        Assert.AreSame(restored, context.npc.CurrentMission);
        Assert.AreEqual(NPCState.Busy, context.npc.State);
    }

    [Test]
    public void OngoingUsesExistingInjuryAndPermanentTraumaCleanup()
    {
        TestContext context = CreateContext();
        context.player.playerData.explorationRegions.Add(new ExplorationRegionState { regionId = "qingyun_outskirts", stage = 3 });
        context.missions.TriggerMission("exploration_ongoing_qingyun", context.npc);
        Mission ongoing = context.missions.GetActiveMissions().Single();
        context.npcs.Injured(context.npc, 2);
        Assert.AreEqual(MissionState.Active, ongoing.State);
        Assert.AreSame(ongoing, context.npc.CurrentMission);
        context.npcs.Injured(context.npc, 5);
        Assert.AreEqual(MissionState.Failed, ongoing.State);
        Assert.IsEmpty(context.missions.GetActiveMissions());
        Assert.IsNull(context.npc.CurrentMission);

        NPCRuntime second = CreateRuntime(context.npcs, "trauma");
        context.missions.TriggerMission("exploration_ongoing_qingyun", second);
        context.npcs.ApplyPermanentTrauma(second, "test_trauma");
        Assert.AreEqual(HealthState.PermanentTrauma, second.Health);
        Assert.IsNull(second.CurrentMission);
        Assert.IsEmpty(context.missions.GetActiveMissions());
    }

    [Test]
    public void FullInboxAtFinalStage_RetriesOnOngoingStartAndDoesNotDuplicate()
    {
        TestContext context = CreateContext();
        context.player.playerData.explorationRegions.Add(new ExplorationRegionState { regionId = "qingyun_outskirts", stage = 2 });
        List<EventInboxEntry> full = Enumerable.Range(0, EventManager.InboxCapacity).Select(i => new EventInboxEntry
        {
            entryId = "full-" + i,
            eventId = "cultivation_insight",
            createdDay = 0,
            expiresDay = 99,
            participantIds = new Dictionary<string, string> { { "actor", context.npc.CharacterId } }
        }).ToList();
        context.events.RestoreState(null, null, 1, 0, full);
        context.missions.TriggerMission("exploration_progress_qingyun", context.npc);
        PassDays(context.missions.GetActiveMissions().Single(), 3);
        Assert.AreEqual(3, ExplorationRules.GetState("qingyun_outskirts").stage);
        Assert.IsNotEmpty(context.missions.LastExplorationNotice);
        Assert.IsFalse(context.events.GetInbox().Any(item => item.eventId == "exploration_event_qingyun"));

        context.events.RestoreState(null, null, 1, 0, new List<EventInboxEntry>());
        context.missions.TriggerMission("exploration_ongoing_qingyun", context.npc);
        Mission ongoing = context.missions.GetActiveMissions().Single();
        Assert.AreEqual(1, context.events.GetInbox().Count(item => item.eventId == "exploration_event_qingyun"));
        Assert.IsNull(context.missions.LastExplorationNotice);
        PassDays(ongoing, 3);
        Assert.AreEqual(1, context.events.GetInbox().Count(item => item.eventId == "exploration_event_qingyun"));
    }

    [Test]
    public void LegacySecretRealmActionStillUsesLevelScaledDurationAndReward()
    {
        TestContext context = CreateContext();
        context.player.playerData.secretRealmLevel = 3;
        Mission legacy = context.missions.CreateMission("exploration_001");
        Assert.AreEqual(ExplorationMissionKind.None, legacy.Data.explorationKind);
        Assert.AreEqual(FacilityRules.SecretRealmMaterialReward(3), legacy.Reward.Items.Single().count);
        legacy.StartMission(context.npc);
        Assert.AreEqual(FacilityRules.SecretRealmDays(3), legacy.RemainingDays);
    }

    private TestContext CreateContext()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        NPCManager npcs = Add<NPCManager>("NPCs");
        RewardManager rewards = Add<RewardManager>("Rewards");
        EventManager events = Add<EventManager>("Events");
        MissionManager missions = Add<MissionManager>("Missions");
        PlayerManager.Instance = player;
        WarehouseManager.Instance = warehouse;
        NPCManager.Instance = npcs;
        RewardManager.Instance = rewards;
        MissionManager.Instance = missions;
        missions.LoadMissionsFromJson();
        events.LoadDefinitions();
        NPCRuntime npc = CreateRuntime(npcs, "explorer");
        return new TestContext { player = player, warehouse = warehouse, npcs = npcs, missions = missions, events = events, npc = npc };
    }

    private T Add<T>(string name) where T : Component
    {
        GameObject item = new GameObject(name);
        created.Add(item);
        return item.AddComponent<T>();
    }

    private NPCRuntime CreateRuntime(NPCManager manager, string id)
    {
        NPCData template = ScriptableObject.CreateInstance<NPCData>();
        created.Add(template);
        template.npcID = id;
        template.npcName = id;
        NPCRuntime runtime = new NPCRuntime(template);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ((Dictionary<string, NPCRuntime>)typeof(NPCManager).GetField("npcById", flags).GetValue(manager))[id] = runtime;
        ((List<NPCRuntime>)typeof(NPCManager).GetField("runtimes", flags).GetValue(manager)).Add(runtime);
        return runtime;
    }

    private static void PassDays(Mission mission, int days)
    {
        for (int i = 0; i < days; i++) mission.PassOneDay();
    }

    private class TestContext
    {
        public PlayerManager player;
        public WarehouseManager warehouse;
        public NPCManager npcs;
        public MissionManager missions;
        public EventManager events;
        public NPCRuntime npc;
    }
}
