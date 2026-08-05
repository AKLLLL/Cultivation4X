using System;
using System.Collections.Generic;
using System.Linq;

namespace Cultivation4X.WorldMap
{
    [Serializable]
    public sealed class MapMissionContext
    {
        public MapActionType actionType;
        public int targetCellIndex = -1;
        public string targetSiteId;
    }

    public enum QiRevivalStage
    {
        Early = 0,
        Middle = 1,
        Late = 2
    }

    public static class WorldMapContentRules
    {
        public const string ExploreMissionId = "map_explore";
        public const string InvestigateSpiritSpringMissionId = "map_spirit_spring_investigate";
        public const string DevelopSpiritSpringMissionId = "map_spirit_spring_develop";
        public const string InvestigateActionId = "investigate";
        public const string DevelopActionId = "develop";
        public const string EstablishVillageRelationActionId = "establish_village_relation";
        public const string DevelopSpiritMineActionId = "develop_spirit_mine";
        public const string BuildCaveResidenceOutpostActionId = "build_cave_residence_outpost";
        public const string ClearBeastLairActionId = "clear_beast_lair";
        public const string InvestigateRuinActionId = "investigate_ruin";
        public const string EstablishVillageRelationMissionId = "map_village_relation";
        public const string DevelopSpiritMineMissionId = "map_spirit_mine_develop";
        public const string BuildCaveResidenceOutpostMissionId = "map_cave_residence_outpost";
        public const string ClearBeastLairMissionId = "map_beast_lair_clear";
        public const string InvestigateRuinMissionId = "map_ruin_investigate";

        private static readonly MapSiteType[] CandidateTypes =
        {
            MapSiteType.Village, MapSiteType.SpiritSpring, MapSiteType.SpiritMine,
            MapSiteType.CaveResidence, MapSiteType.BeastLair, MapSiteType.Ruin
        };

        public static void EnsureCandidates(WorldMap map, WorldMapProgressState progress,
            QiRevivalStage stage = QiRevivalStage.Early)
        {
            if (map?.cells == null || progress == null) return;
            CandidateRankingContext ranking = BuildRankingContext(map);
            if (progress.mapSites == null) progress.mapSites = new List<MapSiteData>();
            HashSet<int> occupied = new HashSet<int>(progress.mapSites
                .Where(site => site != null && WorldMapProgressRules.IsValidCell(map, site.cellIndex))
                .Select(site => site.cellIndex));
            foreach (MapSiteType type in CandidateTypes)
            {
                if (progress.mapSites.Any(site => site != null && site.siteType == type)) continue;
                int selected = FirstAvailable(RankedCells(map, ranking, type, stage), index => !occupied.Contains(index));
                if (selected < 0) continue;
                occupied.Add(selected);
                progress.mapSites.Add(CreateCandidate(type, selected));
            }
            progress.mapSites = progress.mapSites
                .OrderBy(site => site.siteType == MapSiteType.SectBase ? 0 : 1)
                .ThenBy(site => site.siteId, StringComparer.Ordinal).ToList();
        }

