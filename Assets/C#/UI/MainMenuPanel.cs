using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public sealed class MainMenuPanel : MonoBehaviour
{
    private Canvas canvas;
    private RectTransform panel;
    private Button startButton;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MapTestBootstrap.IsTestScene) return;
        if (FindObjectOfType<MainMenuPanel>() != null) return;
        new GameObject("MainMenuPanel").AddComponent<MainMenuPanel>();
    }

    private void Awake()
    {
        canvas = RuntimeUIFactory.Canvas(gameObject, 980);
        canvas.gameObject.name = "MainMenuCanvas";
        panel = RuntimeUIFactory.Panel(canvas.transform, "MainMenu",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        RectTransform rect = panel;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(420f, 300f);
        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 16f;

        RuntimeUIFactory.Text(panel, "修仙宗门", 40, 64);
        RuntimeUIFactory.Text(panel, "洞府立宗 · 从零开始", 20, 40);
        startButton = RuntimeUIFactory.Button(panel, "开始新游戏", 52);
        startButton.onClick.AddListener(StartNewGame);
        panel.gameObject.SetActive(false);
    }

    private IEnumerator Start()
    {
        while (SaveManager.Instance == null || !SaveManager.Instance.IsInitializationComplete ||
               GameFlowStateManager.Instance == null)
            yield return null;
        GameFlowStateManager.Instance.StateChanged += OnFlowStateChanged;
        RefreshVisibility();
    }

    private void OnDestroy()
    {
        if (GameFlowStateManager.Instance != null)
            GameFlowStateManager.Instance.StateChanged -= OnFlowStateChanged;
    }

    private void OnFlowStateChanged(GameFlowState state) => RefreshVisibility();

    private void RefreshVisibility()
    {
        bool show = GameFlowStateManager.Instance != null &&
                    GameFlowStateManager.Instance.Current == GameFlowState.MainMenu &&
                    SaveManager.Instance != null &&
                    !SaveManager.Instance.InitializationFailed;
        panel.gameObject.SetActive(show);
    }

    private void StartNewGame()
    {
        SaveManager.Instance?.StartNewGame();
    }
}
