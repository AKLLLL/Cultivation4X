using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// Builds continuous Region-level terrain shells. The shell is presentation-only: gameplay
    /// hexes, WorldCell.height and navigation remain unchanged.
    /// </summary>
    internal static class RegionTerrainHeightfieldMeshBuilder
    {
        internal sealed class BuildResult
        {
            public Mesh mesh;
            public Func<Vector2, float> heightAt;
            public Func<Vector2, float> slopeAt;
            public Func<Vector2, bool> contains;
        }

        private const int MountainSubdivisions = 3;
        private const float BaseHeight = 0.025f;

        private struct Segment
        {
            public Vector2 a;
            public Vector2 b;
        }

        public static BuildResult BuildMountain(WorldMap map, IReadOnlyList<WorldCell> cells,
            IReadOnlyList<WorldCell> mainPath, Vector2 anchor, int seed)
        {
            if (map?.cells == null || cells == null || cells.Count < 2 ||
                mainPath == null || mainPath.Count < 2) return null;

            var region = new HashSet<int>(cells.Where(cell => cell != null).Select(cell => cell.index));
            List<Segment> boundary = BoundarySegments(map, cells, region);
            List<Segment> ridges = RidgeSegments(mainPath);
            List<Vector2> summits = cells.Where(cell => cell.internalPositionTag == MapInternalPositionTag.Summit)
                .Select(cell => TerrainMeshGenerator.HexCenter(cell.coord)).ToList();
            List<Vector2> passes = cells.Where(cell => cell.internalPositionTag == MapInternalPositionTag.MountainPass)
                .Select(cell => TerrainMeshGenerator.HexCenter(cell.coord)).ToList();
            WorldCell highest = cells.OrderByDescending(cell => cell.height).First();
            AddUniquePoint(summits, TerrainMeshGenerator.HexCenter(highest.coord), 1.8f);
            int peakInterval = Mathf.Max(2, mainPath.Count / 5);
            for (int index = peakInterval / 2; index < mainPath.Count && summits.Count < 7;
                 index += peakInterval)
            {
                AddUniquePoint(summits, TerrainMeshGenerator.HexCenter(mainPath[index].coord), 2.1f);
            }

            float Height(Vector2 point)
            {
                float edgeDistance = DistanceToSegments(point, boundary);
                float edgeFade = Smooth01(edgeDistance / 1.08f);
                float ridgeDistance = DistanceToSegments(point, ridges);
                float ridgeCore = Mathf.Exp(-Mathf.Pow(ridgeDistance / 0.62f, 2f));
                float ridgeShoulder = Mathf.Exp(-Mathf.Pow(ridgeDistance / 1.28f, 2f));
                WorldCell nearest = NearestCell(cells, point);
                float source = nearest == null ? 0.5f : Mathf.InverseLerp(0.62f, 0.94f, nearest.height);
                float summit = 0f;
                foreach (Vector2 peak in summits)
                    summit = Mathf.Max(summit, Mathf.Exp(-(point - peak).sqrMagnitude / 0.92f));
                float pass = 0f;
                foreach (Vector2 saddle in passes)
                    pass = Mathf.Max(pass, Mathf.Exp(-(point - saddle).sqrMagnitude / 0.48f));
                float rockNoise = (ValueNoise(point * 0.47f + SeedOffset(seed)) - 0.5f) * 0.16f +
                                  (ValueNoise(point * 1.31f + SeedOffset(seed ^ 0x51ed)) - 0.5f) * 0.06f;
                float shoulder = ridgeShoulder * 0.16f;
                float crest = ridgeCore * (0.72f + source * 0.34f + summit * 0.68f + rockNoise);
                crest *= Mathf.Lerp(1f, 0.40f, pass);
                return BaseHeight + edgeFade * Mathf.Max(0f, shoulder + crest);
            }

            bool Contains(Vector2 point) => PointInsideRegion(cells, point);
            float Slope(Vector2 point) => SampleSlope(Height, point);
            Mesh mesh = BuildHexSurface(cells, anchor, Height, Slope, MountainSubdivisions,
                "Region Mountain Heightfield");
            return mesh == null ? null : new BuildResult
            {
                mesh = mesh,
                heightAt = Height,
                slopeAt = Slope,
                contains = Contains
            };
        }

        public static BuildResult BuildValley(IReadOnlyList<WorldCell> path, Vector2 anchor, int seed)
        {
            if (path == null || path.Count < 2) return null;
            List<Vector2> centers = SmoothPath(path, 3);
            List<Segment> segments = new List<Segment>();
            for (int i = 1; i < centers.Count; i++)
                segments.Add(new Segment { a = centers[i - 1], b = centers[i] });

            float Height(Vector2 point)
            {
                float distance = DistanceToSegments(point, segments);
                float wall = Mathf.Exp(-Mathf.Pow((distance - 1.24f) / 0.25f, 2f));
                float outerShoulder = Mathf.Exp(-Mathf.Pow((distance - 1.68f) / 0.46f, 2f));
                float noise = (ValueNoise(point * 0.58f + SeedOffset(seed)) - 0.5f) * 0.10f;
                return BaseHeight + Mathf.Max(0f, wall * (1.02f + noise) + outerShoulder * 0.14f);
            }

            float Slope(Vector2 point) => SampleSlope(Height, point);
            bool Contains(Vector2 point) => DistanceToSegments(point, segments) <= 2.25f;
            Mesh mesh = BuildValleyStrip(centers, anchor, Height, Slope);
            return mesh == null ? null : new BuildResult
            {
                mesh = mesh,
                heightAt = Height,
                slopeAt = Slope,
                contains = Contains
            };
        }

        private static Mesh BuildHexSurface(IReadOnlyList<WorldCell> cells, Vector2 anchor,
            Func<Vector2, float> heightAt, Func<Vector2, float> slopeAt, int subdivisions, string name)
        {
            var vertices = new List<Vector3>(cells.Count * subdivisions * subdivisions * 18);
            var normals = new List<Vector3>(vertices.Capacity);
            var colors = new List<Color>(vertices.Capacity);
            var uv = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(vertices.Capacity);
            foreach (WorldCell cell in cells)
            {
                Vector2 center = TerrainMeshGenerator.HexCenter(cell.coord);
                for (int corner = 0; corner < 6; corner++)
                {
                    Vector2 a = HexCorner(center, corner);
                    Vector2 b = HexCorner(center, (corner + 1) % 6);
                    for (int first = 0; first < subdivisions; first++)
                    for (int second = 0; second < subdivisions - first; second++)
                    {
                        Vector2 p0 = Barycentric(center, a, b, first, second, subdivisions);
                        Vector2 p1 = Barycentric(center, a, b, first + 1, second, subdivisions);
                        Vector2 p2 = Barycentric(center, a, b, first, second + 1, subdivisions);
                        AddTriangle(vertices, normals, colors, uv, triangles, anchor,
                            p0, p1, p2, heightAt, slopeAt);
                        if (first + second >= subdivisions - 1) continue;
                        Vector2 p3 = Barycentric(center, a, b, first + 1, second + 1, subdivisions);
                        AddTriangle(vertices, normals, colors, uv, triangles, anchor,
                            p1, p3, p2, heightAt, slopeAt);
                    }
                }
            }
            return Finish(name, vertices, normals, colors, uv, triangles);
        }

        private static Mesh BuildValleyStrip(IReadOnlyList<Vector2> centers, Vector2 anchor,
            Func<Vector2, float> heightAt, Func<Vector2, float> slopeAt)
        {
            const int columns = 15;
            const float halfWidth = 2.25f;
            var vertices = new List<Vector3>(centers.Count * columns);
            var normals = new List<Vector3>(vertices.Capacity);
            var colors = new List<Color>(vertices.Capacity);
            var uv = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>((centers.Count - 1) * (columns - 1) * 6);
            for (int row = 0; row < centers.Count; row++)
            {
                Vector2 tangent = PathTangent(centers, row);
                Vector2 lateral = new Vector2(-tangent.y, tangent.x);
                for (int column = 0; column < columns; column++)
                {
                    float offset = Mathf.Lerp(-halfWidth, halfWidth, column / (float)(columns - 1));
                    Vector2 point = centers[row] + lateral * offset;
                    AddVertex(vertices, normals, colors, uv, anchor, point, heightAt, slopeAt);
                }
                if (row == 0) continue;
                int previous = (row - 1) * columns;
                int current = row * columns;
                for (int column = 0; column < columns - 1; column++)
                {
                    AddWoundTriangle(triangles, vertices, previous + column, current + column,
                        previous + column + 1);
                    AddWoundTriangle(triangles, vertices, previous + column + 1, current + column,
                        current + column + 1);
                }
            }
            return Finish("Region Valley Heightfield", vertices, normals, colors, uv, triangles);
        }

        private static void AddTriangle(List<Vector3> vertices, List<Vector3> normals,
            List<Color> colors, List<Vector2> uv, List<int> triangles, Vector2 anchor,
            Vector2 a, Vector2 b, Vector2 c, Func<Vector2, float> heightAt,
            Func<Vector2, float> slopeAt)
        {
            int start = vertices.Count;
            AddVertex(vertices, normals, colors, uv, anchor, a, heightAt, slopeAt);
            AddVertex(vertices, normals, colors, uv, anchor, b, heightAt, slopeAt);
            AddVertex(vertices, normals, colors, uv, anchor, c, heightAt, slopeAt);
            AddWoundTriangle(triangles, vertices, start, start + 1, start + 2);
        }

        private static void AddVertex(List<Vector3> vertices, List<Vector3> normals,
            List<Color> colors, List<Vector2> uv, Vector2 anchor, Vector2 point,
            Func<Vector2, float> heightAt, Func<Vector2, float> slopeAt)
        {
            const float delta = 0.08f;
            float height = heightAt(point);
            float dx = (heightAt(point + Vector2.right * delta) -
                        heightAt(point + Vector2.left * delta)) / (delta * 2f);
            float dz = (heightAt(point + Vector2.up * delta) -
                        heightAt(point + Vector2.down * delta)) / (delta * 2f);
            float slope = Mathf.Sqrt(dx * dx + dz * dz);
            Color grass = new Color(0.34f, 0.43f, 0.23f, 1f);
            Color soil = new Color(0.38f, 0.31f, 0.22f, 1f);
            Color rock = new Color(0.40f, 0.42f, 0.40f, 1f);
            Color tint = Color.Lerp(grass, soil, Mathf.SmoothStep(0.12f, 0.42f, slope));
            tint = Color.Lerp(tint, rock, Mathf.SmoothStep(0.34f, 0.72f, slope));
            tint = Color.Lerp(tint, rock * 1.12f, Mathf.InverseLerp(0.85f, 1.55f, height));
            vertices.Add(new Vector3(point.x - anchor.x, height, point.y - anchor.y));
            normals.Add(new Vector3(-dx, 1f, -dz).normalized);
            colors.Add(tint);
            uv.Add(point);
        }

        private static void AddWoundTriangle(List<int> triangles, List<Vector3> vertices,
            int a, int b, int c)
        {
            Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            triangles.Add(a);
            if (normal.y >= 0f) { triangles.Add(b); triangles.Add(c); }
            else { triangles.Add(c); triangles.Add(b); }
        }

        private static Mesh Finish(string name, List<Vector3> vertices, List<Vector3> normals,
            List<Color> colors, List<Vector2> uv, List<int> triangles)
        {
            if (vertices.Count == 0 || triangles.Count == 0) return null;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static List<Segment> BoundarySegments(WorldMap map, IReadOnlyList<WorldCell> cells,
            HashSet<int> region)
        {
            var result = new List<Segment>();
            foreach (WorldCell cell in cells)
            {
                Vector2 center = TerrainMeshGenerator.HexCenter(cell.coord);
                for (int direction = 0; direction < 6; direction++)
                {
                    int neighbor = map.GetIndex(map.GetNeighbor(cell.coord, direction));
                    if (neighbor >= 0 && region.Contains(neighbor)) continue;
                    result.Add(new Segment
                    {
                        a = HexCorner(center, direction),
                        b = HexCorner(center, (direction + 1) % 6)
                    });
                }
            }
            return result;
        }

        private static List<Segment> RidgeSegments(IReadOnlyList<WorldCell> mainPath)
        {
            var result = new List<Segment>();
            for (int index = 1; index < mainPath.Count; index++)
            {
                WorldCell previous = mainPath[index - 1];
                WorldCell current = mainPath[index];
                if (previous == null || current == null) continue;
                result.Add(new Segment
                {
                    a = TerrainMeshGenerator.HexCenter(previous.coord),
                    b = TerrainMeshGenerator.HexCenter(current.coord)
                });
            }
            return result;
        }

        private static void AddUniquePoint(List<Vector2> points, Vector2 point, float minimumDistance)
        {
            float minimumDistanceSquared = minimumDistance * minimumDistance;
            if (points.Any(existing => (existing - point).sqrMagnitude < minimumDistanceSquared)) return;
            points.Add(point);
        }

        private static List<Vector2> SmoothPath(IReadOnlyList<WorldCell> path, int samplesPerEdge)
        {
            var result = new List<Vector2>((path.Count - 1) * samplesPerEdge + 1);
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2 a = TerrainMeshGenerator.HexCenter(path[i].coord);
                Vector2 b = TerrainMeshGenerator.HexCenter(path[i + 1].coord);
                for (int sample = 0; sample < samplesPerEdge; sample++)
                    result.Add(Vector2.Lerp(a, b, sample / (float)samplesPerEdge));
            }
            result.Add(TerrainMeshGenerator.HexCenter(path[path.Count - 1].coord));
            for (int pass = 0; pass < 2; pass++)
                for (int i = 1; i < result.Count - 1; i++)
                    result[i] = (result[i - 1] + result[i] * 2f + result[i + 1]) * 0.25f;
            return result;
        }

        private static Vector2 PathTangent(IReadOnlyList<Vector2> path, int index)
        {
            Vector2 tangent = path[Mathf.Min(path.Count - 1, index + 1)] -
                              path[Mathf.Max(0, index - 1)];
            return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector2.up;
        }

        private static float SampleSlope(Func<Vector2, float> heightAt, Vector2 point)
        {
            const float delta = 0.14f;
            float dx = (heightAt(point + Vector2.right * delta) -
                        heightAt(point + Vector2.left * delta)) / (delta * 2f);
            float dz = (heightAt(point + Vector2.up * delta) -
                        heightAt(point + Vector2.down * delta)) / (delta * 2f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float DistanceToSegments(Vector2 point, IReadOnlyList<Segment> segments)
        {
            if (segments == null || segments.Count == 0) return 0f;
            float best = float.MaxValue;
            foreach (Segment segment in segments)
            {
                Vector2 ab = segment.b - segment.a;
                float t = ab.sqrMagnitude <= 0.0001f ? 0f :
                    Mathf.Clamp01(Vector2.Dot(point - segment.a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(point, segment.a + ab * t));
            }
            return best;
        }

        private static bool PointInsideRegion(IReadOnlyList<WorldCell> cells, Vector2 point)
        {
            WorldCell nearest = NearestCell(cells, point);
            if (nearest == null) return false;
            Vector2 local = point - TerrainMeshGenerator.HexCenter(nearest.coord);
            return Mathf.Abs(local.x) <= Mathf.Sqrt(3f) * 0.5f + 0.001f &&
                   Mathf.Abs(local.y) <= 1f + 0.001f &&
                   Mathf.Sqrt(3f) * Mathf.Abs(local.y) + Mathf.Abs(local.x) <= Mathf.Sqrt(3f) + 0.001f;
        }

        private static WorldCell NearestCell(IReadOnlyList<WorldCell> cells, Vector2 point)
        {
            WorldCell nearest = null;
            float best = float.MaxValue;
            foreach (WorldCell cell in cells)
            {
                float distance = (TerrainMeshGenerator.HexCenter(cell.coord) - point).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = cell;
            }
            return nearest;
        }

        private static Vector2 HexCorner(Vector2 center, int corner) =>
            HexGeometry.GetCorners(center)[corner];

        private static Vector2 Barycentric(Vector2 center, Vector2 a, Vector2 b,
            int first, int second, int subdivisions)
        {
            float wa = first / (float)subdivisions;
            float wb = second / (float)subdivisions;
            return center * (1f - wa - wb) + a * wa + b * wb;
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static Vector2 SeedOffset(int seed) => new Vector2(
            (seed & 255) * 0.071f, ((seed >> 8) & 255) * 0.093f);

        private static float ValueNoise(Vector2 position)
        {
            Vector2 cell = new Vector2(Mathf.Floor(position.x), Mathf.Floor(position.y));
            Vector2 local = position - cell;
            local = new Vector2(Smooth01(local.x), Smooth01(local.y));
            float a = Hash(cell);
            float b = Hash(cell + Vector2.right);
            float c = Hash(cell + Vector2.up);
            float d = Hash(cell + Vector2.one);
            return Mathf.Lerp(Mathf.Lerp(a, b, local.x), Mathf.Lerp(c, d, local.x), local.y);
        }

        private static float Hash(Vector2 value)
        {
            float sine = Mathf.Sin(Vector2.Dot(value, new Vector2(127.1f, 311.7f))) * 43758.5453f;
            return sine - Mathf.Floor(sine);
        }
    }
}
