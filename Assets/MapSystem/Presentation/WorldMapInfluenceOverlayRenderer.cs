using System.Collections.Generic;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 3D 宗门影响覆盖层：Core / Influence / Outer 三级填充与描边。
    /// 读取 WorldMapProgressState.cellInfluences 的显式缓存；重算仍由
    /// WorldMapInfluenceRules 承担，本组件不自行推导。
    /// </summary>
    public sealed class WorldMapInfluenceOverlayRenderer : MonoBehaviour
    {
        [SerializeField] private float hexRadiusScale = 0.94f;

        private GameObject overlayObject;
        private Mesh ownedMesh;
        private Material ownedMaterial;
        private bool visible = true;

        public int OverlayCellCount { get; private set; }

        public void Render(WorldMap map, WorldMapProgressState progress)
        {
            Clear();
            if (map?.cells == null || progress?.cellInfluences == null) return;

            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            int rendered = 0;
            foreach (CellInfluenceState influence in progress.cellInfluences)
            {
                if (influence == null || influence.level == InfluenceLevel.None ||
                    influence.cellIndex < 0 || influence.cellIndex >= map.cells.Length) continue;
                if (!WorldMapInfluencePresentation.TryGetOverlayStyle(influence.level,
                        out Color color, out float width)) continue;
                WorldCell cell = map.cells[influence.cellIndex];
                if (cell == null) continue;

                Color fillColor = color;
                fillColor.a *= InfluenceFillAlpha(influence.level);
                MapOverlayMeshBuilder.AppendHexOverlay(map, cell, hexRadiusScale,
                    fillColor, color, width, vertices, colors, triangles);
                rendered++;
            }

            OverlayCellCount = rendered;
            ownedMesh = WorldMapHexOverlayGeometry.CreateMesh("WorldMapInfluenceOverlay", vertices, colors, triangles);
            if (ownedMesh == null) return;

            ownedMaterial = WorldMapHexOverlayGeometry.CreateVertexColorMaterial("WorldMapInfluenceOverlay", true);
            overlayObject = WorldMapHexOverlayGeometry.CreateObject("InfluenceOverlay", transform, ownedMesh, ownedMaterial);
            overlayObject.SetActive(visible);
        }

        public void SetVisible(bool active)
        {
            visible = active;
            if (overlayObject != null) overlayObject.SetActive(active);
        }

        public void Clear()
        {
            if (overlayObject != null) DestroyOwned(overlayObject);
            if (ownedMesh != null) DestroyOwned(ownedMesh);
            if (ownedMaterial != null) DestroyOwned(ownedMaterial);
            overlayObject = null;
            ownedMesh = null;
            ownedMaterial = null;
            OverlayCellCount = 0;
        }

        private static float InfluenceFillAlpha(InfluenceLevel level)
        {
            switch (level)
            {
                case InfluenceLevel.Outer: return 0.18f;
                case InfluenceLevel.Influence: return 0.24f;
                case InfluenceLevel.Core: return 0.30f;
                default: return 0f;
            }
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
