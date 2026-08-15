using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// Builds one continuous presentation mesh for a whole Region. The gameplay hexes remain unchanged.
    /// </summary>
    internal static class RegionLandformMeshBuilder
    {
        public static Mesh BuildMountain(IReadOnlyList<WorldCell> path, Vector2 anchor, int seed)
        {
            if (path == null || path.Count < 2) return null;
            var centers = SmoothPath(path);
            var vertices = new List<Vector3>(centers.Count * 5);
            var colors = new List<Color>(centers.Count * 5);
            var triangles = new List<int>((centers.Count - 1) * 24);
            Color side = new Color(0.25f, 0.29f, 0.25f, 1f);
            Color shoulder = new Color(0.38f, 0.42f, 0.36f, 1f);
            Color crest = new Color(0.58f, 0.60f, 0.53f, 1f);
            for (int i = 0; i < centers.Count; i++)
            {
                Vector2 tangent = Tangent(centers, i);
                Vector2 lateral = new Vector2(-tangent.y, tangent.x);
                float wave = Hash01(seed, i) * 0.18f;
                float width = 0.70f + Hash01(seed ^ 0x5a17, i) * 0.18f;
                float height = 0.68f + wave;
                AddCrossSection(vertices, colors, centers[i] - anchor, lateral, width,
                    new[] { 0.02f, height * 0.55f, height, height * 0.55f, 0.02f },
                    new[] { side, shoulder, crest, shoulder, side });
                if (i > 0) ConnectStrip(triangles, (i - 1) * 5, i * 5, 5);
            }
            return Finish("Continuous Mountain Ridge", vertices, colors, triangles);
        }

        public static Mesh BuildValley(IReadOnlyList<WorldCell> path, Vector2 anchor, int seed)
        {
            if (path == null || path.Count < 2) return null;
            var centers = SmoothPath(path);
            var vertices = new List<Vector3>(centers.Count * 6);
            var colors = new List<Color>(centers.Count * 6);
            var triangles = new List<int>((centers.Count - 1) * 30);
            Color wall = new Color(0.34f, 0.31f, 0.24f, 1f);
            Color rim = new Color(0.47f, 0.44f, 0.32f, 1f);
            Color floor = new Color(0.27f, 0.42f, 0.24f, 1f);
            for (int i = 0; i < centers.Count; i++)
            {
                Vector2 tangent = Tangent(centers, i);
                Vector2 lateral = new Vector2(-tangent.y, tangent.x);
                float halfGap = 0.34f + Hash01(seed, i) * 0.08f;
                float outer = halfGap + 0.62f;
                Vector2 local = centers[i] - anchor;
                float[] offsets = { -outer, -halfGap, -0.18f, 0.18f, halfGap, outer };
                float[] heights = { 0.05f, 0.38f, 0.015f, 0.015f, 0.38f, 0.05f };
                Color[] palette = { wall, rim, floor, floor, rim, wall };
                for (int j = 0; j < offsets.Length; j++)
                {
                    Vector2 p = local + lateral * offsets[j];
                    vertices.Add(new Vector3(p.x, heights[j], p.y));
                    colors.Add(palette[j]);
                }
                if (i > 0) ConnectStrip(triangles, (i - 1) * 6, i * 6, 6);
            }
            return Finish("Continuous Valley Corridor", vertices, colors, triangles);
        }

        public static Mesh BuildHills(IReadOnlyList<WorldCell> cells, Vector2 anchor, int seed)
        {
            if (cells == null || cells.Count == 0) return null;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            for (int i = 0; i < cells.Count; i += Mathf.Max(1, cells.Count / 24))
            {
                Vector2 center = TerrainMeshGenerator.HexCenter(cells[i].coord) - anchor;
                center += new Vector2(HashSigned(seed, i) * 0.16f, HashSigned(seed ^ 91, i) * 0.16f);
                AddDome(vertices, colors, triangles, center,
                    0.58f + Hash01(seed ^ 17, i) * 0.18f,
                    0.30f + Hash01(seed ^ 31, i) * 0.12f,
                    new Color(0.43f, 0.48f, 0.31f, 1f), 10);
            }
            return Finish("Continuous Low Hills", vertices, colors, triangles);
        }

        public static Mesh BuildForestCanopy(IReadOnlyList<WorldCell> cells, Vector2 anchor, int seed)
        {
            if (cells == null || cells.Count == 0) return null;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            int step = Mathf.Max(1, cells.Count / 32);
            for (int i = 0; i < cells.Count; i += step)
            {
                Vector2 center = TerrainMeshGenerator.HexCenter(cells[i].coord) - anchor;
                center += new Vector2(HashSigned(seed, i) * 0.32f, HashSigned(seed ^ 211, i) * 0.32f);
                Color canopy = Color.Lerp(new Color(0.12f, 0.27f, 0.10f, 0.96f),
                    new Color(0.24f, 0.40f, 0.16f, 0.96f), Hash01(seed ^ 73, i));
                AddDome(vertices, colors, triangles, center,
                    0.82f + Hash01(seed ^ 19, i) * 0.46f,
                    0.18f + Hash01(seed ^ 29, i) * 0.18f, canopy, 9);
            }
            return Finish("Region Forest Canopy", vertices, colors, triangles);
        }

        private static List<Vector2> SmoothPath(IReadOnlyList<WorldCell> path)
        {
            var result = new List<Vector2>((path.Count - 1) * 3 + 1);
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2 a = TerrainMeshGenerator.HexCenter(path[i].coord);
                Vector2 b = TerrainMeshGenerator.HexCenter(path[i + 1].coord);
                result.Add(a);
                result.Add(Vector2.Lerp(a, b, 1f / 3f));
                result.Add(Vector2.Lerp(a, b, 2f / 3f));
            }
            result.Add(TerrainMeshGenerator.HexCenter(path[path.Count - 1].coord));
            for (int pass = 0; pass < 2; pass++)
                for (int i = 1; i < result.Count - 1; i++)
                    result[i] = (result[i - 1] + result[i] * 2f + result[i + 1]) * 0.25f;
            return result;
        }

        private static Vector2 Tangent(IReadOnlyList<Vector2> path, int i)
        {
            Vector2 value = path[Mathf.Min(path.Count - 1, i + 1)] - path[Mathf.Max(0, i - 1)];
            return value.sqrMagnitude > 0.0001f ? value.normalized : Vector2.up;
        }

        private static void AddCrossSection(List<Vector3> vertices, List<Color> colors,
            Vector2 center, Vector2 lateral, float width, float[] heights, Color[] palette)
        {
            for (int i = 0; i < heights.Length; i++)
            {
                float offset = Mathf.Lerp(-width, width, i / (float)(heights.Length - 1));
                Vector2 point = center + lateral * offset;
                vertices.Add(new Vector3(point.x, heights[i], point.y));
                colors.Add(palette[i]);
            }
        }

        private static void ConnectStrip(List<int> triangles, int previous, int current, int width)
        {
            for (int i = 0; i < width - 1; i++)
            {
                triangles.Add(previous + i); triangles.Add(current + i); triangles.Add(previous + i + 1);
                triangles.Add(previous + i + 1); triangles.Add(current + i); triangles.Add(current + i + 1);
            }
        }

        private static void AddDome(List<Vector3> vertices, List<Color> colors, List<int> triangles,
            Vector2 center, float radius, float height, Color color, int sides)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(center.x, height, center.y));
            colors.Add(Color.Lerp(color, Color.white, 0.12f));
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                vertices.Add(new Vector3(center.x + Mathf.Cos(angle) * radius, 0.025f,
                    center.y + Mathf.Sin(angle) * radius));
                colors.Add(color);
            }
            for (int i = 0; i < sides; i++)
            {
                triangles.Add(start); triangles.Add(start + 1 + i); triangles.Add(start + 1 + (i + 1) % sides);
            }
        }

        private static Mesh Finish(string name, List<Vector3> vertices, List<Color> colors,
            List<int> triangles)
        {
            if (vertices.Count == 0 || triangles.Count == 0) return null;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float Hash01(int seed, int value)
        {
            unchecked
            {
                uint x = (uint)(seed * 73856093 ^ value * 19349663);
                x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15;
                return (x & 0xffffu) / 65535f;
            }
        }

        private static float HashSigned(int seed, int value) => Hash01(seed, value) * 2f - 1f;
    }
}
