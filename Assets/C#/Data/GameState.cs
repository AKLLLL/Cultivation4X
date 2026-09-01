using System;
using System.Collections.Generic;
using Cultivation4X.WorldMap;

public static class SaveDataVersion
{
    /// <summary>
    /// 25：加入阶段性修行叙事、经历保留策略和月度叙事幂等状态，不兼容旧档。
    /// </summary>
    public const int Current = 25;
}

[Serializable]
public class GameState
{
    public int version = SaveDataVersion.Current;
    public int currentDay;
    public WorldTimeSaveData worldTime = new WorldTimeSaveData();
    public int randomSeed = 48621;
    public int randomRollCount;
    public PlayerData sect = new PlayerData();
    public WorldMap worldMap;
    public WorldMapProgressState worldMapProgress = new WorldMapProgressState();
    public WarehouseData warehouse = new WarehouseData();
    public List<CharacterState> characters = new List<CharacterState>();
    public List<MissionSaveData> activeMissions = new List<MissionSaveData>();
    public List<EventHistoryRecord> eventHistory = new List<EventHistoryRecord>();
    public List<PendingEvent> pendingEvents = new List<PendingEvent>();
    public int missionCandidateDay = -1;
    public List<string> dailyMissionCandidateIds = new List<string>();
    public List<EventInboxEntry> eventInbox = new List<EventInboxEntry>();
    public string activeEventEntryId;
    public int nextInboxSequence;
    public int eventGeneratedDay = -1;
    public int eventGeneratedOrdinaryCount;
    public DaySettlementSummary unreadDaySettlement;
}

[Serializable]
public class MissionSaveData
{
    public string missionId;
    public string assignedCharacterId;
    public MissionState state;
    public int remainingDays;
    public int elapsedDays;
    public int currentNodeIndex;
    public Reward reward;
    public bool hasCapabilitySnapshot;
    public int capabilityScore;
    public MissionResultTier resultTier;
    public MapMissionContext mapContext;
}
