using TMPro;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
/// <summary>
/// 任务节点选择界面
/// </summary>
public class MissionNodePanel : MonoBehaviour
{

    public TMP_Text titleText;

    public TMP_Text descriptionText;

    [Header("三个选项按钮")]
    public Button option1Button;
    public Button option2Button;
    public Button option3Button;
    [Header("按钮文字")]
    public TMP_Text option1Text;
    public TMP_Text option2Text;
    public TMP_Text option3Text;
    //当前等待选择的任务
    private Mission currentMission;

   // private List<MissionOptionButton> buttons =new List<MissionOptionButton>();
    /// <summary>
    /// 打开节点界面
    /// </summary>

    public void Show(
        Mission mission)
    {

        currentMission = mission;


        MissionNodeData node =
            mission.CurrentNode;

        titleText.text =
            node.title;

        descriptionText.text =
            node.description;
        //先全部隐藏

        option1Button.gameObject.SetActive(false);

        option2Button.gameObject.SetActive(false);

        option3Button.gameObject.SetActive(false);
        //根据选项数量显示

        if (node.options.Count >= 1)
        {
            option1Button.gameObject.SetActive(true);

            option1Text.text =
                node.options[0].text;

            option1Button.onClick.RemoveAllListeners();

            option1Button.onClick.AddListener(
                () =>
                {
                    SelectOption(0);
                });

        }
        if (node.options.Count >= 2)
        {

            option2Button.gameObject.SetActive(true);

            option2Text.text =
                node.options[1].text;

            option2Button.onClick.RemoveAllListeners();

            option2Button.onClick.AddListener(
                () =>
                {
                    SelectOption(1);
                });
        }
        if (node.options.Count >= 3)
        {
            option3Button.gameObject.SetActive(true);

            option3Text.text =
                node.options[2].text;

            option3Button.onClick.RemoveAllListeners();

            option3Button.onClick.AddListener(
                () =>
                {
                    SelectOption(2);
                });

        }

        gameObject.SetActive(true);

    }




    /// <summary>
    /// 玩家选择
    /// </summary>
    public void SelectOption(
        int index)
    {

        if (currentMission == null)
            return;



        currentMission.SelectOption(index);



        gameObject.SetActive(false);


        currentMission = null;

    }

}