        public static bool TryPrepareSectBasePlacement(WorldMap map, WorldMapProgressState progress,
            int baseCellIndex, out string reason)
        {
            if (!WorldMapProgressRules.IsValidCell(map, baseCellIndex) || progress?.mapSites == null)
            { reason = "地图内容数据无效"; return false; }
            HashSet<int> occupied = new HashSet<int>(progress.mapSites
                .Where(site => site != null && site.cellIndex != baseCellIndex)
                .Select(site => site.cellIndex));
            CandidateRankingContext ranking = BuildRankingContext(map);
            var replacements = new Dictionary<MapSiteData, int>();
            foreach (MapSiteData site in progress.mapSites
                .Where(site => site != null && site.siteType != MapSiteType.SectBase && site.cellIndex == baseCellIndex)
                .OrderBy(site => site.siteId, StringComparer.Ordinal))
            {
                int replacement = FirstAvailable(RankedCells(map, ranking, site.siteType, QiRevivalStage.Early),
                    index => index != baseCellIndex && !occupied.Contains(index));
                if (replacement < 0) { reason = "没有可用于顺延地图内容的合法格子"; return false; }
                replacements[site] = replacement;
                occupied.Add(replacement);
            }

            MapSiteData spring = progress.mapSites.FirstOrDefault(site =>
                site != null && site.siteType == MapSiteType.SpiritSpring);
            if (spring == null) { reason = "灵泉候选缺失"; return false; }
            int currentSpringCell = replacements.TryGetValue(spring, out int moved) ? moved : spring.cellIndex;
            occupied.Remove(currentSpringCell);
            if (HexCoord.Distance(map.cells[baseCellIndex].coord, map.cells[currentSpringCell].coord) != 1)
            {
                List<int> neighbors = map.GetNeighborIndices(baseCellIndex)
                    .OrderByDescending(index => IsPreferredContentCell(map.cells[index]))
                    .ThenByDescending(index => ScoreSuitability(map, ranking, map.cells[index], MapSiteType.SpiritSpring,
                        QiRevivalStage.Early))
                    .ThenBy(index => StableUnsigned(map.effectiveSeed,
                        "spirit-spring-near-base-" + baseCellIndex + "-" + index))
                    .ThenBy(index => index).ToList();
                int near = neighbors.Where(index => !occupied.Contains(index)).DefaultIfEmpty(-1).First();
                if (near < 0 && neighbors.Count > 0)
                {
                    near = neighbors[0];
                    MapSiteData blocker = progress.mapSites.FirstOrDefault(site => site != null && site != spring &&
                        (replacements.TryGetValue(site, out int proposed) ? proposed : site.cellIndex) == near);
                    if (blocker != null)
                    {
                        occupied.Remove(near);
                        int relocated = FirstAvailable(RankedCells(map, ranking, blocker.siteType, QiRevivalStage.Early),
                            index => index != baseCellIndex && index != near && !occupied.Contains(index));
                        if (relocated < 0) { reason = "没有可用于腾挪相邻地图内容的合法格子"; return false; }
                        replacements[blocker] = relocated;
                        occupied.Add(relocated);
                    }
                }
                if (near < 0) { reason = "宗门驻地附近没有可用于灵泉闭环的合法格子"; return false; }
                replacements[spring] = near;
            }
            foreach (KeyValuePair<MapSiteData, int> replacement in replacements)
                replacement.Key.cellIndex = replacement.Value;
            reason = null;
            return true;
        }

        public static void RefreshHints(WorldMap map, WorldMapProgressState progress)
        {
            if (map?.cells == null || progress?.mapSites == null) return;
            foreach (MapSiteData site in progress.mapSites.Where(IsCandidate))
            {
                if (site.revealState != MapContentRevealState.Hidden) continue;
                CellInfluenceRuntimeState state = WorldMapInfluenceRules.GetCellState(map, progress, site.cellIndex);
                int chance = HintChance(state);
                if (chance <= 0) continue;
                uint roll = StableUnsigned(map.effectiveSeed, "map-content-hint-" + site.siteId) % 100u;
                if (roll < chance) site.revealState = MapContentRevealState.Hinted;
                SynchronizeLegacyFlags(site);
            }
        }

        public static bool CanExplore(WorldMap map, WorldMapProgressState progress, int cellIndex, out string reason)
        {
            if (!WorldMapProgressRules.IsValidCell(map, cellIndex)) { reason = "地图格无效"; return false; }
            if (progress == null) { reason = "地图进度不存在"; return false; }
            if (progress.exploredCellIndices?.Contains(cellIndex) == true) { reason = "该格已经探索完成"; return false; }
            reason = null;
            return true;
        }

