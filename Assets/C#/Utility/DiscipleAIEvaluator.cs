using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 弟子自主 AI 的纯计算层：
/// 条件求值、响应曲线、Goal 生成（含滞后）、Action 过滤、Utility 评分与选平。
/// 本类不执行副作用、不直接修改 NPC/Mission；执行由 DiscipleMissionBridge 完成。
/// </summary>
public static class DiscipleAIEvaluator
{
    public const float GoalActivationThreshold = 3f;
    public const float GoalHysteresisFactor = 0.5f;
    public const int MaxActiveGoals = 3;

    // ---------------------------------------------------------------
    // Goal 生成
    // ---------------------------------------------------------------

    public static List<GoalInstance> GenerateGoals(NPCRuntime npc,
        IReadOnlyList<GoalDefinition> definitions,
        IReadOnlyList<GoalInstance> previous)
    {
        List<GoalInstance> result = new List<GoalInstance>();
        if (npc == null || definitions == null) return result;

        foreach (GoalDefinition definition in definitions)
        {
            if (definition == null || !PassesConditions(npc, definition.conditions)) continue;

            float intensity = Mathf.Max(0f, definition.baseIntensity);
            string reasonLabel = null;
            float bestContribution = 0f;
            foreach (ScoreTerm term in definition.weightTerms ?? new List<ScoreTerm>())
            {
                if (term == null) continue;
                float input = ResolveInput(npc, null, null, term, out string label);
                float curved = EvaluateCurve(input, term.curve);
                float contribution = term.weight * curved;
                intensity += contribution;
                if (contribution > bestContribution)
                {
                    bestContribution = contribution;
                    reasonLabel = string.IsNullOrWhiteSpace(label) ? term.key : label;
                }
            }

            intensity = Mathf.Clamp(intensity, 0f, 10f);

            GoalInstance old = previous?.FirstOrDefault(item => item?.Definition?.id == definition.id);
            if (old != null) intensity = Mathf.Max(intensity, old.Intensity * GoalHysteresisFactor);

            if (intensity < GoalActivationThreshold) continue;
            result.Add(new GoalInstance
            {
                Definition = definition,
                Intensity = intensity,
                ReasonLabel = reasonLabel ?? definition.displayName
            });
        }

        return result
            .OrderByDescending(item => item.Intensity)
            .ThenBy(item => item.Definition.id, StringComparer.Ordinal)
            .Take(MaxActiveGoals)
            .ToList();
    }

    public static bool PassesConditions(NPCRuntime npc, IReadOnlyList<GoalCondition> conditions)
    {
        if (conditions == null) return true;
        foreach (GoalCondition condition in conditions)
        {
            if (condition == null) continue;
            if (!PassesGoalCondition(npc, condition)) return false;
        }
        return true;
    }

