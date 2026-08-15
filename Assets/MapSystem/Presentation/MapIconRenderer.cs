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
        private readonly List<Vector3> flatIconPositions = new List<Vector3>();
        [SerializeField] private TerrainRenderer terrainRenderer;
        private bool hasMap;
        private bool politicalMapEnabled = true;
        private int lastCurveRevision = -1;

        public int IconCount => iconObjects.Count;

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

            foreach (MapSiteData site in progress.mapSites)
            {
                if (site == null || site.cellIndex < 0 || site.cellIndex >= map.cells.Length) continue;
                WorldCell cell = map.cells[site.cellIndex];
                if (cell == null) continue;

                Vector2 center = TerrainMeshGenerator.HexCenter(cell.coord);
                float top = TerrainRenderer.PresentationSurfaceHeight(map, cell);
                GameObject root = new GameObject("SiteIcon_" + site.siteType);
                root.transform.SetParent(transform, false);
                Vector3 flatPosition = transform.TransformPoint(new Vector3(center.x, top + 1.2f, center.y));
                root.transform.position = terrainRenderer != null
                    ? terrainRenderer.CurveWorldPosition(flatPosition)
                    : flatPosition;
                iconObjects.Add(root);
                flatIconPositions.Add(flatPosition);

                GameObject quad = new GameObject("Icon", typeof(MeshFilter), typeof(MeshRenderer));
                quad.transform.SetParent(root.transform, false);
                quad.transform.localPosition = new Vector3(0f, 1.0f, 0f);
                quad.transform.localScale = Vector3.one * 0.9f;
                quad.GetComponent<MeshFilter>().sharedMesh = SharedQuad();
                quad.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(site.siteType);

                TerrainLabel label = root.AddComponent<TerrainLabel>();
                label.Set(string.IsNullOrEmpty(site.siteName)
                        ? TerrainPresentationModels.SiteLabel(site.siteType)
                        : site.siteName,
                    TerrainPresentationModels.ColorForSite(site.siteType));
            }
        }

        /// <summary>远景模式：隐藏地点图标，避免遮挡区域色与区域名。</summary>
        public void SetFarViewVisible(bool visible)
        {
            foreach (GameObject iconObject in iconObjects)
            {
                if (iconObject != null) iconObject.SetActive(!visible);
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
            flatIconPositions.Clear();
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

        private static Material MaterialFor(MapSiteType siteType)
        {
            Shader shader = Shader.Find("Unlit/VertexColor") ?? Shader.Find("Sprites/Default");
            Material material = new Material(shader);
            material.color = TerrainPresentationModels.ColorForSite(siteType);
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
