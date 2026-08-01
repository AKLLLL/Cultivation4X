using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Cultivation4X.WorldMap;

public enum ExplorationMissionKind
{
    None,
    Survey,
    Progress,
    Ongoing
}

[Serializable]
public class ExplorationRegionState
{
    public string regionId;
    public int stage;
}

[Serializable]
public class ExplorationMilestoneDefinition
{
    public string name;
    public string description;
}

[Serializable]
public class ExplorationRegionDefinition
{
    public string id;
    public int order;
    public string name;
    public string unknownDescription;
    public string description;
    public string progressMissionId;
    public string ongoingMissionId;
    public string firstProgressEventId;
    public List<ExplorationMilestoneDefinition> milestones = new List<ExplorationMilestoneDefinition>();
}

public static class ExplorationRules
{
    public const int MaxStage = 3;
    public const string SurveyMissionId = "exploration_survey";
    private static List<ExplorationRegionDefinition> cachedRegions;

    public static IReadOnlyList<ExplorationRegionDefinition> GetRegions()
    {
        if (cachedRegions == null)
        {
            cachedRegions = new List<ExplorationRegionDefinition>();
            foreach (TextAsset file in Resources.LoadAll<TextAsset>("Configs/ExplorationRegions"))
            {
                try
                {
                    List<ExplorationRegionDefinition> loaded = file.text.TrimStart().StartsWith("[")
                        ? JsonConvert.DeserializeObject<List<ExplorationRegionDefinition>>(file.text)
                        : new List<ExplorationRegionDefinition> { JsonConvert.DeserializeObject<ExplorationRegionDefinition>(file.text) };
                    if (loaded != null) cachedRegions.AddRange(loaded.Where(item => item != null));
                }
                catch (Exception exception)
                {
                    Debug.LogError($"探索区域配置解析失败 {file.name}: {exception.Message}");
                }
            }
            cachedRegions = cachedRegions.OrderBy(item => item.order).ThenBy(item => item.id).ToList();
        }
        return cachedRegions.AsReadOnly();
    }

    public static ExplorationRegionDefinition GetRegion(string regionId) =>
        GetRegions().FirstOrDefault(item => item.id == regionId);

    public static ExplorationRegionState GetState(string regionId)
    {
        PlayerData data = PlayerManager.Instance?.playerData;
        return data?.explorationRegions?.FirstOrDefault(item => item.regionId == regionId);
    }

    public static int GetMapCellIndex(string regionId)
    {
        WorldMap map = WorldMapSession.Current;
        if (map?.cells == null || map.pointsOfInterest == null || string.IsNullOrWhiteSpace(regionId))
            return -1;
        WorldPointOfInterest point = map.pointsOfInterest.FirstOrDefault(item => item?.id == regionId);
        return point != null && point.cellIndex >= 0 && point.cellIndex < map.cells.Length
            ? point.cellIndex
            : -1;
    }

    public static ExplorationRegionState DiscoverNextRegion()
    {
        PlayerData data = PlayerManager.Instance?.playerData;
        if (data == null) return null;
        if (data.explorationRegions == null) data.explorationRegions = new List<ExplorationRegionState>();
        ExplorationRegionDefinition next = GetRegions().FirstOrDefault(item => GetState(item.id) == null);
        if (next == null) return null;
        ExplorationRegionState state = new ExplorationRegionState { regionId = next.id, stage = 0 };
        data.explorationRegions.Add(state);
        return state;
    }

    public static bool TryAdvance(string regionId, out ExplorationRegionState state)
    {
        state = GetState(regionId);
        if (state == null || state.stage >= MaxStage) return false;
        state.stage++;
        return true;
    }

    public static bool HasUndiscoveredRegion() => GetRegions().Any(item => GetState(item.id) == null);

    public static void ClearCacheForTests() => cachedRegions = null;
}
