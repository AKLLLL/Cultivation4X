using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
//每日结算面板
public class DaySettlementPanel : MonoBehaviour
{
    private RectTransform panel;
    private TMP_Text content;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<DaySettlementPanel>() == null)
            new GameObject("DaySettlementPanel").AddComponent<DaySettlementPanel>();
    }

    private void Awake()
    {
        RuntimeUIFactory.Canvas(gameObject, 950);
        panel = RuntimeUIFactory.Panel(transform, "DaySettlement", new Vector2(0.16f, 0.08f), new Vector2(0.84f, 0.92f));
        RuntimeUIFactory.Text(panel, "每日结算", 30, 48);
        content = RuntimeUIFactory.Text(panel, string.Empty, 18, 520);
        Button close = RuntimeUIFactory.Button(panel, "确认"); close.onClick.AddListener(Close);
        panel.gameObject.SetActive(false);
    }

    private void Start()
    {
        if (TimeManager.Instance == null) return;
        TimeManager.Instance.OnDaySettlementReady += Show;
        if (TimeManager.Instance.UnreadDaySettlement != null) Show(TimeManager.Instance.UnreadDaySettlement);
    }

    private void OnDestroy()
    {
        if (TimeManager.Instance != null) TimeManager.Instance.OnDaySettlementReady -= Show;
    }

    private void Show(DaySettlementSummary summary)
    {
        if (summary == null) return;
        StringBuilder text = new StringBuilder();
        text.AppendLine($"第 {summary.day} 天");
        text.AppendLine($"灵材 {Signed(summary.goldChange)}　声望 {Signed(summary.reputationChange)}　基础材料 {Signed(summary.basicMaterialChange)}");
        if (summary.missionResults.Count > 0)
            text.AppendLine("任务：" + string.Join("；", summary.missionResults.Select(item => $"{item.missionName} {StateName(item.state)}")));
        foreach (CharacterDayChange item in summary.characterChanges)
            text.AppendLine($"{item.displayName}：修为 {Signed(item.cultivationChange)}，{HealthName(item.healthBefore)} → {HealthName(item.healthAfter)}" +
                (item.realmBefore == item.realmAfter ? string.Empty : $"，境界 {RealmName(item.realmBefore)} → {RealmName(item.realmAfter)}"));
        if (summary.newEventTitles.Count > 0) text.AppendLine("新事件：" + string.Join("、", summary.newEventTitles));
        if (summary.facilityUpgrades.Count > 0)
            text.AppendLine("设施升级：" + string.Join("、", summary.facilityUpgrades.Select(item => $"{FacilityName(item.facility)} Lv.{item.newLevel}")));
        content.text = text.ToString();
        panel.gameObject.SetActive(true);
        TimeManager.Instance?.SetSettlementOpen(true);
    }

    private void Close()
    {
        panel.gameObject.SetActive(false);
        TimeManager.Instance?.SetSettlementOpen(false);
        TimeManager.Instance?.MarkSettlementRead();
        SaveManager.Instance?.AutoSave();
    }

    private static string Signed(int value) => value >= 0 ? "+" + value : value.ToString();
    private static string StateName(MissionState state) => state == MissionState.Completed ? "完成" : state == MissionState.Failed ? "失败" : "待领奖";
    private static string HealthName(HealthState state)
    {
        switch (state)
        {
            case HealthState.Healthy: return "健康";
            case HealthState.LightInjury: return "轻伤";
            case HealthState.HeavyInjury: return "重伤";
            case HealthState.PermanentTrauma: return "永久创伤";
            case HealthState.Dead: return "死亡";
            default: return state.ToString();
        }
    }
    private static string RealmName(CultivationRealm realm)
    {
        switch (realm)
        {
            case CultivationRealm.Mortal: return "凡人";
            case CultivationRealm.QiRefining: return "炼气";
            case CultivationRealm.Foundation: return "筑基";
            case CultivationRealm.GoldenCore: return "金丹";
            default: return realm.ToString();
        }
    }
    private static string FacilityName(FacilityType facility)
    {
        switch (facility)
        {
            case FacilityType.MissionHall: return "任务堂";
            case FacilityType.Warehouse: return "仓库";
            case FacilityType.TrainingRoom: return "修炼室";
            case FacilityType.SecretRealm: return "秘境";
            case FacilityType.AlchemyRoom: return "炼丹房";
            default: return facility.ToString();
        }
    }
}
