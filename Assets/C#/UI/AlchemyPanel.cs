using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AlchemyPanel : MonoBehaviour
{
    private string AlchemyMissionId
    {
        get
        {
            FoundingState founding = PlayerManager.Instance?.playerData?.founding;
            return founding != null && founding.selectedTechniqueId == "qingmu"
                ? "founding_route_alchemy"
                : "production_001";
        }
    }
    private RectTransform panel;
    private string pendingMissionId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MapTestBootstrap.IsTestScene) return;
        if (FindObjectOfType<AlchemyPanel>() == null)
            new GameObject("AlchemyPanel").AddComponent<AlchemyPanel>();
    }

    private void Awake()
    {
        RuntimeUIFactory.Canvas(gameObject, 845);
        panel = RuntimeUIFactory.Panel(transform, "AlchemyRoom", new Vector2(0.22f, 0.16f), new Vector2(0.78f, 0.84f));
        panel.gameObject.SetActive(false);
        BindSceneButton();
    }

    private void Start()
    {
        BindSceneButton();
    }

    private void BindSceneButton()
    {
        GameObject buttonObject = GameObject.Find("Button_AlchemyRoom");
        Button button = buttonObject == null ? null : buttonObject.GetComponent<Button>();
        if (button == null) return;
        button.onClick.RemoveListener(Open);
        button.onClick.AddListener(Open);
    }

    private void Open()
    {
        pendingMissionId = null;
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(panel.gameObject);
        else panel.gameObject.SetActive(true);
        Refresh();
    }

    public void OpenFromSectLayout() => Open();

    private void Refresh()
    {
        if (panel == null) return;
        for (int i = panel.childCount - 1; i >= 0; i--) Destroy(panel.GetChild(i).gameObject);

        RuntimeUIFactory.Text(panel, "炼丹房", 30, 46);
        if (MissionManager.Instance == null || NPCManager.Instance == null || PlayerManager.Instance == null || WarehouseManager.Instance == null)
        {
            RuntimeUIFactory.Text(panel, "炼丹系统尚未初始化。", 18, 42);
            AddCloseButton();
            return;
        }
        if (PlayerManager.Instance.GetFacilityLevel(FacilityType.AlchemyRoom) <= 0)
        {
            RuntimeUIFactory.Text(panel, "丹房尚未建成。青木长生诀理解达到100%并获得青石村支持后，可在“洞府立宗”中建设。", 18, 72);
            AddCloseButton();
            return;
        }

        MissionData data = MissionManager.Instance.GetMissionData(AlchemyMissionId);
        if (data == null)
        {
            RuntimeUIFactory.Text(panel, "炼丹任务配置不存在。", 18, 42);
            AddCloseButton();
            return;
        }

        Mission running = MissionManager.Instance.GetActiveMissions().FirstOrDefault(item =>
            item.Data.id == AlchemyMissionId && (item.State == MissionState.Active || item.State == MissionState.WaitingNode));
        if (running != null)
        {
            RuntimeUIFactory.Text(panel,
                $"当前炼丹：{running.AssignedNPC?.Character.displayName}\n剩余 {running.RemainingDays} 天",
                18, 68);
        }
        else
        {
            int level = PlayerManager.Instance.GetFacilityLevel(FacilityType.AlchemyRoom);
            int days = data.usesFacilityLevelScaling ? FacilityRules.AlchemyDays(level) : data.needDays;
            int pills = FacilityRules.AlchemyPillReward(level);
            RuntimeUIFactory.Text(panel,
                $"{data.name}\n耗时 {days} 天｜消耗 {FormatCosts(data.itemCosts)}｜产出 丹药 x{pills}",
                18, 82);
            Button start = RuntimeUIFactory.Button(panel, "派遣弟子炼丹", 42);
            start.onClick.AddListener(() => { pendingMissionId = AlchemyMissionId; Refresh(); });
        }

        if (!string.IsNullOrEmpty(pendingMissionId)) AddNpcChoices(pendingMissionId);
        AddCloseButton();
    }

    private void AddNpcChoices(string missionId)
    {
        RuntimeUIFactory.Text(panel, "选择炼丹弟子：", 18, 34);
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
        if (!added) RuntimeUIFactory.Text(panel, "暂无可派遣弟子。", 18, 34);
    }

    private static string FormatCosts(List<ItemReward> costs)
    {
        if (costs == null || costs.Count == 0) return "无材料";
        return string.Join("、", costs.Select(item => $"{item.itemId} x{item.count}"));
    }

    private void AddCloseButton()
    {
        Button close = RuntimeUIFactory.Button(panel, "关闭", 40);
        close.onClick.AddListener(() =>
        {
            if (UIManager.Instance != null) UIManager.Instance.ClosePanel(panel.gameObject);
            else panel.gameObject.SetActive(false);
        });
    }
}
