using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 加载并校验 Configs/DiscipleAI 下的 goals/actions/identities JSON。
/// 与既有 Configs 体系一致：纯 JSON 配置，新增行为只需新增配置。
/// </summary>
public static class DiscipleAIConfigLoader
{
    private static DiscipleAIConfig cached;

    public static DiscipleAIConfig Load()
    {
        if (cached != null) return cached;
        cached = new DiscipleAIConfig();
        TextAsset[] files = Resources.LoadAll<TextAsset>("Configs/DiscipleAI");
        foreach (TextAsset file in files)
        {
            if (file == null) continue;
            string name = file.name.ToLowerInvariant();
            try
            {
                if (name.Contains("goal"))
                    AddRange(cached.Goals, Parse<GoalDefinition>(file));
                else if (name.Contains("action"))
                    AddRange(cached.Actions, Parse<ActionDefinition>(file));
                else if (name.Contains("identit"))
                    AddRange(cached.Identities, Parse<IdentityDefinition>(file));
                else
                    Debug.LogWarning($"[DiscipleAI] 无法识别配置文件类型: {file.name}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[DiscipleAI] 配置解析失败 {file.name}: {exception.Message}");
            }
        }
        ValidateInternal(cached);
        return cached;
    }

    public static void ResetForTests() => cached = null;

    /// <summary>
    /// 校验 actions 引用的 Mission 模板是否真实存在。
    /// 由 DiscipleDecisionManager 在 MissionManager 完成加载后调用（两处加载顺序不保证）。
    /// </summary>
    public static List<string> FindMissingMissionReferences()
    {
        DiscipleAIConfig config = Load();
        List<string> missing = new List<string>();
        if (MissionManager.Instance == null) return missing;
        foreach (ActionDefinition action in config.Actions)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.missionId)) continue;
            if (MissionManager.Instance.GetMissionData(action.missionId) == null)
                missing.Add($"{action.id} -> {action.missionId}");
        }
        return missing;
    }

    public static IReadOnlyList<string> GetAutonomousMissionIds()
    {
        DiscipleAIConfig config = Load();
        return config.Actions
            .Where(action => action != null && !string.IsNullOrWhiteSpace(action.missionId))
            .Select(action => action.missionId)
            .Distinct()
            .ToList();
    }

    private static List<T> Parse<T>(TextAsset file)
    {
        return JsonConvert.DeserializeObject<List<T>>(file.text) ?? new List<T>();
    }

    private static void AddRange<T>(List<T> target, IEnumerable<T> items)
    {
        foreach (T item in items ?? Enumerable.Empty<T>())
            if (item != null) target.Add(item);
    }

    private static void ValidateInternal(DiscipleAIConfig config)
    {
        ReportDuplicate(config.Goals.OfType<GoalDefinition>(), item => item.id, "Goal");
        ReportDuplicate(config.Actions.OfType<ActionDefinition>(), item => item.id, "Action");
        ReportDuplicate(config.Identities.OfType<IdentityDefinition>(), item => item.id, "Identity");

        HashSet<string> goalIds = new HashSet<string>(config.Goals.Where(item => !string.IsNullOrWhiteSpace(item?.id)).Select(item => item.id));
        HashSet<string> identityIds = new HashSet<string>(config.Identities.Where(item => !string.IsNullOrWhiteSpace(item?.id)).Select(item => item.id));

        foreach (ActionDefinition action in config.Actions)
        {
            if (action == null) continue;
            if (string.IsNullOrWhiteSpace(action.id))
            {
                Debug.LogError("[DiscipleAI] Action 缺少 ID");
                continue;
            }
            if (string.IsNullOrWhiteSpace(action.missionId))
                Debug.LogError($"[DiscipleAI] Action 缺少 missionId: {action.id}");
            if (action.minIntervalDays < 0)
                Debug.LogError($"[DiscipleAI] Action minIntervalDays 不能为负: {action.id}");
            if (action.identityIds != null)
                foreach (string identityId in action.identityIds)
                    if (!identityIds.Contains(identityId))
                        Debug.LogError($"[DiscipleAI] Action 引用了不存在的身份: {action.id} -> {identityId}");
            foreach (ScoreTerm term in action.scoreTerms ?? new List<ScoreTerm>())
            {
                if (term == null) continue;
                if (term.source == ScoreSource.Goal && !string.IsNullOrWhiteSpace(term.key) && !goalIds.Contains(term.key))
                    Debug.LogError($"[DiscipleAI] Action 引用了不存在的 Goal: {action.id} -> {term.key}");
            }
        }

        foreach (GoalDefinition goal in config.Goals)
        {
            if (goal == null || string.IsNullOrWhiteSpace(goal.id)) continue;
            foreach (ScoreTerm term in goal.weightTerms ?? new List<ScoreTerm>())
                if (term == null || string.IsNullOrWhiteSpace(term.key))
                    Debug.LogError($"[DiscipleAI] Goal 加权项缺少 key: {goal.id}");
        }
    }

    private static void ReportDuplicate<T>(IEnumerable<T> items, Func<T, string> idSelector, string kind)
    {
        HashSet<string> ids = new HashSet<string>();
        foreach (T item in items)
        {
            if (item == null) continue;
            string id = idSelector(item);
            if (string.IsNullOrWhiteSpace(id))
            {
                Debug.LogError($"[DiscipleAI] {kind} 配置缺少 ID");
                continue;
            }
            if (!ids.Add(id)) Debug.LogError($"[DiscipleAI] {kind} ID 重复: {id}");
        }
    }
}
