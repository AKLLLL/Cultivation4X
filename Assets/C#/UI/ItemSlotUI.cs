using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 单个仓库物品格子
/// 负责显示一个物品
/// 不负责仓库逻辑
/// </summary>
public class ItemSlotUI : MonoBehaviour

{
    [Header("物品图标")]
    public Image iconImage;

    [Header("数量文字")]
    public TMP_Text countText;

    [Header("品质边框（以后使用）")]
    public Image borderImage;





    private WarehousePanel warehousePanel;
    private ItemData currentItemData;
    private Button button;
    /// <summary>
    /// 当前格子保存的物品
    /// </summary>
    private ItemStack currentItem;

    /// <summary>
    /// 初始化一个格子
    /// WarehousePanel 创建格子时调用
    /// </summary>

    private void Awake()
    {
        button = GetComponent<Button>();

        button.onClick.AddListener(OnClick);
    }
    public void SetItem(ItemStack item, WarehousePanel panel)
    {
        currentItem = item;
        warehousePanel = panel;
        // 显示数量
        countText.text = "×" + item.count;
        // 读取物品数据库
        ItemData itemData =
            ItemDatabase.Instance.GetItem(item.itemId);

        if (itemData == null)
        {
            Debug.LogWarning($"找不到物品：{item.itemId}");
            return;
        }

        //==============================
        // 暂时没有图标
        // 所以这里只打印名字
        // 下一步再加载Sprite
        //==============================

        Debug.Log($"显示物品：{itemData.itemName}");

        //==============================
        // TODO
        // 下一步：
        // iconImage.sprite = ...
        //==============================

    }


    // 获取当前物品
    public ItemStack GetItem()
    {
        return currentItem;
    }
    // 点击格子
    public void OnPointerClick(PointerEventData eventData)
    {
        if (warehousePanel == null)
            return;

        warehousePanel.SelectItem(currentItem);
    }
    public void OnClick()
    {
        if (warehousePanel == null)
            return;

        warehousePanel.SelectItem(currentItem);
    }
}