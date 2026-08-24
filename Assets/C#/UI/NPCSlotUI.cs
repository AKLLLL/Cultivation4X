using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 宗门NPC列表中的单个格子
/// 负责显示NPC信息
/// 不负责NPC逻辑
/// </summary>
public class NPCSlotUI : MonoBehaviour
{

    [Header("显示")]
    public TMP_Text npcNameText;

    public TMP_Text levelText;

    public TMP_Text stateText;


    //当前显示的NPC
    private NPCRuntime currentNPC;


    private Button button;


    private SectPanel sectPanel;



    private void Awake()
    {
        button = GetComponent<Button>();

        button.onClick.AddListener(OnClick);

    }



    /// <summary>
    /// 初始化格子
    /// SectPanel创建时调用
    /// </summary>

    public void SetNPC(
        NPCRuntime npc,
        SectPanel panel)
    {

        currentNPC = npc;

        sectPanel = panel;



        npcNameText.text =
            npc.Data.npcName;



        levelText.text =
            "境界：练气" + npc.RealmLayer + "层";



        stateText.text =
            "状态：" + npc.State;


    }




    private void OnClick()
    {

        if (currentNPC == null)
            return;


        sectPanel.SelectNPC(currentNPC);

    }

}
