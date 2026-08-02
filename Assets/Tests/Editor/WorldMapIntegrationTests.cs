using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cultivation4X.WorldMap;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

public class WorldMapIntegrationTests
{
    private GameObject root;

    [TearDown]
    public void TearDown()
    {
        WorldMapSession.Clear();
        PlayerManager.Instance = null;
        if (root != null) UnityEngine.Object.DestroyImmediate(root);
    }

    [Test]
    public void Generator_IsDeterministicAndProducesValidWorldLayers()
    {
        MapGenerationSettings settings = new MapGenerationSettings { width = 64, height = 48, seed = 321 };
        WorldMap first = WorldGenerator.Generate(settings);
        WorldMap second = WorldGenerator.Generate(settings);
        Assert.AreEqual(JsonConvert.SerializeObject(first), JsonConvert.SerializeObject(second));
        Assert.AreEqual(3, first.generationVersion);
        Assert.NotNull(first.generationSettings);
        Assert.IsNotEmpty(first.rivers);
        Assert.That(first.spiritVeins.Count(vein => vein.size == SpiritVeinSize.Large),
            Is.InRange(settings.spiritVeins.largeCount.min, settings.spiritVeins.largeCount.max));
        Assert.That(first.spiritVeins.Count(vein => vein.size == SpiritVeinSize.Medium),
            Is.InRange(settings.spiritVeins.mediumCount.min, settings.spiritVeins.mediumCount.max));
        Assert.AreEqual(5, first.spiritVeins.Select(vein => vein.primaryElement).Distinct().Count());
        Assert.IsTrue(first.cells.Any(cell => cell.isBuildable));
        Assert.IsTrue(first.cells.All(cell => cell.totalAura >= 0f && cell.totalAura <= 1f &&
                                                   Math.Abs(cell.totalAura - cell.elementalAura.Total) < 0.00001f));
        Assert.IsTrue(first.rivers.All(segment =>
            HexCoord.Distance(first.cells[segment.fromCellIndex].coord, first.cells[segment.toCellIndex].coord) == 1));
        Assert.IsTrue(first.spiritVeins.All(vein => vein.pathCellIndices
            .Zip(vein.pathCellIndices.Skip(1), (a, b) => HexCoord.Distance(first.cells[a].coord, first.cells[b].coord))
            .All(distance => distance == 1)));
    }

    [Test]
    public void NewGame_RequiresValidWorldSiteBeforeCandidateSelection()
    {
        root = new GameObject("Player");
        PlayerManager player = root.AddComponent<PlayerManager>();
        PlayerManager.Instance = player;
        player.InitializeNewFoundingGame(654);
        FoundingState state = player.playerData.founding;
        WorldMap map = WorldMapSession.Current;
        Assert.NotNull(map);
        Assert.AreEqual(FoundingStage.WorldSelection, state.stage);
        Assert.AreEqual(-1, state.selectedWorldCellIndex);
        int invalid = Array.FindIndex(map.cells, cell => !cell.isBuildable);
        Assert.IsFalse(player.ConfirmWorldSite(invalid, out _));
        int valid = Array.FindIndex(map.cells, cell => cell.isBuildable);
        Assert.IsTrue(player.ConfirmWorldSite(valid, out string reason), reason);
        Assert.AreEqual(valid, state.selectedWorldCellIndex);
        Assert.AreEqual(FoundingStage.CandidateSelection, state.stage);
    }

