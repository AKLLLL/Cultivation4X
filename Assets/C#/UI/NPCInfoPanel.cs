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
    public TMP_Text experienceText;
    public TMP_Text relationshipsText;
    public TMP_Text historyText;

    private NPCRuntime currentNPC;
    private bool layoutCaptured;
    private Vector2 traitsBasePosition;
    private Vector2 relationshipsBasePosition;
    private Vector2 historyBasePosition;
    private float traitsBaseHeight;
    private float relationshipsBaseHeight;

    public void Show(NPCRuntime npc)
    {
        currentNPC = npc;
        if (npc == null) return;

        if (nameText != null) nameText.text = npc.Data.npcName;
        if (stateText != null)
        {
            stateText.text = "状态：" + FormatNpcState(npc.State);
            if (npc.Character.hasGeneratedProfile)
                stateText.text += $"\n攻{npc.Attack} 智{npc.Intelligence} 敏{npc.Agility} 悟{npc.Comprehension} 体{npc.Physique}　资质：{FoundingRules.AptitudeName(npc.AptitudeRank)}";
        }
        if (realmText != null) realmText.text = "境界：" + FormatRealm(npc.Realm);
        if (cultivationText != null) cultivationText.text = "修为：" + npc.Cultivation;
        if (healthText != null) healthText.text = "健康：" + FormatHealth(npc.Health);
        SplitTraitNames(npc.Character.traitIds, out string personality, out string experience);
        if (traitsText != null)
        {
            traitsText.text = "性格：" + personality;
            FoundingFeatureDefinition feature = FoundingRules.GetFeature(npc.Character.initialFeatureId);
            if (npc.Character.hasGeneratedProfile)
                traitsText.text += $"\n初始特点：{feature?.name ?? npc.Character.initialFeatureId}　{feature?.description}";
        }
        if (experienceText != null) experienceText.text = "经历：" + experience;
        if (relationshipsText != null) relationshipsText.text = "关系：" + FormatRelationships(npc.Character.relationships);
        if (historyText != null) historyText.text = "履历：\n" + FormatLifeRecords(npc.Character.lifeRecords);
        RefreshTextLayout();
    }

    public void Hide()
    {
        UIManager.Instance.ClosePanel(gameObject);
    }

    private static void SplitTraitNames(List<string> traitIds, out string personality, out string experience)
    {
        List<string> personalityNames = new List<string>();
        List<string> experienceNames = new List<string>();
        foreach (string traitId in traitIds ?? new List<string>())
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

        personality = personalityNames.Count == 0 ? "无" : string.Join("、", personalityNames);
        experience = experienceNames.Count == 0 ? "无" : string.Join("、", experienceNames);
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

    private void RefreshTextLayout()
    {
        RectTransform traitsRect = traitsText == null ? null : traitsText.rectTransform;
        RectTransform experienceRect = experienceText == null ? null : experienceText.rectTransform;
        RectTransform relationshipsRect = relationshipsText == null ? null : relationshipsText.rectTransform;
        RectTransform historyRect = historyText == null ? null : historyText.rectTransform;
        if (traitsRect == null || relationshipsRect == null || historyRect == null) return;
        if (experienceRect != null && !experienceText.gameObject.activeSelf) experienceText.gameObject.SetActive(true);

        if (!layoutCaptured)
        {
            layoutCaptured = true;
            traitsBasePosition = traitsRect.anchoredPosition;
            relationshipsBasePosition = relationshipsRect.anchoredPosition;
            historyBasePosition = historyRect.anchoredPosition;
            traitsBaseHeight = traitsRect.sizeDelta.y;
            relationshipsBaseHeight = relationshipsRect.sizeDelta.y;
        }

        traitsRect.anchoredPosition = traitsBasePosition;
        relationshipsRect.anchoredPosition = relationshipsBasePosition;
        historyRect.anchoredPosition = historyBasePosition;

        float traitsHeight = PreferredTextHeight(traitsText, traitsBaseHeight);
        traitsRect.sizeDelta = new Vector2(traitsRect.sizeDelta.x, traitsHeight);

        float traitsExtra = Mathf.Max(0f, traitsHeight - traitsBaseHeight);
        float experienceExtra = 0f;
        if (experienceRect != null)
        {
            experienceRect.anchoredPosition = relationshipsBasePosition - new Vector2(0f, traitsExtra);
            float experienceHeight = PreferredTextHeight(experienceText, relationshipsBaseHeight);
            experienceRect.sizeDelta = new Vector2(experienceRect.sizeDelta.x, experienceHeight);
            experienceExtra = Mathf.Max(0f, experienceHeight - relationshipsBaseHeight);
            relationshipsRect.anchoredPosition = relationshipsBasePosition - new Vector2(0f, traitsExtra + relationshipsBaseHeight + experienceExtra);
        }
        else
        {
            relationshipsRect.anchoredPosition = relationshipsBasePosition - new Vector2(0f, traitsExtra);
        }

        float relationshipHeight = PreferredTextHeight(relationshipsText, relationshipsBaseHeight);
        relationshipsRect.sizeDelta = new Vector2(relationshipsRect.sizeDelta.x, relationshipHeight);

        float relationshipExtra = Mathf.Max(0f, relationshipHeight - relationshipsBaseHeight);
        historyRect.anchoredPosition = historyBasePosition - new Vector2(0f, traitsExtra + experienceExtra + (experienceRect == null ? 0f : relationshipsBaseHeight) + relationshipExtra);
    }

    private static float PreferredTextHeight(TMP_Text text, float minimumHeight)
    {
        text.ForceMeshUpdate();
        return Mathf.Max(minimumHeight, text.preferredHeight + 8f);
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
            case CultivationRealm.Mortal: return "凡人";
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
