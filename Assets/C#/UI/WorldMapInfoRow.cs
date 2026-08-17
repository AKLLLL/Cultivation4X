using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 世界地图信息面板公共行组件：统一“标签：内容”的紧凑排版。
/// 环境页、地点页与后续资源/事件页都必须复用本组件，不得各自排版。
/// </summary>
public sealed class WorldMapInfoRow : MonoBehaviour
{
    private TMP_Text text;

    public static WorldMapInfoRow Create(Transform parent, string label, string content)
    {
        GameObject obj = new GameObject("WorldMapInfoRow",
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        WorldMapInfoRow row = obj.AddComponent<WorldMapInfoRow>();
        row.Set(label, content);
        return row;
    }

    public void Set(string label, string content)
    {
        EnsureText();
        text.text = $"{label ?? string.Empty}：{content ?? string.Empty}";
        text.fontSize = 14;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        LayoutElement layout = GetComponent<LayoutElement>();
        layout.minHeight = 18f;
        layout.preferredHeight = 20f;
        layout.flexibleHeight = 0f;
    }

    private void EnsureText()
    {
        if (text != null) return;
        text = GetComponent<TextMeshProUGUI>();
    }
}
