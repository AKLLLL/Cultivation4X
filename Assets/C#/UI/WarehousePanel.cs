using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
/// <summary>
/// 仓库界面
/// 负责读取仓库数据并生成物品格子
/// </summary>
public class WarehousePanel : MonoBehaviour
{
    [Header("格子父物体（ScrollView/Viewport/Content）")]
    public Transform content;

    [Header("物品格子预制体")]
    public GameObject itemSlotPrefab;
    [Header("物品详情面板")]
    public ItemInfoPanel itemInfoPanel;
    /// <summary>
    /// 当前生成出来的所有格子
    /// 用于刷新时删除
    /// </summary>
    private List<GameObject> slotObjects =
        new List<GameObject>();

    private void OnEnable()
    {
        RefreshWarehouse();
        if (itemInfoPanel != null)
        {
            itemInfoPanel.Hide();
        }
    }

    /// <summary>
    /// 刷新整个仓库
    /// </summary>
    public void RefreshWarehouse()
    {
        //==============================
        // 删除旧格子
        //==============================

        foreach (GameObject obj in slotObjects)
        {
            Destroy(obj);
        }

        slotObjects.Clear();

        //==============================
        // 获取仓库数据
        //=============================
        WarehouseData data =
            WarehouseManager.Instance.GetWarehouseData();

        if (data == null)
        {
            Debug.LogWarning("仓库数据为空！");
            return;
        }

        //==============================
        // 遍历所有物品
        //==============================

        foreach (ItemStack item in data.items)
        {
            // 创建一个新的格子
            GameObject slot =
                Instantiate(itemSlotPrefab, content);

            // 保存起来
            slotObjects.Add(slot);

            // 获取格子脚本
            ItemSlotUI slotUI =
                slot.GetComponent<ItemSlotUI>();

            if (slotUI == null)
            {
                Debug.LogError("ItemSlotPrefab没有挂ItemSlotUI脚本！");
                continue;
            }

            // 初始化格子
            slotUI.SetItem(item, this);
        }

        Debug.Log($"仓库刷新，共显示 {data.items.Count} 个物品");
    }
    /// <summary>
    /// 选择一个物品
    /// 刷新右侧详情
    /// </summary>
    public void SelectItem(ItemStack item)
    {
        ItemData data =
            ItemDatabase.Instance.GetItem(item.itemId);

        if (data == null)
            return;

        // 先打开详情面板（加入UIManager栈）
        UIManager.Instance.OpenPanel(itemInfoPanel.gameObject);

        itemInfoPanel.Show(data, item);
        Debug.Log($"查看物品：{data.itemName}");
    }
}