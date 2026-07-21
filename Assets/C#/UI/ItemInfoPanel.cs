using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 物品详情面板
/// 负责显示和隐藏物品信息
/// </summary>
public class ItemInfoPanel : MonoBehaviour
{
    [Header("详细信息")]
    public Image icon;
    public TMP_Text itemName;
    public TMP_Text itemType;
    public TMP_Text itemQuality;
    public TMP_Text itemCount;
    public TMP_Text itemPrice;
    public TMP_Text itemDescription;

    /// <summary>
    /// 显示物品信息
    /// </summary>
    public void Show(ItemData data, ItemStack stack)
    {
        itemName.text = data.itemName;
        itemType.text = "类型：" + ItemTypeUtility.GetDisplayName(data.itemType);
        itemQuality.text = "品质：" + ItemTypeUtility.GetDisplayName(data.itemType);
        itemCount.text = "数量：" + stack.count;
        itemPrice.text = "售价：" + data.price;
        itemDescription.text = data.description;

        // 下一步接入图标系统
        // icon.sprite = data.icon;

        Debug.Log($"显示物品详情：{data.itemName}");
    }
    public void Hide()
    {
         UIManager.Instance.ClosePanel(gameObject);
    }

}