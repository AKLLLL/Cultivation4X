using System;
using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class WorldInfoPanel : MonoBehaviour
{
    private Canvas canvas;
    private RectTransform panel;
    private TMP_Text titleText;
    private RectTransform tabBar;
    private RectTransform content;
    private Button closeButton;
    private readonly List<Button> tabButtons = new List<Button>();
    private readonly List<GameObject> dynamicContent = new List<GameObject>();

    private WorldMap map;
    private WorldCell cell;
    private WorldLocation location;
    private VillageState villageState;
    private int selectedTab;

    public bool IsOpen => panel != null && panel.gameObject.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MapTestBootstrap.IsTestScene) return;
        if (FindObjectOfType<WorldInfoPanel>() != null) return;
        new GameObject("WorldInfoPanel").AddComponent<WorldInfoPanel>();
    }

    private void Awake()
    {
        canvas = RuntimeUIFactory.Canvas(gameObject, 1200);
        panel = RuntimeUIFactory.Panel(canvas.transform, "WorldInfoPanel",
            new Vector2(0.70f, 0f), new Vector2(0.99f, 1f));
        panel.offsetMin = new Vector2(0f, 12f);
        panel.offsetMax = new Vector2(0f, -64f);
        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 10);
        layout.spacing = 6f;

        titleText = RuntimeUIFactory.Text(panel, "世界信息", 24, 38);
        tabBar = RuntimeUIFactory.TabBar(panel, "WorldInfoTabs", 44);
        tabButtons.Add(RuntimeUIFactory.TabButton(tabBar, "环境", true));
        tabButtons.Add(RuntimeUIFactory.TabButton(tabBar, "地点", false));
        tabButtons.Add(RuntimeUIFactory.TabButton(tabBar, "行动", false));
        for (int index = 0; index < tabButtons.Count; index++)
        {
            int captured = index;
            tabButtons[index].onClick.AddListener(() => SelectTab(captured));
        }
        content = RuntimeUIFactory.ScrollContent(panel, "WorldInfoContent");
        closeButton = RuntimeUIFactory.Button(panel, "关闭", 40);
        closeButton.onClick.AddListener(Close);
        panel.gameObject.SetActive(false);
    }

    public void Open(WorldMap worldMap, int cellIndex, VillageState village)
    {
        if (worldMap?.cells == null || cellIndex < 0 || cellIndex >= worldMap.cells.Length)
            return;
        map = worldMap;
        cell = worldMap.cells[cellIndex];
        location = map.GetLocationAt(cell);
        villageState = village;
        selectedTab = 0;
        RefreshTabs();
        RefreshContent();
        if (panel != null && !panel.gameObject.activeSelf)
            OpenManaged();
        else if (panel != null && panel.gameObject.activeSelf)
            OpenManaged();
    }

    public void Close()
    {
        if (panel == null || !panel.gameObject.activeSelf) return;
        CloseManaged();
    }

    private void OpenManaged()
    {
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(panel.gameObject, Close);
        else panel.gameObject.SetActive(true);
    }

    private void CloseManaged()
    {
        if (UIManager.Instance != null) UIManager.Instance.ClosePanel(panel.gameObject);
        else panel.gameObject.SetActive(false);
    }

    private void SelectTab(int index)
    {
        if (index < 0 || index >= tabButtons.Count || index == selectedTab) return;
        selectedTab = index;
        RefreshTabs();
        RefreshContent();
    }

    private void RefreshTabs()
    {
        for (int index = 0; index < tabButtons.Count; index++)
            tabButtons[index].GetComponent<Image>().color = index == selectedTab
                ? new Color(0.55f, 0.36f, 0.13f, 1f)
                : new Color(0.20f, 0.17f, 0.13f, 1f);
    }

    private void RefreshContent()
    {
        if (content == null) return;
        ClearDynamicContent();
        titleText.text = selectedTab switch
        {
            0 => "环境",
            1 => "地点",
            _ => "行动"
        };
        if (selectedTab == 0) ShowEnvironment();
        else if (selectedTab == 1) ShowLocation();
        else ShowActions();
    }

    private void ShowEnvironment()
    {
        if (cell == null)
        {
            AddText("无格子数据");
            return;
        }
        string terrain = WorldMapCellDetailsFormatter.LandformLabel(cell.landform);
        AddText($"地形\n{terrain}");
        AddText($"气候\n{ClimateLabel(cell)}");
        AddText($"灵气浓度\n{cell.totalAura:0.000}");
        AddText($"资源潜力\n{ResourcePotentialLabel(cell.totalAura)}");
    }

    private void ShowLocation()
    {
        if (location == null)
        {
            AddText("暂无地点");
            return;
        }

        AddText(location.name);
        AddText($"类型\n{LocationTypeLabel(location.type)}");
        if (location.type == LocationType.Village)
        {
            VillageState village = villageState ?? new VillageState();
            AddText($"人口\n{village.population}");
            AddText($"关系\n{VillageRelationLabel(village.relation)}");
            AddText($"劳动力\n{village.totalLabor - village.reservedLabor}/{village.totalLabor}");
        }
        else if (location.type == LocationType.Sect)
        {
            AddText($"等级\n{location.level}");
        }
    }

    private void ShowActions()
    {
        if (location == null || location.availableActions == null ||
            location.availableActions.Count == 0)
        {
            AddText("暂无可执行行动\n\n未来支持：\n建设\n改造\n开发");
            return;
        }
        foreach (LocationAction action in location.availableActions)
        {
            if (action == null) continue;
            AddText($"{action.displayName}\n消耗：{action.cost}");
        }
    }

    private void AddText(string value)
    {
        GameObject textObject = new GameObject("InfoText",
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        textObject.transform.SetParent(content, false);
        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.text = value;
        text.fontSize = 18;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = true;
        LayoutElement layout = textObject.GetComponent<LayoutElement>();
        layout.minHeight = 46f;
        layout.preferredHeight = 92f;
        layout.flexibleHeight = 0f;
        dynamicContent.Add(textObject);
    }

    private void ClearDynamicContent()
    {
        foreach (GameObject item in dynamicContent)
        {
            if (item != null)
            {
                if (Application.isPlaying) Destroy(item);
                else DestroyImmediate(item);
            }
        }
        dynamicContent.Clear();
    }

    private static string LocationTypeLabel(LocationType type)
    {
        switch (type)
        {
            case LocationType.Village: return "村庄";
            case LocationType.Sect: return "宗门";
            case LocationType.ResourceNode: return "资源点";
            case LocationType.Ruins: return "遗迹";
            case LocationType.MonsterNest: return "妖兽巢穴";
            default: return "未知";
        }
    }

    private static string ClimateLabel(WorldCell cell)
    {
        if (cell.temperature < 0.32f) return "寒冷";
        if (cell.temperature < 0.45f) return "温凉";
        if (cell.temperature < 0.60f) return "温暖";
        if (cell.temperature < 0.75f) return "炎热";
        return "酷热";
    }

    private static string ResourcePotentialLabel(float aura)
    {
        if (aura < 0.25f) return "贫瘠";
        if (aura < 0.50f) return "普通";
        if (aura < 0.75f) return "丰富";
        return "极佳";
    }

    private static string VillageRelationLabel(int relation)
    {
        if (relation >= FoundingRules.VillageSupportRelation) return "信赖";
        if (relation >= FoundingRules.VillageFamiliarRelation) return "熟悉";
        return "陌生";
    }

    private void OnDestroy()
    {
        ClearDynamicContent();
    }
}
