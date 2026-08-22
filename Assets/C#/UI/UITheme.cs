using TMPro;
using UnityEngine;

[CreateAssetMenu(menuName = "Cultivation4X/UI Theme", fileName = "DefaultUITheme")]
public sealed class UITheme : ScriptableObject
{
    public TMP_FontAsset font;
    public Color background = new Color(0.035f, 0.03f, 0.025f, 0.96f);
    public Color panel = new Color(0.075f, 0.065f, 0.05f, 0.98f);
    public Color card = new Color(0.12f, 0.10f, 0.075f, 0.96f);
    public Color text = new Color(0.93f, 0.90f, 0.82f, 1f);
    public Color secondaryText = new Color(0.72f, 0.68f, 0.60f, 1f);
    public Color accent = new Color(0.76f, 0.51f, 0.19f, 1f);
    public Color danger = new Color(0.78f, 0.24f, 0.18f, 1f);
    public Color disabled = new Color(0.34f, 0.32f, 0.29f, 1f);
    public int titleSize = 28;
    public int bodySize = 18;
    public int helperSize = 14;
    public int spacingSmall = 8;
    public int spacingMedium = 16;
    public int spacingLarge = 24;
}
