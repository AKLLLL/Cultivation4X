using System;
using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;
using UnityEngine;

/// <summary>
/// 游戏时间管理器。
/// 负责现实秒到游戏小时的换算、日历节点与唯一日结编排。
/// </summary>
public class TimeManager : MonoBehaviour
{
    public static TimeManager Instance;

    /// <summary>
    /// 当前是第几天。
    /// </summary>
    public int CurrentDay { get; private set; } = 0;
    public int ActiveDay => CurrentDay + 1;
    public float CurrentHour { get; private set; } = 6f;
    public float SelectedSpeed { get; private set; } = 1f;
    public PauseReason PauseReasons { get; private set; } = PauseReason.Player | PauseReason.FlowState;
    public bool IsPaused => PauseReasons != PauseReason.None;
    public GameDateTime CurrentDateTime => GameCalendarRules.FromActiveDay(ActiveDay, CurrentHour);

    /// <summary>
    /// 每经过一天触发一次。
    /// 参数：当前天数。
    /// </summary>
    public event Action<int> OnDayPassed;
    public event Action<DaySettlementSummary> OnDaySettlementReady;
    public event Action<GameDateTime> OnTimeChanged;
    public event Action<GameDateTime> OnHourChanged;
    public event Action<GameDateTime> OnDayStarted;
    public event Action<GameDateTime> OnDayEnding;
    public event Action<DaySettlementSummary> OnDayEnded;
    public event Action<GameDateTime> OnMonthStarted;
    public event Action<DaySettlementSummary> OnMonthEnded;
    private readonly System.Collections.Generic.Dictionary<string, int> preAdvanceItemChanges =
        new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
    private readonly System.Collections.Generic.List<string> threatNotices = new System.Collections.Generic.List<string>();
    private bool isAdvancingDay;
    private DailyScheduleState dailySchedule;
    private WorldTimeConfig config;
    public DaySettlementSummary UnreadDaySettlement { get; private set; }
    public bool IsSettlementOpen { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            config = WorldTimeConfigLoader.Load();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        SynchronizePauseReasons();
        EnsureCurrentDayPrepared();
        if (IsPaused || !CanWorldClockRun()) return;
        float gameHours = Time.unscaledDeltaTime * 24f / config.secondsPerDay * SelectedSpeed;
        AdvanceHoursInternal(gameHours, false);
    }

    /// <summary>
    /// 兼容旧测试入口；正式玩法由世界时钟跨过午夜触发。
    /// </summary>
    public void EndDay()
    {
        AdvanceOneDayForTesting();
    }

    public void PauseByPlayer()
    {
        PauseReasons |= PauseReason.Player;
        NotifyTimeChanged();
    }

    public bool TrySetSpeed(float multiplier, out string reason)
    {
        config = config ?? WorldTimeConfigLoader.Load();
        if (!config.speedMultipliers.Any(value => Mathf.Approximately(value, multiplier)))
        {
            reason = "时间倍率无效";
            return false;
        }
        SynchronizePauseReasons();
        PauseReason blocking = PauseReasons & ~(PauseReason.Player);
        if (blocking != PauseReason.None)
        {
            reason = PauseReasonLabel(blocking);
            return false;
        }
        SelectedSpeed = multiplier;
        PauseReasons &= ~PauseReason.Player;
        reason = null;
        NotifyTimeChanged();
        return true;
    }

    public void AcknowledgeMonthEnd()
    {
        PauseReasons &= ~PauseReason.MonthEnd;
        PauseReasons |= PauseReason.Player;
        NotifyTimeChanged();
    }

    public string PauseReasonText() => PauseReasonLabel(PauseReasons);

    public void AdvanceOneHourForTesting() => AdvanceHoursInternal(1f, true);

    public void AdvanceOneDayForTesting()
    {
        if (isAdvancingDay) { Debug.LogWarning("正在推进天数，忽略重复调用"); return; }
        EnsureCurrentDayPrepared();
        float hours = 24f - CurrentHour;
        if (hours <= 0.0001f) hours = 24f;
        AdvanceHoursInternal(hours, true);
    }

