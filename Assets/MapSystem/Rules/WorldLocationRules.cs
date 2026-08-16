using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 世界地点实体的创建与查询规则。第一阶段只生成青石村测试地点；
    /// 后续矿洞、遗迹、妖兽巢穴在此扩展。
    /// </summary>
    public static class WorldLocationRules
    {
        public const string StarterVillageId = "world_location_qingshi_village";

        /// <summary>
        /// 在宗门影响范围内选择一个非山体/水域的陆地格生成青石村。
        /// 优先一环，其次二环；随机由 effectiveSeed 派生，结果可复现。
        /// </summary>
        public static WorldLocation CreateStarterVillage(WorldMap map, int sectCellIndex)
        {
            if (map?.cells == null || sectCellIndex < 0 || sectCellIndex >= map.cells.Length)
                return null;
            if (map.GetLocation(StarterVillageId) != null) return map.GetLocation(StarterVillageId);

            int villageCell = SelectStarterVillageCell(map, sectCellIndex);
            if (villageCell < 0) return null;

            WorldCell cell = map.cells[villageCell];
            var location = new WorldLocation
            {
                id = StarterVillageId,
                type = LocationType.Village,
                position = new Vector2Int(cell.coord.col, cell.coord.row),
                name = "青石村",
                ownerId = "qingshi_village",
                level = 1,
                state = LocationState.Active,
                availableActions = new List<LocationAction>
                {
                    new LocationAction
                    {
                        id = "village_survey",
                        displayName = "派遣弟子调查",
                        cost = 0,
                        available = true
                    },
                    new LocationAction
                    {
                        id = "village_labor",
                        displayName = "管理劳动力",
                        cost = 0,
                        available = true
                    },
                    new LocationAction
                    {
                        id = "village_status",
                        displayName = "查看村庄状态",
                        cost = 0,
                        available = true
                    }
                }
            };

            map.locations[location.id] = location;
            cell.locationId = location.id;
            return location;
        }

        private static int SelectStarterVillageCell(WorldMap map, int sectCellIndex)
        {
            int[] firstRing = map.GetNeighborIndices(sectCellIndex)
                .Where(index => IsVillageCandidate(map, index, sectCellIndex))
                .ToArray();
            if (firstRing.Length > 0)
                return PickStable(map, firstRing, sectCellIndex);

            int[] secondRing = map.GetNeighborIndices(sectCellIndex)
                .SelectMany(map.GetNeighborIndices)
                .Where(index => IsVillageCandidate(map, index, sectCellIndex))
                .Distinct()
                .ToArray();
            return secondRing.Length > 0
                ? PickStable(map, secondRing, sectCellIndex)
                : -1;
        }

        private static bool IsVillageCandidate(WorldMap map, int index, int sectCellIndex)
        {
            if (index == sectCellIndex || index < 0 || index >= map.cells.Length) return false;
            WorldCell cell = map.cells[index];
            return cell != null && cell.isBuildable &&
                   cell.landform != LandformType.DeepWater &&
                   cell.landform != LandformType.ShallowWater &&
                   cell.landform != LandformType.Mountain &&
                   string.IsNullOrEmpty(cell.locationId);
        }

        private static int PickStable(WorldMap map, IReadOnlyList<int> candidates, int salt)
        {
            int roll = (int)((uint)(map.effectiveSeed * 397 ^ salt * 31) % (uint)candidates.Count);
            return candidates[Mathf.Clamp(roll, 0, candidates.Count - 1)];
        }
    }
}
