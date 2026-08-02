using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cultivation4X.WorldMap
{
    public sealed class WorldMapGeometryBuffer
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<int> triangles = new List<int>();
        public readonly List<Color> colors = new List<Color>();

        public void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            colors.Add(color); colors.Add(color); colors.Add(color);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        }

        public void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }

        public void AddLine(Vector2 from, Vector2 to, float width, Color color)
        {
            Vector2 delta = to - from;
            if (delta.sqrMagnitude < 0.0000001f || width <= 0f) return;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (width * 0.5f);
            AddQuad(from - normal, from + normal, to + normal, to - normal, color);
        }

        public void AddRing(Vector2 center, float radius, float width, Color color, int segments = 16)
        {
            for (int i = 0; i < segments; i++)
            {
                float first = Mathf.PI * 2f * i / segments;
                float second = Mathf.PI * 2f * (i + 1) / segments;
                AddLine(center + new Vector2(Mathf.Cos(first), Mathf.Sin(first)) * radius,
                    center + new Vector2(Mathf.Cos(second), Mathf.Sin(second)) * radius, width, color);
            }
        }

        public Mesh CreateMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetColors(colors);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    public static class WorldMapIconGeometry
    {
        public static void AddTerrainIcon(WorldMapGeometryBuffer buffer, WorldMapTerrainIconKind kind,
            Vector2 center, float size, Color color)
        {
            float line = size * 0.12f;
            switch (kind)
            {
                case WorldMapTerrainIconKind.Water:
                    AddWave(buffer, center + Vector2.up * size * 0.18f, size, line, color);
                    AddWave(buffer, center - Vector2.up * size * 0.18f, size, line, color);
                    break;
                case WorldMapTerrainIconKind.Plain:
                    buffer.AddLine(center + new Vector2(-0.28f, -0.30f) * size,
                        center + new Vector2(-0.10f, 0.28f) * size, line, color);
                    buffer.AddLine(center + new Vector2(0f, -0.30f) * size,
                        center + new Vector2(0.08f, 0.35f) * size, line, color);
                    buffer.AddLine(center + new Vector2(0.25f, -0.30f) * size,
                        center + new Vector2(0.33f, 0.20f) * size, line, color);
                    break;
                case WorldMapTerrainIconKind.Hill:
                    AddArch(buffer, center + Vector2.left * size * 0.18f, size * 0.44f, line, color);
                    AddArch(buffer, center + Vector2.right * size * 0.20f, size * 0.34f, line, color);
                    break;
                case WorldMapTerrainIconKind.Mountain:
                    AddMountain(buffer, center + Vector2.left * size * 0.16f, size * 0.58f, line, color);
                    AddMountain(buffer, center + new Vector2(size * 0.22f, -size * 0.05f), size * 0.42f, line, color);
                    break;
                case WorldMapTerrainIconKind.Forest:
                    buffer.AddTriangle(center + new Vector2(0f, 0.43f) * size,
                        center + new Vector2(-0.38f, -0.14f) * size,
                        center + new Vector2(0.38f, -0.14f) * size, color);
                    buffer.AddQuad(center + new Vector2(-0.06f, -0.14f) * size,
                        center + new Vector2(0.06f, -0.14f) * size,
                        center + new Vector2(0.06f, -0.43f) * size,
                        center + new Vector2(-0.06f, -0.43f) * size, color);
                    break;
                case WorldMapTerrainIconKind.Snow:
                    for (int i = 0; i < 3; i++)
                    {
                        float angle = Mathf.PI * i / 3f;
                        Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size * 0.43f;
                        buffer.AddLine(center - direction, center + direction, line, color);
                    }
                    break;
            }
        }

        public static void AddMarkerIcon(WorldMapGeometryBuffer buffer, WorldMapMarkerKind kind,
            Vector2 center, float size, Color color)
        {
            float line = size * 0.11f;
            switch (kind)
            {
                case WorldMapMarkerKind.FactionSeat:
                    buffer.AddTriangle(center + Vector2.up * size * 0.48f,
                        center + Vector2.left * size * 0.38f,
                        center + Vector2.down * size * 0.48f, color);
                    buffer.AddTriangle(center + Vector2.up * size * 0.48f,
                        center + Vector2.down * size * 0.48f,
                        center + Vector2.right * size * 0.38f, color);
                    buffer.AddLine(center + Vector2.down * size * 0.42f,
                        center + Vector2.down * size * 0.72f, line, color);
                    break;
                case WorldMapMarkerKind.Village:
                    buffer.AddTriangle(center + Vector2.up * size * 0.42f,
                        center + new Vector2(-0.48f, -0.02f) * size,
                        center + new Vector2(0.48f, -0.02f) * size, color);
                    buffer.AddQuad(center + new Vector2(-0.35f, -0.02f) * size,
                        center + new Vector2(0.35f, -0.02f) * size,
                        center + new Vector2(0.35f, -0.45f) * size,
                        center + new Vector2(-0.35f, -0.45f) * size, color);
                    break;
                case WorldMapMarkerKind.Cave:
                    buffer.AddRing(center, size * 0.43f, line, color, 18);
                    buffer.AddLine(center + new Vector2(-0.30f, -0.33f) * size,
                        center + new Vector2(0.30f, -0.33f) * size, line, color);
                    break;
                case WorldMapMarkerKind.CaveResidence:
                    AddArch(buffer, center + Vector2.up * size * 0.04f, size * 0.62f, line, color);
                    buffer.AddLine(center + new Vector2(-0.38f, -0.40f) * size,
                        center + new Vector2(0.38f, -0.40f) * size, line, color);
                    buffer.AddRing(center + Vector2.down * size * 0.08f, size * 0.12f,
                        line * 0.8f, color, 8);
                    break;
                case WorldMapMarkerKind.ContentHint:
                    buffer.AddRing(center, size * 0.34f, line, color, 4);
                    break;
                case WorldMapMarkerKind.SpiritSpring:
                    buffer.AddRing(center, size * 0.40f, line, color, 16);
                    AddWave(buffer, center, size * 0.72f, line, color);
                    break;
                case WorldMapMarkerKind.SpiritMine:
                    buffer.AddTriangle(center + Vector2.up * size * 0.46f,
                        center + new Vector2(-0.42f, -0.38f) * size,
                        center + new Vector2(0.42f, -0.38f) * size, color);
                    break;
                case WorldMapMarkerKind.BeastLair:
                    buffer.AddRing(center, size * 0.34f, line, color, 12);
                    buffer.AddTriangle(center + new Vector2(-0.32f, 0.20f) * size,
                        center + new Vector2(-0.12f, 0.50f) * size,
                        center + new Vector2(-0.02f, 0.16f) * size, color);
                    buffer.AddTriangle(center + new Vector2(0.32f, 0.20f) * size,
                        center + new Vector2(0.12f, 0.50f) * size,
                        center + new Vector2(0.02f, 0.16f) * size, color);
                    break;
                case WorldMapMarkerKind.Ruin:
                    buffer.AddLine(center + new Vector2(-0.36f, -0.42f) * size,
                        center + new Vector2(-0.25f, 0.40f) * size, line, color);
                    buffer.AddLine(center + new Vector2(0.32f, -0.42f) * size,
                        center + new Vector2(0.20f, 0.34f) * size, line, color);
                    buffer.AddLine(center + new Vector2(-0.30f, 0.16f) * size,
                        center + new Vector2(0.25f, 0.10f) * size, line, color);
                    break;
                case WorldMapMarkerKind.EnvironmentHint:
                    // 环境暗示使用轻量符号，与确认地点的实心图标保持明确层级差异。
                    buffer.AddRing(center, size * 0.28f, line * 0.72f, color, 8);
                    buffer.AddLine(center + Vector2.left * size * 0.42f,
                        center + Vector2.right * size * 0.42f, line * 0.65f, color);
                    break;
                case WorldMapMarkerKind.EnvironmentMoisture:
                    AddWave(buffer, center + Vector2.up * size * 0.18f, size * 0.70f,
                        line * 0.62f, color);
                    AddWave(buffer, center + Vector2.down * size * 0.20f, size * 0.54f,
                        line * 0.62f, color);
                    buffer.AddRing(center + Vector2.up * size * 0.42f, size * 0.10f,
                        line * 0.55f, color, 10);
                    break;
                case WorldMapMarkerKind.EnvironmentMineralVein:
                    AddZigZag(buffer, center, size * 0.72f, line * 0.65f, color);
                    buffer.AddLine(center + Vector2.down * size * 0.40f,
                        center + Vector2.up * size * 0.44f, line * 0.55f, color);
                    break;
                case WorldMapMarkerKind.EnvironmentBeastTracks:
                    AddPaw(buffer, center + new Vector2(-0.20f, 0.18f) * size, size * 0.23f,
                        line * 0.62f, color);
                    AddPaw(buffer, center + new Vector2(0.20f, -0.18f) * size, size * 0.23f,
                        line * 0.62f, color);
                    break;
                case WorldMapMarkerKind.EnvironmentRuinedWalls:
                    buffer.AddLine(center + new Vector2(-0.44f, -0.38f) * size,
                        center + new Vector2(-0.34f, 0.30f) * size, line * 0.72f, color);
                    buffer.AddLine(center + new Vector2(-0.34f, 0.30f) * size,
                        center + new Vector2(-0.02f, 0.12f) * size, line * 0.72f, color);
                    buffer.AddLine(center + new Vector2(0.06f, 0.10f) * size,
                        center + new Vector2(0.26f, 0.40f) * size, line * 0.72f, color);
                    buffer.AddLine(center + new Vector2(0.26f, 0.40f) * size,
                        center + new Vector2(0.45f, -0.38f) * size, line * 0.72f, color);
                    break;
                case WorldMapMarkerKind.EnvironmentSettlementSigns:
                    buffer.AddLine(center + new Vector2(-0.45f, -0.36f) * size,
                        center + new Vector2(-0.45f, 0.30f) * size, line * 0.62f, color);
                    buffer.AddLine(center + new Vector2(0.45f, -0.36f) * size,
                        center + new Vector2(0.45f, 0.30f) * size, line * 0.62f, color);
                    buffer.AddLine(center + new Vector2(-0.52f, 0.16f) * size,
                        center + new Vector2(0f, 0.48f) * size, line * 0.62f, color);
                    buffer.AddLine(center + new Vector2(0f, 0.48f) * size,
                        center + new Vector2(0.52f, 0.16f) * size, line * 0.62f, color);
                    buffer.AddLine(center + new Vector2(-0.45f, -0.02f) * size,
                        center + new Vector2(0.45f, -0.02f) * size, line * 0.62f, color);
                    break;
                case WorldMapMarkerKind.EnvironmentCaveSigns:
                    AddArch(buffer, center + Vector2.down * size * 0.04f, size * 0.56f,
                        line * 0.68f, color);
                    buffer.AddRing(center + Vector2.up * size * 0.35f, size * 0.08f,
                        line * 0.55f, color, 8);
                    buffer.AddLine(center + Vector2.left * size * 0.42f,
                        center + Vector2.right * size * 0.42f, line * 0.55f, color);
                    break;
                default:
                    buffer.AddRing(center, size * 0.40f, line, color, 14);
                    buffer.AddTriangle(center + Vector2.down * size * 0.62f,
                        center + new Vector2(-0.15f, -0.25f) * size,
                        center + new Vector2(0.15f, -0.25f) * size, color);
                    break;
            }
        }

        private static void AddWave(WorldMapGeometryBuffer buffer, Vector2 center, float size, float line, Color color)
        {
            Vector2 a = center + new Vector2(-0.45f, 0f) * size;
            Vector2 b = center + new Vector2(-0.15f, 0.14f) * size;
            Vector2 c = center + new Vector2(0.15f, -0.14f) * size;
            Vector2 d = center + new Vector2(0.45f, 0f) * size;
            buffer.AddLine(a, b, line, color);
            buffer.AddLine(b, c, line, color);
            buffer.AddLine(c, d, line, color);
        }

        private static void AddArch(WorldMapGeometryBuffer buffer, Vector2 center, float size, float line, Color color)
        {
            Vector2 a = center + new Vector2(-0.50f, -0.30f) * size;
            Vector2 b = center + new Vector2(0f, 0.40f) * size;
            Vector2 c = center + new Vector2(0.50f, -0.30f) * size;
            buffer.AddLine(a, b, line, color);
            buffer.AddLine(b, c, line, color);
        }

        private static void AddMountain(WorldMapGeometryBuffer buffer, Vector2 center, float size, float line, Color color)
        {
            Vector2 peak = center + Vector2.up * size * 0.52f;
            Vector2 left = center + new Vector2(-0.48f, -0.48f) * size;
            Vector2 right = center + new Vector2(0.48f, -0.48f) * size;
            buffer.AddLine(left, peak, line, color);
            buffer.AddLine(peak, right, line, color);
        }

        private static void AddZigZag(WorldMapGeometryBuffer buffer, Vector2 center, float size,
            float line, Color color)
        {
            Vector2 first = center + new Vector2(-0.48f, -0.35f) * size;
            Vector2 second = center + new Vector2(-0.16f, 0.30f) * size;
            Vector2 third = center + new Vector2(0.12f, -0.05f) * size;
            Vector2 fourth = center + new Vector2(0.45f, 0.40f) * size;
            buffer.AddLine(first, second, line, color);
            buffer.AddLine(second, third, line, color);
            buffer.AddLine(third, fourth, line, color);
        }

        private static void AddPaw(WorldMapGeometryBuffer buffer, Vector2 center, float size,
            float line, Color color)
        {
            buffer.AddRing(center, size * 0.34f, line, color, 8);
            buffer.AddRing(center + new Vector2(-0.38f, 0.38f) * size, size * 0.16f,
                line * 0.8f, color, 8);
            buffer.AddRing(center + new Vector2(0.38f, 0.38f) * size, size * 0.16f,
                line * 0.8f, color, 8);
        }
    }

    public static class WorldMapOverlayGeometry
    {
        public static WorldMapGeometryBuffer BuildRiverGeometry(WorldMap map, Func<HexCoord, Vector2> cellCenter,
            Func<int, bool> cellVisible = null)
        {
            var buffer = new WorldMapGeometryBuffer();
            if (map?.rivers == null || map.cells == null || cellCenter == null) return buffer;
            foreach (RiverSegment segment in map.rivers)
            {
                if (segment.fromCellIndex < 0 || segment.toCellIndex < 0 ||
                    segment.fromCellIndex >= map.cells.Length || segment.toCellIndex >= map.cells.Length)
                    continue;
                if (cellVisible != null &&
                    (!cellVisible(segment.fromCellIndex) || !cellVisible(segment.toCellIndex)))
                    continue;
                float width = Mathf.Clamp(0.055f + Mathf.Sqrt(Mathf.Max(0f, segment.flow)) / 55f, 0.07f, 0.26f);
                buffer.AddLine(cellCenter(map.cells[segment.fromCellIndex].coord),
                    cellCenter(map.cells[segment.toCellIndex].coord), width,
                    new Color(0.25f, 0.70f, 0.95f, 0.95f));
            }
            return buffer;
        }
    }
}
