using System.Linq;

/// <summary>
/// 只负责写“决策原因”履历。
/// 行动结果履历由 Mission 系统（MissionManager.RecordMissionOutcome）负责；
/// 关系结果履历由 NPCManager.AddRelationship 负责；AI 不重复叙事。
/// </summary>
public static class ExperienceGenerator
{
    public static bool WriteDecisionRecord(NPCRuntime npc, ActionDefinition action,
        string reasonLabel, int day)
    {
        if (npc?.Character == null || action == null) return false;
        string reason = string.IsNullOrWhiteSpace(reasonLabel) ? action.displayName : reasonLabel;
        npc.Character.AddLifeRecord(day, "Decision",
            $"因{reason}，决定{action.displayName}", action.id);
        return true;
    }

    /// <summary>当日是否已写决策履历（读档/重复结算的幂等防护）。</summary>
    public static bool HasDecisionRecordOn(NPCRuntime npc, int day)
    {
        return npc?.Character?.lifeRecords != null &&
            npc.Character.lifeRecords.Any(record =>
                record != null && record.day == day && record.category == "Decision");
    }
}
