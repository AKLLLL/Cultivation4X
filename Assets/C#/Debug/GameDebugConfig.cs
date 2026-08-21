using UnityEngine;

public static class GameDebugConfig
{
    /// <summary>
    /// 正式游戏世界地图调试日志开关。默认关闭；需要诊断时在 Inspector/代码中开启。
    /// 测试地图不受此开关限制。
    /// </summary>
    public static bool EnableWorldMapDebug = false;

    /// <summary>
    /// 仅开发/测试存档使用：临时豁免 DevelopResourceNode / DevelopSpiritMine 的
    /// 影响力范围检查。其余检查（发现状态、地点状态、任务流程）仍完整执行。
    /// 不写入存档，重启恢复关闭。
    /// </summary>
    public static bool BypassResourceDevelopmentInfluence = false;

    public static void LogWorldMap(string message)
    {
        if (EnableWorldMapDebug) Debug.Log(message);
    }

    public static void LogWorldMapWarning(string message)
    {
        if (EnableWorldMapDebug) Debug.LogWarning(message);
    }
}
