using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class NPCSelectPanel : MonoBehaviour
{
    private const int PageSize = 8;

    [Header("列表")]
    public Transform content;

    [Header("按钮预制体")]
    public GameObject npcButtonPrefab;

    [SerializeField]
    private MissionPanel missionPanel;
    [SerializeField]
    private GameObject npcSelectPanel;
    private int currentPage;

    private void OnEnable()
    {
        transform.SetAsLastSibling();
        RefreshList();
    }

    /// <summary>
    /// 刷新NPC列表
    /// </summary>
    private void RefreshList()
    {
        if (content == null || NPCManager.Instance == null) return;
        ConfigureContentLayout();
        // 删除旧按钮
        foreach (Transform child in content)
        {
            Destroy(child.gameObject);
        }

        List<NPCRuntime> npcList = NPCManager.Instance.GetAllNPC();
        int pageCount = CalculatePageCount(npcList.Count);
        currentPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, pageCount - 1));
        if (pageCount > 1)
        {
            RectTransform tabs = RuntimeUIFactory.TabBar(content, "NPCPageTabs");
            for (int page = 0; page < pageCount; page++)
            {
                int capturedPage = page;
                Button tab = RuntimeUIFactory.TabButton(tabs, $"第{page + 1}页", currentPage == page, 38);
                tab.onClick.AddListener(() =>
                {
                    if (currentPage == capturedPage) return;
                    currentPage = capturedPage;
                    RefreshList();
                });
            }
        }

        foreach (NPCRuntime npc in npcList.Skip(CalculatePageStart(currentPage)).Take(PageSize))
        {
            GameObject obj = Instantiate(npcButtonPrefab, content);

            TMP_Text text = obj.GetComponentInChildren<TMP_Text>();

            text.text = npc.Character?.displayName ?? npc.Data?.npcName ?? "未知弟子";

            Button button = obj.GetComponent<Button>();
            // 忙碌时不可点击

            NPCRuntime runtime = npc;
            if (runtime == null)
            {
                Debug.LogError("找不到NPC运行数据：" + (npc.Character?.displayName ?? npc.Data?.npcName));
                continue;
            }

            button.interactable = MissionManager.Instance != null
                ? MissionManager.Instance.CanDispatchOrCancelAutonomous(runtime)
                : runtime.CanDispatch();


            if (button.interactable)
            {
                button.onClick.AddListener(() =>
                {
                    SelectNPC(npc);
                });
            }
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(content as RectTransform);
    }

    private void ConfigureContentLayout()
    {
        ContentSizeFitter fitter = content == null ? null : content.GetComponent<ContentSizeFitter>();
        if (fitter != null)
        {
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
        VerticalLayoutGroup layout = content == null ? null : content.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
            layout.childControlWidth = true;
    }

    private static int CalculatePageCount(int npcCount)
    {
        return npcCount <= 0 ? 1 : (npcCount + PageSize - 1) / PageSize;
    }

    private static int CalculatePageStart(int page)
    {
        return Mathf.Max(0, page) * PageSize;
    }

    /// <summary>
    /// 选择NPC
    /// </summary>
    private void SelectNPC(NPCRuntime npc)
    {
       

        if (missionPanel != null)
        {
            missionPanel.SetSelectedNPC(npc);
        }

        UIManager.Instance.ClosePanel(npcSelectPanel);
    }

    public void OnCancelButtonClick()
    {
      UIManager.Instance.ClosePanel(npcSelectPanel);
    }
}
