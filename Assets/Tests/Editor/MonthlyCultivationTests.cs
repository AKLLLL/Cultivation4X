using System.Collections.Generic;
using System.Linq;
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

    [TestCase(0, 1, 0)]
    [TestCase(1, 1, 1)]
    [TestCase(30, 1, 30)]
    [TestCase(31, 2, 1)]
    public void MonthBoundaries_AreStable(int day, int month, int dayOfMonth)
    {
        Assert.AreEqual(month, MonthlyPlanRules.MonthIndex(day));
        Assert.AreEqual(dayOfMonth, MonthlyPlanRules.DayOfMonth(day));
    }

    [Test]
    public void Template_CreatesThirtyExplicitDaysWithExpectedCounts()
    {
        MonthlyPlanTemplate plan = new MonthlyPlanTemplate { id = "template", name = "标准计划" };
        MonthlyPlanRules.ApplyTemplate(plan);
        Assert.AreEqual(30, plan.days.Count);
        Assert.AreEqual(15, plan.days.Count(item => item == MonthlyActivityType.Training));
        Assert.AreEqual(6, plan.days.Count(item => item == MonthlyActivityType.SectDuty));
        Assert.AreEqual(9, plan.days.Count(item => item == MonthlyActivityType.Free));
    }

    [Test]
    public void MissingPlanAndMissingDays_DefaultToFree()
    {
        Assert.AreEqual(MonthlyActivityType.Free, MonthlyPlanRules.ActivityFor("missing", 1));
        MonthlyPlanTemplate plan = new MonthlyPlanTemplate { id = "template", name = "空计划", days = new List<MonthlyActivityType>() };
        MonthlyPlanRules.Normalize(plan);
        Assert.AreEqual(30, plan.days.Count);
        Assert.IsTrue(plan.days.All(item => item == MonthlyActivityType.Free));
    }

    [Test]
    public void BoundTemplate_RepeatsEveryThirtyDaysAndDeleteUnbindsImplicitly()
    {
        Add<PlayerManager>("Player");
        MonthlyPlanTemplate template = MonthlyPlanRules.CreateTemplate("真传弟子计划");
        Assert.NotNull(template);
        Assert.IsTrue(MonthlyPlanRules.TrySetDay(template, 1, MonthlyActivityType.Training, out _));
        Assert.IsTrue(MonthlyPlanRules.BindDisciple(template.id, "disciple"));
        Assert.AreEqual(MonthlyActivityType.Training, MonthlyPlanRules.ActivityFor("disciple", 1));
        Assert.AreEqual(MonthlyActivityType.Training, MonthlyPlanRules.ActivityFor("disciple", 31));
        Assert.IsTrue(MonthlyPlanRules.DeleteTemplate(template.id));
        Assert.AreEqual(MonthlyActivityType.Free, MonthlyPlanRules.ActivityFor("disciple", 31));
    }

    [Test]
    public void BindingToAnotherTemplate_RemovesPreviousBinding()
    {
        Add<PlayerManager>("Player");
        MonthlyPlanTemplate first = MonthlyPlanRules.CreateTemplate("一");
        MonthlyPlanTemplate second = MonthlyPlanRules.CreateTemplate("二");
        MonthlyPlanRules.BindDisciple(first.id, "disciple");
        MonthlyPlanRules.BindDisciple(second.id, "disciple");
        Assert.IsFalse(first.discipleIds.Contains("disciple"));
        Assert.IsTrue(second.discipleIds.Contains("disciple"));
    }

    [Test]
    public void AutonomousMission_RequiresConsecutiveFutureFreeDaysAndCannotCrossMonth()
    {
        Assert.IsFalse(MonthlyPlanRules.CanStartAutonomousMission(Runtime("late"), 2, 29, out string reason));
        Assert.AreEqual("任务会跨越月末", reason);
    }

    [Test]
    public void SpiritRootTables_MatchApprovedValues()
    {
        Assert.AreEqual(0.2f, SpiritRootRules.AbsorptionMultiplier(SpiritRootQuality.Mixed));
        Assert.AreEqual(6f, SpiritRootRules.AbsorptionMultiplier(SpiritRootQuality.Heavenly));
        Assert.AreEqual(0.95f, SpiritRootRules.LeakageRate(SpiritRootQuality.Mixed));
        Assert.AreEqual(0f, SpiritRootRules.LeakageRate(SpiritRootQuality.Heavenly));
        Assert.AreEqual(0.9f, SpiritRootRules.RefiningMultiplier(SpiritRootQuality.Mixed));
        Assert.AreEqual(1.15f, SpiritRootRules.RefiningMultiplier(SpiritRootQuality.Heavenly));
    }

    [TestCase(6, 35f)]
    [TestCase(10, 50f)]
    [TestCase(15, 65f)]
    [TestCase(18, 80f)]
    [TestCase(20, 100f)]
    public void Physique_DeterminesDerivedLayerOneCapacity(int physique, float expected)
    {
        Assert.AreEqual(expected, DailyCultivationSimulator.AuraCapacity(Runtime("capacity", physique)), 0.001f);
    }

    [Test]
    public void NightLeak_RetainsRemainderAcrossDays()
    {
        NPCRuntime npc = Runtime("leak");
        npc.Character.spiritRoot.quality = SpiritRootQuality.High;
        npc.Character.currentAura = 40f;
        Assert.AreEqual(20f, DailyCultivationSimulator.ApplyNightLeak(npc), 0.001f);
        Assert.AreEqual(20f, npc.Character.currentAura, 0.001f);
        DailyCultivationSimulator.StartDay(npc);
        Assert.AreEqual(20f, npc.Character.currentAura, 0.001f);
    }

    [Test]
    public void NaqiOverflow_AdvancesLayersAndStopsAtThirdLayerCompletion()
    {
        NPCRuntime npc = Runtime("overflow");
        npc.Character.naqiProgress = 95f;
        Assert.AreEqual(205f, RealmProgressionRules.AddNaqi(npc, 210f, 1), 0.001f);
        Assert.AreEqual(3, npc.Character.realmLayer);
        Assert.AreEqual(100f, npc.Character.naqiProgress, 0.001f);
        Assert.AreEqual(0f, RealmProgressionRules.AddNaqi(npc, 10f, 2));
    }

    [Test]
    public void StandardTemplate_CompletesInApprovedCalendarWindow()
    {
        NPCRuntime npc = Runtime("standard");
        npc.Character.spiritRoot.quality = SpiritRootQuality.Medium;
        int completedDay = 0;
        for (int day = 1; day <= 120; day++)
        {
            DailyCultivationSimulator.StartDay(npc);
            if (day % 2 == 1) DailyCultivationSimulator.SimulateTrainingDay(npc, day);
            DailyCultivationSimulator.ApplyNightLeak(npc);
            if (npc.Character.realmLayer == 3 && npc.Character.naqiProgress >= 100f)
            {
                completedDay = day;
                break;
            }
        }
        Assert.That(completedDay, Is.InRange(85, 100));
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

    private NPCRuntime Runtime(string id, int physique = 10)
    {
        NPCData data = ScriptableObject.CreateInstance<NPCData>();
        objects.Add(data);
        data.npcID = id;
        data.npcName = id;
        data.comprehension = 10;
        data.physique = physique;
        return new NPCRuntime(data, new CharacterState
        {
            characterId = id, templateId = id, displayName = id,
            realm = CultivationRealm.QiRefining, realmLayer = 1, health = HealthState.Healthy
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
