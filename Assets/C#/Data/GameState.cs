using System;
using System.Collections.Generic;

public static class SaveDataVersion
{
    public const int Current = 3;
}

[Serializable]
public class GameState
{
    public int version = SaveDataVersion.Current;
    public int currentDay;
    public int randomSeed = 48621;
    public int randomRollCount;
    public PlayerData sect = new PlayerData();
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
}
