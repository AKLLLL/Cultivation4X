using System;
using System.Collections.Generic;
using System.Linq;

public sealed class DiscipleCenterContext : IUIWindowContext
{
    public string CharacterId { get; }

    public DiscipleCenterContext(string characterId)
    {
        CharacterId = characterId;
    }
}

public sealed class DiscipleListItemSnapshot
{
    public string characterId;
    public string name;
    public string realm;
    public string state;
    public float naqiProgress;
}

public sealed class DiscipleHistoryItemSnapshot
{
    public string type;
    public string heading;
    public string body;
}

public sealed class DiscipleCenterSnapshot
{
    public readonly List<DiscipleListItemSnapshot> disciples = new List<DiscipleListItemSnapshot>();
    public string selectedCharacterId;
    public string name;
    public string realm;
    public string state;
    public string health;
    public int age;
    public int mentalState;
    public float currentAura;
    public float auraCapacity;
    public float auraControl;
    public float fatigue;
    public float naqiProgress;
    public float techniqueMastery;
    public string overview;
    public string abilities;
    public string relationships;
    public string history;
    public string observation;
    public string currentAction;
    public string currentPlan;
    public string nextPlan;
    public readonly List<DiscipleHistoryItemSnapshot> historyItems = new List<DiscipleHistoryItemSnapshot>();

    public bool HasSelection => !string.IsNullOrWhiteSpace(selectedCharacterId);
}

public static class DiscipleCenterSnapshotBuilder
{
    public static DiscipleCenterSnapshot Build(string selectedCharacterId = null)
    {
        IReadOnlyList<NPCRuntime> roster = NPCManager.Instance?.GetAllNPC();
        int day = TimeManager.Instance?.CurrentDay ?? 0;
        return Build(roster, selectedCharacterId, day);
    }

    public static DiscipleCenterSnapshot Build(IReadOnlyList<NPCRuntime> roster,
        string selectedCharacterId, int day)
    {
        DiscipleCenterSnapshot snapshot = new DiscipleCenterSnapshot();
        List<NPCRuntime> living = (roster ?? Array.Empty<NPCRuntime>())
            .Where(item => item?.Character?.IsAlive == true)
            .OrderBy(item => DisplayName(item), StringComparer.Ordinal)
            .ToList();

        foreach (NPCRuntime npc in living)
        {
            snapshot.disciples.Add(new DiscipleListItemSnapshot
            {
                characterId = npc.CharacterId,
                name = DisplayName(npc),
                realm = Realm(npc),
                state = State(npc.State),
                naqiProgress = npc.Character.naqiProgress
            });
        }

        NPCRuntime selected = living.FirstOrDefault(item => item.CharacterId == selectedCharacterId)
                              ?? living.FirstOrDefault();
        if (selected == null) return snapshot;

        CharacterState character = selected.Character;
        snapshot.selectedCharacterId = selected.CharacterId;
        snapshot.name = DisplayName(selected);
        snapshot.realm = Realm(selected);
        snapshot.state = State(selected.State);
        snapshot.health = Health(selected.Health);
        snapshot.age = character.age;
        snapshot.mentalState = character.mentalState;
        snapshot.currentAura = character.currentAura;
        snapshot.auraCapacity = DailyCultivationSimulator.AuraCapacity(selected);
        snapshot.auraControl = character.auraControl;
        snapshot.fatigue = character.fatigue;
        snapshot.naqiProgress = character.naqiProgress;
        snapshot.techniqueMastery = character.techniqueMastery;
        snapshot.overview = BuildOverview(selected);
        snapshot.abilities = BuildAbilities(selected);
        snapshot.relationships = BuildRelationships(character.relationships);
        snapshot.historyItems.AddRange(BuildHistory(character.lifeRecords));
        snapshot.history = snapshot.historyItems.Count == 0 ? "暂无人生履历。" :
            string.Join("\n\n", snapshot.historyItems.Select(item => $"{item.heading}\n{item.body}"));
        BuildObservation(selected, day, out snapshot.currentAction, out snapshot.currentPlan,
            out snapshot.nextPlan);
        snapshot.observation = $"{snapshot.currentAction}\n\n{snapshot.currentPlan}\n\n{snapshot.nextPlan}";
        return snapshot;
    }

