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
        CanvasScaler scaler = owner.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
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
        LayoutElement layout = obj.GetComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
        layout.flexibleHeight = 0;
        return text;
    }

    public static Button Button(Transform parent, string label, float height = 42)
    {
        GameObject obj = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<Image>().color = new Color(0.28f, 0.21f, 0.12f, 1f);
        LayoutElement layout = obj.GetComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
        layout.flexibleHeight = 0;
        TMP_Text text = Text(obj.transform, label, 18, height);
        text.alignment = TextAlignmentOptions.Center;
        RectTransform rect = text.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        return obj.GetComponent<Button>();
    }

    public static RectTransform TabBar(Transform parent, string name, float height = 44)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(0f, -height);
        rect.offsetMax = Vector2.zero;
        HorizontalLayoutGroup layout = obj.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 6;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        LayoutElement element = obj.GetComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleHeight = 0;
        return rect;
    }

    public static Button TabButton(Transform parent, string label, bool selected, float height = 42)
    {
        Button button = Button(parent, label, height);
        button.GetComponent<Image>().color = selected
            ? new Color(0.55f, 0.36f, 0.13f, 1f)
            : new Color(0.20f, 0.17f, 0.13f, 1f);
        LayoutElement layout = button.GetComponent<LayoutElement>();
        layout.flexibleWidth = 1;
        return button;
    }

    /// <summary>
    /// 新主界面使用的紧凑页签条。旧 TabBar 保持等分行为，避免改变兼容面板。
    /// </summary>
    public static RectTransform CompactTabBar(Transform parent, string name)
    {
        float height = UIComponentStyles.CompactTabBarHeight;
        GameObject obj = new GameObject(name, typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        HorizontalLayoutGroup layout = obj.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = UIComponentStyles.CompactTabSpacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        LayoutElement element = obj.GetComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleHeight = 0f;
        return obj.GetComponent<RectTransform>();
    }

    public static Button CompactTabButton(Transform parent, string label, bool selected)
    {
        Button button = Button(parent, label, UIComponentStyles.CompactTabBarHeight);
        button.GetComponent<Image>().color = selected
            ? UIComponentStyles.TabSelected
            : UIComponentStyles.TabNormal;
        LayoutElement element = button.GetComponent<LayoutElement>();
        element.minWidth = UIComponentStyles.CompactTabButtonWidth;
        element.preferredWidth = UIComponentStyles.CompactTabButtonWidth;
        element.flexibleWidth = 0f;
        element.minHeight = UIComponentStyles.CompactTabBarHeight;
        element.preferredHeight = UIComponentStyles.CompactTabBarHeight;
        element.flexibleHeight = 0f;
        return button;
    }

    public static RectTransform InfoCard(Transform parent, string name)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<Image>().color = UIComponentStyles.InfoCard;
        VerticalLayoutGroup layout = obj.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = UIComponentStyles.InfoCardSpacing;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        obj.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        obj.GetComponent<LayoutElement>().flexibleHeight = 0f;
        return obj.GetComponent<RectTransform>();
    }

    public static RectTransform ScrollContent(Transform parent, string name)
    {
        GameObject scrollObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        scrollObject.transform.SetParent(parent, false);
        scrollObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.12f);
        scrollObject.GetComponent<LayoutElement>().flexibleHeight = 1;

        GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewportObject.transform.SetParent(scrollObject.transform, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = viewport.offsetMax = Vector2.zero;
        viewportObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
        viewportObject.GetComponent<Mask>().showMaskGraphic = false;

        GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewport, false);
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        return content;
    }
}
