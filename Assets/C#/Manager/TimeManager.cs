using System;
using System.Linq;
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
    private int preAdvanceGoldChange;
    private int preAdvanceMaterialChange;
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
        if (IsSettlementOpen) { Debug.LogWarning("请先关闭每日结算"); return; }
        if (EventManager.Instance != null && !EventManager.Instance.PrepareForDayAdvance(CurrentDay, out string reason))
        {
            Debug.LogWarning($"无法结束今天: {reason}");
            return;
        }
        DayStartSnapshot before = CaptureDayStart();
        CurrentDay++;
        isAdvancingDay = true;

        Debug.Log($"今天是第 {CurrentDay} 天");

        // 固定顺序：角色恢复/修炼 -> 任务推进 -> 外部威胁 -> 事件抽取 -> 结算 -> 自动保存。
        NPCManager.Instance?.OnDayPassed();
        Cultivation4X.WorldMap.WorldMapContentEffects.ApplyDaily(CurrentDay);
        OnDayPassed?.Invoke(CurrentDay);
        ExternalThreatRules.ProcessDay(CurrentDay);
        EventManager.Instance?.ProcessDay(CurrentDay);
        isAdvancingDay = false;
        UnreadDaySettlement = BuildSettlement(before);
        SaveManager.Instance?.AutoSave();
        OnDaySettlementReady?.Invoke(UnreadDaySettlement);
    }

    public void RecordFacilityUpgrade(FacilityType facility, int newLevel)
    {
        facilityUpgrades.Add(new FacilityUpgradeRecord { facility = facility, newLevel = newLevel });
    }

    public void RecordPreAdvanceResourceChange(int goldChange, int basicMaterialChange)
    {
        if (isAdvancingDay) return;
        preAdvanceGoldChange += goldChange;
        preAdvanceMaterialChange += basicMaterialChange;
    }

    public void RecordThreatNotice(string notice)
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
            gold = PlayerManager.Instance == null ? 0 : PlayerManager.Instance.playerData.gold,
            reputation = PlayerManager.Instance == null ? 0 : PlayerManager.Instance.playerData.reputation,
            material = WarehouseManager.Instance == null ? 0 : WarehouseManager.Instance.GetItemCount(FacilityRules.BasicMaterialId)
        };
        if (NPCManager.Instance != null)
            foreach (NPCRuntime npc in NPCManager.Instance.GetAllNPC())
            {
                if (string.IsNullOrWhiteSpace(npc.CharacterId)) continue;
                result.characters[npc.CharacterId] = new CharacterSnapshot
                { cultivation = npc.Cultivation, realm = npc.Realm, health = npc.Health };
            }
        return result;
    }

    private DaySettlementSummary BuildSettlement(DayStartSnapshot before)
    {
        DaySettlementSummary result = new DaySettlementSummary
        {
            day = CurrentDay,
            goldChange = ((PlayerManager.Instance == null ? 0 : PlayerManager.Instance.playerData.gold) - before.gold) + preAdvanceGoldChange,
            reputationChange = (PlayerManager.Instance == null ? 0 : PlayerManager.Instance.playerData.reputation) - before.reputation,
            basicMaterialChange = ((WarehouseManager.Instance == null ? 0 : WarehouseManager.Instance.GetItemCount(FacilityRules.BasicMaterialId)) - before.material) + preAdvanceMaterialChange,
            missionResults = MissionManager.Instance == null ? new System.Collections.Generic.List<MissionDayResult>() : MissionManager.Instance.ConsumeDailyResults(),
            newEventTitles = (EventManager.Instance == null ? new System.Collections.Generic.List<string>() : EventManager.Instance.ConsumeNewEventTitles())
                .Concat(threatNotices).ToList(),
            facilityUpgrades = new System.Collections.Generic.List<FacilityUpgradeRecord>(facilityUpgrades)
        };
        facilityUpgrades.Clear();
        threatNotices.Clear();
        preAdvanceGoldChange = 0;
        preAdvanceMaterialChange = 0;
        if (NPCManager.Instance != null)
            foreach (NPCRuntime npc in NPCManager.Instance.GetAllNPC())
            {
                if (string.IsNullOrWhiteSpace(npc.CharacterId)) continue;
                before.characters.TryGetValue(npc.CharacterId, out CharacterSnapshot old);
                old = old ?? new CharacterSnapshot { realm = npc.Realm, health = npc.Health };
                int cultivationChange = npc.Cultivation - old.cultivation;
                if (cultivationChange != 0 || old.realm != npc.Realm || old.health != npc.Health)
                    result.characterChanges.Add(new CharacterDayChange { characterId = npc.CharacterId, displayName = npc.Character.displayName,
                        cultivationChange = cultivationChange, realmBefore = old.realm, realmAfter = npc.Realm,
                        healthBefore = old.health, healthAfter = npc.Health });
            }
        return result;
    }

    private class DayStartSnapshot
    {
        public int gold;
        public int reputation;
        public int material;
        public System.Collections.Generic.Dictionary<string, CharacterSnapshot> characters = new System.Collections.Generic.Dictionary<string, CharacterSnapshot>();
    }

    private class CharacterSnapshot
    {
        public int cultivation;
        public CultivationRealm realm;
        public HealthState health;
    }

    public void RestoreDay(int day)
    {
        CurrentDay = Mathf.Max(0, day);
    }
}
