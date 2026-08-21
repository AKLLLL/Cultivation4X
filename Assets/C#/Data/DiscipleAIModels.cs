using System;
using System.Collections.Generic;

/// <summary>
/// Goal 条件词表。词表外的条件类型 V1 不实现。
/// </summary>
public enum GoalConditionType
{
    Always,
    HasTrait,
    MissingTrait,
    RealmAtLeast,
    RealmAtMost,
    HealthIs,
    CultivationRatioBelow,
    WarehouseItemBelow,
    RelationshipCountBelow
}

/// <summary>
/// Utility 评分项来源。与设计公式中的 Goal/性格/能力/兴趣/身份/环境/临时事件对应。
/// </summary>
public enum ScoreSource
{
    Goal,
    Trait,
    Ability,
    Interest,
    Identity,
    Environment,
    Event
}

[Serializable]
public class GoalCondition
{
    public GoalConditionType type;
    public string value;
    public int intValue;
}

/// <summary>
/// 评分项：输入 + 响应曲线 + 权重。
/// curve 为线性分段点 [[x,y], ...]，空列表表示恒等曲线。
/// threshold 仅 Environment.WarehouseScarcity 等输入使用。
/// </summary>
[Serializable]
public class ScoreTerm
{
    public ScoreSource source;
    public string key;
    public float weight;
    public float threshold;
    public List<float[]> curve = new List<float[]>();
}

[Serializable]
public class GoalDefinition
{
    public string id;
    public string displayName;
    public float baseIntensity;
    public List<GoalCondition> conditions = new List<GoalCondition>();
    public List<ScoreTerm> weightTerms = new List<ScoreTerm>();
}

[Serializable]
public class IdentityDefinition
{
    public string id;
    public string displayName;
    public float freedom;
    public bool autonomyEnabled;
}

/// <summary>
/// 自主行为的决策描述。执行 100% 复用 Mission；本类不承担执行逻辑。
/// </summary>
[Serializable]
public class ActionDefinition
{
    public string id;
    public string displayName;
    public string missionId;
    public List<string> identityIds = new List<string>();
    public float baseline;
    /// <summary>
    /// 该自主行动的最小间隔天数。
    /// &gt;0：最近一次结束日 D 之后，D..D+minIntervalDays 不再被自主选择（D+minIntervalDays+1 恢复）。
    /// 0：不增加额外间隔，只沿用“终局当日不立即续接”的通用规则。
    /// 冷却只由该 Action 自己的 missionId 履历计算，玩家任务不触发。
    /// </summary>
    public int minIntervalDays = 0;
    public List<ScoreTerm> scoreTerms = new List<ScoreTerm>();
}

/// <summary>
/// Goal 运行时实例（仅内存计算缓存，不存档）。
/// </summary>
public class GoalInstance
{
    public GoalDefinition Definition;
    public float Intensity;
    public string ReasonLabel;
}

public class TermScore
{
    public ScoreTerm Term;
    public float Input;
    public float Curved;
    public float Contribution;
}

public class ActionScoreResult
{
    public ActionDefinition Action;
    public float Score;
    public string ReasonLabel;
    public string FilterReason;
    public List<TermScore> Terms = new List<TermScore>();
    public bool Eligible => string.IsNullOrEmpty(FilterReason);
}

public class DiscipleDecisionResult
{
    public ActionDefinition Selected;
    public string ReasonLabel;
    public List<ActionScoreResult> Candidates = new List<ActionScoreResult>();
}

/// <summary>
/// Configs/DiscipleAI 三个 JSON 的加载结果。
/// </summary>
public class DiscipleAIConfig
{
    public List<GoalDefinition> Goals = new List<GoalDefinition>();
    public List<ActionDefinition> Actions = new List<ActionDefinition>();
    public List<IdentityDefinition> Identities = new List<IdentityDefinition>();

    public IdentityDefinition GetIdentity(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return Identities.Find(item => item != null && item.id == id);
    }
}
