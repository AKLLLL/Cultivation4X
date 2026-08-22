using System;
using System.Linq;
using Cultivation4X.WorldMap;
using UnityEngine;

/// <summary>
/// 游戏时间管理器。
/// 负责推进游戏天数，并通知其他系统。
/// </summary>
public class TimeManager : MonoBehaviour
{
    public static TimeManager Instance;

    /// <summary>
    /// 当前是第几天。
    /// </summary>
    public int CurrentDay { get; private set; } = 0;

    /// <summary>
    /// 每经过一天触发一次。
    /// 参数：当前天数。
    /// </summary>
    public event Action<int> OnDayPassed;
    public event Action<DaySettlementSummary> OnDaySettlementReady;
    private readonly System.Collections.Generic.List<FacilityUpgradeRecord> facilityUpgrades = new System.Collections.Generic.List<FacilityUpgradeRecord>();
    private readonly System.Collections.Generic.Dictionary<string, int> preAdvanceItemChanges =
        new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
    private readonly System.Collections.Generic.List<string> threatNotices = new System.Collections.Generic.List<string>();
    private bool isAdvancingDay;
    public DaySettlementSummary UnreadDaySettlement { get; private set; }
    public bool IsSettlementOpen { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 点击"结束今天"按钮调用。
    /// </summary>
    public void EndDay()
    {
        if (isAdvancingDay) { Debug.LogWarning("正在推进天数，忽略重复调用"); return; }
        if (IsSettlementOpen) { Debug.LogWarning("请先关闭每日结算"); return; }
        if (EventManager.Instance != null && !EventManager.Instance.PrepareForDayAdvance(CurrentDay, out string reason))
        {
            Debug.LogWarning($"无法结束今天: {reason}");
            return;
        }
        try
        {
            DayStartSnapshot before = CaptureDayStart();
            CurrentDay++;
            isAdvancingDay = true;

            Debug.Log($"今天是第 {CurrentDay} 天");

            // 固定顺序：角色恢复/修炼 -> 任务推进 -> 外部威胁 -> 事件抽取 -> 月产出 -> 结算 -> 自动保存。
            NPCManager.Instance?.OnDayPassed();
            Cultivation4X.WorldMap.WorldMapContentEffects.ApplyDaily(CurrentDay);
            OnDayPassed?.Invoke(CurrentDay);
            ExternalThreatRules.ProcessDay(CurrentDay);
            EventManager.Instance?.ProcessDay(CurrentDay);
            System.Collections.Generic.List<ResourceProductionRecord> monthlyProduction =
                CurrentDay > 0 && CurrentDay % ResourceManager.DaysPerMonth == 0
                    ? ResourceManager.MonthUpdate(CurrentDay, CurrentDay / ResourceManager.DaysPerMonth)
                    : new System.Collections.Generic.List<ResourceProductionRecord>();
            isAdvancingDay = false;
            UnreadDaySettlement = BuildSettlement(before, monthlyProduction);
            SaveManager.Instance?.AutoSave();
            OnDaySettlementReady?.Invoke(UnreadDaySettlement);
        }
        catch (Exception exception)
        {
            Debug.LogError($"结束今天失败: {exception}");
        }
        finally
        {
            isAdvancingDay = false;
        }
    }

    public void RecordFacilityUpgrade(FacilityType facility, int newLevel)
    {
        facilityUpgrades.Add(new FacilityUpgradeRecord { facility = facility, newLevel = newLevel });
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
        if (summary != null) OnDaySettlementReady?.Invoke(summary);
    }
    public void MarkSettlementRead() => UnreadDaySettlement = null;
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
                { cultivation = npc.Cultivation, naqiProgress = npc.Character.naqiProgress,
                    techniqueMastery = npc.Character.techniqueMastery, realm = npc.Realm, health = npc.Health };
            }
        return result;
    }

    private DaySettlementSummary BuildSettlement(DayStartSnapshot before,
        System.Collections.Generic.List<ResourceProductionRecord> monthlyProduction)
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
                .Concat(threatNotices).ToList(),
            facilityUpgrades = new System.Collections.Generic.List<FacilityUpgradeRecord>(facilityUpgrades)
        };
        facilityUpgrades.Clear();
        threatNotices.Clear();
        preAdvanceItemChanges.Clear();
        if (NPCManager.Instance != null)
            foreach (NPCRuntime npc in NPCManager.Instance.GetAllNPC())
            {
                if (string.IsNullOrWhiteSpace(npc.CharacterId)) continue;
                before.characters.TryGetValue(npc.CharacterId, out CharacterSnapshot old);
                old = old ?? new CharacterSnapshot { realm = npc.Realm, health = npc.Health };
                int cultivationChange = npc.Cultivation - old.cultivation;
                float naqiChange = npc.Character.naqiProgress - old.naqiProgress;
                float masteryChange = npc.Character.techniqueMastery - old.techniqueMastery;
                if (cultivationChange != 0 || naqiChange != 0f || masteryChange != 0f ||
                    npc.Character.completedMajorCycleToday || old.realm != npc.Realm || old.health != npc.Health)
                    result.characterChanges.Add(new CharacterDayChange { characterId = npc.CharacterId, displayName = npc.Character.displayName,
                        cultivationChange = cultivationChange, dailyAura = npc.Cultivation,
                        naqiProgressChange = naqiChange, techniqueMasteryChange = masteryChange,
                        completedMajorCycle = npc.Character.completedMajorCycleToday,
                        qiDisorderResponse = npc.Character.qiDisorderResponse,
                        realmBefore = old.realm, realmAfter = npc.Realm,
                        healthBefore = old.health, healthAfter = npc.Health });
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
        public int cultivation;
        public float naqiProgress;
        public float techniqueMastery;
        public CultivationRealm realm;
        public HealthState health;
    }

    public void RestoreDay(int day)
    {
        CurrentDay = Mathf.Max(0, day);
    }
}
