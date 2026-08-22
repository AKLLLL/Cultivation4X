#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class GlobalUIAssetBuilder
{
    private const string FontPath = "Assets/Resources/SourceHanSansHWSC-Bold SDF.asset";
    private const string ThemePath = "Assets/UI/Theme/CultivationUITheme.asset";
    private const string CommonPath = "Assets/UI/Prefabs/Common";
    private const string DisciplePath = "Assets/UI/Prefabs/Disciple";
    private const string RootPath = "Assets/Resources/Prefab/UI/UIRoot.prefab";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Cultivation4X/UI/Build Global UI V1")]
    public static void Build()
    {
        EnsureFolders();
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) throw new System.InvalidOperationException($"缺少 TMP 字体: {FontPath}");
        UITheme theme = BuildTheme(font);
        BuildCommonPrefabs(font, theme);
        DiscipleListItemView listItem = BuildDiscipleListItem(font);
        DiscipleHistoryItemView historyItem = BuildDiscipleHistoryItem(font);
        GameObject discipleCenter = BuildDiscipleCenter(font, listItem, historyItem);
        BuildUiRoot(font, theme, discipleCenter);
        MigrateSampleScene();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Global UI V1 资产与 SampleScene 迁移完成。");
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets", "UI");
        EnsureFolder("Assets/UI", "Theme");
        EnsureFolder("Assets/UI", "Prefabs");
        EnsureFolder("Assets/UI/Prefabs", "Common");
        EnsureFolder("Assets/UI/Prefabs", "Disciple");
        EnsureFolder("Assets/Resources/Prefab", "UI");
    }

    private static void EnsureFolder(string parent, string name)
    {
        string path = $"{parent}/{name}";
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
    }

    private static UITheme BuildTheme(TMP_FontAsset font)
    {
        UITheme theme = AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath);
        if (theme == null)
        {
            theme = ScriptableObject.CreateInstance<UITheme>();
            AssetDatabase.CreateAsset(theme, ThemePath);
        }
        theme.font = font;
        theme.background = new Color(0.022f, 0.047f, 0.038f, 0.985f);
        theme.panel = new Color(0.045f, 0.090f, 0.070f, 0.98f);
        theme.card = new Color(0.075f, 0.125f, 0.095f, 0.97f);
        theme.text = new Color(0.91f, 0.92f, 0.84f, 1f);
        theme.secondaryText = new Color(0.63f, 0.70f, 0.61f, 1f);
        theme.accent = new Color(0.48f, 0.38f, 0.17f, 1f);
        EditorUtility.SetDirty(theme);
        return theme;
    }

    private static void BuildCommonPrefabs(TMP_FontAsset font, UITheme theme)
    {
        SaveCommon("TopBar", root =>
        {
            root.AddComponent<HorizontalLayoutGroup>();
            root.GetComponent<Image>().color = theme.panel;
        });
        SaveCommon("NavigationButton", root => CreateButton(root.transform, "导航", font));
        SaveCommon("ScreenFrame", root => root.GetComponent<Image>().color = theme.background);
        SaveCommon("TitleBar", root => CreateText(root.transform, "页面标题", 28, font));
        SaveCommon("TabBar", root => ConfigureCompactTabBar(root));
        SaveCommon("TabButton", root => ConfigureCompactTabButton(
            CreateButton(root.transform, "页签", font)));
        SaveCommon("InfoCard", root =>
        {
            root.GetComponent<Image>().color = UIComponentStyles.InfoCard;
            VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = UIComponentStyles.InfoCardSpacing;
            layout.childForceExpandHeight = false;
        });
        SaveCommon("StatusTag", root => CreateText(root.transform, "状态", 16, font));
        SaveCommon("ProgressBar", root =>
        {
            GameObject fill = Panel(root.transform, "Fill", new Color(0.55f, 0.36f, 0.13f, 1f));
            RectTransform rect = fill.GetComponent<RectTransform>();
            rect.anchorMax = new Vector2(0.6f, 1f);
        });
        SaveCommon("ListItem", root => CreateButton(root.transform, "列表项", font));
        SaveCommon("EmptyState", root => CreateText(root.transform, "暂无内容", 18, font));
        SaveCommon("ReturnToMapButton", root => CreateButton(root.transform, "返回地图", font));
    }

    private static void SaveCommon(string name, System.Action<GameObject> build)
    {
        GameObject root = Panel(null, name, new Color(0.10f, 0.09f, 0.075f, 0.96f));
        build(root);
        PrefabUtility.SaveAsPrefabAsset(root, $"{CommonPath}/{name}.prefab");
        Object.DestroyImmediate(root);
    }

    private static DiscipleListItemView BuildDiscipleListItem(TMP_FontAsset font)
    {
        GameObject root = new GameObject("DiscipleListItem", typeof(RectTransform), typeof(Image),
            typeof(Button), typeof(LayoutElement), typeof(VerticalLayoutGroup), typeof(DiscipleListItemView));
        Image image = root.GetComponent<Image>();
        image.color = new Color(0.075f, 0.105f, 0.085f, 0.96f);
        LayoutElement element = root.GetComponent<LayoutElement>();
        element.minHeight = 78f;
        element.preferredHeight = 78f;
        VerticalLayoutGroup layout = root.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 7, 7);
        layout.spacing = 3f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        GameObject topRow = new GameObject("TopRow", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        topRow.transform.SetParent(root.transform, false);
        topRow.GetComponent<LayoutElement>().preferredHeight = 30f;
        HorizontalLayoutGroup topLayout = topRow.GetComponent<HorizontalLayoutGroup>();
        topLayout.spacing = 8f;
        topLayout.childControlWidth = true;
        topLayout.childControlHeight = true;
        topLayout.childForceExpandWidth = false;
        TMP_Text name = CreateText(topRow.transform, "弟子姓名", 20, font, 30);
        name.fontStyle = FontStyles.Bold;
        name.enableWordWrapping = false;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.GetComponent<LayoutElement>().flexibleWidth = 1f;
        TMP_Text state = CreateStatusTag(topRow.transform, "空闲", font, 62f);

        TMP_Text realm = CreateText(root.transform, "炼气一层", 14, font, 24);
        realm.color = new Color(0.70f, 0.74f, 0.66f, 1f);
        realm.enableWordWrapping = false;
        realm.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement realmSize = realm.GetComponent<LayoutElement>();
        realmSize.minWidth = 110f;
        realmSize.flexibleWidth = 1f;
        root.GetComponent<DiscipleListItemView>().Configure(name, realm, state,
            image, root.GetComponent<Button>());
        string path = $"{DisciplePath}/DiscipleListItem.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        DiscipleListItemView view = prefab == null ? null : prefab.GetComponent<DiscipleListItemView>();
        if (view == null)
            throw new System.InvalidOperationException($"弟子列表项 Prefab 缺少 {nameof(DiscipleListItemView)}: {path}");
        return view;
    }

    private static DiscipleHistoryItemView BuildDiscipleHistoryItem(TMP_FontAsset font)
    {
        GameObject root = Panel(null, "DiscipleHistoryItem", new Color(0.055f, 0.095f, 0.073f, 0.98f));
        root.AddComponent<LayoutElement>().minHeight = 82f;
        root.AddComponent<DiscipleHistoryItemView>();
        HorizontalLayoutGroup layout = root.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 12, 9, 9);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = true;
        Image marker = Panel(root.transform, "TimelineMarker", new Color(0.55f, 0.47f, 0.30f, 1f)).GetComponent<Image>();
        marker.gameObject.AddComponent<LayoutElement>().preferredWidth = 5f;
        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        content.transform.SetParent(root.transform, false);
        content.GetComponent<LayoutElement>().flexibleWidth = 1f;
        VerticalLayoutGroup contentLayout = content.GetComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 3f;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandHeight = false;
        TMP_Text heading = CreateText(content.transform, "第 1 天 · 事件", 16, font, 25);
        heading.fontStyle = FontStyles.Bold;
        TMP_Text body = CreateText(content.transform, "履历内容", 14, font, 32);
        body.color = new Color(0.76f, 0.79f, 0.70f, 1f);
        body.alignment = TextAlignmentOptions.TopLeft;
        TMP_Text type = CreateStatusTag(root.transform, "事件", font, 58f);
        root.GetComponent<DiscipleHistoryItemView>().Configure(marker, type, heading, body);
        string path = $"{DisciplePath}/DiscipleHistoryItem.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        DiscipleHistoryItemView view = prefab == null ? null : prefab.GetComponent<DiscipleHistoryItemView>();
        if (view == null)
            throw new System.InvalidOperationException($"弟子履历 Prefab 缺少 {nameof(DiscipleHistoryItemView)}: {path}");
        return view;
    }

    private static GameObject BuildDiscipleCenter(TMP_FontAsset font, DiscipleListItemView listItem,
        DiscipleHistoryItemView historyItem)
    {
        GameObject root = Panel(null, "DiscipleCenter", new Color(0.022f, 0.047f, 0.038f, 0.99f));
        root.AddComponent<DiscipleCenterView>();
        HorizontalLayoutGroup columns = root.AddComponent<HorizontalLayoutGroup>();
        columns.padding = new RectOffset(14, 14, 14, 14);
        columns.spacing = 10f;
        columns.childControlWidth = true;
        columns.childControlHeight = true;
        columns.childForceExpandWidth = false;
        columns.childForceExpandHeight = true;

        GameObject left = Column(root.transform, "DiscipleList", 310f, 0f);
        AddPanelOutline(left);
        TMP_Text listTitle = CreateText(left.transform, "弟子名录", 23, font, 38);
        listTitle.fontStyle = FontStyles.Bold;
        RectTransform listContent = CreateScrollContent(left.transform, "DiscipleListScroll", out _);
        TMP_Text empty = CreateText(left.transform, "暂无弟子", 16, font, 38);

        GameObject center = Column(root.transform, "CharacterCenter", 0f, 1f);
        AddPanelOutline(center);
        RectTransform tabs = Panel(center.transform, "Tabs", new Color(0.045f, 0.085f, 0.064f, 1f)).GetComponent<RectTransform>();
        HorizontalLayoutGroup tabsLayout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabsLayout.spacing = UIComponentStyles.CompactTabSpacing;
        tabsLayout.childControlWidth = true;
        tabsLayout.childControlHeight = true;
        tabsLayout.childForceExpandWidth = false;
        tabsLayout.childForceExpandHeight = false;
        tabsLayout.childAlignment = TextAnchor.MiddleLeft;
        LayoutElement tabBarSize = tabs.gameObject.AddComponent<LayoutElement>();
        tabBarSize.minHeight = UIComponentStyles.CompactTabBarHeight;
        tabBarSize.preferredHeight = UIComponentStyles.CompactTabBarHeight;
        tabBarSize.flexibleHeight = 0f;
        string[] labels = { "概览", "能力", "关系", "履历" };
        Button[] tabButtons = labels.Select(label => CreateTabButton(tabs, label, font)).ToArray();

        GameObject pages = new GameObject("Pages", typeof(RectTransform), typeof(LayoutElement));
        pages.transform.SetParent(center.transform, false);
        pages.GetComponent<LayoutElement>().flexibleHeight = 1f;
        GameObject[] tabPages = new GameObject[4];

        RectTransform overviewContent = CreateScrollContent(pages.transform, "概览", out GameObject overviewRoot);
        Stretch(overviewRoot.GetComponent<RectTransform>());
        VerticalLayoutGroup overviewLayout = overviewContent.GetComponent<VerticalLayoutGroup>();
        overviewLayout.padding = new RectOffset(18, 18, 16, 16);
        overviewLayout.spacing = 10f;
        TMP_Text name = CreateText(overviewContent, "弟子姓名", 28, font, 40f);
        name.fontStyle = FontStyles.Bold;
        name.enableWordWrapping = false;
        name.overflowMode = TextOverflowModes.Ellipsis;

        GameObject identityRow = CreateOverviewRow(overviewContent, "IdentityRow");
        TMP_Text identityText = CreateText(identityRow.transform, "身份：宗门弟子", 16, font, 28f);
        identityText.GetComponent<LayoutElement>().flexibleWidth = 1f;
        TMP_Text state = CreateStatusTag(identityRow.transform, "空闲", font, 68f);

        GameObject ageRow = CreateOverviewRow(overviewContent, "AgeRow");
        TMP_Text ageText = CreateText(ageRow.transform, "年龄：15", 16, font, 28f);
        ageText.GetComponent<LayoutElement>().flexibleWidth = 1f;
        TMP_Text health = CreateStatusTag(ageRow.transform, "健康", font, 68f);
        TMP_Text realmText = CreateText(overviewContent, "境界：炼气一层", 16, font, 30f);

        GameObject divider = Panel(overviewContent, "Divider", new Color(0.38f, 0.31f, 0.16f, 0.55f));
        divider.AddComponent<LayoutElement>().preferredHeight = 1f;
        CreateProgressRow(overviewContent, "纳气", font, new Color(0.29f, 0.55f, 0.39f, 1f),
            out Image naqiFill, out TMP_Text naqiValue);
        CreateProgressRow(overviewContent, "传承", font, new Color(0.53f, 0.43f, 0.22f, 1f),
            out Image masteryFill, out TMP_Text masteryValue);
        TMP_Text mentalText = CreateText(overviewContent, "心境：100 / 100", 16, font, 30f);
        TMP_Text dailyAuraText = CreateText(overviewContent, "今日灵气：0 / 100", 16, font, 30f);
        tabPages[0] = overviewRoot;

        TMP_Text[] pageTexts = new TMP_Text[2];
        for (int index = 0; index < 2; index++)
        {
            int tabIndex = index + 1;
            RectTransform content = CreateScrollContent(pages.transform, labels[tabIndex], out GameObject scrollRoot);
            Stretch(scrollRoot.GetComponent<RectTransform>());
            TMP_Text pageText = CreateText(content, string.Empty, 16, font, 36);
            pageText.alignment = TextAlignmentOptions.TopLeft;
            pageText.enableWordWrapping = true;
            pageText.overflowMode = TextOverflowModes.Overflow;
            pageText.GetComponent<LayoutElement>().preferredHeight = -1f;
            pageText.GetComponent<LayoutElement>().minHeight = 36f;
            pageTexts[index] = pageText;
            tabPages[tabIndex] = scrollRoot;
        }
        RectTransform historyContent = CreateScrollContent(pages.transform, "履历", out GameObject historyRoot);
        Stretch(historyRoot.GetComponent<RectTransform>());
        TMP_Text historyEmpty = CreateText(historyContent, "暂无人生履历。", 16, font, 38);
        historyEmpty.alignment = TextAlignmentOptions.TopLeft;
        tabPages[3] = historyRoot;

        GameObject right = Column(root.transform, "CultivationObservation", 360f, 0f);
        AddPanelOutline(right);
        TMP_Text observationTitle = CreateText(right.transform, "培养观察", 23, font, 38);
        observationTitle.fontStyle = FontStyles.Bold;
        TMP_Text currentAction = CreateObservationCard(right.transform, "当前行动", "暂无当前行动", font,
            new Color(0.09f, 0.16f, 0.12f, 1f), 106f);
        TMP_Text currentPlan = CreateObservationCard(right.transform, "本月计划", "暂无本月计划", font,
            new Color(0.075f, 0.13f, 0.10f, 1f), 180f);
        TMP_Text nextPlan = CreateObservationCard(right.transform, "下月计划", "暂无下月计划", font,
            new Color(0.075f, 0.13f, 0.10f, 1f), 132f);
        GameObject spacer = new GameObject("FlexibleSpace", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(right.transform, false);
        spacer.GetComponent<LayoutElement>().flexibleHeight = 1f;

        root.GetComponent<DiscipleCenterView>().Configure(listContent, listItem, empty, name, realmText,
            ageText, state, health, naqiFill, naqiValue, masteryFill, masteryValue,
            mentalText, dailyAuraText, pageTexts[0], pageTexts[1], historyContent, historyItem, historyEmpty,
            currentAction, currentPlan, nextPlan, tabButtons, tabPages);
        string path = $"{DisciplePath}/DiscipleCenter.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static void BuildUiRoot(TMP_FontAsset font, UITheme theme, GameObject discipleCenter)
    {
        GameObject root = new GameObject("UIRoot", typeof(UIManager));

        GameObject hudCanvas = CanvasRoot(root.transform, "GlobalHudCanvas", 100);
        GlobalHudView hud = hudCanvas.AddComponent<GlobalHudView>();
        GameObject shell = new GameObject("ShellRoot", typeof(RectTransform));
        shell.transform.SetParent(hudCanvas.transform, false);
        Stretch(shell.GetComponent<RectTransform>());

        GameObject top = Panel(shell.transform, "TopBar", new Color(0.028f, 0.060f, 0.047f, 0.985f));
        RectTransform topRect = top.GetComponent<RectTransform>();
        topRect.anchorMin = new Vector2(0f, 0.93f); topRect.anchorMax = Vector2.one;
        topRect.offsetMin = topRect.offsetMax = Vector2.zero;
        HorizontalLayoutGroup topLayout = top.AddComponent<HorizontalLayoutGroup>();
        topLayout.padding = new RectOffset(18, 18, 8, 8);
        topLayout.spacing = 8f;
        topLayout.childAlignment = TextAnchor.MiddleCenter;
        topLayout.childControlWidth = true;
        topLayout.childControlHeight = true;
        GameObject sectGroup = CreateTopGroup(top.transform, "SectGroup", 240f,
            new Color(0.055f, 0.105f, 0.078f, 1f));
        TMP_Text sectName = CreateText(sectGroup.transform, "未立宗", 21, font, 44);
        sectName.fontStyle = FontStyles.Bold;
        sectName.GetComponent<LayoutElement>().flexibleWidth = 1f;
        TMP_Text context = CreateText(top.transform, "世界地图", 22, font, 50);
        context.alignment = TextAlignmentOptions.Center;
        context.GetComponent<LayoutElement>().flexibleWidth = 1f;
        GameObject resourceGroup = CreateTopGroup(top.transform, "ResourceGroup", 300f,
            new Color(0.060f, 0.115f, 0.085f, 1f));
        TMP_Text resources = CreateText(resourceGroup.transform, "灵石 0　材料 0　弟子 0", 15, font, 44);
        resources.GetComponent<LayoutElement>().flexibleWidth = 1f;
        GameObject progressGroup = CreateTopGroup(top.transform, "ProgressGroup", 205f,
            new Color(0.050f, 0.095f, 0.075f, 1f));
        Button eventButton = CreateButton(progressGroup.transform, "事件 0", font);
        eventButton.GetComponent<LayoutElement>().preferredWidth = 100f;
        TMP_Text day = CreateText(progressGroup.transform, "第 0 天", 15, font, 44);
        day.GetComponent<LayoutElement>().preferredWidth = 90f;
        GameObject actionGroup = CreateTopGroup(top.transform, "ActionGroup", 128f,
            new Color(0.115f, 0.095f, 0.045f, 1f));
        Button endDay = CreateButton(actionGroup.transform, "结束今天", font);
        endDay.GetComponent<LayoutElement>().flexibleWidth = 1f;

        GameObject rail = Panel(shell.transform, "NavigationRail", new Color(0.028f, 0.060f, 0.047f, 0.985f));
        RectTransform railRect = rail.GetComponent<RectTransform>();
        railRect.anchorMin = Vector2.zero; railRect.anchorMax = new Vector2(0.06f, 0.93f);
        railRect.offsetMin = railRect.offsetMax = Vector2.zero;
        VerticalLayoutGroup railLayout = rail.AddComponent<VerticalLayoutGroup>();
        railLayout.padding = new RectOffset(8, 8, 16, 16);
        railLayout.spacing = 10f;
        railLayout.childControlWidth = true;
        railLayout.childForceExpandWidth = true;
        railLayout.childControlHeight = true;
        railLayout.childForceExpandHeight = false;
        Button map = NavigationButton(rail.transform, "图\n地图", font);
        Button sect = NavigationButton(rail.transform, "宗\n宗门", font);
        Button disciple = NavigationButton(rail.transform, "人\n弟子", font);
        Button mission = NavigationButton(rail.transform, "任\n任务", font);
        Button resource = NavigationButton(rail.transform, "库\n资源", font);

        Button returnButton = CreateButton(shell.transform, "返回地图", font);
        RectTransform returnRect = returnButton.GetComponent<RectTransform>();
        returnRect.anchorMin = returnRect.anchorMax = new Vector2(1f, 0f);
        returnRect.pivot = new Vector2(1f, 0f);
        returnRect.anchoredPosition = new Vector2(-24f, 24f);
        returnRect.sizeDelta = new Vector2(150f, 48f);
        Canvas returnCanvas = returnButton.gameObject.AddComponent<Canvas>();
        returnCanvas.overrideSorting = true;
        returnCanvas.sortingOrder = 3900;
        returnButton.gameObject.AddComponent<GraphicRaycaster>();
        returnButton.gameObject.SetActive(false);

        GameObject screenCanvas = CanvasRoot(root.transform, "ScreenCanvas", 2000);
        GameObject screenLayer = new GameObject("ScreenLayer", typeof(RectTransform));
        screenLayer.transform.SetParent(screenCanvas.transform, false);
        RectTransform screenRect = screenLayer.GetComponent<RectTransform>();
        screenRect.anchorMin = new Vector2(0.06f, 0f); screenRect.anchorMax = new Vector2(1f, 0.93f);
        screenRect.offsetMin = screenRect.offsetMax = Vector2.zero;

        GameObject modalCanvas = CanvasRoot(root.transform, "ModalCanvas", 4000);
        GameObject modalLayer = new GameObject("ModalLayer", typeof(RectTransform));
        modalLayer.transform.SetParent(modalCanvas.transform, false);
        Stretch(modalLayer.GetComponent<RectTransform>());

        GameObject overlayCanvas = CanvasRoot(root.transform, "OverlayCanvas", 6000);
        GameObject overlayLayer = new GameObject("OverlayLayer", typeof(RectTransform));
        overlayLayer.transform.SetParent(overlayCanvas.transform, false);
        Stretch(overlayLayer.GetComponent<RectTransform>());

        hud.Configure(shell, sectName, context, resources, eventButton.GetComponentInChildren<TMP_Text>(),
            day, map, sect, disciple, mission, resource, eventButton, endDay, returnButton);
        root.GetComponent<UIManager>().Configure(screenLayer.transform, modalLayer.transform,
            overlayLayer.transform, hud, theme, new[]
            {
                new UIWindowRegistration
                {
                    id = UIWindowId.DiscipleCenter,
                    title = "弟子中心",
                    layer = UIWindowLayer.Screen,
                    escapePolicy = UIEscapePolicy.Allowed,
                    blocksWorldInput = true,
                    cacheInstance = true,
                    prefab = discipleCenter
                }
            });
        PrefabUtility.SaveAsPrefabAsset(root, RootPath);
        Object.DestroyImmediate(root);
    }

    private static void MigrateSampleScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        UIManager oldManager = Resources.FindObjectsOfTypeAll<UIManager>()
            .FirstOrDefault(item => item != null && item.gameObject.scene == scene);
        LegacySceneUiRegistry registry = Resources.FindObjectsOfTypeAll<LegacySceneUiRegistry>()
            .FirstOrDefault(item => item != null && item.gameObject.scene == scene);
        if (oldManager != null)
        {
            SerializedObject serialized = new SerializedObject(oldManager);
            SerializedProperty panels = serialized.FindProperty("panels");
            List<GameObject> legacyPanels = new List<GameObject>();
            for (int index = 0; panels != null && index < panels.arraySize; index++)
            {
                GameObject panel = panels.GetArrayElementAtIndex(index).objectReferenceValue as GameObject;
                if (panel != null) legacyPanels.Add(panel);
            }
            GameObject registryObject = oldManager.gameObject;
            registry = registryObject.GetComponent<LegacySceneUiRegistry>() ??
                       registryObject.AddComponent<LegacySceneUiRegistry>();
            registry.Configure(legacyPanels);
            RetargetLegacyButtonCalls(scene, oldManager, registry);
            Object.DestroyImmediate(oldManager);
            registryObject.name = "LegacyUIRegistry";
            EditorUtility.SetDirty(registryObject);
        }
        if (registry != null) RetargetLegacyButtonCalls(scene, null, registry);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void RetargetLegacyButtonCalls(Scene scene, UIManager oldManager,
        LegacySceneUiRegistry registry)
    {
        foreach (Button button in Resources.FindObjectsOfTypeAll<Button>()
                     .Where(item => item != null && item.gameObject.scene == scene))
        {
            SerializedObject serialized = new SerializedObject(button);
            SerializedProperty calls = serialized.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            bool changed = false;
            for (int index = 0; calls != null && index < calls.arraySize; index++)
            {
                SerializedProperty call = calls.GetArrayElementAtIndex(index);
                SerializedProperty target = call.FindPropertyRelative("m_Target");
                SerializedProperty targetType = call.FindPropertyRelative("m_TargetAssemblyTypeName");
                SerializedProperty method = call.FindPropertyRelative("m_MethodName");
                bool wasOldManager = oldManager != null && target.objectReferenceValue == oldManager;
                bool isMissingManagerCall = target.objectReferenceValue == null &&
                    targetType.stringValue == "UIManager, Assembly-CSharp" && method.stringValue == "OpenPanel";
                if (!wasOldManager && !isMissingManagerCall) continue;
                target.objectReferenceValue = registry;
                targetType.stringValue = "LegacySceneUiRegistry, Assembly-CSharp";
                changed = true;
            }
            if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static GameObject CanvasRoot(Transform parent, string name, int order)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(parent, false);
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return go;
    }

    private static GameObject Column(Transform parent, string name, float width, float flexibleWidth)
    {
        GameObject column = Panel(parent, name, new Color(0.038f, 0.078f, 0.059f, 0.985f));
        column.AddComponent<VerticalLayoutGroup>();
        VerticalLayoutGroup layout = column.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 14, 14);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        LayoutElement element = column.AddComponent<LayoutElement>();
        element.preferredWidth = width;
        element.flexibleWidth = flexibleWidth;
        element.flexibleHeight = 1f;
        return column;
    }

    private static GameObject CreateOverviewRow(Transform parent, string name)
    {
        GameObject row = new GameObject(name, typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().preferredHeight = 28f;
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;
        return row;
    }

    private static TMP_Text CreateStatusTag(Transform parent, string value, TMP_FontAsset font, float width)
    {
        GameObject tag = Panel(parent, value + "Tag", new Color(0.13f, 0.34f, 0.23f, 1f));
        LayoutElement layout = tag.AddComponent<LayoutElement>();
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.flexibleWidth = 0f;
        layout.preferredHeight = 24f;
        TMP_Text text = CreateText(tag.transform, value, 13, font, 24f);
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(text.rectTransform);
        return text;
    }

    private static void CreateProgressRow(Transform parent, string label, TMP_FontAsset font, Color color,
        out Image fill, out TMP_Text value)
    {
        GameObject row = new GameObject(label + "Progress", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().preferredHeight = 20f;
        HorizontalLayoutGroup rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 7f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        TMP_Text labelText = CreateText(row.transform, label, 13, font, 20f);
        labelText.color = new Color(0.66f, 0.70f, 0.62f, 1f);
        labelText.enableWordWrapping = false;
        LayoutElement labelSize = labelText.GetComponent<LayoutElement>();
        labelSize.minWidth = 38f;
        labelSize.preferredWidth = 38f;
        labelSize.flexibleWidth = 0f;
        GameObject bar = Panel(row.transform, label + "Bar", new Color(0.025f, 0.050f, 0.040f, 1f));
        LayoutElement barSize = bar.AddComponent<LayoutElement>();
        barSize.minWidth = 90f;
        barSize.flexibleWidth = 1f;
        fill = Panel(bar.transform, "Fill", color).GetComponent<Image>();
        fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        value = CreateText(row.transform, "0.0%", 12, font, 20f);
        value.alignment = TextAlignmentOptions.MidlineRight;
        value.enableWordWrapping = false;
        value.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement valueSize = value.GetComponent<LayoutElement>();
        valueSize.minWidth = 50f;
        valueSize.preferredWidth = 50f;
        valueSize.flexibleWidth = 0f;
    }

    private static TMP_Text CreateObservationCard(Transform parent, string title, string body,
        TMP_FontAsset font, Color color, float height)
    {
        GameObject card = Panel(parent, title + "Card", color);
        LayoutElement cardSize = card.AddComponent<LayoutElement>();
        cardSize.minHeight = height;
        cardSize.preferredHeight = height;
        AddPanelOutline(card);
        VerticalLayoutGroup layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.spacing = 7f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        TMP_Text heading = CreateText(card.transform, title, 17, font, 27f);
        heading.fontStyle = FontStyles.Bold;
        heading.color = new Color(0.72f, 0.62f, 0.36f, 1f);
        TMP_Text content = CreateText(card.transform, body, 15, font, height - 54f);
        content.alignment = TextAlignmentOptions.TopLeft;
        content.enableWordWrapping = true;
        content.enableAutoSizing = true;
        content.fontSizeMin = 11f;
        content.fontSizeMax = 15f;
        content.overflowMode = TextOverflowModes.Ellipsis;
        return content;
    }

    private static void AddPanelOutline(GameObject panel)
    {
        Outline outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(0.38f, 0.31f, 0.16f, 0.48f);
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;
    }

    private static GameObject CreateTopGroup(Transform parent, string name, float width, Color color)
    {
        GameObject group = Panel(parent, name, color);
        LayoutElement size = group.AddComponent<LayoutElement>();
        size.preferredWidth = width;
        size.preferredHeight = 46f;
        HorizontalLayoutGroup layout = group.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 2, 2);
        layout.spacing = 5f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childAlignment = TextAnchor.MiddleCenter;
        return group;
    }

    private static RectTransform CreateScrollContent(Transform parent, string name, out GameObject scrollRoot)
    {
        scrollRoot = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        scrollRoot.transform.SetParent(parent, false);
        scrollRoot.GetComponent<Image>().color = new Color(0.018f, 0.040f, 0.032f, 0.55f);
        scrollRoot.GetComponent<LayoutElement>().flexibleHeight = 1f;
        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(scrollRoot.transform, false);
        viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
        Stretch(viewport.GetComponent<RectTransform>());
        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f); contentRect.anchorMax = Vector2.one;
        contentRect.pivot = new Vector2(0.5f, 1f); contentRect.offsetMin = contentRect.offsetMax = Vector2.zero;
        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 6, 6);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ScrollRect scroll = scrollRoot.GetComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = contentRect;
        scroll.horizontal = false;
        return contentRect;
    }

    private static Button NavigationButton(Transform parent, string label, TMP_FontAsset font)
    {
        Button button = CreateButton(parent, label, font);
        LayoutElement element = button.GetComponent<LayoutElement>();
        element.preferredHeight = 78f;
        element.minHeight = 64f;
        return button;
    }

    private static Button CreateTabButton(Transform parent, string label, TMP_FontAsset font)
    {
        Button button = CreateButton(parent, label, font);
        ConfigureCompactTabButton(button);
        return button;
    }

    private static void ConfigureCompactTabBar(GameObject root)
    {
        HorizontalLayoutGroup layout = root.GetComponent<HorizontalLayoutGroup>() ??
                                       root.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = UIComponentStyles.CompactTabSpacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        LayoutElement size = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
        size.minHeight = UIComponentStyles.CompactTabBarHeight;
        size.preferredHeight = UIComponentStyles.CompactTabBarHeight;
        size.flexibleHeight = 0f;
    }

    private static void ConfigureCompactTabButton(Button button)
    {
        LayoutElement size = button.GetComponent<LayoutElement>();
        size.minWidth = UIComponentStyles.CompactTabButtonWidth;
        size.preferredWidth = UIComponentStyles.CompactTabButtonWidth;
        size.flexibleWidth = 0f;
        size.minHeight = UIComponentStyles.CompactTabBarHeight;
        size.preferredHeight = UIComponentStyles.CompactTabBarHeight;
        size.flexibleHeight = 0f;
    }

    private static Button CreateButton(Transform parent, string label, TMP_FontAsset font)
    {
        GameObject go = new GameObject(label.Replace("\n", string.Empty), typeof(RectTransform), typeof(Image),
            typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.075f, 0.125f, 0.095f, 0.98f);
        go.GetComponent<LayoutElement>().preferredHeight = 42f;
        TMP_Text text = CreateText(go.transform, label, 16, font);
        text.alignment = TextAlignmentOptions.Center;
        Stretch(text.rectTransform);
        return go.GetComponent<Button>();
    }

    private static TMP_Text CreateText(Transform parent, string value, float size, TMP_FontAsset font, float height = 36f)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        TMP_Text text = go.GetComponent<TMP_Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.color = new Color(0.91f, 0.92f, 0.84f, 1f);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        go.GetComponent<LayoutElement>().preferredHeight = height;
        return text;
    }

    private static GameObject Panel(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        if (parent != null) go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        Stretch(go.GetComponent<RectTransform>());
        return go;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
#endif
