using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public sealed class GlobalUICoreTests
{
    private GameObject managerObject;
    private UIManager manager;

    [SetUp]
    public void SetUp()
    {
        UIManager.Instance = null;
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
        Assert.That(registrations.arraySize, Is.EqualTo(1));
        Assert.That(registrations.GetArrayElementAtIndex(0).FindPropertyRelative("prefab").objectReferenceValue,
            Is.Not.Null);
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

        Transform currentPlan = FindChild(center.transform, "本月计划Card");
        TMP_Text planText = currentPlan.GetComponentsInChildren<TMP_Text>(true).Last();
        Assert.That(currentPlan.GetComponent<LayoutElement>().preferredHeight, Is.GreaterThanOrEqualTo(180f));
        Assert.That(planText.overflowMode, Is.EqualTo(TextOverflowModes.Ellipsis));
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
        npc.Character.techniqueMastery = 14f;
        npc.Character.AddLifeRecord(2, "Recruit", "加入宗门");
        string before = JsonUtility.ToJson(npc.Character);

        DiscipleCenterSnapshot snapshot = DiscipleCenterSnapshotBuilder.Build(
            new[] { npc }, npc.CharacterId, 3);

        Assert.That(snapshot.selectedCharacterId, Is.EqualTo(npc.CharacterId));
        Assert.That(snapshot.overview, Does.Contain("32.5%"));
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
}
