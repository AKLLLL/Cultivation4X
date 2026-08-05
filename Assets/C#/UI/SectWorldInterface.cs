using System.Collections;
using System.Linq;
using Cultivation4X.WorldMap;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SectWorldInterface : MonoBehaviour
{
    public static SectWorldInterface Instance { get; private set; }

    private TMP_Text resourceText;
    private Button warehouseButton;
    private RectTransform briefPanel;
    private RectTransform layoutPanel;
    private RectTransform taskPanel;
    private RectTransform summaryPanel;
    private string lastResourceText;
    private float nextResourceRefreshTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<SectWorldInterface>() == null)
            new GameObject("SectWorldInterface").AddComponent<SectWorldInterface>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Canvas canvas = RuntimeUIFactory.Canvas(gameObject, 930);
        CreateResourceBar(canvas.transform);
        briefPanel = CreatePanel(canvas.transform, "SectBrief",
            new Vector2(0.18f, 0.10f), new Vector2(0.82f, 0.90f));
        layoutPanel = CreatePanel(canvas.transform, "SectLayout",
            new Vector2(0.18f, 0.10f), new Vector2(0.82f, 0.90f));
        taskPanel = CreatePanel(canvas.transform, "StewardHall",
            new Vector2(0.18f, 0.10f), new Vector2(0.82f, 0.90f));
        summaryPanel = CreatePanel(canvas.transform, "SectSummary",
            new Vector2(0.18f, 0.10f), new Vector2(0.82f, 0.90f));
    }

    private IEnumerator Start()
    {
        while (SaveManager.Instance == null || !SaveManager.Instance.IsInitializationComplete)
            yield return null;
        RefreshResourceBar();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextResourceRefreshTime) return;
        nextResourceRefreshTime = Time.unscaledTime + 0.25f;
        RefreshResourceBar();
    }

    public void OpenSectBrief()
    {
        PlayerData sect = PlayerManager.Instance?.playerData;
        FoundingState founding = sect?.founding;
        WorldMap map = WorldMapSession.Current;
        MapSiteData site = WorldMapProgressRules.GetSectBase(WorldMapSession.Progress);
        if (!FoundingRules.HasReachedCave(founding) || map?.cells == null || site == null ||
            site.cellIndex < 0 || site.cellIndex >= map.cells.Length)
            return;

        Clear(briefPanel);
        WorldCell cell = map.cells[site.cellIndex];
        FoundingTechniqueDefinition technique = FoundingRules.GetTechnique(founding.selectedTechniqueId);
        int disciples = LivingDiscipleCount();
        int materials = WarehouseManager.Instance == null
            ? 0
            : WarehouseManager.Instance.GetItemCount(FacilityRules.BasicMaterialId);
        WorldMapProgressState progress = WorldMapSession.Progress;
        WorldMapInfluenceRules.EnsureCurrent(map, progress);
        int core = progress.cellInfluences.Count(item => item.level == InfluenceLevel.Core);
        int influence = progress.cellInfluences.Count(item => item.level == InfluenceLevel.Influence);
        int outer = progress.cellInfluences.Count(item => item.level == InfluenceLevel.Outer);
        int known = progress.revealedCellIndices.Concat(progress.cellInfluences.Select(item => item.cellIndex))
            .Distinct().Count();
        int activeSources = progress.influenceSources.Count(item => item?.isActive == true);
        int discoveredSites = progress.mapSites.Count(item => item != null &&
            item.siteType != MapSiteType.SectBase && item.revealState == MapContentRevealState.Discovered);
        int exploredCells = progress.exploredCellIndices?.Count ?? 0;
        string developedEffects = string.Join("；", progress.mapSites
            .Where(item => item != null && WorldMapContentEffects.IsSiteDeveloped(item))
            .Select(item => WorldMapContentEffects.EffectSummary(item.siteType))
            .Where(item => !string.IsNullOrEmpty(item)));

        RuntimeUIFactory.Text(briefPanel, sect.sectName, 32, 48);
        RuntimeUIFactory.Text(briefPanel,
            $"坐标 {cell.coord.col},{cell.coord.row}　" +
            $"{WorldMapCellDetailsFormatter.LandformLabel(cell.landform)}/" +
            $"{WorldMapCellDetailsFormatter.BiomeLabel(cell.biome)}\n" +
            $"灵气 {WorldMapCellDetailsFormatter.AuraBand(cell.totalAura)}（{cell.totalAura:0.000}）　" +
            $"弟子 {disciples}　功法 {technique?.name ?? "无"}\n" +
            $"灵材 {sect.gold}　基础材料 {materials}　声望 {sect.reputation}\n" +
            $"影响力：核心 {core}　影响 {influence}　外缘 {outer}　认知并集 {known}　活跃来源 {activeSources}\n" +
            $"地图内容：已探索 {exploredCells}　已发现地点 {discoveredSites}" +
            (string.IsNullOrEmpty(developedEffects) ? string.Empty : $"\n已生效后果：{developedEffects}"),
            19, 104);
        Button enter = RuntimeUIFactory.Button(briefPanel, "进入宗门", 46);
        enter.onClick.AddListener(OpenSectLayout);
        AddCloseButton(briefPanel);
        OpenManaged(briefPanel);
    }

    public void OpenSectLayout()
    {
        PlayerData sect = PlayerManager.Instance?.playerData;
        if (!FoundingRules.HasReachedCave(sect?.founding)) return;
        Clear(layoutPanel);
        RuntimeUIFactory.Text(layoutPanel, $"{sect.sectName} · 宗门布局", 30, 48);
        RuntimeUIFactory.Text(layoutPanel, "内部建筑不占用世界格。", 17, 36);
        RectTransform list = RuntimeUIFactory.ScrollContent(layoutPanel, "LayoutList");
        AddEntry(list, "库藏", OpenWarehouse);
        AddEntry(list, "炼丹房", () => FindRuntime<AlchemyPanel>()?.OpenFromSectLayout());
        AddEntry(list, "修炼室", OpenTrainingSummary);
        AddEntry(list, "藏经阁", OpenScriptureSummary);
        AddEntry(list, "任务堂／执事堂", OpenStewardHall);
        AddEntry(list, "宗门建设", () => FindRuntime<SectDevelopmentPanel>()?.OpenFromSectLayout());
        if (sect.founding.stage == FoundingStage.Cave)
            AddEntry(list, "洞府整备／立宗进度", () => FindRuntime<FoundingPanel>()?.OpenFromSectLayout());
        AddCloseButton(layoutPanel);
        OpenManaged(layoutPanel);
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
            $"设施等级：Lv.{level}\n当前效果：每日修为 +{FacilityRules.TrainingGain(level)}\n存活弟子：{LivingDiscipleCount()}",
            19, 86);
        AddEntry(summaryPanel, "查看弟子", OpenSectPanel);
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

    private void CreateResourceBar(Transform canvas)
    {
        GameObject bar = new GameObject("ResourceBar", typeof(RectTransform), typeof(Image),
            typeof(HorizontalLayoutGroup));
        bar.transform.SetParent(canvas, false);
        RectTransform rect = bar.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.06f, 1f);
        rect.anchorMax = new Vector2(0.72f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -10f);
        rect.sizeDelta = new Vector2(0f, 50f);
        bar.GetComponent<Image>().color = new Color(0.04f, 0.04f, 0.035f, 0.88f);
        HorizontalLayoutGroup layout = bar.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 8, 4, 4);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        resourceText = RuntimeUIFactory.Text(bar.transform, string.Empty, 13, 42);
        resourceText.alignment = TextAlignmentOptions.MidlineLeft;
        resourceText.enableWordWrapping = false;
        resourceText.enableAutoSizing = true;
        resourceText.fontSizeMin = 9f;
        resourceText.fontSizeMax = 13f;
        resourceText.overflowMode = TextOverflowModes.Ellipsis;
        warehouseButton = RuntimeUIFactory.Button(bar.transform, "库藏详情", 38);
        warehouseButton.GetComponent<LayoutElement>().preferredWidth = 110f;
        warehouseButton.GetComponent<LayoutElement>().flexibleWidth = 0f;
        warehouseButton.onClick.AddListener(OpenWarehouse);
        Button endDayButton = RuntimeUIFactory.Button(bar.transform, "结束今天", 38);
        endDayButton.GetComponent<LayoutElement>().preferredWidth = 96f;
        endDayButton.GetComponent<LayoutElement>().flexibleWidth = 0f;
        endDayButton.onClick.AddListener(() => TimeManager.Instance?.EndDay());
    }

    private void RefreshResourceBar()
    {
        PlayerData sect = PlayerManager.Instance?.playerData;
        int materials = WarehouseManager.Instance == null
            ? 0
            : WarehouseManager.Instance.GetItemCount(FacilityRules.BasicMaterialId);
        int day = TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay;
        FoundingState founding = sect?.founding;
        bool established = FoundingRules.HasReachedCave(founding) &&
                           WorldMapProgressRules.GetSectBase(WorldMapSession.Progress) != null;
        WorldMapProgressState progress = WorldMapSession.Progress;
        if (established) WorldMapInfluenceRules.EnsureCurrent(WorldMapSession.Current, progress);
        int influence = established ? progress.cellInfluences.Count : 0;
        string value = $"灵材 {sect?.gold ?? 0} 基础材料 {materials} 弟子 {LivingDiscipleCount()} " +
                       $"声望 {sect?.reputation ?? 0} 影响 {influence}格 第 {day}天";
        if (value != lastResourceText && resourceText != null)
        {
            resourceText.text = value;
            lastResourceText = value;
        }
        if (warehouseButton != null) warehouseButton.interactable = established;
    }

    private void OpenWarehouse() => OpenSceneComponent<WarehousePanel>();
    private void OpenMissionPanel() => OpenSceneComponent<MissionPanel>();
    private void OpenSectPanel() => OpenSceneComponent<SectPanel>();

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
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(panel.gameObject);
        else panel.gameObject.SetActive(true);
    }

    private static void Clear(RectTransform panel)
    {
        for (int i = panel.childCount - 1; i >= 0; i--)
            Destroy(panel.GetChild(i).gameObject);
    }
}
