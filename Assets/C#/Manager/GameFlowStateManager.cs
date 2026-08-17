using System;
using System.Collections;
using UnityEngine;

public enum GameFlowState
{
    /// <summary>无存档且尚未开始新游戏。</summary>
    MainMenu,

    /// <summary>角色创建：选弟子 → 选功法 → 确认宗门名称。</summary>
    CharacterSetup,

    /// <summary>宗门选址：2D 六角选址视图。</summary>
    SectPlacement,

    /// <summary>3D 世界地图与常规宗门经营。</summary>
    WorldMap
}

/// <summary>
/// 全局游戏流程状态。只负责“当前处于哪一段流程”，不保存地图、角色或宗门数据；
/// 状态由 SaveManager 的会话状态 + PlayerManager.founding.stage 推导。
/// </summary>
public sealed class GameFlowStateManager : MonoBehaviour
{
    public static GameFlowStateManager Instance { get; private set; }

    public GameFlowState Current { get; private set; } = GameFlowState.MainMenu;
    public event Action<GameFlowState> StateChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MapTestBootstrap.IsTestScene) return;
        if (Instance != null) return;
        new GameObject("GameFlowStateManager").AddComponent<GameFlowStateManager>();
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
    }

    private IEnumerator Start()
    {
        while (SaveManager.Instance == null || !SaveManager.Instance.IsInitializationComplete)
            yield return null;
        SaveManager.Instance.OnInitializationCompleted += OnInitializationCompleted;
        if (PlayerManager.Instance != null)
            PlayerManager.Instance.OnFoundingChanged += OnFoundingChanged;
        Refresh();
    }

    private void OnDestroy()
    {
        if (SaveManager.Instance != null)
            SaveManager.Instance.OnInitializationCompleted -= OnInitializationCompleted;
        if (PlayerManager.Instance != null)
            PlayerManager.Instance.OnFoundingChanged -= OnFoundingChanged;
        if (Instance == this) Instance = null;
    }

    private void OnInitializationCompleted(bool loadedExistingSave) => Refresh();
    private void OnFoundingChanged() => Refresh();

    public void Refresh()
    {
        GameFlowState next = GameFlowState.MainMenu;
        if (SaveManager.Instance != null && SaveManager.Instance.HasGameSession)
            next = StateForFounding(PlayerManager.Instance?.playerData?.founding);
        SetState(next);
    }

    public static GameFlowState StateForFounding(FoundingState founding)
    {
        if (founding == null || !founding.initialized) return GameFlowState.MainMenu;
        switch (founding.stage)
        {
            case FoundingStage.CandidateSelection:
            case FoundingStage.TechniqueSelection:
            case FoundingStage.SectConfirmation:
                return GameFlowState.CharacterSetup;
            case FoundingStage.WorldSelection:
                return GameFlowState.SectPlacement;
            case FoundingStage.Cave:
            case FoundingStage.Completed:
                return GameFlowState.WorldMap;
            default:
                return GameFlowState.MainMenu;
        }
    }

    private void SetState(GameFlowState next)
    {
        if (Current == next) return;
        GameFlowState previous = Current;
        Current = next;
        GameDebugConfig.LogWorldMap($"[GameFlowDiag] GameFlowState {previous} -> {next} " +
                  $"hasGameSession={(SaveManager.Instance != null && SaveManager.Instance.HasGameSession)}");
        StateChanged?.Invoke(Current);
    }
}
