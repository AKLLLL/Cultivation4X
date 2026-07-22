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
#if !UNITY_EDITOR
        if (!Debug.isDebugBuild) { gameObject.SetActive(false); return; }
#endif
        button.onClick.AddListener(
            TriggerEvent
        );
    }



    public void TriggerEvent()
    {

        //测试获取第一个NPC

        var living = NPCManager.Instance?.GetLivingNPC();
        NPCRuntime npc = living != null && living.Count > 0 ? living[0] : null;
        if (npc != null) EventManager.Instance?.DebugEnqueueEvent(npc);

    }

}
