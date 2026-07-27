using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

public class EventInboxTests
{
    private readonly List<Object> created = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        NPCManager.Instance = null;
        MissionManager.Instance = null;
        PlayerManager.Instance = null;
        WarehouseManager.Instance = null;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in created) Object.DestroyImmediate(item);
        created.Clear();
        NPCManager.Instance = null;
        MissionManager.Instance = null;
        PlayerManager.Instance = null;
        WarehouseManager.Instance = null;
    }

    [Test]
    public void LegacyEventDefinitions_AreMigratedToSourcesAndSafeDefaults()
    {
        EventManager manager = Add<EventManager>("Events");
        manager.LoadDefinitions();
        Assert.GreaterOrEqual(manager.GetDefinitions().Count, 20);
        Assert.IsTrue(manager.GetDefinitions().All(item => item.sources.Count > 0));
        Assert.IsTrue(manager.GetDefinitions().All(item => item.expiresAfterDays > 0));
        Assert.IsTrue(manager.GetDefinitions().All(item => !string.IsNullOrEmpty(item.defaultOptionId)));
        Assert.AreEqual("destroy", manager.GetDefinitions().Single(item => item.id == "forbidden_method").defaultOptionId);
        Assert.AreEqual("mark", manager.GetDefinitions().Single(item => item.id == "dangerous_ruins").defaultOptionId);
        Assert.AreEqual("contain", manager.GetDefinitions().Single(item => item.id == "alchemy_fire").defaultOptionId);
    }

    [Test]
    public void CriticalInboxEntry_BlocksDayAdvance()
    {
        NPCManager npcs = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcs;
        NPCRuntime actor = CreateRuntime(npcs, "critical-actor");
        EventManager manager = Add<EventManager>("Events");
        manager.LoadDefinitions();
        manager.RestoreState(null, null, 1, 0, new[]
        {
            new EventInboxEntry { entryId = "critical", eventId = "old_enemy", createdDay = 1, expiresDay = -1,
                participantIds = new Dictionary<string, string> { { "actor", actor.CharacterId } } }
        });
        Assert.IsFalse(manager.PrepareForDayAdvance(1, out string reason));
        Assert.IsNotEmpty(reason);
    }

    [Test]
    public void InvalidCriticalInboxEntry_IsCancelledBeforeBlocking()
    {
        EventManager manager = Add<EventManager>("Events");
        manager.LoadDefinitions();
        manager.RestoreState(null, null, 1, 0, new[]
        {
            new EventInboxEntry { entryId = "invalid", eventId = "old_enemy", createdDay = 1, expiresDay = -1 }
        });
        Assert.IsTrue(manager.PrepareForDayAdvance(1, out _));
        Assert.IsEmpty(manager.GetInbox());
        Assert.AreEqual("cancelled", manager.GetHistory().Single().optionId);
    }

    [Test]
    public void FullInbox_BlocksWithoutDroppingEntries()
    {
        NPCManager npcs = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcs;
        NPCRuntime actor = CreateRuntime(npcs, "inbox-actor");
        EventManager manager = Add<EventManager>("Events");
        manager.LoadDefinitions();
        List<EventInboxEntry> entries = Enumerable.Range(0, EventManager.InboxCapacity)
            .Select(i => new EventInboxEntry { entryId = "e" + i, eventId = "cultivation_insight", createdDay = 1, expiresDay = 99,
                participantIds = new Dictionary<string, string> { { "actor", actor.CharacterId } } }).ToList();
        manager.RestoreState(null, null, 1, 0, entries);
        Assert.IsFalse(manager.PrepareForDayAdvance(1, out _));
        Assert.AreEqual(EventManager.InboxCapacity, manager.GetInbox().Count);
    }

    [Test]
    public void SaveRoundTrip_PreservesInboxActiveIdAndSettlement()
    {
        GameState source = new GameState
        {
            activeEventEntryId = "e1",
            nextInboxSequence = 4,
            eventGeneratedDay = 8,
            eventGeneratedOrdinaryCount = 2,
            eventInbox = new List<EventInboxEntry> { new EventInboxEntry { entryId = "e1", eventId = "wounded_beast" } },
            unreadDaySettlement = new DaySettlementSummary { day = 8, goldChange = 12 }
        };
        GameState restored = JsonConvert.DeserializeObject<GameState>(JsonConvert.SerializeObject(source));
        Assert.AreEqual("e1", restored.activeEventEntryId);
        Assert.AreEqual(4, restored.nextInboxSequence);
        Assert.AreEqual(8, restored.eventGeneratedDay);
        Assert.AreEqual(2, restored.eventGeneratedOrdinaryCount);
        Assert.AreEqual("wounded_beast", restored.eventInbox[0].eventId);
        Assert.AreEqual(12, restored.unreadDaySettlement.goldChange);
    }

    [Test]
    public void RestoreState_DropsBlankParticipantIdsBeforeOpeningActiveEntry()
    {
        NPCManager npcs = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcs;
        NPCRuntime actor = CreateRuntime(npcs, "restore-actor");
        EventManager manager = Add<EventManager>("Events");
        manager.LoadDefinitions();
        manager.RestoreState(null, null, 1, 0, new[]
        {
            new EventInboxEntry
            {
                entryId = "active",
                eventId = "old_enemy",
                createdDay = 1,
                expiresDay = -1,
                participantIds = new Dictionary<string, string>
                {
                    { string.Empty, actor.CharacterId },
                    { "actor", actor.CharacterId }
                }
            }
        }, "active", 1);

        Assert.IsNotNull(manager.GetActiveEvent());
        Assert.IsFalse(manager.GetInbox().Single().participantIds.ContainsKey(string.Empty));
    }

    [Test]
    public void Injury_OnlySeriousInjuryTerminatesMission()
    {
        NPCManager npcs = Add<NPCManager>("NPCs");
        MissionManager missions = Add<MissionManager>("Missions");
        NPCManager.Instance = npcs;
        MissionManager.Instance = missions;
        NPCData template = ScriptableObject.CreateInstance<NPCData>();
        created.Add(template);
        template.npcID = "test"; template.npcName = "test";
        NPCRuntime npc = new NPCRuntime(template);
        Mission mission = new Mission(new MissionData { id = "test", name = "test", needDays = 3 });
        mission.StartMission(npc); missions.AddActiveMission(mission);

        npcs.Injured(npc, 2);
        Assert.AreEqual(MissionState.Active, mission.State);
        Assert.AreSame(mission, npc.CurrentMission);
        npcs.Injured(npc, 5);
        Assert.AreEqual(MissionState.Failed, mission.State);
        Assert.IsNull(npc.CurrentMission);
        Assert.AreEqual(NPCState.Injured, npc.State);
    }

    [Test]
    public void MissionFailure_PreservesItsLightInjury()
    {
        NPCManager npcs = Add<NPCManager>("NPCs");
        MissionManager missions = Add<MissionManager>("Missions");
        NPCManager.Instance = npcs;
        MissionManager.Instance = missions;
        NPCData template = ScriptableObject.CreateInstance<NPCData>();
        created.Add(template);
        template.npcID = "failed"; template.npcName = "failed";
        NPCRuntime npc = new NPCRuntime(template);
        Mission mission = new Mission(new MissionData { id = "failed", name = "failed", needDays = 1 });
        mission.StartMission(npc); missions.AddActiveMission(mission);
        mission.FailMission();
        Assert.AreEqual(HealthState.LightInjury, npc.Health);
        Assert.AreEqual(NPCState.Injured, npc.State);
        Assert.IsNull(npc.CurrentMission);
    }

    private T Add<T>(string name) where T : Component
    {
        GameObject item = new GameObject(name);
        created.Add(item);
        return item.AddComponent<T>();
    }

    private NPCRuntime CreateRuntime(NPCManager manager, string id)
    {
        NPCData template = ScriptableObject.CreateInstance<NPCData>();
        created.Add(template);
        template.npcID = id;
        template.npcName = id;
        NPCRuntime runtime = new NPCRuntime(template);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        ((Dictionary<string, NPCRuntime>)typeof(NPCManager).GetField("npcById", flags).GetValue(manager))[id] = runtime;
        ((List<NPCRuntime>)typeof(NPCManager).GetField("runtimes", flags).GetValue(manager)).Add(runtime);
        return runtime;
    }
}