    private static string BuildOverview(NPCRuntime npc)
    {
        CharacterState state = npc.Character;
        FoundingTechniqueDefinition technique = FoundingRules.GetTechnique(
            PlayerManager.Instance?.playerData?.founding?.selectedTechniqueId);
        return $"身份：宗门弟子\n年龄：{state.age}\n健康：{Health(state.health)}\n" +
               $"心境：{state.mentalState} / {DiscipleMentalStateRules.MaxMentalState}\n" +
               $"当前灵气：{state.currentAura:0.0} / {DailyCultivationSimulator.AuraCapacity(npc):0.0}\n" +
               $"纳气进度：{state.naqiProgress:0.0}%\n灵气控制：{state.auraControl:0.0}\n疲劳：{state.fatigue:0.0}\n" +
               $"灵根：{FoundingRules.SpiritRootName(state.spiritRoot?.quality ?? SpiritRootQuality.Medium)}\n" +
               $"五行：金{state.spiritRoot.gold:P0} 木{state.spiritRoot.wood:P0} 水{state.spiritRoot.water:P0} 火{state.spiritRoot.fire:P0} 土{state.spiritRoot.earth:P0}\n" +
               $"宗门传承：{technique?.name ?? "未选择"}\n传承掌握：{state.techniqueMastery:0.0}%";
    }

    private static string BuildAbilities(NPCRuntime npc)
    {
        SplitTraits(npc.Character.traitIds, out string personality, out string experiences);
        FoundingFeatureDefinition feature = FoundingRules.GetFeature(npc.Character.initialFeatureId);
        string featureText = feature == null ? "无" : $"{feature.name}：{feature.description}";
        return $"力量：{npc.Attack}\n敏捷：{npc.Agility}\n根骨：{npc.Physique}\n悟性：{npc.Comprehension}\n" +
               $"战斗悟性：{npc.CombatComprehension}\n战斗经验：{npc.CombatExperience}\n战力：{npc.CombatPower}\n" +
               $"灵根：{FoundingRules.SpiritRootName(npc.SpiritRootQuality)}\n\n性格：{personality}\n初始特点：{featureText}\n经历特质：{experiences}";
    }

    private static string BuildRelationships(IEnumerable<RelationshipRecord> relationships)
    {
        List<string> rows = new List<string>();
        foreach (RelationshipRecord relationship in relationships ?? Enumerable.Empty<RelationshipRecord>())
        {
            if (relationship == null) continue;
            NPCRuntime target = NPCManager.Instance?.GetRuntime(relationship.targetCharacterId);
            string targetName = target == null ? relationship.targetCharacterId : DisplayName(target);
            if (string.IsNullOrWhiteSpace(targetName)) targetName = "失效角色";
            rows.Add($"{Relationship(relationship.tag)}：{targetName}（第 {relationship.createdDay} 天）");
        }
        return rows.Count == 0 ? "暂无人际关系。" : string.Join("\n", rows);
    }

    private static List<DiscipleHistoryItemSnapshot> BuildHistory(IEnumerable<LifeRecord> records)
    {
        return (records ?? Enumerable.Empty<LifeRecord>())
            .Where(item => item != null)
            .Select((item, index) => new { item, index })
            .OrderByDescending(pair => pair.item.day)
            .ThenByDescending(pair => pair.index)
            .Select(pair => new DiscipleHistoryItemSnapshot
            {
                type = LifeCategory(pair.item.category),
                heading = $"第 {pair.item.day} 天 · {LifeCategory(pair.item.category)}",
                body = string.IsNullOrWhiteSpace(pair.item.text) ? "无详细记录" : pair.item.text
            })
            .ToList();
    }

