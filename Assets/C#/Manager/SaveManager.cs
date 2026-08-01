using System;
using System.Collections;
using System.IO;
using System.Linq;
using Cultivation4X.WorldMap;
using Newtonsoft.Json;
using UnityEngine;

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }
    public bool IsInitializationComplete { get; private set; }
    public bool LoadedExistingSave { get; private set; }
    public bool InitializationFailed { get; private set; }
    public event Action<bool> OnInitializationCompleted;
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
        if (File.Exists(SavePath))
        {
            LoadedExistingSave = Load();
            InitializationFailed = !LoadedExistingSave;
        }
        else
        {
            int seed = unchecked(Environment.TickCount ^ DateTime.UtcNow.Millisecond);
            PlayerManager.Instance?.InitializeNewFoundingGame(seed);
            NPCManager.Instance?.ClearCharacters();
            if (WarehouseManager.Instance != null) WarehouseManager.Instance.warehouseData = new WarehouseData();
            MissionManager.Instance?.RestoreDailyCandidates(TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay,
                new System.Collections.Generic.List<string>());
            LoadedExistingSave = false;
            Save();
        }
        IsInitializationComplete = true;
        OnInitializationCompleted?.Invoke(LoadedExistingSave);
    }

    public GameState CaptureState()
    {
        return new GameState
        {
            currentDay = TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay,
            randomSeed = EventManager.Instance == null ? 48621 : EventManager.Instance.RandomSeed,
            randomRollCount = EventManager.Instance == null ? 0 : EventManager.Instance.RandomRollCount,
            sect = PlayerManager.Instance == null ? new PlayerData() : PlayerManager.Instance.playerData,
            worldMap = WorldMapSession.Current,
            worldMapProgress = WorldMapSession.Progress ?? new WorldMapProgressState(),
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
            if (state.version < SaveDataVersion.Current)
                throw new InvalidDataException("世界地图生成版本更新不兼容旧档，请删除旧存档并开始新游戏");

            ValidateWorldMapState(state);
            MigrateState(state);
            TimeManager.Instance?.RestoreDay(state.currentDay);
            if (PlayerManager.Instance != null) PlayerManager.Instance.playerData = state.sect ?? new PlayerData();
            WorldMapSession.Set(state.worldMap, state.worldMapProgress);
            if (WarehouseManager.Instance != null)
            {
                WarehouseManager.Instance.warehouseData = state.warehouse ?? new WarehouseData();
                WarehouseManager.Instance.NormalizeItems();
            }
            NPCManager.Instance?.RestoreCharacters(state.characters);
            MissionManager.Instance?.RestoreMissions(state.activeMissions);
            PlayerManager.Instance?.ReconcileReservedLabor(MissionManager.Instance?.GetActiveMissions());
            int candidateDay = state.missionCandidateDay < 0 ? state.currentDay : state.missionCandidateDay;
            MissionManager.Instance?.RestoreDailyCandidates(candidateDay, state.dailyMissionCandidateIds);
            EventManager.Instance?.RestoreState(state.eventHistory, state.pendingEvents, state.randomSeed, state.randomRollCount,
                state.eventInbox, state.activeEventEntryId, state.nextInboxSequence, state.eventGeneratedDay, state.eventGeneratedOrdinaryCount);
            TimeManager.Instance?.RestoreUnreadSettlement(state.unreadDaySettlement);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"读取存档失败: {exception}");
            return false;
        }
    }

    public void AutoSave() => Save();
    public string GetSavePath() => SavePath;

    public static void MigrateState(GameState state)
    {
        int sourceVersion = state.version;
        state.sect = state.sect ?? new PlayerData();
        state.worldMapProgress = state.worldMapProgress ?? new WorldMapProgressState();
        state.worldMapProgress.revealedCellIndices =
            state.worldMapProgress.revealedCellIndices ?? new System.Collections.Generic.List<int>();
        state.worldMapProgress.mapSites =
            state.worldMapProgress.mapSites ?? new System.Collections.Generic.List<MapSiteData>();
        int minimumFacilityLevel = sourceVersion < 4 ? 1 : 0;
        state.sect.missionHallLevel = Mathf.Clamp(state.sect.missionHallLevel, minimumFacilityLevel, FacilityRules.MaxLevel);
        state.sect.trainingRoomLevel = Mathf.Clamp(state.sect.trainingRoomLevel, minimumFacilityLevel, FacilityRules.MaxLevel);
        state.sect.warehouseLevel = Mathf.Clamp(state.sect.warehouseLevel, minimumFacilityLevel, FacilityRules.MaxLevel);
        state.sect.secretRealmLevel = Mathf.Clamp(state.sect.secretRealmLevel, minimumFacilityLevel, FacilityRules.MaxLevel);
        state.sect.alchemyRoomLevel = Mathf.Clamp(state.sect.alchemyRoomLevel, minimumFacilityLevel, FacilityRules.MaxLevel);
        state.sect.explorationHallLevel = Mathf.Clamp(state.sect.explorationHallLevel, minimumFacilityLevel, FacilityRules.MaxLevel);
        state.sect.protectionArrayLevel = Mathf.Clamp(state.sect.protectionArrayLevel, 0, FacilityRules.MaxLevel);
        state.sect.inheritanceChamberLevel = Mathf.Clamp(state.sect.inheritanceChamberLevel, 0, FacilityRules.MaxLevel);
        state.sect.forgeRoomLevel = Mathf.Clamp(state.sect.forgeRoomLevel, 0, FacilityRules.MaxLevel);
        state.sect.formationPlatformLevel = Mathf.Clamp(state.sect.formationPlatformLevel, 0, FacilityRules.MaxLevel);
        if (sourceVersion < 4)
        {
            state.sect.founding = new FoundingState
            {
                initialized = true,
                completed = true,
                stage = FoundingStage.Completed,
                candidates = new System.Collections.Generic.List<FounderCandidateData>(),
                selectedFounderIds = new System.Collections.Generic.List<string>(),
                village = new VillageState(),
                externalThreat = new ActiveThreatState()
            };
        }
        else
        {
            state.sect.founding = state.sect.founding ?? new FoundingState();
            state.sect.founding.candidates = state.sect.founding.candidates ?? new System.Collections.Generic.List<FounderCandidateData>();
            state.sect.founding.selectedFounderIds = state.sect.founding.selectedFounderIds ?? new System.Collections.Generic.List<string>();
            state.sect.founding.village = state.sect.founding.village ?? new VillageState();
            state.sect.founding.externalThreat = NormalizeThreatState(state.sect.founding.externalThreat);
            state.sect.founding.techniqueUnderstanding = Mathf.Clamp(state.sect.founding.techniqueUnderstanding, 0, FoundingRules.MaxUnderstanding);
            state.sect.founding.village.relation = Mathf.Clamp(state.sect.founding.village.relation, 0, 100);
            state.sect.founding.village.reservedLabor = Mathf.Clamp(state.sect.founding.village.reservedLabor, 0,
                Mathf.Max(0, state.sect.founding.village.totalLabor));
            foreach (FounderCandidateData candidate in state.sect.founding.candidates.Where(item => item != null))
                if (candidate.combatComprehension <= 0)
                    candidate.combatComprehension = Mathf.Max(0, candidate.comprehension);
        }
        state.sect.founding.externalThreat = NormalizeThreatState(state.sect.founding.externalThreat);
        state.activeMissions = (state.activeMissions ?? new System.Collections.Generic.List<MissionSaveData>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.missionId))
            .ToList();
        state.pendingEvents = (state.pendingEvents ?? new System.Collections.Generic.List<PendingEvent>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.eventId))
            .ToList();
        foreach (PendingEvent item in state.pendingEvents)
            item.participantIds = CleanParticipantIds(item.participantIds);
        state.eventHistory = (state.eventHistory ?? new System.Collections.Generic.List<EventHistoryRecord>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.eventId))
            .ToList();
        foreach (EventHistoryRecord item in state.eventHistory)
            item.participantIds = CleanParticipantIds(item.participantIds);
        state.sect.explorationRegions = (state.sect.explorationRegions ?? new System.Collections.Generic.List<ExplorationRegionState>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.regionId))
            .GroupBy(item => item.regionId)
            .Select(group => new ExplorationRegionState
            {
                regionId = group.Key,
                stage = Mathf.Clamp(group.Max(item => item.stage), 0, ExplorationRules.MaxStage)
            }).ToList();
        state.dailyMissionCandidateIds = state.dailyMissionCandidateIds ?? new System.Collections.Generic.List<string>();
        state.eventInbox = (state.eventInbox ?? new System.Collections.Generic.List<EventInboxEntry>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.entryId) && !string.IsNullOrWhiteSpace(item.eventId))
            .ToList();
        foreach (EventInboxEntry item in state.eventInbox)
            item.participantIds = CleanParticipantIds(item.participantIds);
        state.characters = state.characters ?? new System.Collections.Generic.List<CharacterState>();
        foreach (CharacterState character in state.characters.Where(item => item != null))
        {
            character.traitIds = character.traitIds ?? new System.Collections.Generic.List<string>();
            character.relationships = character.relationships ?? new System.Collections.Generic.List<RelationshipRecord>();
            character.lifeRecords = character.lifeRecords ?? new System.Collections.Generic.List<LifeRecord>();
            character.combatExperience = Mathf.Max(0, character.combatExperience);
            if (character.hasGeneratedProfile && character.baseCombatComprehension <= 0)
                character.baseCombatComprehension = Mathf.Max(0, character.baseComprehension);
        }
        state.version = SaveDataVersion.Current;
    }

    public static void ValidateWorldMapState(GameState state)
    {
        if (state?.worldMap == null)
            throw new InvalidDataException("存档缺少世界地图快照");

        WorldMap map = state.worldMap;
        long expectedCellCount = (long)map.width * map.height;
        if (map.width < 8 || map.height < 8 || expectedCellCount > int.MaxValue ||
            map.cells == null || map.cells.Length != expectedCellCount)
            throw new InvalidDataException("世界地图尺寸或格子数量无效");
        if (map.generationVersion != 3 || map.generationSettings == null)
            throw new InvalidDataException("世界地图生成版本或参数快照无效");
        if (MapGenerationSettingsValidator.Validate(map.generationSettings).Count > 0 ||
            map.generationSettings.width != map.width ||
            map.generationSettings.height != map.height ||
            map.generationSettings.seed != map.userSeed ||
            map.generationSettings.generationVersion != map.generationVersion)
            throw new InvalidDataException("世界地图生成参数快照不一致");

        for (int index = 0; index < map.cells.Length; index++)
        {
            WorldCell cell = map.cells[index];
            if (cell == null || cell.index != index ||
                cell.coord.col != index % map.width || cell.coord.row != index / map.width)
                throw new InvalidDataException($"世界地图格子 {index} 无效");
        }

        if (map.rivers == null || map.spiritVeins == null || map.pointsOfInterest == null)
            throw new InvalidDataException("世界地图覆盖层数据缺失");
        if (map.rivers.Any(segment => segment == null ||
            segment.fromCellIndex < 0 || segment.fromCellIndex >= map.cells.Length ||
            segment.toCellIndex < 0 || segment.toCellIndex >= map.cells.Length ||
            map.GetDirection(segment.fromCellIndex, segment.toCellIndex) < 0))
            throw new InvalidDataException("世界地图河流索引无效");
        if (map.spiritVeins.Any(vein => vein == null || vein.pathCellIndices == null ||
            vein.pathCellIndices.Any(index => index < 0 || index >= map.cells.Length)))
            throw new InvalidDataException("世界地图灵脉索引无效");

        string[] requiredPointIds = { "qingyun_outskirts", "mistwood", "chixia_ridge" };
        if (map.pointsOfInterest.Any(point => point == null || string.IsNullOrWhiteSpace(point.id) ||
            point.cellIndex < 0 || point.cellIndex >= map.cells.Length) ||
            map.pointsOfInterest.GroupBy(point => point.id).Any(group => group.Count() != 1) ||
            requiredPointIds.Any(id => map.pointsOfInterest.All(point => point.id != id)))
            throw new InvalidDataException("世界地图探索地点映射无效");

        WorldMapProgressState progress = state.worldMapProgress;
        if (progress?.revealedCellIndices == null || progress.mapSites == null)
            throw new InvalidDataException("世界地图进度数据缺失");
        if (progress.revealedCellIndices.Any(index => index < 0 || index >= map.cells.Length) ||
            progress.revealedCellIndices.Distinct().Count() != progress.revealedCellIndices.Count)
            throw new InvalidDataException("世界地图认知索引无效");
        if (progress.mapSites.Any(site => site == null || string.IsNullOrWhiteSpace(site.siteId) ||
            site.cellIndex < 0 || site.cellIndex >= map.cells.Length ||
            !Enum.IsDefined(typeof(MapSiteType), site.siteType)) ||
            progress.mapSites.GroupBy(site => site.siteId).Any(group => group.Count() != 1))
            throw new InvalidDataException("世界地图地点数据无效");

        FoundingState founding = state.sect?.founding;
        if (founding == null || !founding.initialized)
            throw new InvalidDataException("存档缺少有效的立宗状态");
        if (!Enum.IsDefined(typeof(FoundingStage), founding.stage))
            throw new InvalidDataException("立宗阶段无效");
        if (founding.candidates == null || founding.candidates.Count < 3 ||
            founding.candidates.Any(candidate => candidate == null ||
                string.IsNullOrWhiteSpace(candidate.candidateId)) ||
            founding.candidates.GroupBy(candidate => candidate.candidateId)
                .Any(group => group.Count() != 1) ||
            founding.selectedFounderIds == null)
            throw new InvalidDataException("开局候选弟子数据无效");

        bool foundersSelected = founding.stage == FoundingStage.TechniqueSelection ||
                                founding.stage == FoundingStage.SectConfirmation ||
                                founding.stage == FoundingStage.Cave ||
                                founding.stage == FoundingStage.Completed;
        if (!foundersSelected)
        {
            if (founding.selectedFounderIds.Count != 0 ||
                !string.IsNullOrEmpty(founding.selectedTechniqueId) ||
                (state.characters?.Count ?? 0) != 0)
                throw new InvalidDataException("弟子选择前存在残留的立宗载荷");
        }
        else
        {
            if (founding.selectedFounderIds.Count != 3 ||
                founding.selectedFounderIds.Any(string.IsNullOrWhiteSpace) ||
                founding.selectedFounderIds.Distinct().Count() != 3 ||
                founding.selectedFounderIds.Any(id =>
                    founding.candidates.All(candidate => candidate.candidateId != id)) ||
                state.characters == null ||
                founding.selectedFounderIds.Any(id =>
                    state.characters.Count(character => character?.characterId == id) != 1))
                throw new InvalidDataException("已选弟子与角色快照不一致");
        }

        bool techniqueSelected = founding.stage == FoundingStage.SectConfirmation ||
                                 founding.stage == FoundingStage.Cave ||
                                 founding.stage == FoundingStage.Completed;
        if (techniqueSelected)
        {
            if (FoundingRules.GetTechnique(founding.selectedTechniqueId) == null)
                throw new InvalidDataException("存档缺少有效的初始功法");
        }
        else if (!string.IsNullOrEmpty(founding.selectedTechniqueId))
            throw new InvalidDataException("功法选择前存在残留的初始功法");

        int selected = founding.selectedWorldCellIndex;
        if (selected < -1 || selected >= map.cells.Length)
            throw new InvalidDataException("洞府选址索引无效");
        if (founding.stage == FoundingStage.WorldSelection && selected != -1)
            throw new InvalidDataException("选址阶段不应已有洞府落点");
        if (founding.initialized && founding.stage != FoundingStage.WorldSelection &&
            (selected < 0 || !map.cells[selected].isBuildable))
            throw new InvalidDataException("存档缺少有效的洞府选址");

        System.Collections.Generic.List<MapSiteData> sectBases = progress.mapSites
            .Where(site => site.siteType == MapSiteType.SectBase).ToList();
        bool hasEstablishedBase = founding.stage == FoundingStage.Cave ||
                                  founding.stage == FoundingStage.Completed;
        if (founding.completed != (founding.stage == FoundingStage.Completed))
            throw new InvalidDataException("立宗完成标记与阶段不一致");
        if (!hasEstablishedBase)
        {
            if (sectBases.Count != 0 ||
                !string.IsNullOrEmpty(state.sect.sectId) ||
                !string.IsNullOrEmpty(state.sect.sectName) ||
                state.sect.influenceRadius != 0 ||
                state.sect.foundedDay != 0)
                throw new InvalidDataException("立宗确认前不应存在宗门驻地");
            return;
        }

        MapSiteData sectBase = sectBases.Count == 1 ? sectBases[0] : null;
        if (sectBase == null ||
            sectBase.siteId != WorldMapProgressRules.PlayerSectBaseId ||
            sectBase.cellIndex != selected ||
            !sectBase.isRevealed || !sectBase.canInteract ||
            state.sect.sectId != "player_sect" ||
            string.IsNullOrWhiteSpace(state.sect.sectName) ||
            state.sect.sectName.Length < 2 || state.sect.sectName.Length > 12 ||
            state.sect.sectName.Any(char.IsControl) ||
            state.sect.sectName != sectBase.siteName ||
            state.sect.influenceRadius != 2 ||
            state.sect.foundedDay < 0)
            throw new InvalidDataException("宗门驻地与宗门数据不一致");
    }

    private static ActiveThreatState NormalizeThreatState(ActiveThreatState state)
    {
        if (state == null) state = new ActiveThreatState();
        state.threatId = string.IsNullOrWhiteSpace(state.threatId) ? null : state.threatId;
        if (state.status == ExternalThreatStatus.None)
        {
            state.scheduledDay = -1;
            state.activatedDay = -1;
            state.nextRaidDay = -1;
            state.discoveryNotificationEnqueued = false;
        }
        state.intelligence = Mathf.Clamp(state.intelligence, 0, 100);
        state.raidCount = Mathf.Max(0, state.raidCount);
        state.selectedCharacterIds = (state.selectedCharacterIds ?? new System.Collections.Generic.List<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct()
            .ToList();
        if (state.resolution != null)
        {
            state.resolution.participantIds = (state.resolution.participantIds ?? new System.Collections.Generic.List<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct()
                .ToList();
        }
        return state;
    }

    private static System.Collections.Generic.Dictionary<string, string> CleanParticipantIds(
        System.Collections.Generic.Dictionary<string, string> participantIds)
    {
        if (participantIds == null) return new System.Collections.Generic.Dictionary<string, string>();
        return participantIds
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.First().Value);
    }
}
