using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class WorldTimeTests
{
    private readonly List<GameObject> created = new List<GameObject>();

    [SetUp]
    public void SetUp()
    {
        TimeManager.Instance = null;
        WorldTimeConfigLoader.ClearCacheForTests();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject item in created)
            if (item != null) Object.DestroyImmediate(item);
        created.Clear();
        TimeManager.Instance = null;
        WorldTimeConfigLoader.ClearCacheForTests();
    }

    [TestCase(1, 1, 1, 1)]
    [TestCase(30, 1, 1, 30)]
    [TestCase(31, 1, 2, 1)]
    [TestCase(360, 1, 12, 30)]
    [TestCase(361, 2, 1, 1)]
    public void Calendar_ConvertsAbsoluteDay(int absoluteDay, int year, int month, int day)
    {
        GameDateTime result = GameCalendarRules.FromActiveDay(absoluteDay, 14.5f);
        Assert.AreEqual(year, result.year);
        Assert.AreEqual(month, result.month);
        Assert.AreEqual(day, result.day);
        Assert.AreEqual("14:30", result.TimeLabel);
    }

    [Test]
    public void Config_UsesApprovedClockAndSpeeds()
    {
        WorldTimeConfig config = WorldTimeConfigLoader.Load();
        Assert.IsTrue(config.IsValid());
        Assert.AreEqual(30f, config.secondsPerDay);
        CollectionAssert.AreEqual(new[] { 1f, 2f, 4f }, config.speedMultipliers);
        CollectionAssert.AreEqual(new[] { 30f, 15f, 7.5f },
            config.speedMultipliers.Select(speed => config.secondsPerDay / speed).ToArray());
        Assert.AreEqual(6f, config.dayStartHour);
        Assert.AreEqual(20f, config.dayEndingHour);
    }

    [Test]
    public void SaveRestore_PreservesClockSpeedAndLockedSchedule_ButLoadsPaused()
    {
        TimeManager time = AddTime();
        DailyScheduleState schedule = new DailyScheduleState
        {
            day = 1,
            disciples = new List<DiscipleDailySchedule>
            {
                new DiscipleDailySchedule
                {
                    characterId = "disciple-1",
                    activity = MonthlyActivityType.Training,
                    cultivationActionId = "meditate",
                    segments = new List<DailyScheduleSegment>
                    {
                        new DailyScheduleSegment { startHour = 11f, endHour = 18f, label = "运转功法" }
                    }
                }
            }
        };
        time.RestoreWorldTime(new WorldTimeSaveData
        {
            currentHour = 14.583333f,
            selectedSpeed = 4f,
            dayPrepared = true,
            dailySchedule = schedule
        });

        WorldTimeSaveData saved = time.CaptureWorldTime();
        Assert.AreEqual(14.583333f, saved.currentHour, 0.0001f);
        Assert.AreEqual(4f, saved.selectedSpeed);
        Assert.IsTrue(saved.dayPrepared);
        Assert.AreEqual("14:35", time.CurrentDateTime.TimeLabel);
        Assert.AreEqual("meditate", saved.dailySchedule.Get("disciple-1").cultivationActionId);
        Assert.AreEqual("运转功法", time.GetCurrentActivityLabel("disciple-1"));
        Assert.IsTrue(time.IsPaused);
        Assert.IsTrue((time.PauseReasons & PauseReason.Player) != 0);
    }

    [Test]
    public void HourAdvance_CrossesSixTwentyAndMidnightExactlyOnce()
    {
        TimeManager time = AddTime();
        int hourSix = 0;
        int hourTwenty = 0;
        int dayEnded = 0;
        time.OnHourChanged += value =>
        {
            if (value.absoluteDay == 1 && Mathf.Approximately(value.hour, 6f)) hourSix++;
            if (value.absoluteDay == 1 && Mathf.Approximately(value.hour, 20f)) hourTwenty++;
        };
        time.OnDayEnded += _ => dayEnded++;
        time.RestoreWorldTime(new WorldTimeSaveData { currentHour = 5.5f, selectedSpeed = 1f });

        for (int index = 0; index < 19; index++) time.AdvanceOneHourForTesting();

        Assert.AreEqual(1, hourSix);
        Assert.AreEqual(1, hourTwenty);
        Assert.AreEqual(1, dayEnded);
        Assert.AreEqual(1, time.CurrentDay);
        Assert.AreEqual(0.5f, time.CurrentHour, 0.001f);
    }

    [Test]
    public void FastAdvance_TriggersMonthEndOnce_AndDay31StartsNewMonth()
    {
        TimeManager time = AddTime();
        int monthEnds = 0;
        time.OnMonthEnded += _ => monthEnds++;

        Assert.AreEqual(30, time.AdvanceDaysForTesting(30));
        Assert.AreEqual(1, monthEnds);
        Assert.IsTrue((time.PauseReasons & PauseReason.MonthEnd) != 0);
        time.MarkSettlementRead();
        Assert.AreEqual(1, time.AdvanceDaysForTesting(1));

        Assert.AreEqual(31, time.CurrentDay);
        GameDateTime nextActiveDate = time.CurrentDateTime;
        Assert.AreEqual(1, nextActiveDate.year);
        Assert.AreEqual(2, nextActiveDate.month);
        Assert.AreEqual(2, nextActiveDate.day);
        Assert.AreEqual(1, monthEnds);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MarkSettlementRead_ClearsUnreadBeforeNotifying(bool isMonthEnd)
    {
        TimeManager time = AddTime();
        time.RestoreUnreadSettlement(new DaySettlementSummary { day = 1, isMonthEnd = isMonthEnd });
        int notifications = 0;
        bool unreadWasClearedWhenNotified = false;
        time.OnTimeChanged += _ =>
        {
            notifications++;
            unreadWasClearedWhenNotified = time.UnreadDaySettlement == null;
        };

        time.MarkSettlementRead();

        Assert.IsNull(time.UnreadDaySettlement);
        Assert.AreEqual(1, notifications);
        Assert.IsTrue(unreadWasClearedWhenNotified);
        Assert.IsFalse((time.PauseReasons & PauseReason.MonthEnd) != 0);
        if (isMonthEnd) Assert.IsTrue((time.PauseReasons & PauseReason.Player) != 0);
    }

    [Test]
    public void TestingAdvance_NinetyDaysHasNoMissingOrDuplicateMonthEnds()
    {
        TimeManager time = AddTime();
        int days = 0;
        int months = 0;
        time.OnDayEnded += _ => days++;
        time.OnMonthEnded += _ => months++;

        int advanced = time.AdvanceDaysForTesting(90);

        Assert.AreEqual(90, advanced);
        Assert.AreEqual(90, days);
        Assert.AreEqual(3, months);
        Assert.AreEqual(90, time.CurrentDay);
    }

    private TimeManager AddTime()
    {
        GameObject root = new GameObject("WorldTimeTest");
        created.Add(root);
        TimeManager time = root.AddComponent<TimeManager>();
        TimeManager.Instance = time;
        return time;
    }
}
