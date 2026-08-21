using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;
using NUnit.Framework;
using UnityEngine;

public class ResourceEcologyTests
{
    [TearDown]
    public void TearDown()
    {
        WorldMapSession.Clear();
        ResourceDefinitionDatabase.ResetForTests();
        GameDebugConfig.BypassResourceDevelopmentInfluence = false;
        if (WarehouseManager.Instance != null) Object.DestroyImmediate(WarehouseManager.Instance.gameObject);
        WarehouseManager.Instance = null;
        if (ItemDatabase.Instance != null) Object.DestroyImmediate(ItemDatabase.Instance.gameObject);
        ItemDatabase.Instance = null;
    }

    [Test]
    public void QinglingOutput_WithoutVeinIsFive_AndGradeOneAlternatesSevenEight()
    {
        WorldMap map = OneCellMap();
        ResourceNodeRuntime node = new ResourceNodeRuntime
        {
            nodeId = "node", regionId = "forest", cellIndex = 0
        };
        ResourceNodeDefinition definition = new ResourceNodeDefinition
        {
            id = "herbs", resourceId = "herb_qingling", baseOutput = 5,
            biomeRequirements = new List<BiomeType> { BiomeType.TemperateForest },
            requiresVeinElement = true, requiredVeinElement = SpiritElement.Wood
        };
        WorldMapProgressState progress = new WorldMapProgressState();

        Assert.AreEqual(5, ResourceCalculator.Calculate(map, progress, node, definition, out float remainder));
        Assert.AreEqual(0f, remainder, 0.0001f);

        progress.spiritualVeins.Add(new SpiritualVeinRuntime
        {
            veinId = "wood", sourceVeinId = "source", definitionId = "natural_grade_1",
            regionId = "forest", element = SpiritElement.Wood, grade = 1
        });
        Assert.AreEqual(7, ResourceCalculator.Calculate(map, progress, node, definition, out remainder));
        node.productionRemainder = remainder;
        Assert.AreEqual(8, ResourceCalculator.Calculate(map, progress, node, definition, out remainder));
    }

    [Test]
    public void EnsureRuntime_DerivesEveryIntersectingVeinDeterministically()
    {
        WorldMap map = OneCellMap();
        map.spiritVeins.Add(new SpiritVein
        {
            id = "wood-medium", size = SpiritVeinSize.Medium, primaryElement = SpiritElement.Wood,
            pathCellIndices = new List<int> { 0 }
        });
        map.spiritVeins.Add(new SpiritVein
        {
            id = "fire-large", size = SpiritVeinSize.Large, primaryElement = SpiritElement.Fire,
            pathCellIndices = new List<int> { 0 }
        });
        WorldMapProgressState progress = new WorldMapProgressState();
        ResourceEcologyRules.EnsureRuntime(map, progress);

        Assert.AreEqual(2, progress.spiritualVeins.Count);
        Assert.AreEqual(1, progress.spiritualVeins.Single(item => item.sourceVeinId == "wood-medium").grade);
        Assert.AreEqual(2, progress.spiritualVeins.Single(item => item.sourceVeinId == "fire-large").grade);
    }

    [Test]
    public void MonthUpdate_SettlesOnceOnDayThirtyAndAgainOnDaySixty()
    {
        WarehouseManager warehouse = AddWarehouse();
        WorldMapProgressState progress = DevelopedQingmuProgress(lastUpdatedDay: 1);
        WorldMapSession.Set(OneCellMap(), progress);
        int before = warehouse.GetItemCount("herb_qingling");

        List<ResourceProductionRecord> first = ResourceManager.MonthUpdate(30, 1);
        List<ResourceProductionRecord> duplicate = ResourceManager.MonthUpdate(30, 1);
        List<ResourceProductionRecord> second = ResourceManager.MonthUpdate(60, 2);

        Assert.AreEqual(1, first.Count);
        Assert.AreEqual(5, first[0].received);
        Assert.IsEmpty(duplicate, "同一个月份不得重复结算");
        Assert.AreEqual(1, second.Count);
        Assert.AreEqual(before + 10, warehouse.GetItemCount("herb_qingling"));
    }