    public int AdvanceDaysForTesting(int days)
    {
        int requested = Mathf.Max(0, days);
        int before = CurrentDay;
        for (int index = 0; index < requested; index++)
        {
            PauseReasons &= ~PauseReason.MonthEnd;
            AdvanceOneDayForTesting();
            if (CurrentDay == before + index) break;
        }
        return CurrentDay - before;
    }

    public int AdvanceMonthsForTesting(int months) =>
        AdvanceDaysForTesting(Mathf.Max(0, months) * GameCalendarRules.DaysPerMonth);

    public DailyScheduleState GetDailySchedule() => dailySchedule;

    public DiscipleDailySchedule GetSchedule(string characterId) =>
        dailySchedule?.day == ActiveDay ? dailySchedule.Get(characterId) : null;

    public string GetCurrentActivityLabel(string characterId)
    {
        Mission mission = NPCManager.Instance?.GetRuntime(characterId)?.CurrentMission;
        if (mission != null && (mission.State == MissionState.Active || mission.State == MissionState.WaitingNode))
            return mission.Data?.name ?? "执行任务";
        return GetSchedule(characterId)?.ActivityAt(CurrentHour) ??
               (CurrentHour < 6f || CurrentHour >= 20f ? "休息" : "自由活动");
    }

    public bool TryGetLockedActivity(string characterId, int day, out MonthlyActivityType activity,
        out string cultivationActionId)
    {
        DiscipleDailySchedule entry = dailySchedule?.day == day ? dailySchedule.Get(characterId) : null;
        if (entry == null)
        {
            activity = default;
            cultivationActionId = null;
            return false;
        }
        activity = entry.activity;
        cultivationActionId = entry.cultivationActionId;
        return true;
    }

    public WorldTimeSaveData CaptureWorldTime() => new WorldTimeSaveData
    {
        currentHour = CurrentHour,
        selectedSpeed = SelectedSpeed,
        dayPrepared = dailySchedule?.day == ActiveDay,
        dailySchedule = dailySchedule
    };

    public void RestoreWorldTime(WorldTimeSaveData state)
    {
        config = config ?? WorldTimeConfigLoader.Load();
        CurrentHour = Mathf.Clamp(state?.currentHour ?? config.dayStartHour, 0f, 23.9999f);
        float requestedSpeed = state?.selectedSpeed ?? 1f;
        SelectedSpeed = config.speedMultipliers.Any(value => Mathf.Approximately(value, requestedSpeed))
            ? requestedSpeed : 1f;
        dailySchedule = state?.dayPrepared == true ? state.dailySchedule : null;
        if (dailySchedule != null && dailySchedule.day != ActiveDay) dailySchedule = null;
        PauseReasons = PauseReason.Player | PauseReason.FlowState;
        NotifyTimeChanged();
    }

    public void ResetForNewGame()
    {
        config = config ?? WorldTimeConfigLoader.Load();
        CurrentDay = 0;
        CurrentHour = config.dayStartHour;
        SelectedSpeed = 1f;
        dailySchedule = null;
        UnreadDaySettlement = null;
        IsSettlementOpen = false;
        PauseReasons = PauseReason.Player | PauseReason.FlowState;
        NotifyTimeChanged();
    }

    private void AdvanceHoursInternal(float gameHours, bool testing)
    {
        if (gameHours <= 0f || isAdvancingDay) return;
        config = config ?? WorldTimeConfigLoader.Load();
        float remaining = gameHours;
        int guard = 0;
        while (remaining > 0.00001f && guard++ < 10000)
        {
            if (!testing)
            {
                SynchronizePauseReasons();
                if (IsPaused || !CanWorldClockRun()) break;
            }
            float nextHour = Mathf.Min(24f, Mathf.Floor(CurrentHour + 0.0001f) + 1f);
            float distance = nextHour - CurrentHour;
            if (remaining + 0.00001f < distance)
            {
                CurrentHour += remaining;
                remaining = 0f;
                NotifyTimeChanged();
                break;
            }
            if (Mathf.Approximately(nextHour, 24f) && !CanCrossMidnight(out string reason))
            {
                CurrentHour = Mathf.Min(23.999f, CurrentHour + Mathf.Max(0f, distance - 0.001f));
                AutoPause(PauseReason.CriticalEvent, reason);
                break;
            }
            CurrentHour = nextHour;
            remaining -= distance;
            ProcessHourBoundary(Mathf.RoundToInt(nextHour));
        }
    }

