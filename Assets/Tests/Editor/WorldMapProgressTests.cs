using System.Linq;
using Cultivation4X.WorldMap;
using NUnit.Framework;

public class WorldMapProgressTests
{
    [TearDown]
    public void TearDown() => WorldMapSession.Clear();

    [Test]
    public void RadiusTwoInfluence_HasNineteenCellsInInteriorAndClipsAtEdge()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 16, height = 16, seed = 711 });
        int center = map.GetIndex(new HexCoord(8, 8));
        int corner = map.GetIndex(new HexCoord(0, 0));

        Assert.AreEqual(InfluenceLevel.Core,
            WorldMapProgressRules.GetInfluence(map, center, center, 2));
        Assert.AreEqual(19, WorldMapProgressRules.CountInfluenceCells(map, center, 2));
        Assert.That(WorldMapProgressRules.CountInfluenceCells(map, corner, 2), Is.LessThan(19));
        Assert.AreEqual(InfluenceLevel.None,
            WorldMapProgressRules.GetInfluence(map, map.cells.Length - 1, corner, 2));
    }

    [Test]
    public void RevealCell_IsIdempotentAndRejectsInvalidIndices()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 16, height = 16, seed = 712 });
        WorldMapProgressState progress = new WorldMapProgressState();

        Assert.IsTrue(WorldMapProgressRules.RevealCell(map, progress, 5));
        Assert.IsTrue(WorldMapProgressRules.RevealCell(map, progress, 5));
        Assert.AreEqual(1, progress.revealedCellIndices.Count);
        Assert.IsFalse(WorldMapProgressRules.RevealCell(map, progress, -1));
        Assert.IsFalse(WorldMapProgressRules.RevealCell(map, progress, map.cells.Length));
        Assert.AreEqual(KnowledgeState.Known,
            WorldMapProgressRules.GetKnowledge(map, progress, 5, -1, 0));
    }

    [Test]
    public void KnowledgeAndDetails_RespectUnknownInfluenceAndCoreLevels()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 16, height = 16, seed = 713 });
        int home = map.GetIndex(new HexCoord(8, 8));
        int influence = map.GetNeighborIndices(home).First();
        int unknown = map.cells.First(cell =>
            HexCoord.Distance(cell.coord, map.cells[home].coord) > 2).index;
        WorldMapProgressState progress = new WorldMapProgressState
        {
            mapSites =
            {
                new MapSiteData
                {
                    siteId = WorldMapProgressRules.PlayerSectBaseId,
                    siteType = MapSiteType.SectBase,
                    siteName = "青云宗",
                    cellIndex = home,
                    isRevealed = true,
                    canInteract = true
                }
            }
        };
        PlayerData sect = new PlayerData
        {
            sectId = "player_sect",
            sectName = "青云宗",
            influenceRadius = 2,
            founding = new FoundingState
            {
                initialized = true,
                stage = FoundingStage.Cave,
                selectedWorldCellIndex = home
            }
        };

        string unknownText = WorldMapCellDetailsFormatter.Format(map, unknown,
            WorldMapViewMode.Landform, false, null, progress, sect);
        string influenceText = WorldMapCellDetailsFormatter.Format(map, influence,
            WorldMapViewMode.Landform, false, null, progress, sect);
        string coreText = WorldMapCellDetailsFormatter.Format(map, home,
            WorldMapViewMode.Landform, false, null, progress, sect);
        Assert.IsTrue(WorldMapProgressRules.RevealCell(map, progress, unknown));
        string revealedText = WorldMapCellDetailsFormatter.Format(map, unknown,
            WorldMapViewMode.Landform, false, null, progress, sect);

        StringAssert.Contains("认知：未知", unknownText);
        StringAssert.DoesNotContain("灵气", unknownText);
        StringAssert.Contains("宗门影响：边缘", influenceText);
        StringAssert.Contains("危险", influenceText);
        StringAssert.Contains("宗门影响：核心", coreText);
        StringAssert.Contains("金", coreText);
        StringAssert.Contains("宗门影响：无", revealedText);
        StringAssert.DoesNotContain("基础灵气", revealedText);
    }

    [Test]
    public void DangerRule_IsDeterministicAndDoesNotMutateCell()
    {
        WorldCell cell = new WorldCell
        {
            landform = LandformType.Mountain,
            biome = BiomeType.Alpine,
            totalAura = 0.5f
        };
        WorldDangerLevel first = WorldMapProgressRules.GetDanger(cell);
        WorldDangerLevel second = WorldMapProgressRules.GetDanger(cell);

        Assert.AreEqual(WorldDangerLevel.High, first);
        Assert.AreEqual(first, second);
        Assert.AreEqual(0.5f, cell.totalAura);
    }
}
