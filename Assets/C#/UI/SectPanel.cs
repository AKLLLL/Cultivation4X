using System.Collections.Generic;
using System.Collections;
using UnityEngine;

/// <summary>
/// 宗门NPC管理面板
/// 显示所有弟子
/// </summary>
public class SectPanel : MonoBehaviour
{


    [Header("NPC格子Prefab")]

    public NPCSlotUI npcSlotPrefab;



    [Header("列表父节点")]

    public Transform content;



    [Header("NPC详情")]

    public NPCInfoPanel infoPanel;



    private List<NPCSlotUI> slots =
        new List<NPCSlotUI>();
    private bool rosterSubscribed;




    private void OnEnable()
    {
        SubscribeRoster();
        StartCoroutine(RefreshWhenReady());
    }

    private void OnDisable()
    {
        if (rosterSubscribed && NPCManager.Instance != null) NPCManager.Instance.OnRosterChanged -= Refresh;
        rosterSubscribed = false;
    }

    private IEnumerator RefreshWhenReady()
    {
        yield return null;
        SubscribeRoster();
        Refresh();
    }

    private void SubscribeRoster()
    {
        if (rosterSubscribed || NPCManager.Instance == null) return;
        NPCManager.Instance.OnRosterChanged += Refresh;
        rosterSubscribed = true;
    }



    /// <summary>
    /// 刷新NPC列表
    /// </summary>

    public void Refresh()
    {

        //清理旧格子

        if (NPCManager.Instance == null)
        {
            Debug.LogWarning("SectPanel：NPCManager 尚未初始化，跳过本次刷新。");
            return;
        }

        if (npcSlotPrefab == null || content == null)
        {
            Debug.LogWarning("SectPanel：NPC列表引用未绑定，无法刷新。");
            return;
        }

        foreach (var slot in slots)
        {
            Destroy(slot.gameObject);
        }


        slots.Clear();



        List<NPCRuntime> npcs =
            NPCManager.Instance.GetAllNPC();



        foreach (var npc in npcs)
        {

            NPCSlotUI slot =
            Instantiate(
                npcSlotPrefab,
                content
            );


            slot.SetNPC(
                npc,
                this
            );


            slots.Add(slot);

        }


    }




    /// <summary>
    /// 点击NPC
    /// </summary>

    public void SelectNPC(
        NPCRuntime npc)
    {

        Debug.Log(
        $"查看NPC：{npc.Data.npcName}"
        );


        UIManager.Instance.OpenPanel(
            infoPanel.gameObject
        );


        infoPanel.Show(npc);

    }

}
