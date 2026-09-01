using System;
using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;
using UnityEngine;

public static class SectDutyEffectIds
{
    public const string OpenHerbLand = "open_herb_land";
    public const string PlantHerbs = "plant_herbs";
    public const string CareHerbs = "care_herbs";
    public const string HarvestHerbs = "harvest_herbs";
    public const string GeneralMaintenance = "general_sect_maintenance";
}

public sealed class SectDutyDecision
{
    public ActionDefinition Action;
    public ActionScoreResult Score;
    public SectFunctionalZoneState Zone;
    public SectDepartmentState Department;
}

public sealed class SectDutyExecutionOutcome
{
    public bool executed;
    public bool worldChanged;
    public string failureReason;
    public string actionId;
    public string actionDisplayName;
    public string targetId;
    public string targetDisplayName;
    public string departmentId;
}

public static class SectDutyResolver
{
    public static SectDutyDecision Resolve(NPCRuntime npc, int day, DiscipleAIConfig config,
        IdentityDefinition identity, PlayerData player, WorldMap map, WorldMapProgressState progress)
    {
        if (npc == null || config == null || identity == null) return null;
        List<GoalInstance> goals = DiscipleAIEvaluator.GenerateGoals(npc, config.Goals, null);
        SectDepartmentState department = SectOrganizationRules.DepartmentFor(player, npc.CharacterId);
        List<SectDutyDecision> candidates = new List<SectDutyDecision>();

        foreach (SectFunctionalZoneState zone in (progress?.functionalZones ??
                     new List<SectFunctionalZoneState>()).Where(item => item != null)
                 .OrderBy(item => item.zoneId, StringComparer.Ordinal))
        {
            string effectId = RequiredEffect(zone);
            ActionDefinition action = config.Actions.FirstOrDefault(item => item != null &&
                item.executionKind == ActionExecutionKind.SectDuty && item.sectDutyEffectId == effectId);
            if (action == null || !WorldMapProgressRules.IsValidCell(map, zone.cellIndex)) continue;
            string displayName = SectFunctionalZoneRules.DisplayName(map, zone);
            float suitability = SectFunctionalZoneRules.SuitabilityMultiplier(map.cells[zone.cellIndex]);
            float bonus = 30f;
            if (department != null && zone.assignedDepartmentId == department.departmentId) bonus += 20f;
            ActionEvaluationContext context = new ActionEvaluationContext
            {
                ExecutionKind = ActionExecutionKind.SectDuty,
                CurrentDay = day,
                TargetId = zone.zoneId,
                TargetDisplayName = displayName,
                ContextBonus = bonus,
                ZoneSuitability = suitability,
                DepartmentId = department?.departmentId
            };
            ActionScoreResult score = DiscipleAIEvaluator.EvaluateActions(
                npc, identity, goals, new[] { action }, context).FirstOrDefault(item => item?.Eligible == true);
            if (score != null)
                candidates.Add(new SectDutyDecision { Action = action, Score = score, Zone = zone, Department = department });
        }

        if (candidates.Count == 0)
        {
            ActionDefinition fallback = config.Actions.FirstOrDefault(item => item != null &&
                item.executionKind == ActionExecutionKind.SectDuty &&
                item.sectDutyEffectId == SectDutyEffectIds.GeneralMaintenance);
            if (fallback == null) return null;
            ActionEvaluationContext context = new ActionEvaluationContext
            {
                ExecutionKind = ActionExecutionKind.SectDuty,
                CurrentDay = day,
                DepartmentId = department?.departmentId
            };
            ActionScoreResult score = DiscipleAIEvaluator.EvaluateActions(
                npc, identity, goals, new[] { fallback }, context).FirstOrDefault(item => item?.Eligible == true);
            return score == null ? null : new SectDutyDecision
                { Action = fallback, Score = score, Department = department };
        }

        return candidates.OrderByDescending(item => item.Score.Score)
            .ThenBy(item => item.Action.id, StringComparer.Ordinal)
            .ThenBy(item => item.Zone?.zoneId, StringComparer.Ordinal)
            .First();
    }

