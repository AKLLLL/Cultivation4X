using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// Combines hand-painted mountain cutouts into a single Region mesh. The mountain range is
    /// a chain of overlapping peaks; a valley is the negative space between two such chains.
    /// </summary>
    internal static class PaintedMountainRangeMeshBuilder
    {
        private struct Placement
        {
            public Vector2 center;
            public float scale;
            public int variant;
        }

        private static readonly Vector2[] VariantPixels =
        {
            new Vector2(167f, 156f), new Vector2(234f, 207f), new Vector2(216f, 230f),
            new Vector2(176f, 170f), new Vector2(177f, 129f), new Vector2(94f, 92f)
        };

        public static Mesh BuildMountain(IReadOnlyList<WorldCell> path, Vector2 anchor,
            int seed, float cameraYaw, int materialCount)
        {
            if (path == null || path.Count < 2 || materialCount <= 0) return null;
            var placements = new List<Placement>();
            for (int i = 0; i < path.Count; i++)
            {
                WorldCell cell = path[i];
                bool pass = cell.internalPositionTag == MapInternalPositionTag.MountainPass;
                if (pass) continue;
                uint hash = Hash(seed, cell.index, i);
                float endpoint = i == 0 || i == path.Count - 1 ? 0.78f : 1f;
                float summit = cell.internalPositionTag == MapInternalPositionTag.Summit ? 1.24f : 1f;
                Vector2 center = TerrainMeshGenerator.HexCenter(cell.coord);
                Vector2 tangent = PathTangent(path, i);
                Vector2 lateral = new Vector2(-tangent.y, tangent.x);
                center += lateral * Signed(hash >> 8) * 0.24f + tangent * Signed(hash >> 16) * 0.12f;
                Add(placements, center, endpoint * summit * Mathf.Lerp(0.90f, 1.10f, Unit(hash)),
                    (int)(hash % (uint)materialCount));

                // A sparse, smaller shoulder chain makes the range read as a group of mountains,
                // while the primary chain preserves a clear macro direction.
                if (i > 0 && i < path.Count - 1 && i % 3 == 1)
                {
                    int side = ((hash >> 24) & 1u) == 0u ? -1 : 1;
                    Add(placements, center + lateral * side * 0.62f - tangent * 0.16f,
                        endpoint * Mathf.Lerp(0.62f, 0.76f, Unit(hash >> 4)),
                        (int)((hash / 7u + 2u) % (uint)materialCount));
                }
            }
            return Build("Painted Mountain Range", placements, anchor, cameraYaw, materialCount);
        }

        public static Mesh BuildValley(IReadOnlyList<WorldCell> path, Vector2 anchor,
            int seed, float cameraYaw, int materialCount)
        {
            if (path == null || path.Count < 2 || materialCount <= 0) return null;
            var placements = new List<Placement>();
            for (int i = 0; i < path.Count; i++)
            {
                WorldCell cell = path[i];
                Vector2 center = TerrainMeshGenerator.HexCenter(cell.coord);
                Vector2 tangent = PathTangent(path, i);
                Vector2 lateral = new Vector2(-tangent.y, tangent.x);
                float endT = Mathf.Min(i, path.Count - 1 - i) / Mathf.Max(1f, (path.Count - 1) * 0.30f);
                float opening = Mathf.Lerp(2.05f, 1.38f, Mathf.Clamp01(endT));
                if (cell.internalPositionTag == MapInternalPositionTag.ValleyEntrance) opening += 0.42f;
                for (int side = -1; side <= 1; side += 2)
                {
                    uint hash = Hash(seed + side * 977, cell.index, i);
                    Vector2 position = center + lateral * opening * side +
                                       tangent * Signed(hash >> 10) * 0.14f;
                    Add(placements, position, Mathf.Lerp(0.82f, 1.04f, Unit(hash)),
                        (int)(hash % (uint)materialCount));
                }
            }
            return Build("Painted Mountain Valley", placements, anchor, cameraYaw, materialCount);
        }

        private static Mesh Build(string name, List<Placement> placements, Vector2 anchor,
            float cameraYaw, int materialCount)
        {
            if (placements.Count == 0) return null;
            Vector3 screenRight3 = Quaternion.Euler(0f, cameraYaw, 0f) * Vector3.right;
            Vector2 screenRight = new Vector2(screenRight3.x, screenRight3.z).normalized;
            Vector3 screenForward3 = Quaternion.Euler(0f, cameraYaw, 0f) * Vector3.forward;
            Vector2 screenForward = new Vector2(screenForward3.x, screenForward3.z).normalized;
            placements.Sort((left, right) =>
            {
                float leftDepth = Vector2.Dot(left.center, screenForward);
                float rightDepth = Vector2.Dot(right.center, screenForward);
                int depth = rightDepth.CompareTo(leftDepth);
                return depth != 0 ? depth : left.variant.CompareTo(right.variant);
            });

            var vertices = new List<Vector3>(placements.Count * 4);
            var uvs = new List<Vector2>(placements.Count * 4);
            var triangles = new List<int>[materialCount];
            for (int i = 0; i < materialCount; i++) triangles[i] = new List<int>();
            foreach (Placement placement in placements)
            {
                int start = vertices.Count;
                Vector2 pixels = VariantPixels[placement.variant % VariantPixels.Length];
                float height = 2.35f * placement.scale;
                float halfWidth = height * pixels.x / pixels.y * 0.5f;
                Vector2 local = placement.center - anchor;
                Vector2 left = local - screenRight * halfWidth;
                Vector2 right = local + screenRight * halfWidth;
                vertices.Add(new Vector3(left.x, 0.025f, left.y));
                vertices.Add(new Vector3(right.x, 0.025f, right.y));
                vertices.Add(new Vector3(right.x, height, right.y));
                vertices.Add(new Vector3(left.x, height, left.y));
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
                List<int> indices = triangles[placement.variant % materialCount];
                indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
                indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
            }

            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32,
                subMeshCount = materialCount };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            for (int i = 0; i < materialCount; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void Add(List<Placement> result, Vector2 center, float scale, int variant) =>
            result.Add(new Placement { center = center, scale = scale, variant = variant });

        private static Vector2 PathTangent(IReadOnlyList<WorldCell> path, int index)
        {
            Vector2 before = TerrainMeshGenerator.HexCenter(path[Mathf.Max(0, index - 1)].coord);
            Vector2 after = TerrainMeshGenerator.HexCenter(path[Mathf.Min(path.Count - 1, index + 1)].coord);
            Vector2 tangent = after - before;
            return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector2.up;
        }

        private static uint Hash(int seed, int cell, int salt)
        {
            unchecked
            {
                uint value = (uint)seed ^ (uint)cell * 0x9e3779b9u ^ (uint)salt * 0x85ebca6bu;
                value ^= value >> 16; value *= 0x7feb352du; value ^= value >> 15;
                return value;
            }
        }

        private static float Unit(uint value) => (value & 0xffffu) / 65535f;
        private static float Signed(uint value) => Unit(value) * 2f - 1f;
    }
}
