using TMPro;
using UnityEngine;

/// <summary>
/// NPC详细信息面板
/// </summary>
public class NPCInfoPanel : MonoBehaviour
{


    public TMP_Text nameText;

    public TMP_Text levelText;

    public TMP_Text expText;

    public TMP_Text goldText;

    public TMP_Text stateText;



    private NPCRuntime currentNPC;



    public void Show(
        NPCRuntime npc)
    {

        currentNPC = npc;


        nameText.text =
            npc.Data.npcName;


        levelText.text =
            "等级：" + npc.Level;


        expText.text =
            "经验：" + npc.Exp;


        goldText.text =
            "金币：" + npc.Gold;


        stateText.text =
            "状态：" + npc.State;

    }



    public void Hide()
    {

        UIManager.Instance.ClosePanel(
            gameObject
        );

    }


}