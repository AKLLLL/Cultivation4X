using System.Collections.Generic;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 3D 选址/选中覆盖层：
    /// - WorldSelection 阶段高亮所有“山地台地”可建格（LandformType.Mountain && isBuildable）；
    /// - 任意阶段给当前选中格画白色六边环。
    /// 几何完全来自 HexGeometryService；高度按每个角点采样连续地表，
    /// 避免环的某条边低于山体网格而被裁剪。
    /// </summary>
    public sealed class WorldMapSelectionOverlayRenderer : MonoBehaviour
    {
        [SerializeField] private Color plateauFillColor = new Color(1f, 0.82f, 0.36f, 0.30f);
        [SerializeField] private Color plateauOutlineColor = new Color(1f, 0.88f, 0.50f, 0.95f);
        [SerializeField] private Color selectedRingColor = Color.white;
        [SerializeField] private float plateauOutlineWidth = 0.08f;
        [SerializeField] private float selectedRingWidth = 0.13f;
        [SerializeField] private float hexRadiusScale = 0.92f;

        private GameObject plateauObject;
        private Mesh plateauMesh;
        private GameObject ringObject;
        private Mesh ringMesh;
        private Material plateauMaterial;
        private Material ringMaterial;
        private WorldMap lastMap;
        private WorldMap ringMap;
        private bool lastSiteSelectionMode;
        private int lastSelectedCellIndex = -1;
        private TerrainRenderer.MapDetailLevel builtPlateauDetailLevel;
        private bool builtPlateauContinuousSurface;
        private bool plateauVisible = true;
        private bool ringVisible = true;

        public int PlateauCount { get; private set; }
        public int SelectedCellIndex => lastSelectedCellIndex;

        public void Render(WorldMap map, int selectedCellIndex, bool siteSelectionMode)
        {
            if (map == null || map.cells == null || map.cells.Length == 0)
            {
                Clear();
                return;
            }

            TerrainRenderer.MapDetailLevel detailLevel = TerrainRenderer.ActiveDetailLevel;
            bool continuousSurface = TerrainRenderer.ActiveContinuousSurface;
            bool plateauDirty = plateauObject == null || lastMap != map ||
                                lastSiteSelectionMode != siteSelectionMode ||
                                builtPlateauDetailLevel != detailLevel ||
                                builtPlateauContinuousSurface != continuousSurface;
            if (plateauDirty)
            {
                RebuildPlateauOverlay(map, siteSelectionMode);
                builtPlateauDetailLevel = detailLevel;
                builtPlateauContinuousSurface = continuousSurface;
            }
            lastMap = map;
            lastSiteSelectionMode = siteSelectionMode;

            if (ringObject == null || ringMap != map || plateauDirty ||
                lastSelectedCellIndex != selectedCellIndex)
                RebuildRing(map, selectedCellIndex);
            ringMap = map;
            lastSelectedCellIndex = selectedCellIndex;
            ApplyVisibility();
        }

        private void RebuildPlateauOverlay(WorldMap map, bool siteSelectionMode)
        {
            DestroyPlateau();
            PlateauCount = 0;
            if (!siteSelectionMode) return;

            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            foreach (WorldCell cell in map.cells)
            {
                if (!IsSelectablePlateau(cell)) continue;
                MapOverlayMeshBuilder.AppendHexOverlay(map, cell, hexRadiusScale,
                    plateauFillColor, plateauOutlineColor, plateauOutlineWidth,
                    vertices, colors, triangles);
                PlateauCount++;
            }

            plateauMesh = WorldMapHexOverlayGeometry.CreateMesh("WorldMapSiteSelectionPlateau",
                vertices, colors, triangles);
            if (plateauMesh == null) return;
            if (plateauMaterial == null)
                plateauMaterial = WorldMapHexOverlayGeometry.CreateVertexColorMaterial(
                    "WorldMapSiteSelectionPlateau", true);
            plateauObject = WorldMapHexOverlayGeometry.CreateObject("SiteSelectionPlateau",
                transform, plateauMesh, plateauMaterial);
        }

        private void RebuildRing(WorldMap map, int selectedCellIndex)
        {
            DestroyRing();
            if (selectedCellIndex < 0 || selectedCellIndex >= map.cells.Length ||
                map.cells[selectedCellIndex] == null) return;

            WorldCell cell = map.cells[selectedCellIndex];
            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            MapOverlayMeshBuilder.AppendHexRing(map, cell, hexRadiusScale,
                selectedRingWidth, selectedRingColor, vertices, colors, triangles);
            ringMesh = WorldMapHexOverlayGeometry.CreateMesh("WorldMapSelectionRing",
                vertices, colors, triangles);
            if (ringMesh == null) return;
            if (ringMaterial == null)
                ringMaterial = WorldMapHexOverlayGeometry.CreateVertexColorMaterial(
                    "WorldMapSelectionRing", true);
            ringObject = WorldMapHexOverlayGeometry.CreateObject("SelectedCellRing",
                transform, ringMesh, ringMaterial);
        }

        public void SetPlateauVisible(bool active)
        {
            plateauVisible = active;
            ApplyVisibility();
        }

        public void SetRingVisible(bool active)
        {
            ringVisible = active;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            if (plateauObject != null) plateauObject.SetActive(plateauVisible);
            if (ringObject != null) ringObject.SetActive(ringVisible);
        }

        public void Clear()
        {
            DestroyPlateau();
            DestroyRing();
            lastMap = null;
            ringMap = null;
            lastSiteSelectionMode = false;
            lastSelectedCellIndex = -1;
            builtPlateauDetailLevel = TerrainRenderer.ActiveDetailLevel;
            builtPlateauContinuousSurface = TerrainRenderer.ActiveContinuousSurface;
            PlateauCount = 0;
        }

        private void DestroyPlateau()
        {
            if (plateauObject != null) DestroyOwned(plateauObject);
            if (plateauMesh != null) DestroyOwned(plateauMesh);
            plateauObject = null;
            plateauMesh = null;
        }

        private void DestroyRing()
        {
            if (ringObject != null) DestroyOwned(ringObject);
            if (ringMesh != null) DestroyOwned(ringMesh);
            ringObject = null;
            ringMesh = null;
        }

        private static bool IsSelectablePlateau(WorldCell cell) =>
            cell != null && cell.landform == LandformType.Mountain && cell.isBuildable;

        private void OnDestroy()
        {
            Clear();
            if (plateauMaterial != null) DestroyOwned(plateauMaterial);
            if (ringMaterial != null) DestroyOwned(ringMaterial);
            plateauMaterial = null;
            ringMaterial = null;
        }

        private static void DestroyOwned(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
