using System.IO;
using System.Linq;
using Cultivation4X.WorldMap;
using Newtonsoft.Json;
using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;

public class WorldMapContentTests
{
    [Test]
    public void Candidates_AreDeterministicAndContainExactlySevenTypes()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 5101 });
        WorldMapProgressState first = new WorldMapProgressState();
        WorldMapProgressState second = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, first);
        WorldMapContentRules.EnsureCandidates(map, second);

        Assert.AreEqual(7, first.mapSites.Count);
        Assert.AreEqual(7, first.mapSites.Select(site => site.siteType).Distinct().Count());
        Assert.AreEqual(JsonConvert.SerializeObject(first.mapSites), JsonConvert.SerializeObject(second.mapSites));
        Assert.AreEqual(7, first.mapSites.Select(site => site.cellIndex).Distinct().Count());
        Assert.IsTrue(first.mapSites.All(site => site.revealState == MapContentRevealState.Hidden));
    }

    [Test]
    public void FoundingHints_FourRemoteTypesAndNeverLeaksUnknownDetails()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 5102 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        MapSiteData site = progress.mapSites.Single(item => item.siteType == MapSiteType.Ruin);

        WorldMapContentRules.RefreshHints(map, progress);
        Assert.AreEqual(MapContentRevealState.Hidden, site.revealState);
        string text = WorldMapCellDetailsFormatter.Format(map, site.cellIndex, WorldMapViewMode.Landform,
            false, null, progress, EstablishedSect(site.cellIndex));
        StringAssert.Contains("认知：未知", text);
        StringAssert.DoesNotContain(site.siteName, text);

        progress.mapSites.Add(new MapSiteData
        {
            siteId = WorldMapProgressRules.PlayerSectBaseId, siteName = "测试宗",
            siteType = MapSiteType.SectBase, cellIndex = map.cells.First(cell =>
                progress.mapSites.All(candidate => candidate.cellIndex != cell.index)).index,
            revealState = MapContentRevealState.Discovered, siteState = MapSiteState.Developed,
            isRevealed = true, canInteract = true
        });
        WorldMapContentRules.RefreshHints(map, progress);
        MapSiteType[] globalHintTypes =
        {
            MapSiteType.SpiritMine, MapSiteType.BeastLair, MapSiteType.Ruin, MapSiteType.ResourceNode
        };
        Assert.IsTrue(progress.mapSites.Where(item => globalHintTypes.Contains(item.siteType))
            .All(item => item.revealState == MapContentRevealState.Hinted));
        Assert.AreEqual("未知线索", WorldMapPresentationMarkerFactory.CreateContentMarkers(map, progress)
            .Single(marker => marker.cellIndex == site.cellIndex).label);
        Assert.AreEqual(WorldMapMarkerKind.ContentHint,
            WorldMapPresentationMarkerFactory.CreateContentMarkers(map, progress)
                .Single(marker => marker.cellIndex == site.cellIndex).kind);
        text = WorldMapCellDetailsFormatter.Format(map, site.cellIndex, WorldMapViewMode.Landform,
            false, null, progress, EstablishedSect(site.cellIndex));
        StringAssert.DoesNotContain(site.siteName, text);

        Assert.IsTrue(WorldMapPresentationPolicy.MarkerVisible(new WorldMapPresentationMarker
        {
            id = WorldMapContentRules.CandidateId(MapSiteType.Village),
            kind = WorldMapMarkerKind.Village,
            cellIndex = site.cellIndex
        }, WorldMapViewMode.Landform, WorldMapIconDensityTier.Hidden, map.effectiveSeed));
    }

    [Test]
    public void Explore_HintedQualifiedDiscoversAndCannotRepeat()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 5103 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        MapSiteData site = progress.mapSites.First();
        site.revealState = MapContentRevealState.Hinted;
        WorldMapContentRules.SynchronizeLegacyFlags(site);
        var context = new MapMissionContext { actionType = MapActionType.Explore, targetCellIndex = site.cellIndex };

        Assert.IsTrue(WorldMapContentRules.CanExplore(map, progress, site.cellIndex, out _));
        Assert.IsTrue(WorldMapContentRules.CompleteSuccessfulAction(map, progress, context,
            MissionResultTier.Qualified, 4, out _));
        Assert.IsFalse(WorldMapContentRules.CanExplore(map, progress, site.cellIndex, out _));
        Assert.AreEqual(1, progress.exploredCellIndices.Count);
        Assert.AreEqual(1, progress.revealedCellIndices.Count);
        Assert.AreEqual(MapContentRevealState.Discovered, site.revealState);
        Assert.IsFalse(WorldMapContentRules.CompleteSuccessfulAction(map, progress, context,
            MissionResultTier.Excellent, 4, out _));
    }

    [Test]
    public void Explore_HiddenCandidateDiscoversItThroughOrdinaryCellEntry()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 5121 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        MapSiteData site = progress.mapSites.Single(item => item.siteType == MapSiteType.SpiritSpring);
        Assert.AreEqual(MapContentRevealState.Hidden, site.revealState);
        var context = new MapMissionContext { actionType = MapActionType.Explore, targetCellIndex = site.cellIndex };

        Assert.IsTrue(WorldMapContentRules.CanExplore(map, progress, site.cellIndex, out _));
        Assert.IsTrue(WorldMapContentRules.CompleteSuccessfulAction(map, progress, context,
            MissionResultTier.Qualified, 4, out _));

        Assert.AreEqual(MapContentRevealState.Discovered, site.revealState);
        CollectionAssert.Contains(progress.exploredCellIndices, site.cellIndex);
        CollectionAssert.Contains(progress.revealedCellIndices, site.cellIndex);
    }

    [Test]
    public void Explore_InsufficientKeepsHintRetryable()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 5120 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        MapSiteData site = progress.mapSites.Single(item => item.siteType == MapSiteType.BeastLair);
        site.revealState = MapContentRevealState.Hinted;
        WorldMapContentRules.SynchronizeLegacyFlags(site);
        var context = new MapMissionContext { actionType = MapActionType.Explore, targetCellIndex = site.cellIndex };

        Assert.IsFalse(WorldMapContentRules.CompleteSuccessfulAction(map, progress, context,
            MissionResultTier.Insufficient, 3, out _));
        Assert.AreEqual(MapContentRevealState.Hinted, site.revealState);
        Assert.IsEmpty(progress.exploredCellIndices);
        Assert.IsTrue(WorldMapContentRules.CanExplore(map, progress, site.cellIndex, out _));
    }

    [Test]
    public void SpiritSpring_RequiresOuterThenInfluenceAndAdvancesAtomically()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 5104 });
        int home = map.GetIndex(new HexCoord(12, 12));
        int outer = map.cells.First(cell => HexCoord.Distance(cell.coord, map.cells[home].coord) == 2).index;
        int influence = map.GetNeighborIndices(home).First();
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        MapSiteData spring = progress.mapSites.Single(site => site.siteType == MapSiteType.SpiritSpring);
        spring.cellIndex = outer;
        spring.revealState = MapContentRevealState.Discovered;
        spring.discoveredDay = spring.lastUpdatedDay = 1;
        WorldMapContentRules.SynchronizeLegacyFlags(spring);
        progress.influenceSources.Add(Source(home));
        progress.isInfluenceDirty = true;
        WorldMapInfluenceRules.Recalculate(map, progress);

        var investigate = new MapMissionContext
            { actionType = MapActionType.InvestigateSpiritSpring, targetCellIndex = outer, targetSiteId = spring.siteId };
        Assert.IsTrue(WorldMapContentRules.CanStartAction(map, progress, investigate, out _));
        Assert.IsTrue(WorldMapContentRules.CompleteSuccessfulAction(map, progress, investigate,
            MissionResultTier.Qualified, 2, out _));
        Assert.AreEqual(MapSiteState.Investigated, spring.siteState);

        var develop = new MapMissionContext
            { actionType = MapActionType.DevelopSpiritSpring, targetCellIndex = outer, targetSiteId = spring.siteId };
        Assert.IsFalse(WorldMapContentRules.CanStartAction(map, progress, develop, out _));
        spring.cellIndex = influence;
        develop.targetCellIndex = influence;
        Assert.IsTrue(WorldMapContentRules.CanStartAction(map, progress, develop, out _));
        Assert.IsTrue(WorldMapContentRules.CompleteSuccessfulAction(map, progress, develop,
            MissionResultTier.Qualified, 3, out _));
        Assert.AreEqual(MapSiteState.Developed, spring.siteState);
        Assert.AreEqual("player_sect", spring.ownerSectId);
    }

    [Test]
    public void RemainingSiteTypes_UseMappedPermissionsRewardsAndOneShotStates()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 5110 });
        int home = map.GetIndex(new HexCoord(12, 12));
        int influence = map.GetNeighborIndices(home).First();
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        progress.influenceSources.Add(Source(home));
        progress.isInfluenceDirty = true;
        WorldMapInfluenceRules.Recalculate(map, progress);

        MapSiteType[] types = { MapSiteType.Village, MapSiteType.SpiritMine,
            MapSiteType.CaveResidence, MapSiteType.BeastLair, MapSiteType.Ruin, MapSiteType.ResourceNode };
        foreach (MapSiteType type in types)
        {
            MapSiteData site = progress.mapSites.Single(item => item.siteType == type);
            site.cellIndex = influence;
            site.revealState = MapContentRevealState.Discovered;
            site.discoveredDay = site.lastUpdatedDay = 1;
            WorldMapContentRules.SynchronizeLegacyFlags(site);
            MapActionType action = WorldMapContentRules.ActionForSite(site);
            MapMissionContext context = new MapMissionContext
                { actionType = action, targetCellIndex = influence, targetSiteId = site.siteId };

            Assert.IsTrue(WorldMapContentRules.CanStartAction(map, progress, context, out string reason),
                $"{type}: {reason}");
            Reward reward = WorldMapContentRules.CreateReward(map, context);
            Assert.Greater(reward.Items.Single(item => item.itemId == FacilityRules.SpiritStoneId).count, 0);
            Assert.IsTrue(WorldMapContentRules.CompleteSuccessfulAction(map, progress, context,
                MissionResultTier.Qualified, 2, out reason), reason);
            Assert.AreEqual(type == MapSiteType.BeastLair || type == MapSiteType.Ruin
                ? MapSiteState.Investigated : MapSiteState.Developed, site.siteState);
            Assert.IsFalse(WorldMapContentRules.CompleteSuccessfulAction(map, progress, context,
                MissionResultTier.Qualified, 2, out _));
        }
    }

    [Test]
    public void RemainingSiteTypes_RejectInsufficientInfluenceAndRoundTripActionContext()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 5111 });
        int home = map.GetIndex(new HexCoord(12, 12));
        int far = map.cells.First(cell => HexCoord.Distance(cell.coord, map.cells[home].coord) >= 4).index;
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        progress.influenceSources.Add(Source(home));
        progress.isInfluenceDirty = true;
        WorldMapInfluenceRules.Recalculate(map, progress);
        MapSiteData village = progress.mapSites.Single(site => site.siteType == MapSiteType.Village);
        village.cellIndex = far;
        village.revealState = MapContentRevealState.Discovered;
        village.discoveredDay = village.lastUpdatedDay = 1;
        WorldMapContentRules.SynchronizeLegacyFlags(village);
        MapMissionContext context = new MapMissionContext
            { actionType = MapActionType.EstablishVillageRelation, targetCellIndex = far, targetSiteId = village.siteId };
        Assert.IsFalse(WorldMapContentRules.CanStartAction(map, progress, context, out _));
        Assert.AreEqual(MapSiteState.None, village.siteState);

        string json = JsonConvert.SerializeObject(context);
        MapMissionContext restored = JsonConvert.DeserializeObject<MapMissionContext>(json);
        Assert.AreEqual(context.actionType, restored.actionType);
        Assert.AreEqual(context.targetCellIndex, restored.targetCellIndex);
        Assert.AreEqual(context.targetSiteId, restored.targetSiteId);
    }

    [Test]
    public void RemainingSiteTypes_CreateSemanticMarkersAndActionLabels()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 5112 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        foreach (MapSiteData site in progress.mapSites.Where(item => item.siteType != MapSiteType.SectBase))
        {
            site.revealState = MapContentRevealState.Discovered;
            site.discoveredDay = site.lastUpdatedDay = 1;
            WorldMapContentRules.SynchronizeLegacyFlags(site);
        }
        var markers = WorldMapPresentationMarkerFactory.CreateContentMarkers(map, progress);
        Assert.AreEqual(7, markers.Count);
        Assert.AreEqual(WorldMapMarkerKind.Village,
            markers.Single(marker => marker.id == WorldMapContentRules.CandidateId(MapSiteType.Village)).kind);
        Assert.AreEqual(WorldMapMarkerKind.SpiritMine,
            markers.Single(marker => marker.id == WorldMapContentRules.CandidateId(MapSiteType.SpiritMine)).kind);
        Assert.AreEqual(WorldMapMarkerKind.CaveResidence,
            markers.Single(marker => marker.id == WorldMapContentRules.CandidateId(MapSiteType.CaveResidence)).kind);
        Assert.AreEqual(WorldMapMarkerKind.BeastLair,
            markers.Single(marker => marker.id == WorldMapContentRules.CandidateId(MapSiteType.BeastLair)).kind);
        Assert.AreEqual(WorldMapMarkerKind.Ruin,
            markers.Single(marker => marker.id == WorldMapContentRules.CandidateId(MapSiteType.Ruin)).kind);
        Assert.AreEqual(MapActionType.EstablishVillageRelation,
            WorldMapContentRules.ActionForSite(progress.mapSites.Single(site => site.siteType == MapSiteType.Village)));
    }

    [Test]
    public void EnvironmentHints_AreStableTwoToThreeAndKnownOnly()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 5113 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        Assert.IsEmpty(WorldMapContentEnvironmentRules.BuildHints(map, progress));
        MapSiteData firstSite = progress.mapSites.First(site => site.siteType != MapSiteType.SectBase);
        progress.influenceSources.Add(Source(firstSite.cellIndex));
        progress.isInfluenceDirty = true;
        WorldMapInfluenceRules.Recalculate(map, progress);
        var influenced = WorldMapContentEnvironmentRules.BuildHints(map, progress);
        Assert.IsNotEmpty(influenced);
        Assert.IsTrue(influenced.Any(item => item.sourceSiteId == firstSite.siteId));
        Assert.IsTrue(influenced.All(item =>
            WorldMapInfluenceRules.GetCellState(map, progress, item.cellIndex).knowledge ==
            KnowledgeState.Known));
        Assert.IsEmpty(progress.revealedCellIndices);
        progress.revealedCellIndices.AddRange(map.cells.Select(cell => cell.index));
        var first = WorldMapContentEnvironmentRules.BuildHints(map, progress);
        var second = WorldMapContentEnvironmentRules.BuildHints(map, progress);
        Assert.AreEqual(JsonConvert.SerializeObject(first), JsonConvert.SerializeObject(second));
        Assert.IsTrue(first.GroupBy(item => item.sourceSiteId).All(group => group.Count() >= 2 && group.Count() <= 3));
        Assert.IsTrue(first.All(item =>
            WorldMapInfluenceRules.GetCellState(map, progress, item.cellIndex).knowledge ==
            KnowledgeState.Known));
        var markers = WorldMapPresentationMarkerFactory.CreateEnvironmentHintMarkers(map, progress);
        Assert.IsTrue(markers.All(marker => progress.revealedCellIndices.Contains(marker.cellIndex)));
        Assert.IsTrue(markers.All(marker => progress.mapSites.All(site => site.cellIndex != marker.cellIndex)));
        Assert.IsTrue(WorldMapPresentationPolicy.TerrainIconsVisible(WorldMapViewMode.Landform,
            WorldMapIconDensityTier.Hidden));
        Assert.IsNotEmpty(WorldMapPresentationPolicy.BuildTerrainIconPlacements(map,
            WorldMapViewMode.Landform, 10f, null));
        var firstTerrain = WorldMapPresentationPolicy.BuildTerrainIconPlacements(
            map, WorldMapViewMode.Landform, 10f, null);
        var secondTerrain = WorldMapPresentationPolicy.BuildTerrainIconPlacements(
            map, WorldMapViewMode.Landform, 10f, null);
        Assert.AreEqual(JsonConvert.SerializeObject(firstTerrain),
            JsonConvert.SerializeObject(secondTerrain));
    }

    [Test]
    public void SemanticMarkers_PreserveSevenSiteAndSevenEnvironmentKinds()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 5114 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        foreach (MapSiteData site in progress.mapSites.Where(item => item.siteType != MapSiteType.SectBase))
        {
            site.revealState = MapContentRevealState.Discovered;
            site.discoveredDay = site.lastUpdatedDay = 1;
            WorldMapContentRules.SynchronizeLegacyFlags(site);
        }
        progress.revealedCellIndices.AddRange(map.cells.Select(cell => cell.index));

        var sites = WorldMapPresentationMarkerFactory.CreateContentMarkers(map, progress);
        var hints = WorldMapPresentationMarkerFactory.CreateEnvironmentHintMarkers(map, progress);
        Assert.AreEqual(7, sites.Select(marker => marker.kind).Distinct().Count());
        Assert.AreEqual(7, hints.Select(marker => marker.kind).Distinct().Count());
        Assert.AreEqual(7, hints.Select(marker => marker.environmentHintKind).Distinct().Count());

        WorldMapMarkerKind[] semanticKinds = sites.Select(marker => marker.kind)
            .Concat(hints.Select(marker => marker.kind)).Distinct().ToArray();
        var signatures = semanticKinds.Select(kind =>
        {
            WorldMapGeometryBuffer buffer = new WorldMapGeometryBuffer();
            WorldMapIconGeometry.AddMarkerIcon(buffer, kind, new UnityEngine.Vector2(0f, 0f), 1f,
                UnityEngine.Color.white);
            return string.Join(";", buffer.vertices.Select(vertex =>
                $"{vertex.x:0.000},{vertex.y:0.000}")) + "/" +
                   string.Join(";", buffer.triangles) + "/" +
                   string.Join(";", buffer.colors.Select(color =>
                $"{color.r:0.000},{color.g:0.000},{color.b:0.000},{color.a:0.000}"));
        }).ToList();
        Assert.AreEqual(14, semanticKinds.Length);
        Assert.AreEqual(14, signatures.Distinct().Count());
    }

    [Test]
    public void VersionNineteen_RoundTripsAndRejectsIllegalContentState()
    {
        Assert.AreEqual(20, SaveDataVersion.Current);
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 5105 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        string json = JsonConvert.SerializeObject(new GameState { worldMap = map, worldMapProgress = progress });
        GameState restored = JsonConvert.DeserializeObject<GameState>(json);
        Assert.AreEqual(SaveDataVersion.Current, restored.version);
        Assert.AreEqual(JsonConvert.SerializeObject(progress.mapSites),
            JsonConvert.SerializeObject(restored.worldMapProgress.mapSites));

        MapSiteData spring = restored.worldMapProgress.mapSites.Single(site => site.siteType == MapSiteType.SpiritSpring);
        spring.siteState = MapSiteState.Developed;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(restored));

        GameState invalidVillage = Clone(JsonConvert.DeserializeObject<GameState>(json));
        MapSiteData village = invalidVillage.worldMapProgress.mapSites.Single(site => site.siteType == MapSiteType.Village);
        village.revealState = MapContentRevealState.Discovered;
        village.discoveredDay = village.lastUpdatedDay = 0;
        village.siteState = MapSiteState.Investigated;
        WorldMapContentRules.SynchronizeLegacyFlags(village);
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(invalidVillage));

        GameState invalidOwner = Clone(JsonConvert.DeserializeObject<GameState>(json));
        MapSiteData undiscoveredOwner = invalidOwner.worldMapProgress.mapSites.Single(site => site.siteType == MapSiteType.Village);
        undiscoveredOwner.revealState = MapContentRevealState.Discovered;
        undiscoveredOwner.discoveredDay = undiscoveredOwner.lastUpdatedDay = 0;
        undiscoveredOwner.ownerSectId = "unexpected_sect";
        WorldMapContentRules.SynchronizeLegacyFlags(undiscoveredOwner);
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(invalidOwner));

        GameState invalidBeast = Clone(JsonConvert.DeserializeObject<GameState>(json));
        MapSiteData beast = invalidBeast.worldMapProgress.mapSites.Single(site => site.siteType == MapSiteType.BeastLair);
        beast.revealState = MapContentRevealState.Discovered;
        beast.discoveredDay = beast.lastUpdatedDay = 0;
        beast.siteState = MapSiteState.Developed;
        beast.ownerSectId = "player_sect";
        WorldMapContentRules.SynchronizeLegacyFlags(beast);
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(invalidBeast));
    }

    [Test]
    public void SectPlacement_PutsDiscoverableSpiritSpringNextToBase()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 5106 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        int home = map.cells.Where(cell => cell.isBuildable).Select(cell => cell.index)
            .First(index => WorldMapContentRules.TryPrepareSectBasePlacement(map, progress, index, out _));
        MapSiteData spring = progress.mapSites.Single(site => site.siteType == MapSiteType.SpiritSpring);
        Assert.AreEqual(1, HexCoord.Distance(map.cells[home].coord, map.cells[spring.cellIndex].coord));
        spring.revealState = MapContentRevealState.Hinted;
        WorldMapContentRules.SynchronizeLegacyFlags(spring);

        var explore = new MapMissionContext { actionType = MapActionType.Explore, targetCellIndex = spring.cellIndex };
        Assert.IsTrue(WorldMapContentRules.CompleteSuccessfulAction(map, progress, explore,
            MissionResultTier.Excellent, 1, out _));
        Assert.AreEqual(MapContentRevealState.Discovered, spring.revealState);
    }

    [Test]
    public void SectPlacement_NoBuildableNeighbor_StillPlacesSpiritSpringNearBase()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 16, height = 16, seed = 805 });
        int home = map.cells.First(cell => cell.isBuildable).index;
        foreach (int neighbor in map.GetNeighborIndices(home))
        {
            map.cells[neighbor].landform = LandformType.Mountain;
            map.cells[neighbor].biome = BiomeType.Alpine;
            map.cells[neighbor].isBuildable = false;
        }
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        Assert.IsFalse(map.GetNeighborIndices(home).Any(index => map.cells[index].isBuildable),
            "该边界场景应覆盖驻地周围没有可建设邻格的情况");

        Assert.IsTrue(WorldMapContentRules.TryPrepareSectBasePlacement(map, progress, home, out string reason), reason);
        MapSiteData spring = progress.mapSites.Single(site => site.siteType == MapSiteType.SpiritSpring);
        Assert.AreEqual(1, HexCoord.Distance(map.cells[home].coord, map.cells[spring.cellIndex].coord));
    }

    [Test]
    public void MapMissionSaveValidation_AcceptsAwaitingAndRejectsTampering()
    {
        GameState valid = EstablishedStateWithMapMission(MissionState.AwaitingReward);
        Assert.DoesNotThrow(() => SaveManager.ValidateWorldMapState(valid));

        GameState badState = Clone(valid);
        badState.activeMissions[0].state = MissionState.NotStarted;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(badState));

        GameState waitingNode = Clone(valid);
        waitingNode.activeMissions[0].state = MissionState.WaitingNode;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(waitingNode));

        GameState missingContext = Clone(valid);
        missingContext.activeMissions[0].mapContext = null;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(missingContext));

        GameState wrongId = Clone(valid);
        wrongId.activeMissions[0].missionId = "combat_001";
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(wrongId));

        GameState targetLeak = Clone(valid);
        targetLeak.activeMissions[0].mapContext.targetSiteId = "unexpected";
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(targetLeak));

        GameState duplicate = Clone(valid);
        duplicate.activeMissions.Add(Clone(valid).activeMissions[0]);
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(duplicate));

        GameState deadActor = Clone(valid);
        deadActor.characters.Single(character => character.characterId == "founder_1").health = HealthState.Dead;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(deadActor));

        GameState rewardTamper = Clone(valid);
        rewardTamper.activeMissions[0].reward.Items.Single(item =>
            item.itemId == FacilityRules.SpiritStoneId).count++;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(rewardTamper));

        GameState villageMission = EstablishedStateWithMapMission(MissionState.AwaitingReward);
        MapSiteData village = villageMission.worldMapProgress.mapSites.Single(site => site.siteType == MapSiteType.Village);
        int villageCell = villageMission.worldMap.GetNeighborIndices(villageMission.sect.founding.selectedWorldCellIndex)
            .First(index => villageMission.worldMapProgress.mapSites.All(site => site.cellIndex != index));
        village.cellIndex = villageCell;
        village.revealState = MapContentRevealState.Discovered;
        village.discoveredDay = village.lastUpdatedDay = 1;
        WorldMapContentRules.SynchronizeLegacyFlags(village);
        WorldLocationRules.SynchronizeFromMapSites(villageMission.worldMap, villageMission.worldMapProgress);
        villageMission.activeMissions[0].missionId = WorldMapContentRules.EstablishVillageRelationMissionId;
        villageMission.activeMissions[0].mapContext = new MapMissionContext
        {
            actionType = MapActionType.EstablishVillageRelation,
            targetCellIndex = villageCell,
            targetSiteId = village.siteId
        };
        villageMission.activeMissions[0].reward = WorldMapContentRules.CreateReward(villageMission.worldMap,
            villageMission.activeMissions[0].mapContext);
        Assert.DoesNotThrow(() => SaveManager.ValidateWorldMapState(villageMission));
        villageMission.activeMissions[0].mapContext.actionType = MapActionType.DevelopSpiritMine;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(villageMission));
    }

    [Test]
    public void VersionSeventeen_RejectsMentalStateOutsideStrictRange()
    {
        GameState invalid = EstablishedStateWithMapMission(MissionState.AwaitingReward);
        invalid.characters[0].mentalState = DiscipleMentalStateRules.MaxMentalState + 1;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(invalid));

        invalid.characters[0].mentalState = DiscipleMentalStateRules.MinMentalState - 1;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(invalid));
    }

    [Test]
    public void SaveV16_RejectsHintedFacadeAndRequiresDiscoveredFacade()
    {
        GameState valid = EstablishedStateWithMapMission(MissionState.AwaitingReward);
        MapMissionContext context = valid.activeMissions[0].mapContext;
        MapSiteData target = valid.worldMapProgress.mapSites.Single(site =>
            site.cellIndex == context.targetCellIndex);
        string facadeId = "world_location_" + target.siteId;
        Assert.IsNull(valid.worldMap.GetLocation(facadeId));

        GameState leakedHint = Clone(valid);
        MapSiteData leakedSite = leakedHint.worldMapProgress.mapSites.Single(site =>
            site.cellIndex == context.targetCellIndex);
        WorldCell leakedCell = leakedHint.worldMap.cells[leakedSite.cellIndex];
        var leakedLocation = new WorldLocation
        {
            id = facadeId,
            sourceMapSiteId = leakedSite.siteId,
            type = LocationType.Ruins,
            name = leakedSite.siteName,
            position = new UnityEngine.Vector2Int(leakedCell.coord.col, leakedCell.coord.row),
            state = LocationState.Active,
            availableActions = new List<LocationAction>(),
            availableMissionIds = new List<string>()
        };
        leakedHint.worldMap.locations[facadeId] = leakedLocation;
        leakedCell.locationId = facadeId;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(leakedHint));

        GameState discovered = Clone(valid);
        discovered.activeMissions.Clear();
        MapSiteData discoveredSite = discovered.worldMapProgress.mapSites.Single(site =>
            site.cellIndex == context.targetCellIndex);
        discoveredSite.revealState = MapContentRevealState.Discovered;
        discoveredSite.discoveredDay = discoveredSite.lastUpdatedDay = discovered.currentDay;
        WorldMapContentRules.SynchronizeLegacyFlags(discoveredSite);
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(discovered));

        WorldLocationRules.SynchronizeFromMapSites(discovered.worldMap, discovered.worldMapProgress);
        Assert.DoesNotThrow(() => SaveManager.ValidateWorldMapState(discovered));
    }

    [Test]
    public void VersionThirteen_RejectsTamperedRegionSnapshots()
    {
        GameState valid = EstablishedStateWithMapMission(MissionState.AwaitingReward);
        Assert.DoesNotThrow(() => SaveManager.ValidateWorldMapState(valid));

        GameState duplicateCell = Clone(valid);
        duplicateCell.worldMap.regions[1].cellIndices.Add(duplicateCell.worldMap.regions[0].cellIndices[0]);
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(duplicateCell));

        GameState missingCell = Clone(valid);
        missingCell.worldMap.regions[0].cellIndices.RemoveAt(0);
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(missingCell));

        GameState wrongCenter = Clone(valid);
        wrongCenter.worldMap.regions[0].centerCellIndex = wrongCenter.worldMap.regions[1].centerCellIndex;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(wrongCenter));

        GameState illegalTag = Clone(valid);
        illegalTag.worldMap.cells[0].internalPositionTag = (MapInternalPositionTag)999;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(illegalTag));

        GameState renamed = Clone(valid);
        renamed.worldMap.regions[0].regionName = "篡改区域";
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(renamed));
    }

    [Test]
    public void VersionThirteen_RejectsUnsafeStaticRegionInputs()
    {
        GameState nullAura = EstablishedStateWithMapMission(MissionState.AwaitingReward);
        nullAura.worldMap.cells[0].elementalAura = null;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(nullAura));

        GameState nanHeight = EstablishedStateWithMapMission(MissionState.AwaitingReward);
        nanHeight.worldMap.cells[0].height = float.NaN;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(nanHeight));

        GameState nullRiver = EstablishedStateWithMapMission(MissionState.AwaitingReward);
        nullRiver.worldMap.rivers.Add(null);
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(nullRiver));

        GameState nullVein = EstablishedStateWithMapMission(MissionState.AwaitingReward);
        nullVein.worldMap.spiritVeins[0] = null;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(nullVein));
    }

    [Test]
    public void SaveVersionGate_ExplicitlyRejectsVersionTwelve()
    {
        MethodInfo deserialize = typeof(SaveManager).GetMethod("DeserializeCurrentVersion",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(deserialize);
        string current = JsonConvert.SerializeObject(new GameState { version = SaveDataVersion.Current });
        Assert.DoesNotThrow(() => deserialize.Invoke(null, new object[] { current }));
        string legacy = JsonConvert.SerializeObject(new GameState { version = 12 });
        TargetInvocationException failure = Assert.Throws<TargetInvocationException>(() =>
            deserialize.Invoke(null, new object[] { legacy }));
        Assert.IsInstanceOf<SaveVersionMismatchException>(failure.InnerException);
    }

    [Test]
    public void ProgressNotification_IsolatesFailingPresentationListener()
    {
        bool secondCalled = false;
        System.Action failing = () => throw new System.InvalidOperationException("ui failure");
        System.Action succeeding = () => secondCalled = true;
        WorldMapSession.ProgressChanged += failing;
        WorldMapSession.ProgressChanged += succeeding;
        try
        {
            Assert.DoesNotThrow(WorldMapSession.NotifyProgressChanged);
            Assert.IsTrue(secondCalled);
        }
        finally
        {
            WorldMapSession.ProgressChanged -= failing;
            WorldMapSession.ProgressChanged -= succeeding;
        }
    }

    private static GameState EstablishedStateWithMapMission(MissionState missionState)
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 32, height = 24, seed = 5107 });
        WorldMapProgressState progress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(map, progress);
        int home = map.cells.Where(cell => cell.isBuildable).Select(cell => cell.index)
            .First(index => WorldMapContentRules.TryPrepareSectBasePlacement(map, progress, index, out _));
        progress.mapSites.Add(new MapSiteData
        {
            siteId = WorldMapProgressRules.PlayerSectBaseId, cellIndex = home,
            siteType = MapSiteType.SectBase, siteName = "测试宗", isRevealed = true, canInteract = true,
            revealState = MapContentRevealState.Discovered, siteState = MapSiteState.Developed,
            ownerSectId = "player_sect", discoveredDay = 0, lastUpdatedDay = 0
        });
        progress.influenceSources.Add(Source(home));
        progress.influenceSources[0].sourceId = WorldMapProgressRules.PlayerSectBaseId;
        progress.isInfluenceDirty = true;
        WorldMapInfluenceRules.Recalculate(map, progress);
        WorldMapContentRules.RefreshHints(map, progress);
        int target = progress.mapSites.First(site => site.siteType != MapSiteType.SectBase &&
            site.revealState == MapContentRevealState.Hinted).cellIndex;
        var context = new MapMissionContext { actionType = MapActionType.Explore, targetCellIndex = target };
        Reward reward = WorldMapContentRules.CreateReward(map, context);
        var mission = new MissionSaveData
        {
            missionId = WorldMapContentRules.ExploreMissionId, assignedCharacterId = "founder_1",
            state = missionState, remainingDays = missionState == MissionState.AwaitingReward ? 0 : 1,
            elapsedDays = 2, currentNodeIndex = 0, reward = reward, hasCapabilitySnapshot = true,
            capabilityScore = 100, resultTier = MissionResultTier.Qualified, mapContext = context
        };
        List<FounderCandidateData> candidates = FoundingRules.GenerateCandidates(5107).Take(3).ToList();
        string[] ids = { "founder_1", "founder_2", "founder_3" };
        for (int index = 0; index < candidates.Count; index++) candidates[index].candidateId = ids[index];
        return new GameState
        {
            currentDay = 2, worldMap = map, worldMapProgress = progress,
            sect = new PlayerData
            {
                sectId = "player_sect", sectName = "测试宗", influenceRadius = 2, foundedDay = 0,
                founding = new FoundingState
                {
                    initialized = true, stage = FoundingStage.Cave, selectedWorldCellIndex = home,
                    pendingSectName = "测试宗",
                    selectedTechniqueId = "qingmu", candidates = candidates,
                    selectedFounderIds = ids.ToList(), village = new VillageState(), externalThreat = new ActiveThreatState()
                }
            },
            characters = ids.Select(id => new CharacterState
            {
                characterId = id, displayName = id, hasGeneratedProfile = true, health = HealthState.Healthy
            }).ToList(),
            activeMissions = new List<MissionSaveData> { mission }
        };
    }

    private static GameState Clone(GameState state) =>
        JsonConvert.DeserializeObject<GameState>(JsonConvert.SerializeObject(state));

    private static PlayerData EstablishedSect(int home) => new PlayerData
    {
        sectId = "player_sect", sectName = "测试宗",
        founding = new FoundingState
        {
            initialized = true, stage = FoundingStage.Cave, selectedWorldCellIndex = home,
            pendingSectName = "测试宗"
        }
    };

    private static InfluenceSourceData Source(int cellIndex) => new InfluenceSourceData
    {
        sourceId = "base", sourceType = InfluenceSourceType.SectBase, cellIndex = cellIndex,
        controllerSectId = "player_sect", baseStrength = WorldMapInfluenceRules.SectBaseStrength,
        radius = WorldMapInfluenceRules.SectBaseRadius, isActive = true
    };
}
