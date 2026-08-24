using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>弟子详细面板：基础信息、能力与关系、人生履历三个独立页面。</summary>
public class NPCInfoPanel : MonoBehaviour
{
    private enum DetailPage { Basic, Abilities, History }

    // 保留旧场景字段，避免破坏 SampleScene 的序列化引用。
    public TMP_Text nameText;
    public TMP_Text stateText;

    [Header("Character details")]
    public TMP_Text realmText;
    public TMP_Text cultivationText;
    public TMP_Text healthText;
    public TMP_Text traitsText;
    public TMP_Text experienceText;
    public TMP_Text relationshipsText;
    public TMP_Text historyText;

    private readonly Dictionary<DetailPage, Button> pageButtons = new Dictionary<DetailPage, Button>();
    private NPCRuntime currentNPC;
    private DetailPage currentPage;
    private RectTransform runtimeRoot;
    private RectTransform pageContent;
    private TMP_Text runtimeNameText;
    private TMP_Text runtimeStateText;

    public void Show(NPCRuntime npc)
    {
        currentNPC = npc;
        if (npc == null) return;
        EnsureRuntimeLayout();
        runtimeNameText.text = npc.Character.displayName;
        runtimeStateText.text = $"当前状态：{FormatNpcState(npc.State)}";
        currentPage = DetailPage.Basic;
        RefreshPageTabs();
        RefreshPage();
    }

    public void Hide()
    {
        if (UIManager.Instance != null) UIManager.Instance.ClosePanel(gameObject);
        else gameObject.SetActive(false);
    }

    private void EnsureRuntimeLayout()
    {
        if (runtimeRoot != null) return;
        NormalizeOwnerRect();
        DisableLegacyTexts();

        GameObject root = new GameObject("DiscipleDetailRuntime",
            typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        root.transform.SetParent(transform, false);
        runtimeRoot = root.GetComponent<RectTransform>();
        runtimeRoot.anchorMin = Vector2.zero;
        runtimeRoot.anchorMax = Vector2.one;
        runtimeRoot.offsetMin = new Vector2(14f, 14f);
        runtimeRoot.offsetMax = new Vector2(-14f, -14f);
        root.GetComponent<Image>().color = new Color(0.075f, 0.065f, 0.055f, 0.985f);
        VerticalLayoutGroup layout = root.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 18, 18);
        layout.spacing = 9f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        runtimeNameText = RuntimeUIFactory.Text(runtimeRoot, string.Empty, 26, 40);
        runtimeNameText.alignment = TextAlignmentOptions.Center;
        runtimeNameText.fontStyle = FontStyles.Bold;
        runtimeStateText = RuntimeUIFactory.Text(runtimeRoot, string.Empty, 17, 30);
        runtimeStateText.alignment = TextAlignmentOptions.Center;
        runtimeStateText.color = new Color(0.82f, 0.78f, 0.68f);

        RectTransform tabs = RuntimeUIFactory.TabBar(runtimeRoot, "DiscipleDetailTabs", 42);
        AddPageTab(tabs, DetailPage.Basic, "基础信息");
        AddPageTab(tabs, DetailPage.Abilities, "能力与关系");
        AddPageTab(tabs, DetailPage.History, "人生履历");

        pageContent = RuntimeUIFactory.ScrollContent(runtimeRoot, "DiscipleDetailScroll");
        Button close = RuntimeUIFactory.Button(runtimeRoot, "关闭", 42);
        close.onClick.AddListener(Hide);
    }

    private void NormalizeOwnerRect()
    {
        RectTransform owner = transform as RectTransform;
        if (owner == null) return;
        // SampleScene 的旧面板为适配旧字段曾设置 localScale=0.5，
        // 会让运行时生成的 TMP 字体也以半像素尺寸渲染并发虚。
        owner.localScale = Vector3.one;
        owner.anchorMin = new Vector2(0.16f, 0.12f);
        owner.anchorMax = new Vector2(0.84f, 0.88f);
        owner.anchoredPosition = Vector2.zero;
        owner.sizeDelta = Vector2.zero;
    }

    private void AddPageTab(Transform tabs, DetailPage page, string label)
    {
        Button button = RuntimeUIFactory.TabButton(tabs, label, page == currentPage, 40);
        pageButtons[page] = button;
        button.onClick.AddListener(() =>
        {
            if (currentPage == page) return;
            currentPage = page;
            RefreshPageTabs();
            RefreshPage();
        });
    }

