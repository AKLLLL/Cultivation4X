using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 测试随机事件按钮
/// 后期可以删除
/// </summary>
public class RandomEventButton : MonoBehaviour
{

    public Button button;


    private void Start()
    {
        button.onClick.AddListener(
            TriggerEvent
        );
    }



    public void TriggerEvent()
    {

        //测试获取第一个NPC

        NPCRuntime npc =
            NPCManager.Instance
            .GetAllNPC()[0];


        RandomEventManager.Instance
        .TriggerRandomEvent(npc);

    }

}