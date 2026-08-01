using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cultivation4X.WorldMap;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

public class SectFoundingIntegrationTests
{
    private readonly List<Object> objects = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        PlayerManager.Instance = null;
        NPCManager.Instance = null;
        WorldMapSession.Clear();
        FoundingRules.ResetCatalogForTests();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in objects)
            if (item != null) Object.DestroyImmediate(item);
        objects.Clear();
        PlayerManager.Instance = null;
        NPCManager.Instance = null;
        WorldMapSession.Clear();
        FoundingRules.ResetCatalogForTests();
    }

    [Test]
    public void FoundingFlow_CreatesExactlyOneSectBaseAfterExplicitConfirmation()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        NPCManager npcs = Add<NPCManager>("NPCs");
        PlayerManager.Instance = player;
        NPCManager.Instance = npcs;
        player.InitializeNewFoundingGame(801);
        WorldMap map = WorldMapSession.Current;
        int buildable = map.cells.First(cell => cell.isBuildable).index;

        Assert.IsTrue(player.ConfirmWorldSite(buildable, out string siteReason), siteReason);
        List<string> founders = player.playerData.founding.candidates.Take(3)
            .Select(candidate => candidate.candidateId).ToList();
        Assert.IsTrue(player.ConfirmFounderSelection(founders, out string founderReason), founderReason);
        Assert.AreEqual(3, npcs.GetAllNPC().Count);
        Assert.IsTrue(player.SelectFoundingTechnique("qingmu", out string techniqueReason), techniqueReason);
        Assert.AreEqual(FoundingStage.SectConfirmation, player.playerData.founding.stage);
        Assert.IsNull(WorldMapProgressRules.GetSectBase(WorldMapSession.Progress));

        Assert.IsTrue(player.ConfirmSectFounding("  青云宗  ", out string reason), reason);
        Assert.AreEqual(FoundingStage.Cave, player.playerData.founding.stage);
        Assert.AreEqual("青云宗", player.playerData.sectName);
        Assert.AreEqual(2, player.playerData.influenceRadius);
        MapSiteData sectBase = WorldMapProgressRules.GetSectBase(WorldMapSession.Progress);
        Assert.NotNull(sectBase);
        Assert.AreEqual(buildable, sectBase.cellIndex);
        Assert.AreEqual("青云宗", sectBase.siteName);
        Assert.AreEqual(1, WorldMapSession.Progress.influenceSources.Count);
        Assert.AreEqual(sectBase.siteId, WorldMapSession.Progress.influenceSources.Single().sourceId);
        InfluenceSourceData source = WorldMapSession.Progress.influenceSources.Single();
        Assert.AreEqual(WorldMapInfluenceRules.SectBaseStrength, source.baseStrength);
        Assert.AreEqual(WorldMapInfluenceRules.SectBaseRadius, source.radius);
        Assert.IsTrue(source.isActive);
        Assert.AreEqual(19, WorldMapSession.Progress.cellInfluences.Count);
        Assert.IsFalse(WorldMapSession.Progress.isInfluenceDirty);
        Assert.IsFalse(player.ConfirmSectFounding("第二宗门", out _));
        Assert.AreEqual(1, WorldMapSession.Progress.mapSites.Count(site =>
            site.siteType == MapSiteType.SectBase));
    }

    [Test]
    public void InvalidSectName_LeavesConfirmationStateUnchanged()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        NPCManager npcs = Add<NPCManager>("NPCs");
        PlayerManager.Instance = player;
        NPCManager.Instance = npcs;
        AdvanceToConfirmation(player, 802);
        FoundingState founding = player.playerData.founding;
        GameState confirmationState = new GameState
        {
            worldMap = WorldMapSession.Current,
            worldMapProgress = WorldMapSession.Progress,
            sect = player.playerData,
            characters = NPCManager.Instance.GetAllNPC()
                .Select(npc => npc.Character).ToList()
        };
        Assert.DoesNotThrow(() => SaveManager.ValidateWorldMapState(confirmationState));
        GameState partialSect = JsonConvert.DeserializeObject<GameState>(
            JsonConvert.SerializeObject(confirmationState));
        partialSect.sect.foundedDay = 3;
        Assert.Throws<InvalidDataException>(() =>
            SaveManager.ValidateWorldMapState(partialSect));

        Assert.IsFalse(player.ConfirmSectFounding("A", out string reason));
        StringAssert.Contains("2–12", reason);
        Assert.AreEqual(FoundingStage.SectConfirmation, founding.stage);
        Assert.IsNull(player.playerData.sectId);
        Assert.IsNull(WorldMapProgressRules.GetSectBase(WorldMapSession.Progress));

        Assert.IsFalse(player.ConfirmSectFounding("宗门\n坏名", out _));
        Assert.AreEqual(FoundingStage.SectConfirmation, founding.stage);
        Assert.IsEmpty(WorldMapSession.Progress.mapSites);
    }

    [Test]
    public void VersionTenSnapshot_RoundTripsSectProgressAndRejectsInconsistentBase()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        NPCManager npcs = Add<NPCManager>("NPCs");
        PlayerManager.Instance = player;
        NPCManager.Instance = npcs;
        AdvanceToConfirmation(player, 803);
        Assert.IsTrue(player.ConfirmSectFounding("玄霄宗", out string reason), reason);
        GameState state = new GameState
        {
            worldMap = WorldMapSession.Current,
            worldMapProgress = WorldMapSession.Progress,
            sect = player.playerData,
            characters = NPCManager.Instance.GetAllNPC()
                .Select(npc => npc.Character).ToList()
        };

        Assert.AreEqual(10, state.version);
        Assert.DoesNotThrow(() => SaveManager.ValidateWorldMapState(state));
        GameState restored = JsonConvert.DeserializeObject<GameState>(
            JsonConvert.SerializeObject(state));
        Assert.DoesNotThrow(() => SaveManager.ValidateWorldMapState(restored));
        Assert.AreEqual("玄霄宗", restored.sect.sectName);
        Assert.AreEqual(player.playerData.founding.selectedWorldCellIndex,
            restored.worldMapProgress.mapSites.Single().cellIndex);
        Assert.AreEqual(state.worldMap.pointsOfInterest.Select(point => point.cellIndex),
            restored.worldMap.pointsOfInterest.Select(point => point.cellIndex));

        GameState missingFounding = JsonConvert.DeserializeObject<GameState>(
            JsonConvert.SerializeObject(state));
        missingFounding.sect.founding = null;
        Assert.Throws<InvalidDataException>(() =>
            SaveManager.ValidateWorldMapState(missingFounding));

        GameState missingCharacters = JsonConvert.DeserializeObject<GameState>(
            JsonConvert.SerializeObject(state));
        missingCharacters.characters.Clear();
        Assert.Throws<InvalidDataException>(() =>
            SaveManager.ValidateWorldMapState(missingCharacters));

        GameState invalidStage = JsonConvert.DeserializeObject<GameState>(
            JsonConvert.SerializeObject(state));
        invalidStage.sect.founding.stage = (FoundingStage)99;
        Assert.Throws<InvalidDataException>(() =>
            SaveManager.ValidateWorldMapState(invalidStage));

        GameState missingTechnique = JsonConvert.DeserializeObject<GameState>(
            JsonConvert.SerializeObject(state));
        missingTechnique.sect.founding.stage = FoundingStage.SectConfirmation;
        missingTechnique.sect.founding.selectedTechniqueId = null;
        missingTechnique.sect.sectId = null;
        missingTechnique.sect.sectName = null;
        missingTechnique.sect.influenceRadius = 0;
        missingTechnique.worldMapProgress.mapSites.Clear();
        missingTechnique.worldMapProgress.influenceSources.Clear();
        missingTechnique.worldMapProgress.cellInfluences.Clear();
        Assert.Throws<InvalidDataException>(() =>
            SaveManager.ValidateWorldMapState(missingTechnique));

        restored.worldMapProgress.mapSites.Single().cellIndex =
            restored.worldMap.cells.Length - 1;
        Assert.Throws<InvalidDataException>(() =>
            SaveManager.ValidateWorldMapState(restored));

        GameState invalidSource = JsonConvert.DeserializeObject<GameState>(
            JsonConvert.SerializeObject(state));
        invalidSource.worldMapProgress.influenceSources.Single().cellIndex = -1;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(invalidSource));

        GameState invalidCache = JsonConvert.DeserializeObject<GameState>(
            JsonConvert.SerializeObject(state));
        invalidCache.worldMapProgress.cellInfluences.Single(item => item.level == InfluenceLevel.Core).value = 99;
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(invalidCache));

        foreach (System.Action<InfluenceSourceData> invalidate in new System.Action<InfluenceSourceData>[]
                 {
                     source => source.baseStrength = 99,
                     source => source.radius = 1,
                     source => source.isActive = false
                 })
        {
            GameState invalidSchema = JsonConvert.DeserializeObject<GameState>(
                JsonConvert.SerializeObject(state));
            invalidate(invalidSchema.worldMapProgress.influenceSources.Single());
            Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(invalidSchema));
        }
    }

    [Test]
    public void InfluenceLoadPreparation_DoesNotRepairMissingProgressOrSourceTruth()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.InitializeNewFoundingGame(804);
        GameState valid = new GameState
        {
            worldMap = WorldMapSession.Current,
            worldMapProgress = WorldMapSession.Progress,
            sect = player.playerData
        };
        Assert.DoesNotThrow(() => SaveManager.ValidateWorldMapState(valid));

        GameState missingProgress = JsonConvert.DeserializeObject<GameState>(
            JsonConvert.SerializeObject(valid));
        missingProgress.worldMapProgress = null;

        InvokeInfluencePreparation(missingProgress);

        Assert.IsNull(missingProgress.worldMapProgress);
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(missingProgress));

        GameState missingSources = JsonConvert.DeserializeObject<GameState>(
            JsonConvert.SerializeObject(valid));
        missingSources.worldMapProgress.influenceSources = null;

        InvokeInfluencePreparation(missingSources);

        Assert.IsNull(missingSources.worldMapProgress.influenceSources);
        Assert.Throws<InvalidDataException>(() => SaveManager.ValidateWorldMapState(missingSources));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InfluenceLoadPreparation_RecoversEmptyOrDirtyCache(bool dirty)
    {
        PlayerManager player = Add<PlayerManager>("Player");
        NPCManager npcs = Add<NPCManager>("NPCs");
        PlayerManager.Instance = player;
        NPCManager.Instance = npcs;
        AdvanceToConfirmation(player, dirty ? 805 : 806);
        Assert.IsTrue(player.ConfirmSectFounding("归元宗", out string reason), reason);
        GameState state = new GameState
        {
            worldMap = WorldMapSession.Current,
            worldMapProgress = WorldMapSession.Progress,
            sect = player.playerData,
            characters = npcs.GetAllNPC().Select(npc => npc.Character).ToList()
        };
        state = JsonConvert.DeserializeObject<GameState>(JsonConvert.SerializeObject(state));
        int expectedInfluenceCount = state.worldMapProgress.cellInfluences.Count;
        if (!dirty) state.worldMapProgress.cellInfluences.Clear();
        state.worldMapProgress.isInfluenceDirty = dirty;

        InvokeInfluencePreparation(state);

        Assert.IsFalse(state.worldMapProgress.isInfluenceDirty);
        Assert.AreEqual(expectedInfluenceCount, state.worldMapProgress.cellInfluences.Count);
        Assert.DoesNotThrow(() => SaveManager.ValidateWorldMapState(state));
    }

    [Test]
    public void SectConfirmationStage_DoesNotCountAsCaveForMissionRules()
    {
        FoundingState state = new FoundingState { stage = FoundingStage.SectConfirmation };
        Assert.IsFalse(FoundingRules.HasReachedCave(state));
        state.stage = FoundingStage.Cave;
        Assert.IsTrue(FoundingRules.HasReachedCave(state));
        state.stage = FoundingStage.Completed;
        Assert.IsTrue(FoundingRules.HasReachedCave(state));
    }

    private void AdvanceToConfirmation(PlayerManager player, int seed)
    {
        player.InitializeNewFoundingGame(seed);
        int buildable = WorldMapSession.Current.cells.First(cell => cell.isBuildable).index;
        Assert.IsTrue(player.ConfirmWorldSite(buildable, out string siteReason), siteReason);
        List<string> founders = player.playerData.founding.candidates.Take(3)
            .Select(candidate => candidate.candidateId).ToList();
        Assert.IsTrue(player.ConfirmFounderSelection(founders, out string founderReason), founderReason);
        Assert.IsTrue(player.SelectFoundingTechnique("qingmu", out string techniqueReason), techniqueReason);
    }

    private static void InvokeInfluencePreparation(GameState state)
    {
        MethodInfo method = typeof(SaveManager).GetMethod("PrepareInfluenceStateForValidation",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(null, new object[] { state });
    }

    private T Add<T>(string name) where T : Component
    {
        GameObject obj = new GameObject(name);
        objects.Add(obj);
        return obj.AddComponent<T>();
    }
}
