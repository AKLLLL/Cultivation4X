using System.Collections.Generic;
using System.Linq;

namespace Cultivation4X.WorldMap
{
    public static class ResourceManager
    {
        public const int DaysPerMonth = 30;

        public static List<ResourceProductionRecord> MonthUpdate(int currentDay, int monthIndex)
        {
            List<ResourceProductionRecord> records = new List<ResourceProductionRecord>();
            WorldMap map = WorldMapSession.Current;
            WorldMapProgressState progress = WorldMapSession.Progress;
            if (map?.cells == null || progress == null || WarehouseManager.Instance == null) return records;
            ResourceEcologyRules.EnsureRuntime(map, progress);
            foreach (ResourceNodeRuntime node in progress.resourceNodes ?? new List<ResourceNodeRuntime>())
            {
                if (node == null || node.lastSettledMonth >= monthIndex) continue;
                MapSiteData site = progress.mapSites?.FirstOrDefault(item => item?.siteId == node.siteId);
                if (!WorldMapContentEffects.IsSiteDeveloped(site) || site.lastUpdatedDay >= currentDay) continue;
                ResourceNodeDefinition definition = ResourceDefinitionDatabase.GetNode(node.definitionId);
                if (definition == null) continue;
                int calculated = ResourceCalculator.Calculate(map, progress, node, definition, out float remainder);
                node.productionRemainder = remainder;
                node.lastCalculatedOutput = calculated;
                node.lastSettledMonth = monthIndex;
                bool received = calculated > 0 && WarehouseManager.Instance.TryAddItem(definition.resourceId, calculated);
                node.lastSettledLost = received ? 0 : calculated;
                records.Add(new ResourceProductionRecord
                {
                    nodeId = node.nodeId,
                    siteName = site.siteName,
                    itemId = definition.resourceId,
                    calculated = calculated,
                    received = received ? calculated : 0,
                    lost = received ? 0 : calculated
                });
            }
            return records;
        }
    }
}
