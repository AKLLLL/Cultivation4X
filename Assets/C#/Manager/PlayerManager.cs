using System;
using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;
using UnityEngine;

/// <summary>
/// 玩家管理器
/// </summary>
public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance;

    /// <summary>
    /// 玩家数据
    /// </summary>
    public PlayerData playerData = new PlayerData();
    public event Action OnFoundingChanged;

    private void Awake()
    {
        Instance = this;
        if (playerData == null) playerData = new PlayerData();
    }

    public void InitializeNewFoundingGame(int seed)
    {
        WorldMap generatedMap = WorldGenerator.Generate(new MapGenerationSettings { seed = seed });
        WorldMapProgressState initialProgress = new WorldMapProgressState();
        WorldMapContentRules.EnsureCandidates(generatedMap, initialProgress);
        WorldMapSession.Set(generatedMap, initialProgress);
        playerData = new PlayerData
        {
            missionHallLevel = 0,
            warehouseLevel = 0,
            trainingRoomLevel = 0,
            secretRealmLevel = 0,
            alchemyRoomLevel = 0,
            protectionArrayLevel = 0,
            inheritanceChamberLevel = 0,
            forgeRoomLevel = 0,
            formationPlatformLevel = 0,
            founding = new FoundingState
            {
                initialized = true,
                sectCreated = false,
                completed = false,
                stage = FoundingStage.CandidateSelection,
                candidateSeed = seed,
                worldSeed = seed,
                selectedWorldCellIndex = -1,
                candidates = FoundingRules.GenerateCandidates(seed),
                selectedFounderIds = new List<string>(),
                village = new VillageState()
            }
        };
        OnFoundingChanged?.Invoke();
    }

    public bool ConfirmFounderSelection(IEnumerable<string> candidateIds, out string reason)
    {
        FoundingState state = playerData.founding;
        List<string> ids = (candidateIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (state == null || state.completed || state.stage != FoundingStage.CandidateSelection) { reason = "当前不能选择开局弟子"; return false; }
        if (ids.Count != 3) { reason = "必须选择三名弟子"; return false; }
        List<FounderCandidateData> candidates = state.candidates.Where(item => ids.Contains(item.candidateId)).ToList();
        if (candidates.Count != 3) { reason = "候选弟子数据无效"; return false; }
        if (NPCManager.Instance == null || !NPCManager.Instance.CreateFounders(candidates)) { reason = "弟子创建失败"; return false; }
        state.selectedFounderIds = ids;
        state.stage = FoundingStage.TechniqueSelection;
        reason = null;
        OnFoundingChanged?.Invoke();
        SaveManager.Instance?.AutoSave();
        return true;
    }

    public bool SelectFoundingTechnique(string techniqueId, out string reason)
    {
        FoundingState state = playerData.founding;
        if (state == null || state.stage != FoundingStage.TechniqueSelection) { reason = "当前不能选择传承"; return false; }
        if (FoundingRules.GetTechnique(techniqueId) == null) { reason = "传承配置不存在"; return false; }
        state.selectedTechniqueId = techniqueId;
        EnsureFoundingTechniqueGranted(state);
        state.stage = FoundingStage.SectConfirmation;
        reason = null;
        OnFoundingChanged?.Invoke();
        SaveManager.Instance?.AutoSave();
        return true;
    }

    public bool ConfirmSectFounding(string requestedName, out string reason)
    {
        FoundingState state = playerData?.founding;
        string sectName = NormalizeSectName(requestedName, out string nameReason);
        if (state == null || state.completed || state.stage != FoundingStage.SectConfirmation)
        { reason = "当前不能确认建立宗门"; return false; }
        if (nameReason != null) { reason = nameReason; return false; }
        if (state.selectedFounderIds == null || state.selectedFounderIds.Distinct().Count() != 3 ||
            state.candidates == null ||
            state.selectedFounderIds.Any(id => state.candidates.All(candidate => candidate?.candidateId != id)))
        { reason = "初始弟子数据无效"; return false; }
        if (NPCManager.Instance == null ||
            state.selectedFounderIds.Any(id => NPCManager.Instance.GetRuntime(id) == null))
        { reason = "初始弟子运行时数据缺失"; return false; }
        if (FoundingRules.GetTechnique(state.selectedTechniqueId) == null)
        { reason = "初始功法数据无效"; return false; }
        if (WorldMapProgressRules.GetSectBase(WorldMapSession.Progress) != null || !string.IsNullOrEmpty(playerData.sectId) ||
            (WorldMapSession.Progress?.influenceSources?.Count ?? 0) != 0 ||
            (WorldMapSession.Progress?.cellInfluences?.Count ?? 0) != 0)
        { reason = "宗门驻地已经建立"; return false; }

        // 只确认身份，不创建驻地；选址完成后由 ConfirmWorldSite 统一落址。
        state.pendingSectName = sectName;
        state.selectedWorldCellIndex = -1;
        state.stage = FoundingStage.WorldSelection;
        reason = null;
        OnFoundingChanged?.Invoke();
        SaveManager.Instance?.AutoSave();
        return true;
    }

    private static string NormalizeSectName(string requestedName, out string reason)
    {
        string sectName = requestedName?.Trim();
        if (string.IsNullOrEmpty(sectName) || sectName.Length < 2 || sectName.Length > 12 ||
            sectName.Any(char.IsControl))
        {
            reason = "宗门名称应为 2–12 个字符，且不能包含控制字符";
            return null;
        }
        reason = null;
        return sectName;
    }

    public void AddReputation(int amount)
    {
        playerData.reputation = Mathf.Max(0, playerData.reputation + amount);
    }

    public int GetFacilityLevel(FacilityType facility)
    {
        switch (facility)
        {
            case FacilityType.MissionHall: return playerData.missionHallLevel;
            case FacilityType.Warehouse: return playerData.warehouseLevel;
            case FacilityType.TrainingRoom: return playerData.trainingRoomLevel;
            case FacilityType.SecretRealm: return playerData.secretRealmLevel;
            case FacilityType.AlchemyRoom: return playerData.alchemyRoomLevel;
            case FacilityType.ProtectionArray: return playerData.protectionArrayLevel;
            case FacilityType.InheritanceChamber: return playerData.inheritanceChamberLevel;
            case FacilityType.ForgeRoom: return playerData.forgeRoomLevel;
            case FacilityType.FormationPlatform: return playerData.formationPlatformLevel;
            default: return 0;
        }
    }

    public bool CanUpgradeFacility(FacilityType facility, out string reason)
    {
        if (facility == FacilityType.MissionHall)
        {
            reason = "宗门事务是界面入口，不作为设施升级";
            return false;
        }
        int level = GetFacilityLevel(facility);
        if (level <= 0) { reason = "设施尚未修复或建成"; return false; }
        if (facility == FacilityType.ProtectionArray ||
            facility == FacilityType.InheritanceChamber || facility == FacilityType.ForgeRoom ||
            facility == FacilityType.FormationPlatform)
        { reason = "该设施当前不可升级"; return false; }
        if (level >= FacilityRules.MaxLevel) { reason = "设施已达到最高等级"; return false; }
        if (WarehouseManager.Instance == null) { reason = "仓库尚未初始化"; return false; }
        if (!WarehouseManager.Instance.HasItem(FacilityRules.SpiritStoneId, FacilityRules.UpgradeSpiritStoneCost(level)))
        { reason = "灵石不足"; return false; }
        if (WarehouseManager.Instance.GetItemCount(FacilityRules.BasicMaterialId) < FacilityRules.UpgradeMaterialCost(level))
        { reason = "基础材料不足"; return false; }
        reason = null;
        return true;
    }

    public FacilityUpgradeResult TryUpgradeFacility(FacilityType facility)
    {
        if (!CanUpgradeFacility(facility, out string reason))
            return new FacilityUpgradeResult { success = false, reason = reason, newLevel = GetFacilityLevel(facility) };

        int level = GetFacilityLevel(facility);
        int spiritStoneCost = FacilityRules.UpgradeSpiritStoneCost(level);
        int materialCost = FacilityRules.UpgradeMaterialCost(level);
        if (!WarehouseManager.Instance.RemoveItem(FacilityRules.SpiritStoneId, spiritStoneCost))
            return new FacilityUpgradeResult { success = false, reason = "灵石扣除失败", newLevel = level };
        WarehouseManager.Instance.RemoveItem(FacilityRules.BasicMaterialId, materialCost);
        SetFacilityLevel(facility, level + 1);
        TimeManager.Instance?.RecordFacilityUpgrade(facility, level + 1);
        EventManager.Instance?.TryTriggerSource(EventSource.FacilityUpgrade);
        return new FacilityUpgradeResult { success = true, newLevel = level + 1 };
    }

    private void SetFacilityLevel(FacilityType facility, int level)
    {
        switch (facility)
        {
            case FacilityType.MissionHall: playerData.missionHallLevel = level; break;
            case FacilityType.Warehouse: playerData.warehouseLevel = level; break;
            case FacilityType.TrainingRoom: playerData.trainingRoomLevel = level; break;
            case FacilityType.SecretRealm: playerData.secretRealmLevel = level; break;
            case FacilityType.AlchemyRoom: playerData.alchemyRoomLevel = level; break;
            case FacilityType.ProtectionArray: playerData.protectionArrayLevel = level; break;
            case FacilityType.InheritanceChamber: playerData.inheritanceChamberLevel = level; break;
            case FacilityType.ForgeRoom: playerData.forgeRoomLevel = level; break;
            case FacilityType.FormationPlatform: playerData.formationPlatformLevel = level; break;
        }
    }

    public void SetFacilityLevelForStory(FacilityType facility, int level)
    {
        SetFacilityLevel(facility, Mathf.Clamp(level, 0, FacilityRules.MaxLevel));
        EvaluateFoundingCompletion();
        OnFoundingChanged?.Invoke();
    }

    public void ProcessIdleFounderDay(NPCRuntime npc)
    {
        FoundingState state = playerData.founding;
        if (npc == null || state == null || GameFlowPermission.IsFoundingDevelopmentComplete(state) ||
            state.stage != FoundingStage.Cave ||
            state.inheritancePreparationProgress >= FoundingRules.MaxUnderstanding ||
            !state.selectedFounderIds.Contains(npc.CharacterId)) return;

        FounderCandidateData candidate = state.candidates.FirstOrDefault(item => item.candidateId == npc.CharacterId);
        int gain = FoundingRules.UnderstandingGain(candidate, GetFacilityLevel(FacilityType.InheritanceChamber) > 0);
        AddInheritancePreparation(gain, npc);
    }

    public void AddInheritancePreparation(int amount, NPCRuntime actor = null)
    {
        FoundingState state = playerData.founding;
        if (state == null || GameFlowPermission.IsFoundingDevelopmentComplete(state) || amount <= 0) return;
        state.inheritancePreparationProgress = Mathf.Clamp(state.inheritancePreparationProgress + amount, 0, FoundingRules.MaxUnderstanding);
        if (state.inheritancePreparationProgress >= FoundingRules.TechniqueMilestone && !state.techniqueMilestoneQueued)
        {
            FoundingTechniqueOption option = FoundingRules.GetTechniqueOption(state.selectedTechniqueId);
            if (option != null && EventManager.Instance != null &&
                EventManager.Instance.TryEnqueueEventById(option.milestoneEventId, actor))
                state.techniqueMilestoneQueued = true;
        }
        EvaluateFoundingCompletion();
        OnFoundingChanged?.Invoke();
    }

    public bool LearnTechnique(NPCRuntime npc, string techniqueId, bool setAsMain, bool bypassRequirements = false)
    {
        TechniqueDefinition definition = TechniqueRules.Get(techniqueId);
        if (npc?.Character == null || definition == null ||
            TechniqueRules.SectState(playerData, techniqueId) == null) return false;
        if (!bypassRequirements)
        {
            TechniqueLearningRequirement requirement = definition.learningRequirement ?? new TechniqueLearningRequirement();
            if (npc.Comprehension < requirement.minimumComprehension ||
                TechniqueRules.RootAffinity(npc.Character.spiritRoot, definition.elements) < requirement.minimumElementAffinity)
                return false;
        }
        npc.Character.techniqueProgresses = npc.Character.techniqueProgresses ?? new List<PersonalTechniqueProgress>();
        if (TechniqueRules.Progress(npc.Character, techniqueId) == null)
            npc.Character.techniqueProgresses.Add(new PersonalTechniqueProgress { techniqueId = techniqueId, understanding = 0f });
        if (setAsMain && definition.category == TechniqueCategory.Main)
            npc.Character.mainTechniqueId = techniqueId;
        return true;
    }

    public bool SwitchMainTechnique(NPCRuntime npc, string techniqueId)
    {
        TechniqueDefinition definition = TechniqueRules.Get(techniqueId);
        if (npc?.Character == null || definition?.category != TechniqueCategory.Main ||
            TechniqueRules.SectState(playerData, techniqueId) == null ||
            TechniqueRules.Progress(npc.Character, techniqueId) == null) return false;
        npc.Character.mainTechniqueId = techniqueId;
        return true;
    }

    public float AddTechniqueUnderstanding(float amount, NPCRuntime actor)
    {
        if (actor?.Character == null || amount <= 0f || string.IsNullOrWhiteSpace(actor.Character.mainTechniqueId)) return 0f;
        PersonalTechniqueProgress progress = TechniqueRules.Progress(actor.Character, actor.Character.mainTechniqueId);
        if (progress == null) return 0f;
        float before = progress.understanding;
        progress.understanding = Mathf.Clamp(progress.understanding + amount, 0f, 100f);
        return progress.understanding - before;
    }

    public float AddSectTechniqueMastery(string techniqueId, float amount, NPCRuntime contributor)
    {
        SectTechniqueState state = TechniqueRules.SectState(playerData, techniqueId);
        if (state == null || amount <= 0f) return 0f;
        float before = state.masteryProgress;
        float cap = state.firstAnnotationResolved ? 100f : TechniqueRules.FirstAnnotationThreshold;
        state.masteryProgress = Mathf.Clamp(state.masteryProgress + amount, 0f, cap);
        if (state.masteryProgress >= TechniqueRules.FirstAnnotationThreshold && !state.firstAnnotationResolved &&
            !state.firstAnnotationQueued && contributor != null && EventManager.Instance != null)
        {
            string eventId = $"technique_{techniqueId}_first_annotation";
            if (EventManager.Instance.TryEnqueueEventById(eventId, contributor)) state.firstAnnotationQueued = true;
        }
        return state.masteryProgress - before;
    }

    public bool ResolveTechniqueAnnotation(string payload)
    {
        string[] parts = (payload ?? string.Empty).Split('|');
        if (parts.Length != 2) return false;
        SectTechniqueState state = TechniqueRules.SectState(playerData, parts[0]);
        if (state == null || state.firstAnnotationResolved || state.masteryProgress < TechniqueRules.FirstAnnotationThreshold ||
            (parts[1] != TechniqueRules.BeginnerAnnotationId && parts[1] != TechniqueRules.AdaptiveAnnotationId)) return false;
        state.annotationIds = state.annotationIds ?? new List<string>();
        state.annotationIds.Add(parts[1]);
        state.firstAnnotationResolved = true;
        state.firstAnnotationQueued = true;
        return true;
    }

    private void EnsureFoundingTechniqueGranted(FoundingState state)
    {
        if (state == null || TechniqueRules.Get(state.selectedTechniqueId) == null) return;
        playerData.techniqueLibrary = playerData.techniqueLibrary ?? new List<SectTechniqueState>();
        if (TechniqueRules.SectState(playerData, state.selectedTechniqueId) == null)
            playerData.techniqueLibrary.Add(new SectTechniqueState { techniqueId = state.selectedTechniqueId });
        foreach (string characterId in state.selectedFounderIds ?? new List<string>())
            LearnTechnique(NPCManager.Instance?.GetRuntime(characterId), state.selectedTechniqueId, true, true);
    }

    public void AddVillageRelation(int amount, NPCRuntime actor = null)
    {
        VillageState village = playerData.founding?.village;
        if (village == null || amount == 0) return;
        int previousRelation = village.relation;
        village.relation = Mathf.Clamp(village.relation + amount, 0, 100);
        if (previousRelation < FoundingRules.VillageSupportRelation &&
            village.relation >= FoundingRules.VillageSupportRelation && !village.supportLaborGranted)
        {
            village.totalLabor = Mathf.Max(village.totalLabor, FoundingRules.VillageLabor);
            village.supportLaborGranted = true;
        }
        if (village.relation >= FoundingRules.VillageFamiliarRelation && !village.milestoneEventQueued && EventManager.Instance != null &&
            EventManager.Instance.TryEnqueueEventById("founding_village_milestone", actor))
            village.milestoneEventQueued = true;
        ExternalThreatRules.TryScheduleFromRelation(TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay);
        OnFoundingChanged?.Invoke();
    }

    public bool ConfirmWorldSite(int cellIndex, out string reason)
    {
        FoundingState state = playerData?.founding;
        WorldMap map = WorldMapSession.Current;
        WorldMapProgressState progress = WorldMapSession.Progress;
        if (state == null || state.completed || state.stage != FoundingStage.WorldSelection)
        { reason = "当前不能选择洞府位置"; return false; }
        if (map?.cells == null || cellIndex < 0 || cellIndex >= map.cells.Length)
        { reason = "地图格不存在"; return false; }
        if (!map.cells[cellIndex].isBuildable)
        { reason = "该地形不能建立洞府"; return false; }
        string sectName = NormalizeSectName(state.pendingSectName, out string nameReason);
        if (nameReason != null) { reason = nameReason; return false; }
        if (state.selectedFounderIds == null || state.selectedFounderIds.Distinct().Count() != 3 ||
            state.candidates == null ||
            state.selectedFounderIds.Any(id => state.candidates.All(candidate => candidate?.candidateId != id)))
        { reason = "初始弟子数据无效"; return false; }
        if (NPCManager.Instance == null ||
            state.selectedFounderIds.Any(id => NPCManager.Instance.GetRuntime(id) == null))
        { reason = "初始弟子运行时数据缺失"; return false; }
        if (FoundingRules.GetTechnique(state.selectedTechniqueId) == null)
        { reason = "初始功法数据无效"; return false; }
        if (WorldMapProgressRules.GetSectBase(progress) != null || !string.IsNullOrEmpty(playerData.sectId) ||
            (progress?.influenceSources?.Count ?? 0) != 0 || (progress?.cellInfluences?.Count ?? 0) != 0)
        { reason = "宗门驻地已经建立"; return false; }

        state.selectedWorldCellIndex = cellIndex;
        MapSiteData sectBase = new MapSiteData
        {
            siteId = WorldMapProgressRules.PlayerSectBaseId,
            cellIndex = cellIndex,
            siteType = MapSiteType.SectBase,
            siteName = sectName,
            isRevealed = true,
            canInteract = true,
            revealState = MapContentRevealState.Discovered,
            siteState = MapSiteState.Developed,
            ownerSectId = WorldMapProgressRules.PlayerSectOwnerId,
            discoveredDay = TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay,
            lastUpdatedDay = TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay
        };
        if (!WorldMapContentRules.TryPrepareSectBasePlacement(map, progress, cellIndex, out reason))
            return false;
        InfluenceSourceData sectBaseSource = new InfluenceSourceData
        {
            sourceId = sectBase.siteId,
            sourceType = InfluenceSourceType.SectBase,
            cellIndex = sectBase.cellIndex,
            controllerSectId = WorldMapProgressRules.PlayerSectOwnerId,
            baseStrength = WorldMapInfluenceRules.SectBaseStrength,
            radius = WorldMapInfluenceRules.SectBaseRadius,
            isActive = true
        };
        WorldMapProgressState updatedProgress = new WorldMapProgressState
        {
            revealedCellIndices = new List<int>(progress?.revealedCellIndices ?? new List<int>()),
            exploredCellIndices = new List<int>(progress?.exploredCellIndices ?? new List<int>()),
            mapSites = new List<MapSiteData>(progress?.mapSites ?? new List<MapSiteData>()) { sectBase },
            resourceNodes = new List<ResourceNodeRuntime>(progress?.resourceNodes ?? new List<ResourceNodeRuntime>()),
            spiritualVeins = new List<SpiritualVeinRuntime>(progress?.spiritualVeins ?? new List<SpiritualVeinRuntime>()),
            influenceSources = new List<InfluenceSourceData>(progress?.influenceSources ?? new List<InfluenceSourceData>())
                { sectBaseSource },
            cellInfluences = new List<CellInfluenceState>(),
            isInfluenceDirty = true
        };
        WorldMapInfluenceRules.Recalculate(map, updatedProgress);
        WorldMapContentRules.RefreshHints(map, updatedProgress);
        ResourceEcologyRules.EnsureRuntime(map, updatedProgress);
        // 宗门建立后，在影响范围内生成青石村 WorldLocation，并把宗门自身也注册为世界地点。
        WorldLocationRules.CreateStarterVillage(map, cellIndex, updatedProgress);
        WorldLocationRules.CreatePlayerSect(map, cellIndex, sectName);
        // 只把已发现内容同步为 WorldLocation 门面；Hidden/Hinted 保持纯 MapSiteData。
        WorldLocationRules.SynchronizeFromMapSites(map, updatedProgress);

        playerData.sectId = WorldMapProgressRules.PlayerSectOwnerId;
        playerData.sectName = sectName;
        playerData.foundedDay = TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay;
        playerData.influenceRadius = 2;
        // 宗门在选址确认时即真实成立；stage 只是后续流程状态。
        state.sectCreated = true;
        state.stage = FoundingStage.Cave;
        WorldMapSession.Set(map, updatedProgress);
        // 建宗同时替换地图进度；显式通知地图表现层立即重绘影响力覆盖。
        WorldMapSession.NotifyProgressChanged();
        reason = null;
        OnFoundingChanged?.Invoke();
        SaveManager.Instance?.AutoSave();
        return true;
    }

    public int ChangeVillagePopulation(int amount)
    {
        VillageState village = playerData.founding?.village;
        if (village == null || amount == 0) return 0;
        int before = village.population;
        village.population = Mathf.Max(0, village.population + amount);
        OnFoundingChanged?.Invoke();
        return village.population - before;
    }

    public int ChangeVillageLabor(int amount, int maximum = int.MaxValue)
    {
        VillageState village = playerData.founding?.village;
        if (village == null || amount == 0) return 0;
        int before = village.totalLabor;
        village.totalLabor = Mathf.Clamp(village.totalLabor + amount, 0, Mathf.Max(0, maximum));
        OnFoundingChanged?.Invoke();
        return village.totalLabor - before;
    }

    public void NotifyFoundingChanged() => OnFoundingChanged?.Invoke();

    public bool TryReserveLabor(int amount, out string reason)
    {
        VillageState village = playerData.founding?.village;
        if (amount <= 0) { reason = null; return true; }
        if (village == null || village.totalLabor - village.reservedLabor < amount) { reason = "可用劳动力不足"; return false; }
        village.reservedLabor += amount;
        reason = null;
        OnFoundingChanged?.Invoke();
        return true;
    }

    public void ReleaseLabor(int amount)
    {
        VillageState village = playerData.founding?.village;
        if (village == null || amount <= 0) return;
        village.reservedLabor = Mathf.Max(0, village.reservedLabor - amount);
        OnFoundingChanged?.Invoke();
    }

    public void ReconcileReservedLabor(IEnumerable<Mission> activeMissions)
    {
        VillageState village = playerData.founding?.village;
        if (village == null) return;
        village.reservedLabor = (activeMissions ?? Enumerable.Empty<Mission>())
            .Where(item => item != null && item.Data != null && item.Data.laborCost > 0 &&
                           (item.State == MissionState.Active || item.State == MissionState.WaitingNode))
            .Sum(item => Mathf.Max(0, item.Data.laborCost));
    }

    public bool IsCoreFounder(string characterId) =>
        playerData.founding != null && playerData.founding.selectedFounderIds.Contains(characterId);

    public void EvaluateFoundingCompletion()
    {
        FoundingState state = playerData.founding;
        if (state == null || GameFlowPermission.IsFoundingDevelopmentComplete(state) ||
            state.inheritancePreparationProgress < FoundingRules.MaxUnderstanding) return;
        bool repairedAny = GetFacilityLevel(FacilityType.TrainingRoom) > 0 ||
                           GetFacilityLevel(FacilityType.Warehouse) > 0 ||
                           GetFacilityLevel(FacilityType.ProtectionArray) > 0 ||
                           GetFacilityLevel(FacilityType.InheritanceChamber) > 0;
        FoundingTechniqueOption option = FoundingRules.GetTechniqueOption(state.selectedTechniqueId);
        if (!repairedAny || option == null || GetFacilityLevel(option.unlockFacility) <= 0) return;
        state.completed = true;
        state.stage = FoundingStage.Completed;
        OnFoundingChanged?.Invoke();
    }

}
