using UnityEngine;
using System;
using System.Collections.Generic;


/// <summary>
/// 仓库管理器
/// 所有增加、减少物品操作都通过这里
/// </summary>
public class WarehouseManager : MonoBehaviour
{

    public static WarehouseManager Instance;


    /// <summary>
    /// 当前仓库数据
    /// </summary>
    public WarehouseData warehouseData =
        new WarehouseData();

    public event Action OnInventoryChanged;



    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;

            if (warehouseData == null) warehouseData = new WarehouseData();
            if (warehouseData.items.Count == 0)
                warehouseData.items.Add(new ItemStack { itemId = FacilityRules.BasicMaterialId, count = 10 });
            NormalizeItems();

            DontDestroyUtility.MarkPersistent(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // UI通过这个接口读取仓库内容
    public WarehouseData GetWarehouseData()
    {
        NormalizeItems();
        return warehouseData;
    }

    /// <summary>
    /// 添加物品
    /// </summary>
    public void AddItem(string itemId, int count)
    {
        TryAddItem(itemId, count);
    }

    public bool CanAddItem(string itemId, int count)
    {
        NormalizeItems();
        if (string.IsNullOrWhiteSpace(itemId) || count <= 0) return false;
        if (warehouseData.items.Exists(item => item.itemId == itemId)) return true;
        int level = PlayerManager.Instance == null ? 1 : PlayerManager.Instance.GetFacilityLevel(FacilityType.Warehouse);
        return warehouseData.items.Count < FacilityRules.WarehouseSlots(level);
    }

    public bool CanAddRewards(System.Collections.Generic.IEnumerable<ItemReward> rewards)
    {
        NormalizeItems();
        int level = PlayerManager.Instance == null ? 1 : PlayerManager.Instance.GetFacilityLevel(FacilityType.Warehouse);
        int freeSlots = Mathf.Max(0, FacilityRules.WarehouseSlots(level) - warehouseData.items.Count);
        var newIds = new System.Collections.Generic.HashSet<string>();
        foreach (ItemReward reward in rewards ?? new ItemReward[0])
        {
            if (reward == null || reward.count <= 0) continue;
            if (!warehouseData.items.Exists(item => item.itemId == reward.itemId)) newIds.Add(reward.itemId);
        }
        return newIds.Count <= freeSlots;
    }

    public bool TryAddItem(string itemId, int count)
    {
        NormalizeItems();
        if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
        {
            Debug.LogWarning($"拒绝无效物品变更: {itemId} x {count}");
            return false;
        }

        if (ItemDatabase.Instance != null && ItemDatabase.Instance.GetItem(itemId) == null)
        {
            Debug.LogWarning($"拒绝添加未知物品: {itemId}");
            return false;
        }

        if (!CanAddItem(itemId, count))
        {
            Debug.LogWarning($"仓库容量不足，无法加入新种类: {itemId}");
            return false;
        }

        //寻找仓库是否已有这个物品
        ItemStack item =
            warehouseData.items.Find(
                x => x.itemId == itemId
            );

        //已有该物品
        if (item != null)
        {
            item.count += count;
        }

        //没有该物品
        else
        {
            ItemStack newItem = new ItemStack();

            newItem.itemId = itemId;
            newItem.count = count;

            warehouseData.items.Add(newItem);
        }


        Debug.Log(
            $"仓库获得 {itemId} x {count}"
        );
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool RemoveItem(
    string itemId,
    int count)
    {
        NormalizeItems();
        if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
            return false;

        ItemStack item =
            warehouseData.items.Find(
            x => x.itemId == itemId
            );

        if (item == null)
        {
            Debug.Log(
            $"没有物品:{itemId}"
            );

            return false;
        }
        if (item.count < count)
        {
            Debug.Log(
            $"物品数量不足:{itemId}"
            );

            return false;
        }

        item.count -= count;

        if (item.count <= 0)
        {
            warehouseData.items.Remove(item);
        }


        Debug.Log(
            $"消耗 {itemId} x {count}"
        );

        OnInventoryChanged?.Invoke();


        return true;

    }

    public int GetItemCount(string itemId)
    {
        NormalizeItems();
        int total = 0;
        foreach (ItemStack item in warehouseData.items)
            if (item.itemId == itemId) total += item.count;
        return total;
    }

    public void NormalizeItems()
    {
        if (warehouseData == null) warehouseData = new WarehouseData();
        if (warehouseData.items == null) warehouseData.items = new List<ItemStack>();

        Dictionary<string, ItemStack> merged = new Dictionary<string, ItemStack>();
        for (int i = warehouseData.items.Count - 1; i >= 0; i--)
        {
            ItemStack item = warehouseData.items[i];
            if (item == null || string.IsNullOrWhiteSpace(item.itemId) || item.count <= 0)
            {
                warehouseData.items.RemoveAt(i);
                continue;
            }

            if (merged.TryGetValue(item.itemId, out ItemStack existing))
            {
                existing.count += item.count;
                warehouseData.items.RemoveAt(i);
            }
            else
            {
                merged[item.itemId] = item;
            }
        }
    }

}
