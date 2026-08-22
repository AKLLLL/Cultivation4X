using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public enum FoundingStage
{
    WorldSelection = -1,
    CandidateSelection = 0,
    TechniqueSelection = 1,
    SectConfirmation = 2,
    Cave = 3,
    Completed = 4
}

public enum FoundingActionKind
{
    None,
    RepairFacility,
    VillagePreach,
    VillageHelp,
    BuildRouteFacility,
    LaborGather,
    LaborBuild,
    LaborCultivate,
    RouteAlchemy,
    RouteForge,
    RouteFormation
}

public enum TechniqueEffectType
{
    CultivationGainFlat,
    CombatPowerFlat,
    TechniqueUnderstandingGainFlat,
    MatchedMissionScoreFlat
}

[Serializable]
public class FounderCandidateData
{
    public string candidateId;
    public string displayName;
    public int age;
    public int attack;
    public int intelligence;
    public int agility;
    public int comprehension;
    public int combatComprehension;
    public int physique;
    public int aptitudeRank;
    public string personalityTraitId;
    public string initialFeatureId;
}

[Serializable]
public class VillageState
{
    public string villageId = "qingshi_village";
    public string displayName = "青石村";
    public int population = 100;
    public int relation;
    public int totalLabor;
    public int reservedLabor;
    public bool milestoneEventQueued;
    public bool supportLaborGranted;
}

[Serializable]
public class FoundingState
{
    public bool initialized;
    /// <summary>宗门是否已经真实成立（选址完成即成立）。真实状态，不与流程阶段 stage 强绑定。</summary>
    public bool sectCreated;
    public bool completed;
    public FoundingStage stage;
    public int candidateSeed;
    public int worldSeed;
    public int selectedWorldCellIndex = -1;
    public string pendingSectName;
    public List<FounderCandidateData> candidates = new List<FounderCandidateData>();
    public List<string> selectedFounderIds = new List<string>();
    public string selectedTechniqueId;
    public int techniqueUnderstanding;
    public bool techniqueMilestoneQueued;
    public bool techniqueMilestoneResolved;
    public VillageState village = new VillageState();
    public ActiveThreatState externalThreat = new ActiveThreatState();
}

[Serializable]
public class FoundingTechniqueDefinition
{
    public string id;
    public string name;
    public string description;
    public List<string> tags = new List<string>();
    public List<TechniqueEffectDefinition> effects = new List<TechniqueEffectDefinition>();
    public FacilityType unlockFacility;
    public string milestoneEventId;
    public string buildMissionId;
    public string actionMissionId;
}

[Serializable]
public class TechniqueEffectDefinition
{
    public TechniqueEffectType type;
    public int amount;
    public int requiredUnderstanding;
}

[Serializable]
public class FoundingFeatureDefinition
{
    public string id;
    public string name;
    public string description;
}

[Serializable]
public class FoundingCatalogData
{
    public List<string> surnames = new List<string>();
    public List<string> givenNames = new List<string>();
    public List<FoundingFeatureDefinition> features = new List<FoundingFeatureDefinition>();
    public List<FoundingTechniqueDefinition> techniques = new List<FoundingTechniqueDefinition>();
}

public static class FoundingRules
{
    public const int MaxUnderstanding = 100;
    public const int TechniqueMilestone = 50;
    public const int VillageFamiliarRelation = 20;
    public const int VillageSupportRelation = 50;
    public const int VillageLabor = 50;
    public const string CatalogResourcePath = "Configs/Founding/founding";

    private static FoundingCatalogData catalog;

    public static FoundingCatalogData Catalog
    {
        get
        {
            if (catalog != null) return catalog;
            TextAsset asset = Resources.Load<TextAsset>(CatalogResourcePath);
            catalog = asset == null ? new FoundingCatalogData() : JsonConvert.DeserializeObject<FoundingCatalogData>(asset.text);
            return catalog ?? (catalog = new FoundingCatalogData());
        }
    }

    public static void ResetCatalogForTests() => catalog = null;

    public static bool HasReachedCave(FoundingState state) =>
        state != null && (state.stage == FoundingStage.Cave || state.stage == FoundingStage.Completed);

    public static FoundingTechniqueDefinition GetTechnique(string id) =>
        Catalog.techniques.FirstOrDefault(item => item.id == id);

    public static FoundingFeatureDefinition GetFeature(string id) =>
        Catalog.features.FirstOrDefault(item => item.id == id);

