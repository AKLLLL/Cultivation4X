using System.Collections.Generic;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 3D 地图渲染管线：只负责把显式传入的 WorldMap / WorldMapProgressState
    /// 分发给各渲染器，不读取 WorldMapSession、PlayerManager 或存档。
    /// 未来 MapSnapshot 落地时，ApplyMap 的参数换成快照即可，各渲染器无需改动。
    /// </summary>
    public sealed class WorldMapRenderPipeline : MonoBehaviour
    {
        [SerializeField] private TerrainRenderer terrainRenderer;
        [SerializeField] private HexGridOverlayRenderer gridRenderer;
        [SerializeField] private WorldMapDecorationRenderer decorationRenderer;
        [SerializeField] private MapIconRenderer iconRenderer;
        [SerializeField] private RegionNameRenderer regionNameRenderer;
        [SerializeField] private RegionOverlayRenderer regionOverlayRenderer;
        [SerializeField] private WorldMapKnowledgeMaskRenderer knowledgeMaskRenderer;
        [SerializeField] private WorldMapInfluenceOverlayRenderer influenceOverlayRenderer;
        [SerializeField] private WorldMapSelectionOverlayRenderer selectionOverlayRenderer;
        [SerializeField] private WorldMapVeinOverlayRenderer veinOverlayRenderer;
        [SerializeField] private bool fullDecoration;
        // 政治表现层默认关闭，保持 TerrainTest 当前验收态（地形 + 森林簇 + 区域名）。
        // 未来 MapSnapshot 落地后再按认知/影响数据显式开启，避免半透明覆盖层
        // 在正式场景中形成“玻璃盖板”。
        [SerializeField] private bool renderRegionOverlay;
        [SerializeField] private bool renderKnowledgeMask;
        [SerializeField] private bool renderInfluenceOverlay;

        public bool RenderRegionOverlay => renderRegionOverlay;
        public bool RenderKnowledgeMask => renderKnowledgeMask;
        public bool RenderInfluenceOverlay => renderInfluenceOverlay;

        private WorldMap currentMap;
        private HashSet<int> knownCells = new HashSet<int>();
        private WorldMapClimateDebugView debugView = WorldMapClimateDebugView.Normal;
        private bool sectPlacementMode;

        public WorldMap CurrentMap => currentMap;
        internal WorldMapClimateDebugView DebugView => debugView;
        public int KnownCellCount => knownCells.Count;
        public TerrainRenderer TerrainRenderer => terrainRenderer;
        public bool SectPlacementMode => sectPlacementMode;

        /// <summary>
        /// 建宗选址模式复用同一个 WorldMapRenderer，只关闭数据型覆盖层
        /// （知识遮罩、影响力、灵脉、图标、区域名），保留地形与 Hex Grid。
        /// </summary>
        public void SetSectPlacementMode(bool active)
        {
            sectPlacementMode = active;
            if (active)
            {
                SetGameObjectActive(knowledgeMaskRenderer, false);
                SetGameObjectActive(influenceOverlayRenderer, false);
                SetGameObjectActive(veinOverlayRenderer, false);
                SetGameObjectActive(iconRenderer, false);
                SetGameObjectActive(regionNameRenderer, false);
                if (decorationRenderer != null && currentMap != null)
                    decorationRenderer.RenderForestTreeClusters(currentMap);
            }
            else if (currentMap != null)
            {
                SetGameObjectActive(iconRenderer, true);
                SetGameObjectActive(regionNameRenderer, true);
                if (renderKnowledgeMask && knowledgeMaskRenderer != null)
                    SetGameObjectActive(knowledgeMaskRenderer, true);
                if (renderInfluenceOverlay && influenceOverlayRenderer != null)
                    SetGameObjectActive(influenceOverlayRenderer, true);
                if (debugView == WorldMapClimateDebugView.SpiritVeinPaths && veinOverlayRenderer != null)
                    SetGameObjectActive(veinOverlayRenderer, true);
            }
        }

        public void ApplyMap(WorldMap map, WorldMapProgressState progress,
            int focusCellIndex, bool revealAll)
        {
            currentMap = map;
            if (map?.cells == null) return;

            knownCells = WorldMapInfluenceRules.CollectKnownCellIndices(map, progress, revealAll);
            if (terrainRenderer != null)
            {
                terrainRenderer.ApplyWorldMapVisualProfile(focusCellIndex);
                terrainRenderer.Render(map);
            }
            if (gridRenderer != null) gridRenderer.Render(map);
            if (decorationRenderer != null)
            {
                if (fullDecoration) decorationRenderer.Render(map, focusCellIndex);
                else decorationRenderer.RenderForestTreeClusters(map);
            }
            if (!sectPlacementMode && regionNameRenderer != null) regionNameRenderer.Render(map);
            else if (regionNameRenderer != null) regionNameRenderer.Clear();
            if (renderRegionOverlay && regionOverlayRenderer != null)
                regionOverlayRenderer.Render(map);
            else if (regionOverlayRenderer != null) regionOverlayRenderer.Clear();
            if (!sectPlacementMode && veinOverlayRenderer != null)
            {
                veinOverlayRenderer.Render(map);
                veinOverlayRenderer.SetVisible(debugView == WorldMapClimateDebugView.SpiritVeinPaths);
            }
            else if (veinOverlayRenderer != null)
            {
                veinOverlayRenderer.Clear();
                veinOverlayRenderer.SetVisible(false);
            }
        }

        public void RefreshDynamicLayers(WorldMap map, WorldMapProgressState progress, bool revealAll)
        {
            if (map?.cells == null) return;
            if (!ReferenceEquals(map, currentMap))
            {
                currentMap = map;
            }

            HashSet<int> nextKnown = WorldMapInfluenceRules.CollectKnownCellIndices(map, progress, revealAll);
            knownCells = nextKnown;
            if (!sectPlacementMode && renderKnowledgeMask && knowledgeMaskRenderer != null)
                knowledgeMaskRenderer.Render(map, nextKnown);
            else if (knowledgeMaskRenderer != null) knowledgeMaskRenderer.Clear();
            if (!sectPlacementMode && renderInfluenceOverlay && influenceOverlayRenderer != null)
                influenceOverlayRenderer.Render(map, progress);
            else if (influenceOverlayRenderer != null) influenceOverlayRenderer.Clear();
            if (!sectPlacementMode && iconRenderer != null) iconRenderer.Render(map, progress);
            else if (iconRenderer != null) iconRenderer.Clear();
        }

        public void RefreshSelection(int selectedCellIndex, bool siteSelectionMode)
        {
            if (currentMap == null) return;
            if (selectionOverlayRenderer != null)
                selectionOverlayRenderer.Render(currentMap, selectedCellIndex, siteSelectionMode);
        }

        /// <summary>
        /// 统一启停世界地图的所有真实表现对象。WorldMapRenderPipeline 自身挂在
        /// 空节点上，因此不能只 SetActive(pipeline.gameObject)，必须逐个切换
        /// 各渲染器/交互对象的 GameObject。
        /// </summary>
        public void SetPresentationsActive(bool active)
        {
            SetGameObjectActive(terrainRenderer, active);
            SetGameObjectActive(gridRenderer, active);
            SetGameObjectActive(decorationRenderer, active);
            SetGameObjectActive(iconRenderer, active);
            SetGameObjectActive(regionNameRenderer, active);
            SetGameObjectActive(regionOverlayRenderer, active);
            SetGameObjectActive(knowledgeMaskRenderer, active);
            SetGameObjectActive(influenceOverlayRenderer, active);
            SetGameObjectActive(selectionOverlayRenderer, active);
            SetGameObjectActive(veinOverlayRenderer, active);
        }

        private static void SetGameObjectActive(Component component, bool active)
        {
            if (component != null) component.gameObject.SetActive(active);
        }

        internal void SetDebugView(WorldMapClimateDebugView view)
        {
            debugView = view;
            if (terrainRenderer != null) terrainRenderer.SetClimateDebugView(view);
            if (veinOverlayRenderer != null)
                veinOverlayRenderer.SetVisible(view == WorldMapClimateDebugView.SpiritVeinPaths);
        }

        public void ClearAll()
        {
            if (knowledgeMaskRenderer != null) knowledgeMaskRenderer.Clear();
            if (influenceOverlayRenderer != null) influenceOverlayRenderer.Clear();
            if (selectionOverlayRenderer != null) selectionOverlayRenderer.Clear();
            if (veinOverlayRenderer != null) veinOverlayRenderer.Clear();
            if (iconRenderer != null) iconRenderer.Clear();
            if (regionNameRenderer != null) regionNameRenderer.Clear();
            if (regionOverlayRenderer != null) regionOverlayRenderer.Clear();
            if (decorationRenderer != null) decorationRenderer.Clear();
            if (gridRenderer != null) gridRenderer.Clear();
            if (terrainRenderer != null) terrainRenderer.Clear();
            currentMap = null;
            knownCells.Clear();
        }

        private void OnDestroy() => ClearAll();
    }
}
