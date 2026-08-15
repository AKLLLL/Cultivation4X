using NUnit.Framework;
using System.Reflection;
using Cultivation4X.WorldMap;

public sealed class MapTestBootstrapTests
{
    [Test]
    public void TerrainTest_UsesTerrainOnlyEvaluationWithoutArtOrMarkers()
    {
        Assert.IsTrue(MapTestManager.TerrainOnlyEvaluationEnabled,
            "当前里程碑必须只显示基础地表，不能让模型或标识掩盖高度场问题");
    }

    [TestCase(MapTestBootstrap.SceneName, true)]
    [TestCase(MapTestBootstrap.ArtAuditionSceneName, true)]
    [TestCase("OrdinaryGameplayScene", false)]
    public void IsTestScene_OnlyIsolatesExplicitTestScenes(string sceneName, bool expected)
    {
        MethodInfo predicate = typeof(MapTestBootstrap).GetMethod(
            "IsIsolatedSceneName",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(predicate, Is.Not.Null);
        Assert.That(predicate.Invoke(null, new object[] { sceneName }), Is.EqualTo(expected));
    }

    [Test]
    public void TerrainStatistics_ReportsBiomeDistributionInsteadOfOnlyGreenAppearance()
    {
        BiomeType[] biomes =
        {
            BiomeType.Grassland, BiomeType.TemperateForest, BiomeType.Rainforest,
            BiomeType.Wetland, BiomeType.Desert, BiomeType.Tundra,
            BiomeType.Snowfield, BiomeType.Alpine
        };
        var map = new WorldMap
        {
            width = biomes.Length,
            height = 1,
            cells = new WorldCell[biomes.Length]
        };
        for (int index = 0; index < biomes.Length; index++)
        {
            map.cells[index] = new WorldCell
            {
                index = index,
                coord = new HexCoord(index, 0),
                landform = biomes[index] == BiomeType.Alpine
                    ? LandformType.Mountain
                    : LandformType.Plain,
                biome = biomes[index],
                moisture = 0.5f
            };
        }

        MethodInfo method = typeof(MapTestManager).GetMethod(
            "BuildTerrainStatisticsText",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        string text = (string)method.Invoke(null, new object[] { map });
        StringAssert.Contains("温暖群系：草原", text);
        StringAssert.Contains("温带林", text);
        StringAssert.Contains("雨林", text);
        StringAssert.Contains("寒冷/高地：苔原", text);
        StringAssert.Contains("高山", text);
    }
}
