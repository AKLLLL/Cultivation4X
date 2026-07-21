using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public class EventManager : MonoBehaviour
{
    public static EventManager Instance { get; private set; }
    public event Action<ActiveCharacterEvent> OnEventPresented;
    public event Action<EventHistoryRecord> OnEventResolved;

    private readonly Dictionary<string, EventDefinition> definitions = new Dictionary<string, EventDefinition>();
    private readonly List<EventHistoryRecord> history = new List<EventHistoryRecord>();
    private readonly List<PendingEvent> pending = new List<PendingEvent>();
    private ActiveCharacterEvent activeEvent;
    private int randomSeed = 48621;
    private int randomRollCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance == null) new GameObject("EventManager").AddComponent<EventManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadDefinitions();
    }

    public void LoadDefinitions()
    {
        definitions.Clear();
        foreach (TextAsset file in Resources.LoadAll<TextAsset>("Configs/CharacterEvents"))
        {
            try
            {
                List<EventDefinition> loaded = file.text.TrimStart().StartsWith("[")
                    ? JsonConvert.DeserializeObject<List<EventDefinition>>(file.text)
                    : new List<EventDefinition> { JsonConvert.DeserializeObject<EventDefinition>(file.text) };
                foreach (EventDefinition definition in loaded)
                {
                    if (!Validate(definition, out string error))
                    {
                        Debug.LogError($"事件配置无效 {file.name}: {error}");
                        continue;
                    }
                    if (definitions.ContainsKey(definition.id))
                    {
                        Debug.LogError($"事件 ID 重复: {definition.id}");
                        continue;
                    }
                    definitions.Add(definition.id, definition);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"事件配置解析失败 {file.name}: {exception.Message}");
            }
        }
    }

    public void ProcessDay(int day)
    {
        if (activeEvent != null) return;
        PendingEvent due = pending.Where(item => item.dueDay <= day).OrderBy(item => item.dueDay).FirstOrDefault();
        if (due != null)
        {
            pending.Remove(due);
            if (TryCreateEvent(due.eventId, due.participantIds, out ActiveCharacterEvent queued))
                Present(queued);
            return;
        }

        List<ActiveCharacterEvent> candidates = new List<ActiveCharacterEvent>();
        foreach (EventDefinition definition in definitions.Values)
        {
            if (definition.tags.Contains("FollowUp")) continue;
            if (IsOnCooldown(definition, day) || HasReachedLimit(definition)) continue;
            if (TryBind(definition, null, out Dictionary<string, NPCRuntime> participants))
                candidates.Add(new ActiveCharacterEvent { Definition = definition, Participants = participants });
        }
        if (candidates.Count == 0) return;

        int total = candidates.Sum(EffectiveWeight);
        int roll = Next(total);
        foreach (ActiveCharacterEvent candidate in candidates)
        {
            roll -= EffectiveWeight(candidate);
            if (roll < 0) { Present(candidate); return; }
        }
    }

    public bool ChooseOption(string optionId)
    {
        if (activeEvent == null) return false;
        EventOptionDefinition option = activeEvent.Definition.options.FirstOrDefault(item => item.id == optionId);
        if (option == null || !ConditionsPass(option.conditions, activeEvent.Participants)) return false;
        EventOutcomeDefinition outcome = WeightedOutcome(option.outcomes);
        if (outcome == null) return false;

        foreach (EventEffect effect in outcome.effects)
            ApplyEffect(effect, activeEvent.Participants);

        int day = TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay;
        EventHistoryRecord record = new EventHistoryRecord
        {
            eventId = activeEvent.Definition.id,
            day = day,
            optionId = option.id,
            resultText = Format(outcome.text, activeEvent.Participants),
            participantIds = activeEvent.Participants.ToDictionary(pair => pair.Key, pair => pair.Value.CharacterId)
        };
        history.Add(record);
        foreach (NPCRuntime participant in activeEvent.Participants.Values.Distinct())
            participant.Character.AddLifeRecord(day, "Event", record.resultText, record.eventId);

        activeEvent = null;
        OnEventResolved?.Invoke(record);
        SaveManager.Instance?.AutoSave();
        return true;
    }

    private void ApplyEffect(EventEffect effect, Dictionary<string, NPCRuntime> participants)
    {
        participants.TryGetValue(effect.participant ?? "actor", out NPCRuntime actor);
        participants.TryGetValue(effect.targetParticipant ?? string.Empty, out NPCRuntime target);
        switch (effect.type)
        {
            case EventEffectType.AddGold: PlayerManager.Instance?.AddGold(effect.amount); break;
            case EventEffectType.AddReputation: PlayerManager.Instance?.AddReputation(effect.amount); break;
            case EventEffectType.AddCultivation: actor?.AddCultivation(effect.amount); break;
            case EventEffectType.AddExperience: if (actor != null) NPCGrow.AddExp(actor, effect.amount); break;
            case EventEffectType.AddTrait: actor?.Character.AddTrait(effect.value); break;
            case EventEffectType.RemoveTrait: actor?.Character.traitIds.Remove(effect.value); break;
            case EventEffectType.AddRelationship:
                if (actor != null && target != null) NPCManager.Instance.AddRelationship(actor.CharacterId, target.CharacterId, effect.relationshipTag);
                break;
            case EventEffectType.Injure: if (actor != null) NPCManager.Instance.Injured(actor, Mathf.Max(1, effect.amount)); break;
            case EventEffectType.PermanentTrauma:
                if (actor != null) { actor.Character.health = HealthState.PermanentTrauma; actor.Character.AddTrait(effect.value); }
                break;
            case EventEffectType.Kill: if (actor != null) NPCManager.Instance.Kill(actor, effect.value); break;
            case EventEffectType.AddItem: WarehouseManager.Instance?.AddItem(effect.value, effect.amount); break;
            case EventEffectType.RemoveItem: WarehouseManager.Instance?.RemoveItem(effect.value, effect.amount); break;
            case EventEffectType.ScheduleEvent:
                pending.Add(new PendingEvent
                {
                    eventId = effect.value,
                    dueDay = (TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay) + Mathf.Max(1, effect.delayDays),
                    participantIds = participants.ToDictionary(pair => pair.Key, pair => pair.Value.CharacterId)
                });
                break;
            case EventEffectType.Recruit: NPCManager.Instance?.RecruitFromTemplate(effect.value); break;
        }
    }

    private bool TryBind(EventDefinition definition, Dictionary<string, string> fixedIds,
        out Dictionary<string, NPCRuntime> result)
    {
        result = new Dictionary<string, NPCRuntime>();
        Dictionary<string, NPCRuntime> bindings = result;
        foreach (EventParticipantRule rule in definition.participants)
        {
            IEnumerable<NPCRuntime> pool = rule.allowDead ? NPCManager.Instance.GetAllNPC() : NPCManager.Instance.GetLivingNPC();
            NPCRuntime chosen = null;
            if (fixedIds != null && fixedIds.TryGetValue(rule.slot, out string fixedId))
            {
                chosen = NPCManager.Instance.GetRuntime(fixedId);
                if (chosen != null && ((!rule.allowDead && !chosen.Character.IsAlive) || bindings.ContainsValue(chosen)))
                    chosen = null;
            }
            else
            {
                List<NPCRuntime> eligible = pool
                    .Where(npc => !bindings.ContainsValue(npc) && ConditionsPass(rule.conditions, Merge(bindings, rule.slot, npc)))
                    .ToList();
                if (eligible.Count > 0) chosen = eligible[Next(eligible.Count)];
            }
            if (chosen == null && rule.required) return false;
            if (chosen != null) result[rule.slot] = chosen;
        }
        return ConditionsPass(definition.conditions, result);
    }

    private bool ConditionsPass(IEnumerable<EventCondition> conditions, Dictionary<string, NPCRuntime> participants)
    {
        if (conditions == null) return true;
        foreach (EventCondition condition in conditions)
        {
            participants.TryGetValue(condition.participant ?? "actor", out NPCRuntime npc);
            switch (condition.type)
            {
                case EventConditionType.Always: break;
                case EventConditionType.HasTrait: if (npc == null || !npc.Character.HasTrait(condition.value)) return false; break;
                case EventConditionType.MissingTrait: if (npc == null || npc.Character.HasTrait(condition.value)) return false; break;
                case EventConditionType.MinimumRealm: if (npc == null || (int)npc.Realm < condition.intValue) return false; break;
                case EventConditionType.HealthIs: if (npc == null || npc.Health.ToString() != condition.value) return false; break;
                case EventConditionType.MinimumGold: if (PlayerManager.Instance == null || PlayerManager.Instance.playerData.gold < condition.intValue) return false; break;
                case EventConditionType.LivingCharacterCount: if (NPCManager.Instance.GetLivingNPC().Count < condition.intValue) return false; break;
                case EventConditionType.HasRelationship:
                    if (npc == null || !npc.Character.relationships.Any(r => r.tag == condition.relationshipTag)) return false;
                    break;
            }
        }
        return true;
    }

    private void Present(ActiveCharacterEvent characterEvent)
    {
        activeEvent = characterEvent;
        OnEventPresented?.Invoke(characterEvent);
        Debug.Log($"事件：{Format(characterEvent.Definition.title, characterEvent.Participants)}");
    }

    private bool TryCreateEvent(string id, Dictionary<string, string> fixedIds, out ActiveCharacterEvent result)
    {
        result = null;
        if (!definitions.TryGetValue(id, out EventDefinition definition) || !TryBind(definition, fixedIds, out Dictionary<string, NPCRuntime> participants)) return false;
        result = new ActiveCharacterEvent { Definition = definition, Participants = participants };
        return true;
    }

    private int EffectiveWeight(ActiveCharacterEvent candidate)
    {
        EventDefinition definition = candidate.Definition;
        int recentCount = history.Count(item => item.eventId == definition.id && item.day >= (TimeManager.Instance.CurrentDay - 30));
        int weight = Mathf.Max(1, definition.baseWeight / (recentCount + 1));
        if (definition.tags.Contains("Recruitment") && NPCManager.Instance.GetLivingNPC().Count <= 1) weight *= 10;
        if (candidate.Participants.TryGetValue("actor", out NPCRuntime actor) && TraitDatabase.Instance != null)
        {
            float modifier = 1f;
            foreach (string traitId in actor.Character.traitIds)
                modifier += TraitDatabase.Instance.Get(traitId)?.eventWeightModifier ?? 0f;
            weight = Mathf.Max(1, Mathf.RoundToInt(weight * modifier));
        }
        return weight;
    }

    private bool IsOnCooldown(EventDefinition definition, int day)
    {
        EventHistoryRecord last = history.LastOrDefault(item => item.eventId == definition.id);
        return last != null && day - last.day < definition.cooldownDays;
    }

    private bool HasReachedLimit(EventDefinition definition) =>
        definition.maxOccurrences > 0 && history.Count(item => item.eventId == definition.id) >= definition.maxOccurrences;

    private EventOutcomeDefinition WeightedOutcome(List<EventOutcomeDefinition> outcomes)
    {
        if (outcomes == null || outcomes.Count == 0) return null;
        int roll = Next(outcomes.Sum(item => Mathf.Max(1, item.weight)));
        foreach (EventOutcomeDefinition outcome in outcomes)
        {
            roll -= Mathf.Max(1, outcome.weight);
            if (roll < 0) return outcome;
        }
        return outcomes[0];
    }

    private int Next(int max)
    {
        if (max <= 1) return 0;
        System.Random random = new System.Random(unchecked(randomSeed + randomRollCount++ * 7919));
        return random.Next(max);
    }

    private static Dictionary<string, NPCRuntime> Merge(Dictionary<string, NPCRuntime> source, string key, NPCRuntime value)
    {
        Dictionary<string, NPCRuntime> copy = new Dictionary<string, NPCRuntime>(source);
        copy[key] = value;
        return copy;
    }

    public static string Format(string template, Dictionary<string, NPCRuntime> participants)
    {
        string result = template ?? string.Empty;
        foreach (var pair in participants)
            result = result.Replace("{" + pair.Key + ".name}", pair.Value.Character.displayName);
        return result;
    }

    private static bool Validate(EventDefinition definition, out string error)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.id)) { error = "缺少事件 ID"; return false; }
        if (definition.options == null || definition.options.Count < 2) { error = "事件至少需要两个选项"; return false; }
        if (definition.options.Any(option => string.IsNullOrWhiteSpace(option.id) || option.outcomes == null || option.outcomes.Count == 0))
        { error = "选项缺少 ID 或结果"; return false; }
        error = null;
        return true;
    }

    public ActiveCharacterEvent GetActiveEvent() => activeEvent;
    public bool IsOptionAvailable(string optionId, out string reason)
    {
        EventOptionDefinition option = activeEvent?.Definition.options.FirstOrDefault(item => item.id == optionId);
        if (option == null) { reason = "选项不存在"; return false; }
        bool available = ConditionsPass(option.conditions, activeEvent.Participants);
        reason = available ? null : option.unavailableReason;
        return available;
    }
    public IReadOnlyList<EventHistoryRecord> GetHistory() => history.AsReadOnly();
    public IReadOnlyList<PendingEvent> GetPendingEvents() => pending.AsReadOnly();

    public void RestoreState(IEnumerable<EventHistoryRecord> records, IEnumerable<PendingEvent> queued, int seed, int rolls)
    {
        history.Clear(); if (records != null) history.AddRange(records);
        pending.Clear(); if (queued != null) pending.AddRange(queued);
        randomSeed = seed;
        randomRollCount = rolls;
    }

    public int RandomSeed => randomSeed;
    public int RandomRollCount => randomRollCount;
}
