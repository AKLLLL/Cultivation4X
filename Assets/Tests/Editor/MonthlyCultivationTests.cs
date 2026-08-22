using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class MonthlyCultivationTests
{
    private readonly List<Object> objects = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in objects) if (item != null) Object.DestroyImmediate(item);
        PlayerManager.Instance = null;
        NPCManager.Instance = null;
        MissionManager.Instance = null;
        WarehouseManager.Instance = null;
    }

    [TestCase(0, 1, 0, 1)]
    [TestCase(1, 1, 1, 2)]
    [TestCase(30, 1, 30, 2)]
    [TestCase(31, 2, 1, 3)]
    public void MonthBoundaries_AreStable(int day, int month, int dayOfMonth, int editable)
    {
        Assert.AreEqual(month, MonthlyPlanRules.MonthIndex(day));
        Assert.AreEqual(dayOfMonth, MonthlyPlanRules.DayOfMonth(day));
        Assert.AreEqual(editable, MonthlyPlanRules.EditableMonth(day));
    }

    [Test]
    public void PlanValidation_RequiresTenPercentStepsAndExactSum()
    {
        MonthlyDisciplePlan plan = new MonthlyDisciplePlan { characterId = "a" };
        Assert.IsTrue(MonthlyPlanRules.TrySetPlan(plan, 50, 20, 30, out _));
        Assert.IsFalse(MonthlyPlanRules.TrySetPlan(plan, 45, 25, 30, out _));
        Assert.IsFalse(MonthlyPlanRules.TrySetPlan(plan, 50, 20, 20, out _));
    }

    [Test]
    public void AuraCurve_IsIncrementalAndMajorCycleOnlyOnce()
    {
        NPCRuntime npc = Runtime("curve");
        Assert.AreEqual(0.5f, NaqiGrowthRules.AddDailyAura(npc, 50), 0.0001f);
        Assert.AreEqual(3.5f, NaqiGrowthRules.AddDailyAura(npc, 50), 0.0001f);
        Assert.AreEqual(4f, npc.Character.naqiProgress, 0.0001f);
        Assert.AreEqual(1f, npc.Character.techniqueMastery, 0.0001f);
        Assert.AreEqual(0f, NaqiGrowthRules.AddDailyAura(npc, 50), 0.0001f);
    }

    [Test]
    public void StartDay_ResetsAuraButKeepsNaqiProgress()
    {
        NPCRuntime npc = Runtime("reset");
        NaqiGrowthRules.AddDailyAura(npc, 50);
        NaqiGrowthRules.StartDay(npc);
        Assert.AreEqual(0, npc.Cultivation);
        Assert.AreEqual(0.5f, npc.Character.naqiProgress, 0.0001f);
    }

    [Test]
    public void AutonomousMission_DayTwentyNineCannotStartTwoDayMission()
    {
        Assert.IsFalse(MonthlyPlanRules.CanStartAutonomousMission(Runtime("late"), 2, 29, out string reason));
        Assert.AreEqual("任务会跨越月末", reason);
    }

    [Test]
    public void DeficitScheduler_ProducesExactFiftyTwentyThirtyMonth()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        NPCRuntime npc = Runtime("schedule");
        player.playerData.monthlyPlans.Add(new MonthlyDisciplePlan
        {
            characterId = npc.CharacterId, monthIndex = 1,
            trainingPercent = 50, sectDutyPercent = 20, freePercent = 30
        });
        for (int day = 1; day <= 30; day++)
        {
            MonthlyActivityType activity = MonthlyPlanRules.PeekScheduledActivity(npc, day);
            MonthlyPlanRules.Consume(npc, day, activity);
        }
        MonthlyDisciplePlan plan = player.playerData.monthlyPlans[0];
        Assert.AreEqual(15, plan.usedTrainingDays);
        Assert.AreEqual(6, plan.usedSectDutyDays);
        Assert.AreEqual(9, plan.usedFreeDays);
    }

    [Test]
    public void NaqiCompletion_TransfersOnlyRemainingTrainingDays()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        NPCRuntime npc = Runtime("transfer");
        MonthlyDisciplePlan plan = new MonthlyDisciplePlan
        {
            characterId = npc.CharacterId, monthIndex = 1,
            trainingPercent = 50, sectDutyPercent = 20, freePercent = 30,
            usedTrainingDays = 5
        };
        player.playerData.monthlyPlans.Add(plan);
        MonthlyPlanRules.TransferRemainingTrainingToFree(npc, 1);
        Assert.AreEqual(10, plan.transferredTrainingDays);
        Assert.AreEqual(19, MonthlyPlanRules.RemainingFreeDays(npc, 1));
        Assert.AreEqual(50, plan.trainingPercent, "预算转移不得破坏10%步进的计划比例");
    }

    [Test]
    public void DisorderCountdown_PausesForTenCompleteDays()
    {
        NPCRuntime npc = Runtime("disorder");
        npc.Character.qiDisorderResponse = QiDisorderResponse.Paused;
        npc.Character.qiDisorderRemainingDays = 10;
        for (int i = 0; i < 9; i++) NaqiGrowthRules.EndDay(npc);
        Assert.AreEqual(QiDisorderResponse.Paused, npc.Character.qiDisorderResponse);
        Assert.AreEqual(1, npc.Character.qiDisorderRemainingDays);
        NaqiGrowthRules.EndDay(npc);
        Assert.AreEqual(QiDisorderResponse.None, npc.Character.qiDisorderResponse);
        Assert.AreEqual(0, npc.Character.qiDisorderRemainingDays);
    }

    [Test]
    public void TrainingStoneToggle_ConsumesExactlyOneStone()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        warehouse.AddItem(NaqiGrowthRules.SpiritStoneId, 2);
        int before = warehouse.GetItemCount(NaqiGrowthRules.SpiritStoneId);
        NPCRuntime npc = Runtime("stone");
        player.playerData.monthlyPlans.Add(new MonthlyDisciplePlan
        {
            characterId = npc.CharacterId, monthIndex = 1,
            trainingPercent = 100, sectDutyPercent = 0, freePercent = 0, useSpiritStone = true
        });
        NaqiGrowthRules.ProcessTrainingDay(npc, 1);
        Assert.AreEqual(before - 1, warehouse.GetItemCount(NaqiGrowthRules.SpiritStoneId));
        Assert.Greater(npc.Cultivation, 0);
    }

    [Test]
    public void PlayerCanCancelActiveAutonomousMissionWithoutFailure()
    {
        Add<PlayerManager>("Player");
        Add<WarehouseManager>("Warehouse");
        Add<NPCManager>("NPCs");
        MissionManager missions = Add<MissionManager>("Missions");
        NPCRuntime npc = Runtime("cancel");
        Mission mission = new Mission(new MissionData
        {
            id = "disciple_ai_test", name = "自主测试", isPlayerAssignable = false,
            needDays = 3, itemRewards = new List<ItemReward>(), nodes = new List<MissionNodeData>()
        });
        mission.StartMission(npc);
        missions.AddActiveMission(mission);
        Assert.IsTrue(missions.TryCancelAutonomousMission(npc, out _));
        Assert.AreEqual(MissionState.Cancelled, mission.State);
        Assert.AreEqual(NPCState.Idle, npc.State);
        Assert.IsEmpty(missions.GetActiveMissions());
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
            realm = CultivationRealm.QiRefining, health = HealthState.Healthy
        });
    }

    private T Add<T>(string name) where T : Component
    {
        GameObject obj = new GameObject(name);
        objects.Add(obj);
        T component = obj.AddComponent<T>();
        if (component is PlayerManager player) PlayerManager.Instance = player;
        if (component is NPCManager npcs) NPCManager.Instance = npcs;
        if (component is MissionManager missions) MissionManager.Instance = missions;
        if (component is WarehouseManager warehouse) WarehouseManager.Instance = warehouse;
        return component;
    }
}
