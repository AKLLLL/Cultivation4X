using System.Collections.Generic;
using Cultivation4X.WorldMap;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 资源状态面板（运行时创建，不修改场景/Prefab）。
/// 只读展示已发现的资源地点；开发必须走 WorldLocation → 地点行动 → Mission。
/// 开发版额外提供非持久化的资源测试控制。
/// </summary>
public sealed class ResourceStatusPanel : MonoBehaviour
{
    private RectTransform panel;
    private RectTransform rowsRoot;
    private Button bypassButton;
    private TMP_Text feedback;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MapTestBootstrap.IsTestScene) return;
        if (FindObjectOfType<ResourceStatusPanel>() == null)
            new GameObject("ResourceStatusPanel").AddComponent<ResourceStatusPanel>();
    }

    private void Awake()
    {
        RuntimeUIFactory.Canvas(gameObject, 860);
        panel = RuntimeUIFactory.Panel(transform, "ResourceStatus",
            new Vector2(0.18f, 0.12f), new Vector2(0.82f, 0.88f));
        RuntimeUIFactory.Text(panel, "资源状态", 30, 48);

        Button refresh = RuntimeUIFactory.Button(panel, "刷新资源状态", 42);
        refresh.onClick.AddListener(Refresh);
        if (Application.isEditor || Debug.isDebugBuild)
        {
            bypassButton = RuntimeUIFactory.Button(panel, BypassLabel(), 42);
            bypassButton.onClick.AddListener(ToggleBypass);
            Button jump = RuntimeUIFactory.Button(panel, "跳至下个资源结算日前一天", 42);
            jump.onClick.AddListener(JumpToNextSettlementEve);
        }
        feedback = RuntimeUIFactory.Text(panel, string.Empty, 15, 64);

        rowsRoot = RuntimeUIFactory.Panel(panel, "ResourceRows",
            new Vector2(0f, 0f), new Vector2(1f, 1f));
        LayoutElement rowsElement = rowsRoot.gameObject.AddComponent<LayoutElement>();
        rowsElement.minHeight = 80f;
        rowsElement.flexibleHeight = 1f;

        Button close = RuntimeUIFactory.Button(panel, "关闭", 42);
        close.onClick.AddListener(Close);
        panel.gameObject.SetActive(false);
    }

    public void OpenFromSectLayout() => Open();

    private void Open()
    {
        Refresh();
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(panel.gameObject, CloseInternal);
        else panel.gameObject.SetActive(true);
    }

    private void Close()
    {
        if (UIManager.Instance != null) UIManager.Instance.ClosePanel(panel.gameObject);
        else CloseInternal();
    }

    private void CloseInternal() => panel.gameObject.SetActive(false);

    private void Refresh()
    {
        ClearRows();
        if (bypassButton != null)
            bypassButton.GetComponentInChildren<TMP_Text>().text = BypassLabel();

        List<ResourceStatusRow> rows = ResourceStatusService.BuildDiscoveredNodeRows(
            WorldMapSession.Current, WorldMapSession.Progress);
        if (rows.Count == 0)
        {
            RuntimeUIFactory.Text(rowsRoot, "尚未发现资源地点。", 16, 46);
            return;
        }

        foreach (ResourceStatusRow row in rows)
        {
            string requirement = row.statusLabel == "已开发"
                ? "已满足"
                : row.developmentRequirementMet ? "已满足" : "需影响力≥影响（可通过探索扩展）";
            RuntimeUIFactory.Text(rowsRoot,
                $"{row.siteName}｜{row.siteTypeLabel}\n" +
                $"状态：{row.statusLabel}｜当前影响力：{row.influenceLabel}｜开发要求：{requirement}\n" +
                $"月基础产量：{row.baseOutput}｜预计产量：{row.expectedOutput}｜上次结算月：第 {row.lastSettledMonth} 月\n" +
                $"仓库现有：{row.resourceName} ×{row.warehouseCount}｜最近损失：{row.lastSettledLost}", 16, 104);
        }
    }

    private void ClearRows()
    {
        for (int index = rowsRoot.childCount - 1; index >= 0; index--)
        {
            Transform child = rowsRoot.GetChild(index);
            if (child == null) continue;
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }
    }

    private void ToggleBypass()
    {
        GameDebugConfig.BypassResourceDevelopmentInfluence =
            !GameDebugConfig.BypassResourceDevelopmentInfluence;
        if (bypassButton != null)
            bypassButton.GetComponentInChildren<TMP_Text>().text = BypassLabel();
        SetFeedback(GameDebugConfig.BypassResourceDevelopmentInfluence
            ? "远程资源开发测试已开启：仅豁免 DevelopResourceNode/DevelopSpiritMine 的影响力检查，仍完整执行任务、奖励、地点状态与自动保存。"
            : "远程资源开发测试已关闭。");
    }

    private void JumpToNextSettlementEve()
    {
        TimeManager time = TimeManager.Instance;
        if (time == null) { SetFeedback("时间系统未初始化。"); return; }
        if (time.IsSettlementOpen) { SetFeedback("请先关闭每日结算。"); return; }
        int target = ResourceStatusService.NextSettlementEveDay(time.CurrentDay);
        if (target <= time.CurrentDay) { SetFeedback("没有更晚的结算日前一天可跳转。"); return; }
        int requested = target - time.CurrentDay;
        int advanced = time.AdvanceDaysForTesting(requested);
        SetFeedback(advanced == requested
            ? $"已通过真实结算链推进至第 {time.CurrentDay} 天。"
            : $"推进在第 {time.CurrentDay} 天停止，请先处理事件或结算阻塞。");
    }

    private void SetFeedback(string text)
    {
        if (feedback != null) feedback.text = text;
    }

    private static string BypassLabel() =>
        GameDebugConfig.BypassResourceDevelopmentInfluence
            ? "远程资源开发测试：开"
            : "远程资源开发测试：关";
}
