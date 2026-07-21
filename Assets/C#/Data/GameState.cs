using System;
using System.Collections.Generic;

public static class SaveDataVersion
{
    public const int Current = 1;
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
