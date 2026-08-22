using System;
using UnityEngine;

public enum UIWindowLayer { HUD, Screen, Modal, Overlay }
public enum UIEscapePolicy { Allowed, Blocked }
public enum UIWindowId { DiscipleCenter }

public interface IUIWindowContext { }

public interface IUIWindowLifecycle
{
    void OnOpened(IUIWindowContext context);
    void OnFocusGained();
    void OnFocusLost();
    void OnClosed();
}

[Serializable]
public sealed class UIWindowRegistration
{
    public UIWindowId id;
    public string title;
    public UIWindowLayer layer = UIWindowLayer.Screen;
    public UIEscapePolicy escapePolicy = UIEscapePolicy.Allowed;
    public bool blocksWorldInput = true;
    public bool cacheInstance = true;
    public GameObject prefab;
}

public class UIWindowView : MonoBehaviour, IUIWindowLifecycle
{
    public virtual void OnOpened(IUIWindowContext context) { }
    public virtual void OnFocusGained() { }
    public virtual void OnFocusLost() { }
    public virtual void OnClosed() { }
}
