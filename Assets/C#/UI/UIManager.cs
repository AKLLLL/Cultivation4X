using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI管理器
/// 所有UI统一由这里管理。
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance;
    private const int ManagedPanelSortingBase = 2000;
    private int nextSortingOrder = ManagedPanelSortingBase;

    [Header("所有UI面板")]
    [SerializeField]
    private List<GameObject> panels = new List<GameObject>();

    /// <summary>
    /// 当前打开的面板
    /// 后打开的位于栈顶。
    /// </summary>
    private readonly Stack<PanelEntry> panelStack = new Stack<PanelEntry>();

    private sealed class PanelEntry
    {
        public GameObject panel;
        public Action onClose;
        public Canvas canvas;
        public bool previousOverrideSorting;
        public int previousSortingOrder;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyUtility.MarkPersistent(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // 游戏开始全部关闭
        foreach (GameObject panel in panels)
        {
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CloseTopPanel();
        }
    }

    /// <summary>
    /// 打开面板
    /// </summary>
    public void OpenPanel(GameObject panel)
    {
        OpenPanel(panel, null);
    }

    public void OpenPanel(GameObject panel, Action onClose)
    {
        if (panel == null)
        {
            Debug.LogWarning("UIPanel为空");
            return;
        }
        // 已经打开，不重复压栈
        if (panel.activeSelf)
            return;

        Canvas canvas = EnsurePanelCanvas(panel);
        PanelEntry entry = new PanelEntry
        {
            panel = panel,
            onClose = onClose,
            canvas = canvas,
            previousOverrideSorting = canvas.overrideSorting,
            previousSortingOrder = canvas.sortingOrder
        };
        canvas.overrideSorting = true;
        canvas.sortingOrder = nextSortingOrder++;
        panel.SetActive(true);
        panelStack.Push(entry);
    }

    /// <summary>
    /// 关闭指定面板
    /// </summary>
    public void ClosePanel(GameObject panel)
    {
        if (panel == null)
            return;

        PanelEntry entry = RemoveFromStack(panel);
        panel.SetActive(false);
        RestoreCanvas(entry);
        entry?.onClose?.Invoke();

       // Debug.Log($"关闭面板：{panel.panelName}");
    }

    /// <summary>
    /// Esc关闭最上层面板
    /// </summary>
    public void CloseTopPanel()
    {
        while (panelStack.Count > 0)
        {
            PanelEntry entry = panelStack.Pop();
            GameObject panel = entry.panel;

            if (panel==null)
                continue;
            Debug.Log("关闭：" + panel.name);
            panel.SetActive(false);
            RestoreCanvas(entry);
            entry.onClose?.Invoke();

            return;
        }
    }

    /// <summary>
    /// 从Stack移除指定面板
    /// Stack本身不能删除中间元素，因此需要重建。
    /// </summary>
    private PanelEntry RemoveFromStack(GameObject panel)
    {
        if (panelStack.Count == 0)
            return null;

        Stack<PanelEntry> temp = new Stack<PanelEntry>();
        PanelEntry removed = null;

        while (panelStack.Count > 0)
        {
            PanelEntry entry = panelStack.Pop();

            if (entry.panel != panel)
            {
                temp.Push(entry);
            }
            else if (removed == null) removed = entry;
        }

        while (temp.Count > 0)
        {
            panelStack.Push(temp.Pop());
        }
        return removed;
    }

    private static Canvas EnsurePanelCanvas(GameObject panel)
    {
        Canvas canvas = panel.GetComponent<Canvas>();
        if (canvas == null) canvas = panel.AddComponent<Canvas>();
        if (panel.GetComponent<GraphicRaycaster>() == null) panel.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static void RestoreCanvas(PanelEntry entry)
    {
        if (entry?.canvas == null) return;
        entry.canvas.overrideSorting = entry.previousOverrideSorting;
        entry.canvas.sortingOrder = entry.previousSortingOrder;
    }
}