    private void RefreshPageTabs()
    {
        foreach (KeyValuePair<DetailPage, Button> item in pageButtons)
            item.Value.GetComponent<Image>().color = item.Key == currentPage
                ? new Color(0.55f, 0.36f, 0.13f, 1f)
                : new Color(0.20f, 0.17f, 0.13f, 1f);
    }

    private void RefreshPage()
    {
        if (pageContent == null || currentNPC == null) return;
        ClearChildren(pageContent);
        if (currentPage == DetailPage.Basic) ShowBasicPage();
        else if (currentPage == DetailPage.Abilities) ShowAbilitiesPage();
        else ShowHistoryPage();
    }

    private void ShowBasicPage()
    {
        AddSection("修行状态");
        AddRow("境界", $"{FormatRealm(currentNPC.Realm)} {currentNPC.RealmLayer} 层");
        AddRow("当前灵气", $"{currentNPC.CurrentAura:0.0} / {DailyCultivationSimulator.AuraCapacity(currentNPC):0.0}");
        AddRow("纳气进度", $"{currentNPC.Character.naqiProgress:0.0}%");
        AddRow("灵气控制", $"{currentNPC.Character.auraControl:0.0}");
        AddRow("疲劳", $"{currentNPC.Character.fatigue:0.0}");
        FoundingState founding = PlayerManager.Instance?.playerData?.founding;
        FoundingTechniqueDefinition technique = FoundingRules.GetTechnique(founding?.selectedTechniqueId);
        AddRow("宗门传承", technique == null ? "未选择" : technique.name);
        AddRow("传承掌握", $"{currentNPC.Character.techniqueMastery:0.0}%");
        AddRow("心境", $"{currentNPC.MentalState} / {DiscipleMentalStateRules.MaxMentalState}");
        AddRow("健康", FormatHealth(currentNPC.Health));
        AddSection("弟子概况");
        AddRow("年龄", currentNPC.Character.age.ToString());
        AddRow("灵根", FoundingRules.SpiritRootName(currentNPC.SpiritRootQuality));
        AddRow("战力", currentNPC.CombatPower.ToString());
    }

    private void ShowAbilitiesPage()
    {
        AddSection("基础能力");
        AddRow("力量", currentNPC.Attack.ToString());
        AddRow("敏捷", currentNPC.Agility.ToString());
        AddRow("根骨", currentNPC.Physique.ToString());
        AddRow("悟性", currentNPC.Comprehension.ToString());
        AddRow("战斗悟性", currentNPC.CombatComprehension.ToString());
        AddRow("战斗经验", currentNPC.CombatExperience.ToString());

        SplitTraitNames(currentNPC.Character.traitIds, out string personality, out string experiences);
        AddSection("性格与经历特质");
        AddWrappedRow("性格", personality);
        FoundingFeatureDefinition feature = FoundingRules.GetFeature(currentNPC.Character.initialFeatureId);
        string featureText = feature == null
            ? Empty(currentNPC.Character.initialFeatureId)
            : $"{feature.name}：{feature.description}";
        AddWrappedRow("初始特点", featureText);
        AddWrappedRow("经历特质", experiences);
        AddSection("人际关系");
        AddDynamicText(FormatRelationships(currentNPC.Character.relationships), 38);
    }

    private void ShowHistoryPage()
    {
        List<LifeRecord> records = currentNPC.Character.lifeRecords ?? new List<LifeRecord>();
        if (records.Count == 0)
        {
            AddDynamicText("暂无人生履历。", 44);
            return;
        }

        foreach (LifeRecord record in records.Where(item => item != null)
                     .Select((item, index) => new { item, index })
                     .OrderByDescending(pair => pair.item.day)
                     .ThenByDescending(pair => pair.index)
                     .Select(pair => pair.item))
        {
            TMP_Text text = AddDynamicText(
                $"第 {record.day} 天 · {FormatLifeRecordCategory(record.category)}\n{record.text}", 52);
            text.color = new Color(0.92f, 0.89f, 0.82f);
        }
    }

