using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class TechniqueSystemTests
{
    private readonly List<Object> objects = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in objects) if (item != null) Object.DestroyImmediate(item);
        PlayerManager.Instance = null;
        NPCManager.Instance = null;
        MissionManager.Instance = null;
        TechniqueRules.ResetCatalogForTests();
        FoundingRules.ResetCatalogForTests();
    }

    [Test]
    public void CatalogAndStages_MatchApprovedV1Rules()
    {
        Assert.AreEqual(3, TechniqueRules.Catalog.techniques.Count);
        Assert.AreEqual(TechniqueUnderstandingStage.Beginner, TechniqueRules.PersonalStage(29.99f));
        Assert.AreEqual(TechniqueUnderstandingStage.Understanding, TechniqueRules.PersonalStage(30f));
        Assert.AreEqual(TechniqueUnderstandingStage.Integrated, TechniqueRules.PersonalStage(60f));
        Assert.AreEqual(TechniqueUnderstandingStage.Perfected, TechniqueRules.PersonalStage(90f));
        Assert.AreEqual(1f, TechniqueRules.SoftCompatibility(0.2f, 0.2f), 0.0001f);
        Assert.That(TechniqueRules.SoftCompatibility(0f, 0f), Is.InRange(0.9f, 1.1f));
        Assert.That(TechniqueRules.SoftCompatibility(1f, 1f), Is.InRange(0.9f, 1.1f));
    }

    [Test]
    public void TechniqueDoesNotChangeAuraCapacity()
    {
        NPCRuntime npc = Runtime("capacity");
        float withoutTechnique = DailyCultivationSimulator.AuraCapacity(npc);
        Equip(npc, "qingmu", 50f);
        Assert.AreEqual(withoutTechnique, DailyCultivationSimulator.AuraCapacity(npc));
    }

    [Test]
    public void SwitchingMainTechnique_PreservesBothProgressRecords()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        NPCRuntime npc = Runtime("switch");
        player.playerData.techniqueLibrary.Add(new SectTechniqueState { techniqueId = "qingmu" });
        player.playerData.techniqueLibrary.Add(new SectTechniqueState { techniqueId = "taixu" });
        Assert.IsTrue(player.LearnTechnique(npc, "qingmu", true));
        player.AddTechniqueUnderstanding(35f, npc);
        Assert.IsTrue(player.LearnTechnique(npc, "taixu", false));
        Assert.IsTrue(player.SwitchMainTechnique(npc, "taixu"));
        Assert.AreEqual(35f, TechniqueRules.Progress(npc.Character, "qingmu").understanding);
        Assert.AreEqual(0f, TechniqueRules.Progress(npc.Character, "taixu").understanding);
    }

    [Test]
    public void SectMastery_CapsAtThirtyUntilOneAnnotationIsChosen()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        SectTechniqueState state = new SectTechniqueState { techniqueId = "qingmu", masteryProgress = 29f };
        player.playerData.techniqueLibrary.Add(state);
        Assert.AreEqual(1f, player.AddSectTechniqueMastery("qingmu", 5f, null));
        Assert.AreEqual(30f, state.masteryProgress);
        Assert.AreEqual("初窥·待抉择", TechniqueRules.SectStageName(state));
        Assert.IsTrue(player.ResolveTechniqueAnnotation("qingmu|beginner_commentary"));
        Assert.IsFalse(player.ResolveTechniqueAnnotation("qingmu|adaptive_teaching"));
        Assert.AreEqual(5f, player.AddSectTechniqueMastery("qingmu", 5f, null));
        Assert.AreEqual("通达", TechniqueRules.SectStageName(state));
    }

    [Test]
    public void TrainingAddsPersonalUnderstandingAndOnlyOneSectContribution()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        SectTechniqueState sect = new SectTechniqueState { techniqueId = "qingmu" };
        player.playerData.techniqueLibrary.Add(sect);
        NPCRuntime npc = Runtime("training");
        Equip(npc, "qingmu", 0f);
        DailyCultivationResult result = DailyCultivationSimulator.SimulateTrainingDay(npc, 1);
        CultivationActionDefinition action = DailyCultivationSimulator.Definitions.Single(item => item.id == result.selectedActionId);
        float quality = result.outcome == CultivationActionOutcome.Failed ? 0.25f :
            result.outcome == CultivationActionOutcome.Excellent ? 1.5f : 1f;
        Assert.AreEqual(action.techniqueDifficulty * quality * 0.75f, sect.masteryProgress, 0.0001f);
        Assert.Greater(TechniqueRules.MainUnderstanding(npc.Character), 0f);
    }

    [Test]
    public void ApplicationBiasAddsToUtilityScoreWithoutFilteringAction()
    {
        NPCRuntime npc = Runtime("ai");
        Equip(npc, "qingmu", 10f);
        MissionManager missions = Add<MissionManager>("Missions");
        MissionManager.Instance = missions;
        missions.LoadMissionsFromJson();
        ActionDefinition recovery = new ActionDefinition
        {
            id = "recovery", displayName = "静养", missionId = "disciple_ai_rest_001", baseline = 1f,
            identityIds = new List<string> { "inner_disciple" }, tags = new List<string> { "recovery" }
        };
        IdentityDefinition identity = new IdentityDefinition { id = "inner_disciple" };
        ActionScoreResult score = DiscipleAIEvaluator.EvaluateActions(npc, identity,
            new List<GoalInstance>(), new List<ActionDefinition> { recovery }).Single();
        Assert.IsTrue(score.Eligible, score.FilterReason);
        Assert.AreEqual(0.5f, score.TechniqueContribution);
        Assert.AreEqual(1.5f, score.Score);
    }

    private NPCRuntime Runtime(string id)
    {
        NPCData data = ScriptableObject.CreateInstance<NPCData>();
        objects.Add(data);
        data.npcID = id;
        data.npcName = id;
        data.comprehension = 10;
        data.physique = 10;
        return new NPCRuntime(data, new CharacterState
        {
            characterId = id, templateId = id, displayName = id,
            realm = CultivationRealm.QiRefining, realmLayer = 1, health = HealthState.Healthy
        });
    }

    private static void Equip(NPCRuntime npc, string techniqueId, float understanding)
    {
        npc.Character.mainTechniqueId = techniqueId;
        npc.Character.techniqueProgresses.Add(new PersonalTechniqueProgress
            { techniqueId = techniqueId, understanding = understanding });
    }

    private T Add<T>(string name) where T : Component
    {
        GameObject item = new GameObject(name);
        objects.Add(item);
        return item.AddComponent<T>();
    }
}
