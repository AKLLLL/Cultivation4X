using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed class SectWorldInterface : MonoBehaviour
{
    public static SectWorldInterface Instance { get; private set; }

    private RectTransform briefPanel;
    private RectTransform layoutPanel;
    private RectTransform taskPanel;
    private RectTransform summaryPanel;
    private Canvas canvas;
    private RectTransform sectManagerContent;
    private RectTransform sectManagerLeftColumn;
    private RectTransform sectManagerRightColumn;
    private bool sectManagerNextRight;
    private readonly List<Button> sectManagerTabButtons = new List<Button>();
    private int sectManagerTabIndex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MapTestBootstrap.IsTestScene) return;
        if (FindObjectOfType<SectWorldInterface>() == null)
            new GameObject("SectWorldInterface").AddComponent<SectWorldInterface>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Canvas canvas = RuntimeUIFactory.Canvas(gameObject, 930);
        this.canvas = canvas;
        briefPanel = CreatePanel(canvas.transform, "SectBrief",
            new Vector2(0.18f, 0.10f), new Vector2(0.82f, 0.90f));
        layoutPanel = CreatePanel(canvas.transform, "SectLayout",
            new Vector2(0.18f, 0.10f), new Vector2(0.82f, 0.90f));
        taskPanel = CreatePanel(canvas.transform, "StewardHall",
            new Vector2(0.18f, 0.10f), new Vector2(0.82f, 0.90f));
        summaryPanel = CreatePanel(canvas.transform, "SectSummary",
            new Vector2(0.18f, 0.10f), new Vector2(0.82f, 0.90f));
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>宗门世界 UI（顶部资源栏）只应在 GameFlowState.WorldMap 显示。</summary>
    public void SetUiVisible(bool visible)
    {
        if (canvas != null) canvas.gameObject.SetActive(visible);
    }

    public void OpenSectBrief()
    {
        // 宗门简报与宗门管理合并：统一打开分页宗门管理界面。
        OpenSectLayout();
    }

    public void OpenSectLayout()
    {
        PlayerData sect = PlayerManager.Instance?.playerData;
        if (!FoundingRules.HasReachedCave(sect?.founding)) return;
        Clear(layoutPanel);
        sectManagerTabButtons.Clear();
        sectManagerContent = null;
        sectManagerTabIndex = 0;

        RuntimeUIFactory.Text(layoutPanel, $"{sect.sectName} · 宗门管理", 30, 48);
        RectTransform tabBar = RuntimeUIFactory.TabBar(layoutPanel, "SectManagerTabs", 44);
        string[] tabs = { "宗门概况", "弟子", "建设", "资源", "事务" };
        for (int index = 0; index < tabs.Length; index++)
        {
            int captured = index;
            Button button = RuntimeUIFactory.TabButton(tabBar, tabs[index], index == 0);
            button.onClick.AddListener(() => SelectSectManagerTab(captured));
            sectManagerTabButtons.Add(button);
        }
        sectManagerContent = CreateSectManagerContent(layoutPanel);
        ShowSectManagerTab(0);
        AddCloseButton(layoutPanel);
        OpenManaged(layoutPanel);
    }

    private void SelectSectManagerTab(int index)
    {
        if (index < 0 || index >= sectManagerTabButtons.Count || index == sectManagerTabIndex) return;
        sectManagerTabIndex = index;
        for (int i = 0; i < sectManagerTabButtons.Count; i++)
            sectManagerTabButtons[i].GetComponent<Image>().color = i == index
                ? new Color(0.55f, 0.36f, 0.13f, 1f)
                : new Color(0.20f, 0.17f, 0.13f, 1f);
        ShowSectManagerTab(index);
    }

    private void ShowSectManagerTab(int index)
    {
        if (sectManagerContent == null) return;
        bool twoColumns = index != 1;
        RebuildSectManagerColumns(twoColumns);
        switch (index)
        {
            case 0: ShowSectOverview(); break;
            case 1: ShowSectDisciples(); break;
            case 2: ShowSectConstruction(); break;
            case 3: ShowSectResources(); break;
            default: ShowSectAffairs(); break;
        }
    }

    private void ShowSectOverview()
    {
        PlayerData sect = PlayerManager.Instance?.playerData;
        if (sect == null) return;
        WorldMap map = WorldMapSession.Current;
        MapSiteData site = WorldMapProgressRules.GetSectBase(WorldMapSession.Progress);
        if (map?.cells == null || site == null || site.cellIndex < 0 || site.cellIndex >= map.cells.Length)
            return;
        WorldCell cell = map.cells[site.cellIndex];
        WorldMapProgressState progress = WorldMapSession.Progress;
        if (progress == null) return;
        WorldMapInfluenceRules.EnsureCurrent(map, progress);
        int core = progress.cellInfluences.Count(item => item.level == InfluenceLevel.Core);
        int influence = progress.cellInfluences.Count(item => item.level == InfluenceLevel.Influence);
        int outer = progress.cellInfluences.Count(item => item.level == InfluenceLevel.Outer);
        int materials = WarehouseManager.Instance == null
            ? 0
            : WarehouseManager.Instance.GetItemCount(FacilityRules.BasicMaterialId);

        AddSectInfoRow("宗门等级", "初创宗门");
        AddSectInfoRow("位置",
            $"{WorldMapCellDetailsFormatter.LandformLabel(cell.landform)}/" +
            $"{WorldMapCellDetailsFormatter.BiomeLabel(cell.biome)}");
        AddSectInfoRow("灵气",
            $"{WorldMapCellDetailsFormatter.AuraBand(cell.totalAura)} ({cell.totalAura:0.000})");
        AddSectInfoRow("弟子", LivingDiscipleCount().ToString());
        AddSectInfoRow("灵石", (WarehouseManager.Instance?.GetItemCount(FacilityRules.SpiritStoneId) ?? 0).ToString());
        AddSectInfoRow("基础材料", materials.ToString());
        AddSectInfoRow("声望", sect.reputation.ToString());
        AddSectInfoRow("影响范围", $"核心{core}　影响{influence}　外缘{outer}");
    }

    private void ShowSectDisciples()
    {
        List<NPCRuntime> npcs = NPCManager.Instance == null
            ? new List<NPCRuntime>()
            : NPCManager.Instance.GetAllNPC();
        if (npcs.Count == 0)
        {
            AddSectInfoText("暂无弟子", 15);
            return;
        }
        foreach (NPCRuntime npc in npcs)
        {
            if (npc?.Data == null) continue;
            NPCRuntime captured = npc;
            Button row = AddSectButton(
                $"{npc.Data.npcName}　{RealmLabel(npc)}　状态：{StateLabel(npc.State)}", 48);
            row.onClick.AddListener(() => OpenNpcDetail(captured));
        }
        AddSectInfoText("点击弟子进入已有弟子详情。", 13);
    }

    private void ShowSectConstruction()
    {
        AddSectInfoText("已有建筑", 16);
        AddBuildingRow("藏经阁", PlayerManager.Instance == null
            ? 0 : PlayerManager.Instance.GetFacilityLevel(FacilityType.InheritanceChamber));
        AddBuildingRow("炼丹房", PlayerManager.Instance == null
            ? 0 : PlayerManager.Instance.GetFacilityLevel(FacilityType.AlchemyRoom));
        AddBuildingRow("修炼室", PlayerManager.Instance == null
            ? 0 : PlayerManager.Instance.GetFacilityLevel(FacilityType.TrainingRoom));
        AddBuildingRow("仓库", PlayerManager.Instance == null
            ? 0 : PlayerManager.Instance.GetFacilityLevel(FacilityType.Warehouse));
        AddBuildingRow("任务堂", PlayerManager.Instance == null
            ? 0 : PlayerManager.Instance.GetFacilityLevel(FacilityType.MissionHall));

        AddSectInfoText("可建设", 16);
        Button future = AddSectButton(
            "药园（未开放）\n需要：基础材料 20　灵石 10", 52);
        future.interactable = false;

        Button upgrade = AddSectButton( "设施升级（仓库/修炼室/秘境/炼丹房）", 46);
        upgrade.onClick.AddListener(() => FindRuntime<SectDevelopmentPanel>()?.OpenFromSectLayout());

        Button scripture = AddSectButton( "藏经阁（功法研究）", 44);
        scripture.onClick.AddListener(OpenScriptureSummary);
        Button alchemy = AddSectButton( "炼丹房（炼制丹药）", 44);
        alchemy.onClick.AddListener(() => FindRuntime<AlchemyPanel>()?.OpenFromSectLayout());
        Button training = AddSectButton( "修炼室（安排修炼）", 44);
        training.onClick.AddListener(OpenTrainingSummary);
    }

    private void ShowSectResources()
    {
        AddSectInfoRow("灵石",
            (WarehouseManager.Instance?.GetItemCount(FacilityRules.SpiritStoneId) ?? 0).ToString());
        AddSectInfoRow("基础材料",
            (WarehouseManager.Instance == null
                ? 0 : WarehouseManager.Instance.GetItemCount(FacilityRules.BasicMaterialId)).ToString());
        AddSectInfoRow("仓库容量",
            WarehouseManager.Instance == null
                ? "0/0"
                : $"{WarehouseManager.Instance.GetUsedSlotCount()}/{WarehouseManager.Instance.GetCapacity()}");
        AddSectInfoRow("丹药", CountItemsByType(ItemType.Pill).ToString());
        AddSectInfoRow("法宝", CountItemsByType(ItemType.Weapon).ToString());

        AddSectInfoText("生产", 16);
        AddSectInfoText("药园：未建设", 13);
        AddSectInfoText("炼丹房：通过宗门建设/炼丹房开放", 13);
        Button warehouse = AddSectButton( "打开仓库", 46);
        warehouse.onClick.AddListener(OpenWarehouse);
    }

    private void ShowSectAffairs()
    {
        AddSectInfoText("弟子安排", 16);
        int day = TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay;
        AddSectInfoText($"当前第 {MonthlyPlanRules.MonthIndex(day)} 月；可编排第 {MonthlyPlanRules.EditableMonth(day)} 月。", 13);
        FoundingState founding = PlayerManager.Instance?.playerData?.founding;
        if (founding != null && founding.sectCreated)
        {
            Button monthlyPlan = AddSectButton("弟子月度计划", 46);
            monthlyPlan.onClick.AddListener(() => FindRuntime<MonthlyPlanPanel>()?.OpenFromSectLayout());
        }
        else AddSectInfoText("正式立宗后开放月度计划。", 13);
        Button steward = AddSectButton( "打开任务堂／执事堂", 46);
        steward.onClick.AddListener(OpenStewardHall);
        Button threat = AddSectButton( "外部威胁", 46);
        threat.onClick.AddListener(() => FindRuntime<ExternalThreatPanel>()?.OpenFromSectLayout());
        if (founding != null && founding.stage == FoundingStage.Cave)
        {
            Button foundingButton = AddSectButton( "洞府整备／立宗进度", 46);
            foundingButton.onClick.AddListener(() => FindRuntime<FoundingPanel>()?.OpenFromSectLayout());
        }
    }

    private static RectTransform CreateSectManagerContent(Transform parent)
    {
        GameObject obj = new GameObject("SectManagerContent",
            typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        HorizontalLayoutGroup layout = obj.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 12;
        layout.childForceExpandWidth = true;
        layout.childControlWidth = true;
        LayoutElement element = obj.GetComponent<LayoutElement>();
        element.flexibleHeight = 1f;
        return rect;
    }

    private void RebuildSectManagerColumns(bool twoColumns)
    {
        if (sectManagerContent == null) return;
        Clear(sectManagerContent);
        sectManagerLeftColumn = CreateSectManagerColumn(sectManagerContent);
        sectManagerRightColumn = twoColumns ? CreateSectManagerColumn(sectManagerContent) : null;
        sectManagerNextRight = false;
    }

    private static RectTransform CreateSectManagerColumn(Transform parent)
    {
        GameObject obj = new GameObject("SectManagerColumn",
            typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        VerticalLayoutGroup layout = obj.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 6;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        LayoutElement element = obj.GetComponent<LayoutElement>();
        element.flexibleWidth = 1f;
        element.flexibleHeight = 1f;
        return rect;
    }

    private RectTransform NextSectColumn()
    {
        if (sectManagerRightColumn == null)
            return sectManagerLeftColumn;
        sectManagerNextRight = !sectManagerNextRight;
        return sectManagerNextRight ? sectManagerRightColumn : sectManagerLeftColumn;
    }

    private void AddSectInfoRow(string label, string value)
    {
        RectTransform target = NextSectColumn();
        if (target == null) return;
        string textValue = $"{label}：{value}";
        TMP_Text text = RuntimeUIFactory.Text(target, textValue, 16, 32);
        text.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private void AddSectInfoText(string textValue, int fontSize)
    {
        RectTransform target = NextSectColumn();
        if (target == null) return;
        RuntimeUIFactory.Text(target, textValue, fontSize, 30);
    }

    private void AddBuildingRow(string name, int level)
    {
        RectTransform target = NextSectColumn();
        if (target == null) return;
        RuntimeUIFactory.Text(target,
            level > 0 ? $"{name}　等级{level}" : $"{name}　未建设", 15, 32);
    }

    private Button AddSectButton(string label, float height = 44)
    {
        RectTransform target = NextSectColumn();
        if (target == null) return null;
        return RuntimeUIFactory.Button(target, label, height);
    }

    private int CountItemsByType(ItemType type)
    {
        if (WarehouseManager.Instance == null || WarehouseManager.Instance.warehouseData?.items == null)
            return 0;
        int total = 0;
        foreach (ItemStack stack in WarehouseManager.Instance.warehouseData.items)
        {
            if (stack == null || string.IsNullOrWhiteSpace(stack.itemId)) continue;
            ItemData data = ItemDatabase.Instance == null ? null : ItemDatabase.Instance.GetItem(stack.itemId);
            if (data != null && data.itemType == type) total += stack.count;
        }
        return total;
    }

    private static string RealmLabel(NPCRuntime npc)
    {
        if (npc == null) return "未知";
        switch (npc.Realm)
        {
            case CultivationRealm.Mortal: return "凡人";
            case CultivationRealm.QiRefining: return $"练气{npc.Level}层";
            case CultivationRealm.Foundation: return "筑基";
            case CultivationRealm.GoldenCore: return "金丹";
            default: return npc.Realm.ToString();
        }
    }

    private static string StateLabel(NPCState state)
    {
        switch (state)
        {
            case NPCState.Idle: return "修炼";
            case NPCState.Busy: return "探索";
            case NPCState.Injured: return "受伤";
            case NPCState.ClosedDoor: return "闭关";
            case NPCState.Traveling: return "外出";
            default: return "空闲";
        }
    }

    private static void AddBuildingCard(Transform parent, string title, string description,
        UnityEngine.Events.UnityAction onClick)
    {
        Button card = RuntimeUIFactory.Button(parent, $"{title}\n{description}", 62);
        card.onClick.AddListener(onClick);
        LayoutElement layout = card.GetComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
    }

    private void OpenStewardHall()
    {
        Clear(taskPanel);
        RuntimeUIFactory.Text(taskPanel, "任务堂／执事堂", 30, 48);
        AddEntry(taskPanel, "宗门任务", OpenMissionPanel);
        AddEntry(taskPanel, "外部威胁", () => FindRuntime<ExternalThreatPanel>()?.OpenFromSectLayout());
        AddCloseButton(taskPanel);
        OpenManaged(taskPanel);
    }

    private void OpenTrainingSummary()
    {
        PlayerData sect = PlayerManager.Instance?.playerData;
        int level = PlayerManager.Instance == null
            ? 0
            : PlayerManager.Instance.GetFacilityLevel(FacilityType.TrainingRoom);
        Clear(summaryPanel);
        RuntimeUIFactory.Text(summaryPanel, "修炼室", 30, 48);
        RuntimeUIFactory.Text(summaryPanel,
            $"设施等级：Lv.{level}\n当前效果：纳气效率 x{(level <= 0 ? 0.8f : level == 1 ? 1f : level == 2 ? 1.1f : 1.2f):0.0}\n存活弟子：{LivingDiscipleCount()}",
            19, 86);
        AddEntry(summaryPanel, "查看弟子", OpenSectDisciplesPage);
        AddCloseButton(summaryPanel);
        OpenManaged(summaryPanel);
    }

    private void OpenScriptureSummary()
    {
        FoundingState founding = PlayerManager.Instance?.playerData?.founding;
        FoundingTechniqueDefinition technique = FoundingRules.GetTechnique(founding?.selectedTechniqueId);
        string tags = technique == null
            ? "无"
            : string.Join("、", (technique.tags ?? new System.Collections.Generic.List<string>())
                .Select(FoundingRules.TechniqueTagName));
        string effects = technique == null
            ? "无"
            : string.Join("、", (technique.effects ?? new System.Collections.Generic.List<TechniqueEffectDefinition>())
                .Where(effect => effect != null &&
                                 founding.techniqueUnderstanding >= effect.requiredUnderstanding)
                .Select(FoundingRules.TechniqueEffectDescription));
        if (string.IsNullOrEmpty(effects)) effects = "尚未解锁";
        Clear(summaryPanel);
        RuntimeUIFactory.Text(summaryPanel, "藏经阁", 30, 48);
        RuntimeUIFactory.Text(summaryPanel,
            $"传承：{technique?.name ?? "无"}\n理解度：{founding?.techniqueUnderstanding ?? 0}%\n" +
            $"标签：{tags}\n已生效：{effects}\n\n功法管理尚未开放。",
            19, 132);
        AddCloseButton(summaryPanel);
        OpenManaged(summaryPanel);
    }

    private void OpenNpcDetail(NPCRuntime npc)
    {
        if (npc == null) return;
        if (UIManager.Instance != null)
        {
            UIManager.Instance.OpenWindow(UIWindowId.DiscipleCenter,
                new DiscipleCenterContext(npc.CharacterId));
            return;
        }
        SectPanel sectPanel = Resources.FindObjectsOfTypeAll<SectPanel>()
            .FirstOrDefault(item => item != null && item.gameObject.scene.IsValid());
        if (sectPanel != null && sectPanel.infoPanel != null)
        {
            NPCInfoPanel detail = sectPanel.infoPanel;
            // 旧弟子列表已废弃：只把 SectPanel 当作 NPCInfoPanel 的父容器激活，
            // 并隐藏旧列表内容，避免出现旧文字列表。
            sectPanel.gameObject.SetActive(true);
            if (sectPanel.content != null)
                sectPanel.content.gameObject.SetActive(false);
            if (UIManager.Instance != null)
            {
                UIManager.Instance.OpenPanel(detail.gameObject, () => CloseNpcDetailContainer(sectPanel));
            }
            else
            {
                detail.gameObject.SetActive(true);
            }
            detail.Show(npc);
            return;
        }
        NPCInfoPanel panel = FindObjectOfType<NPCInfoPanel>(true);
        if (panel != null)
        {
            if (UIManager.Instance != null) UIManager.Instance.OpenPanel(panel.gameObject);
            else panel.gameObject.SetActive(true);
            panel.Show(npc);
            return;
        }
        GameDebugConfig.LogWorldMapWarning("未找到可用的弟子详情面板，已取消打开旧弟子列表。");
    }

    private static void CloseNpcDetailContainer(SectPanel sectPanel)
    {
        if (sectPanel == null) return;
        if (UIManager.Instance != null) UIManager.Instance.ClosePanel(sectPanel.gameObject);
        else sectPanel.gameObject.SetActive(false);
    }

    /// <summary>直接打开新版宗门管理的弟子分页。</summary>
    public void OpenSectDisciplesPage()
    {
        if (UIManager.Instance != null && summaryPanel != null)
            UIManager.Instance.ClosePanel(summaryPanel.gameObject);
        OpenSectLayout();
        SelectSectManagerTab(1);
    }

    private void OpenWarehouse() => OpenSceneComponent<WarehousePanel>();
    private void OpenMissionPanel() => OpenSceneComponent<MissionPanel>();

    private static void OpenSceneComponent<T>() where T : MonoBehaviour
    {
        T target = Resources.FindObjectsOfTypeAll<T>()
            .FirstOrDefault(item => item != null && item.gameObject.scene.IsValid());
        if (target == null) { Debug.LogWarning($"{typeof(T).Name} 不存在"); return; }
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(target.gameObject);
        else target.gameObject.SetActive(true);
    }

    private static T FindRuntime<T>() where T : MonoBehaviour =>
        FindObjectOfType<T>() ?? Resources.FindObjectsOfTypeAll<T>().FirstOrDefault();

    private static int LivingDiscipleCount() =>
        NPCManager.Instance?.GetAllNPC().Count(npc => npc?.Character?.IsAlive == true) ?? 0;

    private static RectTransform CreatePanel(Transform canvas, string name, Vector2 min, Vector2 max)
    {
        RectTransform panel = RuntimeUIFactory.Panel(canvas, name, min, max);
        panel.gameObject.SetActive(false);
        return panel;
    }

    private static void AddEntry(Transform parent, string label, UnityEngine.Events.UnityAction action)
    {
        Button button = RuntimeUIFactory.Button(parent, label, 48);
        button.onClick.AddListener(action);
    }

    private static void AddCloseButton(RectTransform panel)
    {
        Button close = RuntimeUIFactory.Button(panel, "返回", 42);
        close.onClick.AddListener(() =>
        {
            if (UIManager.Instance != null) UIManager.Instance.ClosePanel(panel.gameObject);
            else panel.gameObject.SetActive(false);
        });
    }

    private static void OpenManaged(RectTransform panel)
    {
        // 容器面板已打开时不再提升层级，避免盖住其上层的子面板。
        if (panel != null && panel.gameObject.activeSelf)
            return;
        if (UIManager.Instance != null) UIManager.Instance.OpenScreen(panel.gameObject);
        else panel.gameObject.SetActive(true);
    }

    private static void Clear(RectTransform panel)
    {
        for (int i = panel.childCount - 1; i >= 0; i--)
            Destroy(panel.GetChild(i).gameObject);
    }
}
