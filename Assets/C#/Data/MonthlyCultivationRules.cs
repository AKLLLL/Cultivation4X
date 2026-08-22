using System;
using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;
using UnityEngine;

public enum MonthlyActivityType
{
    Training,
    SectDuty,
    Free
}

[Serializable]
public class MonthlyDisciplePlan
{
    public string characterId;
    public int monthIndex = 1;
    public int trainingPercent = 50;
    public int sectDutyPercent = 20;
    public int freePercent = 30;
    public int usedTrainingDays;
    public int usedSectDutyDays;
    public int usedFreeDays;
    public int transferredTrainingDays;
    public bool useSpiritStone;
}

public static class MonthlyPlanRules
{
    public const int DaysPerMonth = 30;

    public static int MonthIndex(int day) => day <= 0 ? 1 : (day - 1) / DaysPerMonth + 1;
    public static int DayOfMonth(int day) => day <= 0 ? 0 : (day - 1) % DaysPerMonth + 1;
    public static int EditableMonth(int day) => day <= 0 ? 1 : MonthIndex(day) + 1;

    public static MonthlyDisciplePlan GetPlan(string characterId, int monthIndex)
    {
        return PlayerManager.Instance?.playerData?.monthlyPlans?.FirstOrDefault(plan =>
            plan != null && plan.characterId == characterId && plan.monthIndex == monthIndex);
    }

    public static MonthlyDisciplePlan GetOrCreateEditablePlan(string characterId, int day)
    {
        PlayerData player = PlayerManager.Instance?.playerData;
        if (player == null || player.founding == null || !player.founding.sectCreated || string.IsNullOrWhiteSpace(characterId)) return null;
        player.monthlyPlans = player.monthlyPlans ?? new List<MonthlyDisciplePlan>();
        int month = EditableMonth(day);
        MonthlyDisciplePlan plan = player.monthlyPlans.FirstOrDefault(item =>
            item != null && item.characterId == characterId && item.monthIndex == month);
        if (plan != null) return plan;
        plan = new MonthlyDisciplePlan { characterId = characterId, monthIndex = month };
        player.monthlyPlans.Add(plan);
        return plan;
    }

    public static bool TrySetPlan(MonthlyDisciplePlan plan, int training, int duty, int free, out string reason)
    {
        if (plan == null) { reason = "计划不存在"; return false; }
        if (training < 0 || duty < 0 || free < 0 || training > 100 || duty > 100 || free > 100 ||
            training % 10 != 0 || duty % 10 != 0 || free % 10 != 0 || training + duty + free != 100)
        { reason = "三项比例必须以10%递增且合计100%"; return false; }
        NPCRuntime npc = NPCManager.Instance?.GetRuntime(plan.characterId);
        if (training > 0 && npc?.Character != null && npc.Character.naqiProgress >= 100f)
        { reason = "该弟子已完成炼气纳气，不能再安排纳气日"; return false; }
        plan.trainingPercent = training;
        plan.sectDutyPercent = duty;
        plan.freePercent = free;
        reason = null;
        return true;
    }

    public static MonthlyActivityType PeekScheduledActivity(NPCRuntime npc, int day)
    {
        MonthlyDisciplePlan plan = GetPlan(npc?.CharacterId, MonthIndex(day));
        if (plan == null) return MonthlyActivityType.Free;
        float progress = DayOfMonth(day) / (float)DaysPerMonth;
        float transferProgress = plan.transferredTrainingDays * progress;
        float trainingDeficit = (plan.trainingPercent * DaysPerMonth / 100f * progress - transferProgress) - plan.usedTrainingDays;
        float dutyDeficit = plan.sectDutyPercent * DaysPerMonth / 100f * progress - plan.usedSectDutyDays;
        float freeDeficit = (plan.freePercent * DaysPerMonth / 100f * progress + transferProgress) - plan.usedFreeDays;
        if (trainingDeficit >= dutyDeficit && trainingDeficit >= freeDeficit) return MonthlyActivityType.Training;
        return dutyDeficit >= freeDeficit ? MonthlyActivityType.SectDuty : MonthlyActivityType.Free;
    }

    public static void Consume(NPCRuntime npc, int day, MonthlyActivityType activity)
    {
        MonthlyDisciplePlan plan = GetPlan(npc?.CharacterId, MonthIndex(day));
        if (plan == null) return;
        if (activity == MonthlyActivityType.Training) plan.usedTrainingDays++;
        else if (activity == MonthlyActivityType.SectDuty) plan.usedSectDutyDays++;
        else plan.usedFreeDays++;
    }

