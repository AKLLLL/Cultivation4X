using System;
/// <summary>
/// 单个物品奖励
/// </summary>
[Serializable]
public class ItemReward
{
    /// <summary>
    /// 物品ID
    /// 与 ItemData.json 对应
    /// </summary>
    public string itemId;

    /// <summary>
    /// 数量
    /// </summary>
    public int count;
}