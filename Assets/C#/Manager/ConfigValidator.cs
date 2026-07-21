using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public static class ConfigValidator
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ValidateAtStartup()
    {
        ValidateItems();
        ValidateMissions();
    }

    private static void ValidateItems()
    {
        HashSet<string> ids = new HashSet<string>();
        foreach (TextAsset file in Resources.LoadAll<TextAsset>("Configs/Items"))
        {
            try
            {
                ItemData item = JsonConvert.DeserializeObject<ItemData>(file.text);
                if (item == null || string.IsNullOrWhiteSpace(item.itemId))
                    Debug.LogError($"物品配置缺少 ID: {file.name}");
                else if (!ids.Add(item.itemId))
                    Debug.LogError($"物品 ID 重复: {item.itemId}");
                else if (item.maxStack <= 0)
                    Debug.LogError($"物品堆叠上限无效: {item.itemId}");
            }
            catch (Exception exception) { Debug.LogError($"物品配置解析失败 {file.name}: {exception.Message}"); }
        }
    }

    private static void ValidateMissions()
    {
        HashSet<string> ids = new HashSet<string>();
        foreach (TextAsset file in Resources.LoadAll<TextAsset>("Configs/Missions"))
        {
            try
            {
                MissionData mission = JsonConvert.DeserializeObject<MissionData>(file.text);
                if (mission == null || string.IsNullOrWhiteSpace(mission.id))
                    Debug.LogError($"任务配置缺少 ID: {file.name}");
                else if (!ids.Add(mission.id))
                    Debug.LogError($"任务 ID 重复: {mission.id}");
                else if (mission.needDays <= 0)
                    Debug.LogError($"任务天数无效: {mission.id}");
            }
            catch (Exception exception) { Debug.LogError($"任务配置解析失败 {file.name}: {exception.Message}"); }
        }
    }
}
