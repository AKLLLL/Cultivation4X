using UnityEngine;


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



    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;

            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // UI通过这个接口读取仓库内容
    public WarehouseData GetWarehouseData()
    {
        return warehouseData;
    }

    /// <summary>
    /// 添加物品
    /// </summary>
    public void AddItem(string itemId, int count)
    {

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
    }

    public bool RemoveItem(
    string itemId,
    int count)
    {

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


        return true;

    }

}