        public static bool CanStartAction(WorldMap map, WorldMapProgressState progress,
            MapMissionContext context, out string reason)
        {
            if (context == null) { reason = "地图任务上下文缺失"; return false; }
            if (context.actionType == MapActionType.Explore)
                return CanExplore(map, progress, context.targetCellIndex, out reason);
            MapSiteData site = FindSite(progress, context.targetSiteId);
            if (site == null || site.cellIndex != context.targetCellIndex)
            { reason = "地图内容目标无效"; return false; }
            if (site.revealState != MapContentRevealState.Discovered)
            { reason = "地图内容尚未发现"; return false; }
            if (site.siteType != SiteTypeForAction(context.actionType))
            { reason = "地图行动与目标内容类型不一致"; return false; }
            if (context.actionType == MapActionType.InvestigateSpiritSpring)
            {
                if (site.siteState != MapSiteState.None) { reason = "灵泉已经调查"; return false; }
                if (!WorldMapInfluenceRules.CanInvestigate(map, progress, site.cellIndex))
                { reason = "灵泉不在可调查影响范围内"; return false; }
                reason = null; return true;
            }
            if (context.actionType == MapActionType.DevelopSpiritSpring)
            {
                if (site.siteState != MapSiteState.Investigated) { reason = "必须先调查灵泉"; return false; }
                if (!WorldMapInfluenceRules.CanDevelopResource(map, progress, site.cellIndex))
                { reason = "灵泉不在可开发影响范围内"; return false; }
                reason = null; return true;
            }
            MapSiteType expectedType = SiteTypeForAction(context.actionType);
            if (site.siteType != expectedType || site.siteState != MapSiteState.None)
            { reason = "该地图内容已经处理"; return false; }
            bool permitted = context.actionType == MapActionType.EstablishVillageRelation
                ? WorldMapInfluenceRules.CanEstablishVillageRelation(map, progress, site.cellIndex)
                : context.actionType == MapActionType.DevelopSpiritMine
                    ? WorldMapInfluenceRules.CanDevelopResource(map, progress, site.cellIndex)
                    : context.actionType == MapActionType.BuildCaveResidenceOutpost
                        ? WorldMapInfluenceRules.CanBuildOutpost(map, progress, site.cellIndex)
                        : context.actionType == MapActionType.ClearBeastLair
                            ? WorldMapInfluenceRules.CanClear(map, progress, site.cellIndex)
                            : context.actionType == MapActionType.InvestigateRuin &&
                              WorldMapInfluenceRules.CanInvestigate(map, progress, site.cellIndex);
            if (!permitted) { reason = "宗门影响力不足以执行该地图行动"; return false; }
            reason = null;
            return true;
        }

        public static bool CompleteSuccessfulAction(WorldMap map, WorldMapProgressState progress,
            MapMissionContext context, MissionResultTier tier, int currentDay, out string summary)
        {
            if (!CanStartAction(map, progress, context, out summary)) return false;
            if (context.actionType == MapActionType.Explore)
            {
                if (progress.exploredCellIndices == null) progress.exploredCellIndices = new List<int>();
                progress.exploredCellIndices.Add(context.targetCellIndex);
                WorldMapProgressRules.RevealCell(map, progress, context.targetCellIndex);
                MapSiteData candidate = progress.mapSites?.FirstOrDefault(site => IsCandidate(site) &&
                    site.cellIndex == context.targetCellIndex);
                if (candidate != null && tier == MissionResultTier.Excellent &&
                    (candidate.siteType == MapSiteType.SpiritSpring ||
                     DiscoveryRoll(map, candidate.siteId, candidate.cellIndex) < 65u))
                {
                    candidate.revealState = MapContentRevealState.Discovered;
                    candidate.discoveredDay = currentDay;
                    candidate.lastUpdatedDay = currentDay;
                    SynchronizeLegacyFlags(candidate);
                    summary = ExplorationSummary(map, context.targetCellIndex) + $"，发现{candidate.siteName}";
                }
                else summary = ExplorationSummary(map, context.targetCellIndex);
                return true;
            }
            MapSiteData site = FindSite(progress, context.targetSiteId);
            site.siteState = context.actionType == MapActionType.InvestigateSpiritSpring ||
                context.actionType == MapActionType.ClearBeastLair || context.actionType == MapActionType.InvestigateRuin
                ? MapSiteState.Investigated : MapSiteState.Developed;
            if (site.siteState == MapSiteState.Developed) site.ownerSectId = "player_sect";
            site.lastUpdatedDay = currentDay;
            RefreshAvailableActions(site);
            SynchronizeLegacyFlags(site);
            WorldMapContentEffects.ApplySiteCompletion(site, context.actionType, currentDay);
            summary = $"{SiteTypeLabel(site.siteType)}{(site.siteState == MapSiteState.Investigated ? "调查/清理" : "行动")}完成";
            return true;
        }