    private void AddSection(string title)
    {
        TMP_Text text = RuntimeUIFactory.Text(pageContent, title, 19, 34);
        text.fontStyle = FontStyles.Bold;
        text.color = new Color(0.92f, 0.72f, 0.34f);
    }

    private void AddRow(string label, string value) => AddWrappedRow(label, value, 36);

    private void AddWrappedRow(string label, string value, float minHeight = 44)
    {
        GameObject row = new GameObject("DetailRow", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(pageContent, false);
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        row.GetComponent<LayoutElement>().minHeight = minHeight;

        TMP_Text labelText = RuntimeUIFactory.Text(row.transform, label, 16, minHeight);
        LayoutElement labelLayout = labelText.GetComponent<LayoutElement>();
        labelLayout.minWidth = 112f;
        labelLayout.preferredWidth = 112f;
        labelLayout.flexibleWidth = 0f;
        labelText.color = new Color(0.72f, 0.68f, 0.60f);

        TMP_Text valueText = RuntimeUIFactory.Text(row.transform, Empty(value), 16, minHeight);
        LayoutElement valueLayout = valueText.GetComponent<LayoutElement>();
        valueLayout.preferredWidth = -1f;
        valueLayout.flexibleWidth = 1f;
        valueText.enableWordWrapping = true;
        valueText.overflowMode = TextOverflowModes.Overflow;
    }

    private TMP_Text AddDynamicText(string value, float minHeight)
    {
        TMP_Text text = RuntimeUIFactory.Text(pageContent, Empty(value), 16, minHeight);
        LayoutElement layout = text.GetComponent<LayoutElement>();
        layout.minHeight = minHeight;
        layout.preferredHeight = -1f;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    private void DisableLegacyTexts()
    {
        foreach (TMP_Text text in new[]
                 {
                     nameText, stateText, realmText, cultivationText, healthText,
                     traitsText, experienceText, relationshipsText, historyText
                 }.Where(item => item != null).Distinct())
            text.gameObject.SetActive(false);
    }

    private static void ClearChildren(Transform parent)
    {
        for (int index = parent.childCount - 1; index >= 0; index--)
        {
            GameObject child = parent.GetChild(index).gameObject;
            if (Application.isPlaying) Object.Destroy(child);
            else Object.DestroyImmediate(child);
        }
    }

    private static string Empty(string value) => string.IsNullOrWhiteSpace(value) ? "无" : value;

    private static void SplitTraitNames(List<string> traitIds, out string personality, out string experience)
    {
        List<string> personalityNames = new List<string>();
        List<string> experienceNames = new List<string>();
        foreach (string traitId in traitIds ?? new List<string>())
        {
            TraitDefinition definition = TraitDatabase.Instance == null ? null : TraitDatabase.Instance.Get(traitId);
            string displayName = definition == null || string.IsNullOrWhiteSpace(definition.displayName)
                ? traitId
                : definition.displayName;
            if (definition != null && definition.isExperience) experienceNames.Add(displayName);
            else personalityNames.Add(displayName);
        }
        personality = personalityNames.Count == 0 ? "无" : string.Join("、", personalityNames);
        experience = experienceNames.Count == 0 ? "无" : string.Join("、", experienceNames);
    }

    private static string FormatRelationships(List<RelationshipRecord> relationships)
    {
        if (relationships == null || relationships.Count == 0) return "暂无人际关系。";
        return string.Join("\n", relationships.Where(item => item != null).Select(relationship =>
        {
            NPCRuntime target = NPCManager.Instance == null
                ? null
                : NPCManager.Instance.GetRuntime(relationship.targetCharacterId);
            string targetName = target == null ? relationship.targetCharacterId : target.Character.displayName;
            return $"{FormatRelationshipTag(relationship.tag)}：{targetName}";
        }));
    }

    private static string FormatLifeRecordCategory(string category)
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

    private static string FormatRelationshipTag(RelationshipTag tag)
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

    private static string FormatRealm(CultivationRealm realm)
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

    private static string FormatHealth(HealthState health)
    {
        switch (health)
        {
            case HealthState.Healthy: return "健康";
            case HealthState.LightInjury: return "轻伤";
            case HealthState.SeriousInjury: return "重伤";
            case HealthState.PermanentTrauma: return "永久创伤";
            case HealthState.Dead: return "死亡";
            default: return health.ToString();
        }
    }

    private static string FormatNpcState(NPCState state)
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
}
