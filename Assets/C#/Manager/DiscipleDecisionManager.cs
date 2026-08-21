using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 弟子自主行为调度器（V1 唯一新增全局单例）。
///
/// 职责只限于调度：
/// 1. Settlement Growth Check（结算成长检查）：触发 TryBreakthrough，归属成长系统；
/// 2. 识别自主 Mission 终局证据，处理社交关系结果与完成后冷却；
/// 3. 对空闲弟子生成 Goal、评分并启动行动。
///
/// 计算在 DiscipleAIEvaluator，执行在 DiscipleMissionBridge，
/// 决策履历在 ExperienceGenerator，关系履历在 NPCManager。
/// </summary>
public class DiscipleDecisionManager : MonoBehaviour
{
    public static DiscipleDecisionManager Instance { get; private set; }

    private readonly Dictionary<string, DiscipleAIContext> contexts =
        new Dictionary<string, DiscipleAIContext>(StringComparer.Ordinal);

    private int lastProcessedDay = -1;
    private bool subscribedToTimeManager;
    private bool missionReferencesValidated;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MapTestBootstrap.IsTestScene) return;
        if (Instance == null) new GameObject("DiscipleDecisionManager").AddComponent<DiscipleDecisionManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyUtility.MarkPersistent(gameObject);
        TrySubscribe();
    }

    private void Start() => TrySubscribe();

    private void OnDestroy()
    {
        Unsubscribe();
        if (Instance == this) Instance = null;
    }

    private void TrySubscribe()
    {
        if (subscribedToTimeManager || TimeManager.Instance == null) return;
        TimeManager.Instance.OnDaySettlementReady += OnDaySettlementReady;
        subscribedToTimeManager = true;
    }

    private void Unsubscribe()
    {
        if (!subscribedToTimeManager || TimeManager.Instance == null) return;
        TimeManager.Instance.OnDaySettlementReady -= OnDaySettlementReady;
        subscribedToTimeManager = false;
    }

    private void OnDaySettlementReady(DaySettlementSummary summary)
    {
        ProcessSettledDay(TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay);
    }

    /// <summary>公开入口，便于测试与后续把触发点接到其他结算流程。</summary>
    public void ProcessSettledDay(int day)
    {
        if (day <= 0 || lastProcessedDay >= day) return;
        lastProcessedDay = day;

        if (!IsAutonomyActive()) return;
        ValidateMissionReferencesOnce();

        try
        {
            DiscipleAIConfig config = DiscipleAIConfigLoader.Load();
            IdentityDefinition identity = config.Identities.FirstOrDefault(item => item != null && item.autonomyEnabled);
            if (identity == null)
            {
                DiscipleAIDebug.LogWarning("没有启用自主行为的身份配置");
                return;
            }

            List<NPCRuntime> disciples = NPCManager.Instance.GetLivingNPC()
                .Where(npc => npc != null && npc.Character != null && npc.Character.hasGeneratedProfile)
                .ToList();
            if (disciples.Count == 0) return;

            RunSettlementGrowthCheck(disciples);
            ProcessEndedAutonomousMissions(disciples, day);
            ProcessDecisions(disciples, config, identity, day);

            CleanupContexts(disciples);
            SaveManager.Instance?.AutoSave();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[DiscipleAI] 结算处理失败: {exception}");
        }
    }

    private void RunSettlementGrowthCheck(List<NPCRuntime> disciples)
    {
        // Settlement Growth Check：成长系统自决阈值与成败记录，AI 不读结果、不写记录。
        foreach (NPCRuntime npc in disciples)
        {
            if (npc.State != NPCState.Idle || npc.CurrentMission != null) continue;
            npc.TryBreakthrough();
        }
    }

    private void ProcessEndedAutonomousMissions(List<NPCRuntime> disciples, int day)
    {
        foreach (NPCRuntime npc in disciples)
        {
            DiscipleAIContext context = GetOrCreateContext(npc);
            if (DiscipleMissionBridge.TryProcessCompletedSocial(npc, day))
            {
                context.LastActionEndedDay = day;
                continue;
            }
            if (DiscipleMissionBridge.HadAutonomousMissionEndToday(npc, day))
                context.LastActionEndedDay = day;
        }
    }

    private void ProcessDecisions(List<NPCRuntime> disciples, DiscipleAIConfig config,
        IdentityDefinition identity, int day)
    {
        foreach (NPCRuntime npc in disciples)
        {
            if (npc.State != NPCState.Idle || npc.CurrentMission != null) continue;

            DiscipleAIContext context = GetOrCreateContext(npc);
            if (context.LastActionEndedDay == day) continue;              // 完成后冷却
            if (ExperienceGenerator.HasDecisionRecordOn(npc, day)) continue; // 读档/重复结算幂等

            context.Goals = DiscipleAIEvaluator.GenerateGoals(npc, config.Goals, context.Goals);
            context.LastScores = DiscipleAIEvaluator.EvaluateActions(
                npc, identity, context.Goals, config.Actions, day);
            DiscipleDecisionResult decision = DiscipleAIEvaluator.ChooseAction(context.LastScores);
            DiscipleAIDebug.LogDecision(npc, decision);

            if (decision.Selected == null)
            {
                DiscipleAIDebug.Log($"{npc.Character.displayName} 无可用自主行动");
                continue;
            }

            ExperienceGenerator.WriteDecisionRecord(npc, decision.Selected, decision.ReasonLabel, day);
            Mission mission = DiscipleMissionBridge.StartAutonomousMission(decision.Selected, npc, out string reason);
            if (mission == null)
            {
                Debug.LogWarning($"[DiscipleAI] {npc.Character.displayName} 自主行动启动失败: {reason}");
                continue;
            }

            context.LastAction = decision.Selected;
            context.LastDecisionDay = day;
            DiscipleAIDebug.Log($"{npc.Character.displayName} 已决定: {decision.Selected.displayName}");
        }
    }

    private DiscipleAIContext GetOrCreateContext(NPCRuntime npc)
    {
        if (!contexts.TryGetValue(npc.CharacterId, out DiscipleAIContext context))
        {
            context = new DiscipleAIContext(npc.CharacterId);
            contexts.Add(npc.CharacterId, context);
        }
        return context;
    }

    private void CleanupContexts(List<NPCRuntime> activeDisciples)
    {
        HashSet<string> activeIds = new HashSet<string>(
            activeDisciples.Select(npc => npc.CharacterId), StringComparer.Ordinal);
        foreach (string staleId in contexts.Keys.Where(id => !activeIds.Contains(id)).ToList())
            contexts.Remove(staleId);
    }

    private bool IsAutonomyActive()
    {
        FoundingState founding = PlayerManager.Instance?.playerData?.founding;
        return GameFlowPermission.IsSectEstablished(founding) &&
            NPCManager.Instance != null && MissionManager.Instance != null &&
            DiscipleAIConfigLoader.Load().Actions.Count > 0;
    }

    private void ValidateMissionReferencesOnce()
    {
        if (missionReferencesValidated) return;
        missionReferencesValidated = true;
        foreach (string missing in DiscipleAIConfigLoader.FindMissingMissionReferences())
            Debug.LogError($"[DiscipleAI] Action 引用的 Mission 模板不存在: {missing}");
    }
}
