using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class SectVitalityTests
{
    private readonly List<Object> objects = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        PlayerManager.Instance = null;
        WarehouseManager.Instance = null;
        MissionManager.Instance = null;
        NPCManager.Instance = null;
        RewardManager.Instance = null;
        UIManager.Instance = null;
        FoundingRules.ResetCatalogForTests();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in objects) Object.DestroyImmediate(item);
        objects.Clear();
        PlayerManager.Instance = null;
        WarehouseManager.Instance = null;
        MissionManager.Instance = null;
        NPCManager.Instance = null;
        RewardManager.Instance = null;
        UIManager.Instance = null;
        FoundingRules.ResetCatalogForTests();
    }

    [Test]
    public void CombatPower_UsesRealmExperienceAndEquipmentWithoutLegacyTechniqueFlatBonus()
    {
        CreatePlayerWithTechnique("chiyang", 100);
        NPCRuntime npc = CreateRuntime(10, 8, 9, 7, CultivationRealm.QiRefining, 140);
        Assert.AreEqual(10 * 2 + 8 + 9 * 2 + 7 + 100 + 20 + 6,
            CharacterCapabilityRules.CalculateCombatPower(npc, 6));
    }

    [Test]
    public void CandidateGeneration_ProducesDifferentCombatPowerValues()
    {
        List<FounderCandidateData> candidates = FoundingRules.GenerateCandidates(418);
        Assert.AreEqual(10, candidates.Count);
        Assert.Greater(candidates.Select(CharacterCapabilityRules.CalculateCandidateCombatPower).Distinct().Count(), 1);
    }

    [Test]
    public void TechniqueCatalog_UsesApprovedLightweightProfiles()
    {
        Assert.AreEqual(1.08f, TechniqueRules.Get("qingmu").absorptionMultiplier);
        Assert.AreEqual(1.08f, TechniqueRules.Get("chiyang").refiningMultiplier);
        Assert.AreEqual(0.05f, TechniqueRules.Get("taixu").stabilityModifier);
        Assert.IsTrue(TechniqueRules.Get("qingmu").tags.Contains("alchemy"));
    }

    [Test]
    public void MissionCapability_EvaluatesInsufficientQualifiedAndExcellent()
    {
        MissionData data = new MissionData { requiredAttack = 10, requiredCombatPower = 70, excellentScore = 130 };
        NPCRuntime weak = CreateRuntime(9, 1, 1, 1, CultivationRealm.Mortal, 0);
        Assert.AreEqual(MissionResultTier.Insufficient, CharacterCapabilityRules.EvaluateMission(data, weak).tier);

        NPCRuntime qualified = CreateRuntime(10, 15, 15, 10, CultivationRealm.Mortal, 0);
        Assert.AreEqual(MissionResultTier.Qualified, CharacterCapabilityRules.EvaluateMission(data, qualified).tier);

        data.preferredTraitIds = new List<string> { "diligent" };
        data.preferredTechniqueTags = new List<string> { "combat" };
        PlayerManager player = CreatePlayerWithTechnique("chiyang", 100);
        Assert.IsTrue(player.LearnTechnique(qualified, "chiyang", true, true));
        player.AddTechniqueUnderstanding(100f, qualified);
        qualified.Character.AddTrait("diligent");
        data.excellentScore = 120;
        MissionCapabilityEvaluation excellent = CharacterCapabilityRules.EvaluateMission(data, qualified);
        Assert.IsTrue(excellent.techniqueMatched);
        Assert.IsTrue(excellent.traitMatched);
        Assert.AreEqual(MissionResultTier.Excellent, excellent.tier);
    }

    [Test]
    public void MissionSnapshot_PersistsCapabilityTierAndScore()
    {
        MissionData data = new MissionData { id = "snapshot", requiredAttack = 10 };
        NPCRuntime npc = CreateRuntime(15, 1, 1, 1, CultivationRealm.Mortal, 0);
        Mission mission = new Mission(data, new MissionSaveData { state = MissionState.Active }, npc);
        MissionSaveData saved = mission.ToSaveData();
        Assert.IsTrue(saved.hasCapabilitySnapshot);
        Assert.AreEqual(mission.CapabilityScore, saved.capabilityScore);
        Assert.AreEqual(mission.ResultTier, saved.resultTier);
    }

    [Test]
    public void ProtectionArrayAvailability_ReducesFailureInjuryWithoutUpgradeApi()
    {
        Assert.AreEqual(3, FacilityRules.FailureInjuryDays(false));
        Assert.AreEqual(1, FacilityRules.FailureInjuryDays(true));
        GameObject go = new GameObject("Player");
        objects.Add(go);
        PlayerManager player = go.AddComponent<PlayerManager>();
        player.SetFacilityAvailableForStory(FacilityType.MissionHall, false);
        Assert.IsFalse(player.HasFacility(FacilityType.MissionHall));
    }

    [Test]
    public void V4Migration_FillsGeneratedCombatDefaults()
    {
        GameState state = new GameState
        {
            version = 4,
            sect = new PlayerData(),
            characters = new List<CharacterState>
            {
                new CharacterState { hasGeneratedProfile = true, baseComprehension = 14, baseCombatComprehension = 0, combatExperience = -3 }
            }
        };
        SaveManager.MigrateState(state);
        Assert.AreEqual(SaveDataVersion.Current, state.version);
        Assert.AreEqual(14, state.characters[0].baseCombatComprehension);
        Assert.AreEqual(0, state.characters[0].combatExperience);
    }

    [Test]
    public void AwaitingExcellentCombatReward_GrantsCombatExperienceWhenClaimed()
    {
        PlayerManager.Instance = Add<PlayerManager>("Player");
        WarehouseManager.Instance = Add<WarehouseManager>("Warehouse");
        RewardManager.Instance = Add<RewardManager>("Rewards");
        MissionManager manager = Add<MissionManager>("Missions");
        MissionManager.Instance = manager;
        MissionData data = new MissionData { id = "combat-claim", name = "战斗任务", missionType = MissionType.Combat };
        NPCRuntime npc = CreateRuntime(10, 10, 10, 10, CultivationRealm.QiRefining, 0);
        Mission mission = new Mission(data, new MissionSaveData
        {
            state = MissionState.AwaitingReward,
            reward = new Reward(),
            hasCapabilitySnapshot = true,
            capabilityScore = 150,
            resultTier = MissionResultTier.Excellent
        }, npc);

        Assert.IsTrue(manager.TryClaimReward(mission, out string reason), reason);
        Assert.AreEqual(5, npc.CombatExperience);
        Assert.AreEqual(MissionState.Completed, mission.State);
    }

    [Test]
    public void FailedCombatMission_GrantsOneExperienceAndRecordsTier()
    {
        PlayerManager.Instance = Add<PlayerManager>("Player");
        NPCManager.Instance = Add<NPCManager>("NPCs");
        MissionManager manager = Add<MissionManager>("Missions");
        MissionManager.Instance = manager;
        NPCRuntime npc = CreateRuntime(5, 5, 5, 5, CultivationRealm.Mortal, 0);
        Mission mission = new Mission(new MissionData
        {
            id = "combat-failure",
            name = "战斗失败测试",
            missionType = MissionType.Combat,
            requiredCombatPower = 999,
            needDays = 1
        });
        mission.StartMission(npc);

        manager.EvaluateMission(mission);

        Assert.AreEqual(1, npc.CombatExperience);
        Assert.AreEqual(MissionState.Failed, mission.State);
        Assert.IsTrue(npc.Character.lifeRecords.Any(item => item.text.Contains("能力不足")));
    }

    [Test]
    public void ExcellentReward_AddsHalfSpiritStonesAndCultivationButNotOtherItems()
    {
        Mission mission = new Mission(new MissionData { id = "excellent-bonus" },
            new MissionSaveData
            {
                state = MissionState.Active,
                hasCapabilitySnapshot = true,
                resultTier = MissionResultTier.Excellent,
                reward = new Reward
                {
                    Items = new List<ItemReward>
                    {
                        new ItemReward { itemId = "item", count = 2 },
                        new ItemReward { itemId = FacilityRules.SpiritStoneId, count = 3 }
                    }
                }
            }, null);

        mission.ApplyExcellentRewardBonus();

        Assert.AreEqual(4, mission.Reward.Items.Single(item =>
            item.itemId == FacilityRules.SpiritStoneId).count);
        Assert.AreEqual(2, mission.Reward.Items[0].count);
    }

    [Test]
    public void StateDrivenMissions_UseReputationAndIgnoreDayRandomness()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding.sectCreated = true;
        player.playerData.founding.completed = true;
        player.playerData.founding.stage = FoundingStage.Completed;
        MissionManager manager = Add<MissionManager>("Missions");
        MissionManager.Instance = manager;
        manager.LoadMissionsFromJson();

        MissionData rankTwo = new MissionData { id = "rank-two", missionRank = 2 };
        MissionData rankThree = new MissionData { id = "rank-three", missionRank = 3 };
        Assert.IsFalse(manager.IsMissionVisible(rankTwo));
        player.playerData.reputation = 100;
        Assert.IsTrue(manager.IsMissionVisible(rankTwo));
        Assert.IsFalse(manager.IsMissionVisible(rankThree));
        player.playerData.reputation = 300;
        Assert.IsTrue(manager.IsMissionVisible(rankThree));

        manager.RefreshDailyCandidates(1, true);
        string[] first = manager.GetDailyMissionCandidateIds().ToArray();
        manager.RefreshDailyCandidates(99, true);
        CollectionAssert.AreEqual(first, manager.GetDailyMissionCandidateIds());
    }

    [Test]
    public void UIManager_AssignsVisualOrderFromPanelStack()
    {
        UIManager manager = Add<UIManager>("UI");
        GameObject first = new GameObject("First", typeof(RectTransform));
        GameObject second = new GameObject("Second", typeof(RectTransform));
        objects.Add(first);
        objects.Add(second);
        first.SetActive(false);
        second.SetActive(false);
        bool secondClosed = false;

        manager.OpenPanel(first);
        manager.OpenPanel(second, () => secondClosed = true);
        Assert.Greater(second.GetComponent<Canvas>().sortingOrder, first.GetComponent<Canvas>().sortingOrder);

        manager.CloseTopPanel();
        Assert.IsTrue(secondClosed);
        Assert.IsFalse(second.activeSelf);
        Assert.IsTrue(first.activeSelf);
    }

    private PlayerManager CreatePlayerWithTechnique(string techniqueId, int understanding)
    {
        GameObject go = new GameObject("Player");
        objects.Add(go);
        PlayerManager player = go.AddComponent<PlayerManager>();
        PlayerManager.Instance = player;
        player.playerData.founding.selectedTechniqueId = techniqueId;
        player.playerData.techniqueLibrary.Add(new SectTechniqueState { techniqueId = techniqueId });
        return player;
    }

    private NPCRuntime CreateRuntime(int attack, int agility, int physique, int combatComprehension,
        CultivationRealm realm, int combatExperience)
    {
        NPCData data = ScriptableObject.CreateInstance<NPCData>();
        objects.Add(data);
        data.npcID = System.Guid.NewGuid().ToString("N");
        data.npcName = "test";
        data.attack = attack;
        data.agility = agility;
        data.physique = physique;
        data.combatComprehension = combatComprehension;
        CharacterState state = new CharacterState
        {
            characterId = data.npcID,
            baseAttack = attack,
            baseAgility = agility,
            basePhysique = physique,
            baseCombatComprehension = combatComprehension,
            realm = realm,
            combatExperience = combatExperience
        };
        string techniqueId = PlayerManager.Instance?.playerData?.founding?.selectedTechniqueId;
        if (!string.IsNullOrWhiteSpace(techniqueId))
        {
            state.mainTechniqueId = techniqueId;
            state.techniqueProgresses.Add(new PersonalTechniqueProgress { techniqueId = techniqueId, understanding = 100f });
        }
        return new NPCRuntime(data, state);
    }

    private T Add<T>(string name) where T : Component
    {
        GameObject item = new GameObject(name);
        objects.Add(item);
        return item.AddComponent<T>();
    }
}
