using System;
using System.Collections.Generic;
using System.Linq;

namespace Cultivation4X.WorldMap
{
    [Serializable]
    public sealed class InfluenceSourceData
    {
        public string sourceId;
        public InfluenceSourceType sourceType;
        public int cellIndex = -1;
        public string controllerSectId;
        public int baseStrength;
        public int radius;
        public bool isActive;
    }

    [Serializable]
    public sealed class CellInfluenceState
    {
        public int cellIndex = -1;
        public int value;
        public InfluenceLevel level;
        public string controllerSectId;
        public List<string> sourceIds = new List<string>();
    }

    public sealed class CellInfluenceRuntimeState
    {
        public int cellIndex;
        public int value;
        public InfluenceLevel level;
        public string controllerSectId;
        public IReadOnlyList<string> sourceIds;
        public KnowledgeState knowledge;
    }

    public static class WorldMapInfluenceRules
    {
        public const int SectBaseStrength = 100;
        public const int SectBaseRadius = 2;

        public static CellInfluenceRuntimeState GetCellState(
            WorldMap map, WorldMapProgressState progress, int cellIndex)
        {
            if (!WorldMapProgressRules.IsValidCell(map, cellIndex))
                return new CellInfluenceRuntimeState
                {
                    cellIndex = cellIndex,
                    level = InfluenceLevel.None,
                    knowledge = KnowledgeState.Unknown,
                    sourceIds = Array.Empty<string>()
                };

            EnsureCurrent(map, progress);
            CellInfluenceState cached = progress?.cellInfluences?.FirstOrDefault(item =>
                item != null && item.cellIndex == cellIndex);
            bool known = cached != null || progress?.revealedCellIndices?.Contains(cellIndex) == true;
            return new CellInfluenceRuntimeState
            {
                cellIndex = cellIndex,
                value = cached?.value ?? 0,
                level = cached?.level ?? InfluenceLevel.None,
                controllerSectId = cached?.controllerSectId,
                sourceIds = cached?.sourceIds ?? (IReadOnlyList<string>)Array.Empty<string>(),
                knowledge = known ? KnowledgeState.Known : KnowledgeState.Unknown
            };
        }

        public static void EnsureCurrent(WorldMap map, WorldMapProgressState progress)
        {
            if (progress == null) return;
            if (progress.influenceSources == null) progress.influenceSources = new List<InfluenceSourceData>();
            if (progress.cellInfluences == null) progress.cellInfluences = new List<CellInfluenceState>();
            if (progress.isInfluenceDirty ||
                (progress.influenceSources.Any(source => IsUsableSource(map, source)) &&
                 progress.cellInfluences.Count == 0))
                Recalculate(map, progress);
        }

        public static void Recalculate(WorldMap map, WorldMapProgressState progress)
        {
            if (progress == null) return;
            List<CellInfluenceState> replacement = new List<CellInfluenceState>();
            if (map?.cells != null)
            {
                List<InfluenceSourceData> sources = (progress.influenceSources ?? new List<InfluenceSourceData>())
                    .Where(source => IsUsableSource(map, source))
                    .OrderBy(source => source.sourceId, StringComparer.Ordinal)
                    .ToList();
                foreach (WorldCell cell in map.cells)
                {
                    var controllers = sources
                        .Select(source => new { source, contribution = Contribution(map, source, cell.index) })
                        .Where(item => item.contribution > 0)
                        .GroupBy(item => item.source.controllerSectId, StringComparer.Ordinal)
                        .Select(group => new
                        {
                            controller = group.Key,
                            value = Math.Min(100, group.Sum(item => item.contribution)),
                            sourceIds = group.Select(item => item.source.sourceId)
                                .OrderBy(id => id, StringComparer.Ordinal).ToList()
                        })
                        .OrderByDescending(item => item.value)
                        .ThenBy(item => item.controller, StringComparer.Ordinal)
                        .ToList();
                    if (controllers.Count == 0) continue;
                    var winner = controllers[0];
                    replacement.Add(new CellInfluenceState
                    {
                        cellIndex = cell.index,
                        value = winner.value,
                        level = LevelForValue(winner.value),
                        controllerSectId = winner.controller,
                        sourceIds = winner.sourceIds
                    });
                }
            }
            progress.cellInfluences = replacement;
            progress.isInfluenceDirty = false;
        }

