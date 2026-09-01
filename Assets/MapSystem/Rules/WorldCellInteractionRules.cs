using System.Collections.Generic;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 格子交互选项类型。与 Mission/LocationAction 语义分离，
    /// 表示“当前格子本身可以做什么”，执行仍路由到既有 MapAction/Mission。
    /// </summary>
    public enum CellInteractionOptionType
    {
        None = 0,
        Explore = 1,
        PlanHerbZone = 2,
        CancelFunctionalZone = 3,
        ConfirmCancelFunctionalZone = 4
    }

    /// <summary>普通格子的交互选项（不依赖 WorldLocation）。</summary>
    public sealed class CellInteractionOption
    {
        public string id;
        public CellInteractionOptionType optionType;
        public string displayName;
        public bool available;
    }

    /// <summary>
    /// 格子交互选项生成规则：地点、人物、行动三个数据源分离。
    /// 本类只负责“格子行动”这一来源；地点行动继续由 WorldLocation 提供。
    /// </summary>
    public static class WorldCellInteractionRules
    {
        public const string ExploreCellOptionId = "cell_explore";

        public static List<CellInteractionOption> Generate(WorldMap map,
            WorldMapProgressState progress, int cellIndex, bool confirmZoneCancellation = false)
        {
            List<CellInteractionOption> options = new List<CellInteractionOption>();
            if (map?.cells == null || progress == null ||
                cellIndex < 0 || cellIndex >= map.cells.Length) return options;

            bool explored = progress.exploredCellIndices != null &&
                            progress.exploredCellIndices.Contains(cellIndex);
            options.Add(new CellInteractionOption
            {
                id = ExploreCellOptionId,
                optionType = CellInteractionOptionType.Explore,
                displayName = explored ? "探索（已完成）" : "探索",
                available = !explored && WorldMapContentRules.CanExplore(map, progress, cellIndex, out _)
            });

            SectFunctionalZoneState zone = SectFunctionalZoneRules.GetZone(progress, cellIndex);
            if (zone != null)
            {
                options.Add(new CellInteractionOption
                {
                    id = "cancel_" + zone.zoneId,
                    optionType = confirmZoneCancellation
                        ? CellInteractionOptionType.ConfirmCancelFunctionalZone
                        : CellInteractionOptionType.CancelFunctionalZone,
                    displayName = confirmZoneCancellation
                        ? "确认撤销（全部进度将永久清空）"
                        : "撤销功能区规划",
                    available = true
                });
            }
            else if (SectFunctionalZoneRules.CanPlan(map, progress, cellIndex, out _))
            {
                options.Add(new CellInteractionOption
                {
                    id = "plan_herb_zone",
                    optionType = CellInteractionOptionType.PlanHerbZone,
                    displayName = "规划为灵植区",
                    available = true
                });
            }
            return options;
        }
    }
}
