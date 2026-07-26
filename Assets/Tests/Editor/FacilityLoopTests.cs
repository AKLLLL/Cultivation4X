using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

public class FacilityLoopTests
{
    private readonly List<Object> objects = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        PlayerManager.Instance = null;
        WarehouseManager.Instance = null;
        MissionManager.Instance = null;
        NPCManager.Instance = null;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in objects) Object.DestroyImmediate(item);
        objects.Clear();
        PlayerManager.Instance = null;
        WarehouseManager.Instance = null;
        MissionManager.Instance = null;
        NPCManager.Instance = null;
    }

    [Test]
    public void NewGame_HasStarterResourcesAndLevelOneFacilities()
    {
        PlayerData player = new PlayerData();
        WarehouseData warehouse = new WarehouseData();
        Assert.AreEqual(100, player.gold);
        Assert.AreEqual(5, warehouse.items.Single(item => item.itemId == FacilityRules.BasicMaterialId).count);
        Assert.AreEqual(1, player.warehouseLevel);
        Assert.AreEqual(1, player.secretRealmLevel);
        Assert.AreEqual(1, player.alchemyRoomLevel);
    }

    [Test]
    public void FacilityUpgrade_IsAtomicAndUsesSharedResources()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        PlayerManager.Instance = player;
        WarehouseManager.Instance = warehouse;
        FacilityUpgradeResult result = player.TryUpgradeFacility(FacilityType.Warehouse);
        Assert.IsTrue(result.success, $"{result.reason}; gold={player.playerData.gold}; material={warehouse.GetItemCount(FacilityRules.BasicMaterialId)}; level={player.playerData.warehouseLevel}");
        Assert.AreEqual(2, player.playerData.warehouseLevel);
        Assert.AreEqual(0, player.playerData.gold);
        Assert.AreEqual(0, warehouse.GetItemCount(FacilityRules.BasicMaterialId));
        FacilityUpgradeResult failed = player.TryUpgradeFacility(FacilityType.Warehouse);
        Assert.IsFalse(failed.success);
        Assert.AreEqual(2, player.playerData.warehouseLevel);
        Assert.AreEqual(0, warehouse.GetItemCount(FacilityRules.BasicMaterialId));
    }

    [Test]
    public void Warehouse_NewKindsRespectFacilitySlots()
    {
        Add<PlayerManager>("Player");
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        WarehouseManager.Instance = warehouse;
        for (int i = 0; i < 9; i++) Assert.IsTrue(warehouse.TryAddItem("test_" + i, 1));
        Assert.IsFalse(warehouse.TryAddItem("overflow", 1));
        Assert.IsTrue(warehouse.TryAddItem("test_0", 1));
    }

    [Test]
    public void V1Save_MigratesAdditiveFacilityDefaults()
    {
        GameState state = JsonConvert.DeserializeObject<GameState>("{\"version\":1,\"sect\":{\"gold\":7,\"missionHallLevel\":1},\"warehouse\":{\"items\":[]}}");
        SaveManager.MigrateState(state);
        Assert.AreEqual(SaveDataVersion.Current, state.version);
        Assert.AreEqual(7, state.sect.gold);
        Assert.AreEqual(1, state.sect.warehouseLevel);
        Assert.AreEqual(1, state.sect.secretRealmLevel);
        Assert.IsNotNull(state.dailyMissionCandidateIds);
    }

    [Test]
    public void MissionCandidates_AreDeterministicAndExcludeFacilityActions()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding.completed = true;
        player.playerData.founding.stage = FoundingStage.Completed;
        MissionManager manager = Add<MissionManager>("Missions");
        manager.LoadMissionsFromJson();
        manager.RefreshDailyCandidates(12, true);
        string[] first = manager.GetDailyMissionCandidateIds().ToArray();
        manager.RefreshDailyCandidates(12, true);
        CollectionAssert.AreEqual(first, manager.GetDailyMissionCandidateIds());
        Assert.Greater(first.Length, 0);
        Assert.IsFalse(first.Any(id => manager.GetMissionData(id).isFacilityAction));
    }

    [Test]
    public void FacilityActions_UseLevelSpecificDurationAndOutput()
    {
        Assert.AreEqual(5, FacilityRules.SecretRealmDays(1));
        Assert.AreEqual(8, FacilityRules.SecretRealmMaterialReward(3));
        Assert.AreEqual(2, FacilityRules.AlchemyDays(3));
        Assert.AreEqual(2, FacilityRules.AlchemyPillReward(3));
    }

    [Test]
    public void DuplicateMissionCosts_AreAggregatedBeforeDispatch()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding.completed = true;
        player.playerData.founding.stage = FoundingStage.Completed;
        Add<WarehouseManager>("Warehouse");
        NPCManager npcs = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcs;
        MissionManager manager = Add<MissionManager>("Missions");
        manager.LoadMissionsFromJson();
        manager.RefreshDailyCandidates(1, true);
        MissionData data = manager.GetMissionData(manager.GetDailyMissionCandidateIds().First());
        data.itemCosts = new List<ItemReward>
        {
            new ItemReward { itemId = FacilityRules.BasicMaterialId, count = 6 },
            new ItemReward { itemId = FacilityRules.BasicMaterialId, count = 6 }
        };
        Assert.IsFalse(manager.CanTriggerMission(data.id, CreateRuntime(npcs, "cost-actor", false), out _));
    }

    [Test]
    public void LegacyMissionTemplatesLoadAndInvalidRestoreClearsBusyNpc()
    {
        NPCManager npcs = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcs;
        MissionManager manager = Add<MissionManager>("Missions");
        manager.LoadMissionsFromJson();
        Assert.AreEqual(MissionType.WorldEvent, manager.GetMissionData("merchant_001").missionType);

        NPCRuntime npc = CreateRuntime(npcs, "restore-actor", true);
        npc.SetState(NPCState.Busy);
        manager.RestoreMissions(new[] { new MissionSaveData { missionId = "missing", assignedCharacterId = npc.CharacterId } });
        Assert.AreEqual(NPCState.Idle, npc.State);
        Assert.IsNull(npc.CurrentMission);
    }

    private T Add<T>(string name) where T : Component
    {
        GameObject item = new GameObject(name);
        objects.Add(item);
        return item.AddComponent<T>();
    }

    private NPCRuntime CreateRuntime(NPCManager manager, string id, bool inject)
    {
        NPCData template = ScriptableObject.CreateInstance<NPCData>();
        objects.Add(template);
        template.npcID = id;
        template.npcName = id;
        NPCRuntime runtime = new NPCRuntime(template);
        if (!inject) return runtime;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        ((Dictionary<string, NPCRuntime>)typeof(NPCManager).GetField("npcById", flags).GetValue(manager))[id] = runtime;
        ((List<NPCRuntime>)typeof(NPCManager).GetField("runtimes", flags).GetValue(manager)).Add(runtime);
        return runtime;
    }
}
