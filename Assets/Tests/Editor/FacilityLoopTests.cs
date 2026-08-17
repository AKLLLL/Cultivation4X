using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;
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
    public void WarehouseJsonRoundTrip_ReplacesStarterItemsInsteadOfAddingThem()
    {
        GameState state = new GameState
        {
            warehouse = new WarehouseData
            {
                items = new List<ItemStack>
                {
                    new ItemStack { itemId = FacilityRules.BasicMaterialId, count = 12 },
                    new ItemStack { itemId = "roundtrip_item", count = 3 }
                }
            }
        };

        string json = JsonConvert.SerializeObject(state);
        GameState first = JsonConvert.DeserializeObject<GameState>(json);
        GameState second = JsonConvert.DeserializeObject<GameState>(JsonConvert.SerializeObject(first));

        Assert.AreEqual(12, first.warehouse.items.Single(item => item.itemId == FacilityRules.BasicMaterialId).count);
        Assert.AreEqual(2, first.warehouse.items.Count);
        Assert.AreEqual(12, second.warehouse.items.Single(item => item.itemId == FacilityRules.BasicMaterialId).count);
        Assert.AreEqual(2, second.warehouse.items.Count);
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
    public void Warehouse_CapacityStaysStableWhenLastStackConsumed()
    {
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        WarehouseManager.Instance = warehouse;

        int capacity = warehouse.GetCapacity();
        Assert.AreEqual(10, capacity);
        Assert.AreEqual(1, warehouse.GetUsedSlotCount());
        Assert.IsTrue(warehouse.RemoveItem(FacilityRules.BasicMaterialId, 5));
        Assert.AreEqual(0, warehouse.GetItemCount(FacilityRules.BasicMaterialId));
        Assert.AreEqual(0, warehouse.GetUsedSlotCount());
        Assert.AreEqual(capacity, warehouse.GetCapacity());
        Assert.AreEqual(capacity, warehouse.GetFreeSlotCount());
    }

    [Test]
    public void Warehouse_NewGameStartCapacityIsTenEvenWithLevelZeroWarehouse()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.warehouseLevel = 0;

        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        WarehouseManager.Instance = warehouse;

        Assert.AreEqual(10, warehouse.GetCapacity());
        Assert.AreEqual(1, warehouse.GetUsedSlotCount());
        Assert.IsTrue(warehouse.CanAddItem("new_kind", 1));
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

    [Test]
    public void NodeRemoveItemFailure_KeepsWaitingAndRecordsReason()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        WarehouseManager.Instance = Add<WarehouseManager>("Warehouse");
        NPCManager npcs = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcs;
        MissionManager manager = Add<MissionManager>("Missions");
        MissionManager.Instance = manager;
        manager.LoadMissionsFromJson();
        NPCRuntime npc = CreateRuntime(npcs, "node-actor", true);

        const string missingItem = "node_cost_item";
        MissionData data = new MissionData
        {
            id = "test_node_cost",
            name = "节点成本测试",
            missionType = MissionType.Sect,
            needDays = 3,
            itemRewards = new List<ItemReward>(),
            nodes = new List<MissionNodeData>
            {
                new MissionNodeData
                {
                    triggerType = "Day", triggerValue = 1, title = "节点", description = "消耗物品",
                    options = new List<MissionOptionData>
                    {
                        new MissionOptionData
                        {
                            text = "消耗", requirementType = "None",
                            effects = new List<MissionEffectData>
                            {
                                new MissionEffectData { type = "RemoveItem", itemId = missingItem, count = 1 }
                            }
                        }
                    }
                }
            }
        };
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        ((Dictionary<string, MissionData>)typeof(MissionManager)
            .GetField("missionTemplates", flags).GetValue(manager))[data.id] = data;

        Mission mission = manager.CreateMission(data.id);
        mission.StartMission(npc);
        mission.PassOneDay(); // 触发节点 -> WaitingNode
        Assert.AreEqual(MissionState.WaitingNode, mission.State);
        Assert.IsFalse(npc.CanDispatch());

        mission.SelectOption(0);
        Assert.AreEqual(MissionState.WaitingNode, mission.State, "物品不足时任务必须保持等待，不能软锁");
        Assert.IsNotNull(mission.NodeFailureReason);
        StringAssert.Contains("材料不足", mission.NodeFailureReason);

        WarehouseManager.Instance.AddItem(missingItem, 1);
        mission.SelectOption(0);
        Assert.AreEqual(MissionState.Active, mission.State);
        Assert.IsNull(mission.NodeFailureReason);
    }

    [Test]
    public void Kill_CancelsAllAwaitingRewardMissionsForCharacter()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        WarehouseManager.Instance = Add<WarehouseManager>("Warehouse");
        NPCManager npcs = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcs;
        MissionManager manager = Add<MissionManager>("Missions");
        MissionManager.Instance = manager;
        manager.LoadMissionsFromJson();
        NPCRuntime target = CreateRuntime(npcs, "kill-actor", true);
        CreateRuntime(npcs, "other-actor", true);

        Mission ordinary = manager.CreateMission(manager.GetMissionPool()
            .First(item => !item.isFacilityAction && !item.isStoryAction &&
                           !item.generatedByMap && item.missionType != MissionType.WorldEvent).id);
        ordinary.StartMission(target);
        ordinary.WaitForReward();
        manager.AddActiveMission(ordinary);

        Mission map = manager.CreateMission(WorldMapContentRules.ExploreMissionId);
        map.ConfigureMapMission(new MapMissionContext { actionType = MapActionType.Explore, targetCellIndex = 0 },
            new Reward());
        map.StartMission(target);
        map.WaitForReward();
        manager.AddActiveMission(map);

        Assert.AreEqual(2, manager.GetActiveMissions().Count(item => item.State == MissionState.AwaitingReward));
        npcs.Kill(target, "test");
        Assert.IsFalse(target.Character.IsAlive);
        Assert.IsEmpty(manager.GetActiveMissions());
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
