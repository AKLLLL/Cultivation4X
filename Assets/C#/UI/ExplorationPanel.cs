using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ExplorationPanel : MonoBehaviour
{
    private RectTransform panel;
    private string selectedRegionId;
    private string pendingMissionId;

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

        panel = RuntimeUIFactory.Panel(transform, "ExplorationHall", new Vector2(0.12f, 0.06f), new Vector2(0.88f, 0.94f));
        panel.gameObject.SetActive(false);
    }

    private void Open()
    {
        panel.gameObject.SetActive(true);
        Refresh();
    }

    private void Refresh()
    {
        if (panel == null) return;
        for (int i = panel.childCount - 1; i >= 0; i--) Destroy(panel.GetChild(i).gameObject);
        RuntimeUIFactory.Text(panel, "探索堂", 30, 42);

        if (PlayerManager.Instance == null || MissionManager.Instance == null || NPCManager.Instance == null)
        {
            RuntimeUIFactory.Text(panel, "探索系统尚未初始化。", 18, 42);
            AddCloseButton();
            return;
        }

        Mission temporary = MissionManager.Instance.GetActiveMissions().FirstOrDefault(item =>
            item.Data.explorationKind == ExplorationMissionKind.Survey || item.Data.explorationKind == ExplorationMissionKind.Progress);
        if (!string.IsNullOrEmpty(MissionManager.Instance.LastExplorationNotice))
            RuntimeUIFactory.Text(panel, MissionManager.Instance.LastExplorationNotice, 17, 44);
        if (temporary != null)
            RuntimeUIFactory.Text(panel, $"当前探索：{temporary.Data.name}｜{temporary.AssignedNPC?.Character.displayName}｜剩余 {temporary.RemainingDays} 天", 17, 36);
        else if (ExplorationRules.HasUndiscoveredRegion())
            AddMissionAction("勘察未知区域（3天）", ExplorationRules.SurveyMissionId);
        else
            RuntimeUIFactory.Text(panel, "三个预设区域均已发现。", 17, 34);

        IReadOnlyList<ExplorationRegionDefinition> regions = ExplorationRules.GetRegions();
        foreach (ExplorationRegionDefinition region in regions)
        {
            ExplorationRegionState state = ExplorationRules.GetState(region.id);
            string label = state == null ? "未知区域" : $"{region.name}　发现度 {state.stage * 100 / ExplorationRules.MaxStage}%";
            Button button = RuntimeUIFactory.Button(panel, label, 42);
            button.onClick.AddListener(() => { selectedRegionId = region.id; pendingMissionId = null; Refresh(); });
        }

        if (!string.IsNullOrEmpty(selectedRegionId)) AddRegionDetails(ExplorationRules.GetRegion(selectedRegionId));
        if (!string.IsNullOrEmpty(pendingMissionId)) AddNpcChoices(pendingMissionId);
        AddCloseButton();
    }

    private void AddRegionDetails(ExplorationRegionDefinition region)
    {
        if (region == null) return;
        ExplorationRegionState state = ExplorationRules.GetState(region.id);
        if (state == null)
        {
            RuntimeUIFactory.Text(panel, $"未知区域\n{region.unknownDescription}", 18, 70);
            return;
        }

        List<string> found = region.milestones.Take(state.stage).Select(item => item.name).ToList();
        int unknown = Mathf.Max(0, ExplorationRules.MaxStage - state.stage);
        string foundText = found.Count == 0 ? "暂无" : string.Join("、", found);
        RuntimeUIFactory.Text(panel,
            $"{region.name}｜发现度 {state.stage * 100 / ExplorationRules.MaxStage}%\n{region.description}\n已发现：{foundText}｜未知内容：{unknown}项", 18, 82);

        Mission ongoing = MissionManager.Instance.GetActiveMissions().FirstOrDefault(item =>
            item.Data.explorationKind == ExplorationMissionKind.Ongoing && item.Data.explorationRegionId == region.id);
        if (ongoing != null)
        {
            string cycle = ongoing.RemainingDays <= 0 ? "等待仓库空间后结算" : $"距离下次产出 {ongoing.RemainingDays} 天";
            RuntimeUIFactory.Text(panel, $"驻守弟子：{ongoing.AssignedNPC?.Character.displayName}｜{cycle}", 17, 34);
            Button recall = RuntimeUIFactory.Button(panel, "召回驻守弟子", 40);
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
        Button action = RuntimeUIFactory.Button(panel, label, 42);
        action.onClick.AddListener(() => { pendingMissionId = missionId; Refresh(); });
    }

    private void AddNpcChoices(string missionId)
    {
        MissionData data = MissionManager.Instance.GetMissionData(missionId);
        RuntimeUIFactory.Text(panel, data == null ? "任务配置不存在。" : $"为“{data.name}”选择弟子：", 17, 32);
        if (data == null) return;
        List<NPCRuntime> living = NPCManager.Instance.GetLivingNPC();
        bool added = false;
        foreach (NPCRuntime npc in living)
        {
            if (!npc.CanDispatch()) continue;
            added = true;
            bool canStart = MissionManager.Instance.CanTriggerMission(missionId, npc, out string reason);
            Button button = RuntimeUIFactory.Button(panel, canStart ? npc.Character.displayName : $"{npc.Character.displayName}（{reason}）", 38);
            button.interactable = canStart;
            button.onClick.AddListener(() =>
            {
                MissionManager.Instance.TriggerMission(missionId, npc);
                pendingMissionId = null;
                Refresh();
            });
        }
        if (!added) RuntimeUIFactory.Text(panel, "暂无可派遣弟子。", 17, 32);
    }

    private void AddCloseButton()
    {
        Button close = RuntimeUIFactory.Button(panel, "关闭", 40);
        close.onClick.AddListener(() => panel.gameObject.SetActive(false));
    }
}
