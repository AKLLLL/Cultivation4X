using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public sealed class GlobalUICoreTests
{
    private GameObject managerObject;
    private UIManager manager;

    [SetUp]
    public void SetUp()
    {
        UIManager.Instance = null;
        PlayerManager.Instance = null;
        NPCManager.Instance = null;
        managerObject = new GameObject("UIManagerTest");
        manager = managerObject.AddComponent<UIManager>();
        manager.Configure(managerObject.transform, managerObject.transform, managerObject.transform,
            null, null, Array.Empty<UIWindowRegistration>());
    }

    [TearDown]
    public void TearDown()
    {
        if (managerObject != null) UnityEngine.Object.DestroyImmediate(managerObject);
        foreach (GameObject item in Resources.FindObjectsOfTypeAll<GameObject>()
                     .Where(item => item != null && item.name.StartsWith("UITest_", StringComparison.Ordinal)))
            UnityEngine.Object.DestroyImmediate(item);
        UIManager.Instance = null;
        PlayerManager.Instance = null;
        NPCManager.Instance = null;
    }

    [Test]
    public void ScreenStack_HidesAndRestoresPreviousScreen()
    {
        GameObject first = new GameObject("UITest_宗门管理", typeof(RectTransform));
        GameObject second = new GameObject("UITest_弟子中心", typeof(RectTransform));

        manager.OpenScreen(first);
        manager.OpenScreen(second);

        Assert.That(first.activeSelf, Is.False);
        Assert.That(second.activeSelf, Is.True);
        Assert.That(manager.HasOpenScreens, Is.True);

        manager.CloseTopWindow();
        Assert.That(first.activeSelf, Is.True);
        Assert.That(second.activeSelf, Is.False);

        manager.ReturnToWorldMap();
        Assert.That(first.activeSelf, Is.False);
        Assert.That(manager.HasOpenScreens, Is.False);
    }

    [Test]
    public void BlockedModal_IgnoresEscapeClose_ButForceCloseWorks()
    {
        GameObject modal = new GameObject("UITest_关键事件", typeof(RectTransform));
        manager.OpenPanel(modal, null, UIEscapePolicy.Blocked);

        manager.CloseTopWindow();
        Assert.That(modal.activeSelf, Is.True);
        Assert.That(manager.HasOpenModals, Is.True);

        manager.CloseAllPanels();
        Assert.That(modal.activeSelf, Is.False);
        Assert.That(manager.HasOpenModals, Is.False);
    }

    [Test]
    public void CloseCallback_IsInvokedExactlyOnce()
    {
        GameObject modal = new GameObject("UITest_日结", typeof(RectTransform));
        int count = 0;
        manager.OpenPanel(modal, () => count++);

        manager.ClosePanel(modal);
        manager.ClosePanel(modal);

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void GlobalHud_InitialCharacterSetup_DoesNotCloseMandatoryFoundingPanel()
    {
        GameObject modal = new GameObject("UITest_立宗面板", typeof(RectTransform));
        manager.OpenPanel(modal);
        GameObject viewObject = new GameObject("UITest_GlobalHud");
        GlobalHudView view = viewObject.AddComponent<GlobalHudView>();
        typeof(GlobalHudView).GetField("manager",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(view, manager);
        System.Reflection.MethodInfo apply = typeof(GlobalHudView).GetMethod("ApplyFlowState",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        apply?.Invoke(view, new object[] { GameFlowState.CharacterSetup });
        Assert.That(modal.activeSelf, Is.True, "首次角色创建路由不能关闭刚打开的强制立宗面板");

        apply?.Invoke(view, new object[] { GameFlowState.WorldMap });
        apply?.Invoke(view, new object[] { GameFlowState.CharacterSetup });
        Assert.That(modal.activeSelf, Is.False, "从世界地图离开时仍应清理旧窗口");
    }

    [Test]
    public void Overlay_DoesNotBlockOrEnterEscapeStack()
    {
        GameObject overlayPrefab = new GameObject("UITest_OverlayPrefab", typeof(RectTransform));
        manager.Configure(managerObject.transform, managerObject.transform, managerObject.transform,
            null, null, new[]
            {
                new UIWindowRegistration
                {
                    id = UIWindowId.DiscipleCenter,
                    title = "提示",
                    layer = UIWindowLayer.Overlay,
                    escapePolicy = UIEscapePolicy.Allowed,
                    blocksWorldInput = false,
                    cacheInstance = true,
                    prefab = overlayPrefab
                }
            });

        manager.OpenWindow(UIWindowId.DiscipleCenter);
        GameObject instance = managerObject.transform.Cast<Transform>()
            .Select(child => child.gameObject)
            .First(item => item.name == overlayPrefab.name && item != overlayPrefab);

        Assert.That(manager.HasBlockingWindow, Is.False);
        manager.CloseTopWindow();
        Assert.That(instance.activeSelf, Is.True);
        manager.CloseAllPanels();
        Assert.That(instance.activeSelf, Is.False);
    }

    [Test]
    public void UIRootPrefab_HasOneManagerAndValidThemeAndWindowPrefab()
    {
        GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefab/UI/UIRoot.prefab");
        Assert.That(root, Is.Not.Null);
        Assert.That(root.GetComponentsInChildren<UIManager>(true).Length, Is.EqualTo(1));

        SerializedObject serialized = new SerializedObject(root.GetComponent<UIManager>());
        Assert.That(serialized.FindProperty("theme").objectReferenceValue, Is.Not.Null);
        SerializedProperty registrations = serialized.FindProperty("windowRegistrations");
        Assert.That(registrations.arraySize, Is.EqualTo(2));
        for (int index = 0; index < registrations.arraySize; index++)
            Assert.That(registrations.GetArrayElementAtIndex(index).FindPropertyRelative("prefab").objectReferenceValue,
                Is.Not.Null);
        Assert.That(Enumerable.Range(0, registrations.arraySize)
            .Select(index => registrations.GetArrayElementAtIndex(index).FindPropertyRelative("id").enumValueIndex)
            .Distinct().Count(), Is.EqualTo(registrations.arraySize));

        SerializedObject hud = new SerializedObject(root.GetComponentInChildren<GlobalHudView>(true));
        Assert.That(hud.FindProperty("settlementButton").objectReferenceValue, Is.Not.Null);
        Assert.That(hud.FindProperty("endDayButton").objectReferenceValue, Is.Not.Null);
        Assert.That(hud.FindProperty("speed1Button").objectReferenceValue, Is.Not.Null);
        Assert.That(hud.FindProperty("speed2Button").objectReferenceValue, Is.Not.Null);
        Assert.That(hud.FindProperty("speed4Button").objectReferenceValue, Is.Not.Null);
    }

    [TestCase(1920f, 1080f)]
    [TestCase(1280f, 720f)]
    public void WorldTimeHud_ControlsFitAtReferenceSizes(float width, float height)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefab/UI/UIRoot.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = $"UITest_WorldTimeHud_{width}x{height}";
        RectTransform canvas = FindChild(instance.transform, "GlobalHudCanvas").GetComponent<RectTransform>();
        canvas.anchorMin = canvas.anchorMax = new Vector2(0.5f, 0.5f);
        canvas.sizeDelta = new Vector2(width, height);
        RectTransform top = FindChild(instance.transform, "TopBar").GetComponent<RectTransform>();

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(top);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(top);

        RectTransform action = FindChild(top, "ActionGroup").GetComponent<RectTransform>();
        Button[] controls = action.GetComponentsInChildren<Button>(true);
        Assert.That(controls.Length, Is.EqualTo(5));
        foreach (Button control in controls)
        {
            Assert.That(control.GetComponent<RectTransform>().rect.width, Is.GreaterThanOrEqualTo(47.5f));
            Assert.That(control.GetComponentInChildren<TMP_Text>(true).text, Is.Not.Empty);
        }
        Bounds actionBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(top, action);
        Assert.That(actionBounds.max.x, Is.LessThanOrEqualTo(top.rect.xMax + 0.5f));
        Assert.That(actionBounds.min.x, Is.GreaterThanOrEqualTo(top.rect.xMin - 0.5f));
        TMP_Text day = FindChild(top, "ProgressGroup").GetComponentsInChildren<TMP_Text>(true)
            .First(text => text.text.Contains("年"));
        Assert.That(day.rectTransform.rect.width, Is.GreaterThanOrEqualTo(157.5f));
    }

    [Test]
    public void DiscipleCenterPrefab_HasListItemReferenceAndRendersThreeRows()
    {
        GameObject centerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/UI/Prefabs/Disciple/DiscipleCenter.prefab");
        GameObject listPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/UI/Prefabs/Disciple/DiscipleListItem.prefab");
        Assert.That(centerPrefab, Is.Not.Null);
        Assert.That(listPrefab, Is.Not.Null);
        Assert.That(listPrefab.GetComponent<DiscipleListItemView>(), Is.Not.Null);

        DiscipleCenterView prefabView = centerPrefab.GetComponent<DiscipleCenterView>();
        SerializedObject prefabSerialized = new SerializedObject(prefabView);
        Assert.That(prefabSerialized.FindProperty("listItemPrefab").objectReferenceValue, Is.Not.Null);

        GameObject instance = UnityEngine.Object.Instantiate(centerPrefab);
        instance.name = "UITest_DiscipleCenter";
        DiscipleCenterView view = instance.GetComponent<DiscipleCenterView>();
        SerializedObject serialized = new SerializedObject(view);
        RectTransform content = serialized.FindProperty("listContent").objectReferenceValue as RectTransform;
        DiscipleCenterSnapshot snapshot = new DiscipleCenterSnapshot { selectedCharacterId = "disciple_2" };
        snapshot.mainTechniqueName = "青木长生诀";
        snapshot.techniqueStage = "初学";
        snapshot.techniqueUnderstanding = 14f;
        for (int index = 1; index <= 3; index++)
        {
            snapshot.disciples.Add(new DiscipleListItemSnapshot
            {
                characterId = $"disciple_{index}",
                name = $"弟子{index}",
                realm = "炼气1层",
                state = "空闲",
                naqiProgress = index * 10f
            });
        }

        view.Render(snapshot);

        Assert.That(content, Is.Not.Null);
        Assert.That(content.childCount, Is.EqualTo(3));
        TMP_Text masteryText = serialized.FindProperty("masteryText").objectReferenceValue as TMP_Text;
        Assert.That(masteryText.text, Is.EqualTo("初学 · 14.0%"));
        Assert.That(masteryText.GetComponent<LayoutElement>().preferredWidth, Is.EqualTo(100f));
        TMP_Text mentalText = serialized.FindProperty("mentalText").objectReferenceValue as TMP_Text;
        Assert.That(mentalText.text, Does.Contain("主修功法：青木长生诀"));
        Assert.That(mentalText.GetComponent<LayoutElement>().preferredHeight, Is.EqualTo(54f));
    }

    [Test]
    public void DiscipleCenterPresenter_SubscribesTimeRefreshOnlyWhileOpen()
    {
        TimeManager previousTime = TimeManager.Instance;
        GameObject timeObject = new GameObject("UITest_DiscipleCenterTime");
        GameObject viewObject = new GameObject("UITest_DiscipleCenterPresenter");
        DiscipleCenterPresenter presenter = null;
        try
        {
            TimeManager.Instance = null;
            TimeManager time = timeObject.AddComponent<TimeManager>();
            TimeManager.Instance = time;
            DiscipleCenterView view = viewObject.AddComponent<DiscipleCenterView>();
            presenter = new DiscipleCenterPresenter(view);

            presenter.Open(null);

            Assert.That(HasPresenterHandler(time, "OnDayPassed", presenter), Is.True);
            Assert.That(HasPresenterHandler(time, "OnDayStarted", presenter), Is.True);
            Assert.That(HasPresenterHandler(time, "OnHourChanged", presenter), Is.True);
            presenter.Close();
            Assert.That(HasPresenterHandler(time, "OnDayPassed", presenter), Is.False);
            Assert.That(HasPresenterHandler(time, "OnDayStarted", presenter), Is.False);
            Assert.That(HasPresenterHandler(time, "OnHourChanged", presenter), Is.False);
        }
        finally
        {
            presenter?.Close();
            TimeManager.Instance = previousTime;
            UnityEngine.Object.DestroyImmediate(viewObject);
            UnityEngine.Object.DestroyImmediate(timeObject);
        }
    }

    [Test]
    public void DisciplePrefabs_ReserveTextColumnsAndContainObservationText()
    {
        GameObject center = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/UI/Prefabs/Disciple/DiscipleCenter.prefab");
        GameObject listItem = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/UI/Prefabs/Disciple/DiscipleListItem.prefab");

        Transform topRow = FindChild(listItem.transform, "TopRow");
        HorizontalLayoutGroup topLayout = topRow.GetComponent<HorizontalLayoutGroup>();
        TMP_Text realm = listItem.GetComponentsInChildren<TMP_Text>(true)
            .First(text => text.text.Contains("炼气"));
        TMP_Text state = FindChild(topRow, "空闲Tag").GetComponentInChildren<TMP_Text>(true);
        Assert.That(topLayout.childForceExpandWidth, Is.False);
        Assert.That(realm.enableWordWrapping, Is.False);
        Assert.That(realm.GetComponent<LayoutElement>().minWidth, Is.GreaterThanOrEqualTo(100f));
        Assert.That(state.enableWordWrapping, Is.False);
        Assert.That(state.transform.parent.GetComponent<LayoutElement>().minWidth, Is.GreaterThan(0f));

        Transform characterCenter = FindChild(center.transform, "CharacterCenter");
        Transform tabs = FindChild(center.transform, "Tabs");
        Assert.That(characterCenter.GetChild(0), Is.EqualTo(tabs));
        Assert.That(FindChild(center.transform, "IdentityCard"), Is.Null);
        Assert.That(FindChild(center.transform, "PortraitSlot"), Is.Null);
        Assert.That(FindChild(center.transform, "IdentityRow"), Is.Not.Null);
        Assert.That(FindChild(center.transform, "AgeRow"), Is.Not.Null);
        SerializedObject centerView = new SerializedObject(center.GetComponent<DiscipleCenterView>());
        TMP_Text overviewName = centerView.FindProperty("nameText").objectReferenceValue as TMP_Text;
        SerializedProperty pages = centerView.FindProperty("tabPages");
        GameObject overviewPage = pages.GetArrayElementAtIndex(0).objectReferenceValue as GameObject;
        Assert.That(overviewName.transform.IsChildOf(overviewPage.transform), Is.True);
        for (int index = 1; index < pages.arraySize; index++)
        {
            GameObject otherPage = pages.GetArrayElementAtIndex(index).objectReferenceValue as GameObject;
            Assert.That(overviewName.transform.IsChildOf(otherPage.transform), Is.False);
        }
        HorizontalLayoutGroup tabLayout = tabs.GetComponent<HorizontalLayoutGroup>();
        LayoutElement tabBarSize = tabs.GetComponent<LayoutElement>();
        Assert.That(tabLayout.childForceExpandWidth, Is.False);
        Assert.That(tabLayout.childForceExpandHeight, Is.False);
        Assert.That(tabBarSize.flexibleHeight, Is.EqualTo(0f));
        foreach (Transform child in tabs)
        {
            LayoutElement buttonSize = child.GetComponent<LayoutElement>();
            Assert.That(buttonSize.preferredWidth, Is.InRange(88f, 96f));
            Assert.That(buttonSize.flexibleWidth, Is.EqualTo(0f));
            Assert.That(buttonSize.flexibleHeight, Is.EqualTo(0f));
        }

        Transform currentPlan = FindChild(center.transform, "循环计划Card");
        TMP_Text planText = currentPlan.GetComponentsInChildren<TMP_Text>(true).Last();
        Assert.That(currentPlan.GetComponent<LayoutElement>().preferredHeight, Is.GreaterThanOrEqualTo(180f));
        Assert.That(planText.overflowMode, Is.EqualTo(TextOverflowModes.Ellipsis));
    }

    [Test]
    public void MonthlyPlanPrefab_HasReferencesAndRendersThirtyDayCells()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/UI/Prefabs/MonthlyPlan/MonthlyPlan.prefab");
        Assert.That(prefab, Is.Not.Null);
        MonthlyPlanPanel prefabView = prefab.GetComponent<MonthlyPlanPanel>();
        Assert.That(prefabView, Is.Not.Null);
        SerializedObject prefabSerialized = new SerializedObject(prefabView);
        Assert.That(prefabSerialized.FindProperty("templateList").objectReferenceValue, Is.Not.Null);
        Assert.That(prefabSerialized.FindProperty("calendarGrid").objectReferenceValue, Is.Not.Null);
        Assert.That(prefabSerialized.FindProperty("discipleList").objectReferenceValue, Is.Not.Null);
        Assert.That(prefabSerialized.FindProperty("disciplePickerRoot").objectReferenceValue, Is.Not.Null);
        Assert.That(prefabSerialized.FindProperty("disciplePickerList").objectReferenceValue, Is.Not.Null);
        Assert.That(prefabSerialized.FindProperty("trainingBrushButton").objectReferenceValue, Is.Not.Null);
        Assert.That(prefabSerialized.FindProperty("dutyBrushButton").objectReferenceValue, Is.Not.Null);
        Assert.That(prefabSerialized.FindProperty("freeBrushButton").objectReferenceValue, Is.Not.Null);
        Assert.That(prefabSerialized.FindProperty("addDiscipleButton").objectReferenceValue, Is.Not.Null);
        Assert.That(FindChild(prefab.transform, "BulkActions"), Is.Null);
        Assert.That(FindChild(prefab.transform, "DisciplePicker").gameObject.activeSelf, Is.False);

        RectTransform nameRow = FindChild(prefab.transform, "NameRow").GetComponent<RectTransform>();
        TMP_InputField input = FindChild(nameRow, "TemplateNameInput").GetComponent<TMP_InputField>();
        LayoutElement nameRowSize = nameRow.GetComponent<LayoutElement>();
        LayoutElement inputSize = input.GetComponent<LayoutElement>();
        Assert.That(nameRowSize.preferredHeight, Is.EqualTo(UIComponentStyles.CompactControlHeight));
        Assert.That(nameRowSize.flexibleHeight, Is.EqualTo(0f));
        Assert.That(inputSize.preferredWidth, Is.EqualTo(UIComponentStyles.CompactInputWidth));
        Assert.That(inputSize.flexibleWidth, Is.EqualTo(0f));

        GameObject playerObject = new GameObject("UITest_Player");
        PlayerManager player = playerObject.AddComponent<PlayerManager>();
        PlayerManager.Instance = player;
        MonthlyPlanTemplate template = MonthlyPlanRules.CreateTemplate("标准循环");
        Assert.That(template, Is.Not.Null);

        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = "UITest_MonthlyPlan";
        MonthlyPlanPanel view = instance.GetComponent<MonthlyPlanPanel>();
        view.OnOpened(null);
        SerializedObject serialized = new SerializedObject(view);
        RectTransform calendar = serialized.FindProperty("calendarGrid").objectReferenceValue as RectTransform;
        Assert.That(calendar, Is.Not.Null);
        Assert.That(calendar.childCount, Is.EqualTo(MonthlyPlanRules.DaysPerMonth));
    }

    [Test]
    public void MonthlyPlanBrush_PaintsClickAndDragWithoutCycling()
    {
        GameObject playerObject = new GameObject("UITest_Player");
        PlayerManager player = playerObject.AddComponent<PlayerManager>();
        PlayerManager.Instance = player;
        MonthlyPlanTemplate template = MonthlyPlanRules.CreateTemplate("画笔计划");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/UI/Prefabs/MonthlyPlan/MonthlyPlan.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = "UITest_MonthlyPlanBrush";
        MonthlyPlanPanel view = instance.GetComponent<MonthlyPlanPanel>();
        InvokePrivateAwake(view);
        view.OnOpened(null);
        SerializedObject serialized = new SerializedObject(view);
        Button dutyBrush = serialized.FindProperty("dutyBrushButton").objectReferenceValue as Button;
        dutyBrush.onClick.Invoke();

        MonthlyPlanDayCell day1 = FindChild(instance.transform, "Day01").GetComponent<MonthlyPlanDayCell>();
        MonthlyPlanDayCell day2 = FindChild(instance.transform, "Day02").GetComponent<MonthlyPlanDayCell>();
        GameObject eventObject = new GameObject("UITest_EventSystem", typeof(EventSystem));
        PointerEventData pointer = new PointerEventData(eventObject.GetComponent<EventSystem>())
            { button = PointerEventData.InputButton.Left };
        day1.OnPointerDown(pointer);
        day2.OnPointerEnter(pointer);
        MonthlyPlanDayCell.CancelDrag();

        Assert.That(template.days[0], Is.EqualTo(MonthlyActivityType.SectDuty));
        Assert.That(template.days[1], Is.EqualTo(MonthlyActivityType.SectDuty));
        Assert.That(template.days[2], Is.EqualTo(MonthlyActivityType.Free));
    }

    [Test]
    public void MonthlyPlanBinding_ShowsCandidatesOnlyAfterAddAndMovesSelectionToBoundList()
    {
        GameObject playerObject = new GameObject("UITest_Player");
        PlayerManager player = playerObject.AddComponent<PlayerManager>();
        PlayerManager.Instance = player;
        MonthlyPlanTemplate template = MonthlyPlanRules.CreateTemplate("绑定计划");
        GameObject npcObject = new GameObject("UITest_NPCManager");
        NPCManager npcs = npcObject.AddComponent<NPCManager>();
        NPCManager.Instance = npcs;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                                     System.Reflection.BindingFlags.NonPublic;
        var runtimes = (System.Collections.Generic.List<NPCRuntime>)typeof(NPCManager)
            .GetField("runtimes", flags).GetValue(npcs);
        NPCData firstData = ScriptableObject.CreateInstance<NPCData>();
        firstData.npcID = "binding_1";
        firstData.npcName = "楚星";
        NPCData secondData = ScriptableObject.CreateInstance<NPCData>();
        secondData.npcID = "binding_2";
        secondData.npcName = "白宁";
        runtimes.Add(new NPCRuntime(firstData, new CharacterState
        {
            characterId = "binding_1", templateId = "binding_1", displayName = "楚星", health = HealthState.Healthy
        }));
        runtimes.Add(new NPCRuntime(secondData, new CharacterState
        {
            characterId = "binding_2", templateId = "binding_2", displayName = "白宁", health = HealthState.Healthy
        }));

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/UI/Prefabs/MonthlyPlan/MonthlyPlan.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = "UITest_MonthlyPlanBinding";
        MonthlyPlanPanel view = instance.GetComponent<MonthlyPlanPanel>();
        InvokePrivateAwake(view);
        view.OnOpened(null);
        SerializedObject serialized = new SerializedObject(view);
        RectTransform boundList = serialized.FindProperty("discipleList").objectReferenceValue as RectTransform;
        GameObject picker = serialized.FindProperty("disciplePickerRoot").objectReferenceValue as GameObject;
        RectTransform candidates = serialized.FindProperty("disciplePickerList").objectReferenceValue as RectTransform;
        Button add = serialized.FindProperty("addDiscipleButton").objectReferenceValue as Button;
        Assert.That(boundList.childCount, Is.EqualTo(0));
        Assert.That(picker.activeSelf, Is.False);

        add.onClick.Invoke();
        Assert.That(picker.activeSelf, Is.True);
        Assert.That(candidates.childCount, Is.EqualTo(2));
        candidates.GetChild(0).GetComponent<Button>().onClick.Invoke();
        Assert.That(picker.activeSelf, Is.False);
        Assert.That(boundList.childCount, Is.EqualTo(1));
        Assert.That(template.discipleIds.Count, Is.EqualTo(1));

        UnityEngine.Object.DestroyImmediate(firstData);
        UnityEngine.Object.DestroyImmediate(secondData);
    }

    [TestCase(1920f, 1080f)]
    [TestCase(1280f, 720f)]
    public void MonthlyPlanCalendar_FitsThirtyCellsAtReferenceSizes(float width, float height)
    {
        GameObject playerObject = new GameObject("UITest_Player");
        PlayerManager player = playerObject.AddComponent<PlayerManager>();
        PlayerManager.Instance = player;
        Assert.That(MonthlyPlanRules.CreateTemplate("布局计划"), Is.Not.Null);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/UI/Prefabs/MonthlyPlan/MonthlyPlan.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = $"UITest_MonthlyPlan_{width}x{height}";
        RectTransform root = instance.GetComponent<RectTransform>();
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(width, height);
        instance.GetComponent<MonthlyPlanPanel>().OnOpened(null);

        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        RectTransform calendar = FindChild(root, "CalendarGrid").GetComponent<RectTransform>();
        RectTransform lastCell = FindChild(calendar, "Day30").GetComponent<RectTransform>();
        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(calendar, lastCell);
        Assert.That(bounds.max.x, Is.LessThanOrEqualTo(calendar.rect.xMax + 0.5f));
        Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(calendar.rect.yMin - 0.5f));
        RectTransform nameRow = FindChild(root, "NameRow").GetComponent<RectTransform>();
        RectTransform input = FindChild(nameRow, "TemplateNameInput").GetComponent<RectTransform>();
        Assert.That(nameRow.rect.height, Is.InRange(39.5f, 40.5f));
        Assert.That(input.rect.width, Is.InRange(UIComponentStyles.CompactInputWidth - 0.5f,
            UIComponentStyles.CompactInputWidth + 0.5f));
        RectTransform brushes = FindChild(root, "Brushes").GetComponent<RectTransform>();
        Assert.That(brushes.rect.height, Is.InRange(39.5f, 40.5f));
        foreach (Button brush in brushes.GetComponentsInChildren<Button>(true))
            Assert.That(brush.GetComponent<RectTransform>().rect.width,
                Is.InRange(UIComponentStyles.CompactTabButtonWidth - 0.5f,
                    UIComponentStyles.CompactTabButtonWidth + 0.5f));
        RectTransform advanced = FindChild(root, "AdvancedCultivationPlaceholder").GetComponent<RectTransform>();
        Assert.That(advanced.rect.height, Is.InRange(55.5f, 56.5f));
    }

    [TestCase(1920f, 1080f)]
    [TestCase(1280f, 720f)]
    public void DiscipleCenterTabs_KeepCompactRuntimeGeometry(float width, float height)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/UI/Prefabs/Disciple/DiscipleCenter.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = $"UITest_DiscipleCenter_{width}x{height}";
        RectTransform root = instance.GetComponent<RectTransform>();
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(width, height);

        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(root);

        RectTransform tabs = FindChild(root, "Tabs").GetComponent<RectTransform>();
        RectTransform pages = FindChild(root, "Pages").GetComponent<RectTransform>();
        Assert.That(tabs.rect.height, Is.InRange(39.5f, 40.5f));
        Assert.That(pages.rect.height, Is.GreaterThan(tabs.rect.height * 5f));
        foreach (Transform child in tabs)
            Assert.That(((RectTransform)child).rect.width, Is.InRange(91.5f, 92.5f));
    }

    [Test]
    public void UIRootCanvasAndShellAnchors_MatchReferenceLayout()
    {
        GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefab/UI/UIRoot.prefab");
        foreach (CanvasScaler scaler in root.GetComponentsInChildren<CanvasScaler>(true))
        {
            Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
            Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1920f, 1080f)));
            Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(0.5f));
        }

        RectTransform top = FindChild(root.transform, "TopBar").GetComponent<RectTransform>();
        RectTransform rail = FindChild(root.transform, "NavigationRail").GetComponent<RectTransform>();
        RectTransform screen = FindChild(root.transform, "ScreenLayer").GetComponent<RectTransform>();
        Assert.That(top.anchorMin, Is.EqualTo(new Vector2(0f, 0.93f)));
        Assert.That(top.anchorMax, Is.EqualTo(Vector2.one));
        Assert.That(rail.anchorMin, Is.EqualTo(Vector2.zero));
        Assert.That(rail.anchorMax, Is.EqualTo(new Vector2(0.06f, 0.93f)));
        Assert.That(screen.anchorMin, Is.EqualTo(new Vector2(0.06f, 0f)));
        Assert.That(screen.anchorMax, Is.EqualTo(new Vector2(1f, 0.93f)));
    }

    [Test]
    public void LegacyEventShortcut_CanBeHiddenWithoutClosingEventUi()
    {
        GameObject owner = new GameObject("UITest_EventPanel");
        CharacterEventPanel panel = owner.AddComponent<CharacterEventPanel>();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                                     System.Reflection.BindingFlags.NonPublic;
        typeof(CharacterEventPanel).GetMethod("Awake", flags).Invoke(panel, null);
        Button shortcut = (Button)typeof(CharacterEventPanel).GetField("inboxButton", flags).GetValue(panel);

        panel.SetLegacyInboxShortcutVisible(false);
        Assert.That(shortcut.gameObject.activeSelf, Is.False);
        panel.SetLegacyInboxShortcutVisible(true);
        Assert.That(shortcut.gameObject.activeSelf, Is.True);
    }

    [Test]
    public void SampleScene_ContainsLegacyRegistryButNoSceneUIManager()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        Assert.That(Resources.FindObjectsOfTypeAll<UIManager>().Any(item => item.gameObject.scene == scene), Is.False);
        Assert.That(Resources.FindObjectsOfTypeAll<LegacySceneUiRegistry>()
            .Count(item => item.gameObject.scene == scene), Is.EqualTo(1));
        Assert.That(Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>()
            .Where(item => item.gameObject.scene == scene)
            .Select(item => new SerializedObject(item).FindProperty("m_OnClick.m_PersistentCalls.m_Calls"))
            .SelectMany(calls => Enumerable.Range(0, calls.arraySize)
                .Select(index => calls.GetArrayElementAtIndex(index)))
            .Any(call => call.FindPropertyRelative("m_Target").objectReferenceValue == null &&
                         call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue ==
                         "UIManager, Assembly-CSharp"), Is.False);
    }

    [Test]
    public void SnapshotBuilder_DoesNotMutateCharacterState()
    {
        NPCData data = ScriptableObject.CreateInstance<NPCData>();
        data.npcID = "snapshot_test";
        data.npcName = "测试弟子";
        data.attack = 7;
        data.intelligence = 8;
        data.agility = 9;
        data.comprehension = 10;
        data.physique = 11;
        NPCRuntime npc = new NPCRuntime(data);
        npc.Character.naqiProgress = 32.5f;
        npc.Character.mainTechniqueId = "qingmu";
        npc.Character.techniqueProgresses.Add(new PersonalTechniqueProgress { techniqueId = "qingmu", understanding = 14f });
        npc.Character.AddLifeRecord(2, "Recruit", "加入宗门");
        string before = JsonUtility.ToJson(npc.Character);

        DiscipleCenterSnapshot snapshot = DiscipleCenterSnapshotBuilder.Build(
            new[] { npc }, npc.CharacterId, 3);

        Assert.That(snapshot.selectedCharacterId, Is.EqualTo(npc.CharacterId));
        Assert.That(snapshot.overview, Does.Contain("32.5%"));
        Assert.That(snapshot.mainTechniqueName, Is.EqualTo("青木长生诀"));
        Assert.That(snapshot.techniqueStage, Is.EqualTo("初学"));
        Assert.That(snapshot.techniqueUnderstanding, Is.EqualTo(14f));
        Assert.That(snapshot.realm, Is.Not.Empty);
        Assert.That(snapshot.age, Is.EqualTo(npc.Character.age));
        Assert.That(JsonUtility.ToJson(npc.Character), Is.EqualTo(before));
        UnityEngine.Object.DestroyImmediate(data);
    }

    [Test]
    public void SnapshotBuilder_EmptyRosterHasStableEmptyState()
    {
        DiscipleCenterSnapshot snapshot = DiscipleCenterSnapshotBuilder.Build(
            Array.Empty<NPCRuntime>(), "missing", 1);

        Assert.That(snapshot.disciples, Is.Empty);
        Assert.That(snapshot.HasSelection, Is.False);
    }

    [Test]
    public void SnapshotBuilder_DiscipleWithoutTechniqueHasExplicitEmptyState()
    {
        NPCData data = ScriptableObject.CreateInstance<NPCData>();
        data.npcID = "snapshot_no_technique";
        data.npcName = "无功法弟子";
        NPCRuntime npc = new NPCRuntime(data);

        DiscipleCenterSnapshot snapshot = DiscipleCenterSnapshotBuilder.Build(
            new[] { npc }, npc.CharacterId, 1);

        Assert.That(snapshot.mainTechniqueName, Is.EqualTo("未修习"));
        Assert.That(snapshot.techniqueStage, Is.EqualTo("未修习"));
        Assert.That(snapshot.techniqueUnderstanding, Is.Zero);
        UnityEngine.Object.DestroyImmediate(data);
    }

    private static Transform FindChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform nested = FindChild(child, name);
            if (nested != null) return nested;
        }
        return null;
    }

    private static bool HasPresenterHandler(TimeManager time, string eventFieldName,
        DiscipleCenterPresenter presenter)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                                     System.Reflection.BindingFlags.NonPublic;
        Delegate handlers = typeof(TimeManager).GetField(eventFieldName, flags)?.GetValue(time) as Delegate;
        return handlers?.GetInvocationList().Any(handler => ReferenceEquals(handler.Target, presenter)) == true;
    }

    private static void InvokePrivateAwake(MonthlyPlanPanel view)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                                     System.Reflection.BindingFlags.NonPublic;
        typeof(MonthlyPlanPanel).GetMethod("Awake", flags).Invoke(view, null);
    }
}
