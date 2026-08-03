using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cultivation4X.WorldMap;
using Newtonsoft.Json;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class WorldMapRegionTests
{
    [Test]
    public void RegionGeneration_IsDeterministicUniqueConnectedAndComplete()
    {
        var settings = new MapGenerationSettings { width = 64, height = 48, seed = 6101 };
        WorldMap first = WorldGenerator.Generate(settings);
        WorldMap second = WorldGenerator.Generate(settings);

        Assert.AreEqual(JsonConvert.SerializeObject(first.regions), JsonConvert.SerializeObject(second.regions));
        Assert.AreEqual(first.cells.Length, first.regions.Sum(region => region.cellIndices.Count));
        Assert.AreEqual(first.cells.Length, first.regions.SelectMany(region => region.cellIndices).Distinct().Count());
        Assert.AreEqual(first.regions.Count, first.regions.Select(region => region.regionId).Distinct().Count());
        Assert.AreEqual(first.regions.Count, first.regions.Select(region => region.regionName).Distinct().Count());
        Assert.IsTrue(first.regions.All(region => Enum.IsDefined(typeof(MapRegionType), region.regionType)));
        Assert.IsTrue(first.regions.All(region => IsConnected(first, region.cellIndices)));
        Assert.IsTrue(first.cells.All(cell => !string.IsNullOrWhiteSpace(cell.regionId) &&
            Enum.IsDefined(typeof(MapInternalPositionTag), cell.internalPositionTag)));

        List<MapRegionData> ordinary = first.regions.Where(region => region.regionType != MapRegionType.OpenWater).ToList();
        Assert.Greater(ordinary.Count(region => region.cellIndices.Count >= 6 && region.cellIndices.Count <= 24), ordinary.Count / 2);
        Assert.IsTrue(ordinary.All(region => region.cellIndices.Count >= 4 && region.cellIndices.Count <= 32));
        Assert.IsTrue(first.regions.Where(region => region.regionType == MapRegionType.OpenWater)
            .All(region => region.cellIndices.Count >= 4 && region.cellIndices.Count <= 110));
    }

    [TestCase(32, 24, 6121)]
    [TestCase(64, 48, 6122)]
    [TestCase(128, 96, 6123)]
    public void RegionSizes_StayWithinApprovedBoundsAcrossSeeds(int width, int height, int seed)
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = width, height = height, seed = seed });
        List<MapRegionData> ordinary = map.regions.Where(region => region.regionType != MapRegionType.OpenWater).ToList();
        Assert.IsTrue(ordinary.All(region => region.cellIndices.Count >= 4 && region.cellIndices.Count <= 32));
        Assert.Greater(ordinary.Count(region => region.cellIndices.Count >= 6 && region.cellIndices.Count <= 24), ordinary.Count / 2);
        Assert.IsTrue(map.regions.Where(region => region.regionType == MapRegionType.OpenWater)
            .All(region => region.cellIndices.Count >= 4 && region.cellIndices.Count <= 110));
    }

    [Test]
    public void TerrainAndClimate_ProduceSmallPatchyLayout()
    {
        var cases = new[]
        {
            new { width = 64, height = 48, seed = 6101 },
            new { width = 128, height = 96, seed = 48621 },
            new { width = 128, height = 96, seed = 6123 }
        };
        foreach (var item in cases)
        {
            WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            {
                width = item.width, height = item.height, seed = item.seed
            });
            int landformPatches = CountConnectedComponents(map, cell => (int)cell.landform);
            int biomePatches = CountConnectedComponents(map, cell => (int)cell.biome);
            int ordinaryRegions = map.regions.Count(region =>
                region.regionType != MapRegionType.OpenWater);
            TestContext.WriteLine(
                $"Seed {item.seed}: landformPatches={landformPatches}, biomePatches={biomePatches}, ordinaryRegions={ordinaryRegions}");

            Assert.GreaterOrEqual(landformPatches, 12, "地貌应呈现多个小块而不是单一大陆块");
            Assert.GreaterOrEqual(biomePatches, 20, "群系应呈现多个小块而不是大面积同质色块");
            Assert.GreaterOrEqual(ordinaryRegions, 50, "普通区域数量应足以表达小块地貌");
        }
    }

    [Test]
    public void RegionTags_ReflectStaticTerrainAndRegionShape()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 64, height = 48, seed = 6102 });
        Assert.IsTrue(map.cells.Where(cell => cell.landform == LandformType.Mountain)
            .Any(cell => cell.internalPositionTag == MapInternalPositionTag.Ridge ||
                         cell.internalPositionTag == MapInternalPositionTag.Mountainside ||
                         cell.internalPositionTag == MapInternalPositionTag.MountainFoot ||
                         cell.internalPositionTag == MapInternalPositionTag.Hilltop ||
                         cell.internalPositionTag == MapInternalPositionTag.HillFoot));
        Assert.IsTrue(map.regions.Where(region => region.regionType == MapRegionType.Forest)
            .SelectMany(region => region.cellIndices).Any(index =>
                map.cells[index].internalPositionTag == MapInternalPositionTag.ForestEdge ||
                map.cells[index].internalPositionTag == MapInternalPositionTag.DeepForest));
        Assert.IsTrue(map.regions.Where(region => region.regionType == MapRegionType.OpenWater || region.regionType == MapRegionType.Lake)
            .SelectMany(region => region.cellIndices).Any(index =>
                map.cells[index].internalPositionTag == MapInternalPositionTag.Coastline ||
                map.cells[index].internalPositionTag == MapInternalPositionTag.DeepWater ||
                map.cells[index].internalPositionTag == MapInternalPositionTag.Lakeshore ||
                map.cells[index].internalPositionTag == MapInternalPositionTag.Shallows));
    }

    [Test]
    public void HandcraftedTerrain_ProducesLakeForestAndMountainSemantics()
    {
        WorldMap map = CreatePlainMap(12, 12, 6106);
        int[] lake = { 53, 54, 55, 65, 66, 67, 78 };
        foreach (int index in lake) { map.cells[index].landform = LandformType.ShallowWater; map.cells[index].biome = BiomeType.Ocean; }
        int[] forest = { 13, 14, 15, 16, 25, 26, 27, 28 };
        foreach (int index in forest) map.cells[index].biome = BiomeType.TemperateForest;
        int[] mountain = { 97, 98, 99, 100, 101, 109, 110, 111, 112, 113, 121, 122 };
        foreach (int index in mountain) { map.cells[index].landform = LandformType.Mountain; map.cells[index].biome = BiomeType.Alpine; map.cells[index].height = 0.85f; }

        WorldMapRegionRules.Assign(map);

        Assert.IsTrue(lake.All(index => map.regions.Single(region => region.regionId == map.cells[index].regionId).regionType == MapRegionType.Lake));
        Assert.IsTrue(forest.All(index => map.regions.Single(region => region.regionId == map.cells[index].regionId).regionType == MapRegionType.Forest));
        Assert.IsTrue(mountain.All(index => map.regions.Single(region => region.regionId == map.cells[index].regionId).regionType == MapRegionType.MountainRange));
        Assert.IsTrue(lake.All(index => map.cells[index].internalPositionTag == MapInternalPositionTag.Lakeshore ||
                                        map.cells[index].internalPositionTag == MapInternalPositionTag.Shallows));
    }

    [Test]
    public void RegionSnapshot_RoundTripsAndDeterministicRebuildMatches()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 6103 });
        WorldMap restored = JsonConvert.DeserializeObject<WorldMap>(JsonConvert.SerializeObject(map));
        MapRegionBuildResult rebuilt = WorldMapRegionRules.Build(restored);
        Assert.AreEqual(JsonConvert.SerializeObject(map.regions), JsonConvert.SerializeObject(restored.regions));
        Assert.IsTrue(restored.cells.Select((cell, index) => cell.regionId == rebuilt.regionIds[index] &&
            cell.internalPositionTag == rebuilt.internalPositionTags[index]).All(value => value));
    }

    [Test]
    public void ExplorationSummary_IncludesRegionAndPositionWithoutChangingRewardFormula()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 6104 });
        var progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        int target = map.cells.First(cell => cell.landform != LandformType.DeepWater).index;
        var context = new MapMissionContext { actionType = MapActionType.Explore, targetCellIndex = target };
        Reward before = WorldMapContentRules.CreateReward(map, context);
        Assert.IsTrue(WorldMapContentRules.CompleteSuccessfulAction(map, progress, context,
            MissionResultTier.Qualified, 1, out string summary));
        MapRegionData region = map.regions.Single(item => item.regionId == map.cells[target].regionId);
        StringAssert.Contains(region.regionName, summary);
        StringAssert.Contains(WorldMapRegionRules.PositionLabel(map.cells[target].internalPositionTag), summary);
        Reward after = WorldMapContentRules.CreateReward(map, context);
        Assert.AreEqual(JsonConvert.SerializeObject(before), JsonConvert.SerializeObject(after));
    }

    [TestCase(MapSiteType.Village, MapRegionType.Valley)]
    [TestCase(MapSiteType.SpiritSpring, MapRegionType.Swamp)]
    [TestCase(MapSiteType.SpiritMine, MapRegionType.MountainRange)]
    [TestCase(MapSiteType.CaveResidence, MapRegionType.SmallHill)]
    [TestCase(MapSiteType.BeastLair, MapRegionType.Forest)]
    [TestCase(MapSiteType.Ruin, MapRegionType.Desert)]
    public void ContentSuitability_AddsEveryApprovedRegionPreference(MapSiteType siteType, MapRegionType preferredType)
    {
        WorldMap map = CreatePlainMap(8, 8, 6105);
        WorldCell preferred = map.cells[0];
        WorldCell neutral = map.cells[1];
        preferred.regionId = "preferred"; neutral.regionId = "neutral";
        map.regions = new List<MapRegionData>
        {
            new MapRegionData { regionId = "preferred", regionType = preferredType },
            new MapRegionData { regionId = "neutral", regionType = MapRegionType.OpenWater }
        };
        preferred.internalPositionTag = MapInternalPositionTag.Ridge;
        neutral.internalPositionTag = MapInternalPositionTag.OpenPlain;
        preferred.totalAura = neutral.totalAura;
        preferred.elementalAura = neutral.elementalAura;
        MethodInfo suitability = typeof(WorldMapContentRules).GetMethod("Suitability", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(suitability);
        int preferredScore = (int)suitability.Invoke(null, new object[] { map, preferred, siteType, QiRevivalStage.Early });
        int neutralScore = (int)suitability.Invoke(null, new object[] { map, neutral, siteType, QiRevivalStage.Early });
        Assert.Greater(preferredScore, neutralScore);
    }

    [Test]
    public void ControlledMap_DerivesSpecificMountainForestAndLakeTags()
    {
        WorldMap map = CreatePlainMap(7, 7, 6110);
        var region = new MapRegionData { regionId = "controlled", centerCellIndex = 24,
            cellIndices = Enumerable.Range(0, 49).ToList(), averageAura = 0.2f };

        region.regionType = MapRegionType.MountainRange;
        map.cells[0].height = 0.4f;
        Assert.AreEqual(MapInternalPositionTag.MountainFoot, DerivePositionTag(map, region, 0));
        Assert.AreEqual(MapInternalPositionTag.Mountainside, DerivePositionTag(map, region, 24));
        map.cells[24].height = 0.56f;
        Assert.AreEqual(MapInternalPositionTag.Ridge, DerivePositionTag(map, region, 24));
        map.cells[24].height = 0.8f;
        Assert.AreEqual(MapInternalPositionTag.Summit, DerivePositionTag(map, region, 24));
        map.cells[24].height = 0.3f;
        Assert.AreEqual(MapInternalPositionTag.MountainPass, DerivePositionTag(map, region, 24));
        map.cells[24].height = 0.5f; map.cells[17].height = 0.9f; map.cells[23].height = 0.2f;
        Assert.AreEqual(MapInternalPositionTag.Cliff, DerivePositionTag(map, region, 24));
        map.cells[17].height = map.cells[23].height = 0.5f;
        map.spiritVeins.Add(new SpiritVein { id = "cave", pathCellIndices = new List<int> { 0 } });
        Assert.AreEqual(MapInternalPositionTag.CaveMouth, DerivePositionTag(map, region, 0));

        map.spiritVeins.Clear(); region.regionType = MapRegionType.Forest;
        foreach (WorldCell cell in map.cells) { cell.biome = BiomeType.Grassland; cell.landform = LandformType.Plain; cell.moisture = 0.5f; cell.totalAura = 0.2f; }
        Assert.AreEqual(MapInternalPositionTag.ForestEdge, DerivePositionTag(map, region, 0));
        Assert.AreEqual(MapInternalPositionTag.DeepForest, DerivePositionTag(map, region, 16));
        map.cells[24].moisture = 0.2f;
        Assert.AreEqual(MapInternalPositionTag.ForestClearing, DerivePositionTag(map, region, 24));
        map.cells[24].moisture = 0.7f; map.cells[24].totalAura = 0.4f;
        Assert.AreEqual(MapInternalPositionTag.AncientGrove, DerivePositionTag(map, region, 24));
        map.cells[24].landform = LandformType.Mountain;
        Assert.AreEqual(MapInternalPositionTag.BeastTrail, DerivePositionTag(map, region, 24));
        map.cells[24].landform = LandformType.Plain; map.cells[0].moisture = 0.7f; map.cells[1].height = 0.65f;
        Assert.AreEqual(MapInternalPositionTag.HerbSlope, DerivePositionTag(map, region, 0));

        region.regionType = MapRegionType.Lake;
        foreach (WorldCell cell in map.cells) { cell.landform = LandformType.DeepWater; cell.moisture = 0.5f; cell.height = 0.4f; }
        Assert.AreEqual(MapInternalPositionTag.Lakeshore, DerivePositionTag(map, region, 0));
        map.cells[0].landform = LandformType.ShallowWater;
        Assert.AreEqual(MapInternalPositionTag.Shallows, DerivePositionTag(map, region, 0));
        Assert.AreEqual(MapInternalPositionTag.LakeCenter, DerivePositionTag(map, region, 24));
        map.cells[0].landform = LandformType.DeepWater; map.cells[0].moisture = 0.8f;
        Assert.AreEqual(MapInternalPositionTag.ReedShore, DerivePositionTag(map, region, 0));
        map.cells[0].moisture = 0.5f; map.rivers.Add(new RiverSegment { fromCellIndex = 1, toCellIndex = 0 });
        Assert.AreEqual(MapInternalPositionTag.WaterInlet, DerivePositionTag(map, region, 0));
        map.rivers.Clear(); map.rivers.Add(new RiverSegment { fromCellIndex = 0, toCellIndex = 1 });
        Assert.AreEqual(MapInternalPositionTag.WaterOutlet, DerivePositionTag(map, region, 0));
    }

    [TestCase(17.999f, WorldMapZoomLevel.Far)]
    [TestCase(18f, WorldMapZoomLevel.Mid)]
    [TestCase(43.999f, WorldMapZoomLevel.Mid)]
    [TestCase(44f, WorldMapZoomLevel.Near)]
    public void RegionPresentation_ZoomThresholdsUseProjectedHexDiameter(float diameter,
        WorldMapZoomLevel expected)
    {
        Assert.AreEqual(expected, WorldMapRegionPresentationPolicy.GetZoomLevel(diameter));
    }

    [Test]
    public void FarPresentation_HidesHintsButRetainsConfirmedStrategicMarkers()
    {
        Assert.IsFalse(WorldMapRegionPresentationPolicy.ShowOrdinaryHint(WorldMapZoomLevel.Far));
        Assert.IsFalse(WorldMapRegionPresentationPolicy.ShowMarker(WorldMapMarkerKind.ContentHint,
            WorldMapZoomLevel.Far));
        Assert.IsFalse(WorldMapRegionPresentationPolicy.ShowMarker(WorldMapMarkerKind.EnvironmentBeastTracks,
            WorldMapZoomLevel.Far));
        Assert.IsTrue(WorldMapRegionPresentationPolicy.ShowMarker(WorldMapMarkerKind.FactionSeat,
            WorldMapZoomLevel.Far));
        Assert.IsTrue(WorldMapRegionPresentationPolicy.ShowMarker(WorldMapMarkerKind.SpiritSpring,
            WorldMapZoomLevel.Far));
        Assert.IsTrue(WorldMapRegionPresentationPolicy.ShowMarker(WorldMapMarkerKind.PointOfInterest,
            WorldMapZoomLevel.Far));
        Assert.IsFalse(WorldMapRegionPresentationPolicy.DebugOverlayEnabledByDefault);
    }

    [Test]
    public void RegionLabels_AreDeterministicCollisionFilteredAndBounded()
    {
        List<WorldMapRegionLabelCandidate> candidates = Enumerable.Range(0, 60).Select(index =>
            new WorldMapRegionLabelCandidate
            {
                regionId = "region-" + index.ToString("D2"), cellIndex = index,
                displayPriority = index, isKnown = true, isInViewport = true,
                screenX = index * 1000f, screenY = 0f, width = 20f, height = 10f
            }).ToList();

        List<WorldMapRegionLabelCandidate> first = WorldMapRegionPresentationPolicy.SelectRegionLabels(
            candidates, WorldMapZoomLevel.Far);
        List<WorldMapRegionLabelCandidate> second = WorldMapRegionPresentationPolicy.SelectRegionLabels(
            candidates.AsEnumerable().Reverse(), WorldMapZoomLevel.Far);
        Assert.AreEqual(WorldMapRegionPresentationPolicy.FarRegionLabelLimit, first.Count);
        CollectionAssert.AreEqual(first.Select(item => item.regionId), second.Select(item => item.regionId));

        foreach (WorldMapRegionLabelCandidate candidate in candidates)
        {
            candidate.screenX = 100f;
            candidate.screenY = 100f;
        }
        Assert.AreEqual(1, WorldMapRegionPresentationPolicy.SelectRegionLabels(
            candidates, WorldMapZoomLevel.Far).Count);
        Assert.IsEmpty(WorldMapRegionPresentationPolicy.SelectRegionLabels(
            candidates, WorldMapZoomLevel.Near));
    }

    [Test]
    public void MidLabels_OnlyUseSelectedKnownOrHighPriorityCandidates()
    {
        var lowUnknown = new WorldMapRegionLabelCandidate
            { regionId = "low", isInViewport = true, displayPriority = 54, width = 1f, height = 1f };
        var highUnknown = new WorldMapRegionLabelCandidate
            { regionId = "high", isInViewport = true, displayPriority = 55, screenX = 10f, width = 1f, height = 1f };
        var known = new WorldMapRegionLabelCandidate
            { regionId = "known", isInViewport = true, isKnown = true, screenX = 20f, width = 1f, height = 1f };
        var selected = new WorldMapRegionLabelCandidate
            { regionId = "selected", isInViewport = true, isSelected = true, screenX = 30f, width = 1f, height = 1f };

        List<WorldMapRegionLabelCandidate> result = WorldMapRegionPresentationPolicy.SelectRegionLabels(
            new[] { lowUnknown, highUnknown, known, selected }, WorldMapZoomLevel.Mid);
        CollectionAssert.DoesNotContain(result.Select(item => item.regionId).ToList(), "low");
        CollectionAssert.Contains(result.Select(item => item.regionId).ToList(), "high");
        CollectionAssert.Contains(result.Select(item => item.regionId).ToList(), "known");
        Assert.AreEqual("selected", result[0].regionId);
        Assert.LessOrEqual(result.Count, WorldMapRegionPresentationPolicy.MidRegionLabelLimit);
    }

    [Test]
    public void NearDetails_RespectKnowledgeInfluenceSamplingAndSelectionLimit()
    {
        Assert.IsTrue(WorldMapRegionPresentationPolicy.ShowNearDetail(6200, 1,
            KnowledgeState.Known, InfluenceLevel.Core));
        Assert.IsFalse(WorldMapRegionPresentationPolicy.ShowNearDetail(6200, 1,
            KnowledgeState.Unknown, InfluenceLevel.Core));
        Assert.IsFalse(WorldMapRegionPresentationPolicy.ShowNearDetail(6200, 1,
            KnowledgeState.Known, InfluenceLevel.Outer));

        List<int> sampled = Enumerable.Range(0, 100).Where(index =>
            WorldMapRegionPresentationPolicy.ShowNearDetail(6200, index,
                KnowledgeState.Known, InfluenceLevel.Influence)).ToList();
        Assert.That(sampled.Count, Is.InRange(35, 65));
        CollectionAssert.AreEqual(sampled, Enumerable.Range(0, 100).Where(index =>
            WorldMapRegionPresentationPolicy.ShowNearDetail(6200, index,
                KnowledgeState.Known, InfluenceLevel.Influence)).ToList());

        List<int> selected = WorldMapRegionPresentationPolicy.SelectNearDetailCells(
            Enumerable.Range(0, 40), 31);
        Assert.AreEqual(WorldMapRegionPresentationPolicy.NearDetailLabelLimit, selected.Count);
        Assert.AreEqual(31, selected[0]);
    }

    [Test]
    public void LabelSafeArea_ExcludesRightTopBottomAndDoesNotConsumeRegionLimit()
    {
        WorldMapLabelSafeArea safe = WorldMapRegionPresentationPolicy.CreateGameplaySafeArea(1000f, 800f);
        Assert.IsTrue(safe.Contains(300f, 400f, 80f, 24f));
        Assert.IsFalse(safe.Contains(850f, 400f, 80f, 24f));
        Assert.IsFalse(safe.Contains(300f, 760f, 80f, 24f));
        Assert.IsFalse(safe.Contains(300f, 30f, 80f, 24f));

        var unsafeHigh = new WorldMapRegionLabelCandidate
        {
            regionId = "unsafe", displayPriority = 999, isKnown = true, isInViewport = true,
            isInSafeArea = false, width = 20f, height = 10f
        };
        var safeLow = new WorldMapRegionLabelCandidate
        {
            regionId = "safe", displayPriority = 1, isKnown = true, isInViewport = true,
            isInSafeArea = true, screenX = 100f, width = 20f, height = 10f
        };
        List<WorldMapRegionLabelCandidate> result = WorldMapRegionPresentationPolicy.SelectRegionLabels(
            new[] { unsafeHigh, safeLow }, WorldMapZoomLevel.Mid);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("safe", result[0].regionId);
    }

    [Test]
    public void LabelScreenSize_UsesPreferredLocalSizeAndCanvasScale()
    {
        Vector2 size = WorldMapRegionPresentationPolicy.LabelScreenSize(
            new Vector2(104f, 23f), 2f, 12f, 8f, 72f, 28f);
        Assert.AreEqual((104f + 12f) * 2f, size.x, 0.01f);
        Assert.AreEqual((23f + 8f) * 2f, size.y, 0.01f);

        Vector2 minimum = WorldMapRegionPresentationPolicy.LabelScreenSize(
            new Vector2(1f, 1f), 2f, 0f, 0f, 80f, 30f);
        Assert.AreEqual(80f, minimum.x, 0.01f);
        Assert.AreEqual(30f, minimum.y, 0.01f);
    }

    [Test]
    public void NearDetailLabels_FilterCollisionAtFortyFourPixelsAndPrioritizeSelection()
    {
        var ordinary = new WorldMapDetailLabelCandidate
        {
            cellIndex = 1, influenceLevel = InfluenceLevel.Core, isInViewport = true,
            screenX = 100f, screenY = 100f, width = 76f, height = 22f
        };
        var adjacent = new WorldMapDetailLabelCandidate
        {
            cellIndex = 2, influenceLevel = InfluenceLevel.Core, isInViewport = true,
            screenX = 144f, screenY = 100f, width = 76f, height = 22f
        };
        var selected = new WorldMapDetailLabelCandidate
        {
            cellIndex = 3, influenceLevel = InfluenceLevel.Influence, isSelected = true,
            isInViewport = true, screenX = 122f, screenY = 100f, width = 76f, height = 22f
        };
        List<WorldMapDetailLabelCandidate> result = WorldMapRegionPresentationPolicy.SelectNearDetailLabels(
            new[] { ordinary, adjacent }, 6202);
        Assert.AreEqual(1, result.Count, "76px 宽标签相距 44px 时必须过滤重叠项");

        result = WorldMapRegionPresentationPolicy.SelectNearDetailLabels(
            new[] { ordinary, adjacent, selected }, 6202);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(selected.cellIndex, result[0].cellIndex);

        adjacent.screenX = 176f;
        result = WorldMapRegionPresentationPolicy.SelectNearDetailLabels(
            new[] { ordinary, adjacent }, 6202);
        Assert.AreEqual(2, result.Count, "76px 时标签边缘相接，不应算作重叠");
    }

    [Test]
    public void CellDetails_UnknownShowsOnlyCoarseRegionWhileKnownAddsInternalPosition()
    {
        WorldMap map = CreatePlainMap(8, 8, 6201);
        WorldMapRegionRules.Assign(map);
        WorldCell cell = map.cells[20];
        MapRegionData region = map.regions.Single(item => item.regionId == cell.regionId);
        var progress = new WorldMapProgressState();
        var sect = new PlayerData
        {
            sectId = "player_sect",
            founding = new FoundingState { initialized = true, stage = FoundingStage.Cave }
        };

        string unknown = WorldMapCellDetailsFormatter.Format(map, cell.index,
            WorldMapViewMode.Landform, false, null, progress, sect);
        StringAssert.Contains("认知：未知", unknown);
        StringAssert.Contains(region.regionName, unknown);
        StringAssert.Contains(WorldMapRegionRules.RegionTypeLabel(region.regionType), unknown);
        StringAssert.DoesNotContain(WorldMapRegionRules.PositionLabel(cell.internalPositionTag), unknown);
        StringAssert.DoesNotContain("危险：", unknown);
        StringAssert.DoesNotContain("影响值：", unknown);

        progress.revealedCellIndices.Add(cell.index);
        string known = WorldMapCellDetailsFormatter.Format(map, cell.index,
            WorldMapViewMode.Landform, false, null, progress, sect);
        StringAssert.Contains("认知：已知", known);
        StringAssert.Contains(WorldMapRegionRules.PositionLabel(cell.internalPositionTag), known);
    }

    [Test]
    public void SiteSelection_UnknownCellHidesTerrainAuraAndBuildable()
    {
        WorldMap map = CreatePlainMap(8, 8, 6204);
        WorldMapRegionRules.Assign(map);
        WorldCell cell = map.cells[20];
        MapRegionData region = map.regions.Single(item => item.regionId == cell.regionId);
        var progress = new WorldMapProgressState();
        var sect = new PlayerData
        {
            founding = new FoundingState
            {
                initialized = true,
                stage = FoundingStage.WorldSelection
            }
        };

        string unknown = WorldMapCellDetailsFormatter.Format(map, cell.index,
            WorldMapViewMode.Landform, true, null, progress, sect);
        StringAssert.Contains("认知：未知", unknown);
        StringAssert.Contains(region.regionName, unknown);
        StringAssert.Contains(WorldMapRegionRules.RegionTypeLabel(region.regionType), unknown);
        StringAssert.DoesNotContain("可建设", unknown);
        StringAssert.DoesNotContain("灵气", unknown);
        StringAssert.DoesNotContain(WorldMapCellDetailsFormatter.BiomeLabel(cell.biome), unknown);

        progress.revealedCellIndices.Add(cell.index);
        string known = WorldMapCellDetailsFormatter.Format(map, cell.index,
            WorldMapViewMode.Landform, true, null, progress, sect);
        StringAssert.Contains("可建设", known);
        StringAssert.Contains("灵气", known);
    }

    [Test]
    public void Presenter_WiresNearPoolsSelectedAnchorSafeAreaReuseAndHudLifecycle()
    {
        Camera priorCamera = Camera.main;
        Vector3 priorPosition = priorCamera == null ? Vector3.zero : priorCamera.transform.position;
        Quaternion priorRotation = priorCamera == null ? Quaternion.identity : priorCamera.transform.rotation;
        bool priorOrthographic = priorCamera != null && priorCamera.orthographic;
        float priorSize = priorCamera == null ? 5f : priorCamera.orthographicSize;
        Color priorBackground = priorCamera == null ? Color.black : priorCamera.backgroundColor;
        EventSystem priorEventSystem = UnityEngine.Object.FindObjectOfType<EventSystem>();
        GameObject presenterObject = null;
        Camera createdCamera = null;
        EventSystem createdEventSystem = null;
        GameObject ownedHud = null;
        try
        {
            presenterObject = new GameObject("RegionPresenterWiringTest");
            WorldMapPresenter presenter = presenterObject.AddComponent<WorldMapPresenter>();
            Camera camera = GetPrivateField<Camera>(presenter, "mapCamera");
            if (priorCamera == null) createdCamera = camera;
            if (priorEventSystem == null) createdEventSystem = UnityEngine.Object.FindObjectOfType<EventSystem>();
            Canvas canvas = GetPrivateField<Canvas>(presenter, "hudCanvas");
            Assert.NotNull(canvas);
            ownedHud = canvas.gameObject;

            WorldMap map = CreatePlainMap(12, 12, 6203);
            var region = new MapRegionData
            {
                regionId = "single-region", regionName = "测试区域", regionType = MapRegionType.Valley,
                centerCellIndex = 0, displayPriority = 80,
                cellIndices = Enumerable.Range(0, map.cells.Length).ToList()
            };
            map.regions = new List<MapRegionData> { region };
            foreach (WorldCell cell in map.cells)
            {
                cell.regionId = region.regionId;
                cell.internalPositionTag = MapInternalPositionTag.OpenPlain;
            }
            var progress = new WorldMapProgressState
            {
                cellInfluences = map.cells.Select(cell => new CellInfluenceState
                {
                    cellIndex = cell.index, value = 80, level = InfluenceLevel.Core,
                    controllerSectId = "player_sect", sourceIds = new List<string> { "base" }
                }).ToList()
            };
            WorldMapSession.Set(map, progress);
            SetPrivateField(presenter, "map", map);
            InvokePrivate(presenter, "RefreshPresentationCaches");

            int selectedIndex = 65;
            SetPrivateField(presenter, "selectedCellIndex", selectedIndex);
            SetPrivateField(presenter, "lastZoomLevel", WorldMapZoomLevel.Mid);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(5f, 0.96f * Screen.height / 30f);
            PlaceAtViewport(camera, CenterForTest(map.cells[selectedIndex].coord), 0.35f, 0.50f);
            InvokePrivate(presenter, "RefreshRegionLabels");

            List<TMP_Text> regionPool = GetPrivateField<List<TMP_Text>>(presenter, "regionLabelPool");
            TMP_Text selectedLabel = regionPool.FirstOrDefault(item => item.gameObject.activeSelf &&
                item.text.StartsWith(region.regionName, StringComparison.Ordinal));
            Assert.NotNull(selectedLabel, "Mid 选中区域必须以选中格作为可见锚点");
            Vector3 selectedScreen = camera.WorldToScreenPoint(CenterForTest(map.cells[selectedIndex].coord));
            RectTransform regionRoot = GetPrivateField<RectTransform>(presenter, "regionLabelRoot");
            RectTransformUtility.ScreenPointToLocalPointInRectangle(regionRoot, selectedScreen, null,
                out Vector2 expectedAnchor);
            Assert.Less(Vector2.Distance(expectedAnchor, selectedLabel.rectTransform.anchoredPosition), 0.5f);

            PlaceAtViewport(camera, CenterForTest(map.cells[selectedIndex].coord), 0.86f, 0.50f);
            InvokePrivate(presenter, "RefreshRegionLabels");
            Assert.IsFalse(regionPool.Any(item => item.gameObject.activeSelf),
                "右侧 HUD 安全区内的候选不得显示或占用名额");

            SetPrivateField(presenter, "lastZoomLevel", WorldMapZoomLevel.Near);
            PlaceAtViewport(camera, CenterForTest(map.cells[selectedIndex].coord), 0.35f, 0.50f);
            InvokePrivate(presenter, "RefreshRegionLabels");
            Assert.IsFalse(regionPool.Any(item => item.gameObject.activeSelf), "Near 不得显示大区域名");
            List<TMP_Text> detailPool = GetPrivateField<List<TMP_Text>>(presenter, "nearDetailLabelPool");
            int activeDetails = detailPool.Count(item => item.gameObject.activeSelf);
            Assert.That(activeDetails, Is.InRange(1, WorldMapRegionPresentationPolicy.NearDetailLabelLimit));
            TMP_Text[] firstInstances = detailPool.ToArray();
            InvokePrivate(presenter, "RefreshRegionLabels");
            Assert.AreEqual(firstInstances.Length, detailPool.Count);
            for (int index = 0; index < firstInstances.Length; index++)
                Assert.AreSame(firstInstances[index], detailPool[index], "标签刷新必须复用池对象");

            UnityEngine.Object.DestroyImmediate(presenterObject);
            presenterObject = null;
            Assert.IsTrue(ownedHud == null, "Presenter 销毁后不得遗留 WorldMapHUD");
        }
        finally
        {
            if (presenterObject != null) UnityEngine.Object.DestroyImmediate(presenterObject);
            WorldMapSession.Clear();
            if (createdEventSystem != null) UnityEngine.Object.DestroyImmediate(createdEventSystem.gameObject);
            if (createdCamera != null) UnityEngine.Object.DestroyImmediate(createdCamera.gameObject);
            if (priorCamera != null)
            {
                priorCamera.transform.position = priorPosition;
                priorCamera.transform.rotation = priorRotation;
                priorCamera.orthographic = priorOrthographic;
                priorCamera.orthographicSize = priorSize;
                priorCamera.backgroundColor = priorBackground;
            }
        }
    }

    private static T GetPrivateField<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field, name);
        return (T)field.GetValue(target);
    }

    private static void SetPrivateField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field, name);
        field.SetValue(target, value);
    }

    private static object InvokePrivate(object target, string name)
    {
        MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method, name);
        return method.Invoke(target, null);
    }

    private static Vector2 CenterForTest(HexCoord coord) =>
        new Vector2(Mathf.Sqrt(3f) * (coord.col + ((coord.row & 1) == 1 ? 0.5f : 0f)), 1.5f * coord.row);

    private static void PlaceAtViewport(Camera camera, Vector2 world, float viewportX, float viewportY)
    {
        camera.transform.position = new Vector3(
            world.x - (viewportX - 0.5f) * 2f * camera.orthographicSize * camera.aspect,
            world.y - (viewportY - 0.5f) * 2f * camera.orthographicSize,
            -10f);
        camera.transform.rotation = Quaternion.identity;
    }

    private static MapInternalPositionTag DerivePositionTag(WorldMap map, MapRegionData region, int cellIndex)
    {
        MethodInfo method = typeof(WorldMapRegionRules).GetMethod("DerivePositionTag",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (MapInternalPositionTag)method.Invoke(null, new object[] { map, region, cellIndex });
    }

    private static bool IsConnected(WorldMap map, List<int> indices)
    {
        var allowed = new HashSet<int>(indices);
        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(indices[0]); visited.Add(indices[0]);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            foreach (int neighbor in map.GetNeighborIndices(current))
                if (allowed.Contains(neighbor) && visited.Add(neighbor)) queue.Enqueue(neighbor);
        }
        return visited.Count == allowed.Count;
    }

    private static int CountConnectedComponents(WorldMap map, Func<WorldCell, int> classifier)
    {
        bool[] visited = new bool[map.cells.Length];
        int count = 0;
        for (int start = 0; start < map.cells.Length; start++)
        {
            if (visited[start]) continue;
            int type = classifier(map.cells[start]);
            var queue = new Queue<int>();
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int neighbor in map.GetNeighborIndices(current))
                {
                    if (visited[neighbor] || classifier(map.cells[neighbor]) != type) continue;
                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }
            count++;
        }
        return count;
    }

    private static WorldMap CreatePlainMap(int width, int height, int seed)
    {
        var map = new WorldMap
        {
            width = width, height = height, effectiveSeed = seed,
            cells = new WorldCell[width * height], rivers = new List<RiverSegment>(), spiritVeins = new List<SpiritVein>()
        };
        for (int row = 0; row < height; row++)
        for (int col = 0; col < width; col++)
        {
            int index = row * width + col;
            map.cells[index] = new WorldCell
            {
                index = index, coord = new HexCoord(col, row), height = 0.5f,
                landform = LandformType.Plain, biome = BiomeType.Grassland,
                totalAura = 0.2f, elementalAura = new ElementalAura { earth = 0.2f }
            };
        }
        return map;
    }
}
