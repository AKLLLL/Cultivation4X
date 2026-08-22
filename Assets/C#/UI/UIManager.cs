using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>全局 UI Shell；旧面板继续通过 OpenPanel 使用兼容模态栈。</summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance;
    private const int ScreenSortingBase = 2000;
    private const int ModalSortingBase = 4000;
    private const int OverlaySortingBase = 6000;

    [SerializeField] private List<GameObject> panels = new List<GameObject>();
    [SerializeField] private Transform screenLayer;
    [SerializeField] private Transform modalLayer;
    [SerializeField] private Transform overlayLayer;
    [SerializeField] private GlobalHudView globalHud;
    [SerializeField] private UITheme theme;
    [SerializeField] private List<UIWindowRegistration> windowRegistrations = new List<UIWindowRegistration>();

    private readonly Stack<WindowEntry> screenStack = new Stack<WindowEntry>();
    // 保留字段名供现有诊断/测试读取；其内容即兼容 Modal 栈。
    private readonly Stack<WindowEntry> panelStack = new Stack<WindowEntry>();
    private Stack<WindowEntry> modalStack => panelStack;
    private readonly List<WindowEntry> overlayWindows = new List<WindowEntry>();
    private readonly Dictionary<UIWindowId, WindowEntry> cachedWindows = new Dictionary<UIWindowId, WindowEntry>();
    private readonly Dictionary<UIWindowId, UIWindowRegistration> registrations = new Dictionary<UIWindowId, UIWindowRegistration>();
    private int nextScreenOrder = ScreenSortingBase;
    private int nextModalOrder = ModalSortingBase;

    public event Action WindowStateChanged;
    public bool HasOpenScreens => screenStack.Count > 0;
    public bool HasOpenModals => modalStack.Count > 0;
    public bool HasBlockingWindow => screenStack.Any(item => item.blocksWorldInput) || modalStack.Any(item => item.blocksWorldInput);
    public bool HasOpenPanels => HasBlockingWindow;
    public string CurrentScreenTitle => screenStack.Count == 0 ? "世界地图" : screenStack.Peek().title;
    public UIWindowId? CurrentScreenId => screenStack.Count == 0 ? (UIWindowId?)null : screenStack.Peek().id;
    public UITheme Theme => theme;

    private sealed class WindowEntry
    {
        public GameObject panel;
        public Action onClose;
        public Canvas canvas;
        public bool previousOverrideSorting;
        public int previousSortingOrder;
        public int assignedSortingOrder;
        public UIWindowLayer layer;
        public UIEscapePolicy escapePolicy;
        public bool blocksWorldInput;
        public bool callbackInvoked;
        public bool cached;
        public UIWindowId? id;
        public string title;
        public IUIWindowLifecycle lifecycle;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyUtility.MarkPersistent(transform.root.gameObject);
        }
        else if (Instance != this)
        {
            Destroy(transform.root.gameObject);
            return;
        }

        BuildRegistrationIndex();
        foreach (GameObject panel in panels)
            if (panel != null) panel.SetActive(false);
        globalHud?.Bind(this, theme);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) CloseTopWindow();
    }

    public void Configure(Transform screen, Transform modal, Transform overlay, GlobalHudView hud,
        UITheme uiTheme, IEnumerable<UIWindowRegistration> windows)
    {
        screenLayer = screen;
        modalLayer = modal;
        overlayLayer = overlay;
        globalHud = hud;
        theme = uiTheme;
        windowRegistrations = windows == null ? new List<UIWindowRegistration>() : windows.ToList();
        BuildRegistrationIndex();
    }

    private void BuildRegistrationIndex()
    {
        registrations.Clear();
        foreach (UIWindowRegistration registration in windowRegistrations ?? new List<UIWindowRegistration>())
        {
            if (registration == null)
            {
                Debug.LogError("发现空的 UI 窗口注册项。");
                continue;
            }
            if (registration.prefab == null)
            {
                Debug.LogError($"UIWindowId 的 Prefab 引用无效: {registration.id}");
                continue;
            }
            if (registrations.ContainsKey(registration.id))
            {
                Debug.LogError($"UIWindowId 重复注册: {registration.id}");
                continue;
            }
            registrations.Add(registration.id, registration);
        }
    }

    public void OpenWindow(UIWindowId id, IUIWindowContext context = null)
    {
        if (!registrations.TryGetValue(id, out UIWindowRegistration registration))
        {
            Debug.LogError($"UIWindowId 未注册或 Prefab 无效: {id}");
            return;
        }
        if (!cachedWindows.TryGetValue(id, out WindowEntry entry) || entry.panel == null)
        {
            GameObject instance = Instantiate(registration.prefab, LayerRoot(registration.layer), false);
            instance.name = registration.prefab.name;
            Stretch(instance.transform as RectTransform);
            entry = CreateEntry(instance, null, registration.layer, registration.escapePolicy,
                registration.blocksWorldInput, registration.title, id, registration.cacheInstance);
            if (registration.cacheInstance) cachedWindows[id] = entry;
        }
        OpenEntry(entry, context);
    }

    public void CloseWindow(UIWindowId id)
    {
        if (cachedWindows.TryGetValue(id, out WindowEntry entry) && entry?.panel != null) CloseEntry(entry, true);
    }

    public void OpenScreen(GameObject legacyScreen, Action onClose = null)
    {
        if (legacyScreen == null) { Debug.LogWarning("UIScreen 为空"); return; }
        WindowEntry entry = FindEntry(legacyScreen) ?? CreateEntry(legacyScreen, onClose,
            UIWindowLayer.Screen, UIEscapePolicy.Allowed, true, DisplayName(legacyScreen.name), null, false);
        OpenEntry(entry, null);
    }

    public void OpenPanel(GameObject panel) => OpenPanel(panel, null);
    public void OpenPanel(GameObject panel, Action onClose) => OpenPanel(panel, onClose, UIEscapePolicy.Allowed);

    public void OpenPanel(GameObject panel, Action onClose, UIEscapePolicy escapePolicy)
    {
        if (panel == null) { Debug.LogWarning("UIPanel 为空"); return; }
        WindowEntry entry = FindEntry(panel) ?? CreateEntry(panel, onClose, UIWindowLayer.Modal,
            escapePolicy, true, DisplayName(panel.name), null, false);
        entry.escapePolicy = escapePolicy;
        if (onClose != null) entry.onClose = onClose;
        OpenEntry(entry, null);
    }

    private void OpenEntry(WindowEntry entry, IUIWindowContext context)
    {
        if (entry == null || entry.panel == null) return;
        RemoveFromStack(screenStack, entry.panel);
        RemoveFromStack(modalStack, entry.panel);
        overlayWindows.RemoveAll(item => item.panel == entry.panel);
        entry.callbackInvoked = false;
        if (entry.layer == UIWindowLayer.Screen)
        {
            if (screenStack.Count > 0)
            {
                WindowEntry previous = screenStack.Peek();
                previous.lifecycle?.OnFocusLost();
                if (previous.panel != null) previous.panel.SetActive(false);
            }
            entry.assignedSortingOrder = nextScreenOrder++;
            ApplySorting(entry);
            entry.panel.SetActive(true);
            ApplySorting(entry);
            screenStack.Push(entry);
        }
        else if (entry.layer == UIWindowLayer.Overlay)
        {
            entry.assignedSortingOrder = OverlaySortingBase + overlayWindows.Count;
            ApplySorting(entry);
            entry.panel.SetActive(true);
            ApplySorting(entry);
            overlayWindows.Add(entry);
        }
        else
        {
            if (modalStack.Count > 0) modalStack.Peek().lifecycle?.OnFocusLost();
            entry.assignedSortingOrder = nextModalOrder++;
            ApplySorting(entry);
            entry.panel.SetActive(true);
            ApplySorting(entry);
            modalStack.Push(entry);
        }
        entry.lifecycle?.OnOpened(context);
        entry.lifecycle?.OnFocusGained();
        NotifyStateChanged();
    }

    public void ClosePanel(GameObject panel)
    {
        if (panel == null) return;
        WindowEntry entry = FindEntry(panel);
        if (entry == null) { panel.SetActive(false); return; }
        CloseEntry(entry, true);
    }

    public void CloseTopPanel() => CloseTopWindow();

    public void CloseTopWindow()
    {
        if (modalStack.Count > 0)
        {
            WindowEntry modal = modalStack.Peek();
            if (modal.escapePolicy == UIEscapePolicy.Blocked) return;
            CloseEntry(modal, true);
            return;
        }
        if (screenStack.Count > 0) CloseEntry(screenStack.Peek(), true);
    }

    public void ReturnToWorldMap()
    {
        if (modalStack.Count > 0) return;
        while (screenStack.Count > 0) CloseEntry(screenStack.Peek(), false);
        NotifyStateChanged();
    }

    public void CloseAllPanels()
    {
        while (modalStack.Count > 0) CloseEntry(modalStack.Peek(), false);
        while (screenStack.Count > 0) CloseEntry(screenStack.Peek(), false);
        foreach (WindowEntry overlay in overlayWindows.ToArray()) CloseEntry(overlay, false);
        NotifyStateChanged();
    }

    private void CloseEntry(WindowEntry entry, bool restorePrevious)
    {
        if (entry == null) return;
        bool wasScreenTop = screenStack.Count > 0 && ReferenceEquals(screenStack.Peek(), entry);
        bool wasModalTop = modalStack.Count > 0 && ReferenceEquals(modalStack.Peek(), entry);
        RemoveFromStack(screenStack, entry.panel);
        RemoveFromStack(modalStack, entry.panel);
        overlayWindows.RemoveAll(item => item.panel == entry.panel);
        if (entry.panel != null) entry.panel.SetActive(false);
        RestoreCanvas(entry);
        entry.lifecycle?.OnClosed();
        InvokeClose(entry);
        if (!entry.cached && entry.id.HasValue && entry.panel != null) Destroy(entry.panel);
        if (restorePrevious && wasModalTop && modalStack.Count > 0) modalStack.Peek().lifecycle?.OnFocusGained();
        if (restorePrevious && wasScreenTop && screenStack.Count > 0)
        {
            WindowEntry previous = screenStack.Peek();
            if (previous.panel != null) previous.panel.SetActive(true);
            ApplySorting(previous);
            previous.lifecycle?.OnFocusGained();
        }
        NotifyStateChanged();
    }

    private WindowEntry CreateEntry(GameObject panel, Action onClose, UIWindowLayer layer,
        UIEscapePolicy escapePolicy, bool blocksWorldInput, string title, UIWindowId? id, bool cached)
    {
        Canvas canvas = EnsurePanelCanvas(panel);
        return new WindowEntry
        {
            panel = panel,
            onClose = onClose,
            canvas = canvas,
            previousOverrideSorting = canvas.overrideSorting,
            previousSortingOrder = canvas.sortingOrder,
            layer = layer,
            escapePolicy = escapePolicy,
            blocksWorldInput = blocksWorldInput,
            title = string.IsNullOrWhiteSpace(title) ? DisplayName(panel.name) : title,
            id = id,
            cached = cached,
            lifecycle = panel.GetComponent<IUIWindowLifecycle>()
        };
    }

    private WindowEntry FindEntry(GameObject panel)
    {
        WindowEntry entry = screenStack.FirstOrDefault(item => item.panel == panel) ??
                            modalStack.FirstOrDefault(item => item.panel == panel) ??
                            overlayWindows.FirstOrDefault(item => item.panel == panel);
        return entry ?? cachedWindows.Values.FirstOrDefault(item => item.panel == panel);
    }

    private Transform LayerRoot(UIWindowLayer layer)
    {
        if (layer == UIWindowLayer.Screen && screenLayer != null) return screenLayer;
        if (layer == UIWindowLayer.Modal && modalLayer != null) return modalLayer;
        if (layer == UIWindowLayer.Overlay && overlayLayer != null) return overlayLayer;
        return transform;
    }

    private static WindowEntry RemoveFromStack(Stack<WindowEntry> stack, GameObject panel)
    {
        Stack<WindowEntry> temporary = new Stack<WindowEntry>();
        WindowEntry removed = null;
        while (stack.Count > 0)
        {
            WindowEntry entry = stack.Pop();
            if (removed == null && entry.panel == panel) removed = entry;
            else temporary.Push(entry);
        }
        while (temporary.Count > 0) stack.Push(temporary.Pop());
        return removed;
    }

    private static Canvas EnsurePanelCanvas(GameObject panel)
    {
        Canvas canvas = panel.GetComponent<Canvas>();
        if (canvas == null) canvas = panel.AddComponent<Canvas>();
        if (panel.GetComponent<GraphicRaycaster>() == null) panel.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static void ApplySorting(WindowEntry entry)
    {
        if (entry?.canvas == null) return;
        entry.canvas.overrideSorting = true;
        entry.canvas.sortingOrder = entry.assignedSortingOrder;
    }

    private static void RestoreCanvas(WindowEntry entry)
    {
        if (entry?.canvas == null) return;
        entry.canvas.overrideSorting = entry.previousOverrideSorting;
        entry.canvas.sortingOrder = entry.previousSortingOrder;
    }

    private static void InvokeClose(WindowEntry entry)
    {
        if (entry == null || entry.callbackInvoked) return;
        entry.callbackInvoked = true;
        entry.onClose?.Invoke();
    }

    private void NotifyStateChanged()
    {
        WindowStateChanged?.Invoke();
        globalHud?.RefreshWindowState();
    }

    private static string DisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "界面";
        switch (value)
        {
            case "SectBrief":
            case "SectLayout":
            case "StewardHall":
            case "SectSummary": return "宗门管理";
            case "MissionPanel": return "任务中心";
            case "WarehousePanel":
            case "ResourceStatusPanel": return "资源与库藏";
            default: return value.Replace("Panel", string.Empty);
        }
    }

    private static void Stretch(RectTransform rect)
    {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
