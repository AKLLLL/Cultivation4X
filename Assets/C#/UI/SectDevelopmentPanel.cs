using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SectDevelopmentPanel : MonoBehaviour
{
    private RectTransform panel;
    private TMP_Text resources;
    private readonly Dictionary<FacilityType, Button> buttons = new Dictionary<FacilityType, Button>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<SectDevelopmentPanel>() == null)
            new GameObject("SectDevelopmentPanel").AddComponent<SectDevelopmentPanel>();
    }

    private void Awake()
    {
        RuntimeUIFactory.Canvas(gameObject, 850);
        Button launcher = RuntimeUIFactory.Button(transform, "宗门建设");
        RectTransform launchRect = launcher.GetComponent<RectTransform>();
        launchRect.anchorMin = launchRect.anchorMax = new Vector2(0, 1); launchRect.pivot = new Vector2(0, 1);
        launchRect.anchoredPosition = new Vector2(15, -15); launchRect.sizeDelta = new Vector2(150, 45);
        launcher.onClick.AddListener(Open);
        launcher.gameObject.SetActive(false);
        panel = RuntimeUIFactory.Panel(transform, "Development", new Vector2(0.12f, 0.1f), new Vector2(0.88f, 0.9f));
        RuntimeUIFactory.Text(panel, "宗门建设", 30, 48);
        resources = RuntimeUIFactory.Text(panel, string.Empty, 20, 44);
        FacilityType[] upgradeFacilities =
        {
            FacilityType.Warehouse,
            FacilityType.TrainingRoom,
            FacilityType.SecretRealm,
            FacilityType.AlchemyRoom
        };
        foreach (FacilityType facility in upgradeFacilities)
        {
            FacilityType captured = facility;
            Button button = RuntimeUIFactory.Button(panel, string.Empty, 58);
            button.onClick.AddListener(() => Upgrade(captured));
            buttons[facility] = button;
        }
        Button close = RuntimeUIFactory.Button(panel, "关闭"); close.onClick.AddListener(Close);
        panel.gameObject.SetActive(false);
    }

    private void Open()
    {
        Refresh();
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(panel.gameObject, CloseInternal);
        else panel.gameObject.SetActive(true);
    }

    public void OpenFromSectLayout() => Open();

    private void Close()
    {
        if (UIManager.Instance != null) UIManager.Instance.ClosePanel(panel.gameObject);
        else CloseInternal();
    }

    private void CloseInternal() => panel.gameObject.SetActive(false);

    private void Upgrade(FacilityType facility)
    {
        FacilityUpgradeResult result = PlayerManager.Instance.TryUpgradeFacility(facility);
        if (!result.success) Debug.LogWarning(result.reason);
        Refresh();
    }

    private void Refresh()
    {
        if (PlayerManager.Instance == null || WarehouseManager.Instance == null) return;
        resources.text = $"灵材 {PlayerManager.Instance.playerData.gold}　声望 {PlayerManager.Instance.playerData.reputation}　基础材料 {WarehouseManager.Instance.GetItemCount(FacilityRules.BasicMaterialId)}";
        foreach (var pair in buttons)
        {
            int level = PlayerManager.Instance.GetFacilityLevel(pair.Key);
            if (level <= 0)
            {
                pair.Value.GetComponentInChildren<TMP_Text>().text = $"{Name(pair.Key)}｜损坏或未建\n请通过立宗剧情修复或建设";
                pair.Value.interactable = false;
                continue;
            }
            int gold = FacilityRules.UpgradeGoldCost(level), material = FacilityRules.UpgradeMaterialCost(level);
            pair.Value.GetComponentInChildren<TMP_Text>().text = level >= FacilityRules.MaxLevel
                ? $"{Name(pair.Key)} Lv.{level}（已满级）"
                : $"{Name(pair.Key)} Lv.{level} → Lv.{level + 1}　{gold}灵材 / {material}材料\n{Effect(pair.Key, level)}";
            pair.Value.interactable = level < FacilityRules.MaxLevel;
        }
    }

    private static string Name(FacilityType type)
    {
        switch (type) { case FacilityType.MissionHall: return "任务堂"; case FacilityType.Warehouse: return "仓库";
            case FacilityType.TrainingRoom: return "修炼室"; case FacilityType.SecretRealm: return "秘境";
            case FacilityType.ExplorationHall: return "探索堂"; default: return "炼丹房"; }
    }

    private static string Effect(FacilityType type, int level)
    {
        switch (type) { case FacilityType.MissionHall: return $"候选 {FacilityRules.MissionCandidateCount(level)} / 并行 {FacilityRules.MissionConcurrency(level)}";
            case FacilityType.Warehouse: return $"物品种类上限 {FacilityRules.WarehouseSlots(level)}";
            case FacilityType.TrainingRoom: return $"每日基础修为 +{FacilityRules.TrainingGain(level)}";
            case FacilityType.SecretRealm: return $"探索 {FacilityRules.SecretRealmDays(level)} 天 / 材料 {FacilityRules.SecretRealmMaterialReward(level)}";
            case FacilityType.ExplorationHall: return "开启宗门外部探索";
            default: return $"炼丹 {FacilityRules.AlchemyDays(level)} 天 / 丹药 {FacilityRules.AlchemyPillReward(level)}"; }
    }
}
