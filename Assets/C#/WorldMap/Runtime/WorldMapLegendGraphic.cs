using UnityEngine;
using UnityEngine.UI;

namespace Cultivation4X.WorldMap
{
    public sealed class WorldMapLegendGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            var buffer = new WorldMapGeometryBuffer();
            WorldMapTerrainIconKind[] terrain =
            {
                WorldMapTerrainIconKind.Water, WorldMapTerrainIconKind.Plain, WorldMapTerrainIconKind.Hill,
                WorldMapTerrainIconKind.Mountain, WorldMapTerrainIconKind.Forest, WorldMapTerrainIconKind.Snow
            };
            for (int i = 0; i < terrain.Length; i++)
                WorldMapIconGeometry.AddTerrainIcon(buffer, terrain[i], new Vector2(25f + i * 52f, 60f), 29f,
                    new Color(0.88f, 0.90f, 0.92f));

            WorldMapMarkerKind[] markers =
            {
                WorldMapMarkerKind.FactionSeat, WorldMapMarkerKind.Village,
                WorldMapMarkerKind.Cave, WorldMapMarkerKind.PointOfInterest
            };
            Color[] colors =
            {
                new Color(0.95f, 0.35f, 0.25f), new Color(0.96f, 0.82f, 0.38f),
                new Color(1f, 0.78f, 0.18f), new Color(0.45f, 0.90f, 1f)
            };
            for (int i = 0; i < markers.Length; i++)
                WorldMapIconGeometry.AddMarkerIcon(buffer, markers[i], new Vector2(54f + i * 76f, 20f), 30f, colors[i]);

            for (int i = 0; i < buffer.triangles.Count; i += 3)
            {
                int a = buffer.triangles[i];
                int b = buffer.triangles[i + 1];
                int c = buffer.triangles[i + 2];
                helper.AddVert(buffer.vertices[a], buffer.colors[a], Vector2.zero);
                helper.AddVert(buffer.vertices[b], buffer.colors[b], Vector2.zero);
                helper.AddVert(buffer.vertices[c], buffer.colors[c], Vector2.zero);
                int start = helper.currentVertCount - 3;
                helper.AddTriangle(start, start + 1, start + 2);
            }
        }
    }
}