    private static void BuildObservation(NPCRuntime npc, int day, out string currentAction,
        out string currentPlan, out string nextPlan)
    {
        Mission mission = npc.CurrentMission;
        currentAction = mission?.Data == null
            ? $"{State(npc.State)}（剩余 {Math.Max(0, npc.StateRemainDays)} 天）"
            : $"{mission.Data.name}（剩余 {mission.RemainingDays} 天）";
        MonthlyPlanTemplate template = MonthlyPlanRules.GetTemplateFor(npc.CharacterId);
        currentPlan = template == null ? "未绑定计划（全部自由）" : $"{template.name}\n{Plan(template)}";
        nextPlan = $"明日安排\n{ActivityName(MonthlyPlanRules.ActivityFor(npc, day + 1))}";
    }

    private static string Plan(MonthlyPlanTemplate plan)
    {
        if (plan == null) return "尚未制定（全部自由）";
        MonthlyPlanRules.Normalize(plan);
        return $"修炼 {plan.days.Count(item => item == MonthlyActivityType.Training)}日 / " +
               $"宗务 {plan.days.Count(item => item == MonthlyActivityType.SectDuty)}日 / " +
               $"自由 {plan.days.Count(item => item == MonthlyActivityType.Free)}日";
    }

    private static string ActivityName(MonthlyActivityType activity)
    {
        switch (activity)
        {
            case MonthlyActivityType.Training: return "修炼";
            case MonthlyActivityType.SectDuty: return "宗门事务";
            default: return "自由活动";
        }
    }

    private static void SplitTraits(IEnumerable<string> traitIds, out string personality, out string experiences)
    {
        List<string> personalityNames = new List<string>();
        List<string> experienceNames = new List<string>();
        foreach (string id in traitIds ?? Enumerable.Empty<string>())
        {
            TraitDefinition definition = TraitDatabase.Instance?.Get(id);
            string name = definition == null || string.IsNullOrWhiteSpace(definition.displayName)
                ? id : definition.displayName;
            if (definition != null && definition.isExperience) experienceNames.Add(name);
            else personalityNames.Add(name);
        }
        personality = personalityNames.Count == 0 ? "无" : string.Join("、", personalityNames);
        experiences = experienceNames.Count == 0 ? "无" : string.Join("、", experienceNames);
    }

    private static string DisplayName(NPCRuntime npc) =>
        string.IsNullOrWhiteSpace(npc?.Character?.displayName) ? npc?.Data?.npcName ?? "无名弟子" : npc.Character.displayName;

    private static string Realm(NPCRuntime npc)
    {
        switch (npc.Realm)
        {
            case CultivationRealm.Mortal: return "凡人";
            case CultivationRealm.QiRefining: return $"炼气{npc.RealmLayer}层";
            case CultivationRealm.Foundation: return "筑基";
            case CultivationRealm.GoldenCore: return "金丹";
            default: return npc.Realm.ToString();
        }
    }

    private static string State(NPCState state)
    {
        switch (state)
        {
            case NPCState.Idle: return "空闲";
            case NPCState.Busy: return "忙碌";
            case NPCState.Injured: return "养伤";
            case NPCState.ClosedDoor: return "闭关";
            case NPCState.Traveling: return "外出";
            default: return state.ToString();
        }
    }

    private static string Health(HealthState health)
    {
        switch (health)
        {
            case HealthState.Healthy: return "健康";
            case HealthState.LightInjury: return "轻伤";
            case HealthState.HeavyInjury: return "重伤";
            case HealthState.SeriousInjury: return "重度伤势";
            case HealthState.PermanentTrauma: return "永久创伤";
            case HealthState.Dead: return "死亡";
            default: return health.ToString();
        }
    }

    private static string Relationship(RelationshipTag tag)
    {
        switch (tag)
        {
            case RelationshipTag.MasterApprentice: return "师徒";
            case RelationshipTag.Friend: return "好友";
            case RelationshipTag.Rival: return "仇敌";
            case RelationshipTag.LifeSaver: return "救命恩人";
            default: return tag.ToString();
        }
    }

    private static string LifeCategory(string category)
    {
        switch (category)
        {
            case "Mission": return "任务";
            case "Decision": return "决策";
            case "Event": return "事件";
            case "Relationship": return "关系";
            case "Breakthrough": return "突破";
            case "Injury": return "受伤";
            case "Death": return "生死";
            case "Recruit": return "入门";
            default: return string.IsNullOrWhiteSpace(category) ? "经历" : category;
        }
    }
}
