using System;
using System.Collections;
using System.IO;
using System.Linq;
using Cultivation4X.WorldMap;
using Newtonsoft.Json;
using UnityEngine;

internal sealed class SaveVersionMismatchException : Exception
{
    public SaveVersionMismatchException(string message) : base(message)
    {
    }
}

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }
    private bool lastLoadFailedWithVersionMismatch;
    public bool IsInitializationComplete { get; private set; }
    public bool LoadedExistingSave { get; private set; }
    public bool InitializationFailed { get; private set; }
    public bool HasGameSession { get; private set; }
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
        if (MapTestBootstrap.IsTestScene)
        {
            IsInitializationComplete = true;
            HasGameSession = false;
            yield break;
        }
        lastLoadFailedWithVersionMismatch = false;
        if (File.Exists(SavePath))
        {
            if (Load())
            {
                LoadedExistingSave = true;
                InitializationFailed = false;
                HasGameSession = true;
            }
            else if (lastLoadFailedWithVersionMismatch)
            {
                Debug.LogWarning("检测到旧版本存档，将自动舍弃并创建新档");
                LoadedExistingSave = false;
                InitializationFailed = !InitializeNewGame();
            }
            else
            {
                LoadedExistingSave = false;
                InitializationFailed = true;
                HasGameSession = false;
            }
        }
        else
        {
            // 无存档停在主菜单，等待 MainMenuPanel 调用 StartNewGame。
            LoadedExistingSave = false;
            InitializationFailed = false;
            HasGameSession = false;
        }
        IsInitializationComplete = true;
        OnInitializationCompleted?.Invoke(LoadedExistingSave);
    }

    /// <summary>主菜单开始新游戏：创建新档并让 GameFlowState 进入角色创建。</summary>
    public bool StartNewGame()
    {
        if (!IsInitializationComplete || InitializationFailed) return false;
        LoadedExistingSave = false;
        InitializationFailed = !InitializeNewGame();
        OnInitializationCompleted?.Invoke(LoadedExistingSave);
        return !InitializationFailed;
    }

    private bool InitializeNewGame()
    {
        try
        {
            int seed = unchecked(Environment.TickCount ^ DateTime.UtcNow.Millisecond);
            PlayerManager.Instance?.InitializeNewFoundingGame(seed);
            NPCManager.Instance?.ClearCharacters();
            if (WarehouseManager.Instance != null) WarehouseManager.Instance.warehouseData = new WarehouseData();
            MissionManager.Instance?.RestoreDailyCandidates(TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay,
                new System.Collections.Generic.List<string>());
            Save();
            HasGameSession = true;
            return true;
        }
        catch (Exception exception)
        {
            HasGameSession = false;
            Debug.LogError($"初始化新存档失败: {exception}");
            return false;
        }
    }

    public GameState CaptureState()
    {
        WorldMapContentRules.EnsureCandidates(WorldMapSession.Current, WorldMapSession.Progress);
        WorldMapInfluenceRules.EnsureCurrent(WorldMapSession.Current, WorldMapSession.Progress);
        WorldMapContentRules.RefreshHints(WorldMapSession.Current, WorldMapSession.Progress);
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
        if (InitializationFailed)
        {
            Debug.LogWarning("存档初始化失败，已阻止覆盖存档");
            return;
        }
        try
        {
            string json = JsonConvert.SerializeObject(CaptureState(), Formatting.Indented);
            string temporaryPath = SavePath + ".tmp";
            string backupPath = SavePath + ".bak";
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(backupPath)) File.Delete(backupPath);
            if (File.Exists(SavePath)) File.Move(SavePath, backupPath);
            File.Move(temporaryPath, SavePath);
            if (File.Exists(backupPath)) File.Delete(backupPath);
        }
        catch (Exception exception)
        {
            Debug.LogError($"保存失败: {exception.Message}");
            // 正式档被替换失败时，尝试从备份恢复，避免丢失旧档。
            try
            {
                if (!File.Exists(SavePath) && File.Exists(SavePath + ".bak"))
                    File.Move(SavePath + ".bak", SavePath);
            }
            catch (Exception restoreException)
            {
                Debug.LogError($"恢复旧档失败: {restoreException.Message}");
            }
        }
    }

    public bool Load()
    {
        if (!File.Exists(SavePath)) return false;
        try
        {
            GameState state = DeserializeCurrentVersion(File.ReadAllText(SavePath));

            NormalizeCurrentVersionCollections(state);
            PrepareInfluenceStateForValidation(state);
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
        catch (SaveVersionMismatchException exception)
        {
            lastLoadFailedWithVersionMismatch = true;
            Debug.LogWarning($"旧版本存档不兼容，将自动舍弃并创建新档: {exception.Message}");
            return false;
        }
        catch (Exception exception)
        {
            lastLoadFailedWithVersionMismatch = false;
            Debug.LogError($"读取存档失败: {exception}");
            return false;
        }
    }

    public void AutoSave() => Save();
    public string GetSavePath() => SavePath;

    private static GameState DeserializeCurrentVersion(string json)
    {
        GameState state = JsonConvert.DeserializeObject<GameState>(json);
        int version = state?.version ?? -1;
        if (version > SaveDataVersion.Current)
            throw new InvalidDataException("存档版本高于当前游戏版本，拒绝读取");
        if (version < SaveDataVersion.Current)
            throw new SaveVersionMismatchException("存档版本低于当前游戏版本");
        return state;
    }

    public static void MigrateState(GameState state)
    {
        int sourceVersion = state.version;
        state.sect = state.sect ?? new PlayerData();
        state.worldMapProgress = state.worldMapProgress ?? new WorldMapProgressState();
        state.worldMapProgress.revealedCellIndices =
            state.worldMapProgress.revealedCellIndices ?? new System.Collections.Generic.List<int>();
        state.worldMapProgress.exploredCellIndices =
            state.worldMapProgress.exploredCellIndices ?? new System.Collections.Generic.List<int>();
        state.worldMapProgress.mapSites =
            state.worldMapProgress.mapSites ?? new System.Collections.Generic.List<MapSiteData>();
        state.worldMapProgress.influenceSources =
            state.worldMapProgress.influenceSources ?? new System.Collections.Generic.List<InfluenceSourceData>();
        state.worldMapProgress.cellInfluences =
            state.worldMapProgress.cellInfluences ?? new System.Collections.Generic.List<CellInfluenceState>();
        int minimumFacilityLevel = sourceVersion < 4 ? 1 : 0;
        state.sect.missionHallLevel = Mathf.Clamp(state.sect.missionHallLevel, minimumFacilityLevel, FacilityRules.MaxLevel);
        state.sect.trainingRoomLevel = Mathf.Clamp(state.sect.trainingRoomLevel, minimumFacilityLevel, FacilityRules.MaxLevel);
        state.sect.warehouseLevel = Mathf.Clamp(state.sect.warehouseLevel, minimumFacilityLevel, FacilityRules.MaxLevel);
        state.sect.secretRealmLevel = Mathf.Clamp(state.sect.secretRealmLevel, minimumFacilityLevel, FacilityRules.MaxLevel);
        state.sect.alchemyRoomLevel = Mathf.Clamp(state.sect.alchemyRoomLevel, minimumFacilityLevel, FacilityRules.MaxLevel);
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

    private static void NormalizeCurrentVersionCollections(GameState state)
    {
        if (state == null) return;
        if (state.worldMap != null)
        {
            state.worldMap.regions = state.worldMap.regions ?? new System.Collections.Generic.List<MapRegionData>();
            state.worldMap.locations = state.worldMap.locations ??
                new System.Collections.Generic.Dictionary<string, WorldLocation>();
            foreach (WorldLocation location in state.worldMap.locations.Values)
            {
                location.availableActions = location.availableActions ??
                    new System.Collections.Generic.List<LocationAction>();
                location.availableMissionIds = location.availableMissionIds ??
                    new System.Collections.Generic.List<string>();
            }
            foreach (MapRegionData region in state.worldMap.regions.Where(item => item != null))
                region.cellIndices = region.cellIndices ?? new System.Collections.Generic.List<int>();
        }
        if (state.worldMapProgress != null)
        {
            state.worldMapProgress.revealedCellIndices = state.worldMapProgress.revealedCellIndices ??
                new System.Collections.Generic.List<int>();
            state.worldMapProgress.exploredCellIndices = state.worldMapProgress.exploredCellIndices ??
                new System.Collections.Generic.List<int>();
            state.worldMapProgress.mapSites = state.worldMapProgress.mapSites ??
                new System.Collections.Generic.List<MapSiteData>();
            state.worldMapProgress.influenceSources = state.worldMapProgress.influenceSources ??
                new System.Collections.Generic.List<InfluenceSourceData>();
            state.worldMapProgress.cellInfluences = state.worldMapProgress.cellInfluences ??
                new System.Collections.Generic.List<CellInfluenceState>();
            foreach (MapSiteData site in state.worldMapProgress.mapSites.Where(item => item != null))
            {
                site.tags = site.tags ?? new System.Collections.Generic.List<string>();
                site.availableActionIds = site.availableActionIds ?? new System.Collections.Generic.List<string>();
            }
            WorldLocationRules.SynchronizeFromMapSites(state.worldMap, state.worldMapProgress);
        }
        state.activeMissions = state.activeMissions ?? new System.Collections.Generic.List<MissionSaveData>();
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
        if (map.generationVersion < WorldMapGenerationVersion.Current)
            throw new SaveVersionMismatchException("世界地图生成版本低于当前游戏版本");
        if (map.generationVersion != WorldMapGenerationVersion.Current ||
            map.generationSettings == null)
            throw new InvalidDataException("世界地图生成版本与当前游戏不兼容");
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

        ValidateWorldLocations(map, state.worldMapProgress);
        ValidateStaticMapInputs(map);
        ValidateMapRegions(map);

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
        if (progress?.revealedCellIndices == null || progress.exploredCellIndices == null || progress.mapSites == null ||
            progress.influenceSources == null || progress.cellInfluences == null)
            throw new InvalidDataException("世界地图进度数据缺失");
        if (progress.revealedCellIndices.Any(index => index < 0 || index >= map.cells.Length) ||
            progress.revealedCellIndices.Distinct().Count() != progress.revealedCellIndices.Count)
            throw new InvalidDataException("世界地图认知索引无效");
        if (progress.exploredCellIndices.Any(index => index < 0 || index >= map.cells.Length) ||
            progress.exploredCellIndices.Distinct().Count() != progress.exploredCellIndices.Count ||
            progress.exploredCellIndices.Any(index => !progress.revealedCellIndices.Contains(index)))
            throw new InvalidDataException("世界地图探索索引无效");
        if (progress.mapSites.Any(site => site == null || string.IsNullOrWhiteSpace(site.siteId) ||
            site.cellIndex < 0 || site.cellIndex >= map.cells.Length ||
            !Enum.IsDefined(typeof(MapSiteType), site.siteType) ||
            !Enum.IsDefined(typeof(MapContentRevealState), site.revealState) ||
            !Enum.IsDefined(typeof(MapSiteState), site.siteState) ||
            site.tags == null || site.availableActionIds == null ||
            site.tags.Any(string.IsNullOrWhiteSpace) || site.availableActionIds.Any(string.IsNullOrWhiteSpace) ||
            site.tags.Distinct(StringComparer.Ordinal).Count() != site.tags.Count ||
            site.availableActionIds.Distinct(StringComparer.Ordinal).Count() != site.availableActionIds.Count ||
            site.isRevealed != (site.siteType == MapSiteType.SectBase || site.revealState == MapContentRevealState.Discovered)) ||
            progress.mapSites.GroupBy(site => site.siteId).Any(group => group.Count() != 1) ||
            progress.mapSites.GroupBy(site => site.cellIndex).Any(group => group.Count() != 1))
            throw new InvalidDataException("世界地图地点数据无效");
        MapSiteType[] candidateTypes = { MapSiteType.Village, MapSiteType.SpiritSpring, MapSiteType.SpiritMine,
            MapSiteType.CaveResidence, MapSiteType.BeastLair, MapSiteType.Ruin };
        if (candidateTypes.Any(type => progress.mapSites.Count(site => site.siteType == type) != 1) ||
            progress.mapSites.Where(site => site.siteType != MapSiteType.SectBase)
                .Any(site => IsInvalidContentSite(map, state.currentDay, site)))
            throw new InvalidDataException("世界地图候选内容无效");
        ValidateMapMissionContexts(state, map, progress);
        ValidateInfluenceCache(map, progress);

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
                                founding.stage == FoundingStage.WorldSelection ||
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
                                 founding.stage == FoundingStage.WorldSelection ||
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
        bool siteChosen = founding.stage == FoundingStage.Cave ||
                          founding.stage == FoundingStage.Completed;
        if (!siteChosen && selected != -1)
            throw new InvalidDataException("选址完成前不应已有洞府落点");
        if (siteChosen && (selected < 0 || !map.cells[selected].isBuildable))
            throw new InvalidDataException("存档缺少有效的洞府选址");

        bool identityConfirmed = founding.stage == FoundingStage.WorldSelection ||
                                 founding.stage == FoundingStage.Cave ||
                                 founding.stage == FoundingStage.Completed;
        if (identityConfirmed)
        {
            if (string.IsNullOrWhiteSpace(founding.pendingSectName) ||
                founding.pendingSectName.Length < 2 ||
                founding.pendingSectName.Length > 12 ||
                founding.pendingSectName.Any(char.IsControl))
                throw new InvalidDataException("存档缺少有效的待确认宗门名称");
        }
        else if (!string.IsNullOrEmpty(founding.pendingSectName))
        {
            throw new InvalidDataException("确认宗门名称前存在残留的宗门名称");
        }

        System.Collections.Generic.List<MapSiteData> sectBases = progress.mapSites
            .Where(site => site.siteType == MapSiteType.SectBase).ToList();
        bool hasEstablishedBase = founding.stage == FoundingStage.Cave ||
                                  founding.stage == FoundingStage.Completed;
        if (founding.completed != (founding.stage == FoundingStage.Completed))
            throw new InvalidDataException("立宗完成标记与阶段不一致");
        if (!hasEstablishedBase)
        {
            if (sectBases.Count != 0 ||
                progress.influenceSources.Count != 0 ||
                progress.cellInfluences.Count != 0 ||
                !string.IsNullOrEmpty(state.sect.sectId) ||
                !string.IsNullOrEmpty(state.sect.sectName) ||
                state.sect.influenceRadius != 0 ||
                state.sect.foundedDay != 0)
                throw new InvalidDataException("立宗确认前不应存在宗门驻地");
            return;
        }

        MapSiteData sectBase = sectBases.Count == 1 ? sectBases[0] : null;
        InfluenceSourceData sectBaseSource = progress.influenceSources.Count == 1
            ? progress.influenceSources[0]
            : null;
        if (sectBase == null ||
            sectBase.siteId != WorldMapProgressRules.PlayerSectBaseId ||
            sectBase.cellIndex != selected ||
            !sectBase.isRevealed || !sectBase.canInteract ||
            sectBase.revealState != MapContentRevealState.Discovered ||
            sectBase.siteState != MapSiteState.Developed ||
            sectBase.ownerSectId != "player_sect" ||
            state.sect.sectId != "player_sect" ||
            string.IsNullOrWhiteSpace(state.sect.sectName) ||
            state.sect.sectName.Length < 2 || state.sect.sectName.Length > 12 ||
            state.sect.sectName.Any(char.IsControl) ||
            state.sect.sectName != founding.pendingSectName ||
            state.sect.sectName != sectBase.siteName ||
            state.sect.influenceRadius != 2 ||
            state.sect.foundedDay < 0 ||
            sectBaseSource == null ||
            sectBaseSource.sourceId != sectBase.siteId ||
            sectBaseSource.sourceType != InfluenceSourceType.SectBase ||
            sectBaseSource.cellIndex != sectBase.cellIndex ||
            sectBaseSource.controllerSectId != state.sect.sectId ||
            sectBaseSource.baseStrength != WorldMapInfluenceRules.SectBaseStrength ||
            sectBaseSource.radius != WorldMapInfluenceRules.SectBaseRadius ||
            !sectBaseSource.isActive)
            throw new InvalidDataException("宗门驻地与宗门数据不一致");
    }

    private static void ValidateWorldLocations(WorldMap map, WorldMapProgressState progress = null)
    {
        if (map.locations == null)
            throw new InvalidDataException("世界地点表缺失");
        foreach (System.Collections.Generic.KeyValuePair<string, WorldLocation> pair in map.locations)
        {
            WorldLocation location = pair.Value;
            if (location == null || string.IsNullOrWhiteSpace(location.id) ||
                !string.Equals(pair.Key, location.id, StringComparison.Ordinal) ||
                !Enum.IsDefined(typeof(LocationType), location.type) ||
                !Enum.IsDefined(typeof(LocationState), location.state) ||
                string.IsNullOrWhiteSpace(location.name) ||
                location.availableActions == null ||
                location.availableActions.Any(action => action == null ||
                    string.IsNullOrWhiteSpace(action.id) ||
                    string.IsNullOrWhiteSpace(action.displayName)) ||
                location.availableMissionIds == null ||
                location.availableMissionIds.Any(string.IsNullOrWhiteSpace) ||
                location.availableMissionIds.Distinct(StringComparer.Ordinal).Count() !=
                    location.availableMissionIds.Count ||
                location.position.x < 0 || location.position.x >= map.width ||
                location.position.y < 0 || location.position.y >= map.height ||
                (!string.IsNullOrEmpty(location.sourceMapSiteId) &&
                 (progress?.mapSites == null ||
                  progress.mapSites.All(site => site == null || site.siteId != location.sourceMapSiteId))))
                throw new InvalidDataException("世界地点实体无效");
        }

        foreach (WorldCell cell in map.cells)
        {
            if (string.IsNullOrEmpty(cell.locationId)) continue;
            WorldLocation location = map.GetLocation(cell.locationId);
            if (location == null || map.GetIndex(new HexCoord(location.position.x, location.position.y)) != cell.index)
                throw new InvalidDataException("格子地点引用与地点位置不一致");
        }
    }

    private static void ValidateStaticMapInputs(WorldMap map)
    {
        if (map.rivers == null || map.spiritVeins == null || map.pointsOfInterest == null)
            throw new InvalidDataException("世界地图静态图层数据缺失");
        foreach (WorldCell cell in map.cells)
        {
            if (!Enum.IsDefined(typeof(LandformType), cell.landform) ||
                !Enum.IsDefined(typeof(BiomeType), cell.biome) || cell.elementalAura == null ||
                !FiniteUnit(cell.height) || !FiniteUnit(cell.temperature) || !FiniteUnit(cell.moisture) ||
                !FiniteUnit(cell.baseAura) || !FiniteUnit(cell.totalAura) ||
                !FiniteNonNegative(cell.elementalAura.metal) || !FiniteNonNegative(cell.elementalAura.wood) ||
                !FiniteNonNegative(cell.elementalAura.water) || !FiniteNonNegative(cell.elementalAura.fire) ||
                !FiniteNonNegative(cell.elementalAura.earth) ||
                Math.Abs(cell.totalAura - cell.elementalAura.Total) > 0.0001f)
                throw new InvalidDataException($"世界地图格子 {cell.index} 的静态环境数据无效");
        }
        if (map.rivers.Any(segment => segment == null ||
                segment.fromCellIndex < 0 || segment.fromCellIndex >= map.cells.Length ||
                segment.toCellIndex < 0 || segment.toCellIndex >= map.cells.Length ||
                segment.edgeDirection < 0 || segment.edgeDirection >= 6 ||
                map.GetDirection(segment.fromCellIndex, segment.toCellIndex) != segment.edgeDirection ||
                !FiniteNonNegative(segment.flow)))
            throw new InvalidDataException("世界地图河流数据无效");
        if (map.spiritVeins.Any(vein => vein == null || string.IsNullOrWhiteSpace(vein.id) ||
                !Enum.IsDefined(typeof(SpiritVeinSize), vein.size) ||
                !Enum.IsDefined(typeof(SpiritElement), vein.primaryElement) ||
                vein.pathCellIndices == null || vein.pathCellIndices.Count == 0 ||
                vein.pathCellIndices.Any(index => index < 0 || index >= map.cells.Length) ||
                vein.pathCellIndices.Zip(vein.pathCellIndices.Skip(1), (left, right) => map.GetDirection(left, right))
                    .Any(direction => direction < 0) || !FiniteNonNegative(vein.strength) || vein.influenceRadius < 0) ||
            map.spiritVeins.GroupBy(vein => vein.id, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidDataException("世界地图灵脉数据无效");
    }

    private static bool FiniteUnit(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;
    private static bool FiniteNonNegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;

    private static void ValidateMapRegions(WorldMap map)
    {
        if (map.regions == null || map.regions.Count == 0)
            throw new InvalidDataException("世界地图区域数据缺失");
        if (map.regions.Any(region => region == null || string.IsNullOrWhiteSpace(region.regionId) ||
                string.IsNullOrWhiteSpace(region.regionName) || region.regionName.Any(char.IsDigit) || region.regionName.Any(char.IsControl) ||
                region.cellIndices == null || region.cellIndices.Count == 0 ||
                !Enum.IsDefined(typeof(MapRegionType), region.regionType) ||
                !Enum.IsDefined(typeof(LandformType), region.dominantLandform) ||
                !Enum.IsDefined(typeof(BiomeType), region.dominantBiome) ||
                !Enum.IsDefined(typeof(SpiritElement), region.hiddenElementBias) ||
                !Enum.IsDefined(typeof(MapRegionTrend), region.auraTrend) ||
                !Enum.IsDefined(typeof(MapRegionTrend), region.dangerTrend) ||
                float.IsNaN(region.averageAura) || float.IsInfinity(region.averageAura) || region.averageAura < 0f || region.averageAura > 1f ||
                float.IsNaN(region.averageDanger) || float.IsInfinity(region.averageDanger) || region.averageDanger < 0f ||
                region.averageDanger > (float)WorldDangerLevel.High || region.displayPriority < 0 || region.displayPriority > 100 ||
                region.centerCellIndex < 0 || region.centerCellIndex >= map.cells.Length ||
                !region.cellIndices.Contains(region.centerCellIndex) ||
                region.cellIndices.Any(index => index < 0 || index >= map.cells.Length) ||
                region.cellIndices.Distinct().Count() != region.cellIndices.Count ||
                !region.cellIndices.SequenceEqual(region.cellIndices.OrderBy(index => index)) ||
                !IsConnectedRegion(map, region.cellIndices)) ||
            map.regions.GroupBy(region => region.regionId, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            map.regions.GroupBy(region => region.regionName, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidDataException("世界地图区域数据无效");

        int[] assigned = new int[map.cells.Length];
        foreach (MapRegionData region in map.regions)
            foreach (int index in region.cellIndices)
            {
                assigned[index]++;
                WorldCell cell = map.cells[index];
                if (cell.regionId != region.regionId || !Enum.IsDefined(typeof(MapInternalPositionTag), cell.internalPositionTag))
                    throw new InvalidDataException("世界地图格子区域引用无效");
            }
        if (assigned.Any(count => count != 1))
            throw new InvalidDataException("世界地图区域未唯一覆盖全部格子");

        MapRegionBuildResult expected = WorldMapRegionRules.Build(map);
        if (expected.regions.Count != map.regions.Count || expected.regionIds.Length != map.cells.Length)
            throw new InvalidDataException("世界地图区域与确定性生成结果不一致");
        for (int index = 0; index < map.cells.Length; index++)
            if (expected.regionIds[index] != map.cells[index].regionId ||
                expected.internalPositionTags[index] != map.cells[index].internalPositionTag)
                throw new InvalidDataException("世界地图区域格子快照遭到篡改");
        for (int index = 0; index < expected.regions.Count; index++)
            if (!RegionsEqual(expected.regions[index], map.regions[index]))
                throw new InvalidDataException("世界地图区域快照遭到篡改");
    }

    private static bool IsConnectedRegion(WorldMap map, System.Collections.Generic.List<int> indices)
    {
        var allowed = new System.Collections.Generic.HashSet<int>(indices);
        var visited = new System.Collections.Generic.HashSet<int>();
        var queue = new System.Collections.Generic.Queue<int>();
        queue.Enqueue(indices[0]); visited.Add(indices[0]);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            foreach (int neighbor in map.GetNeighborIndices(current))
                if (allowed.Contains(neighbor) && visited.Add(neighbor)) queue.Enqueue(neighbor);
        }
        return visited.Count == allowed.Count;
    }

    private static bool RegionsEqual(MapRegionData left, MapRegionData right)
    {
        return left.regionId == right.regionId && left.regionName == right.regionName &&
            left.regionType == right.regionType && left.centerCellIndex == right.centerCellIndex &&
            left.dominantLandform == right.dominantLandform && left.dominantBiome == right.dominantBiome &&
            left.hiddenElementBias == right.hiddenElementBias && left.auraTrend == right.auraTrend &&
            left.dangerTrend == right.dangerTrend && Math.Abs(left.averageAura - right.averageAura) < 0.00001f &&
            Math.Abs(left.averageDanger - right.averageDanger) < 0.00001f &&
            left.displayPriority == right.displayPriority &&
            left.cellIndices.SequenceEqual(right.cellIndices);
    }

    private static bool IsInvalidContentSite(WorldMap map, int currentDay, MapSiteData site)
    {
        if (site.siteId != WorldMapContentRules.CandidateId(site.siteType) ||
            site.siteName != WorldMapContentRules.SiteTypeLabel(site.siteType) ||
            !site.tags.SequenceEqual(WorldMapContentRules.CandidateTags(site.siteType), StringComparer.Ordinal)) return true;
        if (site.revealState != MapContentRevealState.Discovered &&
            (site.siteState != MapSiteState.None || site.discoveredDay != -1 || site.lastUpdatedDay != -1 ||
             !string.IsNullOrEmpty(site.ownerSectId))) return true;
        if (site.revealState == MapContentRevealState.Discovered &&
            (site.discoveredDay < 0 || site.discoveredDay > currentDay ||
             site.lastUpdatedDay < site.discoveredDay || site.lastUpdatedDay > currentDay)) return true;
        bool validState = site.siteState == MapSiteState.None ||
            (site.siteType == MapSiteType.SpiritSpring &&
             (site.siteState == MapSiteState.Investigated || site.siteState == MapSiteState.Developed)) ||
            ((site.siteType == MapSiteType.Village || site.siteType == MapSiteType.SpiritMine ||
              site.siteType == MapSiteType.CaveResidence) && site.siteState == MapSiteState.Developed) ||
            ((site.siteType == MapSiteType.BeastLair || site.siteType == MapSiteType.Ruin) &&
             site.siteState == MapSiteState.Investigated);
        if (!validState) return true;
        if ((site.siteState == MapSiteState.None || site.siteState == MapSiteState.Investigated) &&
            !string.IsNullOrEmpty(site.ownerSectId)) return true;
        if (site.siteState == MapSiteState.Developed && site.ownerSectId != "player_sect") return true;
        string expectedAction = site.revealState == MapContentRevealState.Discovered && site.siteState == MapSiteState.None
            ? WorldMapContentRules.ActionIdFor(WorldMapContentRules.ActionForSite(site)) :
            site.revealState == MapContentRevealState.Discovered && site.siteType == MapSiteType.SpiritSpring &&
              site.siteState == MapSiteState.Investigated ? WorldMapContentRules.DevelopActionId : null;
        string[] expected = string.IsNullOrEmpty(expectedAction) ? Array.Empty<string>() : new[] { expectedAction };
        if (!(site.availableActionIds ?? new System.Collections.Generic.List<string>()).SequenceEqual(expected, StringComparer.Ordinal) ||
            site.canInteract != (expected.Length > 0)) return true;
        return false;
    }

    private static void ValidateMapMissionContexts(GameState state, WorldMap map, WorldMapProgressState progress)
    {
        System.Collections.Generic.List<MissionSaveData> missions = state.activeMissions ??
            new System.Collections.Generic.List<MissionSaveData>();
        string[] mapMissionIds = { WorldMapContentRules.ExploreMissionId,
            WorldMapContentRules.InvestigateSpiritSpringMissionId,
            WorldMapContentRules.DevelopSpiritSpringMissionId,
            WorldMapContentRules.EstablishVillageRelationMissionId,
            WorldMapContentRules.DevelopSpiritMineMissionId,
            WorldMapContentRules.BuildCaveResidenceOutpostMissionId,
            WorldMapContentRules.ClearBeastLairMissionId,
            WorldMapContentRules.InvestigateRuinMissionId };
        if (missions.Any(mission => mission == null ||
                (mapMissionIds.Contains(mission.missionId) != (mission.mapContext != null))))
            throw new InvalidDataException("地图任务与上下文缺失或错配");
        foreach (MissionSaveData mission in missions.Where(item => item.mapContext != null))
        {
            MapMissionContext context = mission.mapContext;
            if (!Enum.IsDefined(typeof(MapActionType), context.actionType) || context.actionType == MapActionType.None ||
                context.targetCellIndex < 0 || context.targetCellIndex >= map.cells.Length ||
                !Enum.IsDefined(typeof(MissionState), mission.state) ||
                (mission.state != MissionState.Active && mission.state != MissionState.AwaitingReward) ||
                string.IsNullOrWhiteSpace(mission.assignedCharacterId) ||
                state.characters == null || state.characters.Count(character => character != null &&
                    character.characterId == mission.assignedCharacterId && character.IsAlive) != 1 ||
                mission.reward == null || !mission.hasCapabilitySnapshot || mission.capabilityScore < 0 ||
                !Enum.IsDefined(typeof(MissionResultTier), mission.resultTier) ||
                mission.remainingDays < 0 || mission.elapsedDays < 0 || mission.currentNodeIndex != 0 ||
                (mission.state == MissionState.AwaitingReward && mission.resultTier == MissionResultTier.Insufficient))
                throw new InvalidDataException("地图任务上下文无效");
            if ((context.actionType == MapActionType.Explore && !string.IsNullOrEmpty(context.targetSiteId)) ||
                (context.actionType != MapActionType.Explore && string.IsNullOrWhiteSpace(context.targetSiteId)))
                throw new InvalidDataException("地图任务目标引用无效");
            string expectedMissionId = WorldMapContentRules.MissionIdFor(context.actionType);
            if (context.actionType != MapActionType.Explore)
            {
                MapSiteData targetSite = WorldMapContentRules.FindSite(progress, context.targetSiteId);
                if (targetSite == null || targetSite.siteType != WorldMapContentRules.SiteTypeForAction(context.actionType))
                    throw new InvalidDataException("地图任务目标类型无效");
            }
            if (mission.missionId != expectedMissionId ||
                !WorldMapContentRules.CanStartAction(map, progress, context, out _))
                throw new InvalidDataException("地图任务目标与行动不一致");
            Reward expectedReward = WorldMapContentRules.CreateReward(map, context);
            if (mission.state == MissionState.AwaitingReward && mission.resultTier == MissionResultTier.Excellent)
            {
                expectedReward.Gold += Mathf.FloorToInt(expectedReward.Gold * 0.5f);
                expectedReward.Exp += Mathf.FloorToInt(expectedReward.Exp * 0.5f);
            }
            if (!RewardsEqual(expectedReward, mission.reward))
                throw new InvalidDataException("地图任务奖励快照无效");
        }
        if (missions.Where(item => item?.mapContext != null)
            .GroupBy(item => $"{item.mapContext.actionType}:{item.mapContext.targetCellIndex}")
            .Any(group => group.Count() != 1))
            throw new InvalidDataException("地图任务重复");
    }

    private static bool RewardsEqual(Reward left, Reward right)
    {
        if (left == null || right == null || left.Gold != right.Gold || left.Exp != right.Exp ||
            left.Items == null || right.Items == null || left.Items.Count != right.Items.Count) return false;
        for (int index = 0; index < left.Items.Count; index++)
        {
            ItemReward expected = left.Items[index];
            ItemReward actual = right.Items[index];
            if (expected == null || actual == null || expected.itemId != actual.itemId || expected.count != actual.count)
                return false;
        }
        return true;
    }

    private static void PrepareInfluenceStateForValidation(GameState state)
    {
        if (state == null) return;
        WorldMapProgressState progress = state.worldMapProgress;
        if (progress == null) return;
        if (progress.influenceSources == null) return;
        progress.cellInfluences = progress.cellInfluences ??
            new System.Collections.Generic.List<CellInfluenceState>();
        bool sourcesSafe = state.worldMap?.cells != null &&
                           progress.influenceSources.All(source =>
                               WorldMapInfluenceRules.IsUsableSource(state.worldMap, source) &&
                               source.baseStrength == WorldMapInfluenceRules.SectBaseStrength &&
                               source.radius == WorldMapInfluenceRules.SectBaseRadius) &&
                           progress.influenceSources.Select(source => source.sourceId)
                               .Distinct(StringComparer.Ordinal).Count() == progress.influenceSources.Count;
        if (sourcesSafe && (progress.isInfluenceDirty ||
                            (progress.influenceSources.Count > 0 && progress.cellInfluences.Count == 0)))
            WorldMapInfluenceRules.Recalculate(state.worldMap, progress);
    }

    private static void ValidateInfluenceCache(WorldMap map, WorldMapProgressState progress)
    {
        if (progress.isInfluenceDirty)
            throw new InvalidDataException("世界地图影响力缓存未完成计算");
        if (progress.influenceSources.Any(source =>
                !WorldMapInfluenceRules.IsUsableSource(map, source) ||
                !Enum.IsDefined(typeof(InfluenceSourceType), source.sourceType)) ||
            progress.influenceSources.GroupBy(source => source.sourceId).Any(group => group.Count() != 1))
            throw new InvalidDataException("世界地图影响力来源无效");
        if (progress.cellInfluences.Any(cell => IsInvalidInfluenceCell(map, progress, cell)) ||
            progress.cellInfluences.GroupBy(cell => cell.cellIndex).Any(group => group.Count() != 1) ||
            !progress.cellInfluences.Select(cell => cell.cellIndex)
                .SequenceEqual(progress.cellInfluences.Select(cell => cell.cellIndex).OrderBy(index => index)))
            throw new InvalidDataException("世界地图影响力格缓存无效");

        WorldMapProgressState expected = new WorldMapProgressState
        {
            influenceSources = progress.influenceSources,
            isInfluenceDirty = true
        };
        WorldMapInfluenceRules.Recalculate(map, expected);
        if (expected.cellInfluences.Count != progress.cellInfluences.Count)
            throw new InvalidDataException("世界地图影响力缓存与来源不一致");
        for (int index = 0; index < expected.cellInfluences.Count; index++)
        {
            CellInfluenceState left = expected.cellInfluences[index];
            CellInfluenceState right = progress.cellInfluences[index];
            if (left.cellIndex != right.cellIndex || left.value != right.value || left.level != right.level ||
                left.controllerSectId != right.controllerSectId ||
                !left.sourceIds.SequenceEqual(right.sourceIds, StringComparer.Ordinal))
                throw new InvalidDataException("世界地图影响力缓存与来源不一致");
        }
    }

    private static bool IsInvalidInfluenceCell(WorldMap map, WorldMapProgressState progress,
        CellInfluenceState cell)
    {
        if (cell == null || cell.cellIndex < 0 || cell.cellIndex >= map.cells.Length ||
            cell.value < 1 || cell.value > 100 ||
            !Enum.IsDefined(typeof(InfluenceLevel), cell.level) ||
            cell.level != WorldMapInfluenceRules.LevelForValue(cell.value) ||
            string.IsNullOrWhiteSpace(cell.controllerSectId) ||
            cell.sourceIds == null || cell.sourceIds.Count == 0 ||
            cell.sourceIds.Any(string.IsNullOrWhiteSpace) ||
            cell.sourceIds.Distinct(StringComparer.Ordinal).Count() != cell.sourceIds.Count ||
            !cell.sourceIds.SequenceEqual(cell.sourceIds.OrderBy(id => id, StringComparer.Ordinal)))
            return true;
        foreach (string sourceId in cell.sourceIds)
        {
            InfluenceSourceData source = progress.influenceSources.FirstOrDefault(item => item.sourceId == sourceId);
            if (source == null || source.controllerSectId != cell.controllerSectId) return true;
        }
        return false;
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
