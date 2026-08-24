using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class DiscipleCenterView : UIWindowView
{
    [SerializeField] private RectTransform listContent;
    [SerializeField] private DiscipleListItemView listItemPrefab;
    [SerializeField] private TMP_Text emptyText;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text realmText;
    [SerializeField] private TMP_Text ageText;
    [SerializeField] private TMP_Text stateText;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private Image naqiFill;
    [SerializeField] private TMP_Text naqiText;
    [SerializeField] private Image masteryFill;
    [SerializeField] private TMP_Text masteryText;
    [SerializeField] private TMP_Text mentalText;
    [SerializeField] private TMP_Text dailyAuraText;
    [SerializeField] private TMP_Text abilitiesText;
    [SerializeField] private TMP_Text relationshipsText;
    [SerializeField] private RectTransform historyContent;
    [SerializeField] private DiscipleHistoryItemView historyItemPrefab;
    [SerializeField] private TMP_Text historyEmptyText;
    [SerializeField] private TMP_Text currentActionText;
    [SerializeField] private TMP_Text currentPlanText;
    [SerializeField] private TMP_Text nextPlanText;
    [SerializeField] private Button[] tabButtons;
    [SerializeField] private GameObject[] tabPages;

    private readonly List<DiscipleListItemView> items = new List<DiscipleListItemView>();
    private readonly List<DiscipleHistoryItemView> historyItems = new List<DiscipleHistoryItemView>();
    private DiscipleCenterPresenter presenter;
    private int currentTab;

    public void Configure(RectTransform newListContent, DiscipleListItemView newListItemPrefab,
        TMP_Text newEmptyText, TMP_Text newNameText, TMP_Text newRealmText, TMP_Text newAgeText,
        TMP_Text newStateText, TMP_Text newHealthText, Image newNaqiFill, TMP_Text newNaqiText,
        Image newMasteryFill, TMP_Text newMasteryText, TMP_Text newMentalText,
        TMP_Text newDailyAuraText, TMP_Text newAbilitiesText, TMP_Text newRelationshipsText,
        RectTransform newHistoryContent,
        DiscipleHistoryItemView newHistoryItemPrefab, TMP_Text newHistoryEmptyText,
        TMP_Text newCurrentActionText, TMP_Text newCurrentPlanText, TMP_Text newNextPlanText,
        Button[] newTabButtons, GameObject[] newTabPages)
    {
        listContent = newListContent;
        listItemPrefab = newListItemPrefab;
        emptyText = newEmptyText;
        nameText = newNameText;
        realmText = newRealmText;
        ageText = newAgeText;
        stateText = newStateText;
        healthText = newHealthText;
        naqiFill = newNaqiFill;
        naqiText = newNaqiText;
        masteryFill = newMasteryFill;
        masteryText = newMasteryText;
        mentalText = newMentalText;
        dailyAuraText = newDailyAuraText;
        abilitiesText = newAbilitiesText;
        relationshipsText = newRelationshipsText;
        historyContent = newHistoryContent;
        historyItemPrefab = newHistoryItemPrefab;
        historyEmptyText = newHistoryEmptyText;
        currentActionText = newCurrentActionText;
        currentPlanText = newCurrentPlanText;
        nextPlanText = newNextPlanText;
        tabButtons = newTabButtons;
        tabPages = newTabPages;
    }

    private void Awake()
    {
        presenter = new DiscipleCenterPresenter(this);
        for (int index = 0; index < (tabButtons?.Length ?? 0); index++)
        {
            int captured = index;
            tabButtons[index].onClick.AddListener(() => SelectTab(captured));
        }
    }

    public override void OnOpened(IUIWindowContext context)
    {
        presenter.Open((context as DiscipleCenterContext)?.CharacterId);
        SelectTab(currentTab);
    }

    public override void OnClosed()
    {
        presenter.Close();
    }

    public void Render(DiscipleCenterSnapshot snapshot)
    {
        ClearList();
        foreach (DiscipleListItemSnapshot item in snapshot.disciples)
        {
            DiscipleListItemView row = Instantiate(listItemPrefab, listContent);
            row.Bind(item, item.characterId == snapshot.selectedCharacterId,
                () => presenter.Select(item.characterId));
            items.Add(row);
        }

        bool hasSelection = snapshot.HasSelection;
        if (emptyText != null) emptyText.gameObject.SetActive(!hasSelection);
        ConfigureSingleLine(nameText);
        ConfigureSingleLine(realmText);
        ConfigureSingleLine(ageText);
        ConfigureSingleLine(stateText);
        ConfigureSingleLine(healthText);
        Set(nameText, hasSelection ? snapshot.name : "暂无弟子");
        Set(realmText, hasSelection ? $"境界：{snapshot.realm}" : string.Empty);
        Set(ageText, hasSelection ? $"年龄：{snapshot.age}" : string.Empty);
        SetTag(stateText, hasSelection ? snapshot.state : "无", StateColor(snapshot.state));
        SetTag(healthText, hasSelection ? snapshot.health : "无", HealthColor(snapshot.health));
        SetProgress(naqiFill, naqiText, snapshot.naqiProgress);
        SetProgress(masteryFill, masteryText, snapshot.techniqueMastery);
        Set(mentalText, hasSelection
            ? $"心境：{snapshot.mentalState} / {DiscipleMentalStateRules.MaxMentalState}"
            : string.Empty);
        Set(dailyAuraText, hasSelection ? $"当前灵气：{snapshot.currentAura:0.0} / {snapshot.auraCapacity:0.0}　控制 {snapshot.auraControl:0.0}　疲劳 {snapshot.fatigue:0.0}" : string.Empty);
        Set(abilitiesText, snapshot.abilities);
        Set(relationshipsText, snapshot.relationships);
        RenderHistory(snapshot.historyItems);
        Set(currentActionText, hasSelection ? snapshot.currentAction : "暂无当前行动");
        Set(currentPlanText, hasSelection ? snapshot.currentPlan : "暂无本月计划");
        Set(nextPlanText, hasSelection ? snapshot.nextPlan : "暂无明日安排");
    }

    private void SelectTab(int index)
    {
        if (tabPages == null || tabPages.Length == 0) return;
        currentTab = Mathf.Clamp(index, 0, tabPages.Length - 1);
        for (int pageIndex = 0; pageIndex < tabPages.Length; pageIndex++)
        {
            if (tabPages[pageIndex] != null) tabPages[pageIndex].SetActive(pageIndex == currentTab);
            if (tabButtons != null && pageIndex < tabButtons.Length && tabButtons[pageIndex] != null)
            {
                Image image = tabButtons[pageIndex].GetComponent<Image>();
                if (image != null) image.color = pageIndex == currentTab
                    ? new Color(0.43f, 0.34f, 0.15f, 1f)
                    : new Color(0.095f, 0.14f, 0.11f, 1f);
            }
        }
    }

    private void ClearList()
    {
        foreach (DiscipleListItemView item in items)
            if (item != null) Destroy(item.gameObject);
        items.Clear();
    }

    private void RenderHistory(IReadOnlyList<DiscipleHistoryItemSnapshot> snapshots)
    {
        foreach (DiscipleHistoryItemView item in historyItems)
            if (item != null) Destroy(item.gameObject);
        historyItems.Clear();
        bool empty = snapshots == null || snapshots.Count == 0;
        if (historyEmptyText != null) historyEmptyText.gameObject.SetActive(empty);
        if (empty || historyItemPrefab == null || historyContent == null) return;
        foreach (DiscipleHistoryItemSnapshot snapshot in snapshots)
        {
            DiscipleHistoryItemView row = Instantiate(historyItemPrefab, historyContent);
            row.Bind(snapshot);
            historyItems.Add(row);
        }
    }

    private static void SetProgress(Image fill, TMP_Text value, float progress)
    {
        float normalized = Mathf.Clamp01(progress / 100f);
        if (fill != null) fill.rectTransform.anchorMax = new Vector2(normalized, 1f);
        if (value != null) value.text = $"{progress:0.0}%";
    }

    private static void SetTag(TMP_Text text, string value, Color background)
    {
        Set(text, value);
        Image image = text == null ? null : text.transform.parent.GetComponent<Image>();
        if (image != null) image.color = background;
    }

    private static Color StateColor(string state)
    {
        if (state == "空闲") return new Color(0.13f, 0.34f, 0.23f, 1f);
        if (state == "养伤") return new Color(0.45f, 0.16f, 0.12f, 1f);
        return new Color(0.42f, 0.32f, 0.10f, 1f);
    }

    private static Color HealthColor(string health) => health == "健康"
        ? new Color(0.12f, 0.28f, 0.20f, 1f)
        : new Color(0.42f, 0.18f, 0.13f, 1f);

    private static void Set(TMP_Text target, string value)
    {
        if (target != null) target.text = string.IsNullOrWhiteSpace(value) ? "无" : value;
    }

    private static void ConfigureSingleLine(TMP_Text text)
    {
        if (text == null) return;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
    }
}
