using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

public class WorldMapProgressTests
{
    [TearDown]
    public void TearDown() => WorldMapSession.Clear();

    [Test]
    public void SectBaseInfluence_HasOneSixTwelveCellsAndClipsAtEdge()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 16, height = 16, seed = 711 });
        int center = map.GetIndex(new HexCoord(8, 8));
        int corner = map.GetIndex(new HexCoord(0, 0));

        WorldMapProgressState progress = ProgressWithSources(Source("base", center));
        WorldMapInfluenceRules.Recalculate(map, progress);

        Assert.AreEqual(1, progress.cellInfluences.Count(item => item.level == InfluenceLevel.Core));
        Assert.AreEqual(6, progress.cellInfluences.Count(item => item.level == InfluenceLevel.Influence));
        Assert.AreEqual(12, progress.cellInfluences.Count(item => item.level == InfluenceLevel.Outer));
        Assert.AreEqual(19, progress.cellInfluences.Count);

        WorldMapProgressState edge = ProgressWithSources(Source("edge", corner));
        WorldMapInfluenceRules.Recalculate(map, edge);
        Assert.That(edge.cellInfluences.Count, Is.LessThan(19));
        Assert.IsFalse(edge.cellInfluences.Any(item => item.cellIndex == map.cells.Length - 1));
    }

    [TestCase(0, InfluenceLevel.None)]
    [TestCase(1, InfluenceLevel.Outer)]
    [TestCase(29, InfluenceLevel.Outer)]
    [TestCase(30, InfluenceLevel.Influence)]
    [TestCase(69, InfluenceLevel.Influence)]
    [TestCase(70, InfluenceLevel.Core)]
    [TestCase(100, InfluenceLevel.Core)]
    public void InfluenceThresholds_AreExplicit(int value, InfluenceLevel expected)
    {
        Assert.AreEqual(expected, WorldMapInfluenceRules.LevelForValue(value));
    }

    [Test]
    public void MultipleSources_AccumulateSortPromoteAndClamp()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 16, height = 16, seed = 714 });
        int target = map.GetIndex(new HexCoord(8, 8));
        int first = map.GetNeighborIndices(target).First();
        int second = map.GetNeighborIndices(target).Skip(1).First();
        WorldMapProgressState progress = ProgressWithSources(
            Source("z_source", first), Source("a_source", second));

        WorldMapInfluenceRules.Recalculate(map, progress);
        CellInfluenceState state = progress.cellInfluences.Single(item => item.cellIndex == target);

        Assert.AreEqual(100, state.value);
        Assert.AreEqual(InfluenceLevel.Core, state.level);
        CollectionAssert.AreEqual(new[] { "a_source", "z_source" }, state.sourceIds);

        progress.influenceSources.Add(Source("m_source", target));
        progress.isInfluenceDirty = true;
        WorldMapInfluenceRules.Recalculate(map, progress);
        state = progress.cellInfluences.Single(item => item.cellIndex == target);
        Assert.AreEqual(100, state.value);
        CollectionAssert.AreEqual(new[] { "a_source", "m_source", "z_source" }, state.sourceIds);
    }

    [Test]
    public void DirtyAndRepeatedRecalculation_AreIdempotent()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 16, height = 16, seed = 715 });
        WorldMapProgressState progress = ProgressWithSources(Source("base", 100));
        WorldMapInfluenceRules.EnsureCurrent(map, progress);
        string first = JsonConvert.SerializeObject(progress.cellInfluences);
        Assert.IsFalse(progress.isInfluenceDirty);

        progress.isInfluenceDirty = true;
        WorldMapInfluenceRules.EnsureCurrent(map, progress);
        Assert.AreEqual(first, JsonConvert.SerializeObject(progress.cellInfluences));
    }

    [Test]
    public void InactiveSource_DoesNotContribute()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 16, height = 16, seed = 718 });
        InfluenceSourceData source = Source("inactive", 100);
        source.isActive = false;
        WorldMapProgressState progress = ProgressWithSources(source);

        WorldMapInfluenceRules.Recalculate(map, progress);

        Assert.IsEmpty(progress.cellInfluences);
        Assert.AreEqual(0, WorldMapInfluenceRules.Contribution(map, source, source.cellIndex));
    }

    [Test]
    public void SourceStrengthAndRadius_DriveRoundedAndTruncatedContribution()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 16, height = 16, seed = 719 });
        int center = map.GetIndex(new HexCoord(8, 8));
        int distanceOne = map.GetNeighborIndices(center).First();
        int distanceTwo = map.cells.First(cell =>
            HexCoord.Distance(cell.coord, map.cells[center].coord) == 2).index;
        InfluenceSourceData source = Source("scaled", center);
        source.baseStrength = 51;
        source.radius = 1;

        Assert.AreEqual(51, WorldMapInfluenceRules.Contribution(map, source, center));
        Assert.AreEqual(31, WorldMapInfluenceRules.Contribution(map, source, distanceOne));
        Assert.AreEqual(0, WorldMapInfluenceRules.Contribution(map, source, distanceTwo));
        source.baseStrength = 151;
        Assert.AreEqual(100, WorldMapInfluenceRules.Contribution(map, source, center));
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
            WorldMapInfluenceRules.GetCellState(map, progress, 5).knowledge);
        Assert.IsFalse(WorldMapInfluenceRules.CanReveal(map, progress, 5));
    }

    [Test]
    public void InfluenceCache_GrantsKnownWithoutExplicitReveal()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 16, height = 16, seed = 714 });
        WorldMapProgressState progress = new WorldMapProgressState
        {
            cellInfluences = new List<CellInfluenceState>
            {
                new CellInfluenceState
                {
                    cellIndex = 5, value = 100, level = InfluenceLevel.Core,
                    controllerSectId = "player_sect", sourceIds = new List<string> { "base" }
                }
            }
        };
        CellInfluenceRuntimeState cachedState = WorldMapInfluenceRules.GetCellState(map, progress, 5);
        Assert.AreEqual(KnowledgeState.Known, cachedState.knowledge);
        Assert.AreEqual(InfluenceLevel.Core, cachedState.level);
        Assert.IsFalse(progress.revealedCellIndices.Contains(5));

        WorldMapProgressState calculated = ProgressWithSources(Source("base", 5));
        WorldMapInfluenceRules.Recalculate(map, calculated);
        Assert.IsTrue(calculated.cellInfluences.All(state =>
            WorldMapInfluenceRules.GetCellState(map, calculated, state.cellIndex).knowledge ==
            KnowledgeState.Known));
        Assert.IsEmpty(calculated.revealedCellIndices);
    }

    [Test]
    public void InfluenceRange_DisclosesByLevelBeforeExploration()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 16, height = 16, seed = 7132 });
        int home = map.GetIndex(new HexCoord(8, 8));
        int edge = map.GetNeighborIndices(home).First();
        WorldMapProgressState progress = ProgressWithSources(Source("base", home));
        WorldMapInfluenceRules.Recalculate(map, progress);
        PlayerData sect = new PlayerData
        {
            sectId = "player_sect",
            founding = new FoundingState { initialized = true, stage = FoundingStage.Cave }
        };

        string coreText = WorldMapCellDetailsFormatter.Format(map, home,
            WorldMapViewMode.Landform, false, null, progress, sect);
        string edgeText = WorldMapCellDetailsFormatter.Format(map, edge,
            WorldMapViewMode.Landform, false, null, progress, sect);
        int outside = map.cells.First(cell => HexCoord.Distance(cell.coord, map.cells[home].coord) > 2).index;
        string unknownText = WorldMapCellDetailsFormatter.Format(map, outside,
            WorldMapViewMode.Landform, false, null, progress, sect);

        StringAssert.Contains("认知：已知", coreText);
        StringAssert.Contains("基础灵气", coreText);
        StringAssert.Contains("认知：已知", edgeText);
        StringAssert.Contains("影响值：60", edgeText);
        StringAssert.Contains("认知：未知", unknownText);
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
            },
            influenceSources = { Source(WorldMapProgressRules.PlayerSectBaseId, home) },
            isInfluenceDirty = true
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
        StringAssert.Contains("等级：影响", influenceText);
        StringAssert.Contains("影响值：60", influenceText);
        StringAssert.Contains("危险", influenceText);
        StringAssert.Contains("等级：核心", coreText);
        StringAssert.Contains("金", coreText);
        StringAssert.Contains("等级：无", revealedText);
        StringAssert.DoesNotContain("基础灵气", revealedText);
    }

    [Test]
    public void PermissionMatrix_InheritsByCachedInfluenceLevel()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 16, height = 16, seed = 716 });
        int home = map.GetIndex(new HexCoord(8, 8));
        int influence = map.GetNeighborIndices(home).First();
        int outer = map.cells.First(cell => HexCoord.Distance(cell.coord, map.cells[home].coord) == 2).index;
        int none = map.cells.First(cell => HexCoord.Distance(cell.coord, map.cells[home].coord) > 2).index;
        WorldMapProgressState progress = ProgressWithSources(Source("base", home));
        WorldMapInfluenceRules.Recalculate(map, progress);

        // Cached influence grants strategic knowledge and permissions without
        // mutating the explicit exploration record.
        Assert.AreEqual(KnowledgeState.Known,
            WorldMapInfluenceRules.GetCellState(map, progress, outer).knowledge);
        Assert.AreEqual(KnowledgeState.Known,
            WorldMapInfluenceRules.GetCellState(map, progress, influence).knowledge);
        Assert.IsEmpty(progress.revealedCellIndices);

        Assert.IsTrue(WorldMapInfluenceRules.CanInvestigate(map, progress, outer));
        Assert.IsTrue(WorldMapInfluenceRules.CanClear(map, progress, outer));
        Assert.IsTrue(WorldMapInfluenceRules.CanEstablishContact(map, progress, outer));
        Assert.IsFalse(WorldMapInfluenceRules.CanDevelopResource(map, progress, outer));
        Assert.IsTrue(WorldMapInfluenceRules.CanDevelopResource(map, progress, influence));
        Assert.IsTrue(WorldMapInfluenceRules.CanEstablishVillageRelation(map, progress, influence));
        Assert.IsTrue(WorldMapInfluenceRules.CanBuildOutpost(map, progress, influence));
        Assert.IsFalse(WorldMapInfluenceRules.CanBuildCoreFacility(map, progress, influence));
        Assert.IsTrue(WorldMapInfluenceRules.CanBuildCoreFacility(map, progress, home));
        Assert.IsTrue(WorldMapInfluenceRules.CanReveal(map, progress, none));
        Assert.IsTrue(WorldMapProgressRules.RevealCell(map, progress, none));
        Assert.IsFalse(WorldMapInfluenceRules.CanReveal(map, progress, none));
        Assert.IsFalse(WorldMapInfluenceRules.CanInvestigate(map, progress, none));
    }

    [Test]
    public void InfluenceProgress_JsonRoundTripPreservesDeterministicCache()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 16, height = 16, seed = 717 });
        WorldMapProgressState source = ProgressWithSources(Source("base", 100));
        WorldMapInfluenceRules.Recalculate(map, source);
        WorldMapProgressState restored = JsonConvert.DeserializeObject<WorldMapProgressState>(
            JsonConvert.SerializeObject(source));

        Assert.AreEqual(JsonConvert.SerializeObject(source), JsonConvert.SerializeObject(restored));
        InfluenceSourceData restoredSource = restored.influenceSources.Single();
        Assert.AreEqual(WorldMapInfluenceRules.SectBaseStrength, restoredSource.baseStrength);
        Assert.AreEqual(WorldMapInfluenceRules.SectBaseRadius, restoredSource.radius);
        Assert.IsTrue(restoredSource.isActive);
    }

    [Test]
    public void OverlayStylesAndGameplayLegend_DistinguishAllNonzeroLevels()
    {
        Assert.IsFalse(WorldMapInfluencePresentation.TryGetOverlayStyle(
            InfluenceLevel.None, out _, out _));
        Assert.IsTrue(WorldMapInfluencePresentation.TryGetOverlayStyle(
            InfluenceLevel.Outer, out Color outer, out float outerWidth));
        Assert.IsTrue(WorldMapInfluencePresentation.TryGetOverlayStyle(
            InfluenceLevel.Influence, out Color influence, out float influenceWidth));
        Assert.IsTrue(WorldMapInfluencePresentation.TryGetOverlayStyle(
            InfluenceLevel.Core, out Color core, out float coreWidth));

        Assert.Less(outer.a, influence.a);
        Assert.Less(influence.a, core.a);
        Assert.Less(outerWidth, influenceWidth);
        Assert.Less(influenceWidth, coreWidth);
        StringAssert.Contains("外缘", WorldMapInfluencePresentation.LegendText);
        StringAssert.Contains("影响", WorldMapInfluencePresentation.LegendText);
        StringAssert.Contains("核心", WorldMapInfluencePresentation.LegendText);
        StringAssert.Contains("\n", WorldMapInfluencePresentation.LegendText);
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

    private static InfluenceSourceData Source(string id, int cellIndex) => new InfluenceSourceData
    {
        sourceId = id,
        sourceType = InfluenceSourceType.SectBase,
        cellIndex = cellIndex,
        controllerSectId = "player_sect",
        baseStrength = WorldMapInfluenceRules.SectBaseStrength,
        radius = WorldMapInfluenceRules.SectBaseRadius,
        isActive = true
    };

    private static WorldMapProgressState ProgressWithSources(params InfluenceSourceData[] sources) =>
        new WorldMapProgressState
        {
            influenceSources = new List<InfluenceSourceData>(sources),
            isInfluenceDirty = true
        };
}