        public static Reward CreateReward(WorldMap map, MapMissionContext context)
        {
            Reward reward = new Reward();
            if (!WorldMapProgressRules.IsValidCell(map, context?.targetCellIndex ?? -1)) return reward;
            WorldCell cell = map.cells[context.targetCellIndex];
            if (context.actionType == MapActionType.Explore)
            {
                int danger = (int)WorldMapProgressRules.GetDanger(cell);
                int aura = cell.totalAura >= 1.2f ? 2 : cell.totalAura >= 0.6f ? 1 : 0;
                reward.Gold = 10 + danger * 5 + aura * 4;
                reward.Exp = 6 + danger * 3 + aura * 2;
                reward.Items.Add(new ItemReward { itemId = FacilityRules.BasicMaterialId, count = 1 + danger });
            }
            else if (context.actionType == MapActionType.InvestigateSpiritSpring)
            { reward.Gold = 20; reward.Exp = 15; }
            else if (context.actionType == MapActionType.DevelopSpiritSpring)
            {
                reward.Gold = 35; reward.Exp = 20;
                reward.Items.Add(new ItemReward { itemId = FacilityRules.BasicMaterialId, count = 3 });
            }
            else
            {
                switch (context.actionType)
                {
                    case MapActionType.EstablishVillageRelation: reward.Gold = 28; reward.Exp = 18; break;
                    case MapActionType.DevelopSpiritMine:
                        reward.Gold = 32; reward.Exp = 22;
                        reward.Items.Add(new ItemReward { itemId = FacilityRules.BasicMaterialId, count = 2 }); break;
                    case MapActionType.BuildCaveResidenceOutpost:
                        reward.Gold = 30; reward.Exp = 24;
                        reward.Items.Add(new ItemReward { itemId = FacilityRules.BasicMaterialId, count = 2 }); break;
                    case MapActionType.ClearBeastLair:
                        reward.Gold = 24; reward.Exp = 20;
                        reward.Items.Add(new ItemReward { itemId = FacilityRules.BasicMaterialId, count = 1 }); break;
                    case MapActionType.InvestigateRuin: reward.Gold = 26; reward.Exp = 25; break;
                }
            }
            return reward;
        }

        public static MapSiteData FindSite(WorldMapProgressState progress, string siteId) =>
            progress?.mapSites?.FirstOrDefault(site => site != null && site.siteId == siteId);

        public static string SiteTypeLabel(MapSiteType type)
        {
            switch (type)
            {
                case MapSiteType.Village: return "村落";
                case MapSiteType.SpiritSpring: return "灵泉";
                case MapSiteType.SpiritMine: return "灵矿";
                case MapSiteType.CaveResidence: return "洞府";
                case MapSiteType.BeastLair: return "兽巢";
                case MapSiteType.Ruin: return "遗迹";
                default: return "宗门驻地";
            }
        }

        public static string CandidateId(MapSiteType type) =>
            "map_site_" + type.ToString().ToLowerInvariant();

        public static IReadOnlyList<string> CandidateTags(MapSiteType type) => TagsFor(type);

        public static void SynchronizeLegacyFlags(MapSiteData site)
        {
            if (site == null) return;
            // Rebuild the action list before deriving the legacy interaction flag.
            // Callers commonly update reveal/state first and rely on this method to
            // synchronize both fields atomically; deriving canInteract from the
            // stale pre-refresh list leaves valid discovered sites non-interactive.
            RefreshAvailableActions(site);
            site.isRevealed = site.siteType == MapSiteType.SectBase || site.revealState == MapContentRevealState.Discovered;
            site.canInteract = site.siteType == MapSiteType.SectBase ||
                               (site.revealState == MapContentRevealState.Discovered && site.availableActionIds != null &&
                                site.availableActionIds.Count > 0);
        }

        private static MapSiteData CreateCandidate(MapSiteType type, int cellIndex)
        {
            MapSiteData site = new MapSiteData
            {
                siteId = CandidateId(type),
                cellIndex = cellIndex,
                siteType = type,
                siteName = SiteTypeLabel(type),
                revealState = MapContentRevealState.Hidden,
                siteState = MapSiteState.None,
                tags = TagsFor(type)
            };
            SynchronizeLegacyFlags(site);
            return site;
        }

