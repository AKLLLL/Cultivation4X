using System.Collections.Generic;
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




    private void OnEnable()
    {

        Refresh();

    }



    /// <summary>
    /// 刷新NPC列表
    /// </summary>

    public void Refresh()
    {

        //清理旧格子

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