    public static string RequiredEffect(SectFunctionalZoneState zone)
    {
        if (zone == null) return null;
        switch (zone.stage)
        {
            case FunctionalZoneStage.Planned: return SectDutyEffectIds.OpenHerbLand;
            case FunctionalZoneStage.Developing: return SectDutyEffectIds.PlantHerbs;
            case FunctionalZoneStage.Operational:
                return zone.harvestProgress >= SectFunctionalZoneRules.HarvestReadyThreshold
                    ? SectDutyEffectIds.HarvestHerbs
                    : SectDutyEffectIds.CareHerbs;
            default: return null;
        }
    }
}

public static class SectDutyExecutor
{
    public static SectDutyExecutionOutcome Execute(SectDutyDecision decision, WorldMap map,
        WorldMapProgressState progress)
    {
        SectDutyExecutionOutcome outcome = new SectDutyExecutionOutcome
        {
            actionId = decision?.Action?.id,
            actionDisplayName = decision?.Action?.displayName,
            targetId = decision?.Zone?.zoneId,
            targetDisplayName = SectFunctionalZoneRules.DisplayName(map, decision?.Zone),
            departmentId = decision?.Department?.departmentId
        };
        if (decision?.Action == null)
        { outcome.failureReason = "没有可执行的宗务行为"; return outcome; }
        if (decision.Action.sectDutyEffectId == SectDutyEffectIds.GeneralMaintenance)
        {
            outcome.executed = true;
            outcome.targetId = null;
            outcome.targetDisplayName = null;
            return outcome;
        }
        SectFunctionalZoneState zone = decision.Zone;
        if (zone == null || progress?.functionalZones?.Contains(zone) != true ||
            !WorldMapProgressRules.IsValidCell(map, zone.cellIndex))
        { outcome.failureReason = "目标功能区已经失效"; return outcome; }

        float contribution = SectFunctionalZoneRules.SuitabilityMultiplier(map.cells[zone.cellIndex]);
        switch (decision.Action.sectDutyEffectId)
        {
            case SectDutyEffectIds.OpenHerbLand:
                if (zone.stage != FunctionalZoneStage.Planned)
                { outcome.failureReason = "功能区已经不需要开垦"; return outcome; }
                zone.phaseProgress += contribution;
                if (zone.phaseProgress >= SectFunctionalZoneRules.PlannedThreshold)
                {
                    zone.stage = FunctionalZoneStage.Developing;
                    zone.phaseProgress = 0f;
                    TimeManager.Instance?.RecordDayNotice($"{outcome.targetDisplayName}已形成试种地");
                }
                break;
            case SectDutyEffectIds.PlantHerbs:
                if (zone.stage != FunctionalZoneStage.Developing)
                { outcome.failureReason = "功能区当前不能试种"; return outcome; }
                zone.phaseProgress += contribution;
                if (zone.phaseProgress >= SectFunctionalZoneRules.DevelopingThreshold)
                {
                    zone.stage = FunctionalZoneStage.Operational;
                    zone.phaseProgress = 0f;
                    zone.harvestProgress = 0f;
                    TimeManager.Instance?.RecordDayNotice($"{outcome.targetDisplayName}已演化为药圃");
                }
                break;
            case SectDutyEffectIds.CareHerbs:
                if (zone.stage != FunctionalZoneStage.Operational)
                { outcome.failureReason = "功能区尚未形成药圃"; return outcome; }
                zone.harvestProgress = Mathf.Min(SectFunctionalZoneRules.HarvestReadyThreshold,
                    zone.harvestProgress + contribution);
                break;
            case SectDutyEffectIds.HarvestHerbs:
                if (zone.stage != FunctionalZoneStage.Operational ||
                    zone.harvestProgress < SectFunctionalZoneRules.HarvestReadyThreshold)
                { outcome.failureReason = "灵草尚未成熟"; return outcome; }
                zone.harvestProgress -= SectFunctionalZoneRules.HarvestReadyThreshold;
                if (WarehouseManager.Instance == null ||
                    !WarehouseManager.Instance.TryAddItem(SectFunctionalZoneRules.HerbItemId, 1))
                    TimeManager.Instance?.RecordDayNotice($"{outcome.targetDisplayName}采收的青灵草因仓库容量不足而损失");
                break;
            default:
                outcome.failureReason = "未知宗务效果";
                return outcome;
        }
        outcome.executed = true;
        outcome.worldChanged = true;
        return outcome;
    }
}
