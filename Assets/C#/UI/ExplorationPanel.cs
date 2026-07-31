using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ExplorationPanel : MonoBehaviour
{
    private enum ExplorationPage
    {
        Overview,
        Details,
        Dispatch
    }

    private RectTransform panel;
    private RectTransform content;
    private TMP_Text statusText;
    private Button closeButton;
    private string selectedRegionId;
    private string pendingMissionId;
    private ExplorationPage currentPage;
    private readonly Dictionary<ExplorationPage, Button> pageButtons = new Dictionary<ExplorationPage, Button>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<ExplorationPanel>() == null)
            new GameObject("ExplorationPanel").AddComponent<ExplorationPanel>();
    }

    private void Awake()
    {
        RuntimeUIFactory.Canvas(gameObject, 860);
        Button launcher = RuntimeUIFactory.Button(transform, "探索堂");
        RectTransform rect = launcher.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(15, -70);
        rect.sizeDelta = new Vector2(150, 45);
        launcher.onClick.AddListener(Open);
        launcher.gameObject.SetActive(false);

        panel = RuntimeUIFactory.Panel(transform, "ExplorationHall", new Vector2(0.12f, 0.06f), new Vector2(0.88f, 0.94f));
        RuntimeUIFactory.Text(panel, "探索堂", 30, 42);
        statusText = RuntimeUIFactory.Text(panel, string.Empty, 17, 52);
        RectTransform tabs = RuntimeUIFactory.TabBar(panel, "ExplorationTabs");
        AddPageTab(tabs, ExplorationPage.Overview, "区域总览");
        AddPageTab(tabs, ExplorationPage.Details, "区域详情");
        AddPageTab(tabs, ExplorationPage.Dispatch, "派遣弟子");
        content = RuntimeUIFactory.ScrollContent(panel, "ExplorationScroll");
        closeButton = RuntimeUIFactory.Button(panel, "关闭", 40);
        closeButton.onClick.AddListener(Close);
        panel.gameObject.SetActive(false);
    }

    private void Open()
    {
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(panel.gameObject);
        else panel.gameObject.SetActive(true);
        Refresh();
    }

    public void OpenFromSectLayout() => Open();

    private void Close()
    {
        if (UIManager.Instance != null) UIManager.Instance.ClosePanel(panel.gameObject);
        else panel.gameObject.SetActive(false);
    }

    private void Refresh()
    {
        if (panel == null || content == null) return;
        ClearChildren(content);
        RefreshPageTabs();

        if (PlayerManager.Instance == null || MissionManager.Instance == null || NPCManager.Instance == null)
        {
            statusText.text = string.Empty;
            RuntimeUIFactory.Text(content, "探索系统尚未初始化。", 18, 42);
            return;
        }

        Mission temporary = MissionManager.Instance.GetActiveMissions().FirstOrDefault(item =>
            item.Data.explorationKind == ExplorationMissionKind.Survey || item.Data.explorationKind == ExplorationMissionKind.Progress);
        string notice = MissionManager.Instance.LastExplorationNotice;
        if (temporary != null)
            statusText.text = $"当前探索：{temporary.Data.name}｜{temporary.AssignedNPC?.Character.displayName}｜剩余 {temporary.RemainingDays} 天";
        else if (!string.IsNullOrEmpty(notice))
            statusText.text = notice;
        else
            statusText.text = "当前没有进行中的探索。";

        if (currentPage == ExplorationPage.Overview)
            ShowOverview(temporary);
        else if (currentPage == ExplorationPage.Details)
            ShowRegionDetails();
        else
            ShowDispatch();
    }

    private void ShowOverview(Mission temporary)
    {
        if (!string.IsNullOrEmpty(MissionManager.Instance.LastExplorationNotice))
            RuntimeUIFactory.Text(content, MissionManager.Instance.LastExplorationNotice, 17, 44);
        if (temporary == null && ExplorationRules.HasUndiscoveredRegion())
            AddMissionAction("勘察未知区域（3天）", ExplorationRules.SurveyMissionId);
        else if (temporary == null)
            RuntimeUIFactory.Text(content, "三个预设区域均已发现。", 17, 34);

        RuntimeUIFactory.Text(content, "区域列表", 20, 36);
        IReadOnlyList<ExplorationRegionDefinition> regions = ExplorationRules.GetRegions();
        foreach (ExplorationRegionDefinition region in regions)
        {
            ExplorationRegionState state = ExplorationRules.GetState(region.id);
            int mapCellIndex = ExplorationRules.GetMapCellIndex(region.id);
            string coordinate = mapCellIndex >= 0 && Cultivation4X.WorldMap.WorldMapSession.Current != null
                ? $"　地图格 {Cultivation4X.WorldMap.WorldMapSession.Current.cells[mapCellIndex].coord.col}," +
                  $"{Cultivation4X.WorldMap.WorldMapSession.Current.cells[mapCellIndex].coord.row}"
                : string.Empty;
            string label = state == null ? "未知区域" : $"{region.name}　发现度 {state.stage * 100 / ExplorationRules.MaxStage}%{coordinate}";
            Button button = RuntimeUIFactory.Button(content, label, 42);
            button.onClick.AddListener(() =>
            {
                selectedRegionId = region.id;
                pendingMissionId = null;
                currentPage = ExplorationPage.Details;
                Refresh();
            });
        }
    }

    private void ShowRegionDetails()
    {
        if (string.IsNullOrEmpty(selectedRegionId))
        {
            RuntimeUIFactory.Text(content, "请先在“区域总览”中选择一个区域。", 18, 48);
            return;
        }
        AddRegionDetails(ExplorationRules.GetRegion(selectedRegionId));
    }

    private void ShowDispatch()
    {
        if (string.IsNullOrEmpty(pendingMissionId))
        {
            RuntimeUIFactory.Text(content, "请先从区域总览或区域详情中选择探索行动。", 18, 48);
            return;
        }
        AddNpcChoices(pendingMissionId);
    }

    private void AddRegionDetails(ExplorationRegionDefinition region)
    {
        if (region == null) return;
        ExplorationRegionState state = ExplorationRules.GetState(region.id);
        if (state == null)
        {
            RuntimeUIFactory.Text(content, $"未知区域\n{region.unknownDescription}", 18, 70);
            return;
        }

        List<string> found = region.milestones.Take(state.stage).Select(item => item.name).ToList();
        int unknown = Mathf.Max(0, ExplorationRules.MaxStage - state.stage);
        string foundText = found.Count == 0 ? "暂无" : string.Join("、", found);
        RuntimeUIFactory.Text(content,
            $"{region.name}｜发现度 {state.stage * 100 / ExplorationRules.MaxStage}%\n{region.description}\n已发现：{foundText}｜未知内容：{unknown}项", 18, 82);

        Mission ongoing = MissionManager.Instance.GetActiveMissions().FirstOrDefault(item =>
            item.Data.explorationKind == ExplorationMissionKind.Ongoing && item.Data.explorationRegionId == region.id);
        if (ongoing != null)
        {
            string cycle = ongoing.RemainingDays <= 0 ? "等待仓库空间后结算" : $"距离下次产出 {ongoing.RemainingDays} 天";
            RuntimeUIFactory.Text(content, $"驻守弟子：{ongoing.AssignedNPC?.Character.displayName}｜{cycle}", 17, 34);
            Button recall = RuntimeUIFactory.Button(content, "召回驻守弟子", 40);
            recall.onClick.AddListener(() =>
            {
                if (!MissionManager.Instance.TryRecallExplorationMission(region.id, out string reason)) Debug.LogWarning(reason);
                Refresh();
            });
        }
        else if (state.stage < ExplorationRules.MaxStage)
        {
            AddMissionAction("继续探索该区域（3天）", region.progressMissionId);
        }
        else
        {
            AddMissionAction("派遣弟子持续驻守（每3天结算）", region.ongoingMissionId);
        }
    }

    private void AddMissionAction(string label, string missionId)
    {
        Button action = RuntimeUIFactory.Button(content, label, 42);
        action.onClick.AddListener(() =>
        {
            pendingMissionId = missionId;
            currentPage = ExplorationPage.Dispatch;
            Refresh();
        });
    }

    private void AddNpcChoices(string missionId)
    {
        MissionData data = MissionManager.Instance.GetMissionData(missionId);
        RuntimeUIFactory.Text(content, data == null ? "任务配置不存在。" : $"为“{data.name}”选择弟子：", 17, 32);
        if (data == null) return;
        List<NPCRuntime> living = NPCManager.Instance.GetLivingNPC();
        bool added = false;
        foreach (NPCRuntime npc in living)
        {
            if (!npc.CanDispatch()) continue;
            added = true;
            bool canStart = MissionManager.Instance.CanTriggerMission(missionId, npc, out string reason);
            Button button = RuntimeUIFactory.Button(content, canStart ? npc.Character.displayName : $"{npc.Character.displayName}（{reason}）", 38);
            button.interactable = canStart;
            button.onClick.AddListener(() =>
            {
                MissionManager.Instance.TriggerMission(missionId, npc);
                pendingMissionId = null;
                currentPage = ExplorationPage.Overview;
                Refresh();
            });
        }
        if (!added) RuntimeUIFactory.Text(content, "暂无可派遣弟子。", 17, 32);
    }

    private void AddPageTab(Transform tabs, ExplorationPage page, string label)
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
        foreach (KeyValuePair<ExplorationPage, Button> item in pageButtons)
        {
            if (item.Value == null) continue;
            item.Value.GetComponent<Image>().color = item.Key == currentPage
                ? new Color(0.55f, 0.36f, 0.13f, 1f)
                : new Color(0.20f, 0.17f, 0.13f, 1f);
        }
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }
}