    public static List<FounderCandidateData> GenerateCandidates(int seed)
    {
        System.Random random = new System.Random(seed);
        List<string> names = new List<string>();
        foreach (string surname in Catalog.surnames)
            foreach (string given in Catalog.givenNames)
                names.Add(surname + given);

        for (int i = names.Count - 1; i > 0; i--)
        {
            int swap = random.Next(i + 1);
            string value = names[i];
            names[i] = names[swap];
            names[swap] = value;
        }

        string[] personalities = { "cautious", "reckless", "kind", "selfish", "diligent", "lazy", "loyal", "ambitious" };
        List<FounderCandidateData> result = new List<FounderCandidateData>();
        for (int i = 0; i < 10; i++)
        {
            int aptitude = i == 0 ? 4 : random.Next(1, 6);
            result.Add(new FounderCandidateData
            {
                candidateId = $"founder_{seed:x8}_{i:00}",
                displayName = names.Count > i ? names[i] : $"候选弟子{i + 1}",
                age = random.Next(15, 19),
                attack = random.Next(5, 21),
                intelligence = random.Next(5, 21),
                agility = random.Next(5, 21),
                comprehension = random.Next(5, 21),
                combatComprehension = random.Next(5, 21),
                physique = random.Next(5, 21),
                aptitudeRank = aptitude,
                personalityTraitId = personalities[random.Next(personalities.Length)],
                initialFeatureId = Catalog.features.Count == 0 ? null : Catalog.features[random.Next(Catalog.features.Count)].id
            });
        }
        return result;
    }

    public static int UnderstandingGain(FounderCandidateData candidate, bool hasInheritanceChamber) =>
        candidate == null ? 0 : 1 + Mathf.Max(0, candidate.comprehension) / 10 + (hasInheritanceChamber ? 1 : 0) +
            GetActiveEffectTotal(TechniqueEffectType.TechniqueUnderstandingGainFlat);

    public static int GetActiveEffectTotal(TechniqueEffectType type)
    {
        FoundingState state = PlayerManager.Instance?.playerData?.founding;
        return GetEffectTotal(state?.selectedTechniqueId, state?.techniqueUnderstanding ?? 0, type);
    }

    public static int GetEffectTotal(string techniqueId, int understanding, TechniqueEffectType type)
    {
        FoundingTechniqueDefinition technique = GetTechnique(techniqueId);
        return technique?.effects?
            .Where(effect => effect != null && effect.type == type && understanding >= effect.requiredUnderstanding)
            .Sum(effect => effect.amount) ?? 0;
    }

    public static bool HasAnyTag(string techniqueId, IEnumerable<string> tags)
    {
        FoundingTechniqueDefinition technique = GetTechnique(techniqueId);
        if (technique?.tags == null || tags == null) return false;
        HashSet<string> requested = new HashSet<string>(tags.Where(tag => !string.IsNullOrWhiteSpace(tag)),
            StringComparer.OrdinalIgnoreCase);
        return technique.tags.Any(requested.Contains);
    }

    public static string TechniqueTagName(string tag)
    {
        switch (tag?.ToLowerInvariant())
        {
            case "wood": return "木";
            case "herb": return "灵植";
            case "alchemy": return "丹道";
            case "recovery": return "恢复";
            case "fire": return "火";
            case "body": return "体修";
            case "combat": return "战斗";
            case "forge": return "炼器";
            case "soul": return "神魂";
            case "formation": return "阵法";
            case "exploration": return "探索";
            case "research": return "研究";
            default: return tag ?? string.Empty;
        }
    }

    public static string TechniqueEffectDescription(TechniqueEffectDefinition effect)
    {
        if (effect == null) return string.Empty;
        string name;
        switch (effect.type)
        {
            case TechniqueEffectType.CultivationGainFlat: name = "旧修炼增益（炼气V1停用）"; break;
            case TechniqueEffectType.CombatPowerFlat: name = "战力"; break;
            case TechniqueEffectType.TechniqueUnderstandingGainFlat: name = "理解增长"; break;
            case TechniqueEffectType.MatchedMissionScoreFlat: name = "匹配任务评分"; break;
            default: name = effect.type.ToString(); break;
        }
        return $"{name}+{effect.amount}";
    }

    public static string AptitudeName(int rank)
    {
        switch (rank)
        {
            case 1: return "凡品";
            case 2: return "下品";
            case 3: return "中品";
            case 4: return "上品";
            case 5: return "天品";
            default: return "未评定";
        }
    }
}
