using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public enum TechniqueCategory
{
    Main,
    Auxiliary
}

public enum TechniqueUnderstandingStage
{
    Beginner,
    Understanding,
    Integrated,
    Perfected
}

public enum SectTechniqueMasteryStage
{
    Initial,
    Proficient,
    Profound,
    GreatAchievement
}

[Serializable]
public class TechniqueElementProfile
{
    public float metal = 0.2f;
    public float wood = 0.2f;
    public float water = 0.2f;
    public float fire = 0.2f;
    public float earth = 0.2f;
}

[Serializable]
public class TechniqueApplicationBias
{
    public string tag;
    public float score;
}

[Serializable]
public class TechniqueLearningRequirement
{
    public int minimumComprehension;
    public float minimumElementAffinity;
}

[Serializable]
public class TechniqueDefinition
{
    public string id;
    public string name;
    public string description;
    public TechniqueCategory category;
    public List<string> tags = new List<string>();
    public TechniqueElementProfile elements = new TechniqueElementProfile();
    public float absorptionMultiplier = 1f;
    public float refiningMultiplier = 1f;
    public float stabilityModifier;
    public List<TechniqueApplicationBias> applicationBiases = new List<TechniqueApplicationBias>();
    public TechniqueLearningRequirement learningRequirement = new TechniqueLearningRequirement();
    public string theory;
    public string originStory;
}

[Serializable]
public class TechniqueCatalogData
{
    public List<TechniqueDefinition> techniques = new List<TechniqueDefinition>();
}

[Serializable]
public class PersonalTechniqueProgress
{
    public string techniqueId;
    public float understanding;
}

[Serializable]
public class SectTechniqueState
{
    public string techniqueId;
    public float masteryProgress;
    public bool firstAnnotationResolved;
    public bool firstAnnotationQueued;
    public List<string> annotationIds = new List<string>();
}

public static class TechniqueRules
{
    public const string CatalogResourcePath = "Configs/Techniques/techniques";
    public const float FirstAnnotationThreshold = 30f;
    public const string BeginnerAnnotationId = "beginner_commentary";
    public const string AdaptiveAnnotationId = "adaptive_teaching";
    private static TechniqueCatalogData catalog;

    public static TechniqueCatalogData Catalog
    {
        get
        {
            if (catalog != null) return catalog;
            TextAsset asset = Resources.Load<TextAsset>(CatalogResourcePath);
            catalog = asset == null ? new TechniqueCatalogData() :
                JsonConvert.DeserializeObject<TechniqueCatalogData>(asset.text);
            return catalog ?? (catalog = new TechniqueCatalogData());
        }
    }

    public static void ResetCatalogForTests() => catalog = null;

    public static TechniqueDefinition Get(string techniqueId) =>
        Catalog.techniques.FirstOrDefault(item => item != null && item.id == techniqueId);

    public static TechniqueUnderstandingStage PersonalStage(float progress)
    {
        if (progress >= 90f) return TechniqueUnderstandingStage.Perfected;
        if (progress >= 60f) return TechniqueUnderstandingStage.Integrated;
        if (progress >= 30f) return TechniqueUnderstandingStage.Understanding;
        return TechniqueUnderstandingStage.Beginner;
    }

    public static SectTechniqueMasteryStage SectStage(SectTechniqueState state)
    {
        if (state == null) return SectTechniqueMasteryStage.Initial;
        if (state.masteryProgress >= 90f) return SectTechniqueMasteryStage.GreatAchievement;
        if (state.masteryProgress >= 60f) return SectTechniqueMasteryStage.Profound;
        if (state.masteryProgress >= FirstAnnotationThreshold && state.firstAnnotationResolved)
            return SectTechniqueMasteryStage.Proficient;
        return SectTechniqueMasteryStage.Initial;
    }

    public static string PersonalStageName(TechniqueUnderstandingStage stage)
    {
        switch (stage)
        {
            case TechniqueUnderstandingStage.Understanding: return "理解";
            case TechniqueUnderstandingStage.Integrated: return "融汇";
            case TechniqueUnderstandingStage.Perfected: return "圆满";
            default: return "初学";
        }
    }

