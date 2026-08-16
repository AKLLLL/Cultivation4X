using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 区域覆盖层：每个区域生成一个整体多边形网格，半透明，完全独立于
    /// TerrainRenderer，不修改地形。表面按“距区域边界距离”向上拱起成气泡状，
    /// 边界处颜色加深；基准高度统一、不随地形起伏。孔洞挖空、边界线去重。
    /// 远景显示、近景隐藏。
    /// </summary>
    public sealed class RegionOverlayRenderer : MonoBehaviour
    {
        private const float Epsilon = 1e-5f;
        [SerializeField] private float overlayAlpha = 0.85f;
        [SerializeField] private float borderWidth = 0.25f;
        [SerializeField] private float domeHeight = 3f;
        [SerializeField] private int domeSubdivisionLevels = 2;
        [SerializeField] private float baseHeightOffset = TerrainPresentationModels.RegionOverlayBaseOffset;
        private readonly List<GameObject> overlayObjects = new List<GameObject>();
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        private readonly List<Material> ownedMaterials = new List<Material>();
        private readonly List<Segment> borderSegments = new List<Segment>();
        private GameObject borderObject;
        private Mesh borderMesh;
        private float overlayHeight;
        private int minimumRegionCells = 1;
        private bool hasMap;
        private bool politicalMapEnabled = true;

        public int OverlayObjectCount => overlayObjects.Count;
        public float OverlayHeight => overlayHeight;
        public float DomeHeight => domeHeight;
        public int MinimumRegionCells => minimumRegionCells;

        /// <summary>政治地图模式开关：关闭时隐藏全部覆盖层。</summary>
        public void SetPoliticalMapEnabled(bool enabled)
        {
            politicalMapEnabled = enabled;
            if (!enabled) SetOverlayVisible(false);
        }

        public void Render(WorldMap map)
        {
            Clear();
            if (map?.cells == null || map.cells.Length == 0 || map.regions == null) return;
            overlayHeight = 0f;
            minimumRegionCells = TerrainPresentationModels.RegionOverlayMinimumCells(map);
            hasMap = true;

            Shader shader = Shader.Find("Unlit/VertexColorOverlay") ??
                            Shader.Find("Unlit/VertexColorTransparent") ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("RegionOverlayRenderer: 未找到半透明顶点色 Shader");
                return;
            }
            Material overlayMaterial = new Material(shader) { renderQueue = 4000 };
            overlayMaterial.name = "RegionOverlay";
            ownedMaterials.Add(overlayMaterial);

            List<MapRegionData> selectedRegions = WorldMap3DPresentationPolicy.SelectRegionLabels(map,
                map.regions, minimumRegionCells, WorldMapRegionPresentationPolicy.FarRegionLabelLimit, 0f);
            foreach (MapRegionData region in selectedRegions)
            {
                if (region == null || region.cellIndices == null || region.cellIndices.Count == 0) continue;
                float baseHeight = RegionMaximumPresentationHeight(map, region) + baseHeightOffset;
                overlayHeight = Mathf.Max(overlayHeight, baseHeight);
                Mesh mesh = BuildRegionOverlayMesh(map, region, baseHeight);
                if (mesh == null) continue;
                ownedMeshes.Add(mesh);
                GameObject overlayObject =
                    new GameObject("RegionOverlay_" + region.regionId, typeof(MeshFilter), typeof(MeshRenderer));
                overlayObject.transform.SetParent(transform, false);
                overlayObject.GetComponent<MeshFilter>().sharedMesh = mesh;
                overlayObject.GetComponent<MeshRenderer>().sharedMaterial = overlayMaterial;
                overlayObjects.Add(overlayObject);
            }
            borderMesh = BuildRegionBordersMesh();
            if (borderMesh != null)
            {
                ownedMeshes.Add(borderMesh);
                borderObject = new GameObject("RegionBorders", typeof(MeshFilter), typeof(MeshRenderer));
                borderObject.transform.SetParent(transform, false);
                borderObject.GetComponent<MeshFilter>().sharedMesh = borderMesh;
                borderObject.GetComponent<MeshRenderer>().sharedMaterial = overlayMaterial;
            }
            SetOverlayVisible(false);
        }

        private static float RegionMaximumPresentationHeight(WorldMap map, MapRegionData region)
        {
            float maximum = 0f;
            if (map?.cells == null || region?.cellIndices == null) return maximum;
            foreach (int index in region.cellIndices)
            {
                if (index < 0 || index >= map.cells.Length || map.cells[index] == null) continue;
                maximum = Mathf.Max(maximum,
                    TerrainRenderer.PresentationSurfaceHeight(map, map.cells[index]));
            }
            return maximum;
        }

        /// <summary>显示/隐藏覆盖层（远景显示、近景隐藏）。</summary>
        public void SetOverlayVisible(bool visible)
        {
            foreach (GameObject overlayObject in overlayObjects)
            {
                if (overlayObject != null) overlayObject.SetActive(visible);
            }
            if (borderObject != null) borderObject.SetActive(visible);
        }

        public void Clear()
        {
            foreach (GameObject overlayObject in overlayObjects)
            {
                if (overlayObject != null) DestroyOwned(overlayObject);
            }
            overlayObjects.Clear();
            if (borderObject != null) DestroyOwned(borderObject);
            borderObject = null;
            foreach (Mesh mesh in ownedMeshes) DestroyOwned(mesh);
            ownedMeshes.Clear();
            foreach (Material material in ownedMaterials) DestroyOwned(material);
            ownedMaterials.Clear();
            borderSegments.Clear();
            borderObject = null;
            borderMesh = null;
            hasMap = false;
        }

        private void Update()
        {
            if (!hasMap || !politicalMapEnabled) return;
            float hexes = TerrainPresentationModels.VisibleHexesAcross(Camera.main);
            SetOverlayVisible(WorldMap3DPresentationPolicy.ShowRegionOverlays(
                WorldMap3DPresentationPolicy.GetZoomTier(hexes)));
        }

        private Mesh BuildRegionOverlayMesh(WorldMap map, MapRegionData region, float baseHeight)
        {
            Color regionColor = TerrainPresentationModels.ColorForRegion(region.regionType);
            regionColor.a = overlayAlpha;
            Color32 overlayColor = regionColor;
            float radius = HexGeometry.GetRadius();
            HashSet<int> regionCells = new HashSet<int>();
            foreach (int index in region.cellIndices)
            {
                if (index >= 0 && index < map.cells.Length && map.cells[index] != null)
                    regionCells.Add(index);
            }
            if (regionCells.Count == 0) return null;

            // 1) 收集区域轮廓边：与不同区域/地图边界相邻的六边形边。
            List<Segment> segments = new List<Segment>();
            foreach (int index in regionCells)
            {
                WorldCell cell = map.cells[index];
                Vector2 center = HexGeometry.GetCenter(cell);
                Vector2[] corners = HexGeometry.GetCorners(center, radius);
                for (int direction = 0; direction < 6; direction++)
                {
                    HexCoord neighborCoord = map.GetNeighbor(cell.coord, direction);
                    int neighborIndex = map.GetIndex(neighborCoord);
                    if (neighborIndex >= 0 && regionCells.Contains(neighborIndex)) continue;

                    Vector2 mid = (center + HexGeometry.GetCenter(neighborCoord)) * 0.5f;
                    int bestA = -1;
                    int bestB = -1;
                    float bestADistance = float.MaxValue;
                    float bestBDistance = float.MaxValue;
                    for (int corner = 0; corner < 6; corner++)
                    {
                        float distance = Vector2.Distance(corners[corner], mid);
                        if (distance < bestADistance)
                        {
                            bestBDistance = bestADistance;
                            bestB = bestA;
                            bestADistance = distance;
                            bestA = corner;
                        }
                        else if (distance < bestBDistance)
                        {
                            bestBDistance = distance;
                            bestB = corner;
                        }
                    }
                    segments.Add(new Segment
                    {
                        a = Round(corners[bestA]),
                        b = Round(corners[bestB]),
                        height = baseHeight + 0.05f
                    });
                }
            }
            if (segments.Count == 0) return null;
            borderSegments.AddRange(segments);

            // 2) 把轮廓边串成闭合环。
            List<List<Vector2>> loops = ChainLoops(segments);
            if (loops.Count == 0) return null;

            // 3) 孔洞 = 完全位于另一个闭合环内部的环（与绕向无关）。
            List<List<Vector2>> holeLoops = new List<List<Vector2>>();
            foreach (List<Vector2> loop in loops)
            {
                if (loop.Count < 3) continue;
                bool isHole = false;
                foreach (List<Vector2> other in loops)
                {
                    if (other == loop || other.Count < 3) continue;
                    if (PointInPolygon(loop[0], other))
                    {
                        isHole = true;
                        break;
                    }
                }
                if (isHole) holeLoops.Add(loop);
            }
            List<Vector3> vertices = new List<Vector3>();
            List<Color32> colors = new List<Color32>();
            List<int> triangles = new List<int>();
            // 全部轮廓边（外环与孔洞）用于“距边界距离”的拱起与颜色渐变。
            List<Segment> boundaryEdges = new List<Segment>();
            foreach (List<Vector2> loop in loops)
            {
                for (int index = 0; index < loop.Count; index++)
                {
                    boundaryEdges.Add(new Segment
                    {
                        a = loop[index],
                        b = loop[(index + 1) % loop.Count]
                    });
                }
            }
            float falloffRadius = FalloffRadius(loops);
            Color32 boundaryColor = Darken(overlayColor);
            Color32 interiorColor = Brighten(overlayColor);
            foreach (List<Vector2> loop in loops)
            {
                if (holeLoops.Contains(loop)) continue;
                List<Vector2> polygon = RemoveCollinear(loop);
                if (polygon.Count < 3) continue;
                int[] earTriangles = EarClip(polygon);
                if (earTriangles.Length == 0) continue;
                int baseIndex = vertices.Count;
                foreach (Vector2 point in polygon)
                {
                    vertices.Add(new Vector3(point.x, baseHeight, point.y));
                    colors.Add(boundaryColor);
                }
                // 4) 剔除落在孔洞内的三角形，挖空被包围的区域。
                for (int index = 0; index + 2 < earTriangles.Length; index += 3)
                {
                    Vector2 a = polygon[earTriangles[index]];
                    Vector2 b = polygon[earTriangles[index + 1]];
                    Vector2 c = polygon[earTriangles[index + 2]];
                    Vector2 centroid = (a + b + c) / 3f;
                    bool insideHole = false;
                    foreach (List<Vector2> hole in holeLoops)
                    {
                        if (PointInPolygon(centroid, hole))
                        {
                            insideHole = true;
                            break;
                        }
                    }
                    if (insideHole) continue;
                    // 5) 递归 1→4 平滑细分：所有新增顶点按“距边界距离”平滑拱起并提亮，
                    //    形成边界低且深、内部高且亮的平滑气泡。
                    SubdivideDome(
                        baseIndex + earTriangles[index],
                        baseIndex + earTriangles[index + 1],
                        baseIndex + earTriangles[index + 2],
                        polygon[earTriangles[index]],
                        polygon[earTriangles[index + 1]],
                        polygon[earTriangles[index + 2]],
                        domeSubdivisionLevels, vertices, colors, triangles,
                        boundaryEdges, falloffRadius, boundaryColor, interiorColor, baseHeight);
                }
            }
            if (vertices.Count < 3 || triangles.Count == 0) return null;

            Mesh mesh = new Mesh
            {
                name = "RegionOverlay_" + region.regionId,
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 全区域共享边界网格：把每个区域收集的轮廓边去重（相邻区域共享边只画一次），
        /// 统一绘制在覆盖层之上。
        /// </summary>
        private Mesh BuildRegionBordersMesh()
        {
            Dictionary<string, Segment> uniqueSegments = new Dictionary<string, Segment>();
            foreach (Segment segment in borderSegments)
            {
                string key = SegmentKey(segment);
                if (uniqueSegments.TryGetValue(key, out Segment existing))
                {
                    if (segment.height > existing.height) uniqueSegments[key] = segment;
                }
                else
                {
                    uniqueSegments[key] = segment;
                }
            }
            List<Vector3> vertices = new List<Vector3>(uniqueSegments.Count * 4);
            List<Color32> colors = new List<Color32>(uniqueSegments.Count * 4);
            List<int> triangles = new List<int>(uniqueSegments.Count * 6);
            Color32 borderColor = new Color32(40, 32, 28, 235);
            foreach (Segment segment in uniqueSegments.Values)
            {
                AddLineQuad(vertices, colors, triangles,
                    segment.a, segment.b, borderWidth, borderColor, segment.height);
            }
            if (vertices.Count == 0) return null;
            Mesh mesh = new Mesh
            {
                name = "RegionBorders",
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static string SegmentKey(Segment segment)
        {
            Vector2 a = segment.a;
            Vector2 b = segment.b;
            bool swap = a.x > b.x || (Mathf.Approximately(a.x, b.x) && a.y > b.y);
            if (swap)
            {
                Vector2 temporary = a;
                a = b;
                b = temporary;
            }
            return $"{a.x:F3},{a.y:F3}|{b.x:F3},{b.y:F3}";
        }

        private static bool PointInPolygon(Vector2 point, List<Vector2> polygon)
        {
            bool inside = false;
            for (int index = 0, previous = polygon.Count - 1; index < polygon.Count; previous = index++)
            {
                Vector2 current = polygon[index];
                Vector2 last = polygon[previous];
                if ((current.y > point.y) != (last.y > point.y) &&
                    point.x <= (last.x - current.x) * (point.y - current.y) / (last.y - current.y) + current.x)
                    inside = !inside;
            }
            return inside;
        }

        private static List<List<Vector2>> ChainLoops(List<Segment> segments)
        {
            Dictionary<Vector2, List<int>> adjacency = new Dictionary<Vector2, List<int>>();
            for (int index = 0; index < segments.Count; index++)
            {
                AddAdjacency(adjacency, segments[index].a, index);
                AddAdjacency(adjacency, segments[index].b, index);
            }
            bool[] used = new bool[segments.Count];
            List<List<Vector2>> loops = new List<List<Vector2>>();
            for (int start = 0; start < segments.Count; start++)
            {
                if (used[start]) continue;
                List<Vector2> loop = new List<Vector2>();
                Vector2 current = segments[start].a;
                Vector2 loopStart = current;
                int segmentId = start;
                int guard = segments.Count * 2 + 16;
                while (guard-- > 0)
                {
                    used[segmentId] = true;
                    Segment segment = segments[segmentId];
                    Vector2 next = segment.a == current ? segment.b : segment.a;
                    loop.Add(next);
                    current = next;
                    int nextId = -1;
                    if (adjacency.TryGetValue(current, out List<int> candidates))
                    {
                        foreach (int candidate in candidates)
                        {
                            if (!used[candidate] && candidate != segmentId)
                            {
                                nextId = candidate;
                                break;
                            }
                        }
                    }
                    if (nextId < 0 || current == loopStart) break;
                    segmentId = nextId;
                }
                if (loop.Count >= 3) loops.Add(loop);
            }
            return loops;
        }

        private static void AddAdjacency(Dictionary<Vector2, List<int>> adjacency, Vector2 key, int segmentId)
        {
            if (!adjacency.TryGetValue(key, out List<int> list))
            {
                list = new List<int>();
                adjacency[key] = list;
            }
            list.Add(segmentId);
        }

        private static List<Vector2> RemoveCollinear(List<Vector2> loop)
        {
            List<Vector2> result = new List<Vector2>(loop.Count);
            foreach (Vector2 point in loop)
            {
                while (result.Count >= 2 &&
                       Mathf.Abs(Cross(result[result.Count - 2], result[result.Count - 1], point)) < Epsilon)
                    result.RemoveAt(result.Count - 1);
                result.Add(point);
            }
            while (result.Count >= 3 &&
                   Mathf.Abs(Cross(result[result.Count - 2], result[result.Count - 1], result[0])) < Epsilon)
                result.RemoveAt(result.Count - 1);
            while (result.Count >= 3 &&
                   Mathf.Abs(Cross(result[result.Count - 1], result[0], result[1])) < Epsilon)
                result.RemoveAt(0);
            return result;
        }

        /// <summary>耳切三角化（要求多边形为简单多边形，逆时针方向）。</summary>
        private static int[] EarClip(List<Vector2> polygon)
        {
            if (SignedArea(polygon) < 0f) polygon.Reverse();
            List<int> indices = new List<int>(polygon.Count);
            for (int index = 0; index < polygon.Count; index++) indices.Add(index);
            List<int> triangles = new List<int>(Mathf.Max(0, (polygon.Count - 2) * 3));
            int guard = polygon.Count * polygon.Count + 64;
            while (indices.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < indices.Count; i++)
                {
                    int previous = indices[(i - 1 + indices.Count) % indices.Count];
                    int current = indices[i];
                    int next = indices[(i + 1) % indices.Count];
                    Vector2 a = polygon[previous];
                    Vector2 b = polygon[current];
                    Vector2 c = polygon[next];
                    if (Cross(a, b, c) <= Epsilon) continue;
                    if (HasVertexInside(polygon, indices, a, b, c)) continue;
                    triangles.Add(previous);
                    triangles.Add(current);
                    triangles.Add(next);
                    indices.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (indices.Count == 3)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[1]);
                triangles.Add(indices[2]);
            }
            return triangles.ToArray();
        }

        private static bool HasVertexInside(List<Vector2> polygon, List<int> indices,
            Vector2 a, Vector2 b, Vector2 c)
        {
            foreach (int index in indices)
            {
                Vector2 point = polygon[index];
                if (point == a || point == b || point == c) continue;
                if (PointInTriangle(point, a, b, c)) return true;
            }
            return false;
        }

        private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            bool hasNegative = Cross(a, b, point) < -Epsilon ||
                               Cross(b, c, point) < -Epsilon ||
                               Cross(c, a, point) < -Epsilon;
            bool hasPositive = Cross(a, b, point) > Epsilon ||
                               Cross(b, c, point) > Epsilon ||
                               Cross(c, a, point) > Epsilon;
            return !(hasNegative && hasPositive);
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
            (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        private static float SignedArea(List<Vector2> polygon)
        {
            float sum = 0f;
            for (int index = 0; index < polygon.Count; index++)
            {
                Vector2 current = polygon[index];
                Vector2 next = polygon[(index + 1) % polygon.Count];
                sum += current.x * next.y - next.x * current.y;
            }
            return sum * 0.5f;
        }

        private static float FalloffRadius(List<List<Vector2>> loops)
        {
            float totalArea = 0f;
            foreach (List<Vector2> loop in loops)
                totalArea += Mathf.Abs(SignedArea(loop));
            return totalArea > 0.0001f ? Mathf.Sqrt(totalArea / Mathf.PI) : 1f;
        }

        private static float DistanceToBoundary(Vector2 point, List<Segment> edges)
        {
            float minimum = float.MaxValue;
            foreach (Segment edge in edges)
                minimum = Mathf.Min(minimum, DistanceToSegment(point, edge.a, edge.b));
            return minimum;
        }

        private void SubdivideDome(int aIndex, int bIndex, int cIndex,
            Vector2 a, Vector2 b, Vector2 c, int depth,
            List<Vector3> vertices, List<Color32> colors, List<int> triangles,
            List<Segment> boundaryEdges, float falloffRadius,
            Color32 boundaryColor, Color32 interiorColor, float baseHeight)
        {
            if (depth <= 0)
            {
                triangles.Add(aIndex);
                triangles.Add(bIndex);
                triangles.Add(cIndex);
                return;
            }
            Vector2 ab = (a + b) * 0.5f;
            Vector2 bc = (b + c) * 0.5f;
            Vector2 ca = (c + a) * 0.5f;
            int abIndex = AddDomeVertex(ab, vertices, colors, boundaryEdges, falloffRadius,
                boundaryColor, interiorColor, baseHeight);
            int bcIndex = AddDomeVertex(bc, vertices, colors, boundaryEdges, falloffRadius,
                boundaryColor, interiorColor, baseHeight);
            int caIndex = AddDomeVertex(ca, vertices, colors, boundaryEdges, falloffRadius,
                boundaryColor, interiorColor, baseHeight);
            SubdivideDome(aIndex, abIndex, caIndex, a, ab, ca, depth - 1,
                vertices, colors, triangles, boundaryEdges, falloffRadius, boundaryColor, interiorColor, baseHeight);
            SubdivideDome(abIndex, bIndex, bcIndex, ab, b, bc, depth - 1,
                vertices, colors, triangles, boundaryEdges, falloffRadius, boundaryColor, interiorColor, baseHeight);
            SubdivideDome(caIndex, bcIndex, cIndex, ca, bc, c, depth - 1,
                vertices, colors, triangles, boundaryEdges, falloffRadius, boundaryColor, interiorColor, baseHeight);
            SubdivideDome(abIndex, bcIndex, caIndex, ab, bc, ca, depth - 1,
                vertices, colors, triangles, boundaryEdges, falloffRadius, boundaryColor, interiorColor, baseHeight);
        }

        private int AddDomeVertex(Vector2 point, List<Vector3> vertices, List<Color32> colors,
            List<Segment> boundaryEdges, float falloffRadius, Color32 boundaryColor, Color32 interiorColor,
            float baseHeight)
        {
            float distance = DistanceToBoundary(point, boundaryEdges);
            float t = Mathf.Clamp01(falloffRadius > 0.0001f ? distance / falloffRadius : 1f);
            float smooth = t * t * (3f - 2f * t);
            // 颜色使用更陡的曲线：边界暗色带更宽更明显，内部保持鲜亮。
            float colorT = t * t;
            vertices.Add(new Vector3(point.x, baseHeight + domeHeight * smooth, point.y));
            colors.Add(Color32.Lerp(boundaryColor, interiorColor, colorT));
            return vertices.Count - 1;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            float lengthSquared = delta.sqrMagnitude;
            if (lengthSquared < 1e-10f) return Vector2.Distance(point, a);
            float t = Mathf.Clamp01(Vector2.Dot(point - a, delta) / lengthSquared);
            return Vector2.Distance(point, a + delta * t);
        }

        private static Color32 Darken(Color32 color) =>
            new Color32((byte)(color.r * 0.35f), (byte)(color.g * 0.35f),
                (byte)(color.b * 0.35f), color.a);

        private static Color32 Brighten(Color32 color) =>
            new Color32((byte)Mathf.Min(255, color.r * 1.10f),
                (byte)Mathf.Min(255, color.g * 1.10f),
                (byte)Mathf.Min(255, color.b * 1.10f), color.a);

        private static Vector2 Round(Vector2 value) =>
            new Vector2(Mathf.Round(value.x * 1000f) / 1000f, Mathf.Round(value.y * 1000f) / 1000f);

        private static void AddLineQuad(List<Vector3> vertices, List<Color32> colors, List<int> triangles,
            Vector2 from, Vector2 to, float width, Color32 color, float y)
        {
            Vector2 delta = to - from;
            if (delta.sqrMagnitude < 1e-8f || width <= 0f) return;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (width * 0.5f);
            int start = vertices.Count;
            vertices.Add(new Vector3(from.x - normal.x, y, from.y - normal.y));
            vertices.Add(new Vector3(from.x + normal.x, y, from.y + normal.y));
            vertices.Add(new Vector3(to.x - normal.x, y, to.y - normal.y));
            vertices.Add(new Vector3(to.x + normal.x, y, to.y + normal.y));
            for (int index = 0; index < 4; index++) colors.Add(color);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private struct Segment
        {
            public Vector2 a;
            public Vector2 b;
            public float height;
        }

        private void OnDestroy() => Clear();

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
