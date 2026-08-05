using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class LegacyWorldUiGate : MonoBehaviour
{
    private static readonly HashSet<string> HiddenSceneObjects = new HashSet<string>
    {
        "Plane",
        "1",
        "2",
        "3",
        "Button (Legacy)",
        "Button_AlchemyRoom",
        "Button_Sect",
        "Button_Warehouse",
        "Day"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MapTestBootstrap.IsTestScene) return;
        if (FindObjectOfType<LegacyWorldUiGate>() == null)
            new GameObject("LegacyWorldUiGate").AddComponent<LegacyWorldUiGate>();
    }

    private IEnumerator Start()
    {
        yield return null;
        foreach (GameObject target in Resources.FindObjectsOfTypeAll<GameObject>()
                     .Where(item => item != null && item.scene.IsValid() &&
                                    HiddenSceneObjects.Contains(item.name)))
            target.SetActive(false);
    }
}
