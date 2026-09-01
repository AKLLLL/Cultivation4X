using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ExternalThreatPanel : MonoBehaviour
{
    private enum ThreatPage
    {
        Intelligence,
        Investigation,
        Participants,
        Plan
    }

    public static ExternalThreatPanel Instance;
    public const string DiscoveryEventId = "qingshi_threat_discovered";
    public const string InspectOptionId = "inspect";
    private Button launcher;
    private RectTransform panel;
    private RectTransform content;
    private TMP_Text status;
    private Button confirm;
    private readonly List<string> selectedIds = new List<string>();
    private CombatPlanType selectedPlan = CombatPlanType.HeadOn;
    private ThreatPage currentPage;
    private readonly Dictionary<ThreatPage, Button> pageButtons = new Dictionary<ThreatPage, Button>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MapTestBootstrap.IsTestScene) return;
        if (FindObjectOfType<ExternalThreatPanel>() == null)
            new GameObject("ExternalThreatPanel").AddComponent<ExternalThreatPanel>();
    }

    private void Awake()
    {
        Instance = this;
        RuntimeUIFactory.Canvas(gameObject, 880);
        launcher = RuntimeUIFactory.Button(transform, "外部威胁");
        RectTransform launchRect = launcher.GetComponent<RectTransform>();
        launchRect.anchorMin = launchRect.anchorMax = new Vector2(0, 1);
        launchRect.pivot = new Vector2(0, 1);
        launchRect.anchoredPosition = new Vector2(175, -15);
        launchRect.sizeDelta = new Vector2(190, 45);
        launcher.onClick.AddListener(Open);

        panel = RuntimeUIFactory.Panel(transform, "ExternalThreat", new Vector2(0.1f, 0.06f), new Vector2(0.9f, 0.94f));
        RuntimeUIFactory.Text(panel, "青石村外部威胁", 30, 48);
        status = RuntimeUIFactory.Text(panel, string.Empty, 17, 48);
        RectTransform tabs = RuntimeUIFactory.TabBar(panel, "ThreatTabs");
        AddPageTab(tabs, ThreatPage.Intelligence, "威胁情报");
        AddPageTab(tabs, ThreatPage.Investigation, "调查");
        AddPageTab(tabs, ThreatPage.Participants, "参战弟子");
        AddPageTab(tabs, ThreatPage.Plan, "处理方案");
        content = RuntimeUIFactory.ScrollContent(panel, "ThreatScroll");
        confirm = RuntimeUIFactory.Button(panel, "确认执行方案", 48);
        confirm.onClick.AddListener(ResolveSelectedPlan);
        Button close = RuntimeUIFactory.Button(panel, "关闭");
        close.onClick.AddListener(Close);
        panel.gameObject.SetActive(false);
        launcher.gameObject.SetActive(false);
    }

    private void Start()
    {
        if (PlayerManager.Instance != null) PlayerManager.Instance.OnFoundingChanged += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        if (PlayerManager.Instance != null) PlayerManager.Instance.OnFoundingChanged -= Refresh;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        ActiveThreatState threat = ExternalThreatRules.GetState();
        bool visible = threat != null &&
            (threat.status == ExternalThreatStatus.Active || threat.status == ExternalThreatStatus.Resolved);
        if (visible)
        {
            launcher.GetComponentInChildren<TMP_Text>().text =
                threat.status == ExternalThreatStatus.Resolved ? "上次威胁记录" : "外部威胁：青石村";
            if (!launcher.gameObject.activeSelf) launcher.gameObject.SetActive(true);
        }
        else if (launcher.gameObject.activeSelf)
        {
            launcher.gameObject.SetActive(false);
        }
    }

    private void Open()
    {
        Refresh();
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(panel.gameObject, CloseInternal);
        else panel.gameObject.SetActive(true);
    }

    public void OpenFromSectLayout() => Open();

    public static bool TryOpenFromEvent(string eventId, string optionId)
    {
        if (eventId != DiscoveryEventId || optionId != InspectOptionId)
            return false;
        ExternalThreatPanel target = Instance;
        if (target == null)
            target = new GameObject("ExternalThreatPanel").AddComponent<ExternalThreatPanel>();
        if (target == null) return false;
        target.Open();
        return true;
    }

    private void Close()
    {
        if (UIManager.Instance != null) UIManager.Instance.ClosePanel(panel.gameObject);
        else CloseInternal();
    }

    private void CloseInternal() => panel.gameObject.SetActive(false);

    private void Refresh()
    {
        if (content == null) return;
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);
        RefreshPageTabs();

        ActiveThreatState threat = ExternalThreatRules.GetState();
        ExternalThreatDefinition definition = threat == null ? null : ExternalThreatRules.GetDefinition(threat.threatId);
        if (threat == null || definition == null)
        {
            RuntimeUIFactory.Text(content, "当前没有已知外部威胁。", 18, 60);
            status.text = string.Empty;
            confirm.gameObject.SetActive(false);
            return;
        }
        if (threat.status == ExternalThreatStatus.Resolved)
        {
            ShowResolution(threat, definition);
            return;
        }
        if (threat.status != ExternalThreatStatus.Active)
        {
            RuntimeUIFactory.Text(content, "村民尚未确认威胁的具体方向。", 18, 60);
            status.text = string.Empty;
            confirm.gameObject.SetActive(false);
            return;
        }

        selectedIds.RemoveAll(id =>
        {
            NPCRuntime npc = NPCManager.Instance?.GetRuntime(id);
            return npc == null || !npc.CanDispatch();
        });
        bool investigating = ExternalThreatRules.IsInvestigationRunning();
        status.text = investigating ? "调查正在进行；仍可直接选择处理方案，当前情报不足会带来风险。" :
            threat.intelligence >= 100 ? "情报完整，可查看确定性结果预览。" : "可继续调查，也可承担未知风险立即应对。";
        confirm.gameObject.SetActive(false);

        if (currentPage == ThreatPage.Intelligence)
            ShowIntelligence(threat, definition);
        else if (currentPage == ThreatPage.Investigation)
            ShowInvestigation(threat, definition, investigating);
        else if (currentPage == ThreatPage.Participants)
            ShowParticipants();
        else
            ShowPlans(threat, definition);
    }

    private void ShowIntelligence(ActiveThreatState threat, ExternalThreatDefinition definition)
    {
        RuntimeUIFactory.Text(content, BuildThreatDetails(threat, definition), 18, 180);
        RuntimeUIFactory.Text(content, definition.description, 18, 80);
    }

    private void ShowInvestigation(ActiveThreatState threat, ExternalThreatDefinition definition, bool investigating)
    {
        RuntimeUIFactory.Text(content, "调查威胁（2天，仅允许一项）", 20, 36);
        foreach (NPCRuntime npc in IdleLivingNpcs())
        {
            NPCRuntime captured = npc;
            Button investigate = RuntimeUIFactory.Button(content,
                $"派遣 {Name(npc)} 调查　智力 {npc.Intelligence}　预计情报 +{Mathf.Clamp(10 + npc.Intelligence, 15, 30)}", 44);
            investigate.interactable = !investigating && threat.intelligence < 100;
            investigate.onClick.AddListener(() =>
            {
                MissionManager.Instance?.TriggerMission(definition.investigationMissionId, captured);
                Refresh();
            });
        }
        if (!IdleLivingNpcs().Any())
            RuntimeUIFactory.Text(content, "暂无可派遣调查的弟子。", 18, 42);
    }

    private void ShowParticipants()
    {
        RuntimeUIFactory.Text(content, $"参战弟子（已选 {selectedIds.Count}/3）", 20, 36);
        foreach (NPCRuntime npc in IdleLivingNpcs())
        {
            NPCRuntime captured = npc;
            bool selected = selectedIds.Contains(npc.CharacterId);
            Button participant = RuntimeUIFactory.Button(content,
                $"{(selected ? "●" : "○")} {Name(npc)}　战力 {CharacterCapabilityRules.CalculateCombatPower(npc)}", 44);
            participant.onClick.AddListener(() => ToggleParticipant(captured.CharacterId));
        }
        if (!IdleLivingNpcs().Any())
            RuntimeUIFactory.Text(content, "暂无可参战弟子。", 18, 42);
    }

    private void ShowPlans(ActiveThreatState threat, ExternalThreatDefinition definition)
    {
        RuntimeUIFactory.Text(content, "处理方案", 20, 36);
        AddPlanButton(CombatPlanType.HeadOn, "正面迎击｜准备修正 1.00");
        AddPlanButton(CombatPlanType.SimpleDefense,
            $"简单防御｜消耗基础材料×{definition.defenseMaterialCost}");
        AddPlanButton(CombatPlanType.RetreatToCave, "退守洞府｜人口-10 / 劳动力-20 / 关系-20");

        if (threat.intelligence >= 100)
        {
            CombatResolution preview = ExternalThreatRules.Preview(SelectedNpcs(), selectedPlan);
            string previewText = selectedPlan == CombatPlanType.RetreatToCave ? "预览：退守洞府，不进行战斗。" :
                preview == null ? "预览：请先选择1至3名弟子。" :
                $"确定性预览：队伍战力 {preview.partyPower}，第一次交手比值 {preview.firstExchangeRatio:0.00}，" +
                $"最终比值 {preview.finalRatio:0.00}，结果 {ExternalThreatRules.ResultName(preview.resultTier)}" +
                (preview.retreatAttempted ? $"，撤退{(preview.retreatSucceeded ? "成功" : "失败")}" : string.Empty);
            RuntimeUIFactory.Text(content, previewText, 18, 70);
        }

        confirm.gameObject.SetActive(true);
        confirm.interactable = ExternalThreatRules.CanRespond(SelectedNpcs(), selectedPlan, out _);
    }

    private void AddPageTab(Transform tabs, ThreatPage page, string label)
    {
        Button button = RuntimeUIFactory.TabButton(tabs, label, currentPage == page);
        pageButtons[page] = button;
        button.onClick.AddListener(() =>
        {
            if (currentPage == page) return;
            currentPage = page;
            Refresh();
        });
    }

    private void RefreshPageTabs()
    {
        foreach (KeyValuePair<ThreatPage, Button> item in pageButtons)
        {
            if (item.Value == null) continue;
            item.Value.GetComponent<Image>().color = item.Key == currentPage
                ? new Color(0.55f, 0.36f, 0.13f, 1f)
                : new Color(0.20f, 0.17f, 0.13f, 1f);
        }
    }

    private string BuildThreatDetails(ActiveThreatState threat, ExternalThreatDefinition definition)
    {
        StringBuilder text = new StringBuilder();
        text.AppendLine(definition.name);
        text.AppendLine($"情报 {threat.intelligence}/100　下次冲击：第 {threat.nextRaidDay} 天");
        text.AppendLine(threat.intelligence >= 25 ? $"敌人类型：{definition.enemyType}" : "敌人类型：尚未确认");
        if (threat.intelligence >= 50)
            text.AppendLine($"威胁战力：{definition.threatPower}　我方取得先手修正 1.10");
        if (threat.intelligence >= 75)
        {
            float defense = PlayerManager.Instance?.HasFacility(FacilityType.ProtectionArray) == true ? 1.25f : 1.2f;
            text.AppendLine($"情报修正 {1f + 0.0025f * threat.intelligence:0.00}　正面 1.00　简单防御 {defense:0.00}（材料×{definition.defenseMaterialCost}）");
        }
        return text.ToString();
    }

    private void ShowResolution(ActiveThreatState threat, ExternalThreatDefinition definition)
    {
        confirm.gameObject.SetActive(false);
        ThreatResolutionRecord record = threat.resolution;
        if (record == null)
        {
            RuntimeUIFactory.Text(content, $"{definition.name}\n威胁已解决，但没有可展示的结算记录。", 18, 80);
            status.text = string.Empty;
            return;
        }
        status.text = "该威胁已经永久解决。";
        if (currentPage == ThreatPage.Intelligence)
        {
            RuntimeUIFactory.Text(content,
                $"{definition.name}｜第 {record.day} 天解决\n最终情报：{threat.intelligence}/100\n累计冲击：{threat.raidCount} 次",
                18, 110);
            RuntimeUIFactory.Text(content, definition.description, 18, 80);
            return;
        }
        if (currentPage == ThreatPage.Investigation)
        {
            RuntimeUIFactory.Text(content,
                $"调查结果\n最终情报：{threat.intelligence}/100\n敌人类型：{definition.enemyType}\n威胁战力：{definition.threatPower}",
                18, 110);
            return;
        }
        if (currentPage == ThreatPage.Participants)
        {
            List<string> participantIds = record.participantIds ?? new List<string>();
            RuntimeUIFactory.Text(content, $"参战弟子（{participantIds.Count}）", 20, 36);
            foreach (string id in participantIds)
            {
                NPCRuntime npc = NPCManager.Instance?.GetRuntime(id);
                RuntimeUIFactory.Text(content, npc == null ? id : Name(npc), 18, 38);
            }
            if (participantIds.Count == 0)
                RuntimeUIFactory.Text(content, "本次选择退守洞府，没有弟子直接参战。", 18, 52);
            return;
        }

        StringBuilder text = new StringBuilder();
        text.AppendLine($"{definition.name}｜第 {record.day} 天解决");
        text.AppendLine(record.narrative);
        text.AppendLine($"方案：{PlanName(record.plan)}");
        if (record.combat != null)
        {
            text.AppendLine($"队伍战力 {record.combat.partyPower} / 威胁战力 {record.combat.threatPower}");
            text.AppendLine($"第一次交手 {record.combat.firstExchangeRatio:0.00} / 最终 {record.combat.finalRatio:0.00}");
            text.AppendLine($"结果：{ExternalThreatRules.ResultName(record.combat.resultTier)}" +
                (record.combat.retreatAttempted ? $"，撤退{(record.combat.retreatSucceeded ? "成功" : "失败")}" : string.Empty));
        }
        text.AppendLine($"青石村实际变化：人口 {Signed(record.populationChange)}，劳动力 {Signed(record.laborChange)}，关系 {Signed(record.relationChange)}");
        RuntimeUIFactory.Text(content, text.ToString(), 18, 260);
    }

    private void AddPlanButton(CombatPlanType plan, string label)
    {
        Button button = RuntimeUIFactory.Button(content, $"{(selectedPlan == plan ? "●" : "○")} {label}", 44);
        button.onClick.AddListener(() => { selectedPlan = plan; Refresh(); });
    }

    private void ToggleParticipant(string characterId)
    {
        if (selectedIds.Contains(characterId)) selectedIds.Remove(characterId);
        else if (selectedIds.Count < 3) selectedIds.Add(characterId);
        Refresh();
    }

    private void ResolveSelectedPlan()
    {
        ThreatResolutionRecord record = ExternalThreatRules.ResolveThreat(SelectedNpcs(), selectedPlan, out string reason);
        status.text = record == null ? reason : record.narrative;
        Refresh();
    }

    private IEnumerable<NPCRuntime> IdleLivingNpcs() =>
        NPCManager.Instance == null ? Enumerable.Empty<NPCRuntime>() :
        NPCManager.Instance.GetLivingNPC().Where(item => item.CanDispatch())
            .OrderBy(item => item.CharacterId, StringComparer.Ordinal);

    private IEnumerable<NPCRuntime> SelectedNpcs() =>
        selectedIds.Select(id => NPCManager.Instance?.GetRuntime(id)).Where(item => item != null);

    private static string Name(NPCRuntime npc) => npc.Character?.displayName ?? npc.Data?.npcName ?? "未知弟子";
    private static string Signed(int value) => value >= 0 ? "+" + value : value.ToString();
    private static string PlanName(CombatPlanType plan) =>
        plan == CombatPlanType.HeadOn ? "正面迎击" : plan == CombatPlanType.SimpleDefense ? "简单防御" : "退守洞府";
}
