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
    private readonly List<EventInboxEntry> inbox = new List<EventInboxEntry>();
    private ActiveCharacterEvent activeEvent;
    private int randomSeed = 48621;
    private int randomRollCount;
    private int nextInboxSequence;
    private int generatedDay = -1;
    private int generatedOrdinaryCount;
    private readonly List<string> newEventTitles = new List<string>();

    public const int InboxCapacity = 5;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MapTestBootstrap.IsTestScene) return;
        if (Instance == null) new GameObject("EventManager").AddComponent<EventManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyUtility.MarkPersistent(gameObject);
        LoadDefinitions();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void LoadDefinitions()
    {
        if (Instance == null) Instance = this;
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
                    NormalizeDefinition(definition);
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
        ResetDailyGeneration(day);
        ProcessDueEvents(day);
        FoundingState founding = PlayerManager.Instance?.playerData?.founding;
        if (GameFlowPermission.IsSectEstablished(founding) && day > 0 && day % 10 == 0)
            TryEnqueueOrdinaryCadenceEvent(day);
    }

    public bool PrepareForDayAdvance(int day, out string reason)
    {
        CancelInvalidInbox(day);
        ResolveExpired(day);
        if (inbox.Any(item => IsCritical(item))) { reason = "存在尚未处理的关键事件"; return false; }
        if (inbox.Count >= InboxCapacity) { reason = "事件收件箱已满"; return false; }
        reason = null;
        return true;
    }

    public bool TryTriggerSource(EventSource source, NPCRuntime actor = null)
    {
        int day = TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay;
        ResetDailyGeneration(day);
        bool explicitSource = source == EventSource.FollowUp || source == EventSource.Exploration;
        if (!explicitSource)
        {
            float chance = SourceChance(source);
            if (Next(10000) >= Mathf.RoundToInt(chance * 10000f)) return false;
        }

        Dictionary<string, string> fixedIds = actor == null ? null : new Dictionary<string, string> { { "actor", actor.CharacterId } };
        List<ActiveCharacterEvent> candidates = definitions.Values.OrderBy(item => item.id)
            .Where(item => !item.directOnly && item.sources.Contains(source) && (explicitSource || item.isCritical) &&
                !IsOnCooldown(item, day) && !HasReachedLimit(item))
            .Select(item => TryCreateEvent(item.id, fixedIds, out ActiveCharacterEvent candidate) ? candidate : null)
            .Where(item => item != null &&
                Next(10000) < Mathf.RoundToInt(Mathf.Clamp01(item.Definition.triggerChance) * 10000f))
            .ToList();
        if (candidates.Count == 0) return false;
        int roll = Next(candidates.Sum(EffectiveWeight));
        ActiveCharacterEvent selected = candidates.First();
        foreach (ActiveCharacterEvent candidate in candidates)
        {
            roll -= EffectiveWeight(candidate);
            if (roll < 0) { selected = candidate; break; }
        }
        Enqueue(selected, day);
        if (!selected.Definition.isCritical) generatedOrdinaryCount = 1;
        return true;
    }

    private bool TryEnqueueOrdinaryCadenceEvent(int day)
    {
        if (inbox.Count >= InboxCapacity || generatedOrdinaryCount > 0) return false;
        List<ActiveCharacterEvent> candidates = definitions.Values.OrderBy(item => item.id)
            .Where(item => !item.isCritical && !item.directOnly && item.sources.Any(source =>
                source != EventSource.FollowUp && source != EventSource.Exploration) &&
                !IsOnCooldown(item, day) && !HasReachedLimit(item))
            .Select(item => TryCreateEvent(item.id, null, out ActiveCharacterEvent candidate) ? candidate : null)
            .Where(item => item != null)
            .ToList();
        if (candidates.Count == 0) return false;
        int roll = Next(candidates.Sum(EffectiveWeight));
        ActiveCharacterEvent selected = candidates[0];
        foreach (ActiveCharacterEvent candidate in candidates)
        {
            roll -= EffectiveWeight(candidate);
            if (roll < 0) { selected = candidate; break; }
        }
        Enqueue(selected, day);
        generatedOrdinaryCount = 1;
        return true;
    }

    public bool TryEnqueueEventById(string eventId, NPCRuntime actor)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return false;
        if (history.Any(item => item.eventId == eventId) || inbox.Any(item => item.eventId == eventId) || pending.Any(item => item.eventId == eventId))
            return true;
        if (inbox.Count >= InboxCapacity) return false;
        Dictionary<string, string> fixedIds = actor == null
            ? null
            : new Dictionary<string, string> { { "actor", actor.CharacterId } };
        if (!TryCreateEvent(eventId, fixedIds, out ActiveCharacterEvent created)) return false;
        Enqueue(created, TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay);
        return true;
    }

    public bool TryEnqueueRepeatableEventById(string eventId, NPCRuntime actor)
    {
        if (string.IsNullOrWhiteSpace(eventId) || actor == null || inbox.Count >= InboxCapacity) return false;
        string actorId = actor.CharacterId;
        Func<Dictionary<string, string>, bool> sameActor = ids => ids != null &&
            ids.TryGetValue("actor", out string id) && id == actorId;
        if (inbox.Any(item => item.eventId == eventId && sameActor(item.participantIds)) ||
            pending.Any(item => item.eventId == eventId && sameActor(item.participantIds)) ||
            (activeEvent != null && activeEvent.Definition.id == eventId &&
             activeEvent.Participants.TryGetValue("actor", out NPCRuntime activeActor) && activeActor.CharacterId == actorId))
            return true;
        Dictionary<string, string> fixedIds = new Dictionary<string, string> { { "actor", actorId } };
        if (!TryCreateEvent(eventId, fixedIds, out ActiveCharacterEvent created)) return false;
        Enqueue(created, TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay);
        return true;
    }

    public bool ChooseOption(string optionId)
    {
        if (activeEvent == null) return false;
        EventOptionDefinition option = activeEvent.Definition.options.FirstOrDefault(item => item.id == optionId);
        if (option == null || !ConditionsPass(option.conditions, activeEvent.Participants)) return false;
        EventOutcomeDefinition outcome = WeightedOutcome(option.outcomes);
        if (outcome == null) return false;
        if (!CanApplyEffects(outcome.effects, activeEvent.Participants)) return false;

        foreach (EventEffect effect in outcome.effects ?? Enumerable.Empty<EventEffect>())
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

        string effectText = FormatEffectSummary(outcome.effects, activeEvent.Participants);
        Debug.Log(string.IsNullOrEmpty(effectText)
            ? $"事件：{Format(activeEvent.Definition.title, activeEvent.Participants)}，选择：{option.text}，结果：{record.resultText}"
            : $"事件：{Format(activeEvent.Definition.title, activeEvent.Participants)}，选择：{option.text}，结果：{record.resultText}，{effectText}");

        string resolvedEntryId = activeEvent.EntryId;
        activeEvent = null;
        inbox.RemoveAll(item => item.entryId == resolvedEntryId);
        OnEventResolved?.Invoke(record);
        SaveManager.Instance?.AutoSave();
        return true;
    }

    private bool CanApplyEffects(IEnumerable<EventEffect> effects, Dictionary<string, NPCRuntime> participants)
    {
        List<ItemReward> additions = new List<ItemReward>();
        Dictionary<string, int> removals = new Dictionary<string, int>();
        foreach (EventEffect effect in effects ?? Enumerable.Empty<EventEffect>())
        {
            if (!string.IsNullOrEmpty(effect.participant) && !participants.ContainsKey(effect.participant) && effect.type != EventEffectType.AddReputation)
                return false;
            if (effect.type == EventEffectType.AddItem) additions.Add(new ItemReward { itemId = effect.value, count = effect.amount });
            if (effect.type == EventEffectType.RemoveItem)
                removals[effect.value] = removals.TryGetValue(effect.value, out int count) ? count + effect.amount : effect.amount;
            if (effect.type == EventEffectType.ScheduleEvent && !definitions.ContainsKey(effect.value)) return false;
        }
        if (WarehouseManager.Instance == null && (additions.Count > 0 || removals.Count > 0)) return false;
        if (additions.Count > 0 && !WarehouseManager.Instance.CanAddRewards(additions)) return false;
        return removals.All(item => WarehouseManager.Instance.GetItemCount(item.Key) >= item.Value);
    }

    private void ApplyEffect(EventEffect effect, Dictionary<string, NPCRuntime> participants)
    {
        participants.TryGetValue(effect.participant ?? "actor", out NPCRuntime actor);
        participants.TryGetValue(effect.targetParticipant ?? string.Empty, out NPCRuntime target);
        switch (effect.type)
        {
            case EventEffectType.AddReputation: PlayerManager.Instance?.AddReputation(effect.amount); break;
            case EventEffectType.AddAura: actor?.AddAura(effect.amount); break;
            case EventEffectType.AddAuraControl: actor?.AddAuraControl(effect.amount); break;
            case EventEffectType.AddTechniqueUnderstanding: PlayerManager.Instance?.AddTechniqueUnderstanding(effect.amount, actor); break;
            case EventEffectType.AddTrait: actor?.Character.AddTrait(effect.value); break;
            case EventEffectType.RemoveTrait: actor?.Character.traitIds.Remove(effect.value); break;
            case EventEffectType.AddRelationship:
                if (actor != null && target != null) NPCManager.Instance.AddRelationship(actor.CharacterId, target.CharacterId, effect.relationshipTag);
                break;
            case EventEffectType.Injure: if (actor != null) NPCManager.Instance.Injured(actor, Mathf.Max(1, effect.amount)); break;
            case EventEffectType.PermanentTrauma:
                if (actor != null) NPCManager.Instance.ApplyPermanentTrauma(actor, effect.value);
                break;
            case EventEffectType.Kill: if (actor != null) NPCManager.Instance.Kill(actor, effect.value); break;
            case EventEffectType.AddItem:
                WarehouseManager.Instance?.AddItem(effect.value, effect.amount);
                break;
            case EventEffectType.RemoveItem:
                WarehouseManager.Instance?.RemoveItem(effect.value, effect.amount);
                break;
            case EventEffectType.ScheduleEvent:
                pending.Add(new PendingEvent
                {
                    eventId = effect.value,
                    dueDay = (TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay) + Mathf.Max(1, effect.delayDays),
                    participantIds = participants.ToDictionary(pair => pair.Key, pair => pair.Value.CharacterId)
                });
                break;
            case EventEffectType.Recruit: NPCManager.Instance?.RecruitFromTemplate(effect.value); break;
            case EventEffectType.AddInheritancePreparation:
                PlayerManager.Instance?.AddInheritancePreparation(effect.amount, actor);
                break;
            case EventEffectType.AddTechniqueAnnotation:
                PlayerManager.Instance?.ResolveTechniqueAnnotation(effect.value);
                break;
            case EventEffectType.AddVillageRelation:
                PlayerManager.Instance?.AddVillageRelation(effect.amount, actor);
                break;
        }
    }

    private bool TryBind(EventDefinition definition, Dictionary<string, string> fixedIds,
        out Dictionary<string, NPCRuntime> result)
    {
        result = new Dictionary<string, NPCRuntime>();
        if (fixedIds != null) fixedIds = CleanParticipantIds(fixedIds);
        if (NPCManager.Instance == null) return definition.participants == null || definition.participants.Count == 0;
        Dictionary<string, NPCRuntime> bindings = result;
        foreach (EventParticipantRule rule in definition.participants ?? Enumerable.Empty<EventParticipantRule>())
        {
            if (rule == null) continue;
            if (string.IsNullOrWhiteSpace(rule.slot))
            {
                if (rule.required) return false;
                continue;
            }
            IEnumerable<NPCRuntime> pool = rule.allowDead ? NPCManager.Instance.GetAllNPC() : NPCManager.Instance.GetLivingNPC();
            NPCRuntime chosen = null;
            if (fixedIds != null && fixedIds.TryGetValue(rule.slot, out string fixedId))
            {
                chosen = NPCManager.Instance.GetRuntime(fixedId);
                if (chosen != null && ((!rule.allowDead && !chosen.Character.IsAlive) || bindings.ContainsValue(chosen) ||
                    !ConditionsPass(rule.conditions, Merge(bindings, rule.slot, chosen))))
                    chosen = null;
            }
            else if (fixedIds != null)
            {
                if (rule.required) return false;
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
                case EventConditionType.MinimumItem:
                    if (WarehouseManager.Instance == null || !WarehouseManager.Instance.HasItem(condition.value, condition.intValue)) return false;
                    break;
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

    public bool OpenInboxEntry(string entryId)
    {
        EventInboxEntry entry = inbox.FirstOrDefault(item => item.entryId == entryId);
        if (entry == null) return false;
        if (!TryCreateEvent(entry.eventId, entry.participantIds, out ActiveCharacterEvent created))
        {
            inbox.Remove(entry);
            if (activeEvent?.EntryId == entry.entryId) activeEvent = null;
            RecordCancelled(entry.eventId, TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay,
                entry.participantIds, "事件参与者死亡或条件已经失效");
            return false;
        }
        created.EntryId = entry.entryId;
        Present(created);
        return true;
    }

    public bool DebugEnqueueEvent(NPCRuntime actor)
    {
        if (actor == null || inbox.Count >= InboxCapacity) return false;
        Dictionary<string, string> fixedIds = new Dictionary<string, string> { { "actor", actor.CharacterId } };
        ActiveCharacterEvent created = definitions.Values.OrderBy(item => item.id)
            .Select(item => TryCreateEvent(item.id, fixedIds, out ActiveCharacterEvent value) ? value : null)
            .FirstOrDefault(item => item != null);
        if (created == null) return false;
        Enqueue(created, TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay);
        return true;
    }

    private void Enqueue(ActiveCharacterEvent created, int day)
    {
        Dictionary<string, string> ids = created.Participants.ToDictionary(pair => pair.Key, pair => pair.Value.CharacterId);
        if (inbox.Count >= InboxCapacity)
        {
            pending.Add(new PendingEvent { eventId = created.Definition.id, dueDay = day, participantIds = ids });
            return;
        }
        EventInboxEntry entry = new EventInboxEntry
        {
            entryId = $"event-{nextInboxSequence++}",
            eventId = created.Definition.id,
            createdDay = day,
            expiresDay = created.Definition.isCritical ? -1 : day + Mathf.Max(1, created.Definition.expiresAfterDays),
            participantIds = ids
        };
        inbox.Add(entry);
        newEventTitles.Add(Format(created.Definition.title, created.Participants));
    }

    private void ProcessDueEvents(int day)
    {
        foreach (PendingEvent due in pending.Where(item => item.dueDay <= day).OrderBy(item => item.dueDay).ToList())
        {
            if (inbox.Count >= InboxCapacity) break;
            pending.Remove(due);
            if (TryCreateEvent(due.eventId, due.participantIds, out ActiveCharacterEvent created)) Enqueue(created, day);
            else RecordCancelled(due.eventId, day, due.participantIds, "参与者死亡或事件条件已经失效");
        }
    }

    private void ResolveExpired(int day)
    {
        string previouslyActive = activeEvent?.EntryId;
        foreach (EventInboxEntry entry in inbox.Where(item => item.expiresDay >= 0 && item.expiresDay <= day).ToList())
        {
            if (!TryCreateEvent(entry.eventId, entry.participantIds, out ActiveCharacterEvent created))
            { inbox.Remove(entry); RecordCancelled(entry.eventId, day, entry.participantIds, "事件到期时条件已经失效"); continue; }
            created.EntryId = entry.entryId;
            activeEvent = created;
            string optionId = created.Definition.defaultOptionId;
            if (string.IsNullOrEmpty(optionId) || !IsOptionAvailable(optionId, out _))
                optionId = created.Definition.options.FirstOrDefault(option => ConditionsPass(option.conditions, created.Participants))?.id;
            if (string.IsNullOrEmpty(optionId) || !ChooseOption(optionId))
            { activeEvent = null; inbox.Remove(entry); RecordCancelled(entry.eventId, day, entry.participantIds, "事件没有可执行的安全默认选项"); }
        }
        if (activeEvent == null && !string.IsNullOrEmpty(previouslyActive) && inbox.Any(item => item.entryId == previouslyActive))
            OpenInboxEntry(previouslyActive);
    }

    private void CancelInvalidInbox(int day)
    {
        foreach (EventInboxEntry entry in inbox.ToList())
        {
            if (TryCreateEvent(entry.eventId, entry.participantIds, out _)) continue;
            inbox.Remove(entry);
            if (activeEvent?.EntryId == entry.entryId) activeEvent = null;
            RecordCancelled(entry.eventId, day, entry.participantIds, "事件参与者死亡或条件已经失效");
        }
    }

    private void RecordCancelled(string eventId, int day, Dictionary<string, string> participants, string reason)
    {
        EventHistoryRecord record = new EventHistoryRecord { eventId = eventId, day = day, optionId = "cancelled", resultText = reason,
            participantIds = participants ?? new Dictionary<string, string>() };
        history.Add(record);
        OnEventResolved?.Invoke(record);
    }

    private bool IsCritical(EventInboxEntry entry) =>
        definitions.TryGetValue(entry.eventId, out EventDefinition definition) && definition.isCritical;

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
        int currentDay = TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay;
        int recentCount = history.Count(item => item.eventId == definition.id && item.day >= (currentDay - 30));
        int weight = Mathf.Max(1, definition.baseWeight / (recentCount + 1));
        if (definition.tags.Contains("Recruitment") && NPCManager.Instance != null && NPCManager.Instance.GetLivingNPC().Count <= 1) weight *= 10;
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

    private void ResetDailyGeneration(int day)
    {
        if (generatedDay == day) return;
        generatedDay = day;
        generatedOrdinaryCount = 0;
        newEventTitles.Clear();
    }

    private float SourceChance(EventSource source)
    {
        switch (source)
        {
            case EventSource.Training: return 0.15f;
            case EventSource.MissionStart: return 0.10f;
            case EventSource.MissionComplete: return 0.35f;
            case EventSource.MissionFailed:
            case EventSource.Injury: return 0.50f;
            case EventSource.SecretRealm: return 0.40f;
            case EventSource.Alchemy: return 0.25f;
            case EventSource.FacilityUpgrade: return 0.30f;
            case EventSource.SectDaily: return 0.08f;
            case EventSource.Recruitment:
                return NPCManager.Instance != null && NPCManager.Instance.GetLivingNPC().Count <= 2 ? 0.50f : 0.05f;
            case EventSource.FollowUp: return 1f;
            case EventSource.Exploration: return 1f;
            default: return 0.25f;
        }
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

    private static string FormatEffectSummary(IEnumerable<EventEffect> effects, Dictionary<string, NPCRuntime> participants)
    {
        List<string> parts = new List<string>();
        foreach (EventEffect effect in effects ?? Enumerable.Empty<EventEffect>())
        {
            participants.TryGetValue(effect.participant ?? "actor", out NPCRuntime actor);
            string name = actor == null ? "角色" : actor.Character.displayName;
            switch (effect.type)
            {
                case EventEffectType.AddReputation:
                    parts.Add($"声望 {Signed(effect.amount)}");
                    break;
                case EventEffectType.AddAura:
                    parts.Add($"{name}当前灵气 {Signed(effect.amount)}");
                    break;
                case EventEffectType.AddAuraControl:
                    parts.Add($"{name}灵气控制 {Signed(effect.amount)}");
                    break;
                case EventEffectType.AddTechniqueUnderstanding:
                    parts.Add($"{name}功法理解 {Signed(effect.amount)}");
                    break;
                case EventEffectType.AddTrait:
                    parts.Add($"{name}获得特质：{TraitName(effect.value)}");
                    break;
                case EventEffectType.RemoveTrait:
                    parts.Add($"{name}失去特质：{TraitName(effect.value)}");
                    break;
                case EventEffectType.Injure:
                    parts.Add($"{name}受伤 {effect.amount}");
                    break;
                case EventEffectType.PermanentTrauma:
                    parts.Add($"{name}获得永久创伤：{TraitName(effect.value)}");
                    break;
                case EventEffectType.Kill:
                    parts.Add($"{name}死亡：{effect.value}");
                    break;
                case EventEffectType.AddItem:
                    parts.Add($"获得物品 {effect.value} x{effect.amount}");
                    break;
                case EventEffectType.RemoveItem:
                    parts.Add($"消耗物品 {effect.value} x{effect.amount}");
                    break;
                case EventEffectType.ScheduleEvent:
                    parts.Add($"后续事件：{effect.value}（{Mathf.Max(1, effect.delayDays)}天后）");
                    break;
                case EventEffectType.Recruit:
                    parts.Add($"招募：{effect.value}");
                    break;
                case EventEffectType.AddInheritancePreparation:
                    parts.Add($"传承整理 {Signed(effect.amount)}");
                    break;
                case EventEffectType.AddTechniqueAnnotation:
                    parts.Add("形成宗门功法注解");
                    break;
                case EventEffectType.AddVillageRelation:
                    parts.Add($"青石村关系 {Signed(effect.amount)}");
                    break;
            }
        }
        return string.Join("，", parts);
    }

    private static string Signed(int value) => value >= 0 ? "+" + value : value.ToString();

    private static string TraitName(string traitId)
    {
        TraitDefinition definition = TraitDatabase.Instance == null ? null : TraitDatabase.Instance.Get(traitId);
        return string.IsNullOrEmpty(definition?.displayName) ? traitId : definition.displayName;
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

    private static void NormalizeDefinition(EventDefinition definition)
    {
        if (definition == null) return;
        definition.tags = definition.tags ?? new List<string>();
        definition.sources = definition.sources ?? new List<EventSource>();
        if (definition.sources.Count == 0)
        {
            if (definition.tags.Contains("FollowUp")) definition.sources.Add(EventSource.FollowUp);
            else if (definition.tags.Contains("Recruitment")) definition.sources.Add(EventSource.Recruitment);
            else
            {
                if (definition.tags.Contains("Cultivation")) { definition.sources.Add(EventSource.Training); definition.sources.Add(EventSource.FacilityUpgrade); }
                if (definition.tags.Contains("Health")) { definition.sources.Add(EventSource.Injury); definition.sources.Add(EventSource.Recovery); }
                if (definition.tags.Contains("Adventure")) { definition.sources.Add(EventSource.MissionStart); definition.sources.Add(EventSource.MissionNode); definition.sources.Add(EventSource.MissionComplete); definition.sources.Add(EventSource.SecretRealm); }
                if (definition.tags.Contains("Relationship")) definition.sources.Add(EventSource.SectDaily);
                if (definition.tags.Contains("Danger")) { definition.sources.Add(EventSource.MissionFailed); definition.sources.Add(EventSource.Alchemy); }
            }
        }
        if (definition.tags.Contains("Death")) definition.isCritical = true;
        if (definition.expiresAfterDays <= 0) definition.expiresAfterDays = 3;
        if (definition.options != null && definition.options.Count > 0)
        {
            EventOptionDefinition configured = definition.options.FirstOrDefault(option => option.id == definition.defaultOptionId);
            if (configured == null || !IsSafeDefault(configured))
                definition.defaultOptionId = (definition.options.FirstOrDefault(IsSafeDefault) ?? definition.options[0]).id;
        }
    }

    private static bool IsSafeDefault(EventOptionDefinition option)
    {
        return option != null && option.outcomes != null && option.outcomes.All(outcome =>
            outcome.effects == null || outcome.effects.All(effect =>
                effect.type != EventEffectType.Kill && effect.type != EventEffectType.Injure &&
                effect.type != EventEffectType.PermanentTrauma && effect.type != EventEffectType.RemoveItem &&
                !(effect.type == EventEffectType.AddItem && effect.amount < 0)));
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
    public IReadOnlyList<EventInboxEntry> GetInbox() => inbox.AsReadOnly();
    public string ActiveEventEntryId => activeEvent?.EntryId;
    public int NextInboxSequence => nextInboxSequence;
    public IReadOnlyCollection<EventDefinition> GetDefinitions() => definitions.Values;
    public List<string> ConsumeNewEventTitles()
    {
        List<string> result = new List<string>(newEventTitles);
        newEventTitles.Clear();
        return result;
    }

    public void RestoreState(IEnumerable<EventHistoryRecord> records, IEnumerable<PendingEvent> queued, int seed, int rolls,
        IEnumerable<EventInboxEntry> savedInbox = null, string activeEntryId = null, int savedNextSequence = 0,
        int savedGeneratedDay = -1, int savedGeneratedOrdinaryCount = 0)
    {
        history.Clear();
        if (records != null)
        {
            foreach (EventHistoryRecord record in records.Where(item => item != null))
            {
                record.participantIds = CleanParticipantIds(record.participantIds);
                history.Add(record);
            }
        }
        pending.Clear();
        if (queued != null)
        {
            foreach (PendingEvent item in queued.Where(item => item != null))
            {
                item.participantIds = CleanParticipantIds(item.participantIds);
                pending.Add(item);
            }
        }
        inbox.Clear();
        if (savedInbox != null)
        {
            foreach (EventInboxEntry item in savedInbox.Where(item => item != null))
            {
                item.participantIds = CleanParticipantIds(item.participantIds);
                inbox.Add(item);
            }
        }
        randomSeed = seed;
        randomRollCount = rolls;
        nextInboxSequence = savedNextSequence;
        generatedDay = savedGeneratedDay;
        generatedOrdinaryCount = Mathf.Max(0, savedGeneratedOrdinaryCount);
        activeEvent = null;
        if (!string.IsNullOrEmpty(activeEntryId)) OpenInboxEntry(activeEntryId);
    }

    public int RandomSeed => randomSeed;
    public int RandomRollCount => randomRollCount;
    public int GeneratedDay => generatedDay;
    public int GeneratedOrdinaryCount => generatedOrdinaryCount;

    private static Dictionary<string, string> CleanParticipantIds(Dictionary<string, string> participantIds)
    {
        if (participantIds == null) return new Dictionary<string, string>();
        return participantIds
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.First().Value);
    }
}
