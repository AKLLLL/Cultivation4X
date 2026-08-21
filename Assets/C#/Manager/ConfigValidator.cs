using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Cultivation4X.WorldMap;

public static class ConfigValidator
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ValidateAtStartup()
    {
        ValidateItems();
        ValidateResources();
        ValidateMissions();
        ValidateFounding();
        ValidateExternalThreats();
    }

    private static void ValidateResources()
    {
        HashSet<string> itemIds = new HashSet<string>();
        foreach (TextAsset file in Resources.LoadAll<TextAsset>("Configs/Items"))
        {
            try
            {
                ItemData item = JsonConvert.DeserializeObject<ItemData>(file.text);
                if (!string.IsNullOrWhiteSpace(item?.itemId)) itemIds.Add(item.itemId);
            }
            catch (Exception) { }
        }
        HashSet<string> nodeIds = new HashSet<string>();
        foreach (ResourceNodeDefinition node in ResourceDefinitionDatabase.Nodes)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.id) || !nodeIds.Add(node.id) ||
                !itemIds.Contains(node.resourceId) || node.baseOutput <= 0 || node.biomeRequirements == null)
                Debug.LogError($"资源节点配置无效: {node?.id}");
        }
        HashSet<string> veinIds = new HashSet<string>();
        foreach (SpiritualVeinDefinition vein in ResourceDefinitionDatabase.Veins)
        {
            if (vein == null || string.IsNullOrWhiteSpace(vein.id) || !veinIds.Add(vein.id) ||
                vein.grade <= 0 || vein.outputMultiplier < 1f || vein.origin != SpiritualVeinOrigin.Natural)
                Debug.LogError($"灵脉配置无效: {vein?.id}");
        }
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

    private static void ValidateExternalThreats()
    {
        Dictionary<string, MissionData> missions = new Dictionary<string, MissionData>();
        foreach (TextAsset file in Resources.LoadAll<TextAsset>("Configs/Missions"))
        {
            try
            {
                MissionData mission = JsonConvert.DeserializeObject<MissionData>(file.text);
                if (!string.IsNullOrWhiteSpace(mission?.id)) missions[mission.id] = mission;
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

        HashSet<string> threatIds = new HashSet<string>();
        foreach (TextAsset file in Resources.LoadAll<TextAsset>("Configs/ExternalThreats"))
        {
            try
            {
                ExternalThreatDefinition threat = JsonConvert.DeserializeObject<ExternalThreatDefinition>(file.text);
                if (!ExternalThreatRules.ValidateDefinition(threat, out string reason))
                { Debug.LogError($"外部威胁配置无效 {file.name}: {reason}"); continue; }
                if (!threatIds.Add(threat.id)) Debug.LogError($"外部威胁 ID 重复: {threat.id}");
                if (!missions.TryGetValue(threat.investigationMissionId, out MissionData mission) ||
                    mission.threatMissionKind != ThreatMissionKind.Investigation || mission.needDays != 2)
                    Debug.LogError($"外部威胁调查任务配置不匹配: {threat.id} / {threat.investigationMissionId}");
                if (!eventIds.Contains(threat.discoveredEventId))
                    Debug.LogError($"外部威胁发现事件不存在: {threat.id} / {threat.discoveredEventId}");
                if (threat.targetVillageId != "qingshi_village")
                    Debug.LogError($"外部威胁目标村庄无效: {threat.id} / {threat.targetVillageId}");
                if (threat.defenseMaterialCost != 3)
                    Debug.LogError($"外部威胁防御成本必须为3: {threat.id}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"外部威胁配置解析失败 {file.name}: {exception.Message}");
            }
        }
        if (!threatIds.Contains(ExternalThreatRules.QingshiThreatId))
            Debug.LogError($"首次青石村威胁配置不存在: {ExternalThreatRules.QingshiThreatId}");
    }
}
