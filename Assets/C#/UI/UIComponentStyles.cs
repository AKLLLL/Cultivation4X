using UnityEngine;

/// <summary>
/// 新主界面与地图 HUD 共用的轻量视觉尺寸规范。
/// 这里只保存表现常量，不持有状态，也不替代 UITheme。
/// </summary>
public static class UIComponentStyles
{
    public const float CompactTabBarHeight = 40f;
    public const float CompactTabButtonWidth = 92f;
    public const float CompactTabSpacing = 4f;
    public const float InfoCardSpacing = 6f;

    public static readonly Color TabSelected = new Color(0.48f, 0.38f, 0.17f, 1f);
    public static readonly Color TabNormal = new Color(0.075f, 0.125f, 0.095f, 0.98f);
    public static readonly Color InfoCard = new Color(0.045f, 0.090f, 0.070f, 0.90f);
    public static readonly Color PanelOutline = new Color(0.39f, 0.32f, 0.16f, 0.75f);
}
