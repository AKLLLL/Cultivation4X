using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }
    private const string SaveFileName = "cultivation4x-save.json";
    private string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance == null) new GameObject("SaveManager").AddComponent<SaveManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyUtility.MarkPersistent(gameObject);
    }

    private IEnumerator Start()
    {
        yield return null;
        Load();
    }

    public GameState CaptureState()
    {
        return new GameState
        {
            currentDay = TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay,
            randomSeed = EventManager.Instance == null ? 48621 : EventManager.Instance.RandomSeed,
            randomRollCount = EventManager.Instance == null ? 0 : EventManager.Instance.RandomRollCount,
            sect = PlayerManager.Instance == null ? new PlayerData() : PlayerManager.Instance.playerData,
            warehouse = WarehouseManager.Instance == null ? new WarehouseData() : WarehouseManager.Instance.warehouseData,
            characters = NPCManager.Instance == null
                ? new System.Collections.Generic.List<CharacterState>()
                : NPCManager.Instance.GetAllNPC().Select(npc => npc.Character).ToList(),
            activeMissions = MissionManager.Instance == null
                ? new System.Collections.Generic.List<MissionSaveData>()
                : MissionManager.Instance.GetActiveMissions().Select(mission => mission.ToSaveData()).ToList(),
            eventHistory = EventManager.Instance == null
                ? new System.Collections.Generic.List<EventHistoryRecord>()
                : EventManager.Instance.GetHistory().ToList(),
            pendingEvents = EventManager.Instance == null
                ? new System.Collections.Generic.List<PendingEvent>()
                : EventManager.Instance.GetPendingEvents().ToList(),
            missionCandidateDay = MissionManager.Instance == null ? -1 : MissionManager.Instance.MissionCandidateDay,
            dailyMissionCandidateIds = MissionManager.Instance == null
                ? new System.Collections.Generic.List<string>()
                : MissionManager.Instance.GetDailyMissionCandidateIds().ToList(),
            eventInbox = EventManager.Instance == null ? new System.Collections.Generic.List<EventInboxEntry>() : EventManager.Instance.GetInbox().ToList(),
            activeEventEntryId = EventManager.Instance?.ActiveEventEntryId,
            nextInboxSequence = EventManager.Instance == null ? 0 : EventManager.Instance.NextInboxSequence,
            eventGeneratedDay = EventManager.Instance == null ? -1 : EventManager.Instance.GeneratedDay,
            eventGeneratedOrdinaryCount = EventManager.Instance == null ? 0 : EventManager.Instance.GeneratedOrdinaryCount,
            unreadDaySettlement = TimeManager.Instance?.UnreadDaySettlement
        };
    }

    public void Save()
    {
        try
        {
            string json = JsonConvert.SerializeObject(CaptureState(), Formatting.Indented);
            string temporaryPath = SavePath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(SavePath)) File.Delete(SavePath);
            File.Move(temporaryPath, SavePath);
        }
        catch (Exception exception)
        {
            Debug.LogError($"保存失败: {exception.Message}");
        }
    }

    public bool Load()
    {
        if (!File.Exists(SavePath)) return false;
        try
        {
            GameState state = JsonConvert.DeserializeObject<GameState>(File.ReadAllText(SavePath));
            if (state == null || state.version > SaveDataVersion.Current)
                throw new InvalidDataException("存档版本不受支持");

            MigrateState(state);
            TimeManager.Instance?.RestoreDay(state.currentDay);
            if (PlayerManager.Instance != null) PlayerManager.Instance.playerData = state.sect ?? new PlayerData();
            if (WarehouseManager.Instance != null)
            {
                WarehouseManager.Instance.warehouseData = state.warehouse ?? new WarehouseData();
                WarehouseManager.Instance.NormalizeItems();
            }
            NPCManager.Instance?.RestoreCharacters(state.characters);
            MissionManager.Instance?.RestoreMissions(state.activeMissions);
            int candidateDay = state.missionCandidateDay < 0 ? state.currentDay : state.missionCandidateDay;
            MissionManager.Instance?.RestoreDailyCandidates(candidateDay, state.dailyMissionCandidateIds);
            EventManager.Instance?.RestoreState(state.eventHistory, state.pendingEvents, state.randomSeed, state.randomRollCount,
                state.eventInbox, state.activeEventEntryId, state.nextInboxSequence, state.eventGeneratedDay, state.eventGeneratedOrdinaryCount);
            TimeManager.Instance?.RestoreUnreadSettlement(state.unreadDaySettlement);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"读取存档失败: {exception.Message}");
            return false;
        }
    }

    public void AutoSave() => Save();
    public string GetSavePath() => SavePath;

    public static void MigrateState(GameState state)
    {
        state.sect = state.sect ?? new PlayerData();
        state.sect.missionHallLevel = Mathf.Clamp(state.sect.missionHallLevel, 1, FacilityRules.MaxLevel);
        state.sect.trainingRoomLevel = Mathf.Clamp(state.sect.trainingRoomLevel, 1, FacilityRules.MaxLevel);
        state.sect.warehouseLevel = Mathf.Clamp(state.sect.warehouseLevel, 1, FacilityRules.MaxLevel);
        state.sect.secretRealmLevel = Mathf.Clamp(state.sect.secretRealmLevel, 1, FacilityRules.MaxLevel);
        state.sect.alchemyRoomLevel = Mathf.Clamp(state.sect.alchemyRoomLevel, 1, FacilityRules.MaxLevel);
        state.dailyMissionCandidateIds = state.dailyMissionCandidateIds ?? new System.Collections.Generic.List<string>();
        state.eventInbox = state.eventInbox ?? new System.Collections.Generic.List<EventInboxEntry>();
        state.version = SaveDataVersion.Current;
    }
}
