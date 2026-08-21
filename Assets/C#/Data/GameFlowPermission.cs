using System;

/// <summary>
/// 游戏流程权限的统一查询入口。
/// 把“宗门是否成立（sectCreated）”“初创发展是否完成（completed）”
/// “是否进入洞府阶段（stage）”三种语义收敛到这里，
/// 系统入口不再散落直接读取 FoundingState 字段。
/// </summary>
public static class GameFlowPermission
{
    /// <summary>宗门已真实成立。选址完成即成立，与后续建筑修复/功法理解无关。</summary>
    public static bool IsSectEstablished(FoundingState founding) =>
        founding != null && founding.sectCreated;

    /// <summary>初创发展完成（修复 + 功法理解 + 路线设施）。只影响发展内容节奏，不影响宗门成立。</summary>
    public static bool IsFoundingDevelopmentComplete(FoundingState founding) =>
        founding != null && founding.completed;

    /// <summary>是否已进入洞府阶段。剧情/发展内容沿用此查询。</summary>
    public static bool HasReachedCave(FoundingState founding) =>
        FoundingRules.HasReachedCave(founding);
}