    [Test]
    public void MonthUpdate_DoesNotProduceForNodeDevelopedOnSettlementDay()
    {
        WarehouseManager warehouse = AddWarehouse();
        WorldMapProgressState progress = DevelopedQingmuProgress(lastUpdatedDay: 30);
        WorldMapSession.Set(OneCellMap(), progress);

        Assert.IsEmpty(ResourceManager.MonthUpdate(30, 1));
        Assert.AreEqual(0, warehouse.GetItemCount("herb_qingling"));
        Assert.AreEqual(1, ResourceManager.MonthUpdate(60, 2).Count);
        Assert.AreEqual(5, warehouse.GetItemCount("herb_qingling"));
    }

    [Test]
    public void MonthUpdate_RecordsLossWhenWarehouseHasNoSlotForNewItemType()
    {
        WarehouseManager warehouse = AddWarehouse();
        warehouse.warehouseData.items = Enumerable.Range(0, 10)
            .Select(index => new ItemStack { itemId = "occupied_" + index, count = 1 }).ToList();
        WorldMapProgressState progress = DevelopedQingmuProgress(lastUpdatedDay: 1);
        WorldMapSession.Set(OneCellMap(), progress);

        ResourceProductionRecord record = ResourceManager.MonthUpdate(30, 1).Single();

        Assert.AreEqual(5, record.calculated);
        Assert.AreEqual(0, record.received);
        Assert.AreEqual(5, record.lost);
        Assert.AreEqual(0, warehouse.GetItemCount("herb_qingling"));
        ResourceNodeRuntime node = progress.resourceNodes.Single();
        Assert.AreEqual(5, node.lastSettledLost, "损失应持久化到节点运行时，供资源面板读取");
        Assert.AreEqual(5, node.lastCalculatedOutput);
    }

    [Test]
    public void ResourceStatusRows_HideHiddenAndHintedAndShowOnlyDiscovered()
    {
        WorldMap map = OneCellMap();
        WorldMapProgressState progress = new WorldMapProgressState
        {
            mapSites = new List<MapSiteData>
            {
                Site(0, "hidden", MapContentRevealState.Hidden),
                Site(0, "hinted", MapContentRevealState.Hinted),
                Site(0, "discovered", MapContentRevealState.Discovered)
            },
            resourceNodes = new List<ResourceNodeRuntime>
            {
                Node("node-hidden", "hidden"),
                Node("node-hinted", "hinted"),
                Node("node-discovered", "discovered")
            }
        };
        foreach (ResourceNodeRuntime node in progress.resourceNodes)
        {
            node.definitionId = "qingmu_forest_herbs";
            node.cellIndex = 0;
            node.regionId = "forest";
        }

        List<ResourceStatusRow> rows = ResourceStatusService.BuildDiscoveredNodeRows(map, progress);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("discovered", rows[0].siteId);
        Assert.AreEqual("已发现（未开发）", rows[0].statusLabel);
        Assert.AreEqual(5, rows[0].baseOutput);
        Assert.AreEqual(0, rows[0].expectedOutput, "未开发节点不计预计产量");
    }

    [Test]
    public void ResourceStatusRows_DevelopedNodeShowsExpectedOutputWarehouseAndLoss()
    {
        WarehouseManager warehouse = AddWarehouse();
        warehouse.warehouseData.items = new List<ItemStack>
        {
            new ItemStack { itemId = "herb_qingling", count = 4 }
        };
        WorldMap map = OneCellMap();
        WorldMapProgressState progress = DevelopedQingmuProgress(lastUpdatedDay: 1);
        ResourceEcologyRules.EnsureRuntime(map, progress);
        ResourceNodeRuntime node = progress.resourceNodes.Single();
        node.lastSettledMonth = 2;
        node.lastSettledLost = 3;
        node.lastCalculatedOutput = 5;
        WorldMapSession.Set(map, progress);

        ResourceStatusRow row = ResourceStatusService.BuildDiscoveredNodeRows(map, progress).Single();

        Assert.AreEqual("已开发", row.statusLabel);
        Assert.AreEqual(5, row.expectedOutput);
        Assert.AreEqual(4, row.warehouseCount);
        Assert.AreEqual(2, row.lastSettledMonth);
        Assert.AreEqual(3, row.lastSettledLost);
    }

    [Test]
    public void NextSettlementEveDay_ComputesCorrectTargets()
    {
        Assert.AreEqual(29, ResourceStatusService.NextSettlementEveDay(7));
        Assert.AreEqual(29, ResourceStatusService.NextSettlementEveDay(0));
        Assert.AreEqual(59, ResourceStatusService.NextSettlementEveDay(29));
        Assert.AreEqual(59, ResourceStatusService.NextSettlementEveDay(30));
        Assert.AreEqual(89, ResourceStatusService.NextSettlementEveDay(59));
    }

