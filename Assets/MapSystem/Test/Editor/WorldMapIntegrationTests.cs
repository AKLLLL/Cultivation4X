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
        if (WorldGenerationDiagnosticsStore.TryGet(first, out WorldGenerationDiagnostics diagnostics))
            TestContext.WriteLine($"smallMap land={first.cells.Count(cell => cell.landform != LandformType.DeepWater && cell.landform != LandformType.ShallowWater)} " +
                                  $"mountains={first.cells.Count(cell => cell.landform == LandformType.Mountain)} " +
                                  $"maxHeight={first.cells.Max(cell => cell.height):F3} " +
                                  $"maxFlow={diagnostics.maximumAccumulatedFlow:F2} rivers={first.rivers.Count}");
        Assert.AreEqual(JsonConvert.SerializeObject(first), JsonConvert.SerializeObject(second));
        Assert.AreEqual(4, first.generationVersion);
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
        Assert.AreEqual(4, restored.worldMap.generationSettings.generationVersion);
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
        Assert.IsNotEmpty(restored.regions);
        Assert.IsTrue(restored.cells.All(cell => !string.IsNullOrWhiteSpace(cell.regionId)));
        Assert.Less(json.Length, 15_000_000, "默认世界快照超过 15 MB 原型预算");
        TestContext.WriteLine($"Default world JSON bytes (UTF-16 chars): {json.Length}");
    }

    [Test]
    public void Settings_AreValidatedAndCapturedByValue()
    {
        Assert.AreEqual(0.635f, new TerrainGenerationParameters().hillUpperThreshold,
            "默认高山阈值应保留本轮视觉校准值");
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

    [TestCase(6101)]
    [TestCase(48621)]
    [TestCase(6123)]
    [TestCase(1109698167)]
    [TestCase(301473179)]
    [TestCase(1844258240)]
    [TestCase(414882)]
    [TestCase(20260806)]
    [TestCase(777)]
    [TestCase(778)]
    [TestCase(9123)]
    [TestCase(6102)]
    [TestCase(6103)]
    [TestCase(6104)]
    [TestCase(805)]
    [TestCase(5101)]
    [TestCase(5102)]
    [TestCase(9201)]
    [TestCase(123456789)]
    [TestCase(987654321)]
    public void DefaultTerrain_HasVisibleButBoundedMountainRanges(int seed)
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 128, height = 96, seed = seed });
        int land = map.cells.Count(cell => cell.landform != LandformType.DeepWater &&
                                          cell.landform != LandformType.ShallowWater);
        int mountains = map.cells.Count(cell => cell.landform == LandformType.Mountain);
        int buildable = map.cells.Count(cell => cell.isBuildable);
        float mountainPercentage = mountains * 100f / land;
        float buildablePercentage = buildable * 100f / land;
        int largestMountainRange = LargestConnectedMountainComponent(map);
        int denseMountainCore = CountDenseMountainCore(map);
        float denseMountainPercentage = mountains > 0 ? denseMountainCore * 100f / mountains : 0f;
        int maximumMountainThickness = MaximumMountainThickness(map);

        TestContext.WriteLine($"seed={seed} land={land} mountains={mountains} " +
                              $"mountainLand%={mountainPercentage:F2} " +
                              $"buildableLand%={buildablePercentage:F2} " +
                              $"largestMountainRange={largestMountainRange} " +
                              $"denseMountainCore%={denseMountainPercentage:F2} " +
                              $"maximumMountainThickness={maximumMountainThickness}");
        Assert.That(mountainPercentage, Is.InRange(2f, 8.05f),
            "脊线门控后的高山应保持可见，同时不再把高海拔平台整体吞并");
        Assert.GreaterOrEqual(largestMountainRange, 8,
            "至少应形成一片达到宏大山地表现门槛的连续山脉");
        Assert.Less(denseMountainPercentage, 30f,
            "被六个Mountain完全包围的内部格不应占比过高，否则仍是宽阔高原而非山脊");
        Assert.LessOrEqual(maximumMountainThickness, 7,
            "山脉局部厚度不应继续形成大面积实心高原");
        Assert.Greater(buildablePercentage, 35f,
            "提高高山比例后仍应保留足够的建宗候选陆地");
    }

    [Test]
    public void FixedSeed_MountainFieldFormsLongRidgeWithRealHeightRelief()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 128, height = 96, seed = 20260806 });
        WorldCell[] mountains = map.cells.Where(cell => cell.landform == LandformType.Mountain).ToArray();
        WorldCell[] plains = map.cells.Where(cell => cell.landform == LandformType.Plain).ToArray();

        Assert.GreaterOrEqual(LargestConnectedMountainComponent(map), 24,
            "固定验收种子必须形成长山脊，不能退回零散单格高山");
        Assert.IsNotEmpty(mountains);
        Assert.IsNotEmpty(plains);
        Assert.Greater(mountains.Max(cell => cell.height) - plains.Average(cell => cell.height), 0.22f,
            "山脉必须存在于逻辑高度场中，而不是只依靠模型或地形标签");
        Assert.IsTrue(map.regions.Any(region => region.regionType == MapRegionType.Valley),
            "峰脊之间应保留至少一条可被 Region 规则识别的谷地走廊");
        Assert.IsTrue(WorldGenerationDiagnosticsStore.TryGet(map,
            out WorldGenerationDiagnostics diagnostics));
        Assert.AreEqual(map.cells.Length, diagnostics.mountainRidgeCore.Length);
        Assert.Greater(diagnostics.mountainRidgeCore.Count(value => value), 24,
            "运行期诊断必须保留可供表现层识别的连续峰脊骨架");
        Assert.Greater(diagnostics.mountainPeaks.Count(value => value), 1,
            "山系必须保留多个明确峰点，而不是只剩最终模糊高度");
        Assert.Greater(diagnostics.mountainPasses.Count(value => value), 0,
            "山脊连接中必须保留受控山口低点");
        Assert.Greater(diagnostics.terrainSlope.Max(), 0.08f,
            "高度场必须形成可用于裸岩材质判定的明显坡度");

        WorldCell[] terraces = mountains.Where(cell => cell.isBuildable).ToArray();
        TestContext.WriteLine($"mountainTerraces={terraces.Length}");
        Assert.IsNotEmpty(terraces, "固定验收种子必须生成至少一处可建山腰台地");
        foreach (IGrouping<string, WorldCell> group in terraces.GroupBy(cell => cell.regionId))
        {
            WorldCell[] cells = group.ToArray();
            Assert.That(cells.Length, Is.InRange(2, 4),
                "每个山脉 Region 的首版台地必须保持为 2～4 个连续格");
            Assert.IsTrue(cells.All(cell => cell.landform == LandformType.Mountain &&
                                           cell.internalPositionTag == MapInternalPositionTag.Mountainside),
                "可建山地只能来自山腰候选，不能占用峰顶、山脊、山口或山脚");
            Assert.IsTrue(cells.All(cell => !diagnostics.mountainRidgeCore[cell.index] &&
                                           !diagnostics.mountainPeaks[cell.index] &&
                                           !diagnostics.mountainPasses[cell.index]),
                "台地不得覆盖生成期峰脊骨架或山口");
            Assert.IsTrue(cells.All(cell => cells.Length == 1 || map.GetNeighborIndices(cell.index)
                .Any(neighbor => cells.Any(other => other.index == neighbor))),
                "台地集群内的每个格子都必须与同组另一格相邻");
        }
        string terraceLabel = WorldMapPresentationModels.Format(map, terraces[0].index,
            WorldMapViewMode.Landform, true, Array.Empty<WorldMapPresentationMarker>());
        StringAssert.Contains("山地台地", terraceLabel,
            "可建山地必须在选址信息中显示明确台地语义，不能只显示普通山地");
    }

    [Test]
    public void EastToWestRainShadow_WetsWindwardSideAndDriesWesternLee()
    {
        const int width = 7;
        const int height = 3;
        WorldMap map = new WorldMap
        {
            width = width,
            height = height,
            cells = new WorldCell[width * height]
        };
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                int index = row * width + col;
                bool easternWater = col == width - 1;
                bool barrier = col == 3;
                map.cells[index] = new WorldCell
                {
                    index = index,
                    coord = new HexCoord(col, row),
                    landform = easternWater ? LandformType.ShallowWater :
                        barrier ? LandformType.Mountain : LandformType.Plain,
                    height = easternWater ? 0.2f : barrier ? 0.9f : 0.5f,
                    moisture = 0.5f,
                    temperature = 0.6f
                };
            }
        }

        System.Reflection.MethodInfo rainShadow = typeof(TerrainGenerator).GetMethod(
            "ApplyEastToWestRainShadow",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(rainShadow);
        rainShadow.Invoke(null, new object[] { map });

        WorldCell easternWindward = map.cells[1 * width + 4];
        WorldCell mountain = map.cells[1 * width + 3];
        WorldCell westernLee = map.cells[1 * width + 2];
        Assert.Greater(easternWindward.moisture, westernLee.moisture,
            "东风越过山脉后，西侧背风坡应比东侧迎风坡干燥");
        Assert.Greater(mountain.moisture, westernLee.moisture,
            "迎风山地应获得地形抬升降水，而不是与背风坡同湿度");
        Assert.Greater(map.cells[1 * width + 6].moisture, easternWindward.moisture,
            "东侧水体应作为湿气补给源");
        Assert.IsTrue(WorldGenerationDiagnosticsStore.TryGet(map,
            out WorldGenerationDiagnostics diagnostics));
        Assert.Greater(diagnostics.rainfall[mountain.index],
            diagnostics.rainfall[westernLee.index],
            "迎风山地的实际降雨量应高于背风坡，而不只是修改土壤湿度显示");
    }

    [Test]
    public void EastToWestMoisture_DecaysAcrossFlatInlandDistance()
    {
        const int width = 48;
        const int height = 3;
        WorldMap map = new WorldMap
        {
            width = width,
            height = height,
            cells = new WorldCell[width * height]
        };
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                int index = row * width + col;
                bool easternWater = col == width - 1;
                map.cells[index] = new WorldCell
                {
                    index = index,
                    coord = new HexCoord(col, row),
                    landform = easternWater ? LandformType.ShallowWater : LandformType.Plain,
                    height = easternWater ? 0.2f : 0.5f,
                    moisture = 0.5f,
                    temperature = 0.6f
                };
            }
        }

        System.Reflection.MethodInfo rainShadow = typeof(TerrainGenerator).GetMethod(
            "ApplyEastToWestRainShadow",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(rainShadow);
        rainShadow.Invoke(null, new object[] { map });

        float nearCoast = map.cells[1 * width + width - 2].moisture;
        float middleInland = map.cells[1 * width + width / 2].moisture;
        float farInland = map.cells[1 * width].moisture;
        Assert.Greater(nearCoast, middleInland,
            "离开东侧水体后，平地携带水汽应随传播距离逐渐衰减");
        Assert.Greater(middleInland, farInland,
            "远离水体的内陆不应继续获得无来源的湿度正增益");
        Assert.Greater(nearCoast - farInland, 0.20f,
            "长距离平坦内陆应形成足以影响群系分布的湿度梯度");
    }

    [Test]
    public void EastToWestMoisture_WaterSourceFormsBroadHexPlumeInsteadOfSingleRow()
    {
        const int width = 20;
        const int height = 11;
        WorldMap map = new WorldMap
        {
            width = width,
            height = height,
            cells = new WorldCell[width * height]
        };
        WorldMap baselineMap = new WorldMap
        {
            width = width,
            height = height,
            cells = new WorldCell[width * height]
        };
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                int index = row * width + col;
                bool source = col == 18 && row == 5;
                map.cells[index] = new WorldCell
                {
                    index = index,
                    coord = new HexCoord(col, row),
                    landform = source ? LandformType.ShallowWater : LandformType.Plain,
                    height = source ? 0.2f : 0.5f,
                    moisture = 0.18f,
                    temperature = 0.6f
                };
                baselineMap.cells[index] = new WorldCell
                {
                    index = index,
                    coord = new HexCoord(col, row),
                    landform = LandformType.Plain,
                    height = 0.5f,
                    moisture = 0.18f,
                    temperature = 0.6f
                };
            }
        }

        System.Reflection.MethodInfo rainShadow = typeof(TerrainGenerator).GetMethod(
            "ApplyEastToWestRainShadow",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(rainShadow);
        rainShadow.Invoke(null, new object[] { map });
        rainShadow.Invoke(null, new object[] { baselineMap });

        int influencedRows = 0;
        for (int row = 0; row < height; row++)
        {
            float increase = map.GetCell(new HexCoord(13, row)).moisture -
                             baselineMap.GetCell(new HexCoord(13, row)).moisture;
            TestContext.WriteLine($"row={row} plumeIncrease={increase:F4}");
            if (increase > 0.005f) influencedRows++;
        }
        Assert.GreaterOrEqual(influencedRows, 3,
            "单个水体的下风湿气应覆盖多行六边形，而非保持一格宽通道");
    }

    [Test]
    public void DefaultClimate_ReportedAdjacentDiscontinuityIsRelaxed()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 128, height = 96, seed = 186677644 });
        WorldCell westernCell = map.GetCell(new HexCoord(73, 49));
        WorldCell easternCell = map.GetCell(new HexCoord(74, 49));
        Assert.NotNull(westernCell);
        Assert.NotNull(easternCell);
        TestContext.WriteLine($"westMoisture={westernCell.moisture:F3} " +
                              $"eastMoisture={easternCell.moisture:F3}");
        Assert.LessOrEqual(Math.Abs(westernCell.moisture - easternCell.moisture), 0.12f,
            "用户报告的相邻平原格不应继续出现超过合理范围的湿度断崖");
    }

    [TestCase(6101)]
    [TestCase(48621)]
    [TestCase(6123)]
    [TestCase(1109698167)]
    [TestCase(301473179)]
    [TestCase(1844258240)]
    [TestCase(414882)]
    [TestCase(20260806)]
    [TestCase(777)]
    [TestCase(778)]
    [TestCase(9123)]
    [TestCase(6102)]
    [TestCase(6103)]
    [TestCase(6104)]
    [TestCase(805)]
    [TestCase(5101)]
    [TestCase(5102)]
    [TestCase(9201)]
    [TestCase(123456789)]
    [TestCase(987654321)]
    [TestCase(1069496098)]
    [TestCase(1227008098)]
    [TestCase(137381070)]
    [TestCase(338262628)]
    [TestCase(1479788732)]
    [TestCase(2002288134)]
    [TestCase(1182752170)]
    [TestCase(20260806)]
    public void DefaultClimate_HasMeasurableDryAndWetLand(int seed)
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 128, height = 96, seed = seed });
        WorldCell[] land = map.cells.Where(cell =>
            cell.landform != LandformType.DeepWater &&
            cell.landform != LandformType.ShallowWater).ToArray();
        int dry = land.Count(cell => cell.moisture < 0.22f);
        int wet = land.Count(cell => cell.moisture >= 0.66f);
        int desert = land.Count(cell => cell.biome == BiomeType.Desert);
        int grassland = land.Count(cell => cell.biome == BiomeType.Grassland);
        int temperateForest = land.Count(cell => cell.biome == BiomeType.TemperateForest);
        int rainforest = land.Count(cell => cell.biome == BiomeType.Rainforest);
        int wetland = land.Count(cell => cell.biome == BiomeType.Wetland);
        int coast = land.Count(cell => cell.landform == LandformType.Coast);
        int moderate = land.Length - dry - wet;
        float dryPercentage = dry * 100f / land.Length;
        float moderatePercentage = moderate * 100f / land.Length;
        float wetPercentage = wet * 100f / land.Length;
        float desertPercentage = desert * 100f / land.Length;
        float grasslandPercentage = grassland * 100f / land.Length;
        float forestPercentage = (temperateForest + rainforest) * 100f / land.Length;
        float coastPercentage = coast * 100f / land.Length;
        int[] mountainDistance = DistanceFromMountains(map);
        int[] waterDistance = DistanceFromWater(map);
        int desertNearMountains = land.Count(cell =>
            cell.biome == BiomeType.Desert && mountainDistance[cell.index] <= 2);
        int desertNearWater = land.Count(cell =>
            cell.biome == BiomeType.Desert && waterDistance[cell.index] <= 5);
        int desertFarFromMountains = land.Count(cell =>
            cell.biome == BiomeType.Desert && mountainDistance[cell.index] >= 4);
        float nearMountainDesertPercentage = desert > 0
            ? desertNearMountains * 100f / desert
            : 0f;
        float nearWaterDesertPercentage = desert > 0
            ? desertNearWater * 100f / desert
            : 0f;
        double averageLandWaterDistance = land.Average(cell => waterDistance[cell.index]);
        double averageDesertWaterDistance = desert > 0
            ? land.Where(cell => cell.biome == BiomeType.Desert)
                .Average(cell => waterDistance[cell.index])
            : 0d;
        float farMountainDesertPercentage = desert > 0
            ? desertFarFromMountains * 100f / desert
            : 0f;
        int[] desertComponents = DesertComponentMountainContact(map, mountainDistance);
        float mountainTouchingComponentPercentage = desertComponents[0] > 0
            ? desertComponents[1] * 100f / desertComponents[0]
            : 0f;
        float maximumNeighborDifference = 0f;
        WorldCell maximumCell = null;
        WorldCell maximumNeighbor = null;
        foreach (WorldCell cell in land.Where(cell => cell.landform != LandformType.Mountain))
        {
            foreach (int neighborIndex in map.GetNeighborIndices(cell.index))
            {
                WorldCell neighbor = map.cells[neighborIndex];
                if (neighbor.landform == LandformType.DeepWater ||
                    neighbor.landform == LandformType.ShallowWater ||
                    neighbor.landform == LandformType.Mountain) continue;
                float difference = Math.Abs(cell.moisture - neighbor.moisture);
                if (difference <= maximumNeighborDifference) continue;
                maximumNeighborDifference = difference;
                maximumCell = cell;
                maximumNeighbor = neighbor;
            }
        }
        HashSet<int> riverCells = new HashSet<int>(map.rivers.SelectMany(segment =>
            new[] { segment.fromCellIndex, segment.toCellIndex }));

        TestContext.WriteLine($"seed={seed} land={land.Length} " +
                              $"dryLand%={dryPercentage:F2} " +
                              $"moderateLand%={moderatePercentage:F2} " +
                              $"wetLand%={wetPercentage:F2} " +
                              $"desertBiome%={desertPercentage:F2} " +
                              $"grasslandBiome%={grasslandPercentage:F2} " +
                              $"forestBiomes%={forestPercentage:F2} " +
                              $"rainforest={rainforest} wetland={wetland} " +
                              $"coastLandform%={coastPercentage:F2} " +
                              $"desertNearMountain%={nearMountainDesertPercentage:F2} " +
                              $"desertNearWater%={nearWaterDesertPercentage:F2} " +
                              $"meanLandWaterDistance={averageLandWaterDistance:F2} " +
                              $"meanDesertWaterDistance={averageDesertWaterDistance:F2} " +
                              $"desertFarFromMountain%={farMountainDesertPercentage:F2} " +
                              $"desertComponents={desertComponents[0]} " +
                              $"mountainTouchingComponents%={mountainTouchingComponentPercentage:F2} " +
                              $"maxNeighborDelta={maximumNeighborDifference:F3}");
        if (maximumCell != null && maximumNeighbor != null)
            TestContext.WriteLine($"maxPair={maximumCell.coord.col},{maximumCell.coord.row}" +
                                  $"({maximumCell.landform},{maximumCell.moisture:F3}," +
                                  $"river={riverCells.Contains(maximumCell.index)}) <-> " +
                                  $"{maximumNeighbor.coord.col},{maximumNeighbor.coord.row}" +
                                  $"({maximumNeighbor.landform},{maximumNeighbor.moisture:F3}," +
                                  $"river={riverCells.Contains(maximumNeighbor.index)})");
        Assert.That(dryPercentage, Is.InRange(1f, 30f),
            "默认地图应存在真正干燥的陆地，而不是全部停留在草原以上湿度");
        Assert.That(wetPercentage, Is.InRange(1f, 25f),
            "缩短水汽传播后仍应保留沿海或水体附近的湿润陆地");
        Assert.That(moderatePercentage, Is.InRange(55f, 92f),
            "中湿地带仍应是主要过渡空间，避免从湿润直接跳成大片荒漠");
        Assert.That(desertPercentage, Is.InRange(0f, 4.05f),
            "沙漠应保持少量可见，但不应重新形成大面积高对比沙色块");
        Assert.LessOrEqual(grasslandPercentage, 62f,
            "默认气候不应重新坍缩为绝大多数陆地都是同一种草原群系");
        Assert.GreaterOrEqual(forestPercentage, 8f,
            "中湿和湿润气候应形成可测量的森林群系，而不是全部回退为草原");
        Assert.AreEqual(0f, nearMountainDesertPercentage,
            "高对比沙漠与高山之间应保留至少两圈非沙漠过渡格");
        Assert.AreEqual(0f, nearWaterDesertPercentage,
            "高对比沙漠与湖海之间应保留至少五格非水体过渡空间");
        if (desert > 0)
            Assert.GreaterOrEqual(averageDesertWaterDistance,
                averageLandWaterDistance + 2d,
                "沙漠整体应明显偏向大陆纵深，而不是集中在刚越过沿水保护圈的位置");
        if (desert > 0)
            Assert.GreaterOrEqual(farMountainDesertPercentage, 10f,
                "存在沙漠时，应保留一部分距离高山至少四格的大陆内部或低频干旱沙漠");
        Assert.AreEqual(0f, mountainTouchingComponentPercentage,
            "高对比沙漠不应直接从高山边界开始，应保留至少一格干草原或丘陵过渡");
        Assert.LessOrEqual(maximumNeighborDifference, 0.18f,
            "普通相邻陆地不应形成肉眼明显的单格湿度断崖");
    }

    [TestCase(LandformType.Plain, 0.10f, 0.50f, 0.50f, BiomeType.Snowfield)]
    [TestCase(LandformType.Plain, 0.25f, 0.35f, 0.50f, BiomeType.Tundra)]
    [TestCase(LandformType.Plain, 0.25f, 0.64f, 0.50f, BiomeType.TemperateForest)]
    [TestCase(LandformType.Plain, 0.36f, 0.30f, 0.50f, BiomeType.Tundra)]
    [TestCase(LandformType.Plain, 0.45f, 0.75f, 0.52f, BiomeType.Wetland)]
    [TestCase(LandformType.Hill, 0.45f, 0.75f, 0.62f, BiomeType.TemperateForest)]
    [TestCase(LandformType.Plain, 0.60f, 0.30f, 0.50f, BiomeType.Grassland)]
    [TestCase(LandformType.Plain, 0.60f, 0.50f, 0.50f, BiomeType.TemperateForest)]
    [TestCase(LandformType.Plain, 0.76f, 0.12f, 0.50f, BiomeType.Desert)]
    [TestCase(LandformType.Plain, 0.76f, 0.66f, 0.50f, BiomeType.Rainforest)]
    [TestCase(LandformType.Mountain, 0.30f, 0.70f, 0.82f, BiomeType.Snowfield)]
    [TestCase(LandformType.Mountain, 0.60f, 0.30f, 0.82f, BiomeType.Alpine)]
    public void BiomeClassification_UsesTemperatureMoistureAndLandformMatrix(
        LandformType landform, float temperature, float moisture, float height,
        BiomeType expected)
    {
        var cell = new WorldCell
        {
            landform = landform,
            temperature = temperature,
            moisture = moisture,
            height = height
        };
        typeof(TerrainGenerator).GetMethod("ClassifyBiome",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { cell });
        Assert.AreEqual(expected, cell.biome);
    }

    [TestCase(0.16f, 0.95f, BiomeType.Snowfield)]
    [TestCase(0.25f, 0.57f, BiomeType.Tundra)]
    [TestCase(0.25f, 0.58f, BiomeType.TemperateForest)]
    [TestCase(0.36f, 0.41f, BiomeType.Tundra)]
    [TestCase(0.36f, 0.42f, BiomeType.TemperateForest)]
    [TestCase(0.60f, 0.17f, BiomeType.Desert)]
    [TestCase(0.60f, 0.18f, BiomeType.Grassland)]
    [TestCase(0.76f, 0.19f, BiomeType.Desert)]
    [TestCase(0.76f, 0.20f, BiomeType.Grassland)]
    [TestCase(0.76f, 0.59f, BiomeType.TemperateForest)]
    [TestCase(0.76f, 0.60f, BiomeType.Rainforest)]
    public void BiomeMatrix_HasExplicitTemperatureAndMoistureBoundaries(
        float temperature, float moisture, BiomeType expected)
    {
        WorldCell cell = new WorldCell
        {
            landform = LandformType.Hill,
            temperature = temperature,
            moisture = moisture,
            height = 0.55f
        };
        TerrainGenerator.ClassifyBiome(cell);
        Assert.AreEqual(expected, cell.biome);
    }

    [TestCase(20260806)]
    [TestCase(1486022881)]
    public void FinalClimate_HasMeasurableColdInlandWithoutIncreasingMapSize(int seed)
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 128, height = 96, seed = seed });
        WorldCell[] inland = map.cells.Where(cell =>
            cell.landform != LandformType.DeepWater &&
            cell.landform != LandformType.ShallowWater &&
            cell.landform != LandformType.Coast).ToArray();
        int coldClimate = inland.Count(cell => cell.temperature < 0.32f);
        int coldBiomes = inland.Count(cell =>
            cell.biome == BiomeType.Tundra || cell.biome == BiomeType.Snowfield);
        float coldClimatePercentage = coldClimate * 100f / inland.Length;
        float coldBiomePercentage = coldBiomes * 100f / inland.Length;
        TestContext.WriteLine($"seed={seed} inland={inland.Length} " +
                              $"coldClimate%={coldClimatePercentage:F2} " +
                              $"coldBiomes%={coldBiomePercentage:F2}");
        Assert.GreaterOrEqual(coldClimatePercentage, 3f,
            "归一化纬度必须在128x96地图上产生可测量寒带，不能依赖扩大地图");
        Assert.GreaterOrEqual(coldBiomePercentage, 2f,
            "存在中高纬内陆的固定验收种子应形成实际苔原或雪原");
    }

    private static int LargestConnectedMountainComponent(WorldMap map)
    {
        bool[] visited = new bool[map.cells.Length];
        Queue<int> pending = new Queue<int>();
        int largest = 0;
        for (int start = 0; start < map.cells.Length; start++)
        {
            if (visited[start] || map.cells[start].landform != LandformType.Mountain) continue;
            visited[start] = true;
            pending.Enqueue(start);
            int size = 0;
            while (pending.Count > 0)
            {
                int current = pending.Dequeue();
                size++;
                foreach (int neighbor in map.GetNeighborIndices(current))
                {
                    if (visited[neighbor] || map.cells[neighbor].landform != LandformType.Mountain) continue;
                    visited[neighbor] = true;
                    pending.Enqueue(neighbor);
                }
            }
            largest = Math.Max(largest, size);
        }
        return largest;
    }

    private static int[] DistanceFromMountains(WorldMap map)
    {
        int[] distance = Enumerable.Repeat(int.MaxValue, map.cells.Length).ToArray();
        Queue<int> pending = new Queue<int>();
        foreach (WorldCell cell in map.cells)
        {
            if (cell.landform != LandformType.Mountain) continue;
            distance[cell.index] = 0;
            pending.Enqueue(cell.index);
        }
        while (pending.Count > 0)
        {
            int current = pending.Dequeue();
            foreach (int neighbor in map.GetNeighborIndices(current))
            {
                if (distance[neighbor] <= distance[current] + 1) continue;
                distance[neighbor] = distance[current] + 1;
                pending.Enqueue(neighbor);
            }
        }
        return distance;
    }

    private static int[] DistanceFromWater(WorldMap map)
    {
        int[] distance = Enumerable.Repeat(int.MaxValue, map.cells.Length).ToArray();
        Queue<int> pending = new Queue<int>();
        foreach (WorldCell cell in map.cells)
        {
            if (cell.landform != LandformType.DeepWater &&
                cell.landform != LandformType.ShallowWater) continue;
            distance[cell.index] = 0;
            pending.Enqueue(cell.index);
        }
        while (pending.Count > 0)
        {
            int current = pending.Dequeue();
            foreach (int neighbor in map.GetNeighborIndices(current))
            {
                if (distance[neighbor] <= distance[current] + 1) continue;
                distance[neighbor] = distance[current] + 1;
                pending.Enqueue(neighbor);
            }
        }
        return distance;
    }

    private static int[] DesertComponentMountainContact(WorldMap map, int[] mountainDistance)
    {
        bool[] visited = new bool[map.cells.Length];
        Queue<int> pending = new Queue<int>();
        int componentCount = 0;
        int touchingMountainCount = 0;
        for (int start = 0; start < map.cells.Length; start++)
        {
            if (visited[start] || map.cells[start].biome != BiomeType.Desert) continue;
            visited[start] = true;
            pending.Enqueue(start);
            componentCount++;
            bool touchesMountain = false;
            while (pending.Count > 0)
            {
                int current = pending.Dequeue();
                if (mountainDistance[current] <= 1) touchesMountain = true;
                foreach (int neighbor in map.GetNeighborIndices(current))
                {
                    if (visited[neighbor] || map.cells[neighbor].biome != BiomeType.Desert) continue;
                    visited[neighbor] = true;
                    pending.Enqueue(neighbor);
                }
            }
            if (touchesMountain) touchingMountainCount++;
        }
        return new[] { componentCount, touchingMountainCount };
    }

    private static int CountDenseMountainCore(WorldMap map)
    {
        int dense = 0;
        foreach (WorldCell cell in map.cells)
        {
            if (cell.landform != LandformType.Mountain) continue;
            int[] neighbors = map.GetNeighborIndices(cell.index).ToArray();
            if (neighbors.Length == 6 && neighbors.All(index =>
                    map.cells[index].landform == LandformType.Mountain))
                dense++;
        }
        return dense;
    }

    private static int MaximumMountainThickness(WorldMap map)
    {
        int[] depth = new int[map.cells.Length];
        Queue<int> pending = new Queue<int>();
        foreach (WorldCell cell in map.cells)
        {
            if (cell.landform != LandformType.Mountain) continue;
            int[] neighbors = map.GetNeighborIndices(cell.index).ToArray();
            bool boundary = neighbors.Length < 6 || neighbors.Any(index =>
                map.cells[index].landform != LandformType.Mountain);
            if (!boundary) continue;
            depth[cell.index] = 1;
            pending.Enqueue(cell.index);
        }

        int maximumDepth = 0;
        while (pending.Count > 0)
        {
            int current = pending.Dequeue();
            maximumDepth = Math.Max(maximumDepth, depth[current]);
            foreach (int neighbor in map.GetNeighborIndices(current))
            {
                if (depth[neighbor] != 0 ||
                    map.cells[neighbor].landform != LandformType.Mountain) continue;
                depth[neighbor] = depth[current] + 1;
                pending.Enqueue(neighbor);
            }
        }
        return maximumDepth == 0 ? 0 : maximumDepth * 2 - 1;
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
    public void GenerationBenchmark_DefaultSeedSetProducesCompleteDeterministicDiagnostics()
    {
        Assert.AreEqual(12, WorldGenerationBenchmark.DefaultSeeds.Distinct().Count());
        foreach (int seed in WorldGenerationBenchmark.DefaultSeeds)
        {
            MapGenerationSettings settings = new MapGenerationSettings
                { width = 64, height = 48, seed = seed };
            WorldMap first = WorldGenerator.Generate(settings);
            WorldMap second = WorldGenerator.Generate(settings);
            WorldGenerationBenchmarkSnapshot a = WorldGenerationBenchmark.Capture(first);
            WorldGenerationBenchmarkSnapshot b = WorldGenerationBenchmark.Capture(second);

            Assert.AreEqual(a.effectiveSeed, b.effectiveSeed, $"seed {seed}");
            Assert.AreEqual(a.landCount, b.landCount, $"seed {seed}");
            Assert.AreEqual(a.riverSegmentCount, b.riverSegmentCount, $"seed {seed}");
            Assert.AreEqual(a.riverSourceCount, b.riverSourceCount, $"seed {seed}");
            Assert.AreEqual(a.meanTemperature, b.meanTemperature, 0.000001f, $"seed {seed}");
            Assert.AreEqual(a.meanMoisture, b.meanMoisture, 0.000001f, $"seed {seed}");
            Assert.AreEqual(a.meanRainfall, b.meanRainfall, 0.000001f, $"seed {seed}");
            Assert.AreEqual(a.maximumFlow, b.maximumFlow, 0.000001f, $"seed {seed}");
            CollectionAssert.AreEquivalent(a.biomeCounts, b.biomeCounts, $"seed {seed}");
            Assert.AreEqual(first.cells.Length, a.biomeCounts.Values.Sum(), $"seed {seed}");
            Assert.GreaterOrEqual(a.maximumFlow, 1f, $"seed {seed}");
            TestContext.WriteLine(
                $"seed={seed} effective={a.effectiveSeed} land={a.landCount}/{a.cellCount} " +
                $"rivers={a.riverSegmentCount} sources={a.riverSourceCount} " +
                $"tempMean={a.meanTemperature:F4} moistureMean={a.meanMoisture:F4} " +
                $"rainMean={a.meanRainfall:F4} freshDistanceMean={a.meanLandFreshWaterDistance:F2} " +
                $"maxFlow={a.maximumFlow:F0}");
        }
    }

    [Test]
    public void GenerationDiagnostics_AreRuntimeOnlyAndDoNotChangeWorldSerialization()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 64, height = 48, seed = 48621 });
        string serialized = JsonConvert.SerializeObject(map);
        Assert.IsTrue(WorldGenerationDiagnosticsStore.TryGet(map,
            out WorldGenerationDiagnostics diagnostics));
        Assert.AreEqual(map.cells.Length, diagnostics.rainfall.Length);
        Assert.AreEqual(map.cells.Length, diagnostics.transportedMoisture.Length);
        Assert.AreEqual(map.cells.Length, diagnostics.drainageParent.Length);
        Assert.AreEqual(map.cells.Length, diagnostics.filledHeight.Length);
        Assert.AreEqual(map.cells.Length, diagnostics.runoffInput.Length);
        Assert.AreEqual(map.cells.Length, diagnostics.accumulatedFlow.Length);
        Assert.AreEqual(map.cells.Length, diagnostics.freshWaterDistance.Length);
        Assert.AreEqual(map.cells.Length, diagnostics.finalMoisture.Length);
        StringAssert.DoesNotContain("rainfall", serialized);
        StringAssert.DoesNotContain("drainageParent", serialized);
        StringAssert.DoesNotContain("runoffInput", serialized);
        StringAssert.DoesNotContain("freshWaterDistance", serialized);
    }

    [Test]
    public void ReverseFlowAccumulation_UsesRainfallWeightedRunoffInput()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 96, height = 72, seed = 20260806 });
        Assert.IsTrue(WorldGenerationDiagnosticsStore.TryGet(map,
            out WorldGenerationDiagnostics diagnostics));

        WorldCell[] rainyLand = map.cells
            .Where(cell => cell.landform != LandformType.DeepWater &&
                           cell.landform != LandformType.ShallowWater &&
                           diagnostics.rainfall[cell.index] > 0.0001f)
            .OrderBy(cell => diagnostics.rainfall[cell.index])
            .ToArray();
        Assert.Greater(rainyLand.Length, 1, "至少需要两个有降雨的陆地格验证径流权重");
        WorldCell driest = rainyLand.First();
        WorldCell wettest = rainyLand.Last();
        Assert.Greater(diagnostics.runoffInput[wettest.index],
            diagnostics.runoffInput[driest.index],
            "更高降雨量必须产生更高的初始径流，不能再给每格固定 1 单位流量");
        Assert.GreaterOrEqual(diagnostics.accumulatedFlow[wettest.index],
            diagnostics.runoffInput[wettest.index]);

        float meanLandRunoff = map.cells
            .Where(cell => cell.landform != LandformType.DeepWater &&
                           cell.landform != LandformType.ShallowWater)
            .Average(cell => diagnostics.runoffInput[cell.index]);
        Assert.AreEqual(2f, meanLandRunoff, 0.0001f,
            "径流标尺应与既有汇水阈值保持匹配");
    }

    [Test]
    public void FreshWaterDistance_DoesNotTreatOceanConnectedWaterAsFreshWater()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 64, height = 48, seed = 8675309 });
        Assert.IsTrue(WorldGenerationDiagnosticsStore.TryGet(map,
            out WorldGenerationDiagnostics diagnostics));
        WorldCell boundaryOcean = map.cells.First(cell =>
            (cell.coord.col == 0 || cell.coord.row == 0 || cell.coord.col == map.width - 1 ||
             cell.coord.row == map.height - 1) &&
            (cell.landform == LandformType.DeepWater || cell.landform == LandformType.ShallowWater));
        Assert.AreNotEqual(0, diagnostics.freshWaterDistance[boundaryOcean.index],
            "与地图边缘连通的海水不能作为淡水源");
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
