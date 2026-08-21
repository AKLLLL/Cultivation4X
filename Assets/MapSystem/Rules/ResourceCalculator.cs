using System;
using System.Linq;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    public static class ResourceCalculator
    {
        public static int Calculate(WorldMap map, WorldMapProgressState progress,
            ResourceNodeRuntime node, ResourceNodeDefinition definition, out float nextRemainder)
        {
            nextRemainder = node == null ? 0f : Mathf.Clamp01(node.productionRemainder);
            if (map?.cells == null || node == null || definition == null ||
                node.cellIndex < 0 || node.cellIndex >= map.cells.Length) return 0;
            WorldCell cell = map.cells[node.cellIndex];
            if (definition.biomeRequirements != null && definition.biomeRequirements.Count > 0 &&
                !definition.biomeRequirements.Contains(cell.biome)) return 0;
            MapRegionData region = map.regions?.FirstOrDefault(item => item?.regionId == node.regionId);
            float aura = region?.auraTrend == MapRegionTrend.Low ? 0.8f :
                region?.auraTrend == MapRegionTrend.High ? 1.2f : 1f;
            float vein = 1f;
            if (definition.requiresVeinElement)
            {
                SpiritualVeinRuntime match = progress?.spiritualVeins?
                    .Where(item => item != null && item.regionId == node.regionId &&
                                   item.element == definition.requiredVeinElement)
                    .OrderByDescending(item => item.grade)
                    .ThenBy(item => item.veinId, StringComparer.Ordinal).FirstOrDefault();
                SpiritualVeinDefinition veinDefinition = ResourceDefinitionDatabase.GetVein(match?.definitionId);
                if (veinDefinition != null) vein = Mathf.Max(1f, veinDefinition.outputMultiplier);
            }
            float raw = Mathf.Max(0, definition.baseOutput) * aura * vein + nextRemainder;
            int result = Mathf.FloorToInt(raw + 0.0001f);
            nextRemainder = Mathf.Clamp(raw - result, 0f, 0.9999f);
            return result;
        }
    }
}
