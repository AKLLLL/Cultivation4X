using System;

/// <summary>
/// 单个物品堆
/// </summary>
[Serializable]
public class ItemStack
{
    /// <summary>
    /// 物品ID
    /// 对应JSON里的itemId
    /// 例如：
    /// herb
    /// spiritStone
    /// </summary>
    public string itemId;


    /// <summary>
    /// 数量
    /// </summary>
    public int count;
}