using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

/// <summary>
/// 物品数据库
/// 负责读取所有 ItemData
/// </summary>
public class ItemDatabase : MonoBehaviour
{
    public static ItemDatabase Instance;

    /// <summary>
    /// 所有物品
    /// Key = id
    /// </summary>
    private Dictionary<string, ItemData> items =
        new Dictionary<string, ItemData>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyUtility.MarkPersistent(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        LoadItems();
    }

    /// <summary>
    /// 加载所有物品
    /// </summary>
    void LoadItems()
    {
        items.Clear();

        TextAsset[] jsons =
            Resources.LoadAll<TextAsset>("Configs/Items");

        foreach (TextAsset json in jsons)
        {
            ItemData data =
                JsonConvert.DeserializeObject<ItemData>(json.text);

            if (data == null)
            {
                Debug.LogError(
                $"物品解析失败:{json.name}"
                );

                continue;
            }
            if (string.IsNullOrEmpty(data.itemId))
            {
                Debug.LogError(
                $"物品ID为空:{json.name}"
                );

                continue;
            }
            if (items.ContainsKey(data.itemId))
            {
                Debug.LogError($"物品ID重复：{data.itemId}");
                continue;
            }

            items.Add(data.itemId, data);

           // Debug.Log($"加载物品：{data.itemName}");
        }

        Debug.Log($"共加载 {items.Count} 个物品");
    }

    /// <summary>
    /// 根据ID获得物品配置
    /// </summary>
    public ItemData GetItem(string id)
    {
        if (items.TryGetValue(id, out ItemData item))
            return item;

        Debug.LogWarning($"不存在物品：{id}");

        return null;
    }
}
