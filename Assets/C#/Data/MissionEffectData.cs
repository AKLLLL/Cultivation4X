using System;


[Serializable]
public class MissionEffectData
{
    // 效果类型
    public string type;

    // 数值
    public int value;
    //物品ID
    //AddItem RemoveItem使用
    public string itemId;
    //物品数量
    public int count;
    //触发事件ID
    //TriggerEvent使用
    public string eventId;
}