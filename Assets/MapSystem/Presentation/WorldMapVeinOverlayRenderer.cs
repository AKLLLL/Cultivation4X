using System.Collections.Generic;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 3D 灵脉路径覆盖层：按 SpiritVein.pathCellIndices 在连续地表上生成带条，
    /// 颜色表示五行主属性，宽度区分大型/中型。只在 SpiritVeinPaths 调试视图显示。
    /// </summary>
    public sealed class WorldMapVeinOverlayRenderer : MonoBehaviour
    {
        [SerializeField] private float largeWidth = 0.16f;
        [SerializeField] private float mediumWidth = 0.09f;

        private GameObject veinObject;
        private Mesh ownedMesh;
        private Material ownedMaterial;
        private bool visible;

        public int VeinSegmentCount { get; private set; }

        public void Render(WorldMap map)
        {
            Clear();
            if (map?.cells == null || map.spiritVeins == null) return;

            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            int segments = 0;
            foreach (SpiritVein vein in map.spiritVeins)
            {
                if (vein == null || vein.pathCellIndices == null || vein.pathCellIndices.Count == 0) continue;
                Color32 color = TerrainPresentationModels.SpiritElementColor(vein.primaryElement);
                float width = vein.size == SpiritVeinSize.Large ? largeWidth : mediumWidth;
                for (int position = 1; position < vein.pathCellIndices.Count; position++)
                {
                    int from = vein.pathCellIndices[position - 1];
                    int to = vein.pathCellIndices[position];
                    if (from < 0 || from >= map.cells.Length || to < 0 || to >= map.cells.Length ||
                        map.cells[from] == null || map.cells[to] == null) continue;
                    Vector2 fromCenter = HexGeometry.GetCenter(map.cells[from]);
                    Vector2 toCenter = HexGeometry.GetCenter(map.cells[to]);
                    float height = Mathf.Max(
                        MapPresentationLayer.GetHeight(map, map.cells[from]),
                        MapPresentationLayer.GetHeight(map, map.cells[to]));
                    WorldMapHexOverlayGeometry.AppendSegment(vertices, colors, triangles,
                        new Vector3(fromCenter.x, height, fromCenter.y),
                        new Vector3(toCenter.x, height, toCenter.y),
                        width, color);
                    segments++;
                }
            }

            VeinSegmentCount = segments;
            ownedMesh = WorldMapHexOverlayGeometry.CreateMesh("WorldMapVeinPaths", vertices, colors, triangles);
            if (ownedMesh == null) return;
            ownedMaterial = WorldMapHexOverlayGeometry.CreateVertexColorMaterial("WorldMapVeinPaths", true);
            veinObject = WorldMapHexOverlayGeometry.CreateObject("SpiritVeinPaths", transform, ownedMesh, ownedMaterial);
            veinObject.SetActive(visible);
        }

        public void SetVisible(bool active)
        {
            visible = active;
            if (veinObject != null) veinObject.SetActive(active);
        }

        public void Clear()
        {
            if (veinObject != null) DestroyOwned(veinObject);
            if (ownedMesh != null) DestroyOwned(ownedMesh);
            if (ownedMaterial != null) DestroyOwned(ownedMaterial);
            veinObject = null;
            ownedMesh = null;
            ownedMaterial = null;
            VeinSegmentCount = 0;
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
