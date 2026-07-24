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
    private Button launcher;
    private GameObject blocker;
    private readonly HashSet<string> selectedCandidateIds = new HashSet<string>();
    private int candidatePage;
    private string pendingMissionId;

    private static readonly string[] RepairMissionIds =
    {
        "founding_repair_spirit_array",
        "founding_repair_protection_array",
        "founding_repair_inheritance_chamber",
        "founding_repair_storage_chamber"
    };

    private static readonly string[] LaborMissionIds =
    {
        "founding_labor_gather",
        "founding_labor_build",
        "founding_labor_cultivate"
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
            panel.gameObject.SetActive(true);
            Refresh();
            return;
        }
        FoundingState state = PlayerManager.Instance?.playerData?.founding;
        bool mandatory = state != null && !state.completed &&
                         (state.stage == FoundingStage.CandidateSelection || state.stage == FoundingStage.TechniqueSelection);
        blocker.SetActive(mandatory);
        launcher.gameObject.SetActive(state != null && !mandatory);
        if (mandatory)
        {
            panel.gameObject.SetActive(true);
            Refresh();
        }
    }

    private void Open()
    {
        pendingMissionId = null;
        panel.gameObject.SetActive(true);
        Refresh();
    }

    private void Refresh()
    {
        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
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
        RuntimeUIFactory.Text(content, $"已选择 {selectedCandidateIds.Count}/3　候选第 {candidatePage + 1}/2 页", 20, 38);

        foreach (FounderCandidateData candidate in state.candidates.Skip(candidatePage * 5).Take(5))
        {
            string personality = TraitDatabase.Instance?.Get(candidate.personalityTraitId)?.displayName ?? candidate.personalityTraitId;
            FoundingFeatureDefinition feature = FoundingRules.GetFeature(candidate.initialFeatureId);
            string marker = selectedCandidateIds.Contains(candidate.candidateId) ? "【已选】" : string.Empty;
            Button button = RuntimeUIFactory.Button(content,
                $"{marker}{candidate.displayName}　{candidate.age}岁　资质：{FoundingRules.AptitudeName(candidate.aptitudeRank)}\n" +
                $"攻{candidate.attack} 智{candidate.intelligence} 敏{candidate.agility} 悟{candidate.comprehension} 体{candidate.physique}　性格：{personality}　特点：{feature?.name ?? candidate.initialFeatureId}",
                68);
            button.onClick.AddListener(() =>
            {
                if (!selectedCandidateIds.Remove(candidate.candidateId) && selectedCandidateIds.Count < 3)
                    selectedCandidateIds.Add(candidate.candidateId);
                Refresh();
            });
        }

        Button page = RuntimeUIFactory.Button(content, candidatePage == 0 ? "查看后五人" : "查看前五人", 40);
        page.onClick.AddListener(() => { candidatePage = 1 - candidatePage; Refresh(); });
        Button confirm = RuntimeUIFactory.Button(content, selectedCandidateIds.Count == 3 ? "确认三名核心弟子" : "请选择三名弟子", 46);
        confirm.interactable = selectedCandidateIds.Count == 3;
        confirm.onClick.AddListener(() =>
        {
            if (!PlayerManager.Instance.ConfirmFounderSelection(selectedCandidateIds, out string reason))
                Debug.LogWarning(reason);
            Refresh();
        });
    }

    private void ShowTechniques()
    {
        RuntimeUIFactory.Text(content, "选择古修传承", 32, 50);
        RuntimeUIFactory.Text(content, "洞府石壁中留下三道完整传承。此选择决定宗门最初的发展方向，确认后不可更改。", 19, 62);
        foreach (FoundingTechniqueDefinition technique in FoundingRules.Catalog.techniques)
        {
            Button button = RuntimeUIFactory.Button(content, $"{technique.name}\n{technique.description}", 70);
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
        RuntimeUIFactory.Text(content,
            state.completed
                ? $"三名弟子已立下宗门根基。传承：{technique?.name ?? "旧档传承"}。"
                : $"传承：{technique?.name}　理解度 {state.techniqueUnderstanding}%\n洞府修复、参悟传承和接触青石村可以同时推进。",
            19, 66);

        if (!state.completed)
        {
            RuntimeUIFactory.Text(content, "洞府遗迹", 24, 38);
            foreach (string missionId in RepairMissionIds) AddMissionRow(missionId);

            VillageState village = state.village ?? new VillageState();
            RuntimeUIFactory.Text(content, "青石村", 24, 38);
            RuntimeUIFactory.Text(content,
                $"人口 {village.population}　关系 {village.relation}/100（{VillageRelationName(village.relation)}）　" +
                $"劳动力 {village.totalLabor - village.reservedLabor}/{village.totalLabor}",
                18, 42);
            AddMissionRow("founding_village_preach");
            AddMissionRow("founding_village_help");

            RuntimeUIFactory.Text(content, "宗门路线", 24, 38);
            if (technique != null)
            {
                if (state.techniqueUnderstanding < FoundingRules.MaxUnderstanding)
                    RuntimeUIFactory.Text(content, $"理解达到100%后可建设路线设施；当前 {state.techniqueUnderstanding}%。", 18, 38);
                else
                    AddMissionRow(technique.buildMissionId);
            }
            RuntimeUIFactory.Text(content, "宗门建设", 24, 38);
            if ((state.village?.totalLabor ?? 0) <= 0)
                RuntimeUIFactory.Text(content, "需要青石村信赖后才能调配凡人劳动力。", 18, 38);
            foreach (string missionId in LaborMissionIds) AddLaborMissionRow(missionId);
        }

        if (technique != null && PlayerManager.Instance.GetFacilityLevel(technique.unlockFacility) > 0)
        {
            RuntimeUIFactory.Text(content, "路线行动", 24, 38);
            AddMissionRow(technique.actionMissionId);
        }

        if (!string.IsNullOrEmpty(pendingMissionId)) AddNpcChoices(pendingMissionId);
        Button close = RuntimeUIFactory.Button(content, "收起", 42);
        close.onClick.AddListener(() => panel.gameObject.SetActive(false));
    }

    private void AddMissionRow(string missionId)
    {
        MissionData data = MissionManager.Instance?.GetMissionData(missionId);
        if (data == null)
        {
            RuntimeUIFactory.Text(content, $"任务配置缺失：{missionId}", 17, 34);
            return;
        }

        Mission running = MissionManager.Instance.GetActiveMissions().FirstOrDefault(item =>
            item.Data.id == missionId && (item.State == MissionState.Active || item.State == MissionState.WaitingNode));
        if (running != null)
        {
            RuntimeUIFactory.Text(content,
                $"{data.name}｜{running.AssignedNPC?.Character.displayName}｜剩余 {running.RemainingDays} 天",
                18, 38);
            return;
        }

        if (data.foundingAction == FoundingActionKind.RepairFacility &&
            System.Enum.TryParse(data.foundingTargetId, out FacilityType facility) &&
            PlayerManager.Instance.GetFacilityLevel(facility) > 0)
        {
            RuntimeUIFactory.Text(content, $"{data.name}｜已完成", 18, 36);
            return;
        }

        Button button = RuntimeUIFactory.Button(content, $"{data.name}（{data.needDays}天）", 40);
        button.onClick.AddListener(() => { pendingMissionId = missionId; Refresh(); });
    }

    private void AddLaborMissionRow(string missionId)
    {
        MissionData data = MissionManager.Instance?.GetMissionData(missionId);
        if (data == null)
        {
            RuntimeUIFactory.Text(content, $"任务配置缺失：{missionId}", 17, 34);
            return;
        }

        Mission running = MissionManager.Instance.GetActiveMissions().FirstOrDefault(item =>
            item.Data.id == missionId && (item.State == MissionState.Active || item.State == MissionState.WaitingNode));
        if (running != null)
        {
            RuntimeUIFactory.Text(content, $"{data.name} - 剩余 {running.RemainingDays} 天", 18, 38);
            return;
        }

        bool canStart = MissionManager.Instance.CanTriggerLaborMission(missionId, out string reason);
        Button button = RuntimeUIFactory.Button(content,
            canStart ? $"{data.name}（{data.needDays}天）" : $"{data.name}（{reason}）",
            40);
        button.interactable = canStart;
        button.onClick.AddListener(() =>
        {
            MissionManager.Instance.TriggerLaborMission(missionId);
            Refresh();
        });
    }

    private void AddNpcChoices(string missionId)
    {
        MissionData data = MissionManager.Instance.GetMissionData(missionId);
        RuntimeUIFactory.Text(content, $"为“{data?.name ?? missionId}”选择弟子：", 19, 38);
        bool added = false;
        foreach (NPCRuntime npc in NPCManager.Instance.GetLivingNPC())
        {
            if (!npc.CanDispatch()) continue;
            added = true;
            bool canStart = MissionManager.Instance.CanTriggerMission(missionId, npc, out string reason);
            Button button = RuntimeUIFactory.Button(content,
                canStart ? npc.Character.displayName : $"{npc.Character.displayName}（{reason}）", 38);
            button.interactable = canStart;
            button.onClick.AddListener(() =>
            {
                MissionManager.Instance.TriggerMission(missionId, npc);
                pendingMissionId = null;
                Refresh();
            });
        }
        if (!added) RuntimeUIFactory.Text(content, "暂无空闲弟子。", 18, 36);
        Button cancel = RuntimeUIFactory.Button(content, "取消选择", 36);
        cancel.onClick.AddListener(() => { pendingMissionId = null; Refresh(); });
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