    [Test]
    public void RemoteDevelopBypass_OnlyWaivesResourceDevelopmentInfluence()
    {
        WorldMap map = OneCellMap();
        WorldMapProgressState progress = new WorldMapProgressState
        {
            mapSites = new List<MapSiteData>
            {
                new MapSiteData
                {
                    siteId = "map_site_resource_node", cellIndex = 0,
                    siteType = MapSiteType.ResourceNode, siteName = "青木森林",
                    revealState = MapContentRevealState.Discovered, siteState = MapSiteState.None
                }
            },
            cellInfluences = new List<CellInfluenceState>()
        };
        ResourceEcologyRules.EnsureRuntime(map, progress);
        WorldMapSession.Set(map, progress);
        MapMissionContext context = new MapMissionContext
        {
            actionType = MapActionType.DevelopResourceNode,
            targetCellIndex = 0,
            targetSiteId = "map_site_resource_node"
        };

        GameDebugConfig.BypassResourceDevelopmentInfluence = false;
        Assert.IsFalse(WorldMapContentRules.CanStartAction(map, progress, context, out string blocked), blocked);
        GameDebugConfig.BypassResourceDevelopmentInfluence = true;
        Assert.IsTrue(WorldMapContentRules.CanStartAction(map, progress, context, out string bypassed), bypassed);
        GameDebugConfig.BypassResourceDevelopmentInfluence = false;
        Assert.IsFalse(WorldMapContentRules.CanStartAction(map, progress, context, out _));
    }

    private static MapSiteData Site(int cellIndex, string siteId, MapContentRevealState revealState)
    {
        return new MapSiteData
        {
            siteId = siteId, cellIndex = cellIndex, siteType = MapSiteType.ResourceNode,
            siteName = siteId, revealState = revealState, siteState = MapSiteState.None
        };
    }

    private static ResourceNodeRuntime Node(string nodeId, string siteId)
    {
        return new ResourceNodeRuntime { nodeId = nodeId, siteId = siteId };
    }

    [Test]
    public void ResourceStatus_LoadsItemTagsBeforeItemDatabaseStart()
    {
        GameObject databaseOwner = new GameObject("ItemDatabase");
        ItemDatabase database = databaseOwner.AddComponent<ItemDatabase>();
        ItemDatabase.Instance = database;
        WarehouseManager warehouse = AddWarehouse();
        warehouse.warehouseData.items = new List<ItemStack>
        {
            new ItemStack { itemId = "herb_qingling", count = 4 }
        };

        Assert.AreEqual(4, ResourceStatusService.GetCountByTag("spiritual_material"));
        Assert.AreEqual(0.2f, ResourceStatusService.Scarcity("spiritual_material", 5), 0.0001f);
    }

    private static WarehouseManager AddWarehouse()
    {
        GameObject owner = new GameObject("Warehouse");
        WarehouseManager warehouse = owner.AddComponent<WarehouseManager>();
        WarehouseManager.Instance = warehouse;
        return warehouse;
    }

    private static WorldMapProgressState DevelopedQingmuProgress(int lastUpdatedDay)
    {
        return new WorldMapProgressState
        {
            mapSites = new List<MapSiteData>
            {
                new MapSiteData
                {
                    siteId = "map_site_resource_node", siteName = "青木森林", cellIndex = 0,
                    siteType = MapSiteType.ResourceNode, siteState = MapSiteState.Developed,
                    revealState = MapContentRevealState.Discovered, isRevealed = true, canInteract = true,
                    ownerSectId = WorldMapProgressRules.PlayerSectOwnerId, lastUpdatedDay = lastUpdatedDay
                }
            }
        };
    }

    private static WorldMap OneCellMap()
    {
        WorldCell cell = new WorldCell
        {
            index = 0, coord = new HexCoord(0, 0), biome = BiomeType.TemperateForest,
            regionId = "forest", elementalAura = new ElementalAura()
        };
        return new WorldMap
        {
            width = 1, height = 1, cells = new[] { cell },
            regions = new List<MapRegionData>
            {
                new MapRegionData { regionId = "forest", regionType = MapRegionType.Forest,
                    auraTrend = MapRegionTrend.Normal, cellIndices = new List<int> { 0 } }
            },
            spiritVeins = new List<SpiritVein>()
        };
    }
}