    private void ProcessHourBoundary(int hour)
    {
        if (hour >= 24)
        {
            if (!SettleActiveDay()) return;
            CurrentHour = 0f;
            dailySchedule = null;
            GameDateTime date = CurrentDateTime;
            if (date.day == 1) SafeInvoke(OnMonthStarted, date, "OnMonthStarted");
            SafeInvoke(OnHourChanged, date, "OnHourChanged");
            NotifyTimeChanged();
            return;
        }
        GameDateTime current = CurrentDateTime;
        SafeInvoke(OnHourChanged, current, "OnHourChanged");
        if (Mathf.Approximately(hour, config.dayStartHour)) PrepareCurrentDay();
        if (Mathf.Approximately(hour, config.dayEndingHour)) SafeInvoke(OnDayEnding, current, "OnDayEnding");
        NotifyTimeChanged();
    }

    private bool SettleActiveDay()
    {
        if (isAdvancingDay) return false;
        if (!CanCrossMidnight(out string reason))
        {
            AutoPause(PauseReason.CriticalEvent, reason);
            return false;
        }
        try
        {
            EnsureCurrentDayPrepared();
            DayStartSnapshot before = CaptureDayStart();
            CurrentDay++;
            isAdvancingDay = true;
            Debug.Log($"完成第 {CurrentDay} 天结算");

            List<DiscipleDayExecutionResult> executions = NPCManager.Instance?.ProcessDayWithResults() ??
                new List<DiscipleDayExecutionResult>();
            Cultivation4X.WorldMap.WorldMapContentEffects.ApplyDaily(CurrentDay);
            MissionManager.Instance?.ProcessDay(CurrentDay);
            ExternalThreatRules.ProcessDay(CurrentDay);
            EventManager.Instance?.ProcessDay(CurrentDay);
            NPCManager.Instance?.ApplyNightlyCultivationSettlement();
            bool monthEnd = CurrentDay > 0 && CurrentDay % GameCalendarRules.DaysPerMonth == 0;
            List<ResourceProductionRecord> monthlyProduction = monthEnd
                ? ResourceManager.MonthUpdate(CurrentDay, CurrentDay / GameCalendarRules.DaysPerMonth)
                : new List<ResourceProductionRecord>();
            UnreadDaySettlement = BuildSettlement(before, monthlyProduction, executions);
            UnreadDaySettlement.isMonthEnd = monthEnd;
            UnreadDaySettlement.monthIndex = MonthlyPlanRules.MonthIndex(CurrentDay);
            DiscipleDecisionManager.Instance?.ProcessSettledDay(CurrentDay);
            GrowthFeedbackRules.ProcessSettledDay(PlayerManager.Instance?.playerData, CurrentDay, UnreadDaySettlement);
            // 最终自动存档必须已经处于次日 00:00，不能写入 24:00 或上一日锁定日程。
            CurrentHour = 0f;
            dailySchedule = null;
            isAdvancingDay = false;
            SaveManager.Instance?.AutoSave();
            SafeInvoke(OnDayPassed, CurrentDay, "OnDayPassed");
            SafeInvoke(OnDayEnded, UnreadDaySettlement, "OnDayEnded");
            SafeInvoke(OnDaySettlementReady, UnreadDaySettlement, "OnDaySettlementReady");
            if (monthEnd)
            {
                AutoPause(PauseReason.MonthEnd, "月结待确认");
                SafeInvoke(OnMonthEnded, UnreadDaySettlement, "OnMonthEnded");
            }
            if (EventManager.Instance?.HasCriticalInbox == true)
                AutoPause(PauseReason.CriticalEvent, "存在尚未处理的关键事件");
            return true;
        }
        catch (Exception exception)
        {
            PauseReasons |= PauseReason.SettlementFailure | PauseReason.Player;
            Debug.LogError($"日结失败，世界时间已暂停，请读取上一份完整存档: {exception}");
            return false;
        }
        finally
        {
            isAdvancingDay = false;
        }
    }

