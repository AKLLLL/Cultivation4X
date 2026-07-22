using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class RuntimeUIFactory
{
    public static Canvas Canvas(GameObject owner, int order)
    {
        Canvas canvas = owner.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        owner.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        owner.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    public static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
        obj.GetComponent<Image>().color = new Color(0.08f, 0.07f, 0.06f, 0.96f);
        VerticalLayoutGroup layout = obj.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 16, 16); layout.spacing = 8; layout.childForceExpandHeight = false;
        return rect;
    }

    public static TMP_Text Text(Transform parent, string value, int size = 20, float height = 40)
    {
        GameObject obj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        TMP_Text text = obj.GetComponent<TMP_Text>();
        text.text = value; text.fontSize = size; text.color = Color.white; text.enableWordWrapping = true;
        obj.GetComponent<LayoutElement>().preferredHeight = height;
        return text;
    }

    public static Button Button(Transform parent, string label, float height = 42)
    {
        GameObject obj = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<Image>().color = new Color(0.28f, 0.21f, 0.12f, 1f);
        obj.GetComponent<LayoutElement>().preferredHeight = height;
        TMP_Text text = Text(obj.transform, label, 18, height);
        text.alignment = TextAlignmentOptions.Center;
        RectTransform rect = text.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        return obj.GetComponent<Button>();
    }
}
