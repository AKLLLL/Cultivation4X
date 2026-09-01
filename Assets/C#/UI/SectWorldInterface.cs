using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed class SectWorldInterface : MonoBehaviour
{
    public static SectWorldInterface Instance { get; private set; }

    private RectTransform briefPanel;
    private RectTransform layoutPanel;
    private RectTransform taskPanel;
    private RectTransform summaryPanel;
    private Canvas canvas;
    private RectTransform sectManagerContent;
    private RectTransform sectManagerLeftColumn;
    private RectTransform sectManagerRightColumn;
    private bool sectManagerNextRight;
    private readonly List<Button> sectManagerTabButtons = new List<Button>();
    private int sectManagerTabIndex;
    private string pendingDepartmentDeletionId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MapTestBootstrap.IsTestScene) return;
        if (FindObjectOfType<SectWorldInterface>() == null)
            new GameObject("SectWorldInterface").AddComponent<SectWorldInterface>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Canvas canvas = RuntimeUIFactory.Canvas(gameObject, 930);
        this.canvas = canvas;
        briefPanel = CreatePanel(canvas.transform, "SectBrief",
            new Vector2(0.18f, 0.10f), new Vector2(0.82f, 0.90f));
        layoutPanel = CreatePanel(canvas.transform, "SectLayout",
            new Vector2(0.18f, 0.10f), new Vector2(0.82f, 0.90f));
        taskPanel = CreatePanel(canvas.transform, "StewardHall",
            new Vector2(0.18f, 0.10f), new Vector2(0.82f, 0.90f));
        summaryPanel = CreatePanel(canvas.transform, "SectSummary",
            new Vector2(0.18f, 0.10f), new Vector2(0.82f, 0.90f));
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>宗门世界 UI（顶部资源栏）只应在 GameFlowState.WorldMap 显示。</summary>
    public void SetUiVisible(bool visible)
    {
        if (canvas != null) canvas.gameObject.SetActive(visible);
    }

    public void OpenSectBrief()
    {
        // 宗门简报与宗门管理合并：统一打开分页宗门管理界面。
        OpenSectLayout();
    }

    public void OpenSectLayout()
    {
        PlayerData sect = PlayerManager.Instance?.playerData;
        if (!FoundingRules.HasReachedCave(sect?.founding)) return;
        Clear(layoutPanel);
        sectManagerTabButtons.Clear();
        sectManagerContent = null;
        sectManagerTabIndex = 0;

        RuntimeUIFactory.Text(layoutPanel, $"{sect.sectName} · 宗门管理", 30, 48);
        RectTransform tabBar = RuntimeUIFactory.CompactTabBar(layoutPanel, "SectManagerTabs");
        string[] tabs = { "宗门概况", "弟子", "功能区", "部门", "资源", "事务" };
        for (int index = 0; index < tabs.Length; index++)
        {
            int captured = index;
            Button button = RuntimeUIFactory.CompactTabButton(tabBar, tabs[index], index == 0);
            button.onClick.AddListener(() => SelectSectManagerTab(captured));
            sectManagerTabButtons.Add(button);
        }
        sectManagerContent = CreateSectManagerContent(layoutPanel);
        ShowSectManagerTab(0);
        AddCloseButton(layoutPanel);
        OpenManaged(layoutPanel);
    }

    private void SelectSectManagerTab(int index)
    {
        if (index < 0 || index >= sectManagerTabButtons.Count || index == sectManagerTabIndex) return;
        sectManagerTabIndex = index;
        for (int i = 0; i < sectManagerTabButtons.Count; i++)
            sectManagerTabButtons[i].GetComponent<Image>().color = i == index
                ? UIComponentStyles.TabSelected
                : UIComponentStyles.TabNormal;
        ShowSectManagerTab(index);
    }

    private void ShowSectManagerTab(int index)
    {
        if (sectManagerContent == null) return;
        if (index == 2 || index == 3) RebuildSectOrganizationBody();
        else RebuildSectManagerColumns(index != 1);
        switch (index)
        {
            case 0: ShowSectOverview(); break;
            case 1: ShowSectDisciples(); break;
            case 2: ShowFunctionalZones(); break;
            case 3: ShowDepartments(); break;
            case 4: ShowSectResources(); break;
            default: ShowSectAffairs(); break;
        }
    }

    private void ShowSectOverview()
    {
        PlayerData sect = PlayerManager.Instance?.playerData;
        if (sect == null) return;
        WorldMap map = WorldMapSession.Current;
        MapSiteData site = WorldMapProgressRules.GetSectBase(WorldMapSession.Progress);
        if (map?.cells == null || site == null || site.cellIndex < 0 || site.cellIndex >= map.cells.Length)
            return;
        WorldCell cell = map.cells[site.cellIndex];
        WorldMapProgressState progress = WorldMapSession.Progress;
        if (progress == null) return;
        WorldMapInfluenceRules.EnsureCurrent(map, progress);
        int core = progress.cellInfluences.Count(item => item.level == InfluenceLevel.Core);
        int influence = progress.cellInfluences.Count(item => item.level == InfluenceLevel.Influence);
        int outer = progress.cellInfluences.Count(item => item.level == InfluenceLevel.Outer);
        int materials = WarehouseManager.Instance == null
            ? 0
            : WarehouseManager.Instance.GetItemCount(FacilityRules.BasicMaterialId);

        AddSectInfoRow("宗门等级", "初创宗门");
        AddSectInfoRow("位置",
            $"{WorldMapCellDetailsFormatter.LandformLabel(cell.landform)}/" +
            $"{WorldMapCellDetailsFormatter.BiomeLabel(cell.biome)}");
        AddSectInfoRow("灵气",
            $"{WorldMapCellDetailsFormatter.AuraBand(cell.totalAura)} ({cell.totalAura:0.000})");
        AddSectInfoRow("弟子", LivingDiscipleCount().ToString());
        AddSectInfoRow("灵石", (WarehouseManager.Instance?.GetItemCount(FacilityRules.SpiritStoneId) ?? 0).ToString());
        AddSectInfoRow("基础材料", materials.ToString());
        AddSectInfoRow("声望", sect.reputation.ToString());
        AddSectInfoRow("影响范围", $"核心{core}　影响{influence}　外缘{outer}");
        Button monthlyReport = AddSectButton("宗门月报", 46);
        monthlyReport.interactable = GrowthFeedbackRules.LatestReport(sect) != null;
        monthlyReport.onClick.AddListener(() =>
        {
            SectMonthlyReport latest = GrowthFeedbackRules.LatestReport(PlayerManager.Instance?.playerData);
            bool unread = TimeManager.Instance?.UnreadDaySettlement?.isMonthEnd == true &&
                TimeManager.Instance.UnreadDaySettlement.monthIndex == latest?.monthIndex;
            UIManager.Instance?.OpenWindow(UIWindowId.MonthlyReport,
                new MonthlyReportContext(latest?.monthIndex, unread));
        });
    }

    private void ShowSectDisciples()
    {
        List<NPCRuntime> npcs = NPCManager.Instance == null
            ? new List<NPCRuntime>()
            : NPCManager.Instance.GetAllNPC();
        if (npcs.Count == 0)
        {
            AddSectInfoText("暂无弟子", 15);
            return;
        }
        foreach (NPCRuntime npc in npcs)
        {
            if (npc?.Data == null) continue;
            NPCRuntime captured = npc;
            Button row = AddSectButton(
                $"{npc.Data.npcName}　{RealmLabel(npc)}　状态：{StateLabel(npc.State)}", 48);
            row.onClick.AddListener(() => OpenNpcDetail(captured));
        }
        AddSectInfoText("点击弟子进入已有弟子详情。", 13);
    }

    private void ShowFunctionalZones()
    {
        WorldMap map = WorldMapSession.Current;
        WorldMapProgressState progress = WorldMapSession.Progress;
        List<SectFunctionalZoneState> zones = progress?.functionalZones?
            .Where(zone => zone != null).OrderBy(zone => zone.cellIndex).ToList()
            ?? new List<SectFunctionalZoneState>();
        AddSectInfoText("灵植区", 18);
        AddSectInfoText("从世界地图普通格的“行动”页规划。阶段进度来自弟子真实宗务行动。", 13);
        if (zones.Count == 0)
        {
            AddSectInfoText("尚未规划功能区。", 15);
        }
        foreach (SectFunctionalZoneState zone in zones)
        {
            WorldCell cell = map?.cells != null && zone.cellIndex >= 0 && zone.cellIndex < map.cells.Length
                ? map.cells[zone.cellIndex] : null;
            SectDepartmentState department = SectOrganizationRules.Get(
                PlayerManager.Instance?.playerData, zone.assignedDepartmentId);
            Button locate = AddSectButton(
                $"{SectFunctionalZoneRules.DisplayName(map, zone)}　{SectFunctionalZoneRules.StageName(zone.stage)}\n" +
                $"{SectFunctionalZoneRules.ProgressText(zone)}　适宜度：{SectFunctionalZoneRules.SuitabilityName(cell)} " +
                $"×{SectFunctionalZoneRules.SuitabilityMultiplier(cell):0.0}　负责：{department?.name ?? "未绑定"}", 64);
            int capturedCell = zone.cellIndex;
            locate.onClick.AddListener(() => LocateFunctionalZone(capturedCell));
        }

        AddSectInfoText("已开放宗门功能", 18);
        foreach (FacilityType facility in System.Enum.GetValues(typeof(FacilityType)))
        {
            if (facility == FacilityType.SecretRealm) continue;
            AddFacilityStateRow(facility);
        }
        AddFacilityActionButtons();
    }

    private void ShowDepartments()
    {
        PlayerData player = PlayerManager.Instance?.playerData;
        if (player == null) return;
        if (player.departments == null) player.departments = new List<SectDepartmentState>();
        AddSectInfoText("部门只提高弟子响应本部门区域的倾向，不会独占功能区。", 14);
        if (player.departments.Count == 0) AddSectInfoText("尚未创建部门。", 15);
        foreach (SectDepartmentState department in player.departments.Where(item => item != null).ToList())
            AddDepartmentEditor(player, department);

        string suggestedName = NextDepartmentName(player);
        Button create = AddSectButton($"创建“{suggestedName}”", 46);
        create.onClick.AddListener(() =>
        {
            if (!SectOrganizationRules.TryCreate(player, suggestedName, out _, out string reason))
                Debug.LogWarning(reason);
            PersistSectOrganization();
        });
    }

    private void ShowSectResources()
    {
        AddSectInfoRow("灵石",
            (WarehouseManager.Instance?.GetItemCount(FacilityRules.SpiritStoneId) ?? 0).ToString());
        AddSectInfoRow("基础材料",
            (WarehouseManager.Instance == null
                ? 0 : WarehouseManager.Instance.GetItemCount(FacilityRules.BasicMaterialId)).ToString());
        AddSectInfoRow("仓库容量",
            WarehouseManager.Instance == null
                ? "0/0"
                : $"{WarehouseManager.Instance.GetUsedSlotCount()}/{WarehouseManager.Instance.GetCapacity()}");
        AddSectInfoRow("丹药", CountItemsByType(ItemType.Pill).ToString());
        AddSectInfoRow("法宝", CountItemsByType(ItemType.Weapon).ToString());
        List<SectFunctionalZoneState> zones = WorldMapSession.Progress?.functionalZones?
            .Where(zone => zone != null).ToList() ?? new List<SectFunctionalZoneState>();
        AddSectInfoRow("灵植区", zones.Count.ToString());
        AddSectInfoRow("药圃", zones.Count(zone => zone.stage == FunctionalZoneStage.Operational).ToString());
        AddSectInfoRow("青灵草",
            (WarehouseManager.Instance?.GetItemCount(SectFunctionalZoneRules.HerbItemId) ?? 0).ToString());

        AddSectInfoText("生产", 16);
        AddSectInfoText("灵植区由宗务行动推进；药圃照料充足后，弟子可执行采收。", 13);
        AddSectInfoText(PlayerManager.Instance?.HasFacility(FacilityType.AlchemyRoom) == true
            ? "炼丹房：已开放" : "炼丹房：未开放", 13);
        Button warehouse = AddSectButton( "打开仓库", 46);
        warehouse.onClick.AddListener(OpenWarehouse);
    }

    private void ShowSectAffairs()
    {
        AddSectInfoText("弟子安排", 16);
        int day = TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay;
        AddSectInfoText("月计划采用30日循环模板，可由多名弟子共享。未绑定弟子默认自由活动。", 13);
        FoundingState founding = PlayerManager.Instance?.playerData?.founding;
        if (founding != null && founding.sectCreated)
        {
            Button monthlyPlan = AddSectButton("弟子月度计划", 46);
            monthlyPlan.onClick.AddListener(() => UIManager.Instance?.OpenWindow(UIWindowId.MonthlyPlan));
        }
        else AddSectInfoText("正式立宗后开放月度计划。", 13);
        Button steward = AddSectButton( "打开任务堂／执事堂", 46);
        steward.onClick.AddListener(OpenStewardHall);
        Button threat = AddSectButton( "外部威胁", 46);
        threat.onClick.AddListener(() => FindRuntime<ExternalThreatPanel>()?.OpenFromSectLayout());
        if (founding != null && founding.stage == FoundingStage.Cave)
        {
            Button foundingButton = AddSectButton( "洞府整备／立宗进度", 46);
            foundingButton.onClick.AddListener(() => FindRuntime<FoundingPanel>()?.OpenFromSectLayout());
        }
    }

    private static RectTransform CreateSectManagerContent(Transform parent)
    {
        GameObject obj = new GameObject("SectManagerContent",
            typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        HorizontalLayoutGroup layout = obj.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 12;
        layout.childForceExpandWidth = true;
        layout.childControlWidth = true;
        LayoutElement element = obj.GetComponent<LayoutElement>();
        element.flexibleHeight = 1f;
        return rect;
    }

    private void RebuildSectManagerColumns(bool twoColumns)
    {
        if (sectManagerContent == null) return;
        Clear(sectManagerContent);
        sectManagerLeftColumn = CreateSectManagerColumn(sectManagerContent);
        sectManagerRightColumn = twoColumns ? CreateSectManagerColumn(sectManagerContent) : null;
        sectManagerNextRight = false;
    }

    private static RectTransform CreateSectManagerColumn(Transform parent)
    {
        GameObject obj = new GameObject("SectManagerColumn",
            typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        VerticalLayoutGroup layout = obj.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 6;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        LayoutElement element = obj.GetComponent<LayoutElement>();
        element.flexibleWidth = 1f;
        element.flexibleHeight = 1f;
        return rect;
    }

    private RectTransform NextSectColumn()
    {
        if (sectManagerRightColumn == null)
            return sectManagerLeftColumn;
        sectManagerNextRight = !sectManagerNextRight;
        return sectManagerNextRight ? sectManagerRightColumn : sectManagerLeftColumn;
    }

    private void AddSectInfoRow(string label, string value)
    {
        RectTransform target = NextSectColumn();
        if (target == null) return;
        string textValue = $"{label}：{value}";
        TMP_Text text = RuntimeUIFactory.Text(target, textValue, 16, 32);
        text.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private void AddSectInfoText(string textValue, int fontSize)
    {
        RectTransform target = NextSectColumn();
        if (target == null) return;
        RuntimeUIFactory.Text(target, textValue, fontSize, 30);
    }

    private Button AddSectButton(string label, float height = 44)
    {
        RectTransform target = NextSectColumn();
        if (target == null) return null;
        return RuntimeUIFactory.Button(target, label, height);
    }

    private void RebuildSectOrganizationBody()
    {
        if (sectManagerContent == null) return;
        Clear(sectManagerContent);
        GameObject prefab = Resources.Load<GameObject>("Prefab/UI/SectOrganizationBody");
        SectOrganizationBodyView view = prefab == null ? null :
            Instantiate(prefab, sectManagerContent).GetComponent<SectOrganizationBodyView>();
        if (view?.Content != null)
        {
            sectManagerLeftColumn = view.Content;
            sectManagerRightColumn = null;
            sectManagerNextRight = false;
            return;
        }
        GameDebugConfig.LogWorldMapWarning("缺少 SectOrganizationBody Prefab，使用兼容布局");
        sectManagerLeftColumn = CreateSectManagerColumn(sectManagerContent);
        sectManagerRightColumn = null;
        sectManagerNextRight = false;
    }

    private void AddFacilityStateRow(FacilityType facility)
    {
        AddSectInfoRow(FacilityName(facility),
            PlayerManager.Instance?.HasFacility(facility) == true ? "已开放" : "未开放");
    }

    private void AddFacilityActionButtons()
    {
        Button scripture = AddSectButton("藏经阁（功法研究）", 44);
        scripture.interactable = PlayerManager.Instance?.HasFacility(FacilityType.InheritanceChamber) == true;
        scripture.onClick.AddListener(OpenScriptureSummary);
        Button alchemy = AddSectButton("炼丹房（炼制丹药）", 44);
        alchemy.interactable = PlayerManager.Instance?.HasFacility(FacilityType.AlchemyRoom) == true;
        alchemy.onClick.AddListener(() => FindRuntime<AlchemyPanel>()?.OpenFromSectLayout());
        Button training = AddSectButton("修炼室（安排修炼）", 44);
        training.interactable = PlayerManager.Instance?.HasFacility(FacilityType.TrainingRoom) == true;
        training.onClick.AddListener(OpenTrainingSummary);
    }

    private void AddDepartmentEditor(PlayerData player, SectDepartmentState department)
    {
        List<NPCRuntime> living = NPCManager.Instance?.GetAllNPC()
            .Where(npc => npc?.Character?.IsAlive == true)
            .OrderBy(npc => npc.CharacterId, System.StringComparer.Ordinal).ToList()
            ?? new List<NPCRuntime>();
        List<string> livingIds = living.Select(npc => npc.CharacterId).ToList();
        string leaderName = living.FirstOrDefault(npc => npc.CharacterId == department.leaderDiscipleId)
            ?.Character?.displayName ?? "未设置";
        int zoneCount = WorldMapSession.Progress?.functionalZones?.Count(zone =>
            zone?.assignedDepartmentId == department.departmentId) ?? 0;
        AddSectInfoText($"{department.name}　成员{department.memberDiscipleIds?.Count ?? 0}　负责区域{zoneCount}\n负责人：{leaderName}", 17);

        TMP_InputField nameInput = CreateDepartmentNameInput(NextSectColumn(), department.name);
        Button rename = AddSectButton("保存部门名称", 42);
        rename.onClick.AddListener(() =>
        {
            if (!SectOrganizationRules.TryRename(player, department.departmentId,
                    nameInput.text, out string reason)) Debug.LogWarning(reason);
            PersistSectOrganization();
        });

        AddSectInfoText("成员", 15);
        foreach (NPCRuntime npc in living)
        {
            bool belongsHere = department.memberDiscipleIds?.Contains(npc.CharacterId) == true;
            SectDepartmentState other = SectOrganizationRules.DepartmentFor(player, npc.CharacterId);
            Button member = AddSectButton(
                belongsHere ? $"移出　{npc.Character.displayName}" : $"加入　{npc.Character.displayName}", 40);
            member.interactable = other == null || other == department;
            string discipleId = npc.CharacterId;
            member.onClick.AddListener(() =>
            {
                List<string> next = new List<string>(department.memberDiscipleIds ?? new List<string>());
                bool wasMember = next.Contains(discipleId);
                bool wasLeader = department.leaderDiscipleId == discipleId;
                if (wasMember) next.Remove(discipleId); else next.Add(discipleId);
                if (!SectOrganizationRules.TrySetMembers(player, department.departmentId,
                        next, livingIds, out string reason)) Debug.LogWarning(reason);
                else
                {
                    CharacterState character = NPCManager.Instance?.GetRuntime(discipleId)?.Character;
                    if (wasLeader && wasMember)
                        ExperienceGenerator.WriteDepartmentChange(character, CurrentRecordDay(), department,
                            "department_leader_ended");
                    ExperienceGenerator.WriteDepartmentChange(character, CurrentRecordDay(), department,
                        wasMember ? "department_left" : "department_joined");
                }
                PersistSectOrganization();
            });
        }

        if (department.memberDiscipleIds?.Count > 0)
        {
            AddSectInfoText("负责人", 15);
            foreach (string memberId in department.memberDiscipleIds.ToList())
            {
                NPCRuntime npc = living.FirstOrDefault(item => item.CharacterId == memberId);
                if (npc == null) continue;
                Button leader = AddSectButton(
                    department.leaderDiscipleId == memberId
                        ? $"取消负责人　{npc.Character.displayName}"
                        : $"设为负责人　{npc.Character.displayName}", 40);
                leader.onClick.AddListener(() =>
                {
                    string previous = department.leaderDiscipleId;
                    string next = department.leaderDiscipleId == memberId ? null : memberId;
                    if (!SectOrganizationRules.TrySetLeader(player, department.departmentId,
                            next, out string reason)) Debug.LogWarning(reason);
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(previous) && previous != next)
                            ExperienceGenerator.WriteDepartmentChange(
                                NPCManager.Instance?.GetRuntime(previous)?.Character, CurrentRecordDay(), department,
                                "department_leader_ended");
                        if (!string.IsNullOrWhiteSpace(next) && previous != next)
                            ExperienceGenerator.WriteDepartmentChange(
                                NPCManager.Instance?.GetRuntime(next)?.Character, CurrentRecordDay(), department,
                                "department_leader_started");
                    }
                    PersistSectOrganization();
                });
            }
        }

        List<SectFunctionalZoneState> zones = WorldMapSession.Progress?.functionalZones?
            .Where(zone => zone != null).OrderBy(zone => zone.cellIndex).ToList()
            ?? new List<SectFunctionalZoneState>();
        if (zones.Count > 0)
        {
            AddSectInfoText("负责区域", 15);
            foreach (SectFunctionalZoneState zone in zones)
            {
                bool assignedHere = zone.assignedDepartmentId == department.departmentId;
                bool assignedElsewhere = !string.IsNullOrWhiteSpace(zone.assignedDepartmentId) && !assignedHere;
                Button bind = AddSectButton(
                    $"{(assignedHere ? "解除" : "绑定")}　{SectFunctionalZoneRules.DisplayName(WorldMapSession.Current, zone)}", 40);
                bind.interactable = !assignedElsewhere;
                bind.onClick.AddListener(() =>
                {
                    string reason;
                    bool success = assignedHere
                        ? SectOrganizationRules.TryUnassignZone(WorldMapSession.Progress, zone.zoneId, out reason)
                        : SectOrganizationRules.TryAssignZone(player, WorldMapSession.Progress,
                            department.departmentId, zone.zoneId, out reason);
                    if (!success) Debug.LogWarning(reason);
                    PersistSectOrganization();
                });
            }
        }

        bool confirming = pendingDepartmentDeletionId == department.departmentId;
        Button delete = AddSectButton(confirming
            ? "确认删除部门（区域将解除绑定）" : "删除部门", 42);
        delete.onClick.AddListener(() =>
        {
            if (!confirming)
            {
                pendingDepartmentDeletionId = department.departmentId;
                ShowSectManagerTab(sectManagerTabIndex);
                return;
            }
            List<string> formerMembers = new List<string>(department.memberDiscipleIds ?? new List<string>());
            if (!SectOrganizationRules.TryDelete(player, WorldMapSession.Progress,
                    department.departmentId, out string reason)) Debug.LogWarning(reason);
            else
                foreach (string memberId in formerMembers)
                    ExperienceGenerator.WriteDepartmentChange(
                        NPCManager.Instance?.GetRuntime(memberId)?.Character, CurrentRecordDay(), department,
                        "department_left");
            pendingDepartmentDeletionId = null;
            PersistSectOrganization();
        });
    }

    private static TMP_InputField CreateDepartmentNameInput(Transform parent, string value)
    {
        GameObject root = new GameObject("DepartmentNameInput", typeof(RectTransform), typeof(Image),
            typeof(TMP_InputField), typeof(LayoutElement));
        root.transform.SetParent(parent, false);
        root.GetComponent<Image>().color = UIComponentStyles.InfoCard;
        root.GetComponent<LayoutElement>().preferredHeight = 44f;
        TMP_Text text = RuntimeUIFactory.Text(root.transform, value, 18, 42);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(10f, 3f);
        text.rectTransform.offsetMax = new Vector2(-10f, -3f);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        TMP_InputField input = root.GetComponent<TMP_InputField>();
        input.textComponent = text;
        input.textViewport = root.GetComponent<RectTransform>();
        input.characterLimit = 12;
        input.text = value;
        return input;
    }

    private void PersistSectOrganization()
    {
        WorldMapSession.NotifyProgressChanged();
        SaveManager.Instance?.AutoSave();
        ShowSectManagerTab(sectManagerTabIndex);
    }

    private static int CurrentRecordDay() => TimeManager.Instance?.ActiveDay ?? 0;

    private static string NextDepartmentName(PlayerData player)
    {
        if (player?.departments?.Any(item => item?.name == "百草堂") != true) return "百草堂";
        int sequence = 2;
        string candidate;
        do candidate = $"百草堂{sequence++}";
        while (player.departments.Any(item => item?.name == candidate));
        return candidate;
    }

    private void LocateFunctionalZone(int cellIndex)
    {
        if (UIManager.Instance != null) UIManager.Instance.ClosePanel(layoutPanel.gameObject);
        else layoutPanel.gameObject.SetActive(false);
        FindObjectOfType<WorldMap3DController>()?.SelectCellFromUi(cellIndex);
    }

    private static string FacilityName(FacilityType type)
    {
        switch (type)
        {
            case FacilityType.MissionHall: return "任务堂";
            case FacilityType.Warehouse: return "仓库";
            case FacilityType.TrainingRoom: return "修炼室";
            case FacilityType.AlchemyRoom: return "炼丹房";
            case FacilityType.InheritanceChamber: return "传承石室";
            case FacilityType.ForgeRoom: return "炼器台";
            case FacilityType.FormationPlatform: return "阵法台";
            case FacilityType.ProtectionArray: return "护山阵";
            default: return type.ToString();
        }
    }

    private int CountItemsByType(ItemType type)
    {
        if (WarehouseManager.Instance == null || WarehouseManager.Instance.warehouseData?.items == null)
            return 0;
        int total = 0;
        foreach (ItemStack stack in WarehouseManager.Instance.warehouseData.items)
        {
            if (stack == null || string.IsNullOrWhiteSpace(stack.itemId)) continue;
            ItemData data = ItemDatabase.Instance == null ? null : ItemDatabase.Instance.GetItem(stack.itemId);
            if (data != null && data.itemType == type) total += stack.count;
        }
        return total;
    }

    private static string RealmLabel(NPCRuntime npc)
    {
        if (npc == null) return "未知";
        switch (npc.Realm)
        {
            case CultivationRealm.Mortal: return "凡人";
            case CultivationRealm.QiRefining: return $"练气{npc.RealmLayer}层";
            case CultivationRealm.Foundation: return "筑基";
            case CultivationRealm.GoldenCore: return "金丹";
            default: return npc.Realm.ToString();
        }
    }

    private static string StateLabel(NPCState state)
    {
        switch (state)
        {
            case NPCState.Idle: return "修炼";
            case NPCState.Busy: return "探索";
            case NPCState.Injured: return "受伤";
            case NPCState.ClosedDoor: return "闭关";
            case NPCState.Traveling: return "外出";
            default: return "空闲";
        }
    }

    private static void AddBuildingCard(Transform parent, string title, string description,
        UnityEngine.Events.UnityAction onClick)
    {
        Button card = RuntimeUIFactory.Button(parent, $"{title}\n{description}", 62);
        card.onClick.AddListener(onClick);
        LayoutElement layout = card.GetComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
    }

    private void OpenStewardHall()
    {
        Clear(taskPanel);
        RuntimeUIFactory.Text(taskPanel, "任务堂／执事堂", 30, 48);
        AddEntry(taskPanel, "宗门任务", OpenMissionPanel);
        AddEntry(taskPanel, "外部威胁", () => FindRuntime<ExternalThreatPanel>()?.OpenFromSectLayout());
        AddCloseButton(taskPanel);
        OpenManaged(taskPanel);
    }

    private void OpenTrainingSummary()
    {
        bool available = PlayerManager.Instance?.HasFacility(FacilityType.TrainingRoom) == true;
        Clear(summaryPanel);
        RuntimeUIFactory.Text(summaryPanel, "修炼室", 30, 48);
        RuntimeUIFactory.Text(summaryPanel,
            $"功能状态：{(available ? "已开放" : "未开放")}\n" +
            $"当前效果：纳气效率 x{FacilityRules.TrainingMultiplier(available):0.0}\n" +
            $"存活弟子：{LivingDiscipleCount()}",
            19, 86);
        AddEntry(summaryPanel, "查看弟子", OpenSectDisciplesPage);
        AddCloseButton(summaryPanel);
        OpenManaged(summaryPanel);
    }

    private void OpenScriptureSummary()
    {
        FoundingState founding = PlayerManager.Instance?.playerData?.founding;
        TechniqueDefinition technique = FoundingRules.GetTechnique(founding?.selectedTechniqueId);
        string tags = technique == null
            ? "无"
            : string.Join("、", (technique.tags ?? new System.Collections.Generic.List<string>())
                .Select(FoundingRules.TechniqueTagName));
        SectTechniqueState mastery = TechniqueRules.SectState(PlayerManager.Instance?.playerData, technique?.id);
        string annotations = mastery?.annotationIds == null || mastery.annotationIds.Count == 0
            ? "无" : string.Join("、", mastery.annotationIds.Select(id =>
                id == TechniqueRules.BeginnerAnnotationId ? "入门详解" : "因材施教"));
        Clear(summaryPanel);
        RuntimeUIFactory.Text(summaryPanel, "藏经阁", 30, 48);
        RuntimeUIFactory.Text(summaryPanel,
            $"传承：{technique?.name ?? "无"}\n宗门推演：{mastery?.masteryProgress ?? 0:0.0}%（{TechniqueRules.SectStageName(mastery)}）\n" +
            $"标签：{tags}\n注解：{annotations}\n\n功法管理尚未开放。",
            19, 132);
        AddCloseButton(summaryPanel);
        OpenManaged(summaryPanel);
    }

    private void OpenNpcDetail(NPCRuntime npc)
    {
        if (npc == null) return;
        if (UIManager.Instance != null)
        {
            UIManager.Instance.OpenWindow(UIWindowId.DiscipleCenter,
                new DiscipleCenterContext(npc.CharacterId));
            return;
        }
        SectPanel sectPanel = Resources.FindObjectsOfTypeAll<SectPanel>()
            .FirstOrDefault(item => item != null && item.gameObject.scene.IsValid());
        if (sectPanel != null && sectPanel.infoPanel != null)
        {
            NPCInfoPanel detail = sectPanel.infoPanel;
            // 旧弟子列表已废弃：只把 SectPanel 当作 NPCInfoPanel 的父容器激活，
            // 并隐藏旧列表内容，避免出现旧文字列表。
            sectPanel.gameObject.SetActive(true);
            if (sectPanel.content != null)
                sectPanel.content.gameObject.SetActive(false);
            if (UIManager.Instance != null)
            {
                UIManager.Instance.OpenPanel(detail.gameObject, () => CloseNpcDetailContainer(sectPanel));
            }
            else
            {
                detail.gameObject.SetActive(true);
            }
            detail.Show(npc);
            return;
        }
        NPCInfoPanel panel = FindObjectOfType<NPCInfoPanel>(true);
        if (panel != null)
        {
            if (UIManager.Instance != null) UIManager.Instance.OpenPanel(panel.gameObject);
            else panel.gameObject.SetActive(true);
            panel.Show(npc);
            return;
        }
        GameDebugConfig.LogWorldMapWarning("未找到可用的弟子详情面板，已取消打开旧弟子列表。");
    }

    private static void CloseNpcDetailContainer(SectPanel sectPanel)
    {
        if (sectPanel == null) return;
        if (UIManager.Instance != null) UIManager.Instance.ClosePanel(sectPanel.gameObject);
        else sectPanel.gameObject.SetActive(false);
    }

    /// <summary>直接打开新版宗门管理的弟子分页。</summary>
    public void OpenSectDisciplesPage()
    {
        if (UIManager.Instance != null && summaryPanel != null)
            UIManager.Instance.ClosePanel(summaryPanel.gameObject);
        OpenSectLayout();
        SelectSectManagerTab(1);
    }

    private void OpenWarehouse() => OpenSceneComponent<WarehousePanel>();
    private void OpenMissionPanel() => OpenSceneComponent<MissionPanel>();

    private static void OpenSceneComponent<T>() where T : MonoBehaviour
    {
        T target = Resources.FindObjectsOfTypeAll<T>()
            .FirstOrDefault(item => item != null && item.gameObject.scene.IsValid());
        if (target == null) { Debug.LogWarning($"{typeof(T).Name} 不存在"); return; }
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(target.gameObject);
        else target.gameObject.SetActive(true);
    }

    private static T FindRuntime<T>() where T : MonoBehaviour =>
        FindObjectOfType<T>() ?? Resources.FindObjectsOfTypeAll<T>().FirstOrDefault();

    private static int LivingDiscipleCount() =>
        NPCManager.Instance?.GetAllNPC().Count(npc => npc?.Character?.IsAlive == true) ?? 0;

    private static RectTransform CreatePanel(Transform canvas, string name, Vector2 min, Vector2 max)
    {
        RectTransform panel = RuntimeUIFactory.Panel(canvas, name, min, max);
        panel.gameObject.SetActive(false);
        return panel;
    }

    private static void AddEntry(Transform parent, string label, UnityEngine.Events.UnityAction action)
    {
        Button button = RuntimeUIFactory.Button(parent, label, 48);
        button.onClick.AddListener(action);
    }

    private static void AddCloseButton(RectTransform panel)
    {
        Button close = RuntimeUIFactory.Button(panel, "返回", 42);
        close.onClick.AddListener(() =>
        {
            if (UIManager.Instance != null) UIManager.Instance.ClosePanel(panel.gameObject);
            else panel.gameObject.SetActive(false);
        });
    }

    private static void OpenManaged(RectTransform panel)
    {
        // 容器面板已打开时不再提升层级，避免盖住其上层的子面板。
        if (panel != null && panel.gameObject.activeSelf)
            return;
        if (UIManager.Instance != null) UIManager.Instance.OpenScreen(panel.gameObject);
        else panel.gameObject.SetActive(true);
    }

    private static void Clear(RectTransform panel)
    {
        for (int i = panel.childCount - 1; i >= 0; i--)
            Destroy(panel.GetChild(i).gameObject);
    }
}
