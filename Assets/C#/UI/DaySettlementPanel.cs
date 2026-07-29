using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
//每日结算面板
public class DaySettlementPanel : MonoBehaviour
{
    private enum SettlementPage
    {
        Overview,
        MissionsEvents,
        Characters,
        ResourcesFacilities
    }

    private RectTransform panel;
    private RectTransform content;
    private Button confirmButton;
    private DaySettlementSummary currentSummary;
    private SettlementPage currentPage;
    private readonly Dictionary<SettlementPage, Button> pageButtons = new Dictionary<SettlementPage, Button>();

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
        RectTransform tabs = RuntimeUIFactory.TabBar(panel, "SettlementTabs");
        AddPageTab(tabs, SettlementPage.Overview, "总览");
        AddPageTab(tabs, SettlementPage.MissionsEvents, "任务与事件");
        AddPageTab(tabs, SettlementPage.Characters, "弟子变化");
        AddPageTab(tabs, SettlementPage.ResourcesFacilities, "资源与设施");
        content = RuntimeUIFactory.ScrollContent(panel, "SettlementScroll");
        confirmButton = RuntimeUIFactory.Button(panel, "确认");
        confirmButton.onClick.AddListener(Close);
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
        currentSummary = summary;
        currentPage = SettlementPage.Overview;
        Refresh();
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(panel.gameObject, CloseInternal);
        else panel.gameObject.SetActive(true);
        TimeManager.Instance?.SetSettlementOpen(true);
    }

    private void Refresh()
    {
        if (content == null || currentSummary == null) return;
        ClearChildren(content);
        RefreshPageTabs();
        if (currentPage == SettlementPage.Overview)
            ShowOverview(currentSummary);
        else if (currentPage == SettlementPage.MissionsEvents)
            ShowMissionsAndEvents(currentSummary);
        else if (currentPage == SettlementPage.Characters)
            ShowCharacterChanges(currentSummary);
        else
            ShowResourcesAndFacilities(currentSummary);
    }

    private void ShowOverview(DaySettlementSummary summary)
    {
        int missionCount = summary.missionResults == null ? 0 : summary.missionResults.Count;
        int eventCount = summary.newEventTitles == null ? 0 : summary.newEventTitles.Count;
        int characterCount = summary.characterChanges == null ? 0 : summary.characterChanges.Count;
        int facilityCount = summary.facilityUpgrades == null ? 0 : summary.facilityUpgrades.Count;
        RuntimeUIFactory.Text(content, $"第 {summary.day} 天", 26, 46);
        RuntimeUIFactory.Text(content,
            $"任务结果 {missionCount} 项　新事件 {eventCount} 项\n弟子变化 {characterCount} 人　设施升级 {facilityCount} 项",
            18, 64);
        RuntimeUIFactory.Text(content,
            $"灵材 {Signed(summary.goldChange)}　声望 {Signed(summary.reputationChange)}　基础材料 {Signed(summary.basicMaterialChange)}",
            18, 42);
    }

    private void ShowMissionsAndEvents(DaySettlementSummary summary)
    {
        RuntimeUIFactory.Text(content, "任务结果", 22, 38);
        if (summary.missionResults == null || summary.missionResults.Count == 0)
            RuntimeUIFactory.Text(content, "今日没有任务结算。", 18, 38);
        else
            foreach (MissionDayResult item in summary.missionResults)
                RuntimeUIFactory.Text(content, $"{item.missionName}　{StateName(item.state)}", 18, 38);

        RuntimeUIFactory.Text(content, "新事件", 22, 38);
        if (summary.newEventTitles == null || summary.newEventTitles.Count == 0)
            RuntimeUIFactory.Text(content, "今日没有新事件。", 18, 38);
        else
            foreach (string title in summary.newEventTitles)
                RuntimeUIFactory.Text(content, title, 18, 38);
    }

    private void ShowCharacterChanges(DaySettlementSummary summary)
    {
        if (summary.characterChanges == null || summary.characterChanges.Count == 0)
        {
            RuntimeUIFactory.Text(content, "今日没有弟子状态变化。", 18, 42);
            return;
        }

        foreach (CharacterDayChange item in summary.characterChanges)
        {
            StringBuilder text = new StringBuilder();
            text.Append($"{item.displayName}：修为 {Signed(item.cultivationChange)}");
            text.Append($"，{HealthName(item.healthBefore)} → {HealthName(item.healthAfter)}");
            if (item.realmBefore != item.realmAfter)
                text.Append($"，境界 {RealmName(item.realmBefore)} → {RealmName(item.realmAfter)}");
            RuntimeUIFactory.Text(content, text.ToString(), 18, 48);
        }
    }

    private void ShowResourcesAndFacilities(DaySettlementSummary summary)
    {
        RuntimeUIFactory.Text(content, "资源变化", 22, 38);
        RuntimeUIFactory.Text(content,
            $"灵材 {Signed(summary.goldChange)}　声望 {Signed(summary.reputationChange)}　基础材料 {Signed(summary.basicMaterialChange)}",
            18, 42);
        RuntimeUIFactory.Text(content, "设施升级", 22, 38);
        if (summary.facilityUpgrades == null || summary.facilityUpgrades.Count == 0)
            RuntimeUIFactory.Text(content, "今日没有设施升级。", 18, 38);
        else
            foreach (FacilityUpgradeRecord item in summary.facilityUpgrades)
                RuntimeUIFactory.Text(content, $"{FacilityName(item.facility)} Lv.{item.newLevel}", 18, 38);
    }

    private void AddPageTab(Transform tabs, SettlementPage page, string label)
    {
        Button button = RuntimeUIFactory.TabButton(tabs, label, currentPage == page);
        pageButtons[page] = button;
        button.onClick.AddListener(() =>
        {
            if (currentPage == page) return;
            currentPage = page;
            Refresh();
        });
    }

    private void RefreshPageTabs()
    {
        foreach (KeyValuePair<SettlementPage, Button> item in pageButtons)
        {
            if (item.Value == null) continue;
            item.Value.GetComponent<Image>().color = item.Key == currentPage
                ? new Color(0.55f, 0.36f, 0.13f, 1f)
                : new Color(0.20f, 0.17f, 0.13f, 1f);
        }
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }

    private void Close()
    {
        if (UIManager.Instance != null) UIManager.Instance.ClosePanel(panel.gameObject);
        else CloseInternal();
    }

    private void CloseInternal()
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
