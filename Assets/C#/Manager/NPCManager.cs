using System.Collections.Generic;
using System.Linq;
using System;
using Cultivation4X.WorldMap;
using UnityEngine;

public class NPCManager : MonoBehaviour
{

    public static NPCManager Instance;


    private Dictionary<NPCData, NPCRuntime> npcMap
        = new Dictionary<NPCData, NPCRuntime>();

    private readonly Dictionary<string, NPCRuntime> npcById =
        new Dictionary<string, NPCRuntime>();
    private readonly List<NPCRuntime> runtimes = new List<NPCRuntime>();
    public event Action OnRosterChanged;

    public List<NPCRuntime> GetAllNPC()
    {
        return new List<NPCRuntime>(runtimes);
    }

    public List<NPCRuntime> GetLivingNPC()
    {
        return runtimes.Where(npc => npc.Character.IsAlive).ToList();
    }
    private void Awake()
    {
       
        if (Instance == null)
        {
            Instance = this;
            DontDestroyUtility.MarkPersistent(gameObject);
            InitNPC();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 初始化NPC
    /// </summary>
    public void InitNPC()
    {
        npcMap.Clear();
        npcById.Clear();
        runtimes.Clear();
        NPCData[] npcs = Resources.LoadAll<NPCData>("NPC");

        

        foreach (var npc in npcs)
        {
            NPCRuntime runtime = new NPCRuntime(npc);

            npcMap.Add(npc, runtime);
            npcById[runtime.CharacterId] = runtime;
            runtimes.Add(runtime);
           
        }


        Debug.Log("NPC运行时初始化完成");
        OnRosterChanged?.Invoke();
    }

    public void ClearCharacters()
    {
        npcMap.Clear();
        npcById.Clear();
        runtimes.Clear();
        OnRosterChanged?.Invoke();
    }

    public bool CreateFounders(IEnumerable<FounderCandidateData> candidates)
    {
        List<FounderCandidateData> selected = (candidates ?? Enumerable.Empty<FounderCandidateData>()).Where(item => item != null).ToList();
        if (selected.Count != 3 || selected.Select(item => item.candidateId).Distinct().Count() != 3) return false;

        ClearCharacters();
        foreach (FounderCandidateData candidate in selected)
        {
            CharacterState state = new CharacterState
            {
                characterId = candidate.candidateId,
                templateId = string.Empty,
                displayName = candidate.displayName,
                age = candidate.age,
                realmLayer = 1,
                realm = CultivationRealm.QiRefining,
                hasGeneratedProfile = true,
                baseAttack = candidate.attack,
                baseIntelligence = candidate.intelligence,
                baseAgility = candidate.agility,
                baseComprehension = candidate.comprehension,
                baseCombatComprehension = candidate.combatComprehension,
                basePhysique = candidate.physique,
                spiritRoot = candidate.spiritRoot,
                initialFeatureId = candidate.initialFeatureId,
                traitIds = string.IsNullOrWhiteSpace(candidate.personalityTraitId)
                    ? new List<string>()
                    : new List<string> { candidate.personalityTraitId }
            };
            AddGeneratedRuntime(state);
        }
        OnRosterChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 设置NPC状态
    /// </summary>
    public void SetState(NPCData npc, NPCState state, int days = 0)
    {
        NPCRuntime runtime = GetRuntime(npc);

        if (runtime == null)
        {
            Debug.LogError("找不到NPC运行数据：" + npc.npcName);
            return;
        }


        runtime.SetState(state, days);
    }

    public void SetState(NPCRuntime runtime, NPCState state, int days = 0)
    {
        if (runtime == null) return;
        runtime.SetState(state, days);
    }
    //开始任务
    public void StartMission(NPCData npc, Mission mission)
    {
        NPCRuntime runtime = GetRuntime(npc);


        if (runtime == null)
            return;


        runtime.SetState(NPCState.Busy);

        runtime.CurrentMission = mission;
    }

    public void StartMission(NPCRuntime runtime, Mission mission)
    {
        if (runtime == null || !runtime.CanDispatch()) return;
        runtime.SetState(NPCState.Busy);
        runtime.CurrentMission = mission;
    }


    public void Injured(NPCRuntime npc, int days, int recordDay = -1)
    {
        if (npc == null || !npc.Character.IsAlive) return;
        int day = ResolveRecordDay(recordDay);
        npc.Character.health = days >= 5 ? HealthState.SeriousInjury : HealthState.LightInjury;
        ExperienceRecordRules.Add(npc.Character, day, ExperienceType.Injury, ExperienceImportance.Minor,
            "injury", $"受伤，需要休养 {days} 天", null,
            new[] { ExperienceRecordRules.Value("days", days) });
        EventManager.Instance?.TryTriggerSource(EventSource.Injury, npc);
        bool missionCanContinue = npc.CurrentMission != null &&
            (npc.CurrentMission.State == MissionState.Active || npc.CurrentMission.State == MissionState.WaitingNode);
        if (days < 5 && missionCanContinue) return;
        Mission mission = npc.CurrentMission;
        SetState(npc, NPCState.Injured, days);
        if (missionCanContinue) mission.FailMission(false);
        if (mission != null) npc.CurrentMission = null;
    }

    public void ApplyPermanentTrauma(NPCRuntime npc, string traitId, int recordDay = -1)
    {
        if (npc == null || !npc.Character.IsAlive) return;
        npc.Character.health = HealthState.PermanentTrauma;
        npc.Character.AddTrait(traitId);
        ExperienceRecordRules.Add(npc.Character, ResolveRecordDay(recordDay), ExperienceType.Injury,
            ExperienceImportance.Major, "permanent_trauma", "遭受永久创伤", traitId);
        Mission mission = npc.CurrentMission;
        if (mission != null) mission.FailMission(false);
    }

    //恢复空闲状态
    public void Recover(NPCRuntime npc)
    {
        NPCRuntime runtime = npc;
        if (runtime == null)
            return;

        bool recoveredFromInjury = runtime.Character.health != HealthState.Healthy || runtime.State == NPCState.Injured;
        runtime.SetState(NPCState.Idle, 0);
        if (runtime.Character.health != HealthState.PermanentTrauma)
            runtime.Character.health = HealthState.Healthy;

        runtime.CurrentMission = null;
        if (recoveredFromInjury) EventManager.Instance?.TryTriggerSource(EventSource.Recovery, npc);
    }
    /// <summary>
    /// 获取NPC运行数据
    /// </summary>
    public NPCRuntime GetRuntime(NPCData npc)
    {
        if (npcMap.ContainsKey(npc))
        {
            return npcMap[npc];
        }


        return null;
    }

    public NPCRuntime GetRuntime(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId)) return null;
        npcById.TryGetValue(characterId, out NPCRuntime runtime);
        return runtime;
    }

    /// <summary>
    /// 建立人物关系，并给双方各写一条 Relationship 履历。
    /// 这是所有关系写入的统一入口：事件、自主社交、未来外交/师徒/仇敌都走这里。
    /// 文本参数缺省时使用默认文本；旧的三参数调用源码兼容。
    /// </summary>
    public bool AddRelationship(string sourceId, string targetId, RelationshipTag tag,
        string sourceRecordText = null, string targetRecordText = null, int recordDay = -1)
    {
        NPCRuntime source = GetRuntime(sourceId);
        NPCRuntime target = GetRuntime(targetId);
        if (source == null || target == null || sourceId == targetId) return false;
        source.Character.relationships = source.Character.relationships ?? new List<RelationshipRecord>();
        target.Character.relationships = target.Character.relationships ?? new List<RelationshipRecord>();
        if (source.Character.relationships.Any(r => r.targetCharacterId == targetId && r.tag == tag)
            || target.Character.relationships.Any(r => r.targetCharacterId == sourceId && r.tag == tag)) return false;
        int day = ResolveRecordDay(recordDay);
        source.Character.relationships.Add(new RelationshipRecord
        {
            sourceCharacterId = sourceId,
            targetCharacterId = targetId,
            tag = tag,
            createdDay = day
        });
        target.Character.relationships.Add(new RelationshipRecord
        {
            sourceCharacterId = targetId,
            targetCharacterId = sourceId,
            tag = tag,
            createdDay = day
        });
        ExperienceRecordRules.Add(source.Character, day, ExperienceType.Relationship, ExperienceImportance.Major,
            "relationship", string.IsNullOrWhiteSpace(sourceRecordText)
                ? DefaultRelationshipRecordText(source, target, tag)
                : sourceRecordText, targetId, new[]
                {
                    ExperienceRecordRules.Value("relationshipTag", tag.ToString())
                });
        ExperienceRecordRules.Add(target.Character, day, ExperienceType.Relationship, ExperienceImportance.Major,
            "relationship", string.IsNullOrWhiteSpace(targetRecordText)
                ? DefaultRelationshipRecordText(target, source, tag)
                : targetRecordText, sourceId, new[]
                {
                    ExperienceRecordRules.Value("relationshipTag", tag.ToString())
                });
        return true;
    }

    /// <summary>
    /// 关系结果兜底：没有可建关系的目标时，只给当事人写一条 Relationship 履历。
    /// </summary>
    public void RecordRelationshipOutcome(string characterId, string text, int recordDay = -1)
    {
        NPCRuntime runtime = GetRuntime(characterId);
        if (runtime == null || !runtime.Character.IsAlive || string.IsNullOrWhiteSpace(text)) return;
        ExperienceRecordRules.Add(runtime.Character, ResolveRecordDay(recordDay), ExperienceType.Relationship,
            ExperienceImportance.Major, "relationship", text);
    }

    private static string DefaultRelationshipRecordText(NPCRuntime subject, NPCRuntime other, RelationshipTag tag)
    {
        string otherName = string.IsNullOrWhiteSpace(other.Character?.displayName)
            ? other.Data?.npcName
            : other.Character.displayName;
        if (string.IsNullOrWhiteSpace(otherName)) otherName = "同门";
        return $"与{otherName}结为{RelationshipTagName(tag)}";
    }

    private static string RelationshipTagName(RelationshipTag tag)
    {
        switch (tag)
        {
            case RelationshipTag.MasterApprentice: return "师徒";
            case RelationshipTag.Friend: return "好友";
            case RelationshipTag.Rival: return "仇敌";
            case RelationshipTag.LifeSaver: return "救命恩人";
            default: return tag.ToString();
        }
    }

    public bool Kill(NPCRuntime npc, string cause, int recordDay = -1)
    {
        if (npc == null || !npc.Character.IsAlive) return false;
        int day = ResolveRecordDay(recordDay);
        Mission currentMission = npc.CurrentMission;
        // 防止全员死亡坏档：最后一名弟子转为永久创伤。
        if (GetLivingNPC().Count <= 1)
        {
            if (currentMission != null) currentMission.FailMission(false);
            npc.Character.health = HealthState.PermanentTrauma;
            npc.Character.AddTrait("near_death_survivor");
            ExperienceRecordRules.Add(npc.Character, day, ExperienceType.NearDeath, ExperienceImportance.Major,
                "near_death", $"死里逃生：{cause}", cause);
            Recover(npc);
            return false;
        }

        npc.Character.health = HealthState.Dead;
        if (currentMission != null) currentMission.FailMission(false);
        MissionManager.Instance?.CancelAwaitingMissionsForCharacter(npc.CharacterId);
        npc.Character.activityState = NPCState.Idle;
        npc.State = NPCState.Idle;
        npc.CurrentMission = null;
        SectOrganizationRules.CleanupMembers(PlayerManager.Instance?.playerData,
            GetLivingNPC().Select(item => item.CharacterId));
        ExperienceRecordRules.Add(npc.Character, day, ExperienceType.Death, ExperienceImportance.Major,
            "death", cause, cause);
        SaveManager.Instance?.AutoSave();
        return true;
    }

    public NPCRuntime RecruitFromTemplate(string templateId, int recordDay = -1)
    {
        NPCData template = Resources.LoadAll<NPCData>("NPC")
            .FirstOrDefault(data => data.npcID == templateId);
        if (template == null) return null;

        CharacterState state = new CharacterState
        {
            characterId = $"{templateId}_{System.Guid.NewGuid():N}",
            templateId = templateId,
            displayName = template.npcName,
            age = template.age,
            realm = CultivationRealm.QiRefining,
            realmLayer = 1,
            spiritRoot = FoundingRules.GenerateSpiritRoot(new System.Random(templateId.GetHashCode())),
            baseAttack = template.attack,
            baseIntelligence = template.intelligence,
            baseAgility = template.agility,
            baseComprehension = template.comprehension,
            baseCombatComprehension = template.combatComprehension,
            basePhysique = template.physique,
            traitIds = new List<string>(template.initialTraits)
        };
        NPCRuntime runtime = new NPCRuntime(template, state);
        if (!npcMap.ContainsKey(template)) npcMap[template] = runtime;
        npcById[state.characterId] = runtime;
        runtimes.Add(runtime);
        ExperienceRecordRules.Add(state, ResolveRecordDay(recordDay), ExperienceType.Recruitment,
            ExperienceImportance.Minor, "recruit", "加入宗门", templateId);
        return runtime;
    }



    /// <summary>
    /// 每天推进
    /// </summary>
    public void OnDayPassed()
    {
        ProcessDayWithResults();
    }

    /// <summary>
    /// 每天推进并返回实际执行证据。旧入口继续保留，但午夜结算应消费本结果。
    /// </summary>
    public List<DiscipleDayExecutionResult> ProcessDayWithResults()
    {
        List<DiscipleDayExecutionResult> results = new List<DiscipleDayExecutionResult>();
        bool worldChanged = false;
        DiscipleAIConfig aiConfig = null;
        IdentityDefinition identity = null;
        foreach (var npc in runtimes.Where(item => item?.Character != null)
                     .OrderBy(item => item.CharacterId, StringComparer.Ordinal))
        {
            DailyCultivationSimulator.StartDay(npc);
            DiscipleMentalStateRules.RestoreDaily(npc);
            bool wasInjured = npc.State == NPCState.Injured;
            npc.OnDayPassed();
            if (wasInjured && npc.State == NPCState.Idle) Recover(npc);
            if (!npc.Character.IsAlive) continue;
            int day = CurrentDay;
            MonthlyActivityType activity;
            string lockedActionId;
            if (TimeManager.Instance == null ||
                !TimeManager.Instance.TryGetLockedActivity(npc.CharacterId, day, out activity, out lockedActionId))
            {
                activity = MonthlyPlanRules.ActivityFor(npc, day);
                lockedActionId = null;
            }
            DiscipleDayExecutionResult execution = new DiscipleDayExecutionResult
            {
                discipleId = npc.CharacterId,
                scheduledActivity = activity,
                actualActivity = DiscipleCurrentStateBuilder.ActivityKind(activity),
                executed = false
            };
            Mission activeMission = npc.CurrentMission;
            if (activeMission != null && (activeMission.State == MissionState.Active || activeMission.State == MissionState.WaitingNode))
            {
                execution.actualActivity = DiscipleActivityKind.Mission;
                execution.actionId = activeMission.Data?.id;
                execution.actionDisplayName = activeMission.Data?.name ?? "执行任务";
                execution.executed = true;
                results.Add(execution);
                continue;
            }
            if (wasInjured || npc.State != NPCState.Idle || npc.Character.health != HealthState.Healthy)
            {
                execution.actualActivity = DiscipleActivityKind.Recovery;
                execution.actionId = "recovery";
                execution.actionDisplayName = "休养";
                execution.failureReason = npc.Character.health == HealthState.PermanentTrauma ? "永久创伤" : "伤病休养";
                results.Add(execution);
                continue;
            }
            if (activity == MonthlyActivityType.Training)
            {
                DailyCultivationResult cultivation = DailyCultivationSimulator.SimulateTrainingDay(npc, day, lockedActionId);
                execution.actualActivity = DiscipleActivityKind.Training;
                execution.actionId = cultivation?.selectedActionId ?? lockedActionId;
                execution.actionDisplayName = cultivation?.selectedActionName ?? DailyCultivationSimulator.ActionName(lockedActionId);
                execution.executed = cultivation != null;
                execution.failureReason = cultivation == null ? "修炼未执行" : null;
                execution.cultivationResult = cultivation;
            }
            else if (activity == MonthlyActivityType.SectDuty)
            {
                if (aiConfig == null)
                {
                    aiConfig = DiscipleAIConfigLoader.Load();
                    identity = aiConfig.Identities.FirstOrDefault(item => item != null && item.autonomyEnabled);
                }
                SectDutyDecision decision = SectDutyResolver.Resolve(npc, day, aiConfig, identity,
                    PlayerManager.Instance?.playerData, WorldMapSession.Current, WorldMapSession.Progress);
                SectDutyExecutionOutcome outcome = SectDutyExecutor.Execute(
                    decision, WorldMapSession.Current, WorldMapSession.Progress);
                execution.actualActivity = DiscipleActivityKind.SectDuty;
                execution.actionId = outcome.actionId;
                execution.actionDisplayName = string.IsNullOrWhiteSpace(outcome.targetDisplayName)
                    ? outcome.actionDisplayName ?? "宗门事务"
                    : $"{outcome.actionDisplayName ?? "宗门事务"} · {outcome.targetDisplayName}";
                execution.targetId = outcome.targetId;
                execution.targetDisplayName = outcome.targetDisplayName;
                execution.departmentId = outcome.departmentId;
                execution.executed = outcome.executed;
                execution.failureReason = outcome.failureReason;
                worldChanged |= outcome.worldChanged;
            }
            else
            {
                execution.actualActivity = DiscipleActivityKind.Free;
                execution.actionId = "free_day";
                execution.actionDisplayName = "自由活动";
                execution.executed = true;
            }
            results.Add(execution);
        }
        SectOrganizationRules.CleanupMembers(PlayerManager.Instance?.playerData,
            runtimes.Where(item => item?.Character?.IsAlive == true).Select(item => item.CharacterId));
        if (worldChanged) WorldMapSession.NotifyProgressChanged();
        return results;
    }

    public void ApplyNightlyCultivationSettlement()
    {
        foreach (NPCRuntime npc in runtimes)
            if (npc?.Character?.IsAlive == true) DailyCultivationSimulator.ApplyNightLeak(npc);
    }

    private int CurrentDay => TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay;
    private int ResolveRecordDay(int requested) => requested >= 0 ? requested : CurrentDay;

    public void RestoreCharacters(IEnumerable<CharacterState> states)
    {
        npcMap.Clear();
        npcById.Clear();
        runtimes.Clear();
        Dictionary<string, NPCData> templates = Resources.LoadAll<NPCData>("NPC")
            .Where(item => !string.IsNullOrWhiteSpace(item.npcID))
            .GroupBy(item => item.npcID)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (CharacterState state in states ?? Enumerable.Empty<CharacterState>())
        {
            if (state == null) continue;
            NormalizeRestoredCharacter(state);
            if (string.IsNullOrWhiteSpace(state.characterId))
            {
                Debug.LogWarning($"跳过缺少角色ID的存档角色: {state.displayName ?? state.templateId ?? "未知"}");
                continue;
            }
            if (state.hasGeneratedProfile)
            {
                AddGeneratedRuntime(state);
                continue;
            }
            if (!templates.TryGetValue(state.templateId, out NPCData template))
            {
                Debug.LogWarning($"存档角色缺少模板: {state.templateId}");
                continue;
            }
            NPCRuntime runtime = new NPCRuntime(template, state);
            if (!npcMap.ContainsKey(template)) npcMap[template] = runtime;
            npcById[state.characterId] = runtime;
            runtimes.Add(runtime);
        }
        OnRosterChanged?.Invoke();
    }

    private void AddGeneratedRuntime(CharacterState state)
    {
        NormalizeRestoredCharacter(state);
        if (string.IsNullOrWhiteSpace(state.characterId)) return;
        NPCData data = ScriptableObject.CreateInstance<NPCData>();
        data.hideFlags = HideFlags.DontSave;
        data.npcID = state.characterId;
        data.npcName = state.displayName;
        data.age = state.age;
        data.level = state.realmLayer;
        data.exp = 0;
        data.attack = state.baseAttack;
        data.intelligence = state.baseIntelligence;
        data.agility = state.baseAgility;
        data.comprehension = state.baseComprehension;
        data.combatComprehension = state.baseCombatComprehension > 0
            ? state.baseCombatComprehension
            : state.baseComprehension;
        data.physique = state.basePhysique;
        data.initialTraits = new List<string>(state.traitIds ?? new List<string>());
        NPCRuntime runtime = new NPCRuntime(data, state);
        npcMap[data] = runtime;
        npcById[state.characterId] = runtime;
        runtimes.Add(runtime);
    }

    private static void NormalizeRestoredCharacter(CharacterState state)
    {
        if (state == null) return;
        state.traitIds = (state.traitIds ?? new List<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct()
            .ToList();
        state.relationships = (state.relationships ?? new List<RelationshipRecord>())
            .Where(item => item != null &&
                !string.IsNullOrWhiteSpace(item.sourceCharacterId) &&
                !string.IsNullOrWhiteSpace(item.targetCharacterId))
            .ToList();
        state.lifeRecords = state.lifeRecords ?? new List<LifeRecord>();
        state.realmLayer = Mathf.Clamp(state.realmLayer, 1, 3);
        state.naqiProgress = Mathf.Clamp(state.naqiProgress, 0f, 100f);
        state.currentAura = Mathf.Max(0f, state.currentAura);
        state.auraControl = Mathf.Clamp(state.auraControl, 0f, 100f);
        state.fatigue = Mathf.Clamp(state.fatigue, 0f, 100f);
        state.spiritRoot = state.spiritRoot ?? new SpiritRootData();
        SpiritRootRules.Normalize(state.spiritRoot);
        state.activeAuxiliaryTechniqueIds = state.activeAuxiliaryTechniqueIds ?? new List<string>();
        state.techniqueProgresses = state.techniqueProgresses ?? new List<PersonalTechniqueProgress>();
        foreach (PersonalTechniqueProgress progress in state.techniqueProgresses.Where(item => item != null))
            progress.understanding = Mathf.Clamp(progress.understanding, 0f, 100f);
    }

}
