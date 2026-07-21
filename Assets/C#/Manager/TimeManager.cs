using System;
using UnityEngine;

/// <summary>
/// 游戏时间管理器。
/// 负责推进游戏天数，并通知其他系统。
/// </summary>
public class TimeManager : MonoBehaviour
{
    public static TimeManager Instance;

    /// <summary>
    /// 当前是第几天。
    /// </summary>
    public int CurrentDay { get; private set; } = 0;

    /// <summary>
    /// 每经过一天触发一次。
    /// 参数：当前天数。
    /// </summary>
    public event Action<int> OnDayPassed;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 点击"结束今天"按钮调用。
    /// </summary>
    public void EndDay()
    {
        CurrentDay++;

        Debug.Log($"今天是第 {CurrentDay} 天");

        // 固定顺序：角色恢复/修炼 -> 任务推进 -> 事件抽取 -> 自动保存。
        NPCManager.Instance?.OnDayPassed();
        OnDayPassed?.Invoke(CurrentDay);
        EventManager.Instance?.ProcessDay(CurrentDay);
        SaveManager.Instance?.AutoSave();
    }

    public void RestoreDay(int day)
    {
        CurrentDay = Mathf.Max(0, day);
    }
}
