using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 世界地图区域装饰层。模型按 MapRegionData 批量分布并合并，而不是围绕焦点逐格创建。
    /// WorldCell 仍是定位和交互单位；区域对象只负责表现，不写回地图或存档。
    /// </summary>
    public sealed class WorldMapDecorationRenderer : MonoBehaviour
    {
        private enum DecorationKind
        {
            LargeTree,
            Birch,
            Rock,
            FlowerBush,
            Grass,
            MountainKey,
            HillKey,
            ValleyWall
        }

        private sealed class DecorationInstance
        {
            public Transform transform;
            public WorldCell anchorCell;
            public Quaternion baseRotation;
            public Mesh ownedMesh;
        }

        private sealed class TerrainMarkerInstance
        {
            public Transform transform;
            public WorldCell anchorCell;
            public MeshRenderer renderer;
            public Mesh ownedMesh;
        }

        [Header("Dreamscape map adapters")]
        [SerializeField] private GameObject largeTreePrefab;
        [SerializeField] private GameObject birchPrefab;
        [SerializeField] private GameObject rockPrefab;
        [SerializeField] private GameObject rockPrefab02;
        [SerializeField] private GameObject rockPrefab03;
        [SerializeField] private GameObject rockPrefab04;
        [SerializeField] private GameObject flowerBushPrefab;
        [SerializeField] private GameObject grassPrefab;
        [Header("CC0 near-view landform adapters")]
        [SerializeField] private GameObject mountainKeyPrefabA;
        [SerializeField] private GameObject mountainKeyPrefabB;
        [SerializeField] private GameObject mountainKeyPrefabC;
        [SerializeField] private GameObject hillKeyPrefabA;
        [SerializeField] private GameObject hillKeyPrefabB;
        [SerializeField] private GameObject hillKeyPrefabC;
        [SerializeField] private GameObject valleyWallPrefabA;
        [SerializeField] private GameObject valleyWallPrefabB;
        [Header("Painted mountain range")]
        [SerializeField] private Texture2D[] paintedMountainTextures = new Texture2D[0];
        [SerializeField] private TerrainRenderer terrainRenderer;

        private readonly List<DecorationInstance> instances = new List<DecorationInstance>();
        private readonly List<TerrainMarkerInstance> terrainMarkers = new List<TerrainMarkerInstance>();
        private Transform structuralRoot;
        private Transform detailRoot;
        private Transform markerRoot;
        private Material markerMaterial;
        private Material[] paintedMountainMaterials;
        private Material regionTerrainMaterial;
        private Material modularCliffMaterial;
        private readonly Dictionary<string, GameObject> modularCliffPrefabs =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private WorldMap map;
        private WorldMap3DZoomTier activeTier = WorldMap3DZoomTier.Far;
        private int lastCurveRevision = -1;

        public int DecorationCount => instances.Count;
        public int StructuralCount => structuralRoot == null ? 0 : structuralRoot.childCount;
        public int DetailCount => detailRoot == null ? 0 : detailRoot.childCount;
        public int TerrainMarkerCount => terrainMarkers.Count;
        public WorldMap3DZoomTier ActiveZoomTier => activeTier;

        /// <summary>
        /// 为全图所有 Region 生成区域级模型。focusCellIndex 仅为兼容现有调用保留，
        /// 不再限制生成范围。
        /// </summary>
        public void Render(WorldMap worldMap, int focusCellIndex)
        {
            Clear();
            map = worldMap;
            if (map?.cells == null || map.cells.Length == 0) return;

            EnsureRoots();
            IEnumerable<MapRegionData> regions = map.regions ?? new List<MapRegionData>();
            foreach (MapRegionData region in regions
                         .Where(item => item?.cellIndices != null && item.cellIndices.Count > 0)
                         .OrderBy(item => item.cellIndices.Min())
                         .ThenBy(item => item.regionId, StringComparer.Ordinal))
            {
                List<WorldCell> cells = RegionCells(region);
                if (cells.Count == 0) continue;
                SpawnLandformStructure(region, cells);
                SpawnRegionLayer(region, cells, true);
                SpawnTerrainMarker(region, cells);
            }

            ApplyTier(terrainRenderer != null
                ? terrainRenderer.ActiveZoomTier
                : WorldMap3DZoomTier.Near);
        }

        public void ApplyTier(WorldMap3DZoomTier tier)
        {
            activeTier = tier;
            EnsureRoots();
            // Region 级结构在所有缩放层级都保留；远景由标识补充语义，中景不再突然冒出山林。
            float fallbackHexes = tier == WorldMap3DZoomTier.Far ? 40f :
                tier == WorldMap3DZoomTier.Mid ? 22f : 8f;
            ApplyContinuousLod(fallbackHexes);
            RefreshHeights();
        }

        public void Clear()
        {
            foreach (DecorationInstance entry in instances)
            {
                if (entry?.ownedMesh != null) DestroyOwned(entry.ownedMesh);
                if (entry?.transform != null) DestroyOwned(entry.transform.gameObject);
            }
            instances.Clear();
            foreach (TerrainMarkerInstance marker in terrainMarkers)
            {
                if (marker?.ownedMesh != null) DestroyOwned(marker.ownedMesh);
                if (marker?.transform != null) DestroyOwned(marker.transform.gameObject);
            }
            terrainMarkers.Clear();
            map = null;
            lastCurveRevision = -1;
        }

        private void Update()
        {
            if (terrainRenderer == null || map == null) return;
            if (terrainRenderer.ActiveZoomTier != activeTier)
            {
                ApplyTier(terrainRenderer.ActiveZoomTier);
                return;
            }
            ApplyContinuousLod(terrainRenderer.ActiveVisibleHexesAcross);
            if (lastCurveRevision != terrainRenderer.CurveRevision) RefreshHeights();
        }

        private void SpawnTerrainMarker(MapRegionData region, List<WorldCell> cells)
        {
            if (region == null || cells == null || cells.Count == 0 ||
                region.regionType == MapRegionType.Plain ||
                // Forest terrain markers are flat three-diamond proxies.  In the current
                // 3D view they read as pale canopy plates and overlap the actual tree cluster.
                // Far-map iconography is postponed, so keep the forest represented only by
                // its ground colour and near-detail tree mesh for now.
                region.regionType == MapRegionType.Forest ||
                region.regionType == MapRegionType.OpenWater ||
                region.regionType == MapRegionType.Lake) return;
            WorldCell anchor = RegionAnchor(region, cells);
            if (anchor == null) return;
            EnsureMarkerMaterial();
            if (markerMaterial == null) return;

            Mesh mesh = BuildTerrainMarkerMesh(region.regionType, cells.Count);
            if (mesh == null) return;
            var markerObject = new GameObject($"Terrain Marker {region.regionType} {region.regionId}",
                typeof(MeshFilter), typeof(MeshRenderer));
            markerObject.transform.SetParent(markerRoot, false);
            markerObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer meshRenderer = markerObject.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = markerMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            var marker = new TerrainMarkerInstance
            {
                transform = markerObject.transform,
                anchorCell = anchor,
                renderer = meshRenderer,
                ownedMesh = mesh
            };
            terrainMarkers.Add(marker);
            Position(marker);
        }

        private static Mesh BuildTerrainMarkerMesh(MapRegionType type, int cellCount)
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            Color color = TerrainPresentationModels.ColorForRegion(type);
            color.a = 0.92f;
            float size = Mathf.Clamp(0.68f + Mathf.Sqrt(Mathf.Max(1, cellCount)) * 0.10f,
                0.82f, 1.55f);
            switch (type)
            {
                case MapRegionType.MountainRange:
                    AddTriangle(vertices, colors, triangles, new Vector2(-0.62f, -0.30f) * size,
                        new Vector2(0f, 0.72f) * size, new Vector2(0.62f, -0.30f) * size, color);
                    AddTriangle(vertices, colors, triangles, new Vector2(-0.92f, -0.38f) * size,
                        new Vector2(-0.45f, 0.35f) * size, new Vector2(-0.08f, -0.38f) * size, color * 0.82f);
                    break;
                case MapRegionType.Forest:
                    AddDiamond(vertices, colors, triangles, new Vector2(-0.48f, 0f) * size, 0.48f * size, color);
                    AddDiamond(vertices, colors, triangles, new Vector2(0.40f, 0.12f) * size, 0.56f * size, color * 0.86f);
                    AddDiamond(vertices, colors, triangles, new Vector2(0f, -0.42f) * size, 0.40f * size, color * 1.08f);
                    break;
                case MapRegionType.Valley:
                    AddQuad(vertices, colors, triangles, new Vector2(-0.70f, -0.55f) * size,
                        new Vector2(-0.34f, 0.65f) * size, new Vector2(-0.12f, 0.52f) * size,
                        new Vector2(-0.46f, -0.62f) * size, color);
                    AddQuad(vertices, colors, triangles, new Vector2(0.46f, -0.62f) * size,
                        new Vector2(0.12f, 0.52f) * size, new Vector2(0.34f, 0.65f) * size,
                        new Vector2(0.70f, -0.55f) * size, color);
                    break;
                case MapRegionType.Desert:
                    AddDiamond(vertices, colors, triangles, new Vector2(-0.42f, -0.10f) * size, 0.48f * size, color);
                    AddDiamond(vertices, colors, triangles, new Vector2(0.38f, 0.10f) * size, 0.52f * size, color * 0.88f);
                    break;
                case MapRegionType.Swamp:
                    AddDiamond(vertices, colors, triangles, new Vector2(-0.38f, 0.18f) * size, 0.34f * size, color);
                    AddDiamond(vertices, colors, triangles, new Vector2(0.34f, 0.20f) * size, 0.30f * size, color);
                    AddDiamond(vertices, colors, triangles, new Vector2(0f, -0.35f) * size, 0.38f * size, color * 0.82f);
                    break;
                default:
                    AddDiamond(vertices, colors, triangles, Vector2.zero, 0.72f * size, color);
                    break;
            }
            if (vertices.Count == 0) return null;
            var mesh = new Mesh { name = "Region Terrain Marker" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddTriangle(List<Vector3> vertices, List<Color> colors,
            List<int> triangles, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(a.x, 0f, a.y));
            vertices.Add(new Vector3(b.x, 0f, b.y));
            vertices.Add(new Vector3(c.x, 0f, c.y));
            colors.Add(color); colors.Add(color); colors.Add(color);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        }

        private static void AddDiamond(List<Vector3> vertices, List<Color> colors,
            List<int> triangles, Vector2 center, float radius, Color color) =>
            AddQuad(vertices, colors, triangles,
                center + new Vector2(-radius, 0f), center + new Vector2(0f, radius),
                center + new Vector2(radius, 0f), center + new Vector2(0f, -radius), color);

        private static void AddQuad(List<Vector3> vertices, List<Color> colors,
            List<int> triangles, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int start = vertices.Count;
            foreach (Vector2 point in new[] { a, b, c, d })
            {
                vertices.Add(new Vector3(point.x, 0f, point.y));
                colors.Add(color);
            }
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }

        private void EnsureMarkerMaterial()
        {
            if (markerMaterial != null) return;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return;
            markerMaterial = new Material(shader) { name = "World Map Terrain Markers" };
        }

        private void ApplyMarkerOpacity(float opacity)
        {
            bool visible = opacity > 0.001f;
            if (markerRoot != null) markerRoot.gameObject.SetActive(visible);
            foreach (TerrainMarkerInstance marker in terrainMarkers)
            {
                if (marker?.renderer == null) continue;
                var block = new MaterialPropertyBlock();
                marker.renderer.GetPropertyBlock(block);
                block.SetColor("_Color", new Color(1f, 1f, 1f, opacity));
                marker.renderer.SetPropertyBlock(block);
            }
        }

        private void ApplyContinuousLod(float visibleHexes)
        {
            ApplyMarkerOpacity(WorldMap3DPresentationPolicy.TerrainMarkerOpacity(visibleHexes));
            ApplyRootOpacity(structuralRoot,
                WorldMap3DPresentationPolicy.TerrainStructureOpacity(visibleHexes), false);
            ApplyRootOpacity(detailRoot,
                WorldMap3DPresentationPolicy.TerrainDetailOpacity(visibleHexes), true);
        }

        private static void ApplyRootOpacity(Transform root, float opacity, bool scaleChildren)
        {
            if (root == null) return;
            bool visible = opacity > 0.001f;
            root.gameObject.SetActive(visible);
            if (!visible) return;
            if (scaleChildren)
            {
                float scale = Mathf.SmoothStep(0.01f, 1f, opacity);
                for (int i = 0; i < root.childCount; i++)
                    root.GetChild(i).localScale = Vector3.one * scale;
            }
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                Color tint = new Color(1f, 1f, 1f, opacity);
                block.SetColor("_Color", tint);
                block.SetColor("_BaseColor", tint);
                renderer.SetPropertyBlock(block);
            }
        }

        private List<WorldCell> RegionCells(MapRegionData region)
        {
            var result = new List<WorldCell>(region.cellIndices.Count);
            foreach (int index in region.cellIndices)
            {
                if (index < 0 || index >= map.cells.Length || map.cells[index] == null) continue;
                result.Add(map.cells[index]);
            }
            result.Sort((left, right) => left.index.CompareTo(right.index));
            return result;
        }

        private void SpawnRegionLayer(MapRegionData region, List<WorldCell> cells, bool detail)
        {
            if (!detail && (region.regionType == MapRegionType.MountainRange ||
                            region.regionType == MapRegionType.Valley ||
                            region.regionType == MapRegionType.Hills ||
                            region.regionType == MapRegionType.SmallHill))
            {
                SpawnLandformStructure(region, cells);
                return;
            }

            int itemCount = RegionItemCount(region.regionType, cells.Count, detail);
            if (itemCount <= 0) return;
            WorldCell anchor = RegionAnchor(region, cells);
            if (anchor == null) return;

            string layerLabel = detail ? "Detail" : "Structural";
            var regionObject = new GameObject($"{layerLabel} Region {region.regionType} " +
                                              $"{region.regionId} Cells {cells.Count} Items {itemCount}");
            regionObject.transform.SetParent(detail ? detailRoot : structuralRoot, false);
            Vector2 anchorCenter = TerrainMeshGenerator.HexCenter(anchor.coord);
            float anchorHeight = TerrainRenderer.PresentationSurfaceHeightAt(map, anchorCenter, anchor);

            int created = 0;
            for (int item = 0; item < itemCount; item++)
            {
                uint placementHash = StableHash(map.effectiveSeed,
                    region.centerCellIndex >= 0 ? region.centerCellIndex : anchor.index,
                    1000 + item * 7 + (detail ? 1 : 0));
                WorldCell cell = cells[item % cells.Count];
                if (cells.Count > 1)
                    cell = cells[(item + (int)(placementHash % (uint)cells.Count)) % cells.Count];
                DecorationKind kind = RegionDecorationKind(region.regionType, cell, detail,
                    placementHash);
                GameObject prefab = PrefabFor(kind, placementHash);
                if (prefab == null) continue;

                float angle = (placementHash % 360u) * Mathf.Deg2Rad;
                float radius = Mathf.Lerp(0.10f, 0.82f,
                    ((placementHash >> 8) & 0xffffu) / 65535f);
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Vector2 center = TerrainMeshGenerator.HexCenter(cell.coord) + offset;
                float surfaceHeight = TerrainRenderer.PresentationSurfaceHeightAt(map, center, cell);
                uint scaleHash = StableHash(map.effectiveSeed, cell.index, 2000 + item * 11);

                GameObject part = Instantiate(prefab, regionObject.transform);
                part.name = kind + " " + item;
                part.transform.localPosition = new Vector3(center.x - anchorCenter.x,
                    surfaceHeight - anchorHeight, center.y - anchorCenter.y);
                part.transform.localRotation = Quaternion.Euler(0f,
                    (placementHash >> 16) % 360u, 0f);
                part.transform.localScale = Vector3.one * RegionScale(region.regionType,
                    kind, scaleHash);
                created++;
            }

            if (created == 0)
            {
                DestroyOwned(regionObject);
                return;
            }

            bool staticFoliage = region.regionType == MapRegionType.Forest || detail;
            Mesh combined = CombineChildren(regionObject,
                $"{layerLabel} Region Mesh {region.regionId}", staticFoliage);
            if (combined == null)
            {
                DestroyOwned(regionObject);
                return;
            }

            var entry = new DecorationInstance
            {
                transform = regionObject.transform,
                anchorCell = anchor,
                baseRotation = Quaternion.identity,
                ownedMesh = combined
            };
            instances.Add(entry);
            Position(entry);
        }

        /// <summary>
        /// Region 地貌由项目自有模块组合并合并为单一网格。基础地表保持平坦；
        /// 单格 RockFormation 只作为普通装饰，不参与山体结构。
        /// </summary>
        private void SpawnLandformStructure(MapRegionData region, List<WorldCell> cells)
        {
            WorldCell anchor = RegionAnchor(region, cells);
            if (anchor == null) return;

            bool hill = region.regionType == MapRegionType.Hills ||
                        region.regionType == MapRegionType.SmallHill;
            bool forest = region.regionType == MapRegionType.Forest;
            if (!hill && !forest && region.regionType != MapRegionType.MountainRange &&
                region.regionType != MapRegionType.Valley) return;
            // 原 Region Forest Canopy 是一组覆盖地表的浅色圆盘，既遮挡六边格，也不具备林海语义。
            // 森林只保留后续合并的缩小树簇，不再创建这层结构网格。
            // 丘陵只由基础地表的轻微格内起伏表达；额外的逐格 dome 会形成浅色圆片海。
            // 森林同样不再创建覆盖地表的 canopy 结构网格。
            if (forest || hill) return;
            List<WorldCell> pathCells = region.regionType == MapRegionType.Valley
                ? cells.Where(cell => cell.internalPositionTag == MapInternalPositionTag.ValleyFloor ||
                                      cell.internalPositionTag == MapInternalPositionTag.ValleyEntrance).ToList()
                : cells.Where(cell => cell.internalPositionTag == MapInternalPositionTag.Ridge ||
                                      cell.internalPositionTag == MapInternalPositionTag.Summit ||
                                      cell.internalPositionTag == MapInternalPositionTag.MountainPass).ToList();
            if (pathCells.Count < 2) pathCells = cells;

            List<WorldCell> connectedPath = hill || forest
                ? new List<WorldCell>() : BuildConnectedPath(pathCells);
            if (!hill && !forest && connectedPath.Count < 2) connectedPath = BuildConnectedPath(cells);
            if (!hill && !forest && connectedPath.Count < 2) return;

            string label = region.regionType == MapRegionType.MountainRange
                ? "Modular Mountain Range" : "Modular Valley Corridor";
            var regionObject = new GameObject($"Structural Region {region.regionType} {region.regionId} " +
                                              $"Cells {cells.Count} {label}");
            regionObject.transform.SetParent(structuralRoot, false);
            Vector2 anchorCenter = TerrainMeshGenerator.HexCenter(anchor.coord);
            float anchorHeight = TerrainRenderer.PresentationSurfaceHeightAt(map, anchorCenter, anchor);
            EnsureModularCliffAssets();
            int created = region.regionType == MapRegionType.MountainRange
                ? SpawnMountainSpine(regionObject.transform, connectedPath, cells, anchorCenter,
                    anchorHeight)
                : SpawnValleyWalls(regionObject.transform, connectedPath, anchorCenter, anchorHeight);
            Mesh mesh = created > 0
                ? CombineChildren(regionObject, label, false)
                : null;
            if (mesh == null)
            {
                DestroyOwned(regionObject);
                return;
            }
            MeshRenderer meshRenderer = regionObject.GetComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
            DisableWind(meshRenderer);

            var entry = new DecorationInstance
            {
                transform = regionObject.transform,
                anchorCell = anchor,
                baseRotation = Quaternion.identity,
                ownedMesh = mesh
            };
            instances.Add(entry);
            Position(entry);
        }

        private void SpawnHeightfieldDetails(MapRegionData region, IReadOnlyList<WorldCell> cells,
            WorldCell anchor, RegionTerrainHeightfieldMeshBuilder.BuildResult terrain, int seed)
        {
            if (terrain?.heightAt == null || terrain.slopeAt == null || terrain.contains == null ||
                cells == null || cells.Count == 0 || anchor == null) return;
            int target = Mathf.Clamp(cells.Count / 3, 5, 32);
            var points = new List<Vector2>(target);
            var kinds = new List<DecorationKind>(target);
            const float minimumDistance = 1.08f;
            int attempts = target * 36;
            for (int attempt = 0; attempt < attempts && points.Count < target; attempt++)
            {
                uint hash = StableHash(seed, cells[attempt % cells.Count].index, 7100 + attempt * 17);
                WorldCell cell = cells[(int)(hash % (uint)cells.Count)];
                float angle = Hash01(hash >> 5) * Mathf.PI * 2f;
                float radius = Mathf.Sqrt(Hash01(hash >> 13)) * 0.82f;
                Vector2 point = TerrainMeshGenerator.HexCenter(cell.coord) +
                                new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (!terrain.contains(point) || points.Any(existing =>
                        (existing - point).sqrMagnitude < minimumDistance * minimumDistance)) continue;
                float height = terrain.heightAt(point);
                float slope = terrain.slopeAt(point);
                bool valley = region.regionType == MapRegionType.Valley;
                bool rock = slope >= 0.34f || height >= 0.82f ||
                            (!valley && Hash01(hash >> 21) < 0.46f);
                if (!rock && slope > 0.30f) continue;
                if (!rock && height > 0.72f) continue;
                points.Add(point);
                kinds.Add(rock ? DecorationKind.Rock :
                    ((hash & 3u) == 0u ? DecorationKind.Birch : DecorationKind.LargeTree));
            }
            if (points.Count == 0) return;

            var root = new GameObject($"Detail Region {region.regionType} {region.regionId} " +
                                      $"Poisson Items {points.Count}");
            root.transform.SetParent(detailRoot, false);
            Vector2 anchorCenter = TerrainMeshGenerator.HexCenter(anchor.coord);
            for (int index = 0; index < points.Count; index++)
            {
                uint hash = StableHash(seed, anchor.index, 8100 + index * 23);
                GameObject prefab = PrefabFor(kinds[index], hash);
                if (prefab == null) continue;
                GameObject part = Instantiate(prefab, root.transform);
                part.name = kinds[index] + " " + index;
                float height = terrain.heightAt(points[index]);
                part.transform.localPosition = new Vector3(points[index].x - anchorCenter.x,
                    height, points[index].y - anchorCenter.y);
                part.transform.localRotation = Quaternion.Euler(0f, (hash >> 16) % 360u, 0f);
                part.transform.localScale = Vector3.one * RegionScale(region.regionType,
                    kinds[index], hash);
            }
            Mesh combined = CombineChildren(root, $"{region.regionType} Poisson Surface Details", true);
            if (combined == null)
            {
                DestroyOwned(root);
                return;
            }
            var entry = new DecorationInstance
            {
                transform = root.transform,
                anchorCell = anchor,
                baseRotation = Quaternion.identity,
                ownedMesh = combined
            };
            instances.Add(entry);
            Position(entry);
        }

        private int SpawnMountainSpine(Transform parent, IReadOnlyList<WorldCell> path,
            IReadOnlyList<WorldCell> graphCells, Vector2 anchorCenter, float anchorHeight)
        {
            int created = 0;
            for (int point = 0; point < path.Count; point++)
            {
                WorldCell cell = path[point];
                Vector2 center = TerrainMeshGenerator.HexCenter(cell.coord);
                Vector2 tangent = CellPathTangent(path, point);
                int degree = ConnectedDegree(cell, graphCells);
                string role;
                string module;
                if (cell.internalPositionTag == MapInternalPositionTag.Summit)
                {
                    role = "Peak";
                    module = "Clifftile Convex";
                }
                else if (cell.internalPositionTag == MapInternalPositionTag.MountainPass)
                {
                    role = "Pass";
                    module = (point & 1) == 0 ? "Clifftile Pass 1" : "Clifftile Pass 2";
                }
                else if (degree >= 3)
                {
                    role = "Junction";
                    module = "Clifftile Convex";
                }
                else if (IsTurn(path, point))
                {
                    role = "Turn";
                    module = (StableHash(map.effectiveSeed, cell.index, 4291) & 1u) == 0u
                        ? "Clifftile Concave" : "Clifftile Diagonal";
                }
                else
                {
                    role = "Straight";
                    module = $"Clifftile Straight {1 + point % 3}";
                }
                uint hash = StableHash(map.effectiveSeed, cell.index, 4300 + point * 13);
                float length = Mathf.Lerp(2.00f, 2.24f, Hash01(hash));
                float height = role == "Peak" ? 4.20f : role == "Pass" ? 1.75f :
                    role == "Junction" ? 3.45f : Mathf.Lerp(2.75f, 3.25f, Hash01(hash >> 8));
                created += SpawnModularCliffPart(parent, module, "Mountain " + role, center,
                    tangent, cell, anchorCenter, anchorHeight, length, height, false, 0f);
            }
            return created;
        }

        private int SpawnValleyWalls(Transform parent, IReadOnlyList<WorldCell> path,
            Vector2 anchorCenter, float anchorHeight)
        {
            int created = 0;
            for (int point = 0; point < path.Count; point++)
            {
                WorldCell cell = path[point];
                Vector2 pathCenter = TerrainMeshGenerator.HexCenter(cell.coord);
                Vector2 tangent = CellPathTangent(path, point);
                Vector2 lateral = new Vector2(-tangent.y, tangent.x);
                bool entrance = cell.internalPositionTag == MapInternalPositionTag.ValleyEntrance;
                float edgeOffset = entrance ? 1.18f : 0.96f;
                for (int side = -1; side <= 1; side += 2)
                {
                    uint hash = StableHash(map.effectiveSeed, cell.index, 5200 + point * 13 + side * 3);
                    Vector2 position = pathCenter + lateral * edgeOffset * side +
                                       tangent * HashSigned(hash >> 12) * 0.12f;
                    string module = entrance ? "Clifftile Pass 2" : IsTurn(path, point)
                        ? (side < 0 ? "Clifftile Concave" : "Clifftile Convex")
                        : $"Clifftile Straight {1 + (point + (side > 0 ? 1 : 0)) % 3}";
                    float length = Mathf.Lerp(2.00f, 2.18f, Hash01(hash));
                    float height = entrance ? 1.55f : Mathf.Lerp(2.25f, 2.85f, Hash01(hash >> 8));
                    created += SpawnModularCliffPart(parent, module, "Valley Wall", position,
                        tangent, cell, anchorCenter, anchorHeight, length, height, false,
                        side < 0 ? 180f : 0f);
                }
            }
            return created;
        }

        private int SpawnModularCliffPart(Transform parent, string moduleName, string role,
            Vector2 center, Vector2 tangent, WorldCell cell, Vector2 anchorCenter, float anchorHeight,
            float targetLength, float targetHeight, bool mirror, float yawOffset)
        {
            GameObject prefab = ModularCliffPrefab(moduleName);
            if (prefab == null) return 0;
            float surfaceHeight = TerrainRenderer.PresentationSurfaceHeightAt(map, center, cell);
            GameObject part = Instantiate(prefab, parent);
            part.name = role;
            part.transform.localPosition = Vector3.zero;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = Vector3.one;
            Bounds bounds = HierarchyBoundsInLocalSpace(part.transform);
            if (bounds.size.y < 0.001f || Mathf.Max(bounds.size.x, bounds.size.z) < 0.001f)
            {
                DestroyOwned(part);
                return 0;
            }
            bool longAxisIsX = bounds.size.x > bounds.size.z;
            float horizontalScale = targetLength / Mathf.Max(bounds.size.x, bounds.size.z);
            float verticalScale = targetHeight / bounds.size.y;
            part.transform.localScale = new Vector3(mirror ? -horizontalScale : horizontalScale,
                verticalScale, horizontalScale);
            float yaw = Mathf.Atan2(tangent.x, tangent.y) * Mathf.Rad2Deg +
                        (longAxisIsX ? 90f : 0f) + yawOffset;
            part.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            part.transform.localPosition = new Vector3(center.x - anchorCenter.x,
                surfaceHeight - anchorHeight - bounds.min.y * verticalScale,
                center.y - anchorCenter.y);
            foreach (Renderer childRenderer in part.GetComponentsInChildren<Renderer>(true))
                childRenderer.sharedMaterial = modularCliffMaterial;
            return 1;
        }

        private void EnsureModularCliffAssets()
        {
            if (modularCliffMaterial != null) return;
            Shader shader = Shader.Find("Cultivation4X/Map/Static Cliff Module");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return;
            modularCliffMaterial = new Material(shader) { name = "Broken Vector Cliff Static" };
            Texture2D palette = Resources.Load<Texture2D>(
                "BrokenVectorCliffs/Textures/Colorscheme Grey");
            if (palette != null) modularCliffMaterial.mainTexture = palette;
        }

        private GameObject ModularCliffPrefab(string moduleName)
        {
            if (string.IsNullOrEmpty(moduleName)) return null;
            if (modularCliffPrefabs.TryGetValue(moduleName, out GameObject cached)) return cached;
            GameObject loaded = Resources.Load<GameObject>(
                "BrokenVectorCliffs/Models/" + moduleName);
            modularCliffPrefabs[moduleName] = loaded;
            return loaded;
        }

        private static Bounds HierarchyBoundsInLocalSpace(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
            Matrix4x4 toLocal = root.worldToLocalMatrix;
            bool initialized = false;
            Bounds result = default;
            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                Vector3 min = world.min;
                Vector3 max = world.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new Vector3((corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    point = toLocal.MultiplyPoint3x4(point);
                    if (!initialized)
                    {
                        result = new Bounds(point, Vector3.zero);
                        initialized = true;
                    }
                    else result.Encapsulate(point);
                }
            }
            return result;
        }

        /// <summary>按六边格连接图取最大连通分量的近似直径，作为 Region 主轴。</summary>
        private List<WorldCell> BuildConnectedPath(IReadOnlyList<WorldCell> candidates)
        {
            if (candidates == null || candidates.Count == 0) return new List<WorldCell>();
            var byIndex = candidates.Where(cell => cell != null)
                .GroupBy(cell => cell.index).ToDictionary(group => group.Key, group => group.First());
            if (byIndex.Count == 0) return new List<WorldCell>();

            var unvisited = new HashSet<int>(byIndex.Keys);
            List<int> largest = null;
            while (unvisited.Count > 0)
            {
                int seed = unvisited.Min();
                var component = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(seed);
                unvisited.Remove(seed);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    component.Add(current);
                    foreach (int neighbor in map.GetNeighborIndices(current))
                    {
                        if (!byIndex.ContainsKey(neighbor) || !unvisited.Remove(neighbor)) continue;
                        queue.Enqueue(neighbor);
                    }
                }
                if (largest == null || component.Count > largest.Count) largest = component;
            }
            if (largest == null || largest.Count == 0) return new List<WorldCell>();
            var allowed = new HashSet<int>(largest);
            int endA = Farthest(largest.Min(), allowed, null);
            int endB = Farthest(endA, allowed, null);
            Dictionary<int, int> boundaryDistance = BoundaryDistance(allowed);
            Dictionary<int, int> parents = InteriorWeightedPath(endA, endB, allowed,
                boundaryDistance);
            var indices = new List<int> { endB };
            while (indices[indices.Count - 1] != endA &&
                   parents.TryGetValue(indices[indices.Count - 1], out int parent))
                indices.Add(parent);
            indices.Reverse();
            return indices.Select(index => byIndex[index]).ToList();
        }

        /// <summary>
        /// Region distance field: distance zero is the outline; larger values are deeper inside.
        /// The ridge/valley axis then prefers high-clearance cells instead of tracing the outline.
        /// </summary>
        private Dictionary<int, int> BoundaryDistance(HashSet<int> allowed)
        {
            var distance = new Dictionary<int, int>();
            var queue = new Queue<int>();
            foreach (int index in allowed)
            {
                int[] neighbors = map.GetNeighborIndices(index).ToArray();
                if (neighbors.Length < 6 || neighbors.Any(neighbor => !allowed.Contains(neighbor)))
                {
                    distance[index] = 0;
                    queue.Enqueue(index);
                }
            }
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int neighbor in map.GetNeighborIndices(current))
                {
                    if (!allowed.Contains(neighbor) || distance.ContainsKey(neighbor)) continue;
                    distance[neighbor] = distance[current] + 1;
                    queue.Enqueue(neighbor);
                }
            }
            foreach (int index in allowed)
                if (!distance.ContainsKey(index)) distance[index] = 0;
            return distance;
        }

        private Dictionary<int, int> InteriorWeightedPath(int start, int goal,
            HashSet<int> allowed, Dictionary<int, int> boundaryDistance)
        {
            var costs = allowed.ToDictionary(index => index,
                index => index == start ? 0f : float.PositiveInfinity);
            var parents = new Dictionary<int, int>();
            var remaining = new HashSet<int>(allowed);
            while (remaining.Count > 0)
            {
                int current = remaining.OrderBy(index => costs[index]).ThenBy(index => index).First();
                if (float.IsPositiveInfinity(costs[current]) || current == goal) break;
                remaining.Remove(current);
                foreach (int neighbor in map.GetNeighborIndices(current))
                {
                    if (!remaining.Contains(neighbor)) continue;
                    int clearance = boundaryDistance.TryGetValue(neighbor, out int value) ? value : 0;
                    float candidate = costs[current] + 1f + 4f / (1f + clearance);
                    if (candidate >= costs[neighbor]) continue;
                    costs[neighbor] = candidate;
                    parents[neighbor] = current;
                }
            }
            return parents;
        }

        private int Farthest(int start, HashSet<int> allowed, Dictionary<int, int> parents)
        {
            var distance = new Dictionary<int, int> { [start] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            int farthest = start;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (distance[current] > distance[farthest] ||
                    distance[current] == distance[farthest] && current < farthest)
                    farthest = current;
                foreach (int neighbor in map.GetNeighborIndices(current))
                {
                    if (!allowed.Contains(neighbor) || distance.ContainsKey(neighbor)) continue;
                    distance[neighbor] = distance[current] + 1;
                    if (parents != null) parents[neighbor] = current;
                    queue.Enqueue(neighbor);
                }
            }
            return farthest;
        }

        private int ConnectedDegree(WorldCell cell, IReadOnlyList<WorldCell> graphCells)
        {
            if (cell == null || graphCells == null) return 0;
            var allowed = new HashSet<int>(graphCells.Select(item => item.index));
            return map.GetNeighborIndices(cell.index).Count(allowed.Contains);
        }

        private static Vector2 CellPathTangent(IReadOnlyList<WorldCell> path, int index)
        {
            Vector2 before = TerrainMeshGenerator.HexCenter(path[Mathf.Max(0, index - 1)].coord);
            Vector2 after = TerrainMeshGenerator.HexCenter(path[Mathf.Min(path.Count - 1, index + 1)].coord);
            Vector2 tangent = after - before;
            return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector2.up;
        }

        private static bool IsTurn(IReadOnlyList<WorldCell> path, int index)
        {
            if (index <= 0 || index >= path.Count - 1) return false;
            Vector2 previous = TerrainMeshGenerator.HexCenter(path[index].coord) -
                               TerrainMeshGenerator.HexCenter(path[index - 1].coord);
            Vector2 next = TerrainMeshGenerator.HexCenter(path[index + 1].coord) -
                           TerrainMeshGenerator.HexCenter(path[index].coord);
            return Vector2.Dot(previous.normalized, next.normalized) < 0.94f;
        }

        private GameObject RockPrefabForVariant(int variant)
        {
            switch (Mathf.Abs(variant) % 4)
            {
                case 1: return rockPrefab02 != null ? rockPrefab02 : rockPrefab;
                case 2: return rockPrefab03 != null ? rockPrefab03 : rockPrefab;
                case 3: return rockPrefab04 != null ? rockPrefab04 : rockPrefab;
                default: return rockPrefab;
            }
        }

        private static float Hash01(uint hash) => (hash & 0xffffu) / 65535f;
        private static float HashSigned(uint hash) => Hash01(hash) * 2f - 1f;

        private void EnsurePaintedMountainMaterials()
        {
            int count = paintedMountainTextures?.Count(texture => texture != null) ?? 0;
            if (count == 0 || paintedMountainMaterials?.Length == count) return;
            Shader shader = Shader.Find("Cultivation4X/Map/Painted Mountain");
            if (shader == null) return;
            paintedMountainMaterials = paintedMountainTextures.Where(texture => texture != null)
                .Select((texture, index) => new Material(shader)
                {
                    name = $"Painted Mountain Runtime {index}",
                    mainTexture = texture
                }).ToArray();
        }

        private void EnsureRegionTerrainMaterial()
        {
            if (regionTerrainMaterial != null) return;
            Shader shader = Shader.Find("Cultivation4X/Map/Region Terrain");
            if (shader == null)
            {
                EnsureMarkerMaterial();
                return;
            }
            regionTerrainMaterial = new Material(shader) { name = "Region Heightfield Terrain" };
            Texture grass = terrainRenderer != null && terrainRenderer.RegionGrassTexture != null
                ? terrainRenderer.RegionGrassTexture : Texture2D.whiteTexture;
            Texture dirt = terrainRenderer != null && terrainRenderer.RegionDirtTexture != null
                ? terrainRenderer.RegionDirtTexture : Texture2D.whiteTexture;
            Texture stone = terrainRenderer != null && terrainRenderer.RegionStoneTexture != null
                ? terrainRenderer.RegionStoneTexture : Texture2D.whiteTexture;
            regionTerrainMaterial.SetTexture("_GrassTex", grass);
            regionTerrainMaterial.SetTexture("_DirtTex", dirt);
            regionTerrainMaterial.SetTexture("_StoneTex", stone);
            regionTerrainMaterial.SetFloat("_WorldTiling", 0.54f);
        }

        private static int RegionItemCount(MapRegionType type, int cellCount, bool detail)
        {
            switch (type)
            {
                case MapRegionType.Forest:
                    return detail ? Mathf.Clamp(cellCount, 18, 96) : 0;
                case MapRegionType.MountainRange:
                    return 0;
                case MapRegionType.Hills:
                case MapRegionType.SmallHill:
                    return detail
                        ? Mathf.Clamp(cellCount / 2, 2, 14)
                        : 0;
                case MapRegionType.Valley:
                    return 0;
                case MapRegionType.Swamp:
                    return detail ? Mathf.Clamp(cellCount * 2, 8, 48) : 0;
                case MapRegionType.Desert:
                    return detail ? Mathf.Clamp(cellCount / 5, 2, 10) : 0;
                case MapRegionType.Plain:
                    return detail ? Mathf.Clamp(cellCount, 4, 28) : 0;
                default:
                    return 0;
            }
        }

        private static DecorationKind RegionDecorationKind(MapRegionType type, WorldCell cell,
            bool detail, uint hash)
        {
            if (detail)
            {
                switch (type)
                {
                    case MapRegionType.Forest:
                        return (hash & 3u) == 0u ? DecorationKind.Birch : DecorationKind.LargeTree;
                    case MapRegionType.MountainRange:
                        return DecorationKind.MountainKey;
                    case MapRegionType.Valley:
                        return DecorationKind.ValleyWall;
                    case MapRegionType.Hills:
                    case MapRegionType.SmallHill:
                        return (hash & 3u) == 0u ? DecorationKind.HillKey : DecorationKind.Grass;
                    case MapRegionType.Desert:
                        return DecorationKind.Rock;
                    default:
                        return (hash & 3u) == 0u ? DecorationKind.FlowerBush : DecorationKind.Grass;
                }
            }
            switch (type)
            {
                case MapRegionType.Forest:
                    return (hash & 3u) == 0u ? DecorationKind.Birch : DecorationKind.LargeTree;
                case MapRegionType.MountainRange:
                case MapRegionType.Desert:
                    return DecorationKind.Rock;
                case MapRegionType.Hills:
                case MapRegionType.SmallHill:
                    if ((hash % 10u) < 4u) return DecorationKind.Rock;
                    return (hash & 1u) == 0u ? DecorationKind.Birch : DecorationKind.LargeTree;
                case MapRegionType.Valley:
                    return DecorationKind.Rock;
                default:
                    return cell.biome == BiomeType.TemperateForest ||
                           cell.biome == BiomeType.Rainforest
                        ? DecorationKind.LargeTree
                        : DecorationKind.Rock;
            }
        }

        private static float RegionScale(MapRegionType type, DecorationKind kind, uint hash)
        {
            float t = (hash & 0xffffu) / 65535f;
            switch (kind)
            {
                case DecorationKind.LargeTree:
                    return type == MapRegionType.Forest
                        ? Mathf.Lerp(0.26f, 0.40f, t)
                        : Mathf.Lerp(0.52f, 0.76f, t);
                case DecorationKind.Birch:
                    return type == MapRegionType.Forest
                        ? Mathf.Lerp(0.24f, 0.37f, t)
                        : Mathf.Lerp(0.48f, 0.70f, t);
                case DecorationKind.Rock:
                    // 山体由连续地形承担；岩石仅作低矮点缀，不再充当竖直山峰。
                    return Mathf.Lerp(0.28f, 0.48f, t);
                case DecorationKind.FlowerBush:
                    return Mathf.Lerp(0.22f, 0.36f, t);
                case DecorationKind.MountainKey:
                    return Mathf.Lerp(0.62f, 0.86f, t);
                case DecorationKind.HillKey:
                    return Mathf.Lerp(0.58f, 0.78f, t);
                case DecorationKind.ValleyWall:
                    return Mathf.Lerp(0.54f, 0.76f, t);
                default:
                    return Mathf.Lerp(0.24f, 0.40f, t);
            }
        }

        private WorldCell RegionAnchor(MapRegionData region, List<WorldCell> cells)
        {
            if (region.centerCellIndex >= 0 && region.centerCellIndex < map.cells.Length &&
                map.cells[region.centerCellIndex] != null)
                return map.cells[region.centerCellIndex];
            return cells.Count > 0 ? cells[0] : null;
        }

        private GameObject PrefabFor(DecorationKind kind, uint hash)
        {
            switch (kind)
            {
                case DecorationKind.LargeTree: return largeTreePrefab;
                case DecorationKind.Birch: return birchPrefab;
                case DecorationKind.Rock: return rockPrefab;
                case DecorationKind.FlowerBush: return flowerBushPrefab;
                case DecorationKind.MountainKey:
                    return SelectVariant(hash, mountainKeyPrefabA, mountainKeyPrefabB,
                        mountainKeyPrefabC, rockPrefab);
                case DecorationKind.HillKey:
                    return SelectVariant(hash, hillKeyPrefabA, hillKeyPrefabB,
                        hillKeyPrefabC, rockPrefab02 != null ? rockPrefab02 : rockPrefab);
                case DecorationKind.ValleyWall:
                    return SelectVariant(hash, valleyWallPrefabA, valleyWallPrefabB,
                        rockPrefab03, rockPrefab);
                default: return grassPrefab;
            }
        }

        private static GameObject SelectVariant(uint hash, GameObject a, GameObject b,
            GameObject c, GameObject fallback)
        {
            GameObject selected = hash % 3u == 0u ? a : hash % 3u == 1u ? b : c;
            return selected != null ? selected : fallback;
        }

        private void RefreshHeights()
        {
            foreach (DecorationInstance entry in instances) Position(entry);
            foreach (TerrainMarkerInstance marker in terrainMarkers) Position(marker);
            lastCurveRevision = terrainRenderer != null ? terrainRenderer.CurveRevision : -1;
        }

        private void Position(TerrainMarkerInstance marker)
        {
            if (marker?.transform == null || marker.anchorCell == null || map == null) return;
            Vector2 center = TerrainMeshGenerator.HexCenter(marker.anchorCell.coord);
            float height = TerrainRenderer.PresentationSurfaceHeightAt(map, center, marker.anchorCell);
            Vector3 flatWorldPosition = transform.TransformPoint(new Vector3(center.x, height + 0.16f, center.y));
            if (terrainRenderer == null)
            {
                marker.transform.position = flatWorldPosition;
                marker.transform.rotation = transform.rotation;
                return;
            }
            marker.transform.position = terrainRenderer.CurveWorldPosition(flatWorldPosition);
            marker.transform.rotation = Quaternion.FromToRotation(Vector3.up,
                terrainRenderer.CurveWorldNormal(flatWorldPosition)) * transform.rotation;
        }

        private void Position(DecorationInstance entry)
        {
            if (entry?.transform == null || entry.anchorCell == null || map == null) return;
            Vector2 center = TerrainMeshGenerator.HexCenter(entry.anchorCell.coord);
            float height = TerrainRenderer.PresentationSurfaceHeightAt(map, center, entry.anchorCell);
            Vector3 flatLocalPosition = new Vector3(center.x, height + 0.02f, center.y);
            Vector3 flatWorldPosition = transform.TransformPoint(flatLocalPosition);
            if (terrainRenderer == null)
            {
                entry.transform.position = flatWorldPosition;
                entry.transform.rotation = transform.rotation * entry.baseRotation;
                return;
            }
            entry.transform.position = terrainRenderer.CurveWorldPosition(flatWorldPosition);
            entry.transform.rotation = Quaternion.FromToRotation(Vector3.up,
                terrainRenderer.CurveWorldNormal(flatWorldPosition)) *
                (transform.rotation * entry.baseRotation);
        }

        private void EnsureRoots()
        {
            if (structuralRoot == null)
            {
                var root = new GameObject("Structural Region Decorations");
                root.transform.SetParent(transform, false);
                structuralRoot = root.transform;
            }
            if (detailRoot == null)
            {
                var root = new GameObject("Near Region Details");
                root.transform.SetParent(transform, false);
                detailRoot = root.transform;
            }
            if (markerRoot == null)
            {
                var root = new GameObject("Far Terrain Markers");
                root.transform.SetParent(transform, false);
                markerRoot = root.transform;
            }
        }

        private static Mesh CombineChildren(GameObject root, string meshName, bool staticFoliage)
        {
            var selectedRenderers = SelectStrategicRenderers(root);
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            Matrix4x4 rootInverse = root.transform.worldToLocalMatrix;
            foreach (Renderer renderer in selectedRenderers)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;
                Material[] materials = renderer.sharedMaterials;
                int subMeshCount = Mathf.Min(mesh.subMeshCount, materials.Length);
                for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                {
                    Material material = materials[subMesh];
                    if (material == null) continue;
                    if (!byMaterial.TryGetValue(material, out List<CombineInstance> combines))
                    {
                        combines = new List<CombineInstance>();
                        byMaterial.Add(material, combines);
                    }
                    combines.Add(new CombineInstance
                    {
                        mesh = mesh,
                        subMeshIndex = subMesh,
                        transform = rootInverse * filter.transform.localToWorldMatrix
                    });
                }
            }
            if (byMaterial.Count == 0) return null;

            var materialsOut = new List<Material>();
            var materialMeshes = new List<Mesh>();
            var finalCombines = new List<CombineInstance>();
            foreach (KeyValuePair<Material, List<CombineInstance>> pair in byMaterial)
            {
                var materialMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                materialMesh.CombineMeshes(pair.Value.ToArray(), true, true);
                materialMeshes.Add(materialMesh);
                materialsOut.Add(pair.Key);
                finalCombines.Add(new CombineInstance
                {
                    mesh = materialMesh,
                    subMeshIndex = 0,
                    transform = Matrix4x4.identity
                });
            }

            var combined = new Mesh { name = meshName, indexFormat = IndexFormat.UInt32 };
            combined.CombineMeshes(finalCombines.ToArray(), false, false);
            combined.RecalculateBounds();
            foreach (Mesh temporary in materialMeshes) DestroyOwned(temporary);

            var children = new List<GameObject>();
            for (int index = 0; index < root.transform.childCount; index++)
                children.Add(root.transform.GetChild(index).gameObject);
            foreach (GameObject child in children) DestroyOwned(child);
            MeshFilter rootFilter = root.AddComponent<MeshFilter>();
            MeshRenderer rootRenderer = root.AddComponent<MeshRenderer>();
            rootFilter.sharedMesh = combined;
            rootRenderer.sharedMaterials = materialsOut.ToArray();
            rootRenderer.shadowCastingMode = ShadowCastingMode.On;
            rootRenderer.receiveShadows = true;
            if (staticFoliage) DisableWind(rootRenderer);
            return combined;
        }

        private static HashSet<Renderer> SelectStrategicRenderers(GameObject root)
        {
            var selected = new HashSet<Renderer>();
            var lodControlled = new HashSet<Renderer>();
            foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = group.GetLODs();
                foreach (LOD lod in lods)
                    foreach (Renderer renderer in lod.renderers)
                        if (renderer != null) lodControlled.Add(renderer);
                // Dreamscape 的低级 LOD 都以简化树冠为主，俯视战略地图中仍会变成
                // 一块块浅色圆片。Region 最终会合并为单个 mesh，因此直接取第一个
                // 非空实体 LOD，保留树干和树冠轮廓而不增加 GameObject 数量。
                for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
                {
                    Renderer[] renderers = lods[lodIndex].renderers;
                    if (renderers == null || renderers.Length == 0) continue;
                    foreach (Renderer renderer in renderers)
                        if (renderer != null) selected.Add(renderer);
                    break;
                }
            }
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                if (!lodControlled.Contains(renderer)) selected.Add(renderer);
            return selected;
        }

        private static void DisableWind(Renderer renderer)
        {
            if (renderer == null) return;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetFloat("_UseGlobalWindSettings", 0f);
            block.SetFloat("_UsesGlobalWindSettings", 0f);
            block.SetFloat("_WindIntensity", 0f);
            block.SetFloat("_WIndSwayIntensity", 0f);
            block.SetFloat("_WIndSwayFrequency", 0f);
            block.SetFloat("_WindJitterSpeed", 0f);
            block.SetFloat("_WindOffsetIntensity", 0f);
            block.SetFloat("_WindRustleSize", 0f);
            block.SetFloat("_WindScrollSpeed", 0f);
            block.SetFloat("_WindSpeed", 0f);
            block.SetVector("_Speed", Vector4.zero);
            renderer.SetPropertyBlock(block);
        }

        private static uint StableHash(int seed, int cellIndex, int salt)
        {
            unchecked
            {
                uint value = (uint)seed;
                value ^= (uint)cellIndex * 0x9E3779B9u;
                value ^= (uint)salt * 0x85EBCA6Bu;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return value;
            }
        }

        private void OnDestroy()
        {
            Clear();
            if (markerMaterial != null) DestroyOwned(markerMaterial);
            if (regionTerrainMaterial != null) DestroyOwned(regionTerrainMaterial);
            if (modularCliffMaterial != null) DestroyOwned(modularCliffMaterial);
            if (paintedMountainMaterials != null)
                foreach (Material material in paintedMountainMaterials)
                    if (material != null) DestroyOwned(material);
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