        public static InfluenceLevel LevelForValue(int value)
        {
            if (value <= 0) return InfluenceLevel.None;
            if (value < 30) return InfluenceLevel.Outer;
            if (value < 70) return InfluenceLevel.Influence;
            return InfluenceLevel.Core;
        }

        public static int Contribution(WorldMap map, InfluenceSourceData source, int cellIndex)
        {
            if (!WorldMapProgressRules.IsValidCell(map, cellIndex) || !IsUsableSource(map, source)) return 0;
            int distance = HexCoord.Distance(map.cells[source.cellIndex].coord, map.cells[cellIndex].coord);
            if (distance > source.radius) return 0;
            decimal coefficient;
            switch (distance)
            {
                case 0: coefficient = 1m; break;
                case 1: coefficient = 0.6m; break;
                case 2: coefficient = 0.2m; break;
                default: return 0;
            }
            int rounded = decimal.ToInt32(decimal.Round(
                source.baseStrength * coefficient, 0, MidpointRounding.AwayFromZero));
            return Math.Min(100, Math.Max(0, rounded));
        }

        public static bool IsUsableSource(WorldMap map, InfluenceSourceData source) =>
            source != null && source.isActive && source.sourceType == InfluenceSourceType.SectBase &&
            !string.IsNullOrWhiteSpace(source.sourceId) &&
            !string.IsNullOrWhiteSpace(source.controllerSectId) &&
            source.baseStrength > 0 && source.radius >= 0 &&
            WorldMapProgressRules.IsValidCell(map, source.cellIndex);

        public static bool CanReveal(WorldMap map, WorldMapProgressState progress, int cellIndex)
        {
            CellInfluenceRuntimeState state = GetCellState(map, progress, cellIndex);
            return WorldMapProgressRules.IsValidCell(map, cellIndex) &&
                   state.knowledge == KnowledgeState.Unknown && state.level == InfluenceLevel.None;
        }

        public static bool CanInvestigate(WorldMap map, WorldMapProgressState progress, int cellIndex) =>
            HasLevel(map, progress, cellIndex, InfluenceLevel.Outer);
        public static bool CanClear(WorldMap map, WorldMapProgressState progress, int cellIndex) =>
            HasLevel(map, progress, cellIndex, InfluenceLevel.Outer);
        public static bool CanEstablishContact(WorldMap map, WorldMapProgressState progress, int cellIndex) =>
            HasLevel(map, progress, cellIndex, InfluenceLevel.Outer);
        public static bool CanDevelopResource(WorldMap map, WorldMapProgressState progress, int cellIndex) =>
            HasLevel(map, progress, cellIndex, InfluenceLevel.Influence);
        public static bool CanEstablishVillageRelation(WorldMap map, WorldMapProgressState progress, int cellIndex) =>
            HasLevel(map, progress, cellIndex, InfluenceLevel.Influence);
        public static bool CanBuildOutpost(WorldMap map, WorldMapProgressState progress, int cellIndex) =>
            HasLevel(map, progress, cellIndex, InfluenceLevel.Influence);
        public static bool CanBuildCoreFacility(WorldMap map, WorldMapProgressState progress, int cellIndex) =>
            HasLevel(map, progress, cellIndex, InfluenceLevel.Core);

        private static bool HasLevel(WorldMap map, WorldMapProgressState progress, int cellIndex,
            InfluenceLevel minimum) => GetCellState(map, progress, cellIndex).level >= minimum;
    }
}
