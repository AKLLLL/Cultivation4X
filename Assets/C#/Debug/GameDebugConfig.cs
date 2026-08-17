using UnityEngine;

public static class GameDebugConfig
{
    /// <summary>
    /// 正式游戏世界地图调试日志开关。默认关闭；需要诊断时在 Inspector/代码中开启。
    /// 测试地图不受此开关限制。
    /// </summary>
    public static bool EnableWorldMapDebug = false;

    public static void LogWorldMap(string message)
    {
        if (EnableWorldMapDebug) Debug.Log(message);
    }

    public static void LogWorldMapWarning(string message)
    {
        if (EnableWorldMapDebug) Debug.LogWarning(message);
    }
}