        private static IEnumerable<int> RankedCells(WorldMap map, CandidateRankingContext ranking,
            MapSiteType type, QiRevivalStage stage) =>
            map.cells.Where(cell => cell != null)
                .OrderByDescending(IsPreferredContentCell)
                .ThenByDescending(cell => ScoreSuitability(map, ranking, cell, type, stage))
                .ThenBy(cell => StableUnsigned(map.effectiveSeed,
                    "map-content-" + type + "-" + cell.index))
                .ThenBy(cell => cell.index).Select(cell => cell.index);

        private static bool IsPreferredContentCell(WorldCell cell) => cell != null &&
            cell.landform != LandformType.DeepWater && cell.landform != LandformType.ShallowWater;

        private static int Suitability(WorldMap map, WorldCell cell, MapSiteType type, QiRevivalStage stage)
            => ScoreSuitability(map, BuildRankingContext(map), cell, type, stage);

        private static int ScoreSuitability(WorldMap map, CandidateRankingContext ranking,
            WorldCell cell, MapSiteType type, QiRevivalStage stage)
        {
            int score = (int)(cell.totalAura * 100f) + (int)stage * 5;
            bool vein = ranking.veinCells.Contains(cell.index);
            ranking.regionById.TryGetValue(cell.regionId ?? string.Empty, out MapRegionData region);
            MapRegionType? regionType = region?.regionType;
            switch (type)
            {
                case MapSiteType.Village:
                    score += cell.landform == LandformType.Plain ? 180 : 0;
                    score += regionType == MapRegionType.Plain || regionType == MapRegionType.Valley ? 120 : 0;
                    score += IsRegionEdge(map, cell) ? 35 : 0;
                    score += (int)((cell.elementalAura.earth + cell.elementalAura.wood) * 70f); break;
                case MapSiteType.SpiritSpring:
                    score += cell.biome == BiomeType.Wetland ? 220 : 0; score += vein ? 120 : 0;
                    score += regionType == MapRegionType.Valley || regionType == MapRegionType.Forest || regionType == MapRegionType.Swamp ? 120 : 0;
                    score += ranking.nearRiverCells.Contains(cell.index) ? 45 : 0;
                    score += (int)(cell.elementalAura.water * 140f); break;
                case MapSiteType.SpiritMine:
                    score += cell.landform == LandformType.Mountain ? 220 : 0; score += vein ? 150 : 0;
                    score += regionType == MapRegionType.MountainRange || regionType == MapRegionType.Hills || regionType == MapRegionType.SmallHill ? 120 : 0;
                    score += cell.internalPositionTag == MapInternalPositionTag.Ridge ? 45 : 0;
                    score += (int)(cell.elementalAura.metal * 140f); break;
                case MapSiteType.CaveResidence:
                    score += cell.landform == LandformType.Hill ? 200 : 0;
                    score += regionType == MapRegionType.MountainRange || regionType == MapRegionType.SmallHill ? 120 : 0;
                    score += cell.internalPositionTag == MapInternalPositionTag.Mountainside ? 45 : 0;
                    score += (int)(cell.elementalAura.earth * 120f); break;
                case MapSiteType.BeastLair:
                    score += cell.biome == BiomeType.TemperateForest || cell.biome == BiomeType.Rainforest ? 210 : 0;
                    score += regionType == MapRegionType.Forest || regionType == MapRegionType.MountainRange || regionType == MapRegionType.Swamp ? 120 : 0;
                    score += (int)((cell.elementalAura.wood + cell.elementalAura.fire) * 70f); break;
                case MapSiteType.Ruin:
                    score += cell.biome == BiomeType.Desert || cell.biome == BiomeType.Alpine ? 210 : 0;
                    score += regionType == MapRegionType.Desert || regionType == MapRegionType.Hills || regionType == MapRegionType.Valley ? 120 : 0;
                    score += (int)((cell.elementalAura.earth + cell.elementalAura.metal) * 70f); break;
            }
            return score;
        }

        private static string ExplorationSummary(WorldMap map, int cellIndex)
        {
            WorldCell cell = map?.cells != null && cellIndex >= 0 && cellIndex < map.cells.Length
                ? map.cells[cellIndex] : null;
            MapRegionData region = cell == null ? null : map.regions?.FirstOrDefault(item => item?.regionId == cell.regionId);
            if (cell == null || region == null) return "探索完成";
            return $"探索完成：{region.regionName}·{WorldMapRegionRules.PositionLabel(cell.internalPositionTag)}，{EnvironmentPhrase(cell)}";
        }

