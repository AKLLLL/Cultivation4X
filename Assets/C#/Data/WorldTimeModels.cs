using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

[Flags]
public enum PauseReason
{
    None = 0,
    Player = 1 << 0,
    CriticalEvent = 1 << 1,
    MonthEnd = 1 << 2,
    SettlementFailure = 1 << 3,
    FlowState = 1 << 4
}

[Serializable]
public struct GameDateTime
{
    public int year;
    public int month;
    public int day;
    public float hour;
    public int absoluteDay;

    public string DateLabel => $"第{year}年·第{month}月·第{day}日";
    public string TimeLabel
    {
        get
        {
            int wholeHour = Mathf.Clamp(Mathf.FloorToInt(hour), 0, 23);
            int minute = Mathf.Clamp(Mathf.FloorToInt((hour - wholeHour) * 60f + 0.0001f), 0, 59);
            return $"{wholeHour:00}:{minute:00}";
        }
    }
}

public static class GameCalendarRules
{
    public const int DaysPerMonth = 30;
    public const int MonthsPerYear = 12;
    public const int DaysPerYear = DaysPerMonth * MonthsPerYear;

    public static GameDateTime FromActiveDay(int absoluteDay, float hour)
    {
        int safeDay = Mathf.Max(1, absoluteDay);
        int zeroBased = safeDay - 1;
        return new GameDateTime
        {
            year = zeroBased / DaysPerYear + 1,
            month = zeroBased % DaysPerYear / DaysPerMonth + 1,
            day = zeroBased % DaysPerMonth + 1,
            hour = Mathf.Clamp(hour, 0f, 23.9999f),
            absoluteDay = safeDay
        };
    }
}

[Serializable]
public class DailyScheduleSegment
{
    public float startHour;
    public float endHour;
    public string label;

    public bool Contains(float hour) => hour >= startHour && hour < endHour;
}

[Serializable]
public class DiscipleDailySchedule
{
    public string characterId;
    public MonthlyActivityType activity;
    public string cultivationActionId;
    public bool missionOccupied;
    public List<DailyScheduleSegment> segments = new List<DailyScheduleSegment>();

    public string ActivityAt(float hour)
    {
        if (hour < 6f || hour >= 20f) return "休息";
        DailyScheduleSegment segment = segments?.FirstOrDefault(item => item != null && item.Contains(hour));
        return string.IsNullOrWhiteSpace(segment?.label) ? "自由活动" : segment.label;
    }
}

[Serializable]
public class DailyScheduleState
{
    public int day;
    public List<DiscipleDailySchedule> disciples = new List<DiscipleDailySchedule>();

    public DiscipleDailySchedule Get(string characterId) =>
        string.IsNullOrWhiteSpace(characterId) ? null :
        disciples?.FirstOrDefault(item => item?.characterId == characterId);
}

[Serializable]
public class WorldTimeSaveData
{
    public float currentHour = 6f;
    public float selectedSpeed = 1f;
    public bool dayPrepared;
    public DailyScheduleState dailySchedule;
}

[Serializable]
public class WorldTimeConfig
{
    public float secondsPerDay = 30f;
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<float> speedMultipliers = new List<float> { 1f, 2f, 4f };
    public float dayStartHour = 6f;
    public float dayEndingHour = 20f;

    public bool IsValid() => secondsPerDay > 0f && dayStartHour >= 0f &&
        dayEndingHour > dayStartHour && dayEndingHour < 24f &&
        speedMultipliers != null && speedMultipliers.Count == 3 &&
        speedMultipliers.SequenceEqual(new[] { 1f, 2f, 4f });
}

public static class WorldTimeConfigLoader
{
    public const string ResourcePath = "Configs/Time/world_time";
    private static WorldTimeConfig cached;

    public static WorldTimeConfig Load()
    {
        if (cached != null) return cached;
        TextAsset file = Resources.Load<TextAsset>(ResourcePath);
        try
        {
            cached = file == null ? null : JsonConvert.DeserializeObject<WorldTimeConfig>(file.text);
        }
        catch (Exception exception)
        {
            Debug.LogError($"世界时间配置解析失败: {exception.Message}");
        }
        if (cached == null || !cached.IsValid())
        {
            Debug.LogError("世界时间配置缺失或无效，使用安全默认值。");
            cached = new WorldTimeConfig();
        }
        return cached;
    }

    public static void ClearCacheForTests() => cached = null;
}
