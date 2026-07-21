using System;
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
        DontDestroyOnLoad(gameObject);
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
                : EventManager.Instance.GetPendingEvents().ToList()
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

            TimeManager.Instance?.RestoreDay(state.currentDay);
            if (PlayerManager.Instance != null) PlayerManager.Instance.playerData = state.sect ?? new PlayerData();
            if (WarehouseManager.Instance != null) WarehouseManager.Instance.warehouseData = state.warehouse ?? new WarehouseData();
            NPCManager.Instance?.RestoreCharacters(state.characters);
            MissionManager.Instance?.RestoreMissions(state.activeMissions);
            EventManager.Instance?.RestoreState(state.eventHistory, state.pendingEvents, state.randomSeed, state.randomRollCount);
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
}
