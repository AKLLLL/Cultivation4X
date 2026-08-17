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
        public const string PlayerSectId = "world_location_player_sect";

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
                        actionType = LocationActionType.Explore,
                        displayName = "派遣弟子调查",
                        cost = 0,
                        available = true
                    },
                    new LocationAction
                    {
                        id = "village_labor",
                        actionType = LocationActionType.ManageLabor,
                        displayName = "管理劳动力",
                        cost = 0,
                        available = true
                    },
                    new LocationAction
                    {
                        id = "village_status",
                        actionType = LocationActionType.ViewStatus,
                        displayName = "查看村庄状态",
                        cost = 0,
                        available = true
                    }
                },
                availableMissionIds = new List<string>
                {
                    "founding_village_help",
                    "founding_village_preach",
                    "qingshi_threat_investigation",
                    "combat_001"
                }
            };

            map.locations[location.id] = location;
            cell.locationId = location.id;
            return location;
        }

        public static WorldLocation CreatePlayerSect(WorldMap map, int sectCellIndex,
            string sectName)
        {
            if (map?.cells == null || sectCellIndex < 0 || sectCellIndex >= map.cells.Length)
                return null;
            if (map.GetLocation(PlayerSectId) != null) return map.GetLocation(PlayerSectId);
            WorldCell cell = map.cells[sectCellIndex];
            var location = new WorldLocation
            {
                id = PlayerSectId,
                type = LocationType.Sect,
                position = new Vector2Int(cell.coord.col, cell.coord.row),
                name = string.IsNullOrWhiteSpace(sectName) ? "玩家宗门" : sectName,
                ownerId = "player_sect",
                level = 1,
                state = LocationState.Active,
                availableActions = new List<LocationAction>
                {
                    new LocationAction
                    {
                        id = "sect_manage",
                        actionType = LocationActionType.ManageSect,
                        displayName = "进入宗门管理",
                        cost = 0,
                        available = true
                    },
                    new LocationAction
                    {
                        id = "sect_status",
                        actionType = LocationActionType.ViewStatus,
                        displayName = "查看宗门状态",
                        cost = 0,
                        available = true
                    }
                }
            };
            map.locations[location.id] = location;
            cell.locationId = location.id;
            return location;
        }
        /// <summary>
        /// 把既有 MapSiteData 内容同步为 WorldLocation 门面。
        /// MapSiteData 仍负责真实玩法数据；WorldLocation 只负责地图展示与行动入口。
        /// </summary>
        public static void SynchronizeFromMapSites(WorldMap map, WorldMapProgressState progress)
        {
            if (map?.cells == null || progress?.mapSites == null) return;
            if (map.locations == null) map.locations = new Dictionary<string, WorldLocation>();
            HashSet<string> activeIds = new HashSet<string>();
            foreach (MapSiteData site in progress.mapSites)
            {
                if (site == null || site.siteType == MapSiteType.SectBase) continue;
                if (site.siteType == MapSiteType.Village && map.GetLocation(StarterVillageId) != null)
                    continue;
                if (site.cellIndex < 0 || site.cellIndex >= map.cells.Length) continue;
                LocationType type = MapLocationType(site.siteType);
                if (type == LocationType.None) continue;
                string id = "world_location_" + site.siteId;
                WorldLocation location;
                if (map.locations.TryGetValue(id, out location))
                {
                    location.sourceMapSiteId = site.siteId;
                    location.type = type;
                    location.name = string.IsNullOrWhiteSpace(site.siteName)
                        ? MapSiteDefaultName(site.siteType)
                        : site.siteName;
                    location.ownerId = site.ownerSectId;
                    location.state = LocationStateFromSite(site);
                    location.availableActions = location.availableActions ??
                        new List<LocationAction>();
                    location.availableMissionIds = location.availableMissionIds ??
                        new List<string>();
                }
                else
                {
                    location = new WorldLocation
                    {
                        id = id,
                        type = type,
                        name = string.IsNullOrWhiteSpace(site.siteName)
                            ? MapSiteDefaultName(site.siteType)
                            : site.siteName,
                        ownerId = site.ownerSectId,
                        level = 1,
                        state = LocationStateFromSite(site),
                        sourceMapSiteId = site.siteId,
                        availableActions = new List<LocationAction>
                        {
                            new LocationAction
                            {
                                id = site.siteId + "_status",
                                actionType = LocationActionType.ViewStatus,
                                displayName = "查看地点状态",
                                cost = 0,
                                available = true
                            }
                        },
                        availableMissionIds = MapSiteDefaultMissionIds(site.siteType)
                    };
                    map.locations[id] = location;
                }
                BindLocationToCell(map, location, site.cellIndex);
                activeIds.Add(id);
            }

            // 清理已不存在 MapSite 的旧门面（不清理手建青石村/宗门）。
            foreach (string key in map.locations.Keys.ToList())
            {
                WorldLocation stale = map.locations[key];
                if (stale == null || string.IsNullOrEmpty(stale.sourceMapSiteId) ||
                    activeIds.Contains(key)) continue;
                UnbindLocationFromCell(map, stale);
                map.locations.Remove(key);
            }
        }

        /// <summary>
        /// WorldLocation 是否已向玩家揭示。手建青石村/宗门总是可见；
        /// 从 MapSiteData 同步的内容地点必须 Discovered 才可见，避免泄露隐藏地点。
        /// </summary>
        public static bool IsLocationRevealed(WorldLocation location, WorldMapProgressState progress)
        {
            if (location == null) return false;
            if (string.IsNullOrEmpty(location.sourceMapSiteId)) return true;
            MapSiteData site = progress?.mapSites?.FirstOrDefault(item =>
                item != null && item.siteId == location.sourceMapSiteId);
            return site != null && site.revealState == MapContentRevealState.Discovered;
        }

        private static void BindLocationToCell(WorldMap map, WorldLocation location, int cellIndex)
        {
            int oldIndex = map.GetIndex(new HexCoord(location.position.x, location.position.y));
            if (oldIndex >= 0 && oldIndex < map.cells.Length && oldIndex != cellIndex &&
                map.cells[oldIndex].locationId == location.id)
                map.cells[oldIndex].locationId = null;
            location.position = new Vector2Int(map.cells[cellIndex].coord.col,
                map.cells[cellIndex].coord.row);
            map.cells[cellIndex].locationId = location.id;
        }

        private static void UnbindLocationFromCell(WorldMap map, WorldLocation location)
        {
            int index = map.GetIndex(new HexCoord(location.position.x, location.position.y));
            if (index >= 0 && index < map.cells.Length && map.cells[index].locationId == location.id)
                map.cells[index].locationId = null;
        }

        private static LocationType MapLocationType(MapSiteType siteType)
        {
            switch (siteType)
            {
                case MapSiteType.Village: return LocationType.Village;
                case MapSiteType.SpiritSpring:
                case MapSiteType.SpiritMine:
                case MapSiteType.CaveResidence:
                    return LocationType.ResourceNode;
                case MapSiteType.BeastLair: return LocationType.MonsterNest;
                case MapSiteType.Ruin: return LocationType.Ruins;
                default: return LocationType.None;
            }
        }

        private static LocationState LocationStateFromSite(MapSiteData site)
        {
            if (site == null) return LocationState.Active;
            if (site.siteType == MapSiteType.BeastLair && site.siteState == MapSiteState.Developed)
                return LocationState.Inactive;
            return LocationState.Active;
        }

        private static List<string> MapSiteDefaultMissionIds(MapSiteType siteType)
        {
            switch (siteType)
            {
                case MapSiteType.SpiritSpring:
                case MapSiteType.SpiritMine:
                    return new List<string> { "resource_001" };
                case MapSiteType.CaveResidence:
                case MapSiteType.Ruin:
                    return new List<string> { "exploration_001" };
                case MapSiteType.BeastLair:
                    return new List<string> { "combat_001" };
                case MapSiteType.Village:
                    return new List<string>
                    {
                        "founding_village_help",
                        "founding_village_preach",
                        "qingshi_threat_investigation",
                        "combat_001"
                    };
                default:
                    return new List<string>();
            }
        }

        private static string MapSiteDefaultName(MapSiteType siteType)
        {
            switch (siteType)
            {
                case MapSiteType.Village: return "村庄";
                case MapSiteType.SpiritSpring: return "灵泉";
                case MapSiteType.SpiritMine: return "灵矿";
                case MapSiteType.CaveResidence: return "洞府";
                case MapSiteType.BeastLair: return "兽巢";
                case MapSiteType.Ruin: return "遗迹";
                default: return "未知地点";
            }
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
