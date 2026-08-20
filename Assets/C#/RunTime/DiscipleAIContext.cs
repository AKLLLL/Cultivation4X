using System.Collections.Generic;

/// <summary>
/// 单个弟子的 AI 计算缓存。
/// 只保存计算所需缓存，不是 NPC 状态本体，也不存档；
/// NPC 真实状态始终以 NPCRuntime/CharacterState 为准。
/// </summary>
public class DiscipleAIContext
{
    public string CharacterId;
    public List<GoalInstance> Goals = new List<GoalInstance>();
    public List<ActionScoreResult> LastScores = new List<ActionScoreResult>();
    public ActionDefinition LastAction;
    public int LastDecisionDay = -1;
    /// <summary>自主 Mission 终局的日期；当日不重新决策，给玩家派遣窗口。</summary>
    public int LastActionEndedDay = -1;

    public DiscipleAIContext(string characterId)
    {
        CharacterId = characterId;
    }
}
