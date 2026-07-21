using UnityEngine;

/// <summary>
/// 物品类型显示转换
/// 
/// 程序使用Enum
/// UI显示中文
/// </summary>
public static class ItemTypeUtility
{


    public static string GetDisplayName(ItemType type)
    {

        switch (type)
        {

            case ItemType.Resource:
                return "基础资源";


            case ItemType.Herb:
                return "灵草";


            case ItemType.Material:
                return "炼器材料";


            case ItemType.AlchemyMaterial:
                return "炼丹材料";


            case ItemType.Pill:
                return "丹药";


            case ItemType.Weapon:
                return "法宝";


            case ItemType.Technique:
                return "功法";


            case ItemType.Talisman:
                return "符箓";


            case ItemType.BeastEgg:
                return "灵兽";


            case ItemType.QuestItem:
                return "任务物品";


            default:
                return "未知";
        }

    }


}