    private bool CanCrossMidnight(out string reason)
    {
        if (IsSettlementOpen) { reason = "请先关闭结算窗口"; return false; }
        if (EventManager.Instance != null && !EventManager.Instance.PrepareForDayAdvance(CurrentDay, out reason))
            return false;
        reason = null;
        return true;
    }

    private void EnsureCurrentDayPrepared()
    {
        if (!CanWorldClockRun() || CurrentHour < config.dayStartHour || dailySchedule?.day == ActiveDay) return;
        PrepareCurrentDay();
    }

    private void PrepareCurrentDay()
    {
        if (dailySchedule?.day == ActiveDay) return;
        DailyScheduleState state = new DailyScheduleState { day = ActiveDay };
        DiscipleAIConfig aiConfig = null;
        IdentityDefinition sectDutyIdentity = null;
        foreach (NPCRuntime npc in NPCManager.Instance?.GetLivingNPC() ?? Enumerable.Empty<NPCRuntime>())
        {
            if (npc?.Character == null || string.IsNullOrWhiteSpace(npc.CharacterId)) continue;
            MonthlyActivityType activity = MonthlyPlanRules.ActivityFor(npc, ActiveDay);
            Mission mission = npc.CurrentMission;
            DiscipleDailySchedule entry = new DiscipleDailySchedule
            {
                characterId = npc.CharacterId,
                activity = activity,
                missionOccupied = mission != null &&
                    (mission.State == MissionState.Active || mission.State == MissionState.WaitingNode)
            };
            if (entry.missionOccupied)
            {
                entry.segments.Add(Segment(config.dayStartHour, config.dayEndingHour, mission.Data?.id,
                    mission.Data?.name ?? "执行任务"));
            }
            else if (activity == MonthlyActivityType.Training)
            {
                entry.cultivationActionId = DailyCultivationSimulator.SelectActionId(npc, ActiveDay);
                entry.segments.Add(Segment(6f, 11f, "absorb_aura", "吸收天地灵气"));
                entry.segments.Add(Segment(11f, 18f, entry.cultivationActionId,
                    DailyCultivationSimulator.ActionName(entry.cultivationActionId)));
                entry.segments.Add(Segment(18f, 20f, "cultivation_recovery", "调息"));
            }
            else if (activity == MonthlyActivityType.SectDuty)
            {
                if (aiConfig == null)
                {
                    aiConfig = DiscipleAIConfigLoader.Load();
                    sectDutyIdentity = aiConfig.Identities.FirstOrDefault(item => item != null && item.autonomyEnabled);
                }
                SectDutyDecision preview = SectDutyResolver.Resolve(npc, ActiveDay, aiConfig, sectDutyIdentity,
                    PlayerManager.Instance?.playerData, Cultivation4X.WorldMap.WorldMapSession.Current,
                    Cultivation4X.WorldMap.WorldMapSession.Progress);
                string actionId = preview?.Action?.id ?? "sect_general_maintenance";
                string targetName = preview?.Zone == null ? null :
                    Cultivation4X.WorldMap.SectFunctionalZoneRules.DisplayName(
                        Cultivation4X.WorldMap.WorldMapSession.Current, preview.Zone);
                string actionName = preview?.Action?.displayName ?? "日常宗门维护";
                string label = string.IsNullOrWhiteSpace(targetName) ? actionName : $"{actionName} · {targetName}";
                entry.segments.Add(Segment(6f, 18f, actionId, label));
                entry.segments.Add(Segment(18f, 20f, "sect_duty_handover", "整理交接"));
            }
            else
            {
                entry.segments.Add(Segment(6f, 12f, "free_activity", "自由活动"));
                entry.segments.Add(Segment(12f, 18f, "free_rest", "游历休整"));
                entry.segments.Add(Segment(18f, 20f, "free_reflection", "自省"));
            }
            state.disciples.Add(entry);
        }
        dailySchedule = state;
        SafeInvoke(OnDayStarted, CurrentDateTime, "OnDayStarted");
        NotifyTimeChanged();
    }