    [Test]
    public void WorldSnapshotAndExplorationMappings_RoundTrip()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 64, height = 48, seed = 777 });
        GameState state = new GameState { worldMap = map };
        string json = JsonConvert.SerializeObject(state);
        GameState restored = JsonConvert.DeserializeObject<GameState>(json);
        Assert.NotNull(restored?.worldMap);
        Assert.AreEqual(map.cells.Length, restored.worldMap.cells.Length);
        Assert.AreEqual(map.rivers.Count, restored.worldMap.rivers.Count);
        Assert.AreEqual(map.spiritVeins.Count, restored.worldMap.spiritVeins.Count);
        Assert.NotNull(restored.worldMap.generationSettings);
        Assert.AreEqual(3, restored.worldMap.generationSettings.generationVersion);
        Assert.AreEqual(3, restored.worldMap.pointsOfInterest.Count);
        Assert.IsTrue(restored.worldMap.pointsOfInterest.All(point => point.cellIndex >= 0));
    }

    [Test]
    public void DefaultWorldSnapshot_RoundTripsWithinPrototypeBudget()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { seed = 778 });
        string json = JsonConvert.SerializeObject(map);
        WorldMap restored = JsonConvert.DeserializeObject<WorldMap>(json);
        Assert.NotNull(restored);
        Assert.AreEqual(128 * 96, restored.cells.Length);
        Assert.Less(json.Length, 15_000_000, "默认世界快照超过 15 MB 原型预算");
        TestContext.WriteLine($"Default world JSON bytes (UTF-16 chars): {json.Length}");
    }

    [Test]
    public void Settings_AreValidatedAndCapturedByValue()
    {
        MapGenerationSettings settings = new MapGenerationSettings
        {
            width = 64,
            height = 48,
            seed = 9123
        };
        settings.terrain.seaLevel = 0.45f;
        WorldMap map = WorldGenerator.Generate(settings);
        settings.seed = 1;
        settings.terrain.seaLevel = 0.60f;
        settings.spiritVeins.largeCount.min = 0;

        Assert.AreEqual(9123, map.generationSettings.seed);
        Assert.AreEqual(0.45f, map.generationSettings.terrain.seaLevel);
        Assert.AreEqual(10, map.generationSettings.spiritVeins.largeCount.min);

        MapGenerationSettings invalid = new MapGenerationSettings();
        invalid.terrain.seaLevel = invalid.terrain.deepWaterThreshold;
        Assert.That(MapGenerationSettingsValidator.Validate(invalid), Is.Not.Empty);
        Assert.Throws<ArgumentException>(() => WorldGenerator.Generate(invalid));
    }

    [Test]
    public void WaterDistance_IsCalculatedAfterLandformClassification()
    {
        MapGenerationSettings settings = new MapGenerationSettings
        {
            width = 128,
            height = 96,
            seed = 48621
        };
        settings.climate.moistureNoiseStrength = 0f;
        settings.climate.waterProximityMoistureStrength = 1f;
        settings.climate.riverMoistureBoost = 0f;
        WorldMap map = WorldGenerator.Generate(settings);
        WorldCell water = map.cells.First(cell =>
            cell.landform == LandformType.DeepWater || cell.landform == LandformType.ShallowWater);
        WorldCell coast = map.cells.First(cell => cell.landform == LandformType.Coast);
        WorldCell inland = map.cells
            .Where(cell => cell.landform != LandformType.DeepWater &&
                           cell.landform != LandformType.ShallowWater &&
                           cell.landform != LandformType.Coast)
            .OrderBy(cell => cell.moisture).First();

        Assert.Greater(water.moisture, coast.moisture);
        Assert.Greater(coast.moisture, inland.moisture);
    }

    [Test]
    public void RiverNetwork_IsAcyclicAndReachesWaterWithNondecreasingFlow()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 64, height = 48, seed = 7171 });
        Dictionary<int, RiverSegment> downstream = map.rivers
            .GroupBy(segment => segment.fromCellIndex)
            .ToDictionary(group => group.Key, group => group.Single());

        foreach (RiverSegment segment in map.rivers)
        {
            Assert.AreEqual(1, HexCoord.Distance(
                map.cells[segment.fromCellIndex].coord, map.cells[segment.toCellIndex].coord));
            HashSet<int> visited = new HashSet<int>();
            int current = segment.fromCellIndex;
            float previousFlow = 0f;
            for (int step = 0; step <= map.cells.Length; step++)
            {
                Assert.IsTrue(visited.Add(current), "河网出现循环");
                if (!downstream.TryGetValue(current, out RiverSegment next))
                {
                    Assert.IsTrue(map.cells[current].landform == LandformType.DeepWater ||
                                  map.cells[current].landform == LandformType.ShallowWater);
                    break;
                }
                Assert.GreaterOrEqual(next.flow, previousFlow);
                previousFlow = next.flow;
                current = next.toCellIndex;
            }
        }
    }

    [Test]
    public void Statistics_CoverEveryCellWithoutMutatingMap()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 64, height = 48, seed = 8181 });
        string before = JsonConvert.SerializeObject(map);
        WorldMapStatistics statistics = WorldMapStatisticsCalculator.Calculate(map);

        Assert.AreEqual(map.cells.Length, statistics.landforms.Sum(item => item.count));
        Assert.AreEqual(map.cells.Length, statistics.biomes.Sum(item => item.count));
        Assert.AreEqual(map.cells.Length, statistics.moisture.Sum(item => item.count));
        Assert.AreEqual(map.cells.Length, statistics.aura.Sum(item => item.count));
        Assert.AreEqual(before, JsonConvert.SerializeObject(map));
    }

    [Test]
    public void VersionEightSave_RequiresCompleteWorldSnapshotAndValidIndices()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 64, height = 48, seed = 9191 });
        GameState state = new GameState
        {
            worldMap = map,
            sect = new PlayerData
            {
                founding = new FoundingState
                {
                    initialized = true,
                    stage = FoundingStage.WorldSelection,
                    selectedWorldCellIndex = -1,
                    candidates = FoundingRules.GenerateCandidates(9191)
                }
            }
        };
        WorldMapContentRules.EnsureCandidates(map, state.worldMapProgress);
        Assert.DoesNotThrow(() => SaveManager.ValidateWorldMapState(state));

        state.worldMap.pointsOfInterest[0].cellIndex = map.cells.Length;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(state));
    }

    [Test]
    public void ExplorationResolver_RejectsOutOfRangePointWithoutThrowing()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 64, height = 48, seed = 2024 });
        WorldMapSession.Set(map);
        WorldPointOfInterest point = map.pointsOfInterest.First(item => item.id == "mistwood");
        point.cellIndex = map.cells.Length;
        Assert.AreEqual(-1, ExplorationRules.GetMapCellIndex("mistwood"));
    }

    [Test]
    public void PresentationPolicy_IsDeterministicAndDoesNotCreateDemoFacts()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 64, height = 48, seed = 3030 });
        string before = JsonConvert.SerializeObject(map);
        List<WorldMapPresentationMarker> markers =
            WorldMapPresentationMarkerFactory.CreatePointOfInterestMarkers(map);
        List<WorldMapTerrainIconPlacement> first =
            WorldMapPresentationPolicy.BuildTerrainIconPlacements(
                map, WorldMapViewMode.Landform, 45f, markers);
        List<WorldMapTerrainIconPlacement> second =
            WorldMapPresentationPolicy.BuildTerrainIconPlacements(
                map, WorldMapViewMode.Landform, 45f, markers);

        Assert.AreEqual(3, markers.Count);
        Assert.IsTrue(markers.All(marker => !marker.isDemo));
        Assert.AreEqual(JsonConvert.SerializeObject(first), JsonConvert.SerializeObject(second));
        Assert.AreEqual(before, JsonConvert.SerializeObject(map));
    }
}
