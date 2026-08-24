using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cultivation4X.WorldMap;
using NUnit.Framework;
using UnityEngine;

public class WorldMapContentEffectsTests
{
    private readonly List<Object> objects = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        PlayerManager.Instance = null;
        NPCManager.Instance = null;
        WarehouseManager.Instance = null;
        WorldMapContentEffects.ResetForTests();
        WorldMapSession.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in objects) Object.DestroyImmediate(item);
        objects.Clear();
        PlayerManager.Instance = null;
        NPCManager.Instance = null;
        WarehouseManager.Instance = null;
        WorldMapContentEffects.ResetForTests();
        WorldMapSession.Clear();
    }

    [Test]
    public void DailySpiritSpring_DoesNotDirectlyGrantCharacterGrowth()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding = new FoundingState { initialized = true, stage = FoundingStage.Cave };
        NPCManager npcs = Add<NPCManager>("NPCs");
        NPCManager.Instance = npcs;
        npcs.ClearCharacters();
        NPCRuntime idle = Register(npcs, "idle");
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        WarehouseManager.Instance = warehouse;
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 16, height = 16, seed = 9201 });
        WorldMapProgressState progress = new WorldMapProgressState
        {
            mapSites = new List<MapSiteData>
            {
                Developed(MapSiteType.SpiritSpring, 5), Developed(MapSiteType.SpiritMine, 6)
            }
        };
        WorldMapSession.Set(map, progress);
        float beforeAura = idle.CurrentAura;
        int beforeMaterials = warehouse.GetItemCount(FacilityRules.BasicMaterialId);

        WorldMapContentEffects.ApplyDaily(1);
        WorldMapContentEffects.ApplyDaily(1);
        Assert.AreEqual(beforeAura, idle.CurrentAura);
        Assert.AreEqual(beforeMaterials, warehouse.GetItemCount(FacilityRules.BasicMaterialId));
        WorldMapContentEffects.ApplyDaily(2);
        Assert.AreEqual(beforeAura, idle.CurrentAura);
        StringAssert.Contains("环境灵气吸收效率+10%", WorldMapContentEffects.EffectSummary(MapSiteType.SpiritSpring));
    }

    [Test]
    public void CompletionEffects_AreAppliedOnce()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding = new FoundingState
        {
            initialized = true, completed = false, stage = FoundingStage.Cave,
            village = new VillageState(), techniqueUnderstanding = 0
        };
        WarehouseManager warehouse = Add<WarehouseManager>("Warehouse");
        WarehouseManager.Instance = warehouse;
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings { width = 16, height = 16, seed = 9202 });
        MapSiteData village = Developed(MapSiteType.Village, 5);
        MapSiteData ruin = new MapSiteData { siteId = "ruin", siteType = MapSiteType.Ruin,
            cellIndex = 6, siteState = MapSiteState.Investigated };
        WorldMapSession.Set(map, new WorldMapProgressState { mapSites = new List<MapSiteData> { village, ruin } });

        WorldMapContentEffects.ApplySiteCompletion(village, MapActionType.EstablishVillageRelation, 1);
        WorldMapContentEffects.ApplySiteCompletion(village, MapActionType.EstablishVillageRelation, 1);
        Assert.AreEqual(15, player.playerData.founding.village.relation);
        Assert.AreEqual(10, player.playerData.reputation);

        WorldMapContentEffects.ApplySiteCompletion(ruin, MapActionType.InvestigateRuin, 1);
        WorldMapContentEffects.ApplySiteCompletion(ruin, MapActionType.InvestigateRuin, 1);
        Assert.AreEqual(5, player.playerData.founding.techniqueUnderstanding);
    }

    [Test]
    public void BeastLairClearance_SuppressesOrDelaysThreatNode()
    {
        PlayerManager player = Add<PlayerManager>("Player");
        PlayerManager.Instance = player;
        player.playerData.founding = new FoundingState
        {
            initialized = true, stage = FoundingStage.Cave, village = new VillageState(),
            externalThreat = new ActiveThreatState()
        };
        Assert.IsTrue(ExternalThreatRules.ApplyBeastLairClearance(3));
        Assert.AreEqual(ExternalThreatStatus.Resolved, player.playerData.founding.externalThreat.status);

        player.playerData.founding.externalThreat = new ActiveThreatState
        {
            threatId = ExternalThreatRules.QingshiThreatId,
            status = ExternalThreatStatus.Scheduled, scheduledDay = 10
        };
        Assert.IsTrue(ExternalThreatRules.ApplyBeastLairClearance(3));
        Assert.AreEqual(11, player.playerData.founding.externalThreat.scheduledDay);
        player.playerData.founding.externalThreat.status = ExternalThreatStatus.Active;
        player.playerData.founding.externalThreat.nextRaidDay = 20;
        Assert.IsTrue(ExternalThreatRules.ApplyBeastLairClearance(3));
        Assert.AreEqual(21, player.playerData.founding.externalThreat.nextRaidDay);
    }

    private T Add<T>(string name) where T : Component
    {
        GameObject item = new GameObject(name);
        objects.Add(item);
        return item.AddComponent<T>();
    }

    private static MapSiteData Developed(MapSiteType type, int index) => new MapSiteData
    {
        siteId = type.ToString().ToLowerInvariant(), siteType = type, cellIndex = index,
        siteState = MapSiteState.Developed, ownerSectId = "player_sect"
    };

    private static NPCRuntime Register(NPCManager manager, string id)
    {
        NPCData template = ScriptableObject.CreateInstance<NPCData>();
        template.npcID = id; template.npcName = id;
        NPCRuntime runtime = new NPCRuntime(template);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ((Dictionary<string, NPCRuntime>)typeof(NPCManager).GetField("npcById", flags).GetValue(manager))[id] = runtime;
        ((List<NPCRuntime>)typeof(NPCManager).GetField("runtimes", flags).GetValue(manager)).Add(runtime);
        return runtime;
    }
}
