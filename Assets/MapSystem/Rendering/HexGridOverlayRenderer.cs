using System.Collections.Generic;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 战略地图的独立 Hex 操作边界层。边线不写入地形颜色，也不参与碰撞和地图数据。
    /// </summary>
    public sealed class HexGridOverlayRenderer : MonoBehaviour
    {
        [SerializeField] private TerrainRenderer terrainRenderer;
        [SerializeField, Min(0.002f)] private float lineWidth = 0.012f;
        [SerializeField, Range(1, 4)] private int edgeSubdivisions = 2;
        [SerializeField] private Color nearColor = new Color(0.07f, 0.09f, 0.07f, 0.05f);
        [SerializeField] private Color midColor = new Color(0.07f, 0.09f, 0.07f, 0.08f);
        [SerializeField] private Color farColor = new Color(0.07f, 0.09f, 0.07f, 0.16f);

        private GameObject gridObject;
        private Mesh ownedMesh;
        private Material ownedMaterial;
        private WorldMap3DZoomTier activeTier = WorldMap3DZoomTier.Far;
        private bool userVisible = true;

        public int EdgeCount { get; private set; }
        public WorldMap3DZoomTier ActiveZoomTier => activeTier;
        public bool GridVisible => userVisible;
        public Color ActiveColor => ownedMaterial != null ? ownedMaterial.color : Color.clear;
        public float ActiveWidthScale => ownedMaterial != null && ownedMaterial.HasProperty("_WidthScale")
            ? ownedMaterial.GetFloat("_WidthScale") : 1f;
        public float ActiveFogInfluence => ownedMaterial != null && ownedMaterial.HasProperty("_FogInfluence")
            ? ownedMaterial.GetFloat("_FogInfluence") : 1f;

        public void Render(WorldMap map)
        {
            Clear();
            if (map?.cells == null || map.cells.Length == 0) return;

            var vertices = new List<Vector3>();
            var edgeOffsets = new List<Vector2>();
            var triangles = new List<int>();
            EdgeCount = 0;
            foreach (WorldCell cell in map.cells)
            {
                if (cell == null || cell.landform == LandformType.Mountain) continue;
                Vector2 center = TerrainMeshGenerator.HexCenter(cell.coord);
                for (int edge = 0; edge < 6; edge++)
                {
                    int neighbor = map.GetIndex(map.GetNeighbor(cell.coord, edge));
                    if (neighbor >= 0 && neighbor < map.cells.Length &&
                        map.cells[neighbor] != null && map.cells[neighbor].landform == LandformType.Mountain)
                        continue;
                    if (neighbor >= 0 && neighbor < cell.index) continue;
                    WorldCell neighborCell = neighbor >= 0 && neighbor < map.cells.Length
                        ? map.cells[neighbor]
                        : null;
                    AddEdgeRibbon(map, cell, neighborCell, center, edge, vertices, edgeOffsets,
                        triangles);
                    EdgeCount++;
                }
            }

            ownedMesh = new Mesh
            {
                name = "StrategicHexGridMesh",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            ownedMesh.SetVertices(vertices);
            ownedMesh.SetUVs(0, edgeOffsets);
            ownedMesh.SetTriangles(triangles, 0);
            ownedMesh.RecalculateBounds();

            Shader shader = Shader.Find("Cultivation4X/StrategicHexGrid") ?? Shader.Find("Unlit/Color");
            ownedMaterial = new Material(shader) { name = "StrategicHexGridMaterial" };
            gridObject = new GameObject("Strategic Hex Grid", typeof(MeshFilter), typeof(MeshRenderer));
            gridObject.transform.SetParent(transform, false);
            gridObject.GetComponent<MeshFilter>().sharedMesh = ownedMesh;
            gridObject.GetComponent<MeshRenderer>().sharedMaterial = ownedMaterial;
            ApplyTier(terrainRenderer != null ? terrainRenderer.ActiveZoomTier : WorldMap3DZoomTier.Near);
        }

        public void ApplyTier(WorldMap3DZoomTier tier)
        {
            activeTier = tier;
            if (gridObject == null || ownedMaterial == null) return;
            gridObject.SetActive(userVisible && tier != WorldMap3DZoomTier.Far);
            ownedMaterial.color = tier == WorldMap3DZoomTier.Near ? nearColor :
                tier == WorldMap3DZoomTier.Mid ? midColor : farColor;
            if (ownedMaterial.HasProperty("_WidthScale"))
                ownedMaterial.SetFloat("_WidthScale", tier == WorldMap3DZoomTier.Near ? 1f :
                    tier == WorldMap3DZoomTier.Mid ? 1.15f : 1.6f);
            if (ownedMaterial.HasProperty("_FogInfluence"))
                ownedMaterial.SetFloat("_FogInfluence", tier == WorldMap3DZoomTier.Far ? 0.35f : 1f);
        }

        public void SetGridVisible(bool visible)
        {
            userVisible = visible;
            if (gridObject != null)
                gridObject.SetActive(userVisible);
        }

        public void Clear()
        {
            if (gridObject != null) DestroyOwned(gridObject);
            if (ownedMesh != null) DestroyOwned(ownedMesh);
            if (ownedMaterial != null) DestroyOwned(ownedMaterial);
            gridObject = null;
            ownedMesh = null;
            ownedMaterial = null;
            EdgeCount = 0;
        }

        private void Update()
        {
            if (terrainRenderer != null && terrainRenderer.ActiveZoomTier != activeTier)
                ApplyTier(terrainRenderer.ActiveZoomTier);
        }

        private void AddEdgeRibbon(WorldMap map, WorldCell cell, WorldCell neighborCell,
            Vector2 center, int edge, List<Vector3> vertices, List<Vector2> edgeOffsets,
            List<int> triangles)
        {
            float angleA = Mathf.Deg2Rad * (edge * 60f - 30f);
            float angleB = Mathf.Deg2Rad * ((edge + 1) * 60f - 30f);
            Vector2 cornerA = center + new Vector2(Mathf.Cos(angleA), Mathf.Sin(angleA));
            Vector2 cornerB = center + new Vector2(Mathf.Cos(angleB), Mathf.Sin(angleB));
            int segments = Mathf.Clamp(edgeSubdivisions, 1, 4);
            for (int segment = 0; segment < segments; segment++)
            {
                Vector2 a = Vector2.Lerp(cornerA, cornerB, segment / (float)segments);
                Vector2 b = Vector2.Lerp(cornerA, cornerB, (segment + 1f) / segments);
                AddEdgeSegment(map, cell, neighborCell, a, b, vertices, edgeOffsets, triangles);
            }
        }

        private void AddEdgeSegment(WorldMap map, WorldCell cell, WorldCell neighborCell,
            Vector2 pointA, Vector2 pointB, List<Vector3> vertices, List<Vector2> edgeOffsets,
            List<int> triangles)
        {
            float heightA = TerrainRenderer.PresentationSurfaceHeightAt(map, pointA, cell);
            float heightB = TerrainRenderer.PresentationSurfaceHeightAt(map, pointB, cell);
            if (neighborCell != null)
            {
                heightA = Mathf.Max(heightA,
                    TerrainRenderer.PresentationSurfaceHeightAt(map, pointA, neighborCell));
                heightB = Mathf.Max(heightB,
                    TerrainRenderer.PresentationSurfaceHeightAt(map, pointB, neighborCell));
            }
            Vector3 a = new Vector3(pointA.x, heightA + 0.018f, pointA.y);
            Vector3 b = new Vector3(pointB.x, heightB + 0.018f, pointB.y);
            Vector2 flatDirection = (pointB - pointA).normalized;
            Vector3 side = new Vector3(-flatDirection.y, 0f, flatDirection.x) *
                           (lineWidth * 0.5f);
            int start = vertices.Count;
            vertices.Add(a - side);
            vertices.Add(a + side);
            vertices.Add(b - side);
            vertices.Add(b + side);
            Vector2 sideOffset = new Vector2(side.x, side.z);
            edgeOffsets.Add(-sideOffset);
            edgeOffsets.Add(sideOffset);
            edgeOffsets.Add(-sideOffset);
            edgeOffsets.Add(sideOffset);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start + 3);
        }

        private void OnDestroy() => Clear();

        private static void DestroyOwned(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
