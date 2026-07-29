using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class UIPaginationTests
{
    private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic;

    [SetUp]
    public void SetUp()
    {
        PlayerManager.Instance = null;
        NPCManager.Instance = null;
        MissionManager.Instance = null;
        ExternalThreatPanel.Instance = null;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (UnityEngine.Object item in objects)
            if (item != null) UnityEngine.Object.DestroyImmediate(item);
        objects.Clear();
        PlayerManager.Instance = null;
        NPCManager.Instance = null;
        MissionManager.Instance = null;
        ExternalThreatPanel.Instance = null;
    }

    [Test]
    public void MissionPageClassification_MapsEveryApprovedCategory()
    {
        AssertMissionPage(new MissionData { foundingAction = FoundingActionKind.RepairFacility }, "Repair");
        AssertMissionPage(new MissionData { foundingAction = FoundingActionKind.LaborGather }, "Labor");
        AssertMissionPage(new MissionData { foundingAction = FoundingActionKind.LaborBuild }, "Labor");
        AssertMissionPage(new MissionData { foundingAction = FoundingActionKind.LaborCultivate }, "Labor");
        AssertMissionPage(new MissionData { foundingAction = FoundingActionKind.VillagePreach }, "VillageThreat");
        AssertMissionPage(new MissionData { foundingAction = FoundingActionKind.VillageHelp }, "VillageThreat");
        AssertMissionPage(new MissionData { threatMissionKind = ThreatMissionKind.Investigation }, "VillageThreat");
        AssertMissionPage(new MissionData { foundingAction = FoundingActionKind.BuildRouteFacility }, "Other");
        AssertMissionPage(new MissionData { foundingAction = FoundingActionKind.RouteAlchemy }, "Other");
        AssertMissionPage(new MissionData { isFacilityAction = true }, "Other");
        AssertMissionPage(new MissionData(), "Other");
    }

    [Test]
    public void TabBar_UsesTopAnchoredHorizontalStretch()
    {
        GameObject owner = Track(new GameObject("TabBarLayoutTest"));
        RectTransform tabs = RuntimeUIFactory.TabBar(owner.transform, "Tabs", 44);

        Assert.AreEqual(new Vector2(0f, 1f), tabs.anchorMin);
        Assert.AreEqual(new Vector2(1f, 1f), tabs.anchorMax);
        Assert.AreEqual(new Vector2(0.5f, 1f), tabs.pivot);
        Assert.AreEqual(new Vector2(0f, -44f), tabs.offsetMin);
        Assert.AreEqual(Vector2.zero, tabs.offsetMax);
        Assert.AreEqual(44f, tabs.rect.height, 0.01f);
    }

    [Test]
    public void NpcContentLayout_ControlsTabBarWidthAfterRebuild()
    {
        GameObject viewportObject = Track(new GameObject("NpcViewportLayoutTest", typeof(RectTransform)));
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        viewport.sizeDelta = new Vector2(320f, 300f);

        GameObject contentObject = Track(new GameObject("NpcContentLayoutTest", typeof(RectTransform)));
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0f, 1f);
        content.offsetMin = new Vector2(0f, -300f);
        content.offsetMax = Vector2.zero;
        ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = false;
        layout.childForceExpandWidth = true;
        layout.spacing = 10f;
        layout.padding = new RectOffset(0, 0, 0, 0);

        GameObject panelObject = Track(new GameObject("NpcPanelLayoutTest"));
        NPCSelectPanel panel = panelObject.AddComponent<NPCSelectPanel>();
        panel.content = content;
        typeof(NPCSelectPanel).GetMethod("ConfigureContentLayout", InstanceFlags)
            .Invoke(panel, null);
        RectTransform tabs = RuntimeUIFactory.TabBar(content, "NPCPageTabs", 38);
        Button npcButton = null;
        for (int i = 0; i < 8; i++)
            npcButton = RuntimeUIFactory.Button(content, $"模拟弟子{i + 1}", 30);

        LayoutRebuilder.ForceRebuildLayoutImmediate(viewport);
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        Assert.AreEqual(ContentSizeFitter.FitMode.Unconstrained, fitter.horizontalFit);
        Assert.AreEqual(ContentSizeFitter.FitMode.PreferredSize, fitter.verticalFit);
        Assert.IsTrue(layout.childControlWidth);
        Assert.That(content.rect.width, Is.EqualTo(viewport.rect.width).Within(0.1f));
        Assert.Greater(tabs.rect.width, 0f);
        Assert.That(tabs.rect.width, Is.EqualTo(content.rect.width).Within(0.1f));
        Assert.That(npcButton.GetComponent<RectTransform>().rect.width,
            Is.EqualTo(content.rect.width).Within(0.1f));
        Assert.That(content.rect.height, Is.GreaterThanOrEqualTo(38f + 8f * 30f + 8f * 10f));
    }

    [Test]
    public void FoundingCandidateFooter_IsOutsideScrollContentAndVisibleForCandidateStage()
    {
        GameObject owner = Track(new GameObject("FoundingFooterLayoutTest"));
        FoundingPanel founding = owner.AddComponent<FoundingPanel>();
        typeof(FoundingPanel).GetMethod("Awake", InstanceFlags).Invoke(founding, null);

        RectTransform panel = (RectTransform)typeof(FoundingPanel)
            .GetField("panel", InstanceFlags).GetValue(founding);
        RectTransform content = (RectTransform)typeof(FoundingPanel)
            .GetField("content", InstanceFlags).GetValue(founding);
        RectTransform footer = (RectTransform)typeof(FoundingPanel)
            .GetField("candidateFooter", InstanceFlags).GetValue(founding);
        Assert.AreSame(panel, footer.parent);
        Assert.AreNotSame(content, footer.parent);

        FoundingState state = new FoundingState
        {
            stage = FoundingStage.CandidateSelection,
            candidates = FoundingRules.GenerateCandidates(42)
        };
        typeof(FoundingPanel).GetMethod("ShowCandidates", InstanceFlags)
            .Invoke(founding, new object[] { state });

        RectTransform tabs = (RectTransform)typeof(FoundingPanel)
            .GetField("candidateTabs", InstanceFlags).GetValue(founding);
        Button confirm = (Button)typeof(FoundingPanel)
            .GetField("candidateConfirmButton", InstanceFlags).GetValue(founding);
        Assert.IsTrue(footer.gameObject.activeSelf);
        Assert.IsTrue(tabs.gameObject.activeSelf);
        Assert.AreSame(content, tabs.parent);
        Assert.IsNotNull(confirm);
        Assert.GreaterOrEqual(footer.childCount, 2);
    }

    [TestCase(0, 1)]
    [TestCase(8, 1)]
    [TestCase(9, 2)]
    [TestCase(16, 2)]
    [TestCase(17, 3)]
    public void NpcPagination_UsesAtMostEightEntriesPerPage(int npcCount, int expectedPages)
    {
        MethodInfo count = typeof(NPCSelectPanel).GetMethod("CalculatePageCount", StaticFlags);
        MethodInfo start = typeof(NPCSelectPanel).GetMethod("CalculatePageStart", StaticFlags);
        Assert.AreEqual(expectedPages, count.Invoke(null, new object[] { npcCount }));
        Assert.AreEqual(0, start.Invoke(null, new object[] { 0 }));
        Assert.AreEqual(8, start.Invoke(null, new object[] { 1 }));
        Assert.AreEqual(16, start.Invoke(null, new object[] { 2 }));
    }

    [Test]
    public void FoundingCandidateTab_KeepsSelectionsAcrossPages()
    {
        GameObject owner = Track(new GameObject("FoundingPaginationTest"));
        FoundingPanel founding = owner.AddComponent<FoundingPanel>();
        typeof(FoundingPanel).GetMethod("Awake", InstanceFlags).Invoke(founding, null);
        HashSet<string> selected = (HashSet<string>)typeof(FoundingPanel)
            .GetField("selectedCandidateIds", InstanceFlags).GetValue(founding);
        selected.Add("candidate-a");

        RectTransform tabs = RuntimeUIFactory.TabBar(owner.transform, "TestTabs");
        typeof(FoundingPanel).GetMethod("AddCandidateTab", InstanceFlags)
            .Invoke(founding, new object[] { tabs, "后五名", 1 });
        tabs.GetChild(0).GetComponent<Button>().onClick.Invoke();

        Assert.AreEqual(1, typeof(FoundingPanel).GetField("candidatePage", InstanceFlags).GetValue(founding));
        CollectionAssert.Contains(selected, "candidate-a");
    }

    [Test]
    public void ThreatTab_KeepsParticipantsAndPlanAcrossPages()
    {
        GameObject owner = Track(new GameObject("ThreatPaginationTest"));
        ExternalThreatPanel panel = owner.AddComponent<ExternalThreatPanel>();
        typeof(ExternalThreatPanel).GetMethod("Awake", InstanceFlags).Invoke(panel, null);
        List<string> selected = (List<string>)typeof(ExternalThreatPanel)
            .GetField("selectedIds", InstanceFlags).GetValue(panel);
        selected.Add("npc-a");
        typeof(ExternalThreatPanel).GetField("selectedPlan", InstanceFlags)
            .SetValue(panel, CombatPlanType.SimpleDefense);

        IDictionary buttons = (IDictionary)typeof(ExternalThreatPanel)
            .GetField("pageButtons", InstanceFlags).GetValue(panel);
        object participantsKey = Enum.Parse(buttons.GetType().GetGenericArguments()[0], "Participants");
        ((Button)buttons[participantsKey]).onClick.Invoke();

        CollectionAssert.Contains(selected, "npc-a");
        Assert.AreEqual(CombatPlanType.SimpleDefense,
            typeof(ExternalThreatPanel).GetField("selectedPlan", InstanceFlags).GetValue(panel));
    }

    [Test]
    public void ExplorationPanel_UsesThreePagesAndKeepsSelectionState()
    {
        GameObject owner = Track(new GameObject("ExplorationPaginationTest"));
        ExplorationPanel panel = owner.AddComponent<ExplorationPanel>();
        typeof(ExplorationPanel).GetMethod("Awake", InstanceFlags).Invoke(panel, null);

        IDictionary buttons = (IDictionary)typeof(ExplorationPanel)
            .GetField("pageButtons", InstanceFlags).GetValue(panel);
        Assert.AreEqual(3, buttons.Count);

        typeof(ExplorationPanel).GetField("selectedRegionId", InstanceFlags).SetValue(panel, "region-a");
        typeof(ExplorationPanel).GetField("pendingMissionId", InstanceFlags).SetValue(panel, "mission-a");
        object detailsKey = Enum.Parse(buttons.GetType().GetGenericArguments()[0], "Details");
        ((Button)buttons[detailsKey]).onClick.Invoke();

        Assert.AreEqual("region-a",
            typeof(ExplorationPanel).GetField("selectedRegionId", InstanceFlags).GetValue(panel));
        Assert.AreEqual("mission-a",
            typeof(ExplorationPanel).GetField("pendingMissionId", InstanceFlags).GetValue(panel));
        RectTransform root = (RectTransform)typeof(ExplorationPanel)
            .GetField("panel", InstanceFlags).GetValue(panel);
        Button close = (Button)typeof(ExplorationPanel)
            .GetField("closeButton", InstanceFlags).GetValue(panel);
        Assert.AreSame(root, close.transform.parent);
    }

    [Test]
    public void DaySettlementPanel_UsesFourPagesAndKeepsSummary()
    {
        GameObject owner = Track(new GameObject("SettlementPaginationTest"));
        DaySettlementPanel panel = owner.AddComponent<DaySettlementPanel>();
        typeof(DaySettlementPanel).GetMethod("Awake", InstanceFlags).Invoke(panel, null);
        DaySettlementSummary summary = new DaySettlementSummary { day = 12 };
        typeof(DaySettlementPanel).GetMethod("Show", InstanceFlags)
            .Invoke(panel, new object[] { summary });

        IDictionary buttons = (IDictionary)typeof(DaySettlementPanel)
            .GetField("pageButtons", InstanceFlags).GetValue(panel);
        Assert.AreEqual(4, buttons.Count);
        object charactersKey = Enum.Parse(buttons.GetType().GetGenericArguments()[0], "Characters");
        ((Button)buttons[charactersKey]).onClick.Invoke();

        Assert.AreSame(summary,
            typeof(DaySettlementPanel).GetField("currentSummary", InstanceFlags).GetValue(panel));
        Assert.AreEqual("Characters",
            typeof(DaySettlementPanel).GetField("currentPage", InstanceFlags).GetValue(panel).ToString());
        RectTransform root = (RectTransform)typeof(DaySettlementPanel)
            .GetField("panel", InstanceFlags).GetValue(panel);
        Button confirm = (Button)typeof(DaySettlementPanel)
            .GetField("confirmButton", InstanceFlags).GetValue(panel);
        Assert.AreSame(root, confirm.transform.parent);
    }

    [Test]
    public void CharacterEventPanel_ScrollsBodySeparatelyFromOptions()
    {
        GameObject owner = Track(new GameObject("CharacterEventLayoutTest"));
        CharacterEventPanel panel = owner.AddComponent<CharacterEventPanel>();
        typeof(CharacterEventPanel).GetMethod("Awake", InstanceFlags).Invoke(panel, null);

        RectTransform root = (RectTransform)typeof(CharacterEventPanel)
            .GetField("panel", InstanceFlags).GetValue(panel);
        RectTransform bodyContent = (RectTransform)typeof(CharacterEventPanel)
            .GetField("bodyContent", InstanceFlags).GetValue(panel);
        RectTransform options = (RectTransform)typeof(CharacterEventPanel)
            .GetField("optionsContainer", InstanceFlags).GetValue(panel);
        Assert.AreSame(root, options.parent);
        Assert.IsFalse(options.IsChildOf(bodyContent));
        Assert.IsNotNull(bodyContent.parent.parent.GetComponent<ScrollRect>());

        MethodInfo createButton = typeof(CharacterEventPanel).GetMethod("CreateButton", StaticFlags);
        Button option = (Button)createButton.Invoke(null, new object[] { options, "测试选项" });
        Assert.AreSame(options, option.transform.parent);

        RectTransform scrollRoot = bodyContent.parent.parent as RectTransform;
        Assert.IsNotNull(scrollRoot);
        Assert.AreEqual(1f, scrollRoot.GetComponent<LayoutElement>().flexibleHeight);
    }

    private static void AssertMissionPage(MissionData data, string expected)
    {
        object page = typeof(MissionPanel).GetMethod("GetMissionPage", StaticFlags)
            .Invoke(null, new object[] { data });
        Assert.AreEqual(expected, page.ToString());
    }

    private T Track<T>(T item) where T : UnityEngine.Object
    {
        objects.Add(item);
        return item;
    }
}
