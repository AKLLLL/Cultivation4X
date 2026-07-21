using System;

/// <summary>
/// 单个物品配置
/// 对应一个 Json 文件
/// </summary>
[Serializable]
public class ItemData
{
    // 物品ID（唯一）
    public string itemId;
    // 名称
    public string itemName;
   // 描述
    public string description;
    // 类型
    public ItemType itemType;
    // 品质
    public string quality;
    //品阶
    public int rank;
    // 售价
    public int price;
    // 是否可以堆叠
    public bool stackable;
    // 最大堆叠数量
    public int maxStack;
    // 图标路径(Resources)
    public string icon;
}