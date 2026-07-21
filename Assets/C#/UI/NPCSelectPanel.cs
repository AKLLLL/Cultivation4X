using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class NPCSelectPanel : MonoBehaviour
{
    [Header("列表")]
    public Transform content;

    [Header("按钮预制体")]
    public GameObject npcButtonPrefab;

    [SerializeField]
    private MissionPanel missionPanel;
    [SerializeField]
    private GameObject npcSelectPanel;
    private void OnEnable()
    {
        RefreshList();
    }

    /// <summary>
    /// 刷新NPC列表
    /// </summary>
    private void RefreshList()
    {
        // 删除旧按钮
        foreach (Transform child in content)
        {
            Destroy(child.gameObject);
        }

        List<NPCRuntime> npcList =
           NPCManager.Instance.GetAllNPC();

        foreach (NPCRuntime npc in npcList)
        {
            GameObject obj = Instantiate(npcButtonPrefab, content);

            TMP_Text text = obj.GetComponentInChildren<TMP_Text>();

            text.text = npc.Data.name;

            Button button = obj.GetComponent<Button>();
            // 忙碌时不可点击

            NPCRuntime runtime = npc;
            if (runtime == null)
            {
                Debug.LogError("找不到NPC运行数据：" + npc.Data.name);
                continue;
            }

            button.interactable = runtime.CanDispatch();


            if (runtime.CanDispatch())
            {
                button.onClick.AddListener(() =>
                {
                    SelectNPC(npc);
                });
            }
        }
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
