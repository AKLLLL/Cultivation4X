using System.Collections;
using System.Linq;
using Cultivation4X.WorldMap;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class GlobalHudView : MonoBehaviour
{
    [SerializeField] private GameObject shellRoot;
    [SerializeField] private TMP_Text sectNameText;
    [SerializeField] private TMP_Text contextTitleText;
    [SerializeField] private TMP_Text resourceText;
    [SerializeField] private TMP_Text eventText;
    [SerializeField] private TMP_Text dayText;
    [SerializeField] private Button mapButton;
    [SerializeField] private Button sectButton;
    [SerializeField] private Button discipleButton;
    [SerializeField] private Button monthlyPlanButton;
    [SerializeField] private Button missionButton;
    [SerializeField] private Button resourceButton;
    [SerializeField] private Button eventButton;
    [SerializeField] private Button settlementButton;
    [SerializeField] private Button endDayButton;
    [SerializeField] private Button speed1Button;
    [SerializeField] private Button speed2Button;
    [SerializeField] private Button speed4Button;
    [SerializeField] private Button returnToMapButton;

    private UIManager manager;
    private UITheme theme;
    private bool listenersBound;
    private bool hasAppliedFlowState;
    private GameFlowState lastAppliedFlowState;

    public void Configure(GameObject root, TMP_Text sectName, TMP_Text contextTitle, TMP_Text resources,
        TMP_Text events, TMP_Text day, Button map, Button sect, Button disciple, Button monthlyPlan, Button mission,
        Button resource, Button eventInbox, Button endDay, Button returnToMap)
    {
        shellRoot = root;
        sectNameText = sectName;
        contextTitleText = contextTitle;
        resourceText = resources;
        eventText = events;
        dayText = day;
        mapButton = map;
        sectButton = sect;
        discipleButton = disciple;
        monthlyPlanButton = monthlyPlan;
        missionButton = mission;
        resourceButton = resource;
        eventButton = eventInbox;
        endDayButton = endDay;
        returnToMapButton = returnToMap;
    }

    public void ConfigureTimeControls(Button settlement, Button speed1, Button speed2, Button speed4)
    {
        settlementButton = settlement;
        speed1Button = speed1;
        speed2Button = speed2;
        speed4Button = speed4;
    }

    public void Bind(UIManager uiManager, UITheme uiTheme)
    {
        manager = uiManager;
        theme = uiTheme;
        BindButtons();
        ApplyTheme();
        RefreshAll();
    }

    private IEnumerator Start()
    {
        if (shellRoot != null) shellRoot.SetActive(false);
        while (GameFlowStateManager.Instance == null || SaveManager.Instance == null ||
               !SaveManager.Instance.IsInitializationComplete)
            yield return null;
        BindDomainEvents();
        ApplyFlowState(GameFlowStateManager.Instance.Current);
        RefreshAll();
    }

    private void BindButtons()
    {
        mapButton?.onClick.RemoveAllListeners();
        sectButton?.onClick.RemoveAllListeners();
        discipleButton?.onClick.RemoveAllListeners();
        monthlyPlanButton?.onClick.RemoveAllListeners();
        missionButton?.onClick.RemoveAllListeners();
        resourceButton?.onClick.RemoveAllListeners();
        eventButton?.onClick.RemoveAllListeners();
        settlementButton?.onClick.RemoveAllListeners();
        endDayButton?.onClick.RemoveAllListeners();
        speed1Button?.onClick.RemoveAllListeners();
        speed2Button?.onClick.RemoveAllListeners();
        speed4Button?.onClick.RemoveAllListeners();
        returnToMapButton?.onClick.RemoveAllListeners();
        mapButton?.onClick.AddListener(() => manager?.ReturnToWorldMap());
        returnToMapButton?.onClick.AddListener(() => manager?.ReturnToWorldMap());
        sectButton?.onClick.AddListener(() => SectWorldInterface.Instance?.OpenSectLayout());
        discipleButton?.onClick.AddListener(() => manager?.OpenWindow(UIWindowId.DiscipleCenter));
        monthlyPlanButton?.onClick.AddListener(() => manager?.OpenWindow(UIWindowId.MonthlyPlan));
        missionButton?.onClick.AddListener(OpenMissionPanel);
        resourceButton?.onClick.AddListener(OpenResourcePanel);
        eventButton?.onClick.AddListener(OpenEventInbox);
        settlementButton?.onClick.AddListener(OpenLatestSettlement);
        endDayButton?.onClick.AddListener(() => TimeManager.Instance?.PauseByPlayer());
        speed1Button?.onClick.AddListener(() => SelectSpeed(1f));
        speed2Button?.onClick.AddListener(() => SelectSpeed(2f));
        speed4Button?.onClick.AddListener(() => SelectSpeed(4f));
    }

    private void BindDomainEvents()
    {
        if (listenersBound) return;
        listenersBound = true;
        if (manager != null) manager.WindowStateChanged += RefreshWindowState;
        if (GameFlowStateManager.Instance != null) GameFlowStateManager.Instance.StateChanged += ApplyFlowState;
        if (WarehouseManager.Instance != null) WarehouseManager.Instance.OnInventoryChanged += RefreshAll;
        if (NPCManager.Instance != null) NPCManager.Instance.OnRosterChanged += RefreshAll;
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnDayPassed += OnDayPassed;
            TimeManager.Instance.OnTimeChanged += OnTimeChanged;
            TimeManager.Instance.OnDaySettlementReady += OnSettlementReady;
        }
        if (PlayerManager.Instance != null) PlayerManager.Instance.OnFoundingChanged += RefreshAll;
        if (EventManager.Instance != null)
        {
            EventManager.Instance.OnEventPresented += OnEventPresented;
            EventManager.Instance.OnEventResolved += OnEventResolved;
        }
        WorldMapSession.ProgressChanged += RefreshAll;
    }

    private void OnDestroy()
    {
        if (!listenersBound) return;
        if (manager != null) manager.WindowStateChanged -= RefreshWindowState;
        if (GameFlowStateManager.Instance != null) GameFlowStateManager.Instance.StateChanged -= ApplyFlowState;
        if (WarehouseManager.Instance != null) WarehouseManager.Instance.OnInventoryChanged -= RefreshAll;
        if (NPCManager.Instance != null) NPCManager.Instance.OnRosterChanged -= RefreshAll;
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnDayPassed -= OnDayPassed;
            TimeManager.Instance.OnTimeChanged -= OnTimeChanged;
            TimeManager.Instance.OnDaySettlementReady -= OnSettlementReady;
        }
        if (PlayerManager.Instance != null) PlayerManager.Instance.OnFoundingChanged -= RefreshAll;
        if (EventManager.Instance != null)
        {
            EventManager.Instance.OnEventPresented -= OnEventPresented;
            EventManager.Instance.OnEventResolved -= OnEventResolved;
        }
        WorldMapSession.ProgressChanged -= RefreshAll;
    }

    private void ApplyFlowState(GameFlowState state)
    {
        bool leavingWorldMap = hasAppliedFlowState &&
                               lastAppliedFlowState == GameFlowState.WorldMap &&
                               state != GameFlowState.WorldMap;
        lastAppliedFlowState = state;
        hasAppliedFlowState = true;
        bool shellVisible = state == GameFlowState.WorldMap;
        if (shellRoot != null) shellRoot.SetActive(shellVisible);
        CharacterEventPanel eventPanel = Object.FindObjectOfType<CharacterEventPanel>(true);
        eventPanel?.SetLegacyInboxShortcutVisible(!shellVisible);
        // 首次进入 MainMenu/CharacterSetup 时，强制立宗面板可能刚由自己的 Start 打开。
        // 只有确实从世界地图离开时才清理世界地图窗口，避免启动顺序把立宗 UI 关闭。
        if (leavingWorldMap) manager?.CloseAllPanels();
        RefreshAll();
    }

    public void RefreshWindowState()
    {
        if (contextTitleText != null) contextTitleText.text = manager == null ? "世界地图" : manager.CurrentScreenTitle;
        if (returnToMapButton != null) returnToMapButton.gameObject.SetActive(manager != null && manager.HasOpenScreens);
        SetSelected(mapButton, manager == null || !manager.HasOpenScreens);
        SetSelected(sectButton, manager != null && manager.HasOpenScreens && manager.CurrentScreenTitle.Contains("宗门"));
        SetSelected(discipleButton, manager?.CurrentScreenId == UIWindowId.DiscipleCenter);
        SetSelected(monthlyPlanButton, manager?.CurrentScreenId == UIWindowId.MonthlyPlan);
        bool navigationAllowed = manager == null || !manager.HasOpenModals;
        if (mapButton != null) mapButton.interactable = navigationAllowed;
        if (sectButton != null) sectButton.interactable = navigationAllowed;
        if (discipleButton != null) discipleButton.interactable = navigationAllowed;
        if (monthlyPlanButton != null) monthlyPlanButton.interactable = navigationAllowed;
        if (missionButton != null) missionButton.interactable = navigationAllowed;
        if (resourceButton != null) resourceButton.interactable = navigationAllowed;
        if (returnToMapButton != null) returnToMapButton.interactable = navigationAllowed;
    }

    public void RefreshAll()
    {
        PlayerData player = PlayerManager.Instance?.playerData;
        if (sectNameText != null) sectNameText.text = string.IsNullOrWhiteSpace(player?.sectName) ? "未立宗" : player.sectName;
        int stones = WarehouseManager.Instance?.GetItemCount(FacilityRules.SpiritStoneId) ?? 0;
        int materials = WarehouseManager.Instance?.GetItemCount(FacilityRules.BasicMaterialId) ?? 0;
        int disciples = NPCManager.Instance?.GetAllNPC().Count(item => item?.Character?.IsAlive == true) ?? 0;
        if (resourceText != null) resourceText.text = $"灵石 {stones}　材料 {materials}　弟子 {disciples}";
        RefreshTimeControls();
        if (eventText != null) eventText.text = $"事件 {EventManager.Instance?.GetInbox().Count ?? 0}";
        RefreshWindowState();
    }

    private void RefreshTimeControls()
    {
        TimeManager time = TimeManager.Instance;
        if (dayText != null)
        {
            GameDateTime current = time?.CurrentDateTime ?? GameCalendarRules.FromActiveDay(1, 6f);
            dayText.text = $"{current.DateLabel}　{current.TimeLabel}";
        }
        if (settlementButton != null)
        {
            bool unread = time?.UnreadDaySettlement != null;
            TMP_Text label = settlementButton.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = unread ? "结算 ●" : "结算";
            settlementButton.interactable = unread;
        }
        SetSelected(endDayButton, time?.IsPaused == true);
        SetSelected(speed1Button, time != null && !time.IsPaused && Mathf.Approximately(time.SelectedSpeed, 1f));
        SetSelected(speed2Button, time != null && !time.IsPaused && Mathf.Approximately(time.SelectedSpeed, 2f));
        SetSelected(speed4Button, time != null && !time.IsPaused && Mathf.Approximately(time.SelectedSpeed, 4f));
        bool speedAllowed = time != null && (time.PauseReasons & ~PauseReason.Player) == PauseReason.None;
        if (speed1Button != null) speed1Button.interactable = speedAllowed;
        if (speed2Button != null) speed2Button.interactable = speedAllowed;
        if (speed4Button != null) speed4Button.interactable = speedAllowed;
    }

    private void OnDayPassed(int _) => RefreshAll();
    private void OnTimeChanged(GameDateTime _) => RefreshTimeControls();
    private void OnSettlementReady(DaySettlementSummary _) => RefreshAll();
    private void OnEventPresented(ActiveCharacterEvent _) => RefreshAll();
    private void OnEventResolved(EventHistoryRecord _) => RefreshAll();

    private static void OpenMissionPanel()
    {
        MissionPanel panel = Object.FindObjectOfType<MissionPanel>(true);
        if (panel != null) UIManager.Instance?.OpenPanel(panel.gameObject);
    }

    private static void OpenResourcePanel()
    {
        ResourceStatusPanel panel = Object.FindObjectOfType<ResourceStatusPanel>(true);
        panel?.OpenFromSectLayout();
    }

    private static void OpenEventInbox()
    {
        CharacterEventPanel panel = Object.FindObjectOfType<CharacterEventPanel>(true);
        panel?.OpenInbox();
    }

    private static void OpenLatestSettlement()
    {
        DaySettlementSummary unread = TimeManager.Instance?.UnreadDaySettlement;
        if (unread?.isMonthEnd == true && GrowthFeedbackRules.LatestReport(PlayerManager.Instance?.playerData) != null)
        {
            UIManager.Instance?.OpenWindow(UIWindowId.MonthlyReport,
                new MonthlyReportContext(unread.monthIndex, true));
            return;
        }
        DaySettlementPanel panel = Object.FindObjectOfType<DaySettlementPanel>(true);
        panel?.OpenLatest();
    }

    private static void SelectSpeed(float speed)
    {
        if (TimeManager.Instance == null) return;
        if (!TimeManager.Instance.TrySetSpeed(speed, out string reason) && !string.IsNullOrWhiteSpace(reason))
            Debug.LogWarning($"无法继续时间：{reason}");
    }

    private void ApplyTheme()
    {
        if (theme == null) return;
        foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
        {
            if (theme.font != null) text.font = theme.font;
            text.color = theme.text;
        }
    }

    private void SetSelected(Button button, bool selected)
    {
        if (button == null || theme == null) return;
        Image image = button.GetComponent<Image>();
        if (image != null) image.color = selected ? theme.accent : theme.card;
    }
}
