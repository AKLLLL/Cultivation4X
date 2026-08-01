using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public void VersionNineSnapshot_RoundTripsSectProgressAndRejectsInconsistentBase()
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

        Assert.AreEqual(9, state.version);
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
        Assert.Throws<InvalidDataException>(() =>
            SaveManager.ValidateWorldMapState(missingTechnique));

        restored.worldMapProgress.mapSites.Single().cellIndex =
            restored.worldMap.cells.Length - 1;
        Assert.Throws<InvalidDataException>(() =>
            SaveManager.ValidateWorldMapState(restored));
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

    private T Add<T>(string name) where T : Component
    {
        GameObject obj = new GameObject(name);
        objects.Add(obj);
        return obj.AddComponent<T>();
    }
}
