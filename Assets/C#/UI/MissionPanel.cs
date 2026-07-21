using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class MissionPanel : MonoBehaviour
{
    [Header("UI引用")]
    [SerializeField] private GameObject missionPanel;
    [SerializeField] private GameObject npcSelectPanel;


    [System.Serializable]
    public class MissionButton
    {
        public string missionId;
        
        public Button button;
    }

    public MissionButton[] buttons;

    // 当前选择的任务
    private MissionData selectedMissionData;
    // 当前派遣的NPC
    [Header("NPC选择")]
    private NPCRuntime selectedNPC;
    public TMP_Text npcNameText;

    public Button selectNPCButton;
    private void Start()
    {
        foreach (MissionButton item in buttons)
        {
            string id = item.missionId;

            item.button.onClick.AddListener(() =>
            {
                SelectMission(id);
            });
        }
    }

  public  void SelectMission(string id)
    {
        // 根据任务ID获取Mission对象
        selectedMissionData = MissionManager.Instance.GetMissionData(id);

        if (selectedMissionData == null)
        {
            Debug.LogError($"找不到任务：{id}");
            return;
        }

        Debug.Log($"已选择任务：{selectedMissionData.name}");

  
    }
   
    /// <summary>
    /// 设置当前派遣NPC
    /// </summary>
    public void SetSelectedNPC(NPCRuntime npc)
    {
        selectedNPC = npc;

        if (npc == null)
        {
            npcNameText.text = "请选择NPC";
        }
        else
        {
            npcNameText.text = npc.Data.name;
            Debug.Log($"已选择NPC：{npc.Data.name}");
        }
        
    }


    public void OnStartButtonClick()
    {
        if (selectedMissionData == null)
        {
            Debug.Log("请选择任务");
            return;
        }
        if (selectedNPC == null)
        {
            Debug.Log("请选择NPC");
            return;
        }
        MissionManager.Instance.TriggerMission(selectedMissionData.id, selectedNPC);

        // 关闭任务面板
        UIManager.Instance.ClosePanel(missionPanel);
    }


    public void OnSelectNPCButtonClick()
    {
        UIManager.Instance.ClosePanel(missionPanel);
    }

    public void OnCancelButtonClick()
    {
        selectedMissionData = null;

        UIManager.Instance.ClosePanel(missionPanel);
    }

    private void OnEnable()
    {

        selectedMissionData = null;
        SetSelectedNPC(null);
       
    }
}