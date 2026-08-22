using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

public class FoundingSystemTests
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
        FoundingRules.ResetCatalogForTests();
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
        FoundingRules.ResetCatalogForTests();
    }

    [Test]
    public void CandidateGeneration_IsStableUniqueAndContainsHighAptitude()
    {
        List<FounderCandidateData> first = FoundingRules.GenerateCandidates(12345);
        List<FounderCandidateData> second = FoundingRules.GenerateCandidates(12345);
        Assert.AreEqual(10, first.Count);
        Assert.AreEqual(10, first.Select(item => item.displayName).Distinct().Count());
        Assert.AreEqual(JsonConvert.SerializeObject(first), JsonConvert.SerializeObject(second));
        Assert.IsTrue(first.Any(item => item.aptitudeRank >= 4));
        Assert.IsTrue(first.All(item => item.age >= 15 && item.age <= 18));
        Assert.IsTrue(first.All(item => item.attack >= 5 && item.attack <= 20));
        Assert.IsTrue(first.All(item => item.combatComprehension >= 5 && item.combatComprehension <= 20));
    }

    [Test]
    public void V3Save_MigratesAsCompletedAndPreservesExistingFacilityLevels()
    {
        GameState state = JsonConvert.DeserializeObject<GameState>(
            "{\"version\":3,\"sect\":{\"missionHallLevel\":2,\"trainingRoomLevel\":1,\"warehouseLevel\":1,\"secretRealmLevel\":1,\"alchemyRoomLevel\":1},\"characters\":[]}");
        SaveManager.MigrateState(state);
        Assert.AreEqual(SaveDataVersion.Current, state.version);
        Assert.IsTrue(state.sect.founding.completed);
        Assert.AreEqual(FoundingStage.Completed, state.sect.founding.stage);
        Assert.AreEqual(2, state.sect.missionHallLevel);
    }

    [Test]
    public void V4Save_PreservesZeroFacilitiesAndIncompleteFounding()
    {
        GameState state = JsonConvert.DeserializeObject<GameState>(
            "{\"version\":4,\"sect\":{\"missionHallLevel\":0,\"trainingRoomLevel\":0,\"warehouseLevel\":0,\"secretRealmLevel\":0,\"alchemyRoomLevel\":0,\"founding\":{\"initialized\":true,\"completed\":false,\"stage\":\"Cave\",\"techniqueUnderstanding\":42}}}");
        SaveManager.MigrateState(state);
        Assert.AreEqual(0, state.sect.missionHallLevel);
        Assert.IsFalse(state.sect.founding.completed);
        Assert.AreEqual(42, state.sect.founding.techniqueUnderstanding);
    }

    [Test]
    public void GeneratedFounders_RestoreWithoutScriptableObjectTemplates()
    {
        NPCManager manager = Add<NPCManager>("NPCs");
        List<FounderCandidateData> selected = FoundingRules.GenerateCandidates(77).Take(3).ToList();
        Assert.IsTrue(manager.CreateFounders(selected));
        List<CharacterState> saved = JsonConvert.DeserializeObject<List<CharacterState>>(
            JsonConvert.SerializeObject(manager.GetAllNPC().Select(item => item.Character).ToList()));

        GameObject managerObject = manager.gameObject;
        objects.Remove(managerObject);
        Object.DestroyImmediate(managerObject);
        NPCManager.Instance = null;
        NPCManager restored = Add<NPCManager>("RestoredNPCs");
        restored.RestoreCharacters(saved);

        Assert.AreEqual(3, restored.GetAllNPC().Count);
        Assert.AreEqual(selected[0].attack, restored.GetRuntime(selected[0].candidateId).Attack);
        Assert.AreEqual(selected[0].comprehension, restored.GetRuntime(selected[0].candidateId).Comprehension);
        Assert.AreEqual(selected[0].combatComprehension, restored.GetRuntime(selected[0].candidateId).CombatComprehension);
        Assert.AreEqual(selected[0].aptitudeRank, restored.GetRuntime(selected[0].candidateId).AptitudeRank);
    }

    [Test]
    public void MortalRealm_PreservesOldNumericValuesButV1DoesNotAutoBreakThrough()
    {
        Assert.AreEqual(-1, (int)CultivationRealm.Mortal);
        Assert.AreEqual(0, (int)CultivationRealm.QiRefining);
        NPCData data = ScriptableObject.CreateInstance<NPCData>();
        objects.Add(data);
        data.npcID = "mortal";
        data.npcName = "凡人";
        CharacterState state = new CharacterState
        {
            characterId = "mortal",
            templateId = "mortal",
            displayName = "凡人",
            realm = CultivationRealm.Mortal,
            cultivation = 100
        };
        NPCRuntime runtime = new NPCRuntime(data, state);
        Assert.IsFalse(runtime.TryBreakthrough(1f));
        Assert.AreEqual(CultivationRealm.Mortal, runtime.Realm);
    }

    [Test]
    public void NewFoundingGame_UsesZeroFacilitiesButKeepsStarterResources()
    {
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        WarehouseManager.Instance = warehouse;
        PlayerManager player = Add<PlayerManager>("Player");
        player.InitializeNewFoundingGame(91);
        Assert.AreEqual(100, warehouse.GetItemCount(FacilityRules.SpiritStoneId));
        Assert.AreEqual(0, player.GetFacilityLevel(FacilityType.MissionHall));
        Assert.AreEqual(10, player.playerData.founding.candidates.Count);
    }

    [Test]
    public void StoryRepair_BypassesMissionHallAndSpendsCostAtomically()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        NPCManager npcs = Add<NPCManager>("NPCs");
        RewardManager rewards = Add<RewardManager>("Rewards");
        MissionManager missions = Add<MissionManager>("Missions");
        PlayerManager.Instance = player;
        WarehouseManager.Instance = warehouse;
        NPCManager.Instance = npcs;
        RewardManager.Instance = rewards;
        MissionManager.Instance = missions;
        player.InitializeNewFoundingGame(92);
        List<FounderCandidateData> selected = player.playerData.founding.candidates.Take(3).ToList();
        Assert.IsTrue(npcs.CreateFounders(selected));
        player.playerData.founding.selectedFounderIds = selected.Select(item => item.candidateId).ToList();
        player.playerData.founding.selectedTechniqueId = "qingmu";
        player.playerData.founding.stage = FoundingStage.Cave;
        missions.LoadMissionsFromJson();
        NPCRuntime actor = npcs.GetAllNPC()[0];

        Assert.IsTrue(missions.CanTriggerMission("founding_repair_spirit_array", actor, out string reason), reason);
        missions.TriggerMission("founding_repair_spirit_array", actor);
        Assert.AreEqual(1, missions.GetActiveMissions().Count);
        Assert.AreEqual(3, warehouse.GetItemCount(FacilityRules.BasicMaterialId));
        Mission repair = missions.GetActiveMissions().Single();
        repair.PassOneDay();
        repair.PassOneDay();
        repair.PassOneDay();
        Assert.AreEqual(1, player.GetFacilityLevel(FacilityType.TrainingRoom));
        Assert.IsTrue(actor.CanDispatch());
    }

    [Test]
    public void FoundingRepairCosts_AndLaborGatherReward_MatchEarlyTradeoffPlan()
    {
        MissionManager missions = Add<MissionManager>("Missions");
        MissionManager.Instance = missions;
        missions.LoadMissionsFromJson();

        AssertMissionMaterialCost(missions, "founding_repair_spirit_array", 2);
        AssertMissionMaterialCost(missions, "founding_repair_protection_array", 2);
        AssertMissionMaterialCost(missions, "founding_repair_inheritance_chamber", 3);
        AssertMissionMaterialCost(missions, "founding_repair_storage_chamber", 2);

        MissionData gather = missions.GetMissionData("founding_labor_gather");
        Assert.NotNull(gather);
        Assert.AreEqual(10, gather.laborCost);
        Assert.AreEqual(2, gather.itemRewards.Single(item => item.itemId == FacilityRules.BasicMaterialId).count);
    }

    [Test]
    public void IdleCoreFounder_AddsUnderstandingFromComprehension()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        NPCManager npcs = Add<NPCManager>("NPCs");
        player.InitializeNewFoundingGame(93);
        List<FounderCandidateData> selected = player.playerData.founding.candidates.Take(3).ToList();
        Assert.IsTrue(npcs.CreateFounders(selected));
        player.playerData.founding.selectedFounderIds = selected.Select(item => item.candidateId).ToList();
        player.playerData.founding.selectedTechniqueId = "qingmu";
        player.playerData.founding.stage = FoundingStage.Cave;
        NPCRuntime actor = npcs.GetRuntime(selected[0].candidateId);
        player.ProcessIdleFounderDay(actor);
        Assert.AreEqual(1 + selected[0].comprehension / 10, player.playerData.founding.techniqueUnderstanding);
        player.SetFacilityLevelForStory(FacilityType.InheritanceChamber, 1);
        player.ProcessIdleFounderDay(actor);
        Assert.AreEqual((1 + selected[0].comprehension / 10) * 2 + 1, player.playerData.founding.techniqueUnderstanding);
    }

    private static void AssertMissionMaterialCost(MissionManager missions, string missionId, int expected)
    {
        MissionData data = missions.GetMissionData(missionId);
        Assert.NotNull(data, missionId);
        Assert.AreEqual(expected,
            data.itemCosts.Where(item => item.itemId == FacilityRules.BasicMaterialId).Sum(item => item.count),
            missionId);
    }

    [Test]
    public void VillageSupport_ReservesAndReturnsLabor()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        player.InitializeNewFoundingGame(94);
        player.playerData.founding.stage = FoundingStage.Cave;
        player.AddVillageRelation(50);
        Assert.AreEqual(50, player.playerData.founding.village.totalLabor);
        Assert.IsTrue(player.TryReserveLabor(50, out string reason), reason);
        Assert.AreEqual(0, player.playerData.founding.village.totalLabor - player.playerData.founding.village.reservedLabor);
        player.ReleaseLabor(50);
        Assert.AreEqual(50, player.playerData.founding.village.totalLabor - player.playerData.founding.village.reservedLabor);
    }

    [Test]
    public void FoundingCompletesOnlyAfterRepairUnderstandingAndRouteFacility()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        player.InitializeNewFoundingGame(95);
        FoundingState state = player.playerData.founding;
        state.stage = FoundingStage.Cave;
        state.selectedTechniqueId = "qingmu";
        state.techniqueUnderstanding = 100;
        player.SetFacilityLevelForStory(FacilityType.AlchemyRoom, 1);
        Assert.IsFalse(state.completed);
        player.SetFacilityLevelForStory(FacilityType.TrainingRoom, 1);
        Assert.IsTrue(state.completed);
        Assert.AreEqual(FoundingStage.Completed, state.stage);
    }

    [Test]
    public void FoundingCatalog_ReferencesExistingMissionsAndEvents()
    {
        MissionManager missions = Add<MissionManager>("Missions");
        EventManager events = Add<EventManager>("Events");
        missions.LoadMissionsFromJson();
        events.LoadDefinitions();
        Assert.AreEqual(3, FoundingRules.Catalog.techniques.Count);
        foreach (FoundingTechniqueDefinition technique in FoundingRules.Catalog.techniques)
        {
            Assert.IsNotNull(missions.GetMissionData(technique.buildMissionId), technique.buildMissionId);
            Assert.IsNotNull(missions.GetMissionData(technique.actionMissionId), technique.actionMissionId);
            Assert.IsTrue(events.GetDefinitions().Any(item => item.id == technique.milestoneEventId), technique.milestoneEventId);
        }
    }

    [Test]
    public void FailedRouteConstruction_ReturnsReservedLabor()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        NPCManager npcs = Add<NPCManager>("NPCs");
        MissionManager missions = Add<MissionManager>("Missions");
        PlayerManager.Instance = player;
        WarehouseManager.Instance = warehouse;
        NPCManager.Instance = npcs;
        MissionManager.Instance = missions;
        player.InitializeNewFoundingGame(96);
        List<FounderCandidateData> selected = player.playerData.founding.candidates.Take(3).ToList();
        Assert.IsTrue(npcs.CreateFounders(selected));
        FoundingState state = player.playerData.founding;
        state.selectedFounderIds = selected.Select(item => item.candidateId).ToList();
        state.selectedTechniqueId = "qingmu";
        state.stage = FoundingStage.Cave;
        state.techniqueUnderstanding = 100;
        player.SetFacilityLevelForStory(FacilityType.TrainingRoom, 1);
        player.AddVillageRelation(50);
        missions.LoadMissionsFromJson();
        NPCRuntime actor = npcs.GetAllNPC()[0];

        Assert.IsTrue(missions.CanTriggerMission("founding_build_alchemy", actor, out string reason), reason);
        missions.TriggerMission("founding_build_alchemy", actor);
        Assert.AreEqual(50, state.village.reservedLabor);
        missions.GetActiveMissions().Single().FailMission(false);
        Assert.AreEqual(0, state.village.reservedLabor);
        Assert.AreEqual(0, player.GetFacilityLevel(FacilityType.AlchemyRoom));
    }

    [Test]
    public void LaborMission_StartsWithoutNpcAndReturnsReservedLabor()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        RewardManager rewards = Add<RewardManager>("Rewards");
        MissionManager missions = Add<MissionManager>("Missions");
        PlayerManager.Instance = player;
        WarehouseManager.Instance = warehouse;
        RewardManager.Instance = rewards;
        MissionManager.Instance = missions;
        player.InitializeNewFoundingGame(97);
        player.playerData.founding.stage = FoundingStage.Cave;
        player.AddVillageRelation(50);
        missions.LoadMissionsFromJson();

        Assert.IsTrue(missions.CanTriggerLaborMission("founding_labor_gather", out string reason), reason);
        missions.TriggerLaborMission("founding_labor_gather");
        Mission mission = missions.GetActiveMissions().Single();
        Assert.IsNull(mission.AssignedNPC);
        Assert.AreEqual(10, player.playerData.founding.village.reservedLabor);

        mission.PassOneDay();
        mission.PassOneDay();
        mission.PassOneDay();

        Assert.AreEqual(0, player.playerData.founding.village.reservedLabor);
        Assert.AreEqual(7, warehouse.GetItemCount(FacilityRules.BasicMaterialId));
        Assert.AreEqual(0, missions.GetActiveMissions().Count);
    }

    [Test]
    public void GameFlowPermission_SectEstablishedOnlyDependsOnRealState()
    {
        FoundingState founded = new FoundingState { sectCreated = true, stage = FoundingStage.CandidateSelection, completed = false };
        FoundingState developing = new FoundingState { sectCreated = true, stage = FoundingStage.Cave, completed = false };
        FoundingState developed = new FoundingState { sectCreated = true, stage = FoundingStage.Completed, completed = true };
        FoundingState beforeFounding = new FoundingState { sectCreated = false, stage = FoundingStage.CandidateSelection, completed = false };

        Assert.IsTrue(GameFlowPermission.IsSectEstablished(founded),
            "sectCreated 是真实状态，不要求 stage 或 completed");
        Assert.IsTrue(GameFlowPermission.IsSectEstablished(developing));
        Assert.IsTrue(GameFlowPermission.IsSectEstablished(developed));
        Assert.IsFalse(GameFlowPermission.IsSectEstablished(beforeFounding));
        Assert.IsFalse(GameFlowPermission.IsFoundingDevelopmentComplete(developing));
        Assert.IsTrue(GameFlowPermission.IsFoundingDevelopmentComplete(developed));
        Assert.IsFalse(GameFlowPermission.HasReachedCave(founded));
        Assert.IsTrue(GameFlowPermission.HasReachedCave(developing));
    }

    [Test]
    public void OrdinaryMissionVisibility_RequiresSectCreatedInsteadOfDevelopmentComplete()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        MissionManager manager = Add<MissionManager>("Missions");
        PlayerManager.Instance = player;
        MissionManager.Instance = manager;
        player.playerData.founding = new FoundingState
        {
            initialized = true,
            sectCreated = false,
            completed = false,
            stage = FoundingStage.Cave,
            village = new VillageState(),
            externalThreat = new ActiveThreatState()
        };
        MissionData ordinary = new MissionData { id = "perm-ordinary", missionRank = 1, requiredFacilityLevel = 0 };

        Assert.IsFalse(manager.IsMissionVisible(ordinary), "发展未完成前普通任务仍应按宗门成立状态解锁");
        player.playerData.founding.sectCreated = true;
        Assert.IsTrue(manager.IsMissionVisible(ordinary), "选址完成即宗门成立，普通任务应立即可见");
        Assert.IsFalse(player.playerData.founding.completed);
    }

    private T Add<T>(string name) where T : Component
    {
        GameObject obj = new GameObject(name);
        objects.Add(obj);
        return obj.AddComponent<T>();
    }
}
