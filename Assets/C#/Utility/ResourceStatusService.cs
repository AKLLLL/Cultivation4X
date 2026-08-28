using System;
using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;

/// <summary>资源状态面板的一行只读数据。不含 UI 引用，便于 Editor 测试。</summary>
public sealed class ResourceStatusRow
{
    public string nodeId;
    public string siteId;
    public string siteName;
    public string siteTypeLabel;
    public string statusLabel;
    public string influenceLabel;
    public bool developmentRequirementMet;
    public int baseOutput;
    public int expectedOutput;
    public int lastSettledMonth;
    public int lastSettledLost;
    public int warehouseCount;
    public string resourceId;
    public string resourceName;
}

public static class ResourceStatusService
{
    public static int GetCountByTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || WarehouseManager.Instance == null) return 0;
        int total = 0;
        foreach (ItemStack stack in WarehouseManager.Instance.GetWarehouseData()?.items ?? new List<ItemStack>())
        {
            if (stack == null || stack.count <= 0) continue;
            ItemData definition = ItemDatabase.Instance?.GetItem(stack.itemId);
            bool matches = definition?.tags != null && definition.tags.Contains(tag);
            if (!matches) matches = FallbackTag(stack.itemId) == tag;
            if (matches) total += stack.count;
        }
        return total;
    }

    public static bool IsBelow(string tag, int threshold) => GetCountByTag(tag) < Math.Max(0, threshold);

    public static float Scarcity(string tag, float threshold)
    {
        if (threshold <= 0f) return 0f;
        int count = GetCountByTag(tag);
        if (count <= 0) return 1f;
        if (count >= threshold) return 0f;
        return 1f - count / threshold;
    }

    /// <summary>
    /// 只读汇总已发现（Discovered）的资源地点。Hidden/Hinted 不进入列表，
    /// 避免在面板泄露问号内容。本方法不产生资源、不修改地图进度。
    /// </summary>
    public static List<ResourceStatusRow> BuildDiscoveredNodeRows(WorldMap map,
        WorldMapProgressState progress)
    {
        List<ResourceStatusRow> rows = new List<ResourceStatusRow>();
        if (map?.cells == null || progress == null) return rows;

        foreach (ResourceNodeRuntime node in progress.resourceNodes ?? new List<ResourceNodeRuntime>())
        {
            if (node == null) continue;
            MapSiteData site = progress.mapSites?.FirstOrDefault(item => item?.siteId == node.siteId);
            if (site == null || site.revealState != MapContentRevealState.Discovered) continue;
            ResourceNodeDefinition definition = ResourceDefinitionDatabase.GetNode(node.definitionId);
            if (definition == null) continue;

            bool developed = site.siteState == MapSiteState.Developed;
            CellInfluenceRuntimeState influence = WorldMapInfluenceRules.GetCellState(map, progress, site.cellIndex);
            int expected = developed
                ? ResourceCalculator.Calculate(map, progress, node, definition, out _)
                : 0;
            rows.Add(new ResourceStatusRow
            {
                nodeId = node.nodeId,
                siteId = site.siteId,
                siteName = string.IsNullOrWhiteSpace(site.siteName) ? node.nodeId : site.siteName,
                siteTypeLabel = WorldMapContentRules.SiteTypeLabel(site.siteType),
                statusLabel = developed ? "已开发" : "已发现（未开发）",
                influenceLabel = InfluenceLevelLabel(influence.level),
                developmentRequirementMet = developed ||
                    WorldMapInfluenceRules.CanDevelopResource(map, progress, site.cellIndex),
                baseOutput = Math.Max(0, definition.baseOutput),
                expectedOutput = expected,
                lastSettledMonth = node.lastSettledMonth,
                lastSettledLost = node.lastSettledLost,
                warehouseCount = WarehouseManager.Instance == null
                    ? 0 : WarehouseManager.Instance.GetItemCount(definition.resourceId),
                resourceId = definition.resourceId,
                resourceName = ItemDatabase.Instance == null
                    ? definition.resourceId : ItemDatabase.Instance.GetItem(definition.resourceId)?.itemName ?? definition.resourceId
            });
        }
        return rows;
    }

    /// <summary>
    /// 下一个资源结算日的前一天。例如第 7 天 → 29；第 29 天 → 59。
    /// 只计算目标日；实际调试推进必须逐日走 TimeManager 的完整结算链。
    /// </summary>
    public static int NextSettlementEveDay(int currentDay)
    {
        int day = Math.Max(0, currentDay);
        int next = (day / ResourceManager.DaysPerMonth + 1) * ResourceManager.DaysPerMonth;
        if (day >= next - 1) next += ResourceManager.DaysPerMonth;
        return next - 1;
    }

    private static string InfluenceLevelLabel(InfluenceLevel level)
    {
        switch (level)
        {
            case InfluenceLevel.Core: return "核心";
            case InfluenceLevel.Influence: return "影响";
            case InfluenceLevel.Outer: return "外缘";
            default: return "无";
        }
    }

    private static string FallbackTag(string itemId)
    {
        if (itemId == FacilityRules.SpiritStoneId) return "spirit_stone";
        if (itemId == FacilityRules.BasicMaterialId) return "mundane_resource";
        return null;
    }
}