    private static DailyScheduleSegment Segment(float start, float end, string actionId, string label) =>
        new DailyScheduleSegment { startHour = start, endHour = end, actionId = actionId, label = label };

    private bool CanWorldClockRun()
    {
        if (SaveManager.Instance == null || !SaveManager.Instance.IsInitializationComplete ||
            !SaveManager.Instance.HasGameSession) return false;
        if (GameFlowStateManager.Instance == null || GameFlowStateManager.Instance.Current != GameFlowState.WorldMap)
            return false;
        return GameFlowPermission.IsSectEstablished(PlayerManager.Instance?.playerData?.founding);
    }

    private void SynchronizePauseReasons()
    {
        if (CanWorldClockRun()) PauseReasons &= ~PauseReason.FlowState;
        else PauseReasons |= PauseReason.FlowState;
        if (EventManager.Instance?.HasCriticalInbox == true)
        {
            if ((PauseReasons & PauseReason.CriticalEvent) == 0)
                PauseReasons |= PauseReason.Player;
            PauseReasons |= PauseReason.CriticalEvent;
        }
        else PauseReasons &= ~PauseReason.CriticalEvent;
    }

    private void AutoPause(PauseReason reason, string message)
    {
        PauseReasons |= reason | PauseReason.Player;
        if (!string.IsNullOrWhiteSpace(message)) Debug.Log($"世界时间暂停: {message}");
        NotifyTimeChanged();
    }

    private static string PauseReasonLabel(PauseReason reasons)
    {
        if ((reasons & PauseReason.SettlementFailure) != 0) return "日结失败，请读档";
        if ((reasons & PauseReason.CriticalEvent) != 0) return "请先处理关键事件";
        if ((reasons & PauseReason.MonthEnd) != 0) return "请先查看月结";
        if ((reasons & PauseReason.FlowState) != 0) return "当前流程不运行世界时间";
        return (reasons & PauseReason.Player) != 0 ? "已暂停" : null;
    }

    private void NotifyTimeChanged() => SafeInvoke(OnTimeChanged, CurrentDateTime, "OnTimeChanged");

    private static void SafeInvoke<T>(Action<T> handlers, T value, string eventName)
    {
        if (handlers == null) return;
        foreach (Action<T> handler in handlers.GetInvocationList())
        {
            try { handler(value); }
            catch (Exception exception) { Debug.LogError($"时间通知 {eventName} 订阅者异常: {exception}"); }
        }
    }

    public void RecordPreAdvanceItemChange(string itemId, int countChange)
    {
        if (isAdvancingDay || string.IsNullOrWhiteSpace(itemId) || countChange == 0) return;
        preAdvanceItemChanges.TryGetValue(itemId, out int current);
        preAdvanceItemChanges[itemId] = current + countChange;
    }

    public void RecordThreatNotice(string notice)
    {
        RecordDayNotice(notice);
    }

    public void RecordDayNotice(string notice)
    {
        if (!string.IsNullOrWhiteSpace(notice)) threatNotices.Add(notice);
    }

    public void RestoreUnreadSettlement(DaySettlementSummary summary)
    {
        UnreadDaySettlement = summary;
        if (summary?.isMonthEnd == true) PauseReasons |= PauseReason.MonthEnd | PauseReason.Player;
    }
    public void MarkSettlementRead()
    {
        bool wasMonthEnd = UnreadDaySettlement?.isMonthEnd == true;
        UnreadDaySettlement = null;
        if (wasMonthEnd) AcknowledgeMonthEnd();
        else NotifyTimeChanged();
    }
    public void SetSettlementOpen(bool open) => IsSettlementOpen = open;

