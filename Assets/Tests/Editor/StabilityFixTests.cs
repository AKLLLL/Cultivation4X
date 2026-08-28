using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// P0 稳定性修复包的 EditMode 回归测试：
/// 1. EndDay 推进中途异常时复位 isAdvancingDay。
/// 2. 任务节点选项效果预检，失败不产生部分效果。
/// 3. 节点触发日与完成日重叠时，选择选项后当天结算。
/// </summary>
public class StabilityFixTests
{
    private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

    [SetUp]
    public void SetUp()
    {
        PlayerManager.Instance = null;
        WarehouseManager.Instance = null;
        MissionManager.Instance = null;
        NPCManager.Instance = null;
        RewardManager.Instance = null;
        TimeManager.Instance = null;
        UIManager.Instance = null;
        ItemDatabase.Instance = null;
        ExternalThreatPanel.Instance = null;
        ExternalThreatRules.ResetForTests();
        FoundingRules.ResetCatalogForTests();
        Cultivation4X.WorldMap.WorldMapContentEffects.ResetForTests();
        Cultivation4X.WorldMap.WorldMapSession.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (UnityEngine.Object item in created)
            if (item != null) UnityEngine.Object.DestroyImmediate(item);
        created.Clear();
        PlayerManager.Instance = null;
        WarehouseManager.Instance = null;
        MissionManager.Instance = null;
        NPCManager.Instance = null;
        RewardManager.Instance = null;
        TimeManager.Instance = null;
        UIManager.Instance = null;
        ItemDatabase.Instance = null;
        ExternalThreatPanel.Instance = null;
        ExternalThreatRules.ResetForTests();
        FoundingRules.ResetCatalogForTests();
        Cultivation4X.WorldMap.WorldMapContentEffects.ResetForTests();
        Cultivation4X.WorldMap.WorldMapSession.Clear();
    }

    [Test]
    public void EndDay_ThrowingSubscriberResetsAdvancingFlagAndNextDayCanProceed()
    {
        TimeManager time = Add<TimeManager>("Time");
        TimeManager.Instance = time;
        Action<int> throwingSubscriber = _ => throw new InvalidOperationException("OnDayPassed subscriber failure");
        time.OnDayPassed += throwingSubscriber;

        LogAssert.Expect(LogType.Error, new Regex("时间通知 OnDayPassed 订阅者异常"));
        time.EndDay();
        Assert.AreEqual(1, time.CurrentDay);

        // 若 isAdvancingDay 未复位，第二次 EndDay 会被防重入直接忽略。
        LogAssert.Expect(LogType.Error, new Regex("时间通知 OnDayPassed 订阅者异常"));
        time.EndDay();
        Assert.AreEqual(2, time.CurrentDay, "异常后 isAdvancingDay 必须复位，否则日结会永久卡死");

        time.OnDayPassed -= throwingSubscriber;
    }

    [Test]
    public void NodeOption_EffectsArePreflightedAtomicallyAndCanBeRetried()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        WarehouseManager.Instance = warehouse;
        NPCManager npcs = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcs;
        MissionManager missions = Add<MissionManager>("Missions");
        MissionManager.Instance = missions;

        NPCRuntime npc = CreateRuntime(npcs, "preflight-actor", true);
        const string missingItem = "preflight_missing_item";
        MissionData data = new MissionData
        {
            id = "test_preflight_atomic",
            name = "预检测试",
            missionType = MissionType.Sect,
            needDays = 2,
            itemRewards = new List<ItemReward>(),
            nodes = new List<MissionNodeData>
            {
                new MissionNodeData
                {
                    triggerType = "Day",
                    triggerValue = 1,
                    title = "预检节点",
                    description = "先加金币再消耗不存在物品",
                    options = new List<MissionOptionData>
                    {
                        new MissionOptionData
                        {
                            text = "执行",
                            requirementType = "None",
                            effects = new List<MissionEffectData>
                            {
                                new MissionEffectData { type = "AddItem", itemId = FacilityRules.SpiritStoneId, count = 50 },
                                new MissionEffectData { type = "RemoveItem", itemId = missingItem, count = 1 }
                            }
                        }
                    }
                }
            }
        };

