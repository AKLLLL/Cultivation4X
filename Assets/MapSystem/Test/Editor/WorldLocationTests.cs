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
    public void IsLocationRevealed_HidesContentUntilDiscoveredAndKeepsHandCreatedVisible()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 32, height = 24, seed = 7004 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);

        MapSiteData spring = progress.mapSites.Single(site => site.siteType == MapSiteType.SpiritSpring);
        WorldLocation facade = map.GetLocation("world_location_" + spring.siteId);
        Assert.NotNull(facade);
        Assert.IsFalse(WorldLocationRules.IsLocationRevealed(facade, progress),
            "Hidden 的 MapSite 不应暴露地点信息");

        spring.revealState = MapContentRevealState.Discovered;
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
        WorldLocation facade = map.GetLocation("world_location_" + beast.siteId);
        Assert.AreEqual(LocationState.Active, facade.state);

        beast.siteState = MapSiteState.Developed;
        beast.ownerSectId = "player_sect";
        WorldLocationRules.SynchronizeFromMapSites(map, progress);
        Assert.AreEqual(LocationState.Inactive, facade.state,
            "清理后的兽巢应在 WorldLocation 门面上标记为 Inactive");
    }

    [Test]
    public void SynchronizeFromMapSites_CreatesFacadesAndRebindsMovedSites()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 32, height = 24, seed = 7003 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);

        Assert.AreEqual(6, progress.mapSites.Count);
        Assert.AreEqual(6, map.locations.Count);
        foreach (MapSiteData site in progress.mapSites)
        {
            WorldLocation facade = map.GetLocation("world_location_" + site.siteId);
            Assert.NotNull(facade, "每个 MapSite 都应同步为 WorldLocation 门面");
            Assert.AreEqual(site.siteId, facade.sourceMapSiteId);
            Assert.AreEqual(site.cellIndex,
                map.GetIndex(new HexCoord(facade.position.x, facade.position.y)));
            Assert.AreEqual(facade.id, map.cells[site.cellIndex].locationId);
            Assert.NotNull(facade.availableActions);
            Assert.NotNull(facade.availableMissionIds);
        }

        MapSiteData spring = progress.mapSites.Single(site => site.siteType == MapSiteType.SpiritSpring);
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

        progress.mapSites.Remove(beast);
        WorldLocationRules.SynchronizeFromMapSites(map, progress);
        Assert.IsFalse(map.locations.ContainsKey(beastId), "失效的 MapSite 门面应被清理");

        int sect = map.cells.First(cell => cell.isBuildable).index;
        WorldLocation village = WorldLocationRules.CreateStarterVillage(map, sect);
        Assert.NotNull(village);
        WorldLocationRules.SynchronizeFromMapSites(map, progress);

        Assert.NotNull(map.GetLocation(WorldLocationRules.StarterVillageId));
        Assert.IsFalse(map.locations.ContainsKey(candidateVillageId),
            "手建青石村存在时，候选村庄门面不应重复保留");
        Assert.AreEqual(5, map.locations.Count);
    }
}
