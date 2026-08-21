using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public static class ExternalThreatRules
{
    public const string QingshiThreatId = "qingshi_beast_assault";
    private const string ResourcePath = "Configs/ExternalThreats";
    private static Dictionary<string, ExternalThreatDefinition> definitions;

    public static IReadOnlyDictionary<string, ExternalThreatDefinition> Definitions
    {
        get
        {
            if (definitions == null) LoadDefinitions();
            return definitions;
        }
    }

    public static void ResetForTests() => definitions = null;

    public static void LoadDefinitions()
    {
        definitions = new Dictionary<string, ExternalThreatDefinition>(StringComparer.Ordinal);
        foreach (TextAsset file in Resources.LoadAll<TextAsset>(ResourcePath))
        {
            try
            {
                ExternalThreatDefinition definition = JsonConvert.DeserializeObject<ExternalThreatDefinition>(file.text);
                if (!ValidateDefinition(definition, out string reason))
                {
                    Debug.LogError($"外部威胁配置无效 {file.name}: {reason}");
                    continue;
                }
                if (definitions.ContainsKey(definition.id))
                {
                    Debug.LogError($"外部威胁 ID 重复: {definition.id}");
                    continue;
                }
                definitions.Add(definition.id, definition);
            }
            catch (Exception exception)
            {
                Debug.LogError($"外部威胁配置解析失败 {file.name}: {exception.Message}");
            }
        }
    }

    public static ExternalThreatDefinition GetDefinition(string id)
    {
        Definitions.TryGetValue(id ?? string.Empty, out ExternalThreatDefinition definition);
        return definition;
    }

    public static bool ValidateDefinition(ExternalThreatDefinition definition, out string reason)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.id)) { reason = "缺少 ID"; return false; }
        if (definition.threatPower <= 0 || definition.activationDelayDays < 1 || definition.raidIntervalDays < 1)
        { reason = "战力或日期参数无效"; return false; }
        if (string.IsNullOrWhiteSpace(definition.investigationMissionId) ||
            string.IsNullOrWhiteSpace(definition.discoveredEventId))
        { reason = "调查任务或发现事件缺失"; return false; }
        reason = null;
        return true;
    }

    public static ActiveThreatState GetState()
    {
        FoundingState founding = PlayerManager.Instance?.playerData?.founding;
        if (founding == null) return null;
        if (founding.externalThreat == null) founding.externalThreat = new ActiveThreatState();
        return founding.externalThreat;
    }

    public static bool TryScheduleFromRelation(int currentDay)
    {
        VillageState village = PlayerManager.Instance?.playerData?.founding?.village;
        ActiveThreatState state = GetState();
        ExternalThreatDefinition definition = GetDefinition(QingshiThreatId);
        if (village == null || state == null || definition == null ||
            village.relation < definition.triggerRelation || state.status != ExternalThreatStatus.None)
            return false;
        state.threatId = definition.id;
        state.status = ExternalThreatStatus.Scheduled;
        state.scheduledDay = Math.Max(0, currentDay) + definition.activationDelayDays;
        PlayerManager.Instance.NotifyFoundingChanged();
        return true;
    }

    public static void ProcessDay(int day)
    {
        ActiveThreatState state = GetState();
        if (state == null || state.status == ExternalThreatStatus.None)
        {
            TryScheduleFromRelation(day);
            return;
        }
        ExternalThreatDefinition definition = GetDefinition(state.threatId);
        if (definition == null) return;

        if (state.status == ExternalThreatStatus.Scheduled && day >= state.scheduledDay)
        {
            state.status = ExternalThreatStatus.Active;
            state.activatedDay = day;
            state.intelligence = 0;
            state.nextRaidDay = day + definition.raidIntervalDays;
            TryEnqueueDiscoveryNotification(state, definition);
            PlayerManager.Instance?.NotifyFoundingChanged();
            return;
        }

        if (state.status == ExternalThreatStatus.Active)
        {
            if (!state.discoveryNotificationEnqueued)
                TryEnqueueDiscoveryNotification(state, definition);
            if (day < state.nextRaidDay) return;
            while (day >= state.nextRaidDay)
            {
                ApplyRaidPressure(definition);
                state.raidCount++;
                state.nextRaidDay += definition.raidIntervalDays;
            }
            MissionManager.Instance?.CancelLaborMissionsUntilValid();
            PlayerManager.Instance?.NotifyFoundingChanged();
        }
    }

    /// <summary>
    /// 清理地图兽巢后的威胁后果：尚未排程时封存对应兽潮，已排程/激活时将下一节点顺延一天。
    /// 状态直接复用现有外部威胁存档，不增加另一套抑制标记。
    /// </summary>
    public static bool ApplyBeastLairClearance(int currentDay)
    {
        ActiveThreatState state = GetState();
        if (state == null) return false;
        if (state.status == ExternalThreatStatus.None)
        {
            state.threatId = QingshiThreatId;
            state.status = ExternalThreatStatus.Resolved;
            state.scheduledDay = -1;
            state.activatedDay = -1;
            state.nextRaidDay = -1;
        }
        else if (state.status == ExternalThreatStatus.Scheduled)
        {
            state.scheduledDay = Math.Max(currentDay + 1, state.scheduledDay + 1);
        }
        else if (state.status == ExternalThreatStatus.Active)
        {
            state.nextRaidDay = Math.Max(currentDay + 1, state.nextRaidDay + 1);
        }
        else return false;
        PlayerManager.Instance?.NotifyFoundingChanged();
        TimeManager.Instance?.RecordThreatNotice("地图兽巢已处理，外部兽潮威胁被抑制或延后");
        return true;
    }

    private static bool TryEnqueueDiscoveryNotification(ActiveThreatState state,
        ExternalThreatDefinition definition)
    {
        if (state == null || definition == null || state.status != ExternalThreatStatus.Active ||
            state.discoveryNotificationEnqueued || EventManager.Instance == null)
            return false;
        if (!EventManager.Instance.TryEnqueueEventById(definition.discoveredEventId, null))
            return false;
        state.discoveryNotificationEnqueued = true;
        TimeManager.Instance?.RecordThreatNotice($"发现外部威胁：{definition.name}");
        PlayerManager.Instance?.NotifyFoundingChanged();
        return true;
    }

    public static int AddIntelligence(NPCRuntime investigator)
    {
        ActiveThreatState state = GetState();
        if (state == null || state.status != ExternalThreatStatus.Active || investigator == null) return 0;
        int gain = Mathf.Clamp(10 + investigator.Intelligence, 15, 30);
        int before = state.intelligence;
        state.intelligence = Mathf.Clamp(state.intelligence + gain, 0, 100);
        PlayerManager.Instance?.NotifyFoundingChanged();
        return state.intelligence - before;
    }

    public static bool IsInvestigationRunning()
    {
        return MissionManager.Instance != null && MissionManager.Instance.GetActiveMissions()
            .Any(item => item.Data != null && item.Data.threatMissionKind == ThreatMissionKind.Investigation &&
                (item.State == MissionState.Active || item.State == MissionState.WaitingNode));
    }

    public static bool CanRespond(IEnumerable<NPCRuntime> participants, CombatPlanType plan, out string reason)
    {
        ActiveThreatState state = GetState();
        if (state == null || state.status != ExternalThreatStatus.Active) { reason = "当前没有可处理的外部威胁"; return false; }
        if (plan == CombatPlanType.RetreatToCave) { reason = null; return true; }
        List<NPCRuntime> party = NormalizeParty(participants);
        if (party.Count < 1 || party.Count > 3) { reason = "必须选择1至3名空闲且存活的弟子"; return false; }
        if (plan == CombatPlanType.SimpleDefense &&
            (WarehouseManager.Instance == null || WarehouseManager.Instance.GetItemCount(FacilityRules.BasicMaterialId) < GetDefinition(state.threatId).defenseMaterialCost))
        { reason = "基础材料不足"; return false; }
        reason = null;
        return true;
    }

    public static ThreatResolutionRecord ResolveThreat(IEnumerable<NPCRuntime> participants, CombatPlanType plan, out string reason)
    {
        if (!CanRespond(participants, plan, out reason)) return null;
        ActiveThreatState state = GetState();
        ExternalThreatDefinition definition = GetDefinition(state.threatId);
        List<NPCRuntime> party = NormalizeParty(participants);
        ThreatResolutionRecord record = new ThreatResolutionRecord
        {
            day = TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay,
            plan = plan,
            participantIds = party.Select(item => item.CharacterId).ToList()
        };

        if (plan == CombatPlanType.RetreatToCave)
        {
            ApplyVillageChanges(-10, -20, -20, record);
            record.narrative = "宗门退守洞府，保住了弟子与库存，但青石村独自承受了野兽冲击。";
            Complete(state, record);
            MissionManager.Instance?.CancelLaborMissionsUntilValid();
            reason = null;
            return record;
        }

        if (plan == CombatPlanType.SimpleDefense &&
            !WarehouseManager.Instance.RemoveItem(FacilityRules.BasicMaterialId, definition.defenseMaterialCost))
        { reason = "准备资源扣除失败"; return null; }
        List<CombatantPower> powers = party.Select(item => new CombatantPower
        {
            characterId = item.CharacterId,
            power = CharacterCapabilityRules.CalculateCombatPower(item, 0)
        }).ToList();
        float preparation = plan == CombatPlanType.SimpleDefense
            ? Mathf.Min(1.35f, 1.2f + 0.05f * PlayerManager.Instance.GetFacilityLevel(FacilityType.ProtectionArray))
            : 1f;
        record.combat = CombatResolver.Resolve(new CombatRequest
        {
            combatants = powers,
            threatPower = definition.threatPower,
            intelligence = state.intelligence,
            preparationModifier = preparation
        });
        record.weakestCharacterId = powers.OrderBy(item => item.power)
            .ThenBy(item => item.characterId, StringComparer.Ordinal).First().characterId;
        ApplyCombatOutcome(record, party);
        record.narrative = BuildNarrative(definition, plan, record, party);
        Complete(state, record);
        MissionManager.Instance?.CancelLaborMissionsUntilValid();
        reason = null;
        return record;
    }

    public static CombatResolution Preview(IEnumerable<NPCRuntime> participants, CombatPlanType plan)
    {
        ActiveThreatState state = GetState();
        ExternalThreatDefinition definition = state == null ? null : GetDefinition(state.threatId);
        List<NPCRuntime> party = NormalizeParty(participants);
        if (definition == null || party.Count == 0 || plan == CombatPlanType.RetreatToCave) return null;
        float preparation = plan == CombatPlanType.SimpleDefense
            ? Mathf.Min(1.35f, 1.2f + 0.05f * (PlayerManager.Instance?.GetFacilityLevel(FacilityType.ProtectionArray) ?? 0))
            : 1f;
        return CombatResolver.Resolve(new CombatRequest
        {
            combatants = party.Select(item => new CombatantPower
                { characterId = item.CharacterId, power = CharacterCapabilityRules.CalculateCombatPower(item) }).ToList(),
            threatPower = definition.threatPower,
            intelligence = state.intelligence,
            preparationModifier = preparation
        });
    }

    public static void RestoreLaborAfterVillageHelp()
    {
        ActiveThreatState state = GetState();
        if (state?.status == ExternalThreatStatus.Resolved)
            PlayerManager.Instance?.ChangeVillageLabor(10, FoundingRules.VillageLabor);
    }

    private static List<NPCRuntime> NormalizeParty(IEnumerable<NPCRuntime> participants)
    {
        return (participants ?? Enumerable.Empty<NPCRuntime>())
            .Where(item => item != null && item.Character.IsAlive && item.State == NPCState.Idle)
            .GroupBy(item => item.CharacterId)
            .Select(group => group.First())
            .OrderBy(item => item.CharacterId, StringComparer.Ordinal)
            .Take(4)
            .ToList();
    }

    private static void ApplyRaidPressure(ExternalThreatDefinition definition)
    {
        VillageState village = PlayerManager.Instance?.playerData?.founding?.village;
        if (village == null) return;
        int available = Math.Max(0, village.totalLabor);
        int applied = Math.Min(available, definition.raidLaborLoss);
        village.totalLabor -= applied;
        int shortfall = definition.raidLaborLoss - applied;
        int populationLoss = shortfall / 2;
        if (populationLoss > 0) village.population = Math.Max(0, village.population - populationLoss);
        TimeManager.Instance?.RecordThreatNotice(
            populationLoss > 0
                ? $"青石村受袭：劳动力-{applied}，人口-{populationLoss}"
                : $"青石村受袭：劳动力-{applied}");
    }

    private static void ApplyCombatOutcome(ThreatResolutionRecord record, List<NPCRuntime> party)
    {
        NPCManager npcManager = NPCManager.Instance;
        switch (record.combat.resultTier)
        {
            case CombatResultTier.PerfectVictory:
                party.ForEach(item => item.AddCombatExperience(5));
                ApplyVillageChanges(0, 0, 10, record);
                break;
            case CombatResultTier.Victory:
                party.ForEach(item => item.AddCombatExperience(3));
                ApplyVillageChanges(0, 0, 5, record);
                npcManager?.Injured(party.First(item => item.CharacterId == record.weakestCharacterId), 2);
                break;
            case CombatResultTier.CostlyVictory:
                party.ForEach(item => { item.AddCombatExperience(3); npcManager?.Injured(item, 3); });
                ApplyVillageChanges(0, -5, 0, record);
                break;
            default:
                party.ForEach(item => item.AddCombatExperience(1));
                if (record.combat.retreatSucceeded)
                {
                    party.ForEach(item => npcManager?.Injured(item, 5));
                    ApplyVillageChanges(0, -10, -5, record);
                }
                else
                {
                    NPCRuntime weakest = party.First(item => item.CharacterId == record.weakestCharacterId);
                    npcManager?.Kill(weakest, "青石村外部威胁中撤退失败");
                    foreach (NPCRuntime survivor in party.Where(item => item.CharacterId != record.weakestCharacterId && item.Character.IsAlive))
                        npcManager?.Injured(survivor, 7);
                    ApplyVillageChanges(-5, -20, -10, record);
                }
                break;
        }
    }

    private static void ApplyVillageChanges(int population, int labor, int relation, ThreatResolutionRecord record)
    {
        PlayerManager manager = PlayerManager.Instance;
        if (manager == null) return;
        int populationBefore = manager.playerData?.founding?.village?.population ?? 0;
        int laborBefore = manager.playerData?.founding?.village?.totalLabor ?? 0;
        int relationBefore = manager.playerData?.founding?.village?.relation ?? 0;
        manager.ChangeVillagePopulation(population);
        manager.ChangeVillageLabor(labor);
        manager.AddVillageRelation(relation);
        VillageState village = manager.playerData?.founding?.village;
        record.populationChange += (village?.population ?? populationBefore) - populationBefore;
        record.laborChange += (village?.totalLabor ?? laborBefore) - laborBefore;
        record.relationChange += (village?.relation ?? relationBefore) - relationBefore;
    }

    private static void Complete(ActiveThreatState state, ThreatResolutionRecord record)
    {
        state.status = ExternalThreatStatus.Resolved;
        state.selectedPlan = record.plan;
        state.selectedCharacterIds = new List<string>(record.participantIds);
        state.resolution = record;
        PlayerManager.Instance?.NotifyFoundingChanged();
        SaveManager.Instance?.AutoSave();
    }

    private static string BuildNarrative(ExternalThreatDefinition definition, CombatPlanType plan,
        ThreatResolutionRecord record, List<NPCRuntime> party)
    {
        string names = string.Join("、", party.Select(item => item.Character.displayName));
        string first = (definition.firstExchangeTemplate ?? "{participants}与敌人第一次交手。")
            .Replace("{participants}", names)
            .Replace("{initiative}", InitiativeName(record.combat.initiativeModifier));
        string battle = (definition.battleTemplate ?? "宗门采取{plan}，最终取得{result}。")
            .Replace("{plan}", PlanName(plan))
            .Replace("{result}", ResultName(record.combat.resultTier));
        if (!record.combat.retreatAttempted) return first + battle;
        string retreat = (definition.retreatTemplate ?? "撤退{retreatResult}。")
            .Replace("{retreatResult}", record.combat.retreatSucceeded ? "成功" : "失败");
        return first + battle + retreat;
    }

    public static string ResultName(CombatResultTier tier)
    {
        switch (tier)
        {
            case CombatResultTier.PerfectVictory: return "完胜";
            case CombatResultTier.Victory: return "胜利";
            case CombatResultTier.CostlyVictory: return "惨胜";
            case CombatResultTier.Failure: return "失败";
            default: return "惨败";
        }
    }

    private static string PlanName(CombatPlanType plan) =>
        plan == CombatPlanType.HeadOn ? "正面迎击" : plan == CombatPlanType.SimpleDefense ? "简单防御" : "退守洞府";

    private static string InitiativeName(float modifier) =>
        modifier > 1f ? "我方取得主动" : modifier < 1f ? "敌方取得主动" : "双方同时察觉";
}
