using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Cultivation4X.WorldMap
{
    public class WorldCellInteractionRulesTests
    {
        [Test]
        public void Generate_OrdinaryUnknownCellReturnsExplore()
        {
            WorldMap map = BuildMap(2, 2);
            WorldMapProgressState progress = new WorldMapProgressState();

            CellInteractionOption explore = WorldCellInteractionRules.Generate(map, progress, 0).Single();

            Assert.AreEqual(WorldCellInteractionRules.ExploreCellOptionId, explore.id);
            Assert.AreEqual(CellInteractionOptionType.Explore, explore.optionType);
            Assert.AreEqual("探索", explore.displayName);
            Assert.IsTrue(explore.available);
        }

        [Test]
        public void Generate_HintedCellReturnsOnlyExplore()
        {
            WorldMap map = BuildMap(2, 2);
            WorldMapProgressState progress = new WorldMapProgressState
            {
                mapSites = new List<MapSiteData>
                {
                    new MapSiteData
                    {
                        siteId = "hint", siteName = "不应显示", siteType = MapSiteType.Ruin,
                        cellIndex = 1, revealState = MapContentRevealState.Hinted
                    }
                }
            };

            CellInteractionOption explore = WorldCellInteractionRules.Generate(map, progress, 1).Single();

            Assert.AreEqual(WorldCellInteractionRules.ExploreCellOptionId, explore.id);
            Assert.AreEqual(CellInteractionOptionType.Explore, explore.optionType);
            Assert.AreEqual("探索", explore.displayName);
            Assert.IsTrue(explore.available);
        }

        [Test]
        public void Generate_ExploredCellReturnsDisabledCompletedExplore()
        {
            WorldMap map = BuildMap(2, 2);
            WorldMapProgressState progress = new WorldMapProgressState
            {
                exploredCellIndices = new List<int> { 1 }
            };

            CellInteractionOption explore = WorldCellInteractionRules.Generate(map, progress, 1).Single();

            Assert.AreEqual("探索（已完成）", explore.displayName);
            Assert.IsFalse(explore.available);
        }

        [Test]
        public void Generate_InvalidCellReturnsEmpty()
        {
            WorldMap map = BuildMap(2, 2);
            WorldMapProgressState progress = new WorldMapProgressState();

            Assert.IsEmpty(WorldCellInteractionRules.Generate(map, progress, -1));
            Assert.IsEmpty(WorldCellInteractionRules.Generate(map, progress, 4));
        }

        private static WorldMap BuildMap(int width, int height)
        {
            WorldCell[] cells = new WorldCell[width * height];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = new WorldCell { index = i };
            return new WorldMap
            {
                width = width,
                height = height,
                cells = cells,
                locations = new Dictionary<string, WorldLocation>()
            };
        }
    }
}