        private static string EnvironmentPhrase(WorldCell cell)
        {
            if (cell.landform == LandformType.Mountain) return "山势险峻";
            if (cell.landform == LandformType.Hill) return "丘势起伏";
            if (cell.biome == BiomeType.TemperateForest || cell.biome == BiomeType.Rainforest) return "林木繁茂";
            if (cell.biome == BiomeType.Wetland) return "水泽丰沛";
            if (cell.biome == BiomeType.Desert) return "地貌荒旷";
            if (cell.landform == LandformType.DeepWater || cell.landform == LandformType.ShallowWater) return "水域浩渺";
            return "地势开阔";
        }

        private static bool IsRegionEdge(WorldMap map, WorldCell cell) =>
            map.GetNeighborIndices(cell.index).Any(index => map.cells[index].regionId != cell.regionId);

        private static CandidateRankingContext BuildRankingContext(WorldMap map)
        {
            var context = new CandidateRankingContext();
            foreach (MapRegionData region in map?.regions ?? new List<MapRegionData>())
                if (region != null && !string.IsNullOrEmpty(region.regionId)) context.regionById[region.regionId] = region;
            foreach (SpiritVein vein in map?.spiritVeins ?? new List<SpiritVein>())
                if (vein?.pathCellIndices != null) foreach (int index in vein.pathCellIndices) context.veinCells.Add(index);
            foreach (RiverSegment segment in map?.rivers ?? new List<RiverSegment>())
            {
                if (segment == null) continue;
                context.riverCells.Add(segment.fromCellIndex);
                context.riverCells.Add(segment.toCellIndex);
            }
            foreach (int riverCell in context.riverCells)
            {
                context.nearRiverCells.Add(riverCell);
                foreach (int neighbor in map.GetNeighborIndices(riverCell)) context.nearRiverCells.Add(neighbor);
            }
            return context;
        }

        private sealed class CandidateRankingContext
        {
            public readonly Dictionary<string, MapRegionData> regionById = new Dictionary<string, MapRegionData>(StringComparer.Ordinal);
            public readonly HashSet<int> veinCells = new HashSet<int>();
            public readonly HashSet<int> riverCells = new HashSet<int>();
            public readonly HashSet<int> nearRiverCells = new HashSet<int>();
        }

        private static List<string> TagsFor(MapSiteType type)
        {
            switch (type)
            {
                case MapSiteType.Village: return new List<string> { "human", "settlement" };
                case MapSiteType.SpiritSpring: return new List<string> { "water", "aura", "resource" };
                case MapSiteType.SpiritMine: return new List<string> { "mineral", "aura", "resource" };
                case MapSiteType.CaveResidence: return new List<string> { "cave", "cultivator" };
                case MapSiteType.BeastLair: return new List<string> { "beast", "danger" };
                default: return new List<string> { "ruin", "ancient" };
            }
        }

        private static void RefreshAvailableActions(MapSiteData site)
        {
            if (site == null) return;
            if (site.availableActionIds == null) site.availableActionIds = new List<string>();
            site.availableActionIds.Clear();
            if (site.revealState != MapContentRevealState.Discovered) return;
            if (site.siteState == MapSiteState.None)
            {
                switch (site.siteType)
                {
                    case MapSiteType.SpiritSpring: site.availableActionIds.Add(InvestigateActionId); break;
                    case MapSiteType.Village: site.availableActionIds.Add(EstablishVillageRelationActionId); break;
                    case MapSiteType.SpiritMine: site.availableActionIds.Add(DevelopSpiritMineActionId); break;
                    case MapSiteType.CaveResidence: site.availableActionIds.Add(BuildCaveResidenceOutpostActionId); break;
                    case MapSiteType.BeastLair: site.availableActionIds.Add(ClearBeastLairActionId); break;
                    case MapSiteType.Ruin: site.availableActionIds.Add(InvestigateRuinActionId); break;
                }
            }
            else if (site.siteType == MapSiteType.SpiritSpring && site.siteState == MapSiteState.Investigated)
                site.availableActionIds.Add(DevelopActionId);
        }

