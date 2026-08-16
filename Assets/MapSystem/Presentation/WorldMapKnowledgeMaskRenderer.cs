using System.Collections.Generic;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 世界认知迷雾：不是“未知格六角遮罩”，而是覆盖在连续地形上的一整片软雾。
    ///
    /// 视觉规则：
    /// - 未知区域保留地形与群系颜色可读性，只降低对比度/饱和度观感；
    /// - 相邻格共享同一角点顶点，内部没有六角边界的重叠混合线；
    /// - 距离已知格越近，雾越淡，边界平滑过渡；
    /// - 内容隐藏（地点/资源/灵兽）由 MapIconRenderer 与认知状态负责，本层不处理。
    ///
    /// 实现仍按 MapOverlayMeshBuilder 的约定：HexGeometry 取角点、
    /// TerrainRenderer 采样地表高度、MapPresentationLayer 加统一偏移，
    /// 并使用 ZTest Always 的 Overlay Shader。
    /// </summary>
    public sealed class WorldMapKnowledgeMaskRenderer : MonoBehaviour
    {
        [SerializeField] private Color fogColor = new Color(0.06f, 0.09f, 0.14f, 0.55f);
        [Tooltip("雾在多少格内从已知边界淡入；超过该距离为满雾。")]
        [SerializeField] private int fogFalloffCells = 1;

        private GameObject fogObject;
        private Mesh ownedMesh;
        private Material ownedMaterial;
        private bool visible = true;

        public int HiddenCellCount { get; private set; }
        public bool MaskVisible => fogObject != null && fogObject.activeSelf;

        private struct CornerKey : System.IEquatable<CornerKey>
        {
            private const float Precision = 1000f;
            private readonly int x;
            private readonly int z;
            private readonly bool water;

            public CornerKey(Vector2 position, bool water)
            {
                x = Mathf.RoundToInt(position.x * Precision);
                z = Mathf.RoundToInt(position.y * Precision);
                this.water = water;
            }

            public bool Equals(CornerKey other) =>
                x == other.x && z == other.z && water == other.water;
            public override bool Equals(object obj) => obj is CornerKey other && Equals(other);
            public override int GetHashCode() => unchecked(((x * 397) ^ z) * 397 ^ (water ? 1 : 0));
        }

        public void Render(WorldMap map, IReadOnlyCollection<int> knownCellIndices)
        {
            Clear();
            if (map?.cells == null || map.cells.Length == 0) return;

            HashSet<int> known = knownCellIndices != null
                ? new HashSet<int>(knownCellIndices)
                : new HashSet<int>();
            float[] fogAlpha = BuildFogAlphaField(map, known);
            HiddenCellCount = 0;
            for (int index = 0; index < map.cells.Length; index++)
                if (map.cells[index] != null && !known.Contains(index)) HiddenCellCount++;
            if (HiddenCellCount == 0) return;

            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            var cornerVertices = new Dictionary<CornerKey, int>();

            for (int index = 0; index < map.cells.Length; index++)
            {
                WorldCell cell = map.cells[index];
                if (cell == null || known.Contains(index)) continue;

                float centerAlpha = fogAlpha[index];
                if (centerAlpha <= 0.001f) continue;
                Vector2 center = HexGeometry.GetCenter(cell);
                Vector2[] corners = HexGeometry.GetCorners(center, HexGeometry.GetRadius());
                float centerHeight = MapPresentationLayer.GetHeightAt(map, center, cell);
                int centerVertex = vertices.Count;
                vertices.Add(new Vector3(center.x, centerHeight, center.y));
                colors.Add(FogVertexColor(centerAlpha));

                bool water = IsWater(cell);
                int[] cornerIndices = new int[6];
                for (int corner = 0; corner < 6; corner++)
                {
                    float cornerAlpha = CornerAlpha(map, corners[corner], fogAlpha);
                    CornerKey key = new CornerKey(corners[corner], water);
                    if (!cornerVertices.TryGetValue(key, out int cornerVertex))
                    {
                        float cornerHeight = MapPresentationLayer.GetHeightAt(map,
                            corners[corner], cell);
                        cornerVertex = vertices.Count;
                        vertices.Add(new Vector3(corners[corner].x, cornerHeight, corners[corner].y));
                        colors.Add(FogVertexColor(cornerAlpha));
                        cornerVertices.Add(key, cornerVertex);
                    }
                    cornerIndices[corner] = cornerVertex;
                }

                for (int corner = 0; corner < 6; corner++)
                {
                    triangles.Add(centerVertex);
                    triangles.Add(cornerIndices[(corner + 1) % 6]);
                    triangles.Add(cornerIndices[corner]);
                }
            }

            ownedMesh = WorldMapHexOverlayGeometry.CreateMesh(
                "WorldMapKnowledgeFog", vertices, colors, triangles);
            if (ownedMesh == null) return;
            ownedMaterial = WorldMapHexOverlayGeometry.CreateVertexColorMaterial(
                "WorldMapKnowledgeFog", true);
            fogObject = WorldMapHexOverlayGeometry.CreateObject(
                "KnowledgeFog", transform, ownedMesh, ownedMaterial);
            fogObject.SetActive(visible);
        }

        private float[] BuildFogAlphaField(WorldMap map, HashSet<int> known)
        {
            float[] alpha = new float[map.cells.Length];
            int[] distance = new int[map.cells.Length];
            for (int index = 0; index < distance.Length; index++)
                distance[index] = int.MaxValue;
            Queue<int> queue = new Queue<int>();
            foreach (int index in known)
            {
                if (index < 0 || index >= distance.Length) continue;
                distance[index] = 0;
                queue.Enqueue(index);
            }
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int neighbor in map.GetNeighborIndices(current))
                {
                    if (distance[neighbor] <= distance[current] + 1) continue;
                    distance[neighbor] = distance[current] + 1;
                    queue.Enqueue(neighbor);
                }
            }

            float falloff = Mathf.Max(1, fogFalloffCells) + 1f;
            for (int index = 0; index < alpha.Length; index++)
            {
                if (known.Contains(index)) continue;
                float t = distance[index] == int.MaxValue
                    ? 1f
                    : Mathf.Clamp01(distance[index] / falloff);
                alpha[index] = fogColor.a * t * t * (3f - 2f * t);
            }
            return alpha;
        }

        private static float CornerAlpha(WorldMap map, Vector2 corner, float[] fogAlpha)
        {
            HexCoord guess = HexGeometry.GetCoordFromWorld(
                new Vector3(corner.x, 0f, corner.y));
            float minimum = 1f;
            for (int row = guess.row - 1; row <= guess.row + 1; row++)
            {
                for (int col = guess.col - 1; col <= guess.col + 1; col++)
                {
                    int index = map.GetIndex(new HexCoord(col, row));
                    if (index < 0 || index >= map.cells.Length || map.cells[index] == null) continue;
                    if ((HexGeometry.GetCenter(map.cells[index]) - corner).sqrMagnitude >
                        HexGeometry.Radius * HexGeometry.Radius + 0.0001f) continue;
                    minimum = Mathf.Min(minimum, fogAlpha[index]);
                }
            }
            return minimum;
        }

        private Color32 FogVertexColor(float alpha)
        {
            Color color = fogColor;
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        public void SetVisible(bool active)
        {
            visible = active;
            if (fogObject != null) fogObject.SetActive(active);
        }

        public void Clear()
        {
            if (fogObject != null) DestroyOwned(fogObject);
            if (ownedMesh != null) DestroyOwned(ownedMesh);
            if (ownedMaterial != null) DestroyOwned(ownedMaterial);
            fogObject = null;
            ownedMesh = null;
            ownedMaterial = null;
            HiddenCellCount = 0;
        }

        private static bool IsWater(WorldCell cell) =>
            cell != null && (cell.landform == LandformType.DeepWater ||
                             cell.landform == LandformType.ShallowWater);

        private void OnDestroy() => Clear();

        private static void DestroyOwned(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
