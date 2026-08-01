using System;
using System.Collections.Generic;
using System.Linq;
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
        playerData = new PlayerData
        {
            gold = 100,
            missionHallLevel = 0,
            warehouseLevel = 0,
            trainingRoomLevel = 0,
            secretRealmLevel = 0,
            alchemyRoomLevel = 0,
            explorationHallLevel = 0,
            protectionArrayLevel = 0,
            inheritanceChamberLevel = 0,
            forgeRoomLevel = 0,
            formationPlatformLevel = 0,
            founding = new FoundingState
            {
                initialized = true,
                completed = false,
                stage = FoundingStage.CandidateSelection,
                candidateSeed = seed,
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
        state.stage = FoundingStage.Cave;
        reason = null;
        OnFoundingChanged?.Invoke();
        SaveManager.Instance?.AutoSave();
        return true;
    }

    /// <summary>
    /// 增加金币
    /// </summary>
    public void AddGold(int gold)
    {
        playerData.gold = Mathf.Max(0, playerData.gold + gold);
    }

    public bool SpendGold(int amount)
    {
        if (amount < 0 || playerData.gold < amount) return false;
        playerData.gold -= amount;
        return true;
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
            case FacilityType.ExplorationHall: return playerData.explorationHallLevel;
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
        if (facility == FacilityType.ExplorationHall || facility == FacilityType.ProtectionArray ||
            facility == FacilityType.InheritanceChamber || facility == FacilityType.ForgeRoom ||
            facility == FacilityType.FormationPlatform)
        { reason = "该设施当前不可升级"; return false; }
        if (level >= FacilityRules.MaxLevel) { reason = "设施已达到最高等级"; return false; }
        if (playerData.gold < FacilityRules.UpgradeGoldCost(level)) { reason = "灵材不足"; return false; }
        if (WarehouseManager.Instance == null || WarehouseManager.Instance.GetItemCount(FacilityRules.BasicMaterialId) < FacilityRules.UpgradeMaterialCost(level))
        { reason = "基础材料不足"; return false; }
        reason = null;
        return true;
    }

    public FacilityUpgradeResult TryUpgradeFacility(FacilityType facility)
    {
        if (!CanUpgradeFacility(facility, out string reason))
            return new FacilityUpgradeResult { success = false, reason = reason, newLevel = GetFacilityLevel(facility) };

        int level = GetFacilityLevel(facility);
        int goldCost = FacilityRules.UpgradeGoldCost(level);
        int materialCost = FacilityRules.UpgradeMaterialCost(level);
        playerData.gold -= goldCost;
        WarehouseManager.Instance.RemoveItem(FacilityRules.BasicMaterialId, materialCost);
        SetFacilityLevel(facility, level + 1);
        TimeManager.Instance?.RecordFacilityUpgrade(facility, level + 1);
        TimeManager.Instance?.RecordPreAdvanceResourceChange(-goldCost, -materialCost);
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
            case FacilityType.ExplorationHall: playerData.explorationHallLevel = level; break;
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
        if (npc == null || state == null || state.completed || state.stage != FoundingStage.Cave ||
            state.techniqueUnderstanding >= FoundingRules.MaxUnderstanding ||
            !state.selectedFounderIds.Contains(npc.CharacterId)) return;

        FounderCandidateData candidate = state.candidates.FirstOrDefault(item => item.candidateId == npc.CharacterId);
        int gain = FoundingRules.UnderstandingGain(candidate, GetFacilityLevel(FacilityType.InheritanceChamber) > 0);
        AddTechniqueUnderstanding(gain, npc);
    }

    public void AddTechniqueUnderstanding(int amount, NPCRuntime actor = null)
    {
        FoundingState state = playerData.founding;
        if (state == null || state.completed || amount <= 0) return;
        state.techniqueUnderstanding = Mathf.Clamp(state.techniqueUnderstanding + amount, 0, FoundingRules.MaxUnderstanding);
        if (state.techniqueUnderstanding >= FoundingRules.TechniqueMilestone && !state.techniqueMilestoneQueued)
        {
            FoundingTechniqueDefinition technique = FoundingRules.GetTechnique(state.selectedTechniqueId);
            if (technique != null && EventManager.Instance != null &&
                EventManager.Instance.TryEnqueueEventById(technique.milestoneEventId, actor))
                state.techniqueMilestoneQueued = true;
        }
        EvaluateFoundingCompletion();
        OnFoundingChanged?.Invoke();
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
        if (state == null || state.completed || state.techniqueUnderstanding < FoundingRules.MaxUnderstanding) return;
        bool repairedAny = GetFacilityLevel(FacilityType.TrainingRoom) > 0 ||
                           GetFacilityLevel(FacilityType.Warehouse) > 0 ||
                           GetFacilityLevel(FacilityType.ProtectionArray) > 0 ||
                           GetFacilityLevel(FacilityType.InheritanceChamber) > 0;
        FoundingTechniqueDefinition technique = FoundingRules.GetTechnique(state.selectedTechniqueId);
        if (!repairedAny || technique == null || GetFacilityLevel(technique.unlockFacility) <= 0) return;
        state.completed = true;
        state.stage = FoundingStage.Completed;
        OnFoundingChanged?.Invoke();
    }

    public bool UpgradeFacility(string facility, int goldCost)
    {
        if (facility == "Infirmary") return false;
        if (!System.Enum.TryParse(facility, out FacilityType type)) return false;
        return TryUpgradeFacility(type).success;
    }
}
