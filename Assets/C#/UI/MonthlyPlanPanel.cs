using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MonthlyPlanPanel : MonoBehaviour
{
    private RectTransform panel;
    private RectTransform content;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MapTestBootstrap.IsTestScene) return;
        if (FindObjectOfType<MonthlyPlanPanel>() == null)
            new GameObject("MonthlyPlanPanel").AddComponent<MonthlyPlanPanel>();
    }

    private void Awake()
    {
        RuntimeUIFactory.Canvas(gameObject, 846);
        panel = RuntimeUIFactory.Panel(transform, "MonthlyPlan", new Vector2(0.18f, 0.10f), new Vector2(0.82f, 0.90f));
        panel.gameObject.SetActive(false);
    }

    public void OpenFromSectLayout()
    {
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(panel.gameObject);
        else panel.gameObject.SetActive(true);
        Refresh();
    }

    private void Refresh()
    {
        for (int i = panel.childCount - 1; i >= 0; i--) Destroy(panel.GetChild(i).gameObject);
        int day = TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay;
        RuntimeUIFactory.Text(panel, $"第 {MonthlyPlanRules.EditableMonth(day)} 月弟子计划", 28, 44);
        RuntimeUIFactory.Text(panel, "当前月已锁定。点击调整10%，三项始终合计100%；未设置计划的弟子本月全部自由。", 16, 56);
        content = RuntimeUIFactory.ScrollContent(panel, "MonthlyPlanScroll");
        List<NPCRuntime> disciples = NPCManager.Instance == null ? new List<NPCRuntime>() : NPCManager.Instance.GetLivingNPC();
        foreach (NPCRuntime npc in disciples) AddDisciple(npc, day);
        if (disciples.Count == 0) RuntimeUIFactory.Text(content, "暂无可编排弟子。", 18, 40);
        Button close = RuntimeUIFactory.Button(panel, "保存并关闭", 42);
        close.onClick.AddListener(() =>
        {
            SaveManager.Instance?.AutoSave();
            if (UIManager.Instance != null) UIManager.Instance.ClosePanel(panel.gameObject);
            else panel.gameObject.SetActive(false);
        });
    }

    private void AddDisciple(NPCRuntime npc, int day)
    {
        MonthlyDisciplePlan plan = MonthlyPlanRules.GetPlan(npc.CharacterId, MonthlyPlanRules.EditableMonth(day));
        if (plan == null)
        {
            RuntimeUIFactory.Text(content,
                $"{npc.Character.displayName}　纳气 {npc.Character.naqiProgress:0.0}%　尚未制定（该月全部自由）", 18, 42);
            Button enable = RuntimeUIFactory.Button(content, "采用默认 50 / 20 / 30", 36);
            enable.onClick.AddListener(() => { MonthlyPlanRules.GetOrCreateEditablePlan(npc.CharacterId, day); Refresh(); });
            return;
        }
        RuntimeUIFactory.Text(content,
            $"{npc.Character.displayName}　纳气 {npc.Character.naqiProgress:0.0}%　修炼 {plan.trainingPercent}% / 宗务 {plan.sectDutyPercent}% / 自由 {plan.freePercent}%",
            18, 42);
        AddAdjustButton(npc, plan, MonthlyActivityType.Training, 10, "修炼 +10");
        AddAdjustButton(npc, plan, MonthlyActivityType.Training, -10, "修炼 -10");
        AddAdjustButton(npc, plan, MonthlyActivityType.SectDuty, 10, "宗务 +10");
        AddAdjustButton(npc, plan, MonthlyActivityType.SectDuty, -10, "宗务 -10");
        Button stone = RuntimeUIFactory.Button(content,
            plan.useSpiritStone ? "灵石辅助：开" : "灵石辅助：关", 36);
        stone.onClick.AddListener(() => { plan.useSpiritStone = !plan.useSpiritStone; Refresh(); });
    }

    private void AddAdjustButton(NPCRuntime npc, MonthlyDisciplePlan plan, MonthlyActivityType target, int delta, string label)
    {
        Button button = RuntimeUIFactory.Button(content, label, 34);
        button.onClick.AddListener(() =>
        {
            int training = plan.trainingPercent;
            int duty = plan.sectDutyPercent;
            int free = plan.freePercent;
            if (target == MonthlyActivityType.Training) training += delta;
            else duty += delta;
            if (delta > 0) free -= delta;
            else free -= delta;
            if (free < 0 && target == MonthlyActivityType.Training) { duty += free; free = 0; }
            if (free < 0 && target == MonthlyActivityType.SectDuty) { training += free; free = 0; }
            if (MonthlyPlanRules.TrySetPlan(plan, training, duty, free, out _)) Refresh();
        });
    }
}
