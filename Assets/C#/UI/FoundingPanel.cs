using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FoundingPanel : MonoBehaviour
{
    private RectTransform panel;
    private RectTransform content;
    private RectTransform candidateTabs;
    private RectTransform candidateFooter;
    private TMP_Text candidateSelectionText;
    private Button candidateConfirmButton;
    private Button launcher;
    private GameObject blocker;
    private readonly HashSet<string> selectedCandidateIds = new HashSet<string>();
    private int candidatePage;

    private static readonly string[] RepairMissionIds =
    {
        "founding_repair_spirit_array",
        "founding_repair_protection_array",
        "founding_repair_inheritance_chamber",
        "founding_repair_storage_chamber"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<FoundingPanel>() == null)
            new GameObject("FoundingPanel").AddComponent<FoundingPanel>();
    }

    private void Awake()
    {
        RuntimeUIFactory.Canvas(gameObject, 900);
        blocker = new GameObject("Blocker", typeof(RectTransform), typeof(Image));
        blocker.transform.SetParent(transform, false);
        RectTransform blockerRect = blocker.GetComponent<RectTransform>();
        blockerRect.anchorMin = Vector2.zero;
        blockerRect.anchorMax = Vector2.one;
        blockerRect.offsetMin = blockerRect.offsetMax = Vector2.zero;
        blocker.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        launcher = RuntimeUIFactory.Button(transform, "洞府立宗");
        RectTransform launcherRect = launcher.GetComponent<RectTransform>();
        launcherRect.anchorMin = launcherRect.anchorMax = new Vector2(1, 0.5f);
        launcherRect.pivot = new Vector2(1, 0.5f);
        launcherRect.anchoredPosition = new Vector2(-15, 0);
        launcherRect.sizeDelta = new Vector2(150, 45);
        launcher.onClick.AddListener(Open);

        panel = RuntimeUIFactory.Panel(transform, "Founding", new Vector2(0.08f, 0.05f), new Vector2(0.92f, 0.95f));
        content = CreateScrollContent(panel);
        candidateTabs = RuntimeUIFactory.TabBar(content, "CandidateTabs");
        AddCandidateTab(candidateTabs, "前五名", 0);
        AddCandidateTab(candidateTabs, "后五名", 1);
        candidateTabs.gameObject.SetActive(false);
        candidateFooter = CreateCandidateFooter(panel);
        candidateSelectionText = RuntimeUIFactory.Text(candidateFooter, string.Empty, 20, 38);
        candidateConfirmButton = RuntimeUIFactory.Button(candidateFooter, string.Empty, 46);
        candidateConfirmButton.onClick.AddListener(ConfirmCandidateSelection);
        candidateFooter.gameObject.SetActive(false);
        panel.gameObject.SetActive(false);
        launcher.gameObject.SetActive(false);
    }

    private IEnumerator Start()
    {
        while (SaveManager.Instance == null || !SaveManager.Instance.IsInitializationComplete)
            yield return null;

        if (PlayerManager.Instance != null) PlayerManager.Instance.OnFoundingChanged += OnFoundingChanged;
        RefreshVisibility();
    }

    private void OnDestroy()
    {
        if (PlayerManager.Instance != null) PlayerManager.Instance.OnFoundingChanged -= OnFoundingChanged;
    }

    private void OnFoundingChanged()
    {
        RefreshVisibility();
        if (panel.gameObject.activeSelf) Refresh();
    }

    private void RefreshVisibility()
    {
        if (SaveManager.Instance != null && SaveManager.Instance.InitializationFailed)
        {
            blocker.SetActive(true);
            launcher.gameObject.SetActive(false);
            OpenManagedPanel();
            Refresh();
            return;
        }
        FoundingState state = PlayerManager.Instance?.playerData?.founding;
        bool mandatory = state != null && !state.completed &&
                         (state.stage == FoundingStage.CandidateSelection || state.stage == FoundingStage.TechniqueSelection);
        blocker.SetActive(mandatory);
        launcher.gameObject.SetActive(state != null && state.stage != FoundingStage.WorldSelection && !mandatory);
        if (mandatory)
        {
            OpenManagedPanel();
            Refresh();
        }
    }

    private void Open()
    {
        OpenManagedPanel();
        Refresh();
    }

    private void Refresh()
    {
        ClearContentDynamicChildren();
        HideCandidateChrome();
        if (SaveManager.Instance != null && SaveManager.Instance.InitializationFailed)
        {
            RuntimeUIFactory.Text(content, "存档读取失败", 30, 50);
            RuntimeUIFactory.Text(content, "为避免覆盖原存档，未创建新游戏。请检查日志和存档文件。", 20, 70);
            return;
        }

        FoundingState state = PlayerManager.Instance?.playerData?.founding;
        if (state == null)
        {
            RuntimeUIFactory.Text(content, "立宗系统尚未初始化。", 22, 48);
            return;
        }

        if (state.stage == FoundingStage.CandidateSelection) ShowCandidates(state);
        else if (state.stage == FoundingStage.TechniqueSelection) ShowTechniques();
        else ShowCave(state);
    }

    private void ShowCandidates(FoundingState state)
    {
        RuntimeUIFactory.Text(content, "三个少年与古修洞府", 32, 50);
        RuntimeUIFactory.Text(content, "十名同行少年在山中发现一座破败洞府。选择三人作为最初的核心弟子。", 19, 60);
        candidateTabs.gameObject.SetActive(true);
        candidateFooter.gameObject.SetActive(true);
        candidateSelectionText.text = $"已选择 {selectedCandidateIds.Count}/3";
        candidateConfirmButton.GetComponentInChildren<TMP_Text>().text =
            selectedCandidateIds.Count == 3 ? "确认三名核心弟子" : "请选择三名弟子";
        candidateConfirmButton.interactable = selectedCandidateIds.Count == 3;
        RefreshCandidateTabs();

        foreach (FounderCandidateData candidate in state.candidates.Skip(candidatePage * 5).Take(5))
        {
            string personality = TraitDatabase.Instance?.Get(candidate.personalityTraitId)?.displayName ?? candidate.personalityTraitId;
            FoundingFeatureDefinition feature = FoundingRules.GetFeature(candidate.initialFeatureId);
            string marker = selectedCandidateIds.Contains(candidate.candidateId) ? "【已选】" : string.Empty;
            Button button = RuntimeUIFactory.Button(content,
                $"{marker}{candidate.displayName}　{candidate.age}岁　资质：{FoundingRules.AptitudeName(candidate.aptitudeRank)}\n" +
                $"战力 {CharacterCapabilityRules.CalculateCandidateCombatPower(candidate)}　力量{candidate.attack} 智{candidate.intelligence} 敏{candidate.agility} 体{candidate.physique}\n" +
                $"悟{candidate.comprehension}　战斗悟性{candidate.combatComprehension}　性格：{personality}　特点：{feature?.name ?? candidate.initialFeatureId}",
                68);
            button.onClick.AddListener(() =>
            {
                if (!selectedCandidateIds.Remove(candidate.candidateId) && selectedCandidateIds.Count < 3)
                    selectedCandidateIds.Add(candidate.candidateId);
                Refresh();
            });
        }

    }

    private void AddCandidateTab(Transform tabs, string label, int page)
    {
        Button button = RuntimeUIFactory.TabButton(tabs, label, candidatePage == page);
        button.onClick.AddListener(() =>
        {
            if (candidatePage == page) return;
            candidatePage = page;
            Refresh();
        });
    }

    private void ConfirmCandidateSelection()
    {
        if (PlayerManager.Instance == null) return;
        if (!PlayerManager.Instance.ConfirmFounderSelection(selectedCandidateIds, out string reason))
            Debug.LogWarning(reason);
        Refresh();
    }

    private void RefreshCandidateTabs()
    {
        if (candidateTabs == null) return;
        for (int i = 0; i < candidateTabs.childCount; i++)
        {
            Button button = candidateTabs.GetChild(i).GetComponent<Button>();
            if (button == null) continue;
            button.GetComponent<Image>().color = i == candidatePage
                ? new Color(0.55f, 0.36f, 0.13f, 1f)
                : new Color(0.20f, 0.17f, 0.13f, 1f);
        }
    }

    private void HideCandidateChrome()
    {
        if (candidateTabs != null) candidateTabs.gameObject.SetActive(false);
        if (candidateFooter != null) candidateFooter.gameObject.SetActive(false);
    }

    private void ClearContentDynamicChildren()
    {
        if (content == null) return;
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            if (content.GetChild(i) == candidateTabs) continue;
            Destroy(content.GetChild(i).gameObject);
        }
    }

    private static RectTransform CreateCandidateFooter(Transform parent)
    {
        GameObject obj = new GameObject("CandidateFooter", typeof(RectTransform), typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        VerticalLayoutGroup layout = obj.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 5;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        obj.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        LayoutElement element = obj.GetComponent<LayoutElement>();
        element.flexibleHeight = 0;
        return obj.GetComponent<RectTransform>();
    }

    private void ShowTechniques()
    {
        RuntimeUIFactory.Text(content, "选择古修传承", 32, 50);
        RuntimeUIFactory.Text(content, "洞府石壁中留下三道完整传承。此选择决定宗门最初的发展方向，确认后不可更改。", 19, 62);
        foreach (FoundingTechniqueDefinition technique in FoundingRules.Catalog.techniques)
        {
            string tags = string.Join("、", (technique.tags ?? new List<string>()).Select(FoundingRules.TechniqueTagName));
            Button button = RuntimeUIFactory.Button(content, $"{technique.name}\n{technique.description}\n标签：{tags}", 86);
            button.onClick.AddListener(() =>
            {
                if (!PlayerManager.Instance.SelectFoundingTechnique(technique.id, out string reason))
                    Debug.LogWarning(reason);
                Refresh();
            });
        }
    }

    private void ShowCave(FoundingState state)
    {
        FoundingTechniqueDefinition technique = FoundingRules.GetTechnique(state.selectedTechniqueId);
        RuntimeUIFactory.Text(content, state.completed ? "宗门初立" : "破败洞府", 32, 50);
        string tags = technique == null ? "无" : string.Join("、", (technique.tags ?? new List<string>()).Select(FoundingRules.TechniqueTagName));
        string effects = technique == null
            ? "无"
            : string.Join("、", (technique.effects ?? new List<TechniqueEffectDefinition>())
                .Where(effect => effect != null && state.techniqueUnderstanding >= effect.requiredUnderstanding)
                .Select(FoundingRules.TechniqueEffectDescription));
        if (string.IsNullOrEmpty(effects)) effects = "尚未解锁";
        RuntimeUIFactory.Text(content,
            $"传承：{technique?.name ?? "旧档传承"}　理解度 {state.techniqueUnderstanding}%\n标签：{tags}\n已解锁：{effects}\n所有行动请前往“宗门事务”。",
            19, 102);

        RuntimeUIFactory.Text(content, "核心弟子", 24, 38);
        foreach (NPCRuntime npc in NPCManager.Instance?.GetAllNPC() ?? new List<NPCRuntime>())
            RuntimeUIFactory.Text(content, $"{npc.Character.displayName}　战力 {npc.CombatPower}　{npc.State}", 18, 34);

        RuntimeUIFactory.Text(content, "洞府设施", 24, 38);
        foreach (string missionId in RepairMissionIds)
        {
            MissionData repair = MissionManager.Instance?.GetMissionData(missionId);
            if (repair == null || !System.Enum.TryParse(repair.foundingTargetId, out FacilityType facility)) continue;
            bool repairing = MissionManager.Instance.GetActiveMissions().Any(item => item.Data.id == missionId &&
                (item.State == MissionState.Active || item.State == MissionState.WaitingNode));
            string status = PlayerManager.Instance.GetFacilityLevel(facility) > 0 ? "已修复" : repairing ? "修复中" : "损坏";
            RuntimeUIFactory.Text(content, $"{repair.name}：{status}", 18, 34);
        }

        VillageState village = state.village ?? new VillageState();
        RuntimeUIFactory.Text(content, "青石村", 24, 38);
        RuntimeUIFactory.Text(content,
            $"人口 {village.population}　关系 {village.relation}/100（{VillageRelationName(village.relation)}）　劳动力 {village.totalLabor - village.reservedLabor}/{village.totalLabor}",
            18, 42);
        RuntimeUIFactory.Text(content, state.completed ? "立宗完成：常规任务已由宗门事务按声望开放。" :
            "完成修复、功法理解与路线建设后，即可建立宗门。", 18, 42);
        Button close = RuntimeUIFactory.Button(content, "收起", 42);
        close.onClick.AddListener(CloseManagedPanel);
    }

    private void OpenManagedPanel()
    {
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(panel.gameObject, CloseInternal);
        else panel.gameObject.SetActive(true);
    }

    private void CloseManagedPanel()
    {
        if (UIManager.Instance != null) UIManager.Instance.ClosePanel(panel.gameObject);
        else CloseInternal();
    }

    private void CloseInternal()
    {
        if (SaveManager.Instance != null && SaveManager.Instance.InitializationFailed)
        {
            blocker.SetActive(true);
            launcher.gameObject.SetActive(false);
            if (UIManager.Instance != null) UIManager.Instance.OpenPanel(panel.gameObject, CloseInternal);
            else panel.gameObject.SetActive(true);
            return;
        }
        panel.gameObject.SetActive(false);
        if (PlayerManager.Instance?.playerData?.founding != null)
        {
            blocker.SetActive(false);
            launcher.gameObject.SetActive(true);
        }
    }

    private static string VillageRelationName(int relation)
    {
        if (relation >= FoundingRules.VillageSupportRelation) return "信赖";
        if (relation >= FoundingRules.VillageFamiliarRelation) return "熟悉";
        return "陌生";
    }

    private static RectTransform CreateScrollContent(RectTransform parent)
    {
        GameObject scrollObject = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        scrollObject.transform.SetParent(parent, false);
        scrollObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.15f);
        scrollObject.GetComponent<LayoutElement>().flexibleHeight = 1;

        GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewportObject.transform.SetParent(scrollObject.transform, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = viewport.offsetMax = Vector2.zero;
        viewportObject.GetComponent<Image>().color = new Color(0, 0, 0, 0.01f);
        viewportObject.GetComponent<Mask>().showMaskGraphic = false;

        GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewport, false);
        RectTransform result = contentObject.GetComponent<RectTransform>();
        result.anchorMin = new Vector2(0, 1);
        result.anchorMax = new Vector2(1, 1);
        result.pivot = new Vector2(0.5f, 1);
        result.offsetMin = result.offsetMax = Vector2.zero;
        VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.spacing = 7;
        layout.childForceExpandHeight = false;
        contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = result;
        scroll.horizontal = false;
        scroll.vertical = true;
        return result;
    }
}