        public static MapActionType ActionForSite(MapSiteData site)
        {
            if (site == null) return MapActionType.None;
            if (site.siteState == MapSiteState.Developed ||
                (site.siteType != MapSiteType.SpiritSpring && site.siteState == MapSiteState.Investigated))
                return MapActionType.None;
            if (site.siteType == MapSiteType.SpiritSpring)
                return site.siteState == MapSiteState.Investigated ? MapActionType.DevelopSpiritSpring : MapActionType.InvestigateSpiritSpring;
            switch (site.siteType)
            {
                case MapSiteType.Village: return MapActionType.EstablishVillageRelation;
                case MapSiteType.SpiritMine: return MapActionType.DevelopSpiritMine;
                case MapSiteType.CaveResidence: return MapActionType.BuildCaveResidenceOutpost;
                case MapSiteType.BeastLair: return MapActionType.ClearBeastLair;
                case MapSiteType.Ruin: return MapActionType.InvestigateRuin;
                default: return MapActionType.None;
            }
        }

        public static string MissionIdFor(MapActionType action)
        {
            switch (action)
            {
                case MapActionType.Explore: return ExploreMissionId;
                case MapActionType.InvestigateSpiritSpring: return InvestigateSpiritSpringMissionId;
                case MapActionType.DevelopSpiritSpring: return DevelopSpiritSpringMissionId;
                case MapActionType.EstablishVillageRelation: return EstablishVillageRelationMissionId;
                case MapActionType.DevelopSpiritMine: return DevelopSpiritMineMissionId;
                case MapActionType.BuildCaveResidenceOutpost: return BuildCaveResidenceOutpostMissionId;
                case MapActionType.ClearBeastLair: return ClearBeastLairMissionId;
                case MapActionType.InvestigateRuin: return InvestigateRuinMissionId;
                default: return null;
            }
        }

        public static string ActionIdFor(MapActionType action)
        {
            switch (action)
            {
                case MapActionType.InvestigateSpiritSpring: return InvestigateActionId;
                case MapActionType.DevelopSpiritSpring: return DevelopActionId;
                case MapActionType.EstablishVillageRelation: return EstablishVillageRelationActionId;
                case MapActionType.DevelopSpiritMine: return DevelopSpiritMineActionId;
                case MapActionType.BuildCaveResidenceOutpost: return BuildCaveResidenceOutpostActionId;
                case MapActionType.ClearBeastLair: return ClearBeastLairActionId;
                case MapActionType.InvestigateRuin: return InvestigateRuinActionId;
                default: return null;
            }
        }

        public static MapSiteType SiteTypeForAction(MapActionType action)
        {
            switch (action)
            {
                case MapActionType.InvestigateSpiritSpring:
                case MapActionType.DevelopSpiritSpring: return MapSiteType.SpiritSpring;
                case MapActionType.EstablishVillageRelation: return MapSiteType.Village;
                case MapActionType.DevelopSpiritMine: return MapSiteType.SpiritMine;
                case MapActionType.BuildCaveResidenceOutpost: return MapSiteType.CaveResidence;
                case MapActionType.ClearBeastLair: return MapSiteType.BeastLair;
                case MapActionType.InvestigateRuin: return MapSiteType.Ruin;
                default: return MapSiteType.SectBase;
            }
        }

        private static bool IsCandidate(MapSiteData site) => site != null && site.siteType != MapSiteType.SectBase;

        private static int HintChance(CellInfluenceRuntimeState state)
        {
            if (state == null || state.knowledge == KnowledgeState.Unknown) return 0;
            switch (state.level)
            {
                case InfluenceLevel.Core: return 25;
                case InfluenceLevel.Influence: return 15;
                case InfluenceLevel.Outer: return 5;
                default: return 0;
            }
        }

        private static int FirstAvailable(IEnumerable<int> source, Func<int, bool> predicate)
        {
            foreach (int value in source) if (predicate(value)) return value;
            return -1;
        }

        private static uint StableUnsigned(int seed, string label) =>
            unchecked((uint)SeedDerivation.Derive(seed, label));

        private static uint DiscoveryRoll(WorldMap map, string siteId, int cellIndex) =>
            StableUnsigned(map.effectiveSeed, "map-content-discover-" + siteId + "-" + cellIndex) % 100u;
    }
}
