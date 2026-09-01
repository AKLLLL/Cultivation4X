using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 玩家数据
/// </summary>
[System.Serializable]
public class PlayerData
{
    public string sectId;
    public string sectName;
    public int foundedDay;
    public int influenceRadius;

    public int reputation = 0;
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<FacilityType> availableFacilities = new List<FacilityType>
    {
        FacilityType.MissionHall,
        FacilityType.Warehouse,
        FacilityType.TrainingRoom,
        FacilityType.SecretRealm,
        FacilityType.AlchemyRoom,
        FacilityType.ProtectionArray,
        FacilityType.InheritanceChamber,
        FacilityType.ForgeRoom,
        FacilityType.FormationPlatform
    };
    public List<SectDepartmentState> departments = new List<SectDepartmentState>();
    public int nextDepartmentSequence = 1;
    public List<MonthlyPlanTemplate> monthlyPlanTemplates = new List<MonthlyPlanTemplate>();
    public List<SectTechniqueState> techniqueLibrary = new List<SectTechniqueState>();
    public SectGrowthFeedbackState growthFeedback = new SectGrowthFeedbackState();
    public FoundingState founding = new FoundingState { initialized = true, sectCreated = true, completed = true, stage = FoundingStage.Completed };
}
