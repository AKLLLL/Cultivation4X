using UnityEngine;

/// <summary>
/// 弟子心境的纯规则层。任务仍由 Mission 执行，本类只把已结算结果
/// 转换为角色状态变化，不创建第二套行动系统。
/// </summary>
public static class DiscipleMentalStateRules
{
    public const int MinMentalState = 0;
    public const int MaxMentalState = 100;
    public const int DailyRecovery = 1;
    public const int CultivationSuccessChange = -20;
    public const int CultivationFailureChange = -50;
    public const int RestSuccessChange = 5;
    public const int RestFailureChange = -5;
    public const int GlobalAutonomyCooldownDays = 3;
    public const string StudyMissionId = "disciple_ai_cultivate_001";
    public const string CultivationMissionId = StudyMissionId;
    public const string RestMissionId = "disciple_ai_rest_001";

    public static void RestoreDaily(NPCRuntime npc)
    {
        if (npc?.Character == null || !npc.Character.IsAlive) return;
        npc.ChangeMentalState(DailyRecovery);
    }

    public static void ApplyMissionResult(NPCRuntime npc, string missionId, MissionResultTier tier)
    {
        if (npc?.Character == null || string.IsNullOrWhiteSpace(missionId)) return;
        bool failed = tier == MissionResultTier.Insufficient;
        if (missionId == RestMissionId)
            npc.ChangeMentalState(failed ? RestFailureChange : RestSuccessChange);
    }

    public static bool IsCultivationAction(ActionDefinition action)
    {
        return false;
    }

    public static bool IsAutonomyCoolingDown(NPCRuntime npc, int currentDay)
    {
        int lastEndDay = DiscipleMissionBridge.GetMostRecentMissionEndDay(npc, StudyMissionId);
        return lastEndDay >= 0 && currentDay <= lastEndDay + GlobalAutonomyCooldownDays;
    }

    public static int Clamp(int value) => Mathf.Clamp(value, MinMentalState, MaxMentalState);
}