    private DayStartSnapshot CaptureDayStart()
    {
        DayStartSnapshot result = new DayStartSnapshot
        {
            reputation = PlayerManager.Instance == null ? 0 : PlayerManager.Instance.playerData.reputation,
            items = CaptureWarehouseCounts()
        };
        if (NPCManager.Instance != null)
            foreach (NPCRuntime npc in NPCManager.Instance.GetAllNPC())
            {
                if (string.IsNullOrWhiteSpace(npc.CharacterId)) continue;
                result.characters[npc.CharacterId] = new CharacterSnapshot
                { currentAura = npc.CurrentAura, naqiProgress = npc.Character.naqiProgress,
                    techniqueUnderstanding = TechniqueRules.MainUnderstanding(npc.Character), auraControl = npc.Character.auraControl,
                    fatigue = npc.Character.fatigue, realmLayer = npc.RealmLayer, realm = npc.Realm, health = npc.Health,
                    techniqueId = npc.Character.mainTechniqueId };
            }
        return result;
    }

    private DaySettlementSummary BuildSettlement(DayStartSnapshot before,
        System.Collections.Generic.List<ResourceProductionRecord> monthlyProduction,
        System.Collections.Generic.List<DiscipleDayExecutionResult> executions)
    {
        System.Collections.Generic.Dictionary<string, int> after = CaptureWarehouseCounts();
        DaySettlementSummary result = new DaySettlementSummary
        {
            day = CurrentDay,
            reputationChange = (PlayerManager.Instance == null ? 0 : PlayerManager.Instance.playerData.reputation) - before.reputation,
            itemChanges = before.items.Keys.Concat(after.Keys).Concat(preAdvanceItemChanges.Keys)
                .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => new ItemDayChange
                {
                    itemId = id,
                    countChange = (after.TryGetValue(id, out int current) ? current : 0) -
                                  (before.items.TryGetValue(id, out int previous) ? previous : 0) +
                                  (preAdvanceItemChanges.TryGetValue(id, out int prior) ? prior : 0)
                }).Where(change => change.countChange != 0).ToList(),
            resourceProduction = monthlyProduction ?? new System.Collections.Generic.List<ResourceProductionRecord>(),
            missionResults = MissionManager.Instance == null ? new System.Collections.Generic.List<MissionDayResult>() : MissionManager.Instance.ConsumeDailyResults(),
            newEventTitles = (EventManager.Instance == null ? new System.Collections.Generic.List<string>() : EventManager.Instance.ConsumeNewEventTitles())
                .Concat(threatNotices).ToList()
        };
        threatNotices.Clear();
        preAdvanceItemChanges.Clear();
        Dictionary<string, DiscipleDayExecutionResult> executionById = (executions ??
                new System.Collections.Generic.List<DiscipleDayExecutionResult>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.discipleId))
            .GroupBy(item => item.discipleId).ToDictionary(group => group.Key, group => group.First());
        if (NPCManager.Instance != null)
            foreach (NPCRuntime npc in NPCManager.Instance.GetAllNPC())
            {
                if (string.IsNullOrWhiteSpace(npc.CharacterId)) continue;
                before.characters.TryGetValue(npc.CharacterId, out CharacterSnapshot old);
                old = old ?? new CharacterSnapshot { realm = npc.Realm, health = npc.Health };
                float auraChange = npc.CurrentAura - old.currentAura;
                float naqiChange = npc.Character.naqiProgress - old.naqiProgress;
                float understandingChange = TechniqueRules.MainUnderstanding(npc.Character) - old.techniqueUnderstanding;
                float controlChange = npc.Character.auraControl - old.auraControl;
                float fatigueChange = npc.Character.fatigue - old.fatigue;
                if (auraChange != 0f || naqiChange != 0f || understandingChange != 0f || controlChange != 0f || fatigueChange != 0f ||
                    old.realmLayer != npc.RealmLayer || old.realm != npc.Realm || old.health != npc.Health)
                    result.characterChanges.Add(new CharacterDayChange { characterId = npc.CharacterId, displayName = npc.Character.displayName,
                        currentAuraChange = auraChange, currentAura = npc.CurrentAura,
                        naqiProgressChange = naqiChange, techniqueUnderstandingChange = understandingChange,
                        auraControlChange = controlChange, fatigueChange = fatigueChange,
                        realmLayerBefore = old.realmLayer, realmLayerAfter = npc.RealmLayer,
                        cultivationResult = npc.Character.latestCultivationResult?.date == CurrentDay ? npc.Character.latestCultivationResult : null,
                        realmBefore = old.realm, realmAfter = npc.Realm,
                        healthBefore = old.health, healthAfter = npc.Health });
                if (!executionById.TryGetValue(npc.CharacterId, out DiscipleDayExecutionResult execution)) continue;
                string techniqueId = string.IsNullOrWhiteSpace(npc.Character.mainTechniqueId)
                    ? old.techniqueId : npc.Character.mainTechniqueId;
                float understandingAfter = TechniqueRules.MainUnderstanding(npc.Character);
                float understandingBefore = old.techniqueId == techniqueId ? old.techniqueUnderstanding : 0f;
                float positiveNaqi = execution.cultivationResult?.naqiGain ?? Mathf.Max(0f,
                    (npc.RealmLayer - old.realmLayer) * 100f + npc.Character.naqiProgress - old.naqiProgress);
                result.discipleResults.Add(new DiscipleDayResult
                {
                    discipleId = npc.CharacterId,
                    displayName = npc.Character.displayName,
                    worldDay = CurrentDay,
                    scheduledActivity = execution.scheduledActivity,
                    actualActivity = execution.actualActivity,
                    actionId = execution.actionId,
                    actionDisplayName = execution.actionDisplayName,
                    targetId = execution.targetId,
                    targetDisplayName = execution.targetDisplayName,
                    departmentId = execution.departmentId,
                    executed = execution.executed,
                    failureReason = execution.failureReason,
                    naqiGain = Mathf.Max(0f, positiveNaqi),
                    auraControlGain = npc.Character.auraControl - old.auraControl,
                    techniqueProgressGain = understandingAfter - understandingBefore,
                    fatigueChange = npc.Character.fatigue - old.fatigue,
                    fatiguePeak = Mathf.Max(old.fatigue, npc.Character.fatigue),
                    fatigueBefore = old.fatigue,
                    fatigueAfter = npc.Character.fatigue,
                    naqiBefore = old.naqiProgress,
                    naqiAfter = npc.Character.naqiProgress,
                    auraControlBefore = old.auraControl,
                    auraControlAfter = npc.Character.auraControl,
                    realmLayerBefore = old.realmLayer,
                    realmLayerAfter = npc.RealmLayer,
                    techniqueId = techniqueId,
                    techniqueUnderstandingBefore = understandingBefore,
                    techniqueUnderstandingAfter = understandingAfter,
                    techniqueStageBefore = TechniqueRules.PersonalStage(understandingBefore),
                    techniqueStageAfter = TechniqueRules.PersonalStage(understandingAfter),
                    cultivationResult = execution.cultivationResult
                });
            }
        return result;
    }

    private class DayStartSnapshot
    {
        public int reputation;
        public System.Collections.Generic.Dictionary<string, int> items =
            new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
        public System.Collections.Generic.Dictionary<string, CharacterSnapshot> characters = new System.Collections.Generic.Dictionary<string, CharacterSnapshot>();
    }

    private static System.Collections.Generic.Dictionary<string, int> CaptureWarehouseCounts()
    {
        var result = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ItemStack item in WarehouseManager.Instance?.GetWarehouseData()?.items ??
                 new System.Collections.Generic.List<ItemStack>())
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId) || item.count <= 0) continue;
            result.TryGetValue(item.itemId, out int current);
            result[item.itemId] = current + item.count;
        }
        return result;
    }

    private class CharacterSnapshot
    {
        public float currentAura;
        public float naqiProgress;
        public float techniqueUnderstanding;
        public float auraControl;
        public float fatigue;
        public string techniqueId;
        public int realmLayer;
        public CultivationRealm realm;
        public HealthState health;
    }

    public void RestoreDay(int day)
    {
        CurrentDay = Mathf.Max(0, day);
        if (dailySchedule != null && dailySchedule.day != ActiveDay) dailySchedule = null;
    }
}
