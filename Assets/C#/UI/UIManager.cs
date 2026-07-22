using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI管理器
/// 所有UI统一由这里管理。
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("所有UI面板")]
    [SerializeField]
    private List<GameObject> panels = new List<GameObject>();

    /// <summary>
    /// 当前打开的面板
    /// 后打开的位于栈顶。
    /// </summary>
    private Stack<GameObject> panelStack = new Stack<GameObject>();

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
        if (panel == null)
        {
            Debug.LogWarning("UIPanel为空");
            return;
        }
        // 已经打开，不重复压栈
        if (panel.activeSelf)
            return;

        panel.SetActive(true);

        panelStack.Push(panel);
    }

    /// <summary>
    /// 关闭指定面板
    /// </summary>
    public void ClosePanel(GameObject panel)
    {
        if (panel == null)
            return;

        panel.SetActive(false);

        RemoveFromStack(panel);

       // Debug.Log($"关闭面板：{panel.panelName}");
    }

    /// <summary>
    /// Esc关闭最上层面板
    /// </summary>
    public void CloseTopPanel()
    {
        while (panelStack.Count > 0)
        {
            GameObject panel = panelStack.Pop();

            if (panel==null)
                continue;
            Debug.Log("关闭：" + panel.name);
            panel.SetActive(false);

            return;
        }
    }

    /// <summary>
    /// 从Stack移除指定面板
    /// Stack本身不能删除中间元素，因此需要重建。
    /// </summary>
    private void RemoveFromStack(GameObject panel)
    {
        if (panelStack.Count == 0)
            return;

        Stack<GameObject> temp = new Stack<GameObject>();

        while (panelStack.Count > 0)
        {
            GameObject p = panelStack.Pop();

            if (p != panel)
            {
                temp.Push(p);
            }
        }

        while (temp.Count > 0)
        {
            panelStack.Push(temp.Pop());
        }
    }
}
