using System;
using System.Collections.Generic;
using System.Linq;

namespace Cultivation4X.WorldMap
{
    public static class ResourceEcologyRules
    {
        public const string QingmuSiteId = "map_site_resource_node";

        public static void EnsureRuntime(WorldMap map, WorldMapProgressState progress)
        {
            if (map?.cells == null || progress == null) return;
            if (progress.resourceNodes == null) progress.resourceNodes = new List<ResourceNodeRuntime>();
            if (progress.spiritualVeins == null) progress.spiritualVeins = new List<SpiritualVeinRuntime>();

            List<SpiritualVeinRuntime> derived = new List<SpiritualVeinRuntime>();
            foreach (MapRegionData region in map.regions ?? new List<MapRegionData>())
            foreach (SpiritVein vein in map.spiritVeins ?? new List<SpiritVein>())
            {
                if (region == null || vein == null || string.IsNullOrWhiteSpace(region.regionId) ||
                    string.IsNullOrWhiteSpace(vein.id) || vein.pathCellIndices == null ||
                    !vein.pathCellIndices.Any(region.cellIndices.Contains)) continue;
                int grade = vein.size == SpiritVeinSize.Large ? 2 : 1;
                derived.Add(new SpiritualVeinRuntime
                {
                    veinId = $"regional_vein:{region.regionId}:{vein.id}",
                    sourceVeinId = vein.id,
                    definitionId = grade == 2 ? "natural_grade_2" : "natural_grade_1",
                    regionId = region.regionId,
                    element = vein.primaryElement,
                    grade = grade,
                    origin = SpiritualVeinOrigin.Natural
                });
            }
            progress.spiritualVeins = derived.OrderBy(item => item.veinId, StringComparer.Ordinal).ToList();

            EnsureNode(map, progress, progress.mapSites?.FirstOrDefault(site =>
                site != null && site.siteType == MapSiteType.ResourceNode), "qingmu_forest_herbs");
            EnsureNode(map, progress, progress.mapSites?.FirstOrDefault(site =>
                site != null && site.siteType == MapSiteType.SpiritMine), "spirit_mine_stones");
            progress.resourceNodes = progress.resourceNodes
                .Where(node => node != null && progress.mapSites.Any(site => site?.siteId == node.siteId))
                .OrderBy(node => node.nodeId, StringComparer.Ordinal).ToList();
        }

        private static void EnsureNode(WorldMap map, WorldMapProgressState progress,
            MapSiteData site, string definitionId)
        {
            if (site == null || !WorldMapProgressRules.IsValidCell(map, site.cellIndex)) return;
            ResourceNodeRuntime node = progress.resourceNodes.FirstOrDefault(item => item?.siteId == site.siteId);
            if (node == null)
            {
                node = new ResourceNodeRuntime { nodeId = "resource_node:" + site.siteId, siteId = site.siteId };
                progress.resourceNodes.Add(node);
            }
            node.definitionId = definitionId;
            node.cellIndex = site.cellIndex;
            node.regionId = map.cells[site.cellIndex].regionId;
        }

        public static bool IsVeinDiscovered(WorldMapProgressState progress, SpiritualVeinRuntime vein)
        {
            if (progress?.mapSites == null || progress.resourceNodes == null || vein == null) return false;
            return progress.resourceNodes.Where(node => node?.regionId == vein.regionId)
                .Select(node => progress.mapSites.FirstOrDefault(site => site?.siteId == node.siteId))
                .Any(site => site != null && site.revealState == MapContentRevealState.Discovered);
        }
    }
}