        Mission mission = new Mission(data);
        mission.StartMission(npc);
        mission.PassOneDay();
        Assert.AreEqual(MissionState.WaitingNode, mission.State);

        mission.SelectOption(0);

        Assert.AreEqual(MissionState.WaitingNode, mission.State, "预检失败后任务必须停留在 WaitingNode");
        Assert.AreEqual(100, warehouse.GetItemCount(FacilityRules.SpiritStoneId), "RemoveItem 预检失败时前面的 AddItem 不得生效");
        Assert.IsNotNull(mission.NodeFailureReason);
        StringAssert.Contains("材料不足", mission.NodeFailureReason);
        Assert.AreEqual(0, warehouse.GetItemCount(missingItem));

        warehouse.TryAddItem(missingItem, 1);
        mission.SelectOption(0);

        Assert.AreEqual(MissionState.Active, mission.State, "补齐材料后同一节点必须可以再次选择");
        Assert.AreEqual(150, warehouse.GetItemCount(FacilityRules.SpiritStoneId));
        Assert.IsNull(mission.NodeFailureReason);
        Assert.AreEqual(0, warehouse.GetItemCount(missingItem));
    }

    [Test]
    public void FinalDayNode_SettlesOnSameDayWhenOptionIsChosen()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        WarehouseManager.Instance = warehouse;
        RewardManager rewards = Add<RewardManager>("Rewards");
        RewardManager.Instance = rewards;
        NPCManager npcs = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcs;
        MissionManager missions = Add<MissionManager>("Missions");
        MissionManager.Instance = missions;

        NPCRuntime npc = CreateRuntime(npcs, "final-day-actor", true);
        MissionData data = new MissionData
        {
            id = "test_final_day_node",
            name = "最后一天节点",
            missionType = MissionType.Sect,
            needDays = 1,
            itemRewards = new List<ItemReward>(),
            nodes = new List<MissionNodeData>
            {
                new MissionNodeData
                {
                    triggerType = "Day",
                    triggerValue = 1,
                    title = "最后一天节点",
                    description = "最后一天触发",
                    options = new List<MissionOptionData>
                    {
                        new MissionOptionData
                        {
                            text = "立即结算",
                            requirementType = "None",
                            effects = new List<MissionEffectData>()
                        }
                    }
                }
            }
        };

        Mission mission = new Mission(data);
        mission.StartMission(npc);
        missions.AddActiveMission(mission);
        mission.PassOneDay();
        Assert.AreEqual(MissionState.WaitingNode, mission.State);

        mission.SelectOption(0);

        Assert.AreEqual(MissionState.Completed, mission.State, "最后一天触发的节点应在选择选项后当天结算");
        Assert.AreEqual(0, missions.GetActiveMissions().Count, "任务结算后必须从活动列表移除");
        Assert.AreEqual(NPCState.Idle, npc.State);
    }

    private T Add<T>(string name) where T : Component
    {
        GameObject item = new GameObject(name);
        created.Add(item);
        return item.AddComponent<T>();
    }

    private NPCRuntime CreateRuntime(NPCManager manager, string id, bool inject)
    {
        NPCData template = ScriptableObject.CreateInstance<NPCData>();
        created.Add(template);
        template.npcID = id;
        template.npcName = id;
        NPCRuntime runtime = new NPCRuntime(template);
        if (!inject) return runtime;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ((Dictionary<string, NPCRuntime>)typeof(NPCManager).GetField("npcById", flags).GetValue(manager))
            [runtime.CharacterId] = runtime;
        ((List<NPCRuntime>)typeof(NPCManager).GetField("runtimes", flags).GetValue(manager)).Add(runtime);
        return runtime;
    }
}
