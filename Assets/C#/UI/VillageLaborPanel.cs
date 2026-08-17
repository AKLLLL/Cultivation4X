using System.Collections;
using Cultivation4X.WorldMap;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class VillageLaborPanel : MonoBehaviour
{
    private static readonly string[] LaborMissionIds =
    {
        "founding_labor_cultivate", // 开垦灵田
        "founding_labor_gather",    // 采集材料
        "founding_labor_build"      // 修建设施
    };

    private Canvas canvas;
    private RectTransform panel;
    private TMP_Text laborText;
    private TMP_Text feedbackText;
    private WorldLocation village;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MapTestBootstrap.IsTestScene) return;
        if (FindObjectOfType<VillageLaborPanel>() != null) return;
        new GameObject("VillageLaborPanel").AddComponent<VillageLaborPanel>();
    }

    private void Awake()
    {
        canvas = RuntimeUIFactory.Canvas(gameObject, 1300);
        panel = RuntimeUIFactory.Panel(canvas.transform, "VillageLaborPanel",
            new Vector2(0.30f, 0.20f), new Vector2(0.70f, 0.80f));
        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 16, 16);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperCenter;

        RuntimeUIFactory.Text(panel, "青石村劳动力", 28, 44);
        laborText = RuntimeUIFactory.Text(panel, "可用劳动力：--", 20, 40);
        AddLaborButton(panel, "开垦灵田", 0);
        AddLaborButton(panel, "采集材料", 1);
        AddLaborButton(panel, "修建设施", 2);
        feedbackText = RuntimeUIFactory.Text(panel, string.Empty, 16, 56);
        Button close = RuntimeUIFactory.Button(panel, "关闭", 42);
        close.onClick.AddListener(Close);
        panel.gameObject.SetActive(false);
    }

    public void Open(WorldLocation location)
    {
        village = location;
        RefreshLaborText();
        feedbackText.text = string.Empty;
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(panel.gameObject, Close);
        else panel.gameObject.SetActive(true);
    }

    public void Close()
    {
        if (panel == null || !panel.gameObject.activeSelf) return;
        if (UIManager.Instance != null) UIManager.Instance.ClosePanel(panel.gameObject);
        else panel.gameObject.SetActive(false);
    }

    private void AddLaborButton(Transform parent, string label, int missionIndex)
    {
        Button button = RuntimeUIFactory.Button(parent, label, 46);
        button.onClick.AddListener(() => TriggerLaborMission(missionIndex));
    }

    private void TriggerLaborMission(int missionIndex)
    {
        MissionManager missions = MissionManager.Instance;
        if (missions == null)
        {
            feedbackText.text = "任务系统尚未初始化。";
            return;
        }
        string missionId = LaborMissionIds[missionIndex];
        if (!missions.CanTriggerLaborMission(missionId, out string reason))
        {
            feedbackText.text = reason;
            return;
        }
        missions.TriggerLaborMission(missionId);
        feedbackText.text = "已派遣村民执行，随时间推进完成。";
        RefreshLaborText();
    }

    private void RefreshLaborText()
    {
        VillageState state = PlayerManager.Instance?.playerData?.founding?.village;
        if (state == null)
        {
            laborText.text = "可用劳动力：--";
            return;
        }
        laborText.text =
            $"可用劳动力：{state.totalLabor - state.reservedLabor}/{state.totalLabor}";
    }

    private IEnumerator Start()
    {
        while (SaveManager.Instance == null || !SaveManager.Instance.IsInitializationComplete)
            yield return null;
        RefreshLaborText();
    }
}
