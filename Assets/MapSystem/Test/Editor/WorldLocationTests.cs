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
}
