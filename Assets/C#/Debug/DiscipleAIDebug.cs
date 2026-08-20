using UnityEngine;

/// <summary>
/// 弟子自主 AI 调试日志。默认关闭，需要诊断时开启 EnableLog。
/// </summary>
public static class DiscipleAIDebug
{
    public static bool EnableLog = false;

    public static void Log(string message)
    {
        if (EnableLog) Debug.Log($"[DiscipleAI] {message}");
    }

    public static void LogWarning(string message)
    {
        if (EnableLog) Debug.LogWarning($"[DiscipleAI] {message}");
    }

    public static void LogDecision(NPCRuntime npc, DiscipleDecisionResult decision)
    {
        if (!EnableLog || decision == null) return;
        string name = npc == null ? "未知弟子" : npc.Character.displayName;
        if (decision.Selected == null)
        {
            Log($"{name} 无可用行动");
            foreach (ActionScoreResult candidate in decision.Candidates)
                if (candidate != null && !string.IsNullOrEmpty(candidate.FilterReason))
                    Log($"  {candidate.Action.id}: 过滤 -> {candidate.FilterReason}");
            return;
        }

        Log($"{name} 选择 {decision.Selected.id}({decision.Selected.displayName})，原因: {decision.ReasonLabel}");
        foreach (ActionScoreResult candidate in decision.Candidates)
        {
            if (candidate == null) continue;
            if (!candidate.Eligible)
            {
                Log($"  {candidate.Action.id}: 过滤 -> {candidate.FilterReason}");
                continue;
            }
            Log($"  {candidate.Action.id}: {candidate.Score:F2}");
            foreach (TermScore term in candidate.Terms)
                Log($"    {term.Term.source}.{term.Term.key}: input={term.Input:F2} curved={term.Curved:F2} x{term.Term.weight} = {term.Contribution:F2}");
        }
    }
}
