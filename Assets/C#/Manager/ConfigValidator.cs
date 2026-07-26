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
        ValidateExploration();
        ValidateFounding();
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
                else if (mission.requiredCombatPower < 0 || mission.excellentScore < 0)
                    Debug.LogError($"任务能力阈值无效: {mission.id}");
            }
            catch (Exception exception) { Debug.LogError($"任务配置解析失败 {file.name}: {exception.Message}"); }
        }
    }

    private static void ValidateExploration()
    {
        Dictionary<string, MissionData> missions = new Dictionary<string, MissionData>();
        foreach (TextAsset file in Resources.LoadAll<TextAsset>("Configs/Missions"))
        {
            try
            {
                MissionData mission = JsonConvert.DeserializeObject<MissionData>(file.text);
                if (mission != null && !string.IsNullOrWhiteSpace(mission.id)) missions[mission.id] = mission;
            }
            catch (Exception) { }
        }

        HashSet<string> eventIds = new HashSet<string>();
        foreach (TextAsset file in Resources.LoadAll<TextAsset>("Configs/CharacterEvents"))
        {
            try
            {
                List<EventDefinition> loaded = file.text.TrimStart().StartsWith("[")
                    ? JsonConvert.DeserializeObject<List<EventDefinition>>(file.text)
                    : new List<EventDefinition> { JsonConvert.DeserializeObject<EventDefinition>(file.text) };
                foreach (EventDefinition definition in loaded ?? new List<EventDefinition>())
                    if (!string.IsNullOrWhiteSpace(definition?.id)) eventIds.Add(definition.id);
            }
            catch (Exception) { }
        }

        HashSet<string> regionIds = new HashSet<string>();
        foreach (TextAsset file in Resources.LoadAll<TextAsset>("Configs/ExplorationRegions"))
        {
            try
            {
                List<ExplorationRegionDefinition> regions = file.text.TrimStart().StartsWith("[")
                    ? JsonConvert.DeserializeObject<List<ExplorationRegionDefinition>>(file.text)
                    : new List<ExplorationRegionDefinition> { JsonConvert.DeserializeObject<ExplorationRegionDefinition>(file.text) };
                foreach (ExplorationRegionDefinition region in regions ?? new List<ExplorationRegionDefinition>())
                {
                    if (region == null || string.IsNullOrWhiteSpace(region.id)) { Debug.LogError($"探索区域缺少 ID: {file.name}"); continue; }
                    if (!regionIds.Add(region.id)) Debug.LogError($"探索区域 ID 重复: {region.id}");
                    if (region.milestones == null || region.milestones.Count != ExplorationRules.MaxStage)
                        Debug.LogError($"探索区域必须配置三个阶段: {region.id}");
                    ValidateExplorationMission(region.progressMissionId, ExplorationMissionKind.Progress, region.id, missions);
                    ValidateExplorationMission(region.ongoingMissionId, ExplorationMissionKind.Ongoing, region.id, missions);
                    if (!eventIds.Contains(region.firstProgressEventId)) Debug.LogError($"探索区域事件不存在: {region.id} / {region.firstProgressEventId}");
                }
            }
            catch (Exception exception) { Debug.LogError($"探索区域配置解析失败 {file.name}: {exception.Message}"); }
        }

        ValidateExplorationMission(ExplorationRules.SurveyMissionId, ExplorationMissionKind.Survey, null, missions);
    }

    private static void ValidateExplorationMission(string missionId, ExplorationMissionKind kind, string regionId,
        Dictionary<string, MissionData> missions)
    {
        if (!missions.TryGetValue(missionId ?? string.Empty, out MissionData mission))
        { Debug.LogError($"探索任务不存在: {missionId}"); return; }
        if (mission.explorationKind != kind || mission.requiredFacility != FacilityType.ExplorationHall ||
            (regionId != null && mission.explorationRegionId != regionId))
            Debug.LogError($"探索任务配置不匹配: {missionId}");
    }

    private static void ValidateFounding()
    {
        TextAsset catalogFile = Resources.Load<TextAsset>(FoundingRules.CatalogResourcePath);
        if (catalogFile == null) { Debug.LogError("立宗目录配置不存在"); return; }
        try
        {
            FoundingCatalogData catalog = JsonConvert.DeserializeObject<FoundingCatalogData>(catalogFile.text);
            if (catalog == null || catalog.techniques == null || catalog.techniques.Count != 3)
            { Debug.LogError("立宗配置必须包含三种传承"); return; }
            if (catalog.surnames == null || catalog.givenNames == null || catalog.surnames.Count * catalog.givenNames.Count < 10)
                Debug.LogError("立宗候选姓名组合不足10个");

            HashSet<string> missionIds = new HashSet<string>();
            foreach (TextAsset file in Resources.LoadAll<TextAsset>("Configs/Missions"))
            {
                MissionData mission = JsonConvert.DeserializeObject<MissionData>(file.text);
                if (!string.IsNullOrWhiteSpace(mission?.id)) missionIds.Add(mission.id);
            }
            HashSet<string> eventIds = new HashSet<string>();
            foreach (TextAsset file in Resources.LoadAll<TextAsset>("Configs/CharacterEvents"))
            {
                List<EventDefinition> definitions = file.text.TrimStart().StartsWith("[")
                    ? JsonConvert.DeserializeObject<List<EventDefinition>>(file.text)
                    : new List<EventDefinition> { JsonConvert.DeserializeObject<EventDefinition>(file.text) };
                foreach (EventDefinition definition in definitions ?? new List<EventDefinition>())
                    if (!string.IsNullOrWhiteSpace(definition?.id)) eventIds.Add(definition.id);
            }
            foreach (FoundingTechniqueDefinition technique in catalog.techniques)
            {
                if (technique == null || string.IsNullOrWhiteSpace(technique.id) || technique.tags == null || technique.tags.Count == 0)
                { Debug.LogError("传承标签配置无效"); continue; }
                if (!missionIds.Contains(technique.buildMissionId)) Debug.LogError($"路线建设任务不存在: {technique.buildMissionId}");
                if (!missionIds.Contains(technique.actionMissionId)) Debug.LogError($"路线行动不存在: {technique.actionMissionId}");
                if (!eventIds.Contains(technique.milestoneEventId)) Debug.LogError($"传承事件不存在: {technique.milestoneEventId}");
                foreach (TechniqueEffectDefinition effect in technique.effects ?? new List<TechniqueEffectDefinition>())
                    if (effect == null || effect.amount < 0 || effect.requiredUnderstanding < 0 ||
                        effect.requiredUnderstanding > FoundingRules.MaxUnderstanding)
                        Debug.LogError($"传承效果配置无效: {technique.id}");
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"立宗配置解析失败: {exception.Message}");
        }
    }
}
