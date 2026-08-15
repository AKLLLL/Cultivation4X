using System.Collections.Generic;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 宏观区域名渲染器：在区域中心显示地貌名称（山脉/山谷/湖泊…），
    /// 只在拉远到设定格数后显示，拉近时隐藏，避免遮挡格子信息。
    /// </summary>
    public sealed class RegionNameRenderer : MonoBehaviour
    {
        [SerializeField] private float labelHeightOffset = 0.15f;
        private readonly List<GameObject> labels = new List<GameObject>();
        private bool hasMap;
        private bool politicalMapEnabled = true;

        public int LabelCount => labels.Count;

        /// <summary>政治地图模式开关：关闭时隐藏全部区域名。</summary>
        public void SetPoliticalMapEnabled(bool enabled)
        {
            politicalMapEnabled = enabled;
            if (!enabled) SetLabelsActive(false);
        }

        public void Render(WorldMap map)
        {
            Clear();
            hasMap = map != null && map.cells != null && map.cells.Length > 0;
            if (!hasMap) return;
            if (map.regions == null) return;
            int minimumRegionCells = TerrainPresentationModels.RegionOverlayMinimumCells(map);
            List<MapRegionData> selectedRegions = WorldMap3DPresentationPolicy.SelectRegionLabels(map,
                map.regions, minimumRegionCells,
                WorldMap3DPresentationPolicy.RegionLabelLimit(WorldMap3DZoomTier.Far));

            foreach (MapRegionData region in selectedRegions)
            {
                WorldCell centerCell = map.cells[region.centerCellIndex];
                if (centerCell == null) continue;

                Vector2 center = TerrainMeshGenerator.HexCenter(centerCell.coord);
                GameObject labelObject = new GameObject("RegionLabel_" + region.regionId);
                labelObject.transform.SetParent(transform, false);
                // 区域名印在地形表面（跟随区域中心地形高度），不浮空。
                labelObject.transform.position =
                    new Vector3(center.x,
                        TerrainPresentationModels.RegionOverlayBaseHeight(map, region) + labelHeightOffset,
                        center.y);
                TerrainLabel label = labelObject.AddComponent<TerrainLabel>();
                string text = string.IsNullOrEmpty(region.regionName)
                    ? TerrainPresentationModels.RegionLabel(region.regionType)
                    : region.regionName;
                label.Set(text, TerrainPresentationModels.ColorForRegion(region.regionType));
                label.SetFlat(true);
                label.SetCharacterSize(0.22f);
                labels.Add(labelObject);
            }
        }

        public void Clear()
        {
            foreach (GameObject labelObject in labels)
            {
                if (labelObject != null) DestroyOwned(labelObject);
            }
            labels.Clear();
            hasMap = false;
        }

        public void SetLabelsActive(bool active)
        {
            foreach (GameObject labelObject in labels)
            {
                if (labelObject != null) labelObject.SetActive(active);
            }
        }

        private void Update()
        {
            if (!hasMap) return;
            if (!politicalMapEnabled)
            {
                SetLabelsActive(false);
                return;
            }
            float hexes = TerrainPresentationModels.VisibleHexesAcross(Camera.main);
            bool showFar = WorldMap3DPresentationPolicy.ShowRegionLabels(
                WorldMap3DPresentationPolicy.GetZoomTier(hexes));
            SetLabelsActive(showFar);
            if (showFar)
            {
                // 字号随缩放自适应但保持克制：最远约半格宽，不会糊满地图。
                float size = Mathf.Clamp(hexes * 0.004f, 0.25f, 0.6f);
                foreach (GameObject labelObject in labels)
                {
                    if (labelObject != null)
                    {
                        TerrainLabel label = labelObject.GetComponent<TerrainLabel>();
                        if (label != null) label.SetCharacterSize(size);
                    }
                }
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
