using UnityEngine;

public static class DontDestroyUtility
{
    public static void MarkPersistent(GameObject target)
    {
        if (target == null) return;
        if (target.transform.parent != null) target.transform.SetParent(null);
        Object.DontDestroyOnLoad(target);
    }
}
