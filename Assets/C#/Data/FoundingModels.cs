using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public enum FoundingStage
{
    CandidateSelection,
    TechniqueSelection,
    Cave,
    Completed
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
}

[Serializable]
public class FoundingState
{
    public bool initialized;
    public bool completed;
    public FoundingStage stage;
    public int candidateSeed;
    public List<FounderCandidateData> candidates = new List<FounderCandidateData>();
    public List<string> selectedFounderIds = new List<string>();
    public string selectedTechniqueId;
    public int techniqueUnderstanding;
    public bool techniqueMilestoneQueued;
    public bool techniqueMilestoneResolved;
    public VillageState village = new VillageState();
}

[Serializable]
public class FoundingTechniqueDefinition
{
    public string id;
    public string name;
    public string description;
    public FacilityType unlockFacility;
    public string milestoneEventId;
    public string buildMissionId;
    public string actionMissionId;
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
                physique = random.Next(5, 21),
                aptitudeRank = aptitude,
                personalityTraitId = personalities[random.Next(personalities.Length)],
                initialFeatureId = Catalog.features.Count == 0 ? null : Catalog.features[random.Next(Catalog.features.Count)].id
            });
        }
        return result;
    }

    public static int UnderstandingGain(FounderCandidateData candidate, bool hasInheritanceChamber) =>
        candidate == null ? 0 : 1 + Mathf.Max(0, candidate.comprehension) / 10 + (hasInheritanceChamber ? 1 : 0);

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
