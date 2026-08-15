using Cultivation4X.WorldMap;
using NUnit.Framework;
using UnityEngine;

public sealed class HexGridOverlayRendererTests
{
    [Test]
    public void Render_CreatesEachSharedHexEdgeOnceAndFollowsZoomTier()
    {
        WorldMap map = BuildTwoCellMap();
        GameObject root = new GameObject("HexGridOverlayRendererTest");
        HexGridOverlayRenderer renderer = root.AddComponent<HexGridOverlayRenderer>();
        try
        {
            renderer.Render(map);
            Assert.AreEqual(11, renderer.EdgeCount, "两个相邻 Hex 应共享一条边，而不是重复绘制");
            MeshFilter filter = root.GetComponentInChildren<MeshFilter>();
            Assert.NotNull(filter);
            Assert.AreEqual(renderer.EdgeCount * 8, filter.sharedMesh.vertexCount,
                "每条 Hex 边应分段贴合连续坡面");

            renderer.ApplyTier(WorldMap3DZoomTier.Near);
            Assert.IsTrue(filter.gameObject.activeSelf);
            float nearAlpha = renderer.ActiveColor.a;
            float nearWidth = renderer.ActiveWidthScale;
            renderer.ApplyTier(WorldMap3DZoomTier.Mid);
            Assert.IsTrue(filter.gameObject.activeSelf);
            float midAlpha = renderer.ActiveColor.a;
            float midWidth = renderer.ActiveWidthScale;
            renderer.SetGridVisible(false);
            Assert.IsFalse(filter.gameObject.activeSelf);
            renderer.SetGridVisible(true);
            Assert.IsTrue(filter.gameObject.activeSelf);
            renderer.ApplyTier(WorldMap3DZoomTier.Far);
            Assert.IsFalse(filter.gameObject.activeSelf,
                "远景只显示地形色块，不显示六角格网格线");
            float farAlpha = renderer.ActiveColor.a;
            float farWidth = renderer.ActiveWidthScale;
            Assert.Less(nearAlpha, midAlpha, "近景操作边界应比中景更淡");
            Assert.Less(midAlpha, farAlpha, "远景应恢复清晰的战略六边形格网");
            Assert.Less(nearWidth, midWidth, "中景网格应比近景稍宽");
            Assert.Less(midWidth, farWidth, "远景网格应加宽，避免缩小后不足一个像素");
            Assert.AreEqual(0.35f, renderer.ActiveFogInfluence, 0.0001f,
                "远景网格应降低雾效影响以保持战略可读性");
        }
        finally
        {
            renderer.Clear();
            Object.DestroyImmediate(root);
        }
    }

    private static WorldMap BuildTwoCellMap()
    {
        return new WorldMap
        {
            width = 2,
            height = 1,
            cells = new[]
            {
                new WorldCell { index = 0, coord = new HexCoord(0, 0), landform = LandformType.Plain },
                new WorldCell { index = 1, coord = new HexCoord(1, 0), landform = LandformType.Plain }
            }
        };
    }
}
