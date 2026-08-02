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
                WorldMapIconGeometry.AddTerrainIcon(buffer, terrain[i], new Vector2(25f + i * 52f, 136f), 29f,
                    new Color(0.88f, 0.90f, 0.92f));

            WorldMapMarkerKind[] siteMarkers =
            {
                WorldMapMarkerKind.FactionSeat, WorldMapMarkerKind.ContentHint,
                WorldMapMarkerKind.Village, WorldMapMarkerKind.SpiritSpring,
                WorldMapMarkerKind.SpiritMine, WorldMapMarkerKind.CaveResidence,
                WorldMapMarkerKind.BeastLair, WorldMapMarkerKind.Ruin
            };
            Color[] siteColors =
            {
                new Color(0.98f, 0.32f, 0.22f), new Color(0.72f, 0.86f, 1f, 0.86f),
                new Color(0.98f, 0.82f, 0.32f), new Color(0.20f, 0.84f, 1f),
                new Color(0.68f, 0.78f, 0.96f), new Color(0.95f, 0.56f, 0.20f),
                new Color(0.92f, 0.34f, 0.18f), new Color(0.78f, 0.70f, 0.56f)
            };
            for (int i = 0; i < siteMarkers.Length; i++)
                WorldMapIconGeometry.AddMarkerIcon(buffer, siteMarkers[i], new Vector2(20f + i * 40f, 92f), 24f,
                    siteColors[i]);

            WorldMapMarkerKind[] environmentMarkers =
            {
                WorldMapMarkerKind.EnvironmentMoisture, WorldMapMarkerKind.EnvironmentMineralVein,
                WorldMapMarkerKind.EnvironmentBeastTracks, WorldMapMarkerKind.EnvironmentRuinedWalls,
                WorldMapMarkerKind.EnvironmentSettlementSigns, WorldMapMarkerKind.EnvironmentCaveSigns
            };
            Color[] environmentColors =
            {
                new Color(0.24f, 0.88f, 0.96f, 0.72f), new Color(0.86f, 0.68f, 0.28f, 0.72f),
                new Color(0.92f, 0.40f, 0.28f, 0.72f), new Color(0.70f, 0.64f, 0.72f, 0.72f),
                new Color(0.42f, 0.84f, 0.46f, 0.72f), new Color(0.70f, 0.48f, 0.92f, 0.72f)
            };
            for (int i = 0; i < environmentMarkers.Length; i++)
                WorldMapIconGeometry.AddMarkerIcon(buffer, environmentMarkers[i], new Vector2(27f + i * 52f, 42f), 24f,
                    environmentColors[i]);

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