    public static bool PassesGoalCondition(NPCRuntime npc, GoalCondition condition)
    {
        if (condition == null || npc == null) return false;
        switch (condition.type)
        {
            case GoalConditionType.Always:
                return true;
            case GoalConditionType.HasTrait:
                return npc.Character != null && npc.Character.HasTrait(condition.value);
            case GoalConditionType.MissingTrait:
                return npc.Character != null && !npc.Character.HasTrait(condition.value);
            case GoalConditionType.RealmAtLeast:
                return TryParseRealm(condition, out CultivationRealm realm) && (int)npc.Realm >= (int)realm;
            case GoalConditionType.RealmAtMost:
                return TryParseRealm(condition, out realm) && (int)npc.Realm <= (int)realm;
            case GoalConditionType.HealthIs:
                return HealthMatches(npc.Health, condition.value);
            case GoalConditionType.CultivationRatioBelow:
                return CultivationRatio(npc) < ParseFloat(condition.value, condition.intValue);
            case GoalConditionType.WarehouseItemBelow:
                return WarehouseCount(condition.value) < Mathf.Max(0, condition.intValue);
            case GoalConditionType.RelationshipCountBelow:
                return npc.Character != null && npc.Character.relationships.Count < Mathf.Max(0, condition.intValue);
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------
    // Action 过滤与评分
    // ---------------------------------------------------------------

    public static List<ActionScoreResult> EvaluateActions(NPCRuntime npc,
        IdentityDefinition identity,
        IReadOnlyList<GoalInstance> goals,
        IReadOnlyList<ActionDefinition> actions,
        int currentDay = -1)
    {
        List<ActionScoreResult> results = new List<ActionScoreResult>();
        if (npc == null || actions == null) return results;

        foreach (ActionDefinition action in actions)
        {
            if (action == null) continue;
            ActionScoreResult result = new ActionScoreResult { Action = action };
            string filterReason = FilterReason(npc, identity, action, currentDay);
            if (!string.IsNullOrEmpty(filterReason))
            {
                result.FilterReason = filterReason;
                results.Add(result);
                continue;
            }

            float score = action.baseline;
            float bestPositive = 0f;
            string reasonLabel = action.displayName;
            foreach (ScoreTerm term in action.scoreTerms ?? new List<ScoreTerm>())
            {
                if (term == null) continue;
                float input = ResolveInput(npc, identity, goals, term, out string label);
                float curved = EvaluateCurve(input, term.curve);
                float contribution = term.weight * curved;
                score += contribution;
                result.Terms.Add(new TermScore { Term = term, Input = input, Curved = curved, Contribution = contribution });
                if (contribution > bestPositive && !string.IsNullOrWhiteSpace(label))
                {
                    bestPositive = contribution;
                    reasonLabel = label;
                }
            }

            result.Score = score;
            result.ReasonLabel = reasonLabel;
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// 选平：只在通过过滤的候选中取最高分；同分取 actionId 字典序最小；不引入随机扰动。
    /// </summary>
    public static DiscipleDecisionResult ChooseAction(IReadOnlyList<ActionScoreResult> results)
    {
        DiscipleDecisionResult decision = new DiscipleDecisionResult();
        if (results == null) return decision;
        decision.Candidates.AddRange(results.Where(item => item != null));

        ActionScoreResult selected = decision.Candidates
            .Where(item => item.Eligible)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Action.id, StringComparer.Ordinal)
            .FirstOrDefault();
        decision.Selected = selected?.Action;
        decision.ReasonLabel = selected?.ReasonLabel ?? selected?.Action?.displayName;
        return decision;
    }

    // ---------------------------------------------------------------
    // 曲线与输入
    // ---------------------------------------------------------------

    public static float EvaluateCurve(float input, IReadOnlyList<float[]> curve)
    {
        if (curve == null || curve.Count == 0) return Mathf.Clamp01(input);

        List<float[]> points = curve
            .Where(item => item != null && item.Length >= 2)
            .OrderBy(item => item[0])
            .ToList();
        if (points.Count == 0) return Mathf.Clamp01(input);
        if (points.Count == 1) return points[0][1];

        if (input <= points[0][0]) return points[0][1];
        if (input >= points[points.Count - 1][0]) return points[points.Count - 1][1];

        for (int i = 0; i < points.Count - 1; i++)
        {
            float x0 = points[i][0];
            float x1 = points[i + 1][0];
            if (input < x0 || input > x1) continue;
            float t = x1 <= x0 ? 0f : (input - x0) / (x1 - x0);
            return points[i][1] + t * (points[i + 1][1] - points[i][1]);
        }
        return points[points.Count - 1][1];
    }

    private static float ResolveInput(NPCRuntime npc, IdentityDefinition identity,
        IReadOnlyList<GoalInstance> goals, ScoreTerm term, out string reasonLabel)
    {
        reasonLabel = null;
        switch (term.source)
        {
            case ScoreSource.Goal:
                GoalInstance goal = goals?.FirstOrDefault(item => item?.Definition?.id == term.key);
                reasonLabel = goal?.Definition?.displayName;
                return goal == null ? 0f : goal.Intensity / 10f;

            case ScoreSource.Trait:
                bool hasTrait = npc.Character != null && npc.Character.HasTrait(term.key);
                TraitDefinition trait = TraitDatabase.Instance == null ? null : TraitDatabase.Instance.Get(term.key);
                reasonLabel = string.IsNullOrWhiteSpace(trait?.displayName) ? term.key : trait.displayName;
                return hasTrait ? 1f : 0f;

            case ScoreSource.Ability:
                return ResolveAbility(npc, term.key, out reasonLabel);

            case ScoreSource.Interest:
                // V1 没有兴趣数据，字段预留恒为 0。
                reasonLabel = term.key;
                return 0f;

            case ScoreSource.Identity:
                if (term.key == "IdentityFreedom")
                {
                    reasonLabel = "身份";
                    return identity == null ? 0f : Mathf.Clamp01(identity.freedom);
                }
                reasonLabel = term.key;
                return 0f;

            case ScoreSource.Environment:
                return ResolveEnvironment(term, out reasonLabel);

            case ScoreSource.Event:
                // EventContext V1 不做，字段预留恒为 0。
                reasonLabel = term.key;
                return 0f;

            default:
                return 0f;
        }
    }

    private static float ResolveAbility(NPCRuntime npc, string key, out string reasonLabel)
    {
        switch (key)
        {
            case "CultivationRatio":
                reasonLabel = "传承掌握";
                return CultivationRatio(npc);
            case "SpiritRootQuality":
                reasonLabel = "天资";
                return Mathf.Clamp01((int)npc.SpiritRootQuality / 5f);
            case "RealmIndex":
                reasonLabel = "境界";
                return Mathf.Clamp01(((int)npc.Realm + 1) / 4f);
            default:
                reasonLabel = key;
                return 0f;
        }
    }

    private static float ResolveEnvironment(ScoreTerm term, out string reasonLabel)
    {
        if (term.key.StartsWith("WarehouseTagScarcity.", StringComparison.Ordinal))
        {
            reasonLabel = "资源匮乏";
            string tag = term.key.Substring("WarehouseTagScarcity.".Length);
            float threshold = term.threshold > 0f ? term.threshold : 5f;
            return ResourceStatusService.Scarcity(tag, threshold);
        }
        if (term.key.StartsWith("WarehouseScarcity.", StringComparison.Ordinal))
        {
            reasonLabel = "资源匮乏";
            string itemId = term.key.Substring("WarehouseScarcity.".Length);
            float threshold = term.threshold > 0f ? term.threshold : 5f;
            float count = WarehouseCount(itemId);
            DiscipleAIDebug.Log($"Scarcity {itemId}: count={count}, threshold={threshold}, warehouse={(WarehouseManager.Instance == null ? "null" : "ok")}");
            if (count <= 0f) return 1f;
            if (count >= threshold) return 0f;
            return 1f - count / threshold;
        }
        if (term.key.StartsWith("FacilityAvailable.", StringComparison.Ordinal))
        {
            reasonLabel = "设施";
            string facilityName = term.key.Substring("FacilityAvailable.".Length);
            if (!Enum.TryParse(facilityName, out FacilityType facility)) return 0f;
            int level = PlayerManager.Instance == null ? 0 : PlayerManager.Instance.GetFacilityLevel(facility);
            return level > 0 ? 1f : 0f;
        }
        reasonLabel = term.key;
        return 0f;
    }

    private static string FilterReason(NPCRuntime npc, IdentityDefinition identity,
        ActionDefinition action, int currentDay)
    {
        if (npc == null || !npc.CanDispatch()) return "弟子当前无法自主行动";
        if (identity == null || action.identityIds == null || !action.identityIds.Contains(identity.id))
            return "身份不符";
        int day = currentDay >= 0
            ? currentDay
            : TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay;
        if (DiscipleMentalStateRules.IsAutonomyCoolingDown(npc, day))
            return "自主研读后冷却中";
        if (action.minIntervalDays > 0)
        {
            int lastEndDay = DiscipleMissionBridge.GetMostRecentMissionEndDay(npc, action.missionId);
            if (lastEndDay >= 0 && day <= lastEndDay + action.minIntervalDays) return "冷却中";
        }
        if (MissionManager.Instance == null) return "任务系统未初始化";
        MissionData data = MissionManager.Instance.GetMissionData(action.missionId);
        if (data == null) return "任务模板不存在";
        if (data.nodes != null && data.nodes.Count > 0) return "任务包含节点";
        if (data.itemCosts != null && data.itemCosts.Count > 0) return "需要消耗资源";
        if (data.missionType == MissionType.WorldEvent || data.generatedByMap || data.isStoryAction)
            return "不属于自主任务";
        if (!MonthlyPlanRules.CanStartAutonomousMission(npc, data.needDays, day, out string budgetReason))
            return budgetReason;

        if (data.isFacilityAction)
        {
            int level = PlayerManager.Instance == null ? 0 : PlayerManager.Instance.GetFacilityLevel(data.requiredFacility);
            if (level < data.requiredFacilityLevel) return "设施等级不足";
            bool occupied = MissionManager.Instance.GetActiveMissions().Any(mission =>
                mission?.Data != null && mission.Data.isFacilityAction &&
                mission.Data.requiredFacility == data.requiredFacility &&
                (mission.State == MissionState.Active || mission.State == MissionState.WaitingNode ||
                 mission.State == MissionState.AwaitingReward));
            if (occupied) return "设施正在使用";
        }
        return null;
    }

    // ---------------------------------------------------------------
    // 工具
    // ---------------------------------------------------------------

    public static float CultivationRatio(NPCRuntime npc)
    {
        if (npc == null || !npc.Character.IsAlive) return 0f;
        return Mathf.Clamp01(npc.Character.techniqueMastery / 100f);
    }

    private static bool HealthMatches(HealthState health, string value)
    {
        if (value == "Injured")
            return health == HealthState.LightInjury || health == HealthState.HeavyInjury || health == HealthState.SeriousInjury;
        return Enum.TryParse(value, out HealthState parsed) && health == parsed;
    }

    private static bool TryParseRealm(GoalCondition condition, out CultivationRealm realm)
    {
        if (!string.IsNullOrWhiteSpace(condition.value) && Enum.TryParse(condition.value, out realm)) return true;
        realm = (CultivationRealm)condition.intValue;
        return condition.intValue >= (int)CultivationRealm.Mortal;
    }

    private static float ParseFloat(string value, int fallback) =>
        float.TryParse(value, out float parsed) ? parsed : fallback;

    private static int WarehouseCount(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId) || WarehouseManager.Instance == null) return 0;
        return WarehouseManager.Instance.GetItemCount(itemId);
    }
}
