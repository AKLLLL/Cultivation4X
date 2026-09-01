using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cultivation4X.WorldMap;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

public class SectFunctionalZoneTests
{
    private readonly List<Object> objects = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        PlayerManager.Instance = null;
        NPCManager.Instance = null;
        WarehouseManager.Instance = null;
        TimeManager.Instance = null;
        DiscipleAIConfigLoader.ResetForTests();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in objects) if (item != null) Object.DestroyImmediate(item);
        objects.Clear();
        PlayerManager.Instance = null;
        NPCManager.Instance = null;
        WarehouseManager.Instance = null;
        TimeManager.Instance = null;
        DiscipleAIConfigLoader.ResetForTests();
    }

    [Test]
    public void PlanRequiresExplorationInfluenceAndEmptyBuildableCell()
    {
        WorldMap map = CreateMap(1);
        WorldMapProgressState progress = CreateControlledProgress(0);

        progress.exploredCellIndices.Clear();
        Assert.IsFalse(SectFunctionalZoneRules.CanPlan(map, progress, 0, out _));
        progress.exploredCellIndices.Add(0);
        progress.cellInfluences[0].level = InfluenceLevel.Outer;
        Assert.IsFalse(SectFunctionalZoneRules.CanPlan(map, progress, 0, out _));
        progress.cellInfluences[0].level = InfluenceLevel.Influence;
        Assert.IsTrue(SectFunctionalZoneRules.CanPlan(map, progress, 0, out string reason), reason);

        progress.mapSites.Add(new MapSiteData { siteId = "occupied", cellIndex = 0 });
        Assert.IsFalse(SectFunctionalZoneRules.CanPlan(map, progress, 0, out _));
        progress.mapSites.Clear();
        progress.resourceNodes.Add(new ResourceNodeRuntime { nodeId = "resource", cellIndex = 0 });
        Assert.IsFalse(SectFunctionalZoneRules.CanPlan(map, progress, 0, out _));
        progress.resourceNodes.Clear();
        map.cells[0].isBuildable = false;
        Assert.IsFalse(SectFunctionalZoneRules.CanPlan(map, progress, 0, out _));
        map.cells[0].isBuildable = true;
        map.cells[0].landform = LandformType.ShallowWater;
        Assert.IsFalse(SectFunctionalZoneRules.CanPlan(map, progress, 0, out _));
        map.cells[0].landform = LandformType.Plain;
        progress.cellInfluences[0].controllerSectId = "enemy_sect";
        Assert.IsFalse(SectFunctionalZoneRules.CanPlan(map, progress, 0, out _));
    }

    [Test]
    public void PlanAndCancelUseStableIdAndPermanentlyClearState()
    {
        WorldMap map = CreateMap(1);
        WorldMapProgressState progress = CreateControlledProgress(0);
        Assert.IsTrue(SectFunctionalZoneRules.TryPlan(map, progress, 0,
            out SectFunctionalZoneState zone, out string reason), reason);
        Assert.AreEqual("sect_zone_cell_0", zone.zoneId);
        zone.stage = FunctionalZoneStage.Operational;
        zone.harvestProgress = 3f;
        zone.assignedDepartmentId = "department_0001";

        Assert.IsTrue(SectFunctionalZoneRules.TryCancel(progress, 0, out reason), reason);
        Assert.IsEmpty(progress.functionalZones);
    }

    [Test]
    public void DepartmentMembershipLeaderBindingAndDeletionFollowSingleOwnershipRules()
    {
        PlayerData player = new PlayerData { departments = new List<SectDepartmentState>() };
        WorldMapProgressState progress = new WorldMapProgressState
        {
            functionalZones = new List<SectFunctionalZoneState>
            {
                new SectFunctionalZoneState { zoneId = "sect_zone_cell_0", cellIndex = 0 }
            }
        };
        Assert.IsTrue(SectOrganizationRules.TryCreate(player, " 百草堂 ",
            out SectDepartmentState herbs, out string reason), reason);
        Assert.AreEqual("department_0001", herbs.departmentId);
        Assert.AreEqual("百草堂", herbs.name);
        Assert.IsTrue(SectOrganizationRules.TryCreate(player, "灵田司",
            out SectDepartmentState fields, out reason), reason);
        Assert.IsTrue(SectOrganizationRules.TrySetMembers(player, herbs.departmentId,
            new[] { "disciple_a" }, new[] { "disciple_a", "disciple_b" }, out reason), reason);
        Assert.IsFalse(SectOrganizationRules.TrySetMembers(player, fields.departmentId,
            new[] { "disciple_a" }, new[] { "disciple_a", "disciple_b" }, out _));
        Assert.IsFalse(SectOrganizationRules.TrySetLeader(player, herbs.departmentId,
            "disciple_b", out _));
        Assert.IsTrue(SectOrganizationRules.TrySetLeader(player, herbs.departmentId,
            "disciple_a", out reason), reason);
        Assert.IsTrue(SectOrganizationRules.TryAssignZone(player, progress,
            herbs.departmentId, "sect_zone_cell_0", out reason), reason);
        Assert.IsFalse(SectOrganizationRules.TryAssignZone(player, progress,
            fields.departmentId, "sect_zone_cell_0", out _));

        Assert.IsTrue(SectOrganizationRules.TryDelete(player, progress,
            herbs.departmentId, out reason), reason);
        Assert.IsNull(progress.functionalZones[0].assignedDepartmentId);
        Assert.IsNull(SectOrganizationRules.DepartmentFor(player, "disciple_a"));
    }

    [Test]
    public void KillingDepartmentLeaderCleansMembershipBeforeImmediateSavePoint()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        NPCManager npcs = Add<NPCManager>("NPCs");
        PlayerManager.Instance = player;
        NPCManager.Instance = npcs;
        npcs.RestoreCharacters(new[]
        {
            GeneratedCharacter("disciple_a"),
            GeneratedCharacter("disciple_b")
        });
        objects.AddRange(npcs.GetAllNPC().Select(item => item.Data).Where(item => item != null));
        SectDepartmentState department = new SectDepartmentState
        {
            departmentId = "department_0001",
            name = "百草堂",
            leaderDiscipleId = "disciple_a",
            memberDiscipleIds = new List<string> { "disciple_a", "disciple_b" }
        };
        player.playerData.departments = new List<SectDepartmentState> { department };

        Assert.IsTrue(npcs.Kill(npcs.GetLivingNPC().Single(item =>
            item.CharacterId == "disciple_a"), "测试死亡", 3));

        CollectionAssert.AreEqual(new[] { "disciple_b" }, department.memberDiscipleIds);
        Assert.IsNull(department.leaderDiscipleId);
    }

    [Test]
    public void ResolverPrefersOwnDepartmentZoneButFallsBackToMaintenanceWithoutDemand()
    {
        WorldMap map = CreateMap(2);
        WorldMapProgressState progress = CreateControlledProgress(0, 1);
        progress.functionalZones.Add(new SectFunctionalZoneState
        {
            zoneId = SectFunctionalZoneRules.ZoneId(0), cellIndex = 0,
            stage = FunctionalZoneStage.Planned, assignedDepartmentId = "department_0001"
        });
        progress.functionalZones.Add(new SectFunctionalZoneState
        {
            zoneId = SectFunctionalZoneRules.ZoneId(1), cellIndex = 1,
            stage = FunctionalZoneStage.Planned
        });
        PlayerData player = new PlayerData
        {
            departments = new List<SectDepartmentState>
            {
                new SectDepartmentState
                {
                    departmentId = "department_0001", name = "百草堂",
                    memberDiscipleIds = new List<string> { "disciple_a" }
                }
            }
        };
        NPCRuntime npc = CreateNpc("disciple_a");
        DiscipleAIConfig config = DiscipleAIConfigLoader.Load();
        IdentityDefinition identity = config.GetIdentity("inner_disciple");

        SectDutyDecision decision = SectDutyResolver.Resolve(npc, 1, config, identity,
            player, map, progress);
        Assert.NotNull(decision);
        Assert.AreEqual(0, decision.Zone.cellIndex);
        Assert.AreEqual("department_0001", decision.Department.departmentId);

        progress.functionalZones.Clear();
        decision = SectDutyResolver.Resolve(npc, 1, config, identity, player, map, progress);
        Assert.NotNull(decision);
        Assert.AreEqual(SectDutyEffectIds.GeneralMaintenance, decision.Action.sectDutyEffectId);
        Assert.IsNull(decision.Zone);
    }

    [Test]
    public void EnvironmentMultipliersMatchTheV1Table()
    {
        WorldCell cell = CreateMap(1).cells[0];
        cell.biome = BiomeType.TemperateForest;
        cell.moisture = 0.6f;
        Assert.AreEqual(1.2f, SectFunctionalZoneRules.SuitabilityMultiplier(cell));
        cell.biome = BiomeType.Desert;
        Assert.AreEqual(0.8f, SectFunctionalZoneRules.SuitabilityMultiplier(cell));
        cell.biome = BiomeType.Grassland;
        cell.moisture = 0.4f;
        Assert.AreEqual(1f, SectFunctionalZoneRules.SuitabilityMultiplier(cell));
    }

    [Test]
    public void LaterDiscipleCanChooseTheNextStageAfterEarlierDiscipleCompletesOpening()
    {
        WorldMap map = CreateMap(1);
        WorldMapProgressState progress = CreateControlledProgress(0);
        SectFunctionalZoneState zone = new SectFunctionalZoneState
        {
            zoneId = SectFunctionalZoneRules.ZoneId(0), cellIndex = 0,
            stage = FunctionalZoneStage.Planned,
            phaseProgress = SectFunctionalZoneRules.PlannedThreshold - 1f
        };
        progress.functionalZones.Add(zone);
        DiscipleAIConfig config = DiscipleAIConfigLoader.Load();
        IdentityDefinition identity = config.GetIdentity("inner_disciple");

        SectDutyDecision first = SectDutyResolver.Resolve(CreateNpc("disciple_a"), 1,
            config, identity, new PlayerData(), map, progress);
        Assert.AreEqual(SectDutyEffectIds.OpenHerbLand, first.Action.sectDutyEffectId);
        Assert.IsTrue(SectDutyExecutor.Execute(first, map, progress).executed);
        Assert.AreEqual(FunctionalZoneStage.Developing, zone.stage);

        SectDutyDecision second = SectDutyResolver.Resolve(CreateNpc("disciple_b"), 1,
            config, identity, new PlayerData(), map, progress);
        Assert.AreEqual(SectDutyEffectIds.PlantHerbs, second.Action.sectDutyEffectId);
    }

    [Test]
    public void MaintenanceProducesNoLegacyMaterialsAndFullWarehouseLosesHarvest()
    {
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        WarehouseManager.Instance = warehouse;
        int initialMaterials = warehouse.GetItemCount(FacilityRules.BasicMaterialId);
        SectDutyExecutionOutcome maintenance = SectDutyExecutor.Execute(new SectDutyDecision
        {
            Action = new ActionDefinition
            {
                id = "sect_general_maintenance", displayName = "日常宗门维护",
                executionKind = ActionExecutionKind.SectDuty,
                sectDutyEffectId = SectDutyEffectIds.GeneralMaintenance
            }
        }, null, null);
        Assert.IsTrue(maintenance.executed);
        Assert.AreEqual(initialMaterials, warehouse.GetItemCount(FacilityRules.BasicMaterialId));

        warehouse.warehouseData.items = Enumerable.Range(0, FacilityRules.WarehouseSlots)
            .Select(index => new ItemStack { itemId = $"occupied_{index}", count = 1 }).ToList();
        WorldMap map = CreateMap(1);
        WorldMapProgressState progress = CreateControlledProgress(0);
        SectFunctionalZoneState zone = new SectFunctionalZoneState
        {
            zoneId = SectFunctionalZoneRules.ZoneId(0), cellIndex = 0,
            stage = FunctionalZoneStage.Operational,
            harvestProgress = SectFunctionalZoneRules.HarvestReadyThreshold
        };
        progress.functionalZones.Add(zone);

        SectDutyExecutionOutcome harvested = SectDutyExecutor.Execute(Decision(zone,
            SectDutyEffectIds.HarvestHerbs, "sect_harvest_herbs"), map, progress);
        Assert.IsTrue(harvested.executed);
        Assert.AreEqual(0f, zone.harvestProgress, 0.001f);
        Assert.AreEqual(0, warehouse.GetItemCount(SectFunctionalZoneRules.HerbItemId));
    }

    [Test]
    public void ExecutorAdvancesStagesThenConsumesMaturityOnHarvest()
    {
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        WarehouseManager.Instance = warehouse;
        WorldMap map = CreateMap(1);
        WorldMapProgressState progress = CreateControlledProgress(0);
        SectFunctionalZoneState zone = new SectFunctionalZoneState
        {
            zoneId = SectFunctionalZoneRules.ZoneId(0), cellIndex = 0,
            stage = FunctionalZoneStage.Planned,
            phaseProgress = SectFunctionalZoneRules.PlannedThreshold - 1f
        };
        progress.functionalZones.Add(zone);

        SectDutyExecutionOutcome opened = SectDutyExecutor.Execute(Decision(zone,
            SectDutyEffectIds.OpenHerbLand, "sect_open_herb_land"), map, progress);
        Assert.IsTrue(opened.executed);
        Assert.AreEqual(FunctionalZoneStage.Developing, zone.stage);

        zone.phaseProgress = SectFunctionalZoneRules.DevelopingThreshold - 1f;
        SectDutyExecutor.Execute(Decision(zone, SectDutyEffectIds.PlantHerbs,
            "sect_plant_herbs"), map, progress);
        Assert.AreEqual(FunctionalZoneStage.Operational, zone.stage);

        zone.harvestProgress = SectFunctionalZoneRules.HarvestReadyThreshold;
        SectDutyExecutionOutcome harvested = SectDutyExecutor.Execute(Decision(zone,
            SectDutyEffectIds.HarvestHerbs, "sect_harvest_herbs"), map, progress);
        Assert.IsTrue(harvested.executed);
        Assert.AreEqual(0f, zone.harvestProgress, 0.001f);
        Assert.AreEqual(1, warehouse.GetItemCount(SectFunctionalZoneRules.HerbItemId));
    }

    [Test]
    public void V25StateRoundTripPreservesFacilitiesDepartmentsAndZonesAndRejectsV24()
    {
        GameState state = new GameState
        {
            version = SaveDataVersion.Current,
            sect = new PlayerData
            {
                availableFacilities = new List<FacilityType> { FacilityType.MissionHall },
                departments = new List<SectDepartmentState>
                {
                    new SectDepartmentState { departmentId = "department_0001", name = "百草堂" }
                }
            },
            worldMapProgress = new WorldMapProgressState
            {
                functionalZones = new List<SectFunctionalZoneState>
                {
                    new SectFunctionalZoneState { zoneId = "sect_zone_cell_3", cellIndex = 3,
                        stage = FunctionalZoneStage.Developing, phaseProgress = 4f,
                        assignedDepartmentId = "department_0001" }
                }
            }
        };
        GameState restored = JsonConvert.DeserializeObject<GameState>(JsonConvert.SerializeObject(state));
        CollectionAssert.AreEqual(state.sect.availableFacilities, restored.sect.availableFacilities);
        Assert.AreEqual("百草堂", restored.sect.departments.Single().name);
        Assert.AreEqual(4f, restored.worldMapProgress.functionalZones.Single().phaseProgress);

        MethodInfo deserialize = typeof(SaveManager).GetMethod("DeserializeCurrentVersion",
            BindingFlags.Static | BindingFlags.NonPublic);
        TargetInvocationException thrown = Assert.Throws<TargetInvocationException>(() =>
            deserialize.Invoke(null, new object[] { "{\"version\":24}" }));
        Assert.IsInstanceOf<SaveVersionMismatchException>(thrown.InnerException);
    }

    [Test]
    public void NinetyDayDutySettlementIsIdenticalAtSpeedOneAndSpeedFour()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        NPCManager npcs = Add<NPCManager>("NPCs");
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        TimeManager time = Add<TimeManager>("Time");
        PlayerManager.Instance = player;
        NPCManager.Instance = npcs;
        WarehouseManager.Instance = warehouse;
        TimeManager.Instance = time;

        string speedOne = RunNinetyDayDutyScenario(player, npcs, warehouse, time, 1f);
        string speedFour = RunNinetyDayDutyScenario(player, npcs, warehouse, time, 4f);

        Assert.AreEqual(speedOne, speedFour);
    }

    private WorldMap CreateMap(int count)
    {
        WorldMap map = new WorldMap { width = count, height = 1, cells = new WorldCell[count] };
        for (int index = 0; index < count; index++)
        {
            map.cells[index] = new WorldCell
            {
                index = index, coord = new HexCoord(index, 0), isBuildable = true,
                landform = LandformType.Plain, biome = BiomeType.TemperateForest,
                moisture = 0.4f
            };
        }
        return map;
    }

    private static WorldMapProgressState CreateControlledProgress(params int[] cells)
    {
        WorldMapProgressState progress = new WorldMapProgressState { isInfluenceDirty = false };
        foreach (int cell in cells)
        {
            progress.exploredCellIndices.Add(cell);
            progress.cellInfluences.Add(new CellInfluenceState
            {
                cellIndex = cell, value = 60, level = InfluenceLevel.Influence,
                controllerSectId = WorldMapProgressRules.PlayerSectOwnerId,
                sourceIds = new List<string> { "source" }
            });
        }
        return progress;
    }

    private NPCRuntime CreateNpc(string id)
    {
        NPCData data = ScriptableObject.CreateInstance<NPCData>();
        objects.Add(data);
        data.npcID = id;
        data.npcName = id;
        return new NPCRuntime(data, new CharacterState
        {
            characterId = id, displayName = id, realm = CultivationRealm.QiRefining
        });
    }

    private static CharacterState GeneratedCharacter(string id) => new CharacterState
    {
        characterId = id,
        displayName = id,
        hasGeneratedProfile = true,
        health = HealthState.Healthy,
        realm = CultivationRealm.QiRefining,
        realmLayer = 1,
        spiritRoot = new SpiritRootData()
    };

    private string RunNinetyDayDutyScenario(PlayerManager player, NPCManager npcs,
        WarehouseManager warehouse, TimeManager time, float speed)
    {
        const string discipleId = "duty_disciple";
        player.playerData = new PlayerData
        {
            availableFacilities = new List<FacilityType> { FacilityType.MissionHall },
            monthlyPlanTemplates = new List<MonthlyPlanTemplate>
            {
                new MonthlyPlanTemplate
                {
                    id = "all_duty", name = "全宗务",
                    days = Enumerable.Repeat(MonthlyActivityType.SectDuty,
                        MonthlyPlanRules.DaysPerMonth).ToList(),
                    discipleIds = new List<string> { discipleId }
                }
            },
            founding = new FoundingState
            {
                initialized = true, sectCreated = true, completed = true,
                stage = FoundingStage.Completed,
                village = new VillageState(), externalThreat = new ActiveThreatState()
            }
        };
        WorldMap map = CreateMap(1);
        WorldMapProgressState progress = CreateControlledProgress(0);
        progress.functionalZones.Add(new SectFunctionalZoneState
        {
            zoneId = SectFunctionalZoneRules.ZoneId(0), cellIndex = 0,
            stage = FunctionalZoneStage.Planned
        });
        WorldMapSession.Set(map, progress);
        warehouse.warehouseData = new WarehouseData { items = new List<ItemStack>() };
        npcs.RestoreCharacters(new[] { GeneratedCharacter(discipleId) });
        foreach (NPCData data in npcs.GetAllNPC().Select(item => item.Data).Where(item => item != null))
            if (!objects.Contains(data)) objects.Add(data);
        time.ResetForNewGame();
        time.RestoreWorldTime(new WorldTimeSaveData
        {
            currentHour = 6f,
            selectedSpeed = speed
        });

        Assert.AreEqual(90, time.AdvanceDaysForTesting(90));
        return JsonConvert.SerializeObject(new
        {
            zones = progress.functionalZones,
            herbCount = warehouse.GetItemCount(SectFunctionalZoneRules.HerbItemId),
            feedback = player.playerData.growthFeedback
        });
    }

    private static SectDutyDecision Decision(SectFunctionalZoneState zone,
        string effectId, string actionId)
    {
        return new SectDutyDecision
        {
            Zone = zone,
            Action = new ActionDefinition
            {
                id = actionId, displayName = actionId,
                executionKind = ActionExecutionKind.SectDuty,
                sectDutyEffectId = effectId
            }
        };
    }

    private T Add<T>(string name) where T : Component
    {
        GameObject gameObject = new GameObject(name);
        objects.Add(gameObject);
        return gameObject.AddComponent<T>();
    }
}
