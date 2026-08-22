using UnityEngine;

public static class UIRootBootstrap
{
    private const string ResourcePath = "Prefab/UI/UIRoot";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (UIManager.Instance != null) return;
        GameObject prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogError($"未找到 Resources/{ResourcePath}.prefab，UI Shell 无法初始化。");
            return;
        }
        Object.Instantiate(prefab).name = "UIRoot";
    }
}