    public static int RemainingFreeDays(NPCRuntime npc, int day)
    {
        MonthlyDisciplePlan plan = GetPlan(npc?.CharacterId, MonthIndex(day));
        int target = plan == null ? DaysPerMonth : plan.freePercent * DaysPerMonth / 100 + plan.transferredTrainingDays;
        return Mathf.Max(0, target - (plan?.usedFreeDays ?? 0));
    }

    public static bool CanStartAutonomousMission(NPCRuntime npc, int needDays, int decisionDay, out string reason)
    {
        int futureCalendarDays = DaysPerMonth - DayOfMonth(decisionDay);
        if (needDays <= 0 || needDays > futureCalendarDays)
        { reason = "任务会跨越月末"; return false; }
        if (needDays > RemainingFreeDays(npc, decisionDay))
        { reason = "本月剩余自由日不足"; return false; }
        reason = null;
        return true;
    }

    public static void TransferRemainingTrainingToFree(NPCRuntime npc, int day)
    {
        MonthlyDisciplePlan plan = GetPlan(npc?.CharacterId, MonthIndex(day));
        if (plan == null) return;
        int targetTraining = plan.trainingPercent * DaysPerMonth / 100;
        int remaining = Mathf.Max(0, targetTraining - plan.usedTrainingDays - plan.transferredTrainingDays);
        plan.transferredTrainingDays += remaining;
    }
}

public static class NaqiGrowthRules
{
    public const string SpiritStoneId = "LingShi_001";
    public const string RegulatingPillId = "regulating_pill_001";
    public const string DisorderEventId = "qi_disorder";

    public static void StartDay(NPCRuntime npc)
    {
        if (npc?.Character == null) return;
        npc.Character.cultivation = 0;
        npc.Character.completedMajorCycleToday = false;
    }

    public static void EndDay(NPCRuntime npc)
    {
        if (npc?.Character == null || npc.Character.qiDisorderRemainingDays <= 0 ||
            npc.Character.qiDisorderResponse == QiDisorderResponse.Pending) return;
        npc.Character.qiDisorderRemainingDays--;
        if (npc.Character.qiDisorderRemainingDays <= 0)
            npc.Character.qiDisorderResponse = QiDisorderResponse.None;
    }

    public static float AddDailyAura(NPCRuntime npc, int amount)
    {
        if (npc?.Character == null || !npc.Character.IsAlive || amount <= 0 || npc.Character.naqiProgress >= 100f) return 0f;
        int oldAura = Mathf.Clamp(npc.Character.cultivation, 0, 100);
        int newAura = Mathf.Clamp(oldAura + amount, 0, 100);
        if (newAura <= oldAura) return 0f;
        npc.Character.cultivation = newAura;
        float gain = BaseCurve(newAura) - BaseCurve(oldAura);
        if (newAura >= 100 && oldAura < 100 && !npc.Character.completedMajorCycleToday)
        {
            gain += 2f;
            npc.Character.completedMajorCycleToday = true;
            AddTechniqueMastery(npc, 1f);
        }
        float before = npc.Character.naqiProgress;
        npc.Character.naqiProgress = Mathf.Clamp(before + gain, 0f, 100f);
        if (npc.Character.naqiProgress >= 100f)
        {
            MonthlyPlanRules.TransferRemainingTrainingToFree(npc, TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay);
            TimeManager.Instance?.RecordDayNotice($"{npc.Character.displayName} 已完成炼气纳气，本月剩余修炼日转为自由日");
        }
        return npc.Character.naqiProgress - before;
    }

    public static void AddTechniqueMastery(NPCRuntime npc, float amount)
    {
        if (npc?.Character == null || amount <= 0f) return;
        float before = npc.Character.techniqueMastery;
        npc.Character.techniqueMastery = Mathf.Clamp(before + amount, 0f, 100f);
    }

