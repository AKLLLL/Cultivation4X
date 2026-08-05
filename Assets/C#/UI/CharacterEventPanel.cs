using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CharacterEventPanel : MonoBehaviour
{
    private Canvas canvas;
    private RectTransform panel;
    private TMP_Text titleText;
    private TMP_Text bodyText;
    private RectTransform bodyContent;
    private RectTransform optionsContainer;
    private readonly List<Button> optionButtons = new List<Button>();
    private RectTransform inboxPanel;
    private Button inboxButton;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MapTestBootstrap.IsTestScene) return;
        if (FindObjectOfType<CharacterEventPanel>() == null)
            new GameObject("CharacterEventPanel").AddComponent<CharacterEventPanel>();
    }

    private void Awake()
    {
        BuildRuntimeUI();
        panel.gameObject.SetActive(false);
        inboxPanel.gameObject.SetActive(false);
    }

    private void Start()
    {
        if (EventManager.Instance != null)
        {
            EventManager.Instance.OnEventPresented += Show;
            EventManager.Instance.OnEventResolved += OnResolved;
        }
        RefreshInboxButton();
        ActiveCharacterEvent active = EventManager.Instance?.GetActiveEvent();
        if (active != null) Show(active);
    }

    private void OnDestroy()
    {
        if (EventManager.Instance != null)
            EventManager.Instance.OnEventPresented -= Show;
        if (EventManager.Instance != null)
            EventManager.Instance.OnEventResolved -= OnResolved;
    }

    private void Show(ActiveCharacterEvent active)
    {
        CloseManaged(inboxPanel.gameObject);
        OpenManaged(panel.gameObject);
        titleText.text = EventManager.Format(active.Definition.title, active.Participants);
        bodyText.text = EventManager.Format(active.Definition.body, active.Participants);
        RefreshBodyLayout();
        foreach (Button button in optionButtons) Destroy(button.gameObject);
        optionButtons.Clear();

        foreach (EventOptionDefinition option in active.Definition.options)
        {
            EventOptionDefinition captured = option;
            string eventId = active.Definition.id;
            Button button = CreateButton(optionsContainer, option.text);
            button.interactable = EventManager.Instance.IsOptionAvailable(option.id, out string reason);
            if (!button.interactable && !string.IsNullOrWhiteSpace(reason))
                button.GetComponentInChildren<TMP_Text>().text = $"{option.text}（{reason}）";
            button.onClick.AddListener(() =>
            {
                if (EventManager.Instance.ChooseOption(captured.id))
                {
                    CloseManaged(panel.gameObject);
                    RefreshInboxButton();
                    ExternalThreatPanel.TryOpenFromEvent(eventId, captured.id);
                }
            });
            optionButtons.Add(button);
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(optionsContainer);
    }

    private void BuildRuntimeUI()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        gameObject.AddComponent<GraphicRaycaster>();

        inboxButton = RuntimeUIFactory.Button(transform, "事件收件箱");
        RectTransform inboxButtonRect = inboxButton.GetComponent<RectTransform>();
        inboxButtonRect.anchorMin = inboxButtonRect.anchorMax = new Vector2(1, 1); inboxButtonRect.pivot = new Vector2(1, 1);
        inboxButtonRect.anchoredPosition = new Vector2(-15, -15); inboxButtonRect.sizeDelta = new Vector2(180, 45);
        inboxButton.onClick.AddListener(ShowInbox);
        inboxPanel = RuntimeUIFactory.Panel(transform, "Inbox", new Vector2(0.2f, 0.15f), new Vector2(0.8f, 0.85f));

        GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
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
        bodyContent = RuntimeUIFactory.ScrollContent(panel, "EventBodyScroll");
        bodyText = CreateText(bodyContent, 22, FontStyles.Normal);
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.enableWordWrapping = true;
        bodyText.overflowMode = TextOverflowModes.Overflow;
        LayoutElement bodyLayout = bodyText.GetComponent<LayoutElement>();
        bodyLayout.minHeight = 90f;
        bodyLayout.preferredHeight = 90f;
        bodyLayout.flexibleHeight = 0f;
        optionsContainer = CreateOptionsContainer(panel);
    }

    private void OnResolved(EventHistoryRecord _) => RefreshInboxButton();

    private void RefreshInboxButton()
    {
        if (inboxButton == null || EventManager.Instance == null) return;
        inboxButton.GetComponentInChildren<TMP_Text>().text = $"事件收件箱 ({EventManager.Instance.GetInbox().Count}/{EventManager.InboxCapacity})";
    }

    private void ShowInbox()
    {
        if (EventManager.Instance == null) return;
        CloseManaged(panel.gameObject);
        for (int i = inboxPanel.childCount - 1; i >= 0; i--) Destroy(inboxPanel.GetChild(i).gameObject);
        RuntimeUIFactory.Text(inboxPanel, "事件收件箱", 30, 48);
        foreach (EventInboxEntry entry in EventManager.Instance.GetInbox())
        {
            EventInboxEntry captured = entry;
            EventDefinition definition = EventManager.Instance.GetDefinitions().FirstOrDefault(item => item.id == entry.eventId);
            string expiry = entry.expiresDay < 0 ? "关键事件" : $"第 {entry.expiresDay} 天到期";
            Button button = RuntimeUIFactory.Button(inboxPanel, $"{definition?.title ?? entry.eventId}　{expiry}", 48);
            button.onClick.AddListener(() => EventManager.Instance.OpenInboxEntry(captured.entryId));
        }
        Button close = RuntimeUIFactory.Button(inboxPanel, "关闭"); close.onClick.AddListener(() => CloseManaged(inboxPanel.gameObject));
        OpenManaged(inboxPanel.gameObject);
        RefreshInboxButton();
    }

    private static void OpenManaged(GameObject target)
    {
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(target);
        else target.SetActive(true);
    }

    private static void CloseManaged(GameObject target)
    {
        if (target == null || !target.activeSelf) return;
        if (UIManager.Instance != null) UIManager.Instance.ClosePanel(target);
        else target.SetActive(false);
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

    private void RefreshBodyLayout()
    {
        if (bodyText == null || bodyContent == null) return;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        float availableWidth = bodyContent.rect.width;
        if (availableWidth <= 1f)
            availableWidth = Mathf.Max(160f, panel.rect.width - 60f);
        float preferredHeight = bodyText.GetPreferredValues(bodyText.text, availableWidth, 0f).y;
        LayoutElement bodyLayout = bodyText.GetComponent<LayoutElement>();
        float height = Mathf.Max(90f, preferredHeight + 24f);
        bodyLayout.minHeight = height;
        bodyLayout.preferredHeight = height;
        LayoutRebuilder.ForceRebuildLayoutImmediate(bodyContent);
        ScrollRect scroll = bodyContent.GetComponentInParent<ScrollRect>();
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
    }

    private static RectTransform CreateOptionsContainer(Transform parent)
    {
        GameObject obj = new GameObject("Options", typeof(RectTransform), typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        VerticalLayoutGroup layout = obj.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 10;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        obj.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        obj.GetComponent<LayoutElement>().flexibleHeight = 0;
        return obj.GetComponent<RectTransform>();
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
