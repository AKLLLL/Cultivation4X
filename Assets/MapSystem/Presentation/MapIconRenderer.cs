using System.Collections.Generic;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 地点图标渲染器：根据进度数据中的地点（村庄/洞府/灵矿等）在 3D 地形上方
    /// 生成悬浮图标与地点名标签。图标逻辑与 TerrainRenderer 分离；
    /// 灵脉表现属于更高地图层级，本层不处理。
    /// </summary>
    public sealed class MapIconRenderer : MonoBehaviour
    {
        private static Mesh quadMesh;
        private readonly List<GameObject> iconObjects = new List<GameObject>();
        private readonly HashSet<GameObject> farViewPersistentIcons = new HashSet<GameObject>();
        private readonly List<Vector3> flatIconPositions = new List<Vector3>();
        private readonly Dictionary<int, WorldLocationMarkerView> locationMarkers =
            new Dictionary<int, WorldLocationMarkerView>();
        [SerializeField] private TerrainRenderer terrainRenderer;
        // 默认 false 保持 TerrainTest 演示模式不变；正式游戏预制体设为 true，
        // 真实图标只画在已知格上；Hidden 不画，Hinted 可越过遮罩显示匿名问号。
        [SerializeField] private bool respectKnowledgeMask;
        private bool hasMap;
        private bool politicalMapEnabled = true;
        private int selectedCellIndex = -1;
        private int lastCurveRevision = -1;

        public int IconCount => iconObjects.Count;
        public bool RespectKnowledgeMask => respectKnowledgeMask;

        /// <summary>政治地图模式开关：关闭时地点图标在所有缩放下保持可见。</summary>
        public void SetPoliticalMapEnabled(bool enabled)
        {
            politicalMapEnabled = enabled;
            if (!enabled) SetFarViewVisible(false);
        }

        public void Render(WorldMap map, WorldMapProgressState progress)
        {
            Clear();
            if (map?.cells == null || progress?.mapSites == null) return;
            if (terrainRenderer == null) terrainRenderer = FindObjectOfType<TerrainRenderer>();
            hasMap = true;

            HashSet<int> knownCells = respectKnowledgeMask
                ? WorldMapInfluenceRules.CollectKnownCellIndices(map, progress, false)
                : null;
            HashSet<int> visibleLocationCells = CollectVisibleLocationCells(
                map, progress, knownCells, respectKnowledgeMask);
            foreach (MapSiteData site in progress.mapSites)
            {
                if (site == null || site.cellIndex < 0 || site.cellIndex >= map.cells.Length) continue;
                bool isSectBase = site.siteType == MapSiteType.SectBase;
                bool hinted = !isSectBase &&
                              site.revealState == MapContentRevealState.Hinted;
                if (knownCells != null && !isSectBase && !hinted && !knownCells.Contains(site.cellIndex)) continue;
                if (!isSectBase && site.revealState == MapContentRevealState.Hidden) continue;
                if (visibleLocationCells.Contains(site.cellIndex)) continue;
                WorldCell cell = map.cells[site.cellIndex];
                if (cell == null) continue;

                Vector2 center = HexGeometry.GetCenter(cell);
                float top = MapPresentationLayer.GetIconHeight(map, cell);
                GameObject root = new GameObject(hinted ? "SiteHint_" + site.cellIndex : "SiteIcon_" + site.siteType);
                root.transform.SetParent(transform, false);
                Vector3 flatPosition = transform.TransformPoint(new Vector3(center.x, top, center.y));
                root.transform.position = terrainRenderer != null
                    ? terrainRenderer.CurveWorldPosition(flatPosition)
                    : flatPosition;
                iconObjects.Add(root);
                if (hinted) farViewPersistentIcons.Add(root);
                flatIconPositions.Add(flatPosition);

                GameObject quad = new GameObject("Icon", typeof(MeshFilter), typeof(MeshRenderer));
                quad.transform.SetParent(root.transform, false);
                quad.transform.localPosition = new Vector3(0f, 1.0f, 0f);
                quad.transform.localScale = Vector3.one * (hinted ? 0.72f : 0.9f);
                quad.GetComponent<MeshFilter>().sharedMesh = SharedQuad();
                quad.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(site.siteType, hinted);

                TerrainLabel label = root.AddComponent<TerrainLabel>();
                Color labelColor = hinted
                    ? new Color(0.72f, 0.86f, 1f, 0.86f)
                    : TerrainPresentationModels.ColorForSite(site.siteType);
                label.Set(hinted ? "？" : SiteSymbol(site.siteType), labelColor);
                label.SetCharacterSize(hinted ? 0.18f : 0.16f);
                label.SetYAxisBillboard(true);
            }

            if (map.locations != null)
            {
                foreach (WorldLocation location in map.locations.Values)
                {
                    if (location == null || location.position.x < 0 ||
                        location.position.x >= map.width ||
                        location.position.y < 0 || location.position.y >= map.height) continue;
                    int locationCell = map.GetIndex(
                        new HexCoord(location.position.x, location.position.y));
                    if (locationCell < 0 || locationCell >= map.cells.Length) continue;
                    if (!visibleLocationCells.Contains(locationCell)) continue;
                    AddWorldLocationIcon(map, location, locationCell);
                }
            }
            foreach (SectFunctionalZoneState zone in progress.functionalZones ??
                         new List<SectFunctionalZoneState>())
            {
                if (zone == null || zone.cellIndex < 0 || zone.cellIndex >= map.cells.Length) continue;
                if (knownCells != null && !knownCells.Contains(zone.cellIndex)) continue;
                AddFunctionalZoneIcon(map, zone);
            }
            SetSelectedCell(selectedCellIndex);
        }

        private void AddFunctionalZoneIcon(WorldMap map, SectFunctionalZoneState zone)
        {
            WorldCell cell = map.cells[zone.cellIndex];
            Vector2 center = HexGeometry.GetCenter(cell);
            float top = MapPresentationLayer.GetIconHeight(map, cell);
            GameObject root = new GameObject("FunctionalZone_" + zone.zoneId, typeof(TerrainLabel));
            root.transform.SetParent(transform, false);
            Vector3 flatPosition = transform.TransformPoint(new Vector3(center.x, top, center.y));
            root.transform.position = terrainRenderer != null
                ? terrainRenderer.CurveWorldPosition(flatPosition)
                : flatPosition;
            iconObjects.Add(root);
            farViewPersistentIcons.Add(root);
            flatIconPositions.Add(flatPosition);
            TerrainLabel label = root.GetComponent<TerrainLabel>();
            string symbol = zone.stage == FunctionalZoneStage.Operational ? "圃" :
                zone.stage == FunctionalZoneStage.Developing ? "芽" : "植";
            Color color = zone.stage == FunctionalZoneStage.Operational
                ? new Color(0.22f, 0.94f, 0.34f, 1f)
                : zone.stage == FunctionalZoneStage.Developing
                    ? new Color(0.34f, 0.84f, 0.38f, 0.88f)
                    : new Color(0.46f, 0.72f, 0.43f, 0.72f);
            label.Set(symbol, color);
            label.SetCharacterSize(0.17f);
            label.SetYAxisBillboard(true);
        }

        private static HashSet<int> CollectVisibleLocationCells(WorldMap map,
            WorldMapProgressState progress, HashSet<int> knownCells, bool respectKnowledgeMask)
        {
            HashSet<int> result = new HashSet<int>();
            if (map?.locations == null || map.cells == null) return result;
            foreach (WorldLocation location in map.locations.Values)
            {
                if (location == null) continue;
                bool revealed = WorldLocationRules.IsLocationRevealed(location, progress);
                if (!revealed) continue;
                int cellIndex = map.GetIndex(new HexCoord(location.position.x, location.position.y));
                if (cellIndex < 0 || cellIndex >= map.cells.Length) continue;
                if (respectKnowledgeMask && knownCells != null && !knownCells.Contains(cellIndex))
                    continue;
                result.Add(cellIndex);
            }
            return result;
        }

        private void AddWorldLocationIcon(WorldMap map, WorldLocation location, int cellIndex)
        {
            WorldCell cell = map.cells[map.GetIndex(
                new HexCoord(location.position.x, location.position.y))];
            if (cell == null) return;
            Vector2 center = HexGeometry.GetCenter(cell);
            float top = MapPresentationLayer.GetIconHeight(map, cell);
            GameObject root = LocationMarkerRenderer.CreateRoot(center, top,
                location.type, location.name);
            root.transform.SetParent(transform, false);
            Vector3 flatPosition = transform.TransformPoint(new Vector3(center.x, top, center.y));
            root.transform.position = terrainRenderer != null
                ? terrainRenderer.CurveWorldPosition(flatPosition)
                : flatPosition;
            iconObjects.Add(root);
            flatIconPositions.Add(flatPosition);
            WorldLocationMarkerView marker = root.GetComponent<WorldLocationMarkerView>();
            if (marker != null) locationMarkers[cellIndex] = marker;
        }

        public void SetSelectedCell(int cellIndex)
        {
            selectedCellIndex = cellIndex;
            foreach (KeyValuePair<int, WorldLocationMarkerView> pair in locationMarkers)
                if (pair.Value != null) pair.Value.SetSelected(pair.Key == cellIndex);
        }

        /// <summary>远景模式：隐藏普通地点图标；匿名线索保留，便于玩家寻找探索目标。</summary>
        public void SetFarViewVisible(bool visible)
        {
            foreach (GameObject iconObject in iconObjects)
            {
                if (iconObject != null)
                    iconObject.SetActive(!visible || farViewPersistentIcons.Contains(iconObject));
            }
        }

        private void Update()
        {
            if (!hasMap) return;
            RefreshCurvePositions();
            if (!politicalMapEnabled)
            {
                SetFarViewVisible(false);
                return;
            }
            float hexes = TerrainPresentationModels.VisibleHexesAcross(Camera.main);
            SetFarViewVisible(!WorldMap3DPresentationPolicy.ShowSiteIcons(
                WorldMap3DPresentationPolicy.GetZoomTier(hexes)));
        }

        public void Clear()
        {
            foreach (GameObject iconObject in iconObjects)
            {
                if (iconObject != null) DestroyOwned(iconObject);
            }
            iconObjects.Clear();
            farViewPersistentIcons.Clear();
            flatIconPositions.Clear();
            locationMarkers.Clear();
            hasMap = false;
            lastCurveRevision = -1;
        }

        private void RefreshCurvePositions()
        {
            if (terrainRenderer == null || lastCurveRevision == terrainRenderer.CurveRevision) return;
            int count = Mathf.Min(iconObjects.Count, flatIconPositions.Count);
            for (int index = 0; index < count; index++)
            {
                if (iconObjects[index] != null)
                    iconObjects[index].transform.position =
                        terrainRenderer.CurveWorldPosition(flatIconPositions[index]);
            }
            lastCurveRevision = terrainRenderer.CurveRevision;
        }

        private static Mesh SharedQuad()
        {
            if (quadMesh != null) return quadMesh;
            quadMesh = new Mesh { name = "MapIconQuad" };
            quadMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            };
            quadMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            quadMesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            quadMesh.RecalculateNormals();
            return quadMesh;
        }

        private static string SiteSymbol(MapSiteType siteType)
        {
            switch (siteType)
            {
                case MapSiteType.SectBase: return "宗";
                case MapSiteType.Village: return "村";
                case MapSiteType.SpiritSpring: return "泉";
                case MapSiteType.SpiritMine: return "矿";
                case MapSiteType.ResourceNode: return "材";
                case MapSiteType.CaveResidence: return "府";
                case MapSiteType.BeastLair: return "危";
                case MapSiteType.Ruin: return "墟";
                default: return "点";
            }
        }

        private static Material MaterialFor(MapSiteType siteType, bool hinted)
        {
            // 地点图标同样属于统一地图表现层：ZTest Always，不被地形裁剪。
            Shader overlayShader = Shader.Find("Unlit/VertexColorOverlay");
            Shader fallback = hinted
                ? Shader.Find("Unlit/VertexColorTransparent") ?? Shader.Find("Unlit/VertexColor") ?? Shader.Find("Sprites/Default")
                : Shader.Find("Unlit/VertexColor") ?? Shader.Find("Sprites/Default");
            Shader shader = overlayShader != null ? overlayShader : fallback;
            Material material = new Material(shader);
            material.renderQueue = 4000;
            material.color = hinted
                ? new Color(0.72f, 0.86f, 1f, 0.65f)
                : TerrainPresentationModels.ColorForSite(siteType);
            return material;
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