    public static void ProcessTrainingDay(NPCRuntime npc, int day)
    {
        if (npc?.Character == null || npc.Character.naqiProgress >= 100f) return;
        if (npc.Character.qiDisorderResponse == QiDisorderResponse.Pending ||
            npc.Character.qiDisorderResponse == QiDisorderResponse.Paused) return;
        MonthlyDisciplePlan plan = MonthlyPlanRules.GetPlan(npc.CharacterId, MonthlyPlanRules.MonthIndex(day));
        bool wantsStone = plan != null && plan.useSpiritStone;
        bool usedStone = wantsStone && WarehouseManager.Instance != null &&
            WarehouseManager.Instance.RemoveItem(SpiritStoneId, 1);
        if (wantsStone && !usedStone)
            TimeManager.Instance?.RecordDayNotice($"{npc.Character.displayName} 灵石不足，按普通纳气结算");
        float aura = SectAuraFactor() * RootFactor(npc.Physique) * MasteryFactor(npc.Character.techniqueMastery) *
            RoomFactor() * (usedStone ? 1.25f : 1f) *
            (npc.Character.qiDisorderResponse == QiDisorderResponse.Continuing ? 0.7f : 1f);
        AddDailyAura(npc, Mathf.RoundToInt(Mathf.Clamp(90f * aura, 0f, 100f)));
        AddTechniqueMastery(npc, 0.5f + npc.Comprehension / 10f);
        PlayerManager.Instance?.ProcessIdleFounderDay(npc);
        EventManager.Instance?.TryTriggerSource(EventSource.Training, npc);
        RollDisorder(npc, day);
    }

    private static float BaseCurve(int aura) { float ratio = aura / 100f; return 2f * ratio * ratio; }
    private static float RootFactor(int root) => Mathf.Lerp(0.75f, 1.25f, Mathf.InverseLerp(5f, 20f, root));
    private static float MasteryFactor(float mastery) => Mathf.Lerp(0.75f, 1.25f, Mathf.Clamp01(mastery / 100f));
    private static float RoomFactor()
    {
        int level = PlayerManager.Instance == null ? 0 : PlayerManager.Instance.GetFacilityLevel(FacilityType.TrainingRoom);
        return level <= 0 ? 0.8f : level == 1 ? 1f : level == 2 ? 1.1f : 1.2f;
    }
    private static float SectAuraFactor()
    {
        MapSiteData site = WorldMapProgressRules.GetSectBase(WorldMapSession.Progress);
        WorldMap map = WorldMapSession.Current;
        float aura = site != null && map?.cells != null && site.cellIndex >= 0 && site.cellIndex < map.cells.Length
            ? map.cells[site.cellIndex].totalAura : 0.5f;
        return Mathf.Lerp(0.75f, 1.25f, Mathf.Clamp01(aura));
    }

    private static void RollDisorder(NPCRuntime npc, int day)
    {
        CharacterState state = npc.Character;
        float raw = 3f + 3f * state.cultivation / 100f + 0.25f * (12.5f - npc.Physique) +
            0.04f * (50f - state.techniqueMastery) + (state.qiDisorderResponse == QiDisorderResponse.Continuing ? 3f : 0f);
        float chance = Mathf.Clamp(raw, 1f, 12f) / 100f;
        int seed = EventManager.Instance == null ? 48621 : EventManager.Instance.RandomSeed;
        uint hash = StableHash(seed, day, npc.CharacterId);
        if ((hash % 1000000u) / 1000000f >= chance) return;
        bool repeated = state.qiDisorderRemainingDays > 0;
        state.qiDisorderRemainingDays = 10;
        if (!repeated)
        {
            bool queued = EventManager.Instance != null &&
                EventManager.Instance.TryEnqueueRepeatableEventById(DisorderEventId, npc);
            state.qiDisorderResponse = queued ? QiDisorderResponse.Pending : QiDisorderResponse.Paused;
            if (!queued) TimeManager.Instance?.RecordDayNotice($"{state.displayName} 的紊乱事件未能入箱，已采用安全的暂停纳气方案");
        }
        state.AddLifeRecord(day, "Cultivation", repeated ? "灵气紊乱再次加重，持续时间重置为10天" : "纳气时发生灵气紊乱", DisorderEventId);
        TimeManager.Instance?.RecordDayNotice($"{state.displayName} 发生灵气紊乱");
    }

    private static uint StableHash(int seed, int day, string id)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)seed) * 16777619u;
            hash = (hash ^ (uint)day) * 16777619u;
            foreach (char c in id ?? string.Empty) hash = (hash ^ c) * 16777619u;
            return hash;
        }
    }
}
