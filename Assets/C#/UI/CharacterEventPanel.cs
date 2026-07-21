using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CharacterEventPanel : MonoBehaviour
{
    private Canvas canvas;
    private RectTransform panel;
    private TMP_Text titleText;
    private TMP_Text bodyText;
    private readonly List<Button> optionButtons = new List<Button>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<CharacterEventPanel>() == null)
            new GameObject("CharacterEventPanel").AddComponent<CharacterEventPanel>();
    }

    private void Awake()
    {
        BuildRuntimeUI();
        panel.gameObject.SetActive(false);
    }

    private void Start()
    {
        if (EventManager.Instance != null)
            EventManager.Instance.OnEventPresented += Show;
        ActiveCharacterEvent active = EventManager.Instance?.GetActiveEvent();
        if (active != null) Show(active);
    }

    private void OnDestroy()
    {
        if (EventManager.Instance != null)
            EventManager.Instance.OnEventPresented -= Show;
    }

    private void Show(ActiveCharacterEvent active)
    {
        panel.gameObject.SetActive(true);
        titleText.text = EventManager.Format(active.Definition.title, active.Participants);
        bodyText.text = EventManager.Format(active.Definition.body, active.Participants);
        foreach (Button button in optionButtons) Destroy(button.gameObject);
        optionButtons.Clear();

        foreach (EventOptionDefinition option in active.Definition.options)
        {
            EventOptionDefinition captured = option;
            Button button = CreateButton(panel, option.text);
            button.interactable = EventManager.Instance.IsOptionAvailable(option.id, out string reason);
            if (!button.interactable && !string.IsNullOrWhiteSpace(reason))
                button.GetComponentInChildren<TMP_Text>().text = $"{option.text}（{reason}）";
            button.onClick.AddListener(() =>
            {
                if (EventManager.Instance.ChooseOption(captured.id)) panel.gameObject.SetActive(false);
            });
            optionButtons.Add(button);
        }
    }

    private void BuildRuntimeUI()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        gameObject.AddComponent<GraphicRaycaster>();

        GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panelObject.transform.SetParent(transform, false);
        panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = new Vector2(0.2f, 0.15f);
        panel.anchorMax = new Vector2(0.8f, 0.85f);
        panel.offsetMin = panel.offsetMax = Vector2.zero;
        panelObject.GetComponent<Image>().color = new Color(0.08f, 0.07f, 0.06f, 0.97f);
        VerticalLayoutGroup layout = panelObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(30, 30, 25, 25);
        layout.spacing = 16;
        layout.childForceExpandHeight = false;
        titleText = CreateText(panel, 32, FontStyles.Bold);
        bodyText = CreateText(panel, 22, FontStyles.Normal);
    }

    private static TMP_Text CreateText(Transform parent, int size, FontStyles style)
    {
        GameObject obj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        TMP_Text text = obj.GetComponent<TMP_Text>();
        text.fontSize = size;
        text.fontStyle = style;
        text.color = Color.white;
        text.enableWordWrapping = true;
        obj.GetComponent<LayoutElement>().preferredHeight = size * 3;
        return text;
    }

    private static Button CreateButton(Transform parent, string label)
    {
        GameObject obj = new GameObject("Option", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<Image>().color = new Color(0.25f, 0.2f, 0.12f, 1f);
        obj.GetComponent<LayoutElement>().preferredHeight = 54;
        TMP_Text text = CreateText(obj.transform, 20, FontStyles.Normal);
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return obj.GetComponent<Button>();
    }
}
