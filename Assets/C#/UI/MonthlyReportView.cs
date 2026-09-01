using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MonthlyReportContext : IUIWindowContext
{
    public int? MonthIndex { get; }
    public bool AcknowledgeUnreadMonthEnd { get; }

    public MonthlyReportContext(int? monthIndex = null, bool acknowledgeUnreadMonthEnd = false)
    {
        MonthIndex = monthIndex;
        AcknowledgeUnreadMonthEnd = acknowledgeUnreadMonthEnd;
    }
}

public sealed class MonthlyReportView : UIWindowView
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text periodText;
    [SerializeField] private TMP_Text contentText;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button toggleAllButton;
    [SerializeField] private TMP_Text toggleAllText;
    [SerializeField] private Button closeButton;

    private int selectedMonthIndex;
    private bool showAll;
    private bool acknowledgeUnread;
    private TMP_Text discipleGrowthText;
    private TMP_Text experienceHighlightsText;
    private TMP_Text sectResourcesText;

    public void Configure(TMP_Text title, TMP_Text period, TMP_Text content, Button previous,
        Button next, Button toggleAll, TMP_Text toggleText, Button close)
    {
        titleText = title;
        periodText = period;
        contentText = content;
        previousButton = previous;
        nextButton = next;
        toggleAllButton = toggleAll;
        toggleAllText = toggleText;
        closeButton = close;
    }

    private void Awake()
    {
        EnsureSectionCards();
        previousButton?.onClick.AddListener(() => Move(-1));
        nextButton?.onClick.AddListener(() => Move(1));
        toggleAllButton?.onClick.AddListener(() => { showAll = !showAll; Render(); });
        closeButton?.onClick.AddListener(() => UIManager.Instance?.CloseWindow(UIWindowId.MonthlyReport));
    }

    public override void OnOpened(IUIWindowContext context)
    {
        EnsureSectionCards();
        MonthlyReportContext reportContext = context as MonthlyReportContext;
        acknowledgeUnread = reportContext?.AcknowledgeUnreadMonthEnd == true;
        SectMonthlyReport latest = GrowthFeedbackRules.LatestReport(PlayerManager.Instance?.playerData);
        selectedMonthIndex = reportContext?.MonthIndex ?? latest?.monthIndex ?? 0;
        showAll = false;
        TimeManager.Instance?.SetSettlementOpen(true);
        Render();
    }

    public override void OnClosed()
    {
        TimeManager.Instance?.SetSettlementOpen(false);
        if (!acknowledgeUnread || TimeManager.Instance?.UnreadDaySettlement?.isMonthEnd != true) return;
        TimeManager.Instance.MarkSettlementRead();
        SaveManager.Instance?.AutoSave();
        acknowledgeUnread = false;
    }

    private void Move(int offset)
    {
        List<SectMonthlyReport> reports = Reports();
        int index = reports.FindIndex(item => item.monthIndex == selectedMonthIndex);
        if (index < 0) return;
        selectedMonthIndex = reports[Mathf.Clamp(index + offset, 0, reports.Count - 1)].monthIndex;
        showAll = false;
        Render();
    }

    private void Render()
    {
        List<SectMonthlyReport> reports = Reports();
        SectMonthlyReport report = reports.FirstOrDefault(item => item.monthIndex == selectedMonthIndex)
                                  ?? reports.LastOrDefault();
        if (report == null)
        {
            Set(titleText, "宗门月报");
            Set(periodText, "暂无已完成月份");
            SetReportContent("完成首个30日结算后，将在这里生成弟子成长摘要。",
                "暂无值得关注的经历。", "暂无宗门资源月结记录。");
            SetNavigation(false, false);
            if (toggleAllButton != null) toggleAllButton.interactable = false;
            return;
        }
        selectedMonthIndex = report.monthIndex;
        int index = reports.IndexOf(report);
        Set(titleText, $"第{report.monthIndex}月宗门月报");
        Set(periodText, $"第{report.year}年·第{report.month}月　世界日 {report.startDay}—{report.endDay}");
        SetNavigation(index > 0, index < reports.Count - 1);
        if (toggleAllButton != null) toggleAllButton.interactable = report.disciples.Count > report.highlightDiscipleIds.Count;
        Set(toggleAllText, showAll ? "只看重点" : "查看全部弟子");
        SetReportContent(BuildDiscipleContent(report, showAll), BuildExperienceContent(report),
            BuildResourceContent(report));
    }

    private void EnsureSectionCards()
    {
        if (contentText == null || discipleGrowthText != null) return;
        Transform parent = contentText.transform.parent;
        if (parent == null) return;
        TMP_FontAsset font = contentText.font;
        Color bodyColor = contentText.color;
        discipleGrowthText = CreateSectionCard(parent, "DiscipleGrowthCard", "弟子成长", font, bodyColor);
        experienceHighlightsText = CreateSectionCard(parent, "ExperienceHighlightsCard", "值得关注", font, bodyColor);
        sectResourcesText = CreateSectionCard(parent, "SectResourcesCard", "宗门资源", font, bodyColor);
        contentText.gameObject.SetActive(false);
    }

    private static TMP_Text CreateSectionCard(Transform parent, string name, string heading,
        TMP_FontAsset font, Color bodyColor)
    {
        RectTransform card = RuntimeUIFactory.InfoCard(parent, name);
        TMP_Text title = RuntimeUIFactory.Text(card, heading, 18, 30f);
        title.font = font;
        title.color = bodyColor;
        title.fontStyle = FontStyles.Bold;
        TMP_Text body = RuntimeUIFactory.Text(card, string.Empty, 16, 36f);
        body.font = font;
        body.color = bodyColor;
        body.alignment = TextAlignmentOptions.TopLeft;
        body.enableWordWrapping = true;
        body.overflowMode = TextOverflowModes.Overflow;
        LayoutElement bodySize = body.GetComponent<LayoutElement>();
        bodySize.minHeight = 36f;
        bodySize.preferredHeight = -1f;
        bodySize.flexibleHeight = 0f;
        return body;
    }

    private void SetReportContent(string growth, string experiences, string resources)
    {
        if (discipleGrowthText != null && experienceHighlightsText != null && sectResourcesText != null)
        {
            Set(discipleGrowthText, growth);
            Set(experienceHighlightsText, experiences);
            Set(sectResourcesText, resources);
            return;
        }
        Set(contentText, $"弟子成长\n────────────────\n{growth}\n\n值得关注\n────────────────\n{experiences}\n\n宗门资源\n────────────────\n{resources}");
    }

    private static string BuildDiscipleContent(SectMonthlyReport report, bool all)
    {
        StringBuilder text = new StringBuilder();
        HashSet<string> selected = all ? null : new HashSet<string>(report.highlightDiscipleIds);
        List<DiscipleMonthlyStats> disciples = report.disciples
            .Where(item => all || selected.Contains(item.discipleId))
            .OrderBy(item => all ? item.displayName : report.highlightDiscipleIds.IndexOf(item.discipleId).ToString("D3"),
                StringComparer.Ordinal).ToList();
        if (disciples.Count == 0) text.AppendLine(all ? "本月没有实际参与者。" : "本月没有达到高亮条件的弟子，可查看全部弟子。");
        foreach (DiscipleMonthlyStats stats in disciples)
        {
            text.AppendLine($"\n{stats.displayName}");
            text.AppendLine($"计划 修炼{stats.plannedTrainingDays} / 宗务{stats.plannedSectDutyDays} / 自由{stats.plannedFreeDays} 日");
            text.AppendLine($"实际 修炼{stats.actualTrainingDays} / 宗务{stats.actualSectDutyDays} / 自由{stats.actualFreeDays} / Mission{stats.missionDays} / 休养{stats.recoveryDays} 日");
            text.AppendLine($"纳气 +{stats.naqiGain:0.0}%　控制 +{stats.auraControlGain:0.0}　功法理解 +{stats.techniqueProgressGain:0.0}　疲劳峰值 {stats.maxFatigue:0.0}");
            MonthlyActionCount action = stats.actionCounts.OrderByDescending(item => item.count)
                .ThenBy(item => item.actionId, StringComparer.Ordinal).FirstOrDefault();
            if (action != null) text.AppendLine($"主要行动：{action.displayName ?? action.actionId} ×{action.count}");
        }
        return text.ToString().Trim();
    }

    private static string BuildExperienceContent(SectMonthlyReport report)
    {
        StringBuilder text = new StringBuilder();
        List<LifeRecord> experiences = FindExperiences(report.highlightExperienceIds);
        if (experiences.Count == 0) text.AppendLine("本月暂无关键经历。");
        foreach (LifeRecord record in experiences.OrderByDescending(item => item.day).Take(12))
            text.AppendLine($"第{record.day}日　{ExperienceRecordRules.Format(record)}");
        return text.ToString().Trim();
    }

    private static string BuildResourceContent(SectMonthlyReport report)
    {
        StringBuilder text = new StringBuilder();
        if (report.itemChanges.Count == 0) text.AppendLine("全月库存无净变化。");
        foreach (ItemMonthChange item in report.itemChanges.OrderBy(item => item.itemId, StringComparer.Ordinal))
            text.AppendLine($"{ItemName(item.itemId)}　{(item.countChange >= 0 ? "+" : string.Empty)}{item.countChange}");
        text.AppendLine("\n月结生产（与上方库存净变化分列）");
        if (report.resourceProduction.Count == 0) text.AppendLine("无月结生产记录。");
        foreach (ResourceProductionRecord production in report.resourceProduction)
            text.AppendLine($"{production.siteName ?? production.nodeId} · {ItemName(production.itemId)}　应产{production.calculated} / 入库{production.received} / 损失{production.lost}");
        return text.ToString().Trim();
    }

    private static List<LifeRecord> FindExperiences(IEnumerable<string> ids)
    {
        HashSet<string> requested = new HashSet<string>(ids ?? Enumerable.Empty<string>());
        return NPCManager.Instance?.GetAllNPC().Where(npc => npc?.Character?.lifeRecords != null)
            .SelectMany(npc => npc.Character.lifeRecords).Where(item => item != null && requested.Contains(item.id))
            .GroupBy(item => item.id).Select(group => group.First()).ToList() ?? new List<LifeRecord>();
    }

    private static List<SectMonthlyReport> Reports() =>
        (PlayerManager.Instance?.playerData?.growthFeedback?.reports ?? new List<SectMonthlyReport>())
        .Where(item => item != null).OrderBy(item => item.monthIndex).ToList();

    private static string ItemName(string itemId) =>
        ItemDatabase.Instance?.GetItem(itemId)?.itemName ?? itemId ?? "未知资源";

    private void SetNavigation(bool previous, bool next)
    {
        if (previousButton != null) previousButton.interactable = previous;
        if (nextButton != null) nextButton.interactable = next;
    }

    private static void Set(TMP_Text target, string value)
    {
        if (target != null) target.text = value ?? string.Empty;
    }
}
