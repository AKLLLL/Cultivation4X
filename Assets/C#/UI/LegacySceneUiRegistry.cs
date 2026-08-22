using System.Collections.Generic;
using UnityEngine;

/// <summary>保留旧场景面板引用并在场景加载时关闭；不参与窗口调度。</summary>
public sealed class LegacySceneUiRegistry : MonoBehaviour
{
    [SerializeField] private List<GameObject> legacyPanels = new List<GameObject>();

    public void Configure(IEnumerable<GameObject> panels)
    {
        legacyPanels = panels == null ? new List<GameObject>() : new List<GameObject>(panels);
    }

    public void OpenPanel(GameObject panel)
    {
        if (panel == null) return;
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(panel);
        else panel.SetActive(true);
    }

    private void Awake()
    {
        foreach (GameObject panel in legacyPanels)
            if (panel != null) panel.SetActive(false);
    }
}