    public static string SectStageName(SectTechniqueState state)
    {
        if (state != null && state.masteryProgress >= FirstAnnotationThreshold && !state.firstAnnotationResolved)
            return "初窥·待抉择";
        switch (SectStage(state))
        {
            case SectTechniqueMasteryStage.Proficient: return "通达";
            case SectTechniqueMasteryStage.Profound: return "精深";
            case SectTechniqueMasteryStage.GreatAchievement: return "大成";
            default: return "初窥";
        }
    }

    public static PersonalTechniqueProgress Progress(CharacterState character, string techniqueId)
    {
        return character?.techniqueProgresses?.FirstOrDefault(item => item != null && item.techniqueId == techniqueId);
    }

    public static float MainUnderstanding(CharacterState character) =>
        Progress(character, character?.mainTechniqueId)?.understanding ?? 0f;

    public static TechniqueDefinition MainTechnique(CharacterState character) => Get(character?.mainTechniqueId);

    public static SectTechniqueState SectState(PlayerData player, string techniqueId) =>
        player?.techniqueLibrary?.FirstOrDefault(item => item != null && item.techniqueId == techniqueId);

    public static bool HasAnyTag(CharacterState character, IEnumerable<string> tags)
    {
        TechniqueDefinition technique = MainTechnique(character);
        if (technique?.tags == null || tags == null) return false;
        HashSet<string> requested = new HashSet<string>(tags.Where(item => !string.IsNullOrWhiteSpace(item)),
            StringComparer.OrdinalIgnoreCase);
        return technique.tags.Any(requested.Contains);
    }

    public static float ApplicationScore(CharacterState character, IEnumerable<string> actionTags)
    {
        TechniqueDefinition technique = MainTechnique(character);
        if (technique?.applicationBiases == null || actionTags == null) return 0f;
        HashSet<string> tags = new HashSet<string>(actionTags.Where(item => !string.IsNullOrWhiteSpace(item)),
            StringComparer.OrdinalIgnoreCase);
        float score = technique.applicationBiases.Where(item => item != null && tags.Contains(item.tag))
            .Sum(item => item.score);
        return Mathf.Clamp(score, -1.5f, 1.5f);
    }

    public static float RootAffinity(SpiritRootData root, TechniqueElementProfile profile)
    {
        if (root == null || profile == null) return 0.2f;
        return Mathf.Clamp01(root.gold * profile.metal + root.wood * profile.wood +
            root.water * profile.water + root.fire * profile.fire + root.earth * profile.earth);
    }

    public static float SoftCompatibility(float rootAffinity, float environmentAffinity)
    {
        float average = Mathf.Clamp01((rootAffinity + environmentAffinity) * 0.5f);
        float normalized = average < 0.2f ? (average - 0.2f) / 0.2f : (average - 0.2f) / 0.8f;
        // 当前内容量较少，首版采用 0.9–1.1 柔性修正；内容丰富后重新评估强适配惩罚。
        return Mathf.Clamp(1f + 0.1f * normalized, 0.9f, 1.1f);
    }

    public static float LearningAnnotationMultiplier(CharacterState character, SectTechniqueState sectState)
    {
        if (character == null || sectState?.annotationIds == null) return 1f;
        float understanding = MainUnderstanding(character);
        if (sectState.annotationIds.Contains(BeginnerAnnotationId) && understanding < 30f) return 1.25f;
        TechniqueDefinition technique = MainTechnique(character);
        if (sectState.annotationIds.Contains(AdaptiveAnnotationId) &&
            RootAffinity(character.spiritRoot, technique?.elements) >= 0.4f) return 1.2f;
        return 1f;
    }

    public static float StageContributionMultiplier(float understanding)
    {
        switch (PersonalStage(understanding))
        {
            case TechniqueUnderstandingStage.Understanding: return 1f;
            case TechniqueUnderstandingStage.Integrated: return 1.25f;
            case TechniqueUnderstandingStage.Perfected: return 1.5f;
            default: return 0.75f;
        }
    }
}
