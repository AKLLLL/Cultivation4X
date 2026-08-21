using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// AI Action → Mission 的桥接层。
/// 自主行为不创建第二套任务系统：本类只负责把选中的 ActionDefinition
/// 转换为既有 Mission 并启动，以及识别自主 Mission 的终局证据并分发关系副作用。
/// </summary>
public static class DiscipleMissionBridge
{
    public const string SocialActionId = "social";

    /// <summary>
    /// 启动自主 Mission。所有执行/推进/奖励/恢复都走既有 Mission 流程。
    /// </summary>
    public static Mission StartAutonomousMission(ActionDefinition action, NPCRuntime npc, out string reason)
    {
        reason = null;
        if (action == null) { reason = "行动配置为空"; return null; }
        if (npc == null || !npc.CanDispatch()) { reason = "弟子当前无法执行任务"; return null; }
        if (MissionManager.Instance == null) { reason = "任务系统未初始化"; return null; }

        Mission mission = MissionManager.Instance.CreateMission(action.missionId);
        if (mission == null) { reason = "任务模板不存在"; return null; }
        if (!CanStartData(mission.Data, out reason)) return null;

        mission.StartMission(npc);
        MissionManager.Instance.AddActiveMission(mission);
        EventManager.Instance?.TryTriggerSource(EventSource.MissionStart, npc);
        DiscipleAIDebug.Log($"{npc.Character.displayName} 自主开始: {mission.Data.name}");
        return mission;
    }

    private static bool CanStartData(MissionData data, out string reason)
    {
        if (data == null) { reason = "任务配置为空"; return false; }
        if (data.nodes != null && data.nodes.Count > 0) { reason = "任务包含节点"; return false; }
        if (data.itemCosts != null && data.itemCosts.Count > 0)
        { reason = "需要消耗资源"; return false; }
        if (data.isFacilityAction)
        {
            int level = PlayerManager.Instance == null ? 0 : PlayerManager.Instance.GetFacilityLevel(data.requiredFacility);
            if (level < data.requiredFacilityLevel) { reason = "设施等级不足"; return false; }
            bool occupied = MissionManager.Instance.GetActiveMissions().Any(mission =>
                mission?.Data != null && mission.Data.isFacilityAction &&
                mission.Data.requiredFacility == data.requiredFacility &&
                (mission.State == MissionState.Active || mission.State == MissionState.WaitingNode ||
                 mission.State == MissionState.AwaitingReward));
            if (occupied) { reason = "设施正在使用"; return false; }
        }
        reason = null;
        return true;
    }

    /// <summary>
    /// 确定性社交目标：characterId 升序第一个“非自己、存活、双方无任何关系记录”的弟子。
    /// </summary>
    public static NPCRuntime FindSocialTarget(NPCRuntime source)
    {
        if (source == null || NPCManager.Instance == null) return null;
        return NPCManager.Instance.GetLivingNPC()
            .Where(candidate => candidate != null && candidate.CharacterId != source.CharacterId)
            .Where(candidate => !HasAnyRelation(source, candidate) && !HasAnyRelation(candidate, source))
            .OrderBy(candidate => candidate.CharacterId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// 社交 Mission 终局的兜底处理。以“Mission 履历已写入 + 当日尚无 Relationship 履历”
    /// 作为终局与幂等证据，读档后重放也安全。
    /// 返回 true 表示本次写入了关系结果。
    /// </summary>
    public static bool TryProcessCompletedSocial(NPCRuntime npc, int day)
    {
        if (npc == null || !npc.Character.IsAlive || NPCManager.Instance == null) return false;
        string socialMissionId = SocialMissionId();
        if (string.IsNullOrWhiteSpace(socialMissionId)) return false;

        bool missionRecordExists = npc.Character.lifeRecords != null &&
            npc.Character.lifeRecords.Any(record =>
                record != null && record.day == day && record.category == "Mission" &&
                record.sourceId == socialMissionId);
        if (!missionRecordExists) return false;

        bool alreadyProcessed = npc.Character.lifeRecords.Any(record =>
            record != null && record.day == day && record.category == "Relationship");
        if (alreadyProcessed) return false;

        NPCRuntime target = FindSocialTarget(npc);
        if (target != null)
        {
            NPCManager.Instance.AddRelationship(
                npc.CharacterId, target.CharacterId, RelationshipTag.Friend,
                $"与{DisplayName(target)}结为好友，心境渐安",
                $"与{DisplayName(npc)}结为好友");
        }
        else
        {
            NPCManager.Instance.RecordRelationshipOutcome(npc.CharacterId, "独自静思，心境渐平");
        }
        return true;
    }

    /// <summary>
    /// 当日是否有自主 Mission 终局的履历证据（用于完成后冷却 1 个决策周期）。
    /// 只统计玩家不可派遣（isPlayerAssignable=false）的 AI 专用模板，玩家手动炼丹/探索不算自主终局。
    /// </summary>
    public static bool HadAutonomousMissionEndToday(NPCRuntime npc, int day)
    {
        if (npc == null || npc.Character.lifeRecords == null) return false;
        HashSet<string> missionIds = new HashSet<string>(
            DiscipleAIConfigLoader.GetAutonomousMissionIds(),
            StringComparer.Ordinal);
        if (MissionManager.Instance != null)
        {
            missionIds.RemoveWhere(id =>
            {
                MissionData data = MissionManager.Instance.GetMissionData(id);
                return data == null || data.isPlayerAssignable;
            });
        }
        return npc.Character.lifeRecords.Any(record =>
            record != null && record.day == day && record.category == "Mission" &&
            !string.IsNullOrWhiteSpace(record.sourceId) && missionIds.Contains(record.sourceId));
    }

    /// <summary>
    /// 从持久化 Mission 履历读取某行动最近一次结束日。
    /// 只匹配该 Action 自己的 missionId，因此玩家任务不会触发自主冷却。
    /// </summary>
    public static int GetMostRecentMissionEndDay(NPCRuntime npc, string missionId)
    {
        if (npc?.Character?.lifeRecords == null || string.IsNullOrWhiteSpace(missionId)) return -1;
        int latest = -1;
        foreach (LifeRecord record in npc.Character.lifeRecords)
        {
            if (record == null || record.category != "Mission" ||
                record.sourceId != missionId || record.day <= latest) continue;
            latest = record.day;
        }
        return latest;
    }

    private static bool HasAnyRelation(NPCRuntime subject, NPCRuntime other)
    {
        return subject?.Character?.relationships != null &&
            subject.Character.relationships.Any(record =>
                record != null && record.targetCharacterId == other.CharacterId);
    }

    private static string SocialMissionId()
    {
        ActionDefinition social = DiscipleAIConfigLoader.Load().Actions
            .FirstOrDefault(action => action != null && action.id == SocialActionId);
        return social?.missionId;
    }

    private static string DisplayName(NPCRuntime npc)
    {
        if (npc == null) return "同门";
        if (!string.IsNullOrWhiteSpace(npc.Character?.displayName)) return npc.Character.displayName;
        if (!string.IsNullOrWhiteSpace(npc.Data?.npcName)) return npc.Data.npcName;
        return "同门";
    }
}
