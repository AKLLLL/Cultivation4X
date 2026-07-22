using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

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
    private RectTransform dynamicList;
    private TMP_Text statusText;
    private void Start()
    {
        foreach (MissionButton item in buttons)
        {
            if (item.button != null) item.button.gameObject.SetActive(false);
        }
        EnsureDynamicUI();
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
        if (!MissionManager.Instance.CanTriggerMission(selectedMissionData.id, selectedNPC, out string reason))
        { if (statusText != null) statusText.text = reason; return; }
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
        EnsureDynamicUI();
        RefreshDynamicList();
        selectedMissionData = null;
        SetSelectedNPC(null);
       
    }

    private void EnsureDynamicUI()
    {
        if (dynamicList != null || missionPanel == null) return;
        dynamicList = RuntimeUIFactory.Panel(missionPanel.transform, "DailyMissions", new Vector2(0.52f, 0.08f), new Vector2(0.97f, 0.92f));
        statusText = RuntimeUIFactory.Text(dynamicList, string.Empty, 17, 52);
    }

    private void RefreshDynamicList()
    {
        if (dynamicList == null || MissionManager.Instance == null) return;
        for (int i = dynamicList.childCount - 1; i >= 1; i--) Destroy(dynamicList.GetChild(i).gameObject);
        int hall = PlayerManager.Instance == null ? 1 : PlayerManager.Instance.GetFacilityLevel(FacilityType.MissionHall);
        int running = MissionManager.Instance.GetActiveMissions().Count(m => !m.Data.isFacilityAction &&
            (m.State == MissionState.Active || m.State == MissionState.WaitingNode));
        statusText.text = $"今日任务　任务堂 Lv.{hall}　并行 {running}/{FacilityRules.MissionConcurrency(hall)}";
        foreach (string id in MissionManager.Instance.GetDailyMissionCandidateIds()) AddMissionButton(MissionManager.Instance.GetMissionData(id));
        RuntimeUIFactory.Text(dynamicList, "设施行动", 20, 34);
        foreach (MissionData data in MissionManager.Instance.GetMissionPool().Where(item => item.isFacilityAction)) AddMissionButton(data);
        foreach (Mission mission in MissionManager.Instance.GetActiveMissions().Where(item => item.State == MissionState.AwaitingReward).ToList())
        {
            Button claim = RuntimeUIFactory.Button(dynamicList, $"领取：{mission.Data.name}");
            claim.onClick.AddListener(() => { if (!MissionManager.Instance.TryClaimReward(mission, out string reason)) statusText.text = reason; RefreshDynamicList(); });
        }
    }

    private void AddMissionButton(MissionData data)
    {
        if (data == null) return;
        string reason = GeneralLockReason(data);
        int days = data.isFacilityAction && PlayerManager.Instance != null
            ? FacilityRules.ActionDays(data.requiredFacility, PlayerManager.Instance.GetFacilityLevel(data.requiredFacility)) : data.needDays;
        Button button = RuntimeUIFactory.Button(dynamicList, string.IsNullOrEmpty(reason)
            ? $"{data.name}　{days}天　消耗 {data.goldCost}灵材" : $"{data.name}（{reason}）", 48);
        button.interactable = string.IsNullOrEmpty(reason);
        button.onClick.AddListener(() => SelectMission(data.id));
    }

    private string GeneralLockReason(MissionData data)
    {
        if (PlayerManager.Instance == null || WarehouseManager.Instance == null) return "系统未初始化";
        if (data.requiredFacilityLevel > PlayerManager.Instance.GetFacilityLevel(data.requiredFacility)) return "设施等级不足";
        if (PlayerManager.Instance.playerData.gold < data.goldCost) return "灵材不足";
        foreach (ItemReward cost in data.itemCosts ?? new List<ItemReward>())
            if (WarehouseManager.Instance.GetItemCount(cost.itemId) < cost.count) return "材料不足";
        if (data.isFacilityAction && MissionManager.Instance.GetActiveMissions().Any(m => m.Data.isFacilityAction &&
            m.Data.requiredFacility == data.requiredFacility && (m.State == MissionState.Active || m.State == MissionState.WaitingNode))) return "设施忙碌";
        return null;
    }
}
