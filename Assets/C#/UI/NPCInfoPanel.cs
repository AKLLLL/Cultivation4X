using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// NPC detail panel.
/// </summary>
public class NPCInfoPanel : MonoBehaviour
{
    public TMP_Text nameText;
    public TMP_Text stateText;

    [Header("Character details")]
    public TMP_Text realmText;
    public TMP_Text cultivationText;
    public TMP_Text healthText;
    public TMP_Text traitsText;
    public TMP_Text relationshipsText;
    public TMP_Text historyText;

    private NPCRuntime currentNPC;

    public void Show(NPCRuntime npc)
    {
        currentNPC = npc;
        if (npc == null) return;

        if (nameText != null) nameText.text = npc.Data.npcName;
        if (stateText != null) stateText.text = "状态：" + FormatNpcState(npc.State);
        if (realmText != null) realmText.text = "境界：" + FormatRealm(npc.Realm);
        if (cultivationText != null) cultivationText.text = "修为：" + npc.Cultivation;
        if (healthText != null) healthText.text = "健康：" + FormatHealth(npc.Health);
        if (traitsText != null) traitsText.text = FormatTraits(npc.Character.traitIds);
        if (relationshipsText != null) relationshipsText.text = "关系：" + FormatRelationships(npc.Character.relationships);
        if (historyText != null) historyText.text = "履历：\n" + FormatLifeRecords(npc.Character.lifeRecords);
    }

    public void Hide()
    {
        UIManager.Instance.ClosePanel(gameObject);
    }

    private static string FormatTraits(List<string> traitIds)
    {
        if (traitIds == null || traitIds.Count == 0) return "性格：无\n经历：无";

        List<string> personalityNames = new List<string>();
        List<string> experienceNames = new List<string>();
        foreach (string traitId in traitIds)
        {
            TraitDefinition definition = TraitDatabase.Instance == null ? null : TraitDatabase.Instance.Get(traitId);
            string displayName = definition == null || string.IsNullOrWhiteSpace(definition.displayName)
                ? traitId
                : definition.displayName;

            if (definition != null && definition.isExperience)
                experienceNames.Add(displayName);
            else
                personalityNames.Add(displayName);
        }

        string personality = personalityNames.Count == 0 ? "无" : string.Join("、", personalityNames);
        string experience = experienceNames.Count == 0 ? "无" : string.Join("、", experienceNames);
        return $"性格：{personality}\n经历：{experience}";
    }

    private static string FormatRelationships(List<RelationshipRecord> relationships)
    {
        if (relationships == null || relationships.Count == 0) return "无";

        List<string> names = new List<string>();
        foreach (RelationshipRecord relationship in relationships)
        {
            NPCRuntime target = NPCManager.Instance == null ? null : NPCManager.Instance.GetRuntime(relationship.targetCharacterId);
            string targetName = target == null ? relationship.targetCharacterId : target.Character.displayName;
            names.Add($"{FormatRelationshipTag(relationship.tag)}：{targetName}");
        }

        return string.Join("、", names);
    }

    private static string FormatLifeRecords(List<LifeRecord> lifeRecords)
    {
        if (lifeRecords == null || lifeRecords.Count == 0) return "暂无";

        List<string> lines = new List<string>();
        foreach (LifeRecord record in lifeRecords)
            lines.Add($"第{record.day}天：{record.text}");

        return string.Join("\n", lines);
    }

    private static string FormatRelationshipTag(RelationshipTag tag)
    {
        switch (tag)
        {
            case RelationshipTag.MasterApprentice: return "师徒";
            case RelationshipTag.Friend: return "好友";
            case RelationshipTag.Rival: return "仇敌";
            case RelationshipTag.LifeSaver: return "救命恩人";
            default: return tag.ToString();
        }
    }

    private static string FormatRealm(CultivationRealm realm)
    {
        switch (realm)
        {
            case CultivationRealm.QiRefining: return "炼气";
            case CultivationRealm.Foundation: return "筑基";
            case CultivationRealm.GoldenCore: return "金丹";
            default: return realm.ToString();
        }
    }

    private static string FormatHealth(HealthState health)
    {
        switch (health)
        {
            case HealthState.Healthy: return "健康";
            case HealthState.LightInjury: return "轻伤";
            case HealthState.SeriousInjury: return "重伤";
            case HealthState.PermanentTrauma: return "永久创伤";
            case HealthState.Dead: return "死亡";
            default: return health.ToString();
        }
    }

    private static string FormatNpcState(NPCState state)
    {
        switch (state)
        {
            case NPCState.Idle: return "空闲";
            case NPCState.Busy: return "忙碌";
            case NPCState.Injured: return "养伤";
            case NPCState.ClosedDoor: return "闭关";
            case NPCState.Traveling: return "外出";
            default: return state.ToString();
        }
    }
}
