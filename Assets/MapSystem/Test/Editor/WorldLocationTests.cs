using System.Linq;
using Cultivation4X.WorldMap;
using NUnit.Framework;

public class WorldLocationTests
{
    [Test]
    public void CreateStarterVillage_PlacesLocationNearSectAndLinksCell()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 32, height = 24, seed = 7001 });
        int sect = map.cells.First(cell => cell.isBuildable).index;

        WorldLocation village = WorldLocationRules.CreateStarterVillage(map, sect);

        Assert.NotNull(village);
        Assert.AreEqual(WorldLocationRules.StarterVillageId, village.id);
        Assert.AreEqual(LocationType.Village, village.type);
        Assert.AreEqual(LocationState.Active, village.state);
        Assert.AreEqual("青石村", village.name);
        Assert.AreEqual(3, village.availableActions.Count);
        Assert.IsTrue(village.availableActions.Any(action => action.actionType == LocationActionType.Explore),
            "青石村保留既有的地点调查入口");

        int villageCell = map.GetIndex(new HexCoord(village.position.x, village.position.y));
        Assert.GreaterOrEqual(villageCell, 0);
        Assert.IsTrue(HexCoord.Distance(map.cells[sect].coord, map.cells[villageCell].coord) <= 2,
            "青石村必须位于宗门影响范围内");
        Assert.AreEqual(village.id, map.cells[villageCell].locationId);
        Assert.AreSame(village, map.GetLocation(village.id));
        Assert.AreSame(village, map.GetLocationAt(map.cells[villageCell]));
    }

    [Test]
    public void StarterVillage_IsIdempotent()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 32, height = 24, seed = 7002 });
        int sect = map.cells.First(cell => cell.isBuildable).index;

        WorldLocation first = WorldLocationRules.CreateStarterVillage(map, sect);
        WorldLocation second = WorldLocationRules.CreateStarterVillage(map, sect);

        Assert.AreSame(first, second);
        Assert.AreEqual(1, map.locations.Count);
        Assert.AreEqual(first.id, map.cells[map.GetIndex(
            new HexCoord(first.position.x, first.position.y))].locationId);
    }

    [Test]
    public void SynchronizeFromMapSites_CreatesFacadeOnlyAfterDiscovery()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 32, height = 24, seed = 7004 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);

        MapSiteData spring = progress.mapSites.Single(site => site.siteType == MapSiteType.SpiritSpring);
        WorldLocation facade = map.GetLocation("world_location_" + spring.siteId);
        Assert.IsNull(facade, "Hidden 的 MapSite 不应创建地点门面");
        Assert.IsTrue(string.IsNullOrEmpty(map.cells[spring.cellIndex].locationId));

        spring.revealState = MapContentRevealState.Discovered;
        spring.discoveredDay = spring.lastUpdatedDay = 0;
        WorldMapContentRules.SynchronizeLegacyFlags(spring);
        WorldLocationRules.SynchronizeFromMapSites(map, progress);
        facade = map.GetLocation("world_location_" + spring.siteId);
        Assert.NotNull(facade);
        Assert.IsTrue(WorldLocationRules.IsLocationRevealed(facade, progress),
            "Discovered 后地点门面应可见");

        int sect = map.cells.First(cell => cell.isBuildable).index;
        WorldLocation village = WorldLocationRules.CreateStarterVillage(map, sect);
        Assert.NotNull(village);
        Assert.IsTrue(WorldLocationRules.IsLocationRevealed(village, progress),
            "手建青石村应始终可见");
    }

    [Test]
    public void SynchronizeFromMapSites_UpdatesLocationStateFromSiteProgress()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 32, height = 24, seed = 7005 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);

        MapSiteData beast = progress.mapSites.Single(site => site.siteType == MapSiteType.BeastLair);
        beast.revealState = MapContentRevealState.Discovered;
        beast.discoveredDay = beast.lastUpdatedDay = 0;
        WorldMapContentRules.SynchronizeLegacyFlags(beast);
        WorldLocationRules.SynchronizeFromMapSites(map, progress);
        WorldLocation facade = map.GetLocation("world_location_" + beast.siteId);
        Assert.AreEqual(LocationState.Active, facade.state);

        beast.siteState = MapSiteState.Developed;
        beast.ownerSectId = "player_sect";
        WorldLocationRules.SynchronizeFromMapSites(map, progress);
        Assert.AreEqual(LocationState.Inactive, facade.state,
            "清理后的兽巢应在 WorldLocation 门面上标记为 Inactive");
    }

    [Test]
    public void SynchronizeFromMapSites_RefreshesResourceDevelopmentAction()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 32, height = 24, seed = 7011 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        MapSiteData resource = progress.mapSites.Single(site => site.siteType == MapSiteType.ResourceNode);
        resource.revealState = MapContentRevealState.Discovered;
        WorldMapContentRules.SynchronizeLegacyFlags(resource);

        WorldLocationRules.SynchronizeFromMapSites(map, progress);
        WorldLocation facade = map.GetLocation("world_location_" + resource.siteId);
        Assert.IsTrue(facade.availableActions.Any(action =>
            action.actionType == LocationActionType.DevelopResourceNode));
        Assert.IsEmpty(facade.availableMissionIds,
            "地图资源开发必须携带 MapMissionContext，不应退回普通资源任务");

        resource.siteState = MapSiteState.Developed;
        resource.ownerSectId = WorldMapProgressRules.PlayerSectOwnerId;
        WorldLocationRules.SynchronizeFromMapSites(map, progress);
        Assert.IsFalse(facade.availableActions.Any(action =>
            action.actionType == LocationActionType.DevelopResourceNode));
    }

    [Test]
    public void SynchronizeFromMapSites_CreatesFacadesAndRebindsMovedSites()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 32, height = 24, seed = 7003 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);

        Assert.AreEqual(7, progress.mapSites.Count);
        MapSiteData spring = progress.mapSites.Single(site => site.siteType == MapSiteType.SpiritSpring);
        spring.revealState = MapContentRevealState.Discovered;
        spring.discoveredDay = spring.lastUpdatedDay = 0;
        WorldMapContentRules.SynchronizeLegacyFlags(spring);
        WorldLocationRules.SynchronizeFromMapSites(map, progress);
        Assert.AreEqual(1, map.locations.Count);
        WorldLocation facade = map.GetLocation("world_location_" + spring.siteId);
        Assert.NotNull(facade);
        Assert.AreEqual(spring.siteId, facade.sourceMapSiteId);
        Assert.AreEqual(facade.id, map.cells[spring.cellIndex].locationId);

        int oldCell = spring.cellIndex;
        int newCell = map.cells.First(cell => cell.index != oldCell && string.IsNullOrEmpty(cell.locationId)).index;
        spring.cellIndex = newCell;
        WorldLocationRules.SynchronizeFromMapSites(map, progress);

        WorldLocation moved = map.GetLocation("world_location_" + spring.siteId);
        Assert.AreEqual(newCell, map.GetIndex(new HexCoord(moved.position.x, moved.position.y)));
        Assert.AreEqual(moved.id, map.cells[newCell].locationId);
        Assert.IsTrue(string.IsNullOrEmpty(map.cells[oldCell].locationId), "旧格的地点引用应被清空");
    }

    [Test]
    public void SynchronizeFromMapSites_RemovesStaleContentButKeepsStarterVillage()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 32, height = 24, seed = 7001 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        MapSiteData candidateVillage = progress.mapSites.Single(site => site.siteType == MapSiteType.Village);
        string candidateVillageId = "world_location_" + candidateVillage.siteId;
        MapSiteData beast = progress.mapSites.Single(site => site.siteType == MapSiteType.BeastLair);
        string beastId = "world_location_" + beast.siteId;
        beast.revealState = MapContentRevealState.Discovered;
        beast.discoveredDay = beast.lastUpdatedDay = 0;
        WorldMapContentRules.SynchronizeLegacyFlags(beast);
        WorldLocationRules.SynchronizeFromMapSites(map, progress);
        Assert.NotNull(map.GetLocation(beastId));

        progress.mapSites.Remove(beast);
        WorldLocationRules.SynchronizeFromMapSites(map, progress);
        Assert.IsFalse(map.locations.ContainsKey(beastId), "失效的 MapSite 门面应被清理");

        int sect = map.cells.First(cell => cell.isBuildable).index;
        WorldLocation village = WorldLocationRules.CreateStarterVillage(map, sect);
        Assert.NotNull(village);
        WorldLocationRules.SynchronizeFromMapSites(map, progress);

        Assert.NotNull(map.GetLocation(WorldLocationRules.StarterVillageId));
        Assert.IsFalse(map.locations.ContainsKey(candidateVillageId),
            "Hidden 候选村庄不应创建门面");
        Assert.AreEqual(1, map.locations.Count);
    }


    [Test]
    public void CreateStarterVillage_ExplicitlyAvoidsReservedMapSiteCells()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 32, height = 24, seed = 7012 });
        int sect = map.cells.First(cell => cell.isBuildable &&
            map.GetNeighborIndices(cell.index).Count(index => map.cells[index].isBuildable) >= 2).index;
        int reserved = map.GetNeighborIndices(sect).First(index => map.cells[index].isBuildable);
        var progress = new WorldMapProgressState
        {
            mapSites = new System.Collections.Generic.List<MapSiteData>
            {
                new MapSiteData
                {
                    siteId = "reserved", siteName = "隐藏灵泉", siteType = MapSiteType.SpiritSpring,
                    cellIndex = reserved, revealState = MapContentRevealState.Hidden
                }
            }
        };

        WorldLocation village = WorldLocationRules.CreateStarterVillage(map, sect, progress);

        Assert.NotNull(village);
        Assert.AreNotEqual(reserved,
            map.GetIndex(new HexCoord(village.position.x, village.position.y)));
    }
}
