using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MonthlyPlanPanel : UIWindowView
{
    [SerializeField] private RectTransform templateList;
    [SerializeField] private TMP_Text emptyTemplateText;
    [SerializeField] private TMP_InputField nameInput;
    [SerializeField] private RectTransform calendarGrid;
    [SerializeField] private TMP_Text planSummaryText;
    [SerializeField] private RectTransform discipleList;
    [SerializeField] private TMP_Text bindingSummaryText;
    [SerializeField] private GameObject disciplePickerRoot;
    [SerializeField] private RectTransform disciplePickerList;
    [SerializeField] private Button createButton;
    [SerializeField] private Button copyButton;
    [SerializeField] private Button renameButton;
    [SerializeField] private Button deleteButton;
    [SerializeField] private Button trainingBrushButton;
    [SerializeField] private Button dutyBrushButton;
    [SerializeField] private Button freeBrushButton;
    [SerializeField] private Button addDiscipleButton;
    [SerializeField] private TMP_FontAsset font;

    private readonly List<GameObject> generated = new List<GameObject>();
    private string selectedTemplateId;
    private MonthlyActivityType paintType = MonthlyActivityType.Training;
    private bool disciplePickerOpen;

    public void Configure(RectTransform templates, TMP_Text emptyTemplates, TMP_InputField templateName,
        RectTransform calendar, TMP_Text planSummary, RectTransform disciples, TMP_Text bindingSummary,
        GameObject pickerRoot, RectTransform pickerList, Button create, Button copy, Button rename,
        Button delete, Button trainingBrush, Button dutyBrush, Button freeBrush, Button addDisciple,
        TMP_FontAsset uiFont)
    {
        templateList = templates;
        emptyTemplateText = emptyTemplates;
        nameInput = templateName;
        calendarGrid = calendar;
        planSummaryText = planSummary;
        discipleList = disciples;
        bindingSummaryText = bindingSummary;
        disciplePickerRoot = pickerRoot;
        disciplePickerList = pickerList;
        createButton = create;
        copyButton = copy;
        renameButton = rename;
        deleteButton = delete;
        trainingBrushButton = trainingBrush;
        dutyBrushButton = dutyBrush;
        freeBrushButton = freeBrush;
        addDiscipleButton = addDisciple;
        font = uiFont;
    }

    private void Awake()
    {
        createButton?.onClick.AddListener(CreateTemplate);
        copyButton?.onClick.AddListener(CopyTemplate);
        renameButton?.onClick.AddListener(RenameTemplate);
        deleteButton?.onClick.AddListener(DeleteTemplate);
        trainingBrushButton?.onClick.AddListener(() => SelectBrush(MonthlyActivityType.Training));
        dutyBrushButton?.onClick.AddListener(() => SelectBrush(MonthlyActivityType.SectDuty));
        freeBrushButton?.onClick.AddListener(() => SelectBrush(MonthlyActivityType.Free));
        addDiscipleButton?.onClick.AddListener(ToggleDisciplePicker);
    }

    public override void OnOpened(IUIWindowContext context)
    {
        EnsureSelection();
        Refresh();
    }

    public override void OnFocusGained() => Refresh();

    public override void OnClosed()
    {
        disciplePickerOpen = false;
        MonthlyPlanDayCell.CancelDrag();
        SaveManager.Instance?.AutoSave();
    }

    private void CreateTemplate()
    {
        MonthlyPlanTemplate template = MonthlyPlanRules.CreateTemplate();
        selectedTemplateId = template?.id;
        disciplePickerOpen = false;
        Refresh();
    }

    private void CopyTemplate()
    {
        MonthlyPlanTemplate template = MonthlyPlanRules.CopyTemplate(selectedTemplateId);
        selectedTemplateId = template?.id ?? selectedTemplateId;
        disciplePickerOpen = false;
        Refresh();
    }

    private void RenameTemplate()
    {
        MonthlyPlanRules.RenameTemplate(selectedTemplateId, nameInput?.text);
        Refresh();
    }

    private void DeleteTemplate()
    {
        if (!MonthlyPlanRules.DeleteTemplate(selectedTemplateId)) return;
        selectedTemplateId = null;
        disciplePickerOpen = false;
        EnsureSelection();
        Refresh();
    }

    private void SelectBrush(MonthlyActivityType activity)
    {
        paintType = activity;
        RefreshBrushState();
        RefreshPlanSummary();
    }

    private void ToggleDisciplePicker()
    {
        if (SelectedTemplate() == null) return;
        disciplePickerOpen = !disciplePickerOpen;
        Refresh();
    }

    private void EnsureSelection()
    {
        if (MonthlyPlanRules.GetTemplate(selectedTemplateId) == null)
            selectedTemplateId = MonthlyPlanRules.GetTemplates().FirstOrDefault(item => item != null)?.id;
    }

    private MonthlyPlanTemplate SelectedTemplate() => MonthlyPlanRules.GetTemplate(selectedTemplateId);

    private void Refresh()
    {
        ClearGenerated();
        EnsureSelection();
        MonthlyPlanTemplate selected = SelectedTemplate();
        IReadOnlyList<MonthlyPlanTemplate> templates = MonthlyPlanRules.GetTemplates();
        if (emptyTemplateText != null) emptyTemplateText.gameObject.SetActive(templates.Count == 0);

        foreach (MonthlyPlanTemplate template in templates.Where(item => item != null))
        {
            MonthlyPlanTemplate captured = template;
            Button button = CreateRuntimeButton(templateList,
                $"{(template.id == selectedTemplateId ? "●" : "○")} {template.name}\n绑定弟子：{template.discipleIds?.Count ?? 0}", 58f);
            button.onClick.AddListener(() =>
            {
                selectedTemplateId = captured.id;
                disciplePickerOpen = false;
                Refresh();
            });
        }

        bool hasTemplate = selected != null;
        if (nameInput != null)
        {
            nameInput.interactable = hasTemplate;
            nameInput.SetTextWithoutNotify(selected?.name ?? string.Empty);
        }
        SetInteractable(copyButton, hasTemplate);
        SetInteractable(renameButton, hasTemplate);
        SetInteractable(deleteButton, hasTemplate);
        SetInteractable(trainingBrushButton, hasTemplate);
        SetInteractable(dutyBrushButton, hasTemplate);
        SetInteractable(freeBrushButton, hasTemplate);
        SetInteractable(addDiscipleButton, hasTemplate);
        if (!hasTemplate) disciplePickerOpen = false;
        if (disciplePickerRoot != null) disciplePickerRoot.SetActive(hasTemplate && disciplePickerOpen);
        RefreshBrushState();

        if (!hasTemplate)
        {
            if (planSummaryText != null) planSummaryText.text = "新建计划模板后，可编辑30日循环日程。";
            if (bindingSummaryText != null) bindingSummaryText.text = "已绑定：0 / 无限制";
            return;
        }

        MonthlyPlanRules.Normalize(selected);
        RefreshPlanSummary();
        for (int index = 0; index < MonthlyPlanRules.DaysPerMonth; index++)
        {
            int day = index + 1;
            CreateDayCell(calendarGrid, day, selected.days[index]);
        }
        RenderDiscipleBindings(selected);
    }

    private void RenderDiscipleBindings(MonthlyPlanTemplate selected)
    {
        List<NPCRuntime> disciples = NPCManager.Instance?.GetLivingNPC() ?? new List<NPCRuntime>();
        if (bindingSummaryText != null)
            bindingSummaryText.text = $"已绑定：{selected.discipleIds.Count} / 无限制";

        foreach (NPCRuntime npc in disciples.Where(item => selected.discipleIds.Contains(item.CharacterId)))
        {
            NPCRuntime captured = npc;
            CreateBindingRow(discipleList, DisplayName(npc), "移除", () =>
            {
                MonthlyPlanRules.UnbindDisciple(selected.id, captured.CharacterId);
                Refresh();
            });
        }

        if (!disciplePickerOpen) return;
        foreach (NPCRuntime npc in disciples.Where(item => !selected.discipleIds.Contains(item.CharacterId)))
        {
            NPCRuntime captured = npc;
            MonthlyPlanTemplate current = MonthlyPlanRules.GetTemplateFor(npc.CharacterId);
            string suffix = current == null ? string.Empty : $"（当前：{current.name}）";
            Button candidate = CreateRuntimeButton(disciplePickerList, $"{DisplayName(npc)}{suffix}", 42f);
            candidate.onClick.AddListener(() =>
            {
                MonthlyPlanRules.BindDisciple(selected.id, captured.CharacterId);
                disciplePickerOpen = false;
                Refresh();
            });
        }
    }

    private void PaintDay(MonthlyPlanDayCell cell, int day)
    {
        if (!MonthlyPlanRules.TrySetDay(SelectedTemplate(), day, paintType, out _)) return;
        cell.Render(paintType, ActivityColor(paintType), ActivityName(paintType));
    }

    private void CompletePaintStroke()
    {
        RefreshPlanSummary();
        SaveManager.Instance?.AutoSave();
    }

    private void RefreshPlanSummary()
    {
        MonthlyPlanTemplate selected = SelectedTemplate();
        if (planSummaryText == null || selected == null) return;
        MonthlyPlanRules.Normalize(selected);
        int training = selected.days.Count(item => item == MonthlyActivityType.Training);
        int duty = selected.days.Count(item => item == MonthlyActivityType.SectDuty);
        int free = selected.days.Count - training - duty;
        planSummaryText.text = $"30日循环　修炼 {training} / 宗务 {duty} / 自由 {free}　当前画笔：{ActivityName(paintType)}";
    }

    private void RefreshBrushState()
    {
        SetBrushSelected(trainingBrushButton, paintType == MonthlyActivityType.Training);
        SetBrushSelected(dutyBrushButton, paintType == MonthlyActivityType.SectDuty);
        SetBrushSelected(freeBrushButton, paintType == MonthlyActivityType.Free);
    }

    private GameObject CreateDayCell(Transform parent, int day, MonthlyActivityType activity)
    {
        GameObject root = new GameObject($"Day{day:00}", typeof(RectTransform), typeof(Image),
            typeof(LayoutElement), typeof(MonthlyPlanDayCell));
        root.transform.SetParent(parent, false);
        Image background = root.GetComponent<Image>();
        background.color = ActivityColor(activity);
        LayoutElement size = root.GetComponent<LayoutElement>();
        size.minHeight = 64f;
        size.preferredHeight = 64f;
        size.flexibleHeight = 0f;
        TMP_Text text = CreateRuntimeText(root.transform, $"{day}\n{ActivityName(activity)}", 15f);
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(3f, 3f);
        textRect.offsetMax = new Vector2(-3f, -3f);
        text.alignment = TextAlignmentOptions.Center;
        MonthlyPlanDayCell behavior = root.GetComponent<MonthlyPlanDayCell>();
        behavior.Configure(day, background, text, PaintDay, CompletePaintStroke);
        generated.Add(root);
        return root;
    }

    private void CreateBindingRow(Transform parent, string name, string actionLabel, UnityEngine.Events.UnityAction action)
    {
        GameObject row = new GameObject("BoundDisciple", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        LayoutElement rowSize = row.GetComponent<LayoutElement>();
        rowSize.preferredHeight = 42f;
        rowSize.flexibleHeight = 0f;
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.padding = new RectOffset(8, 4, 2, 2);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        TMP_Text label = CreateRuntimeText(row.transform, name, 15f);
        LayoutElement labelSize = label.gameObject.AddComponent<LayoutElement>();
        labelSize.flexibleWidth = 1f;
        labelSize.preferredHeight = 38f;
        Button remove = CreateRuntimeButton(row.transform, actionLabel, 36f, false);
        LayoutElement removeSize = remove.GetComponent<LayoutElement>();
        removeSize.minWidth = 62f;
        removeSize.preferredWidth = 62f;
        removeSize.flexibleWidth = 0f;
        remove.onClick.AddListener(action);
        generated.Add(row);
    }

    private Button CreateRuntimeButton(Transform parent, string label, float height, bool trackRoot = true)
    {
        GameObject root = new GameObject("Item", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        root.transform.SetParent(parent, false);
        root.GetComponent<Image>().color = UIComponentStyles.TabNormal;
        LayoutElement rootSize = root.GetComponent<LayoutElement>();
        rootSize.minHeight = height;
        rootSize.preferredHeight = height;
        rootSize.flexibleHeight = 0f;
        TMP_Text text = CreateRuntimeText(root.transform, label, 15f);
        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(10f, 4f);
        rect.offsetMax = new Vector2(-10f, -4f);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        if (trackRoot) generated.Add(root);
        return root.GetComponent<Button>();
    }

    private TMP_Text CreateRuntimeText(Transform parent, string value, float size)
    {
        GameObject root = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        root.transform.SetParent(parent, false);
        TMP_Text text = root.GetComponent<TMP_Text>();
        text.font = font;
        text.fontSize = size;
        text.color = new Color(0.88f, 0.87f, 0.78f, 1f);
        text.text = value;
        text.raycastTarget = false;
        return text;
    }

    private void ClearGenerated()
    {
        foreach (GameObject item in generated)
            if (item != null)
            {
                item.SetActive(false);
                if (Application.isPlaying) Destroy(item);
                else DestroyImmediate(item);
            }
        generated.Clear();
    }

    private static string DisplayName(NPCRuntime npc) =>
        string.IsNullOrWhiteSpace(npc?.Character?.displayName) ? npc?.Data?.npcName ?? "无名弟子" : npc.Character.displayName;

    private static void SetInteractable(Button button, bool value)
    {
        if (button != null) button.interactable = value;
    }

    private static void SetBrushSelected(Button button, bool selected)
    {
        Image image = button?.GetComponent<Image>();
        if (image != null) image.color = selected ? UIComponentStyles.TabSelected : UIComponentStyles.TabNormal;
    }

    private static Color ActivityColor(MonthlyActivityType activity)
    {
        switch (activity)
        {
            case MonthlyActivityType.Training: return new Color(0.18f, 0.32f, 0.20f, 1f);
            case MonthlyActivityType.SectDuty: return new Color(0.16f, 0.29f, 0.35f, 1f);
            default: return new Color(0.39f, 0.30f, 0.12f, 1f);
        }
    }

    private static string ActivityName(MonthlyActivityType activity)
    {
        switch (activity)
        {
            case MonthlyActivityType.Training: return "修炼";
            case MonthlyActivityType.SectDuty: return "宗务";
            default: return "自由";
        }
    }
}
