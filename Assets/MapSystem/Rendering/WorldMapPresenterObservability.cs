using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cultivation4X.WorldMap
{
    public partial class WorldMapPresenter
    {
        private const float PresentationHexRadius = 0.96f;
        private readonly Dictionary<string, Transform> layerRoots = new Dictionary<string, Transform>();
        private readonly List<WorldMapPresentationMarker> externalPresentationMarkers =
            new List<WorldMapPresentationMarker>();
        private readonly List<GameObject> observabilityPages = new List<GameObject>();
        private List<WorldMapPresentationMarker> presentationMarkers = new List<WorldMapPresentationMarker>();
        private WorldMapViewMode viewMode = WorldMapViewMode.Landform;
        private GameObject observabilityRoot;
        private Button observabilityToggle;
        private bool debugViewEnabled;
        private TMP_Text legendText;
        private TMP_Text statisticsText;
        private TMP_Text parametersText;
        private WorldMapIconDensityTier lastDensityTier = WorldMapIconDensityTier.Hidden;
        private WorldMapZoomLevel lastZoomLevel = WorldMapZoomLevel.Far;
        private float lastPresentationOrthographicSize = -1f;
        private RectTransform regionLabelRoot;
        private RectTransform nearDetailLabelRoot;
        private readonly List<TMP_Text> regionLabelPool = new List<TMP_Text>();
        private readonly List<TMP_Text> nearDetailLabelPool = new List<TMP_Text>();
        private TMP_Text labelMeasure;
        private readonly Dictionary<string, MapRegionData> presentationRegionById =
            new Dictionary<string, MapRegionData>();
        private readonly Dictionary<int, CellInfluenceState> presentationInfluenceByCell =
            new Dictionary<int, CellInfluenceState>();
        private readonly HashSet<int> presentationKnownCellIndices = new HashSet<int>();
        private readonly HashSet<string> presentationKnownRegionIds = new HashSet<string>();

        public WorldMapViewMode ViewMode => viewMode;
        public int SelectedCellIndex => selectedCellIndex;

        public void SetViewMode(WorldMapViewMode mode)
        {
            bool selecting = PlayerManager.Instance?.playerData?.founding?.stage == FoundingStage.WorldSelection;
            if (selecting && mode != WorldMapViewMode.Landform) return;
            if (!debugViewEnabled && mode != WorldMapViewMode.Landform) return;
            if (viewMode == mode) return;
            viewMode = mode;
            Rebuild();
            RefreshLegend();
            RefreshDetails();
        }

        public void SetPresentationMarkers(IEnumerable<WorldMapPresentationMarker> markers)
        {
            externalPresentationMarkers.Clear();
            if (markers != null)
                externalPresentationMarkers.AddRange(markers
                    .Where(marker => marker != null)
                    .Select(CloneMarker));
            RefreshPresentationMarkers();
            Rebuild();
            RefreshDetails();
        }

        private static WorldMapPresentationMarker CloneMarker(WorldMapPresentationMarker marker) =>
            new WorldMapPresentationMarker
            {
                id = marker.id,
                label = marker.label,
                kind = marker.kind,
                environmentHintKind = marker.environmentHintKind,
                cellIndex = marker.cellIndex,
                isDemo = false
            };

        private void CreateLayerRoots()
        {
            foreach (string layerName in new[]
            {
                "Terrain", "Boundaries", "TerrainIcons", "Rivers", "SpiritVeins",
                "FactionMarkers", "LocationMarkers", "RegionAmbience", "Influence", "Selection"
            })
            {
                GameObject root = new GameObject(layerName);
                root.transform.SetParent(transform, false);
                layerRoots[layerName] = root.transform;
            }
        }

        private Transform Layer(string name) =>
            layerRoots.TryGetValue(name, out Transform root) ? root : transform;

        private void RefreshPresentationCaches()
        {
            presentationRegionById.Clear();
            presentationInfluenceByCell.Clear();
            presentationKnownCellIndices.Clear();
            presentationKnownRegionIds.Clear();
            if (map?.cells == null) return;

            foreach (MapRegionData region in map.regions ?? new List<MapRegionData>())
                if (region != null && !string.IsNullOrEmpty(region.regionId) &&
                    !presentationRegionById.ContainsKey(region.regionId))
                    presentationRegionById.Add(region.regionId, region);

            FoundingState founding = PlayerManager.Instance?.playerData?.founding;
            bool showAll = debugViewEnabled || founding == null || !FoundingRules.HasReachedCave(founding);
            WorldMapProgressState progress = WorldMapSession.Progress;
            if (!showAll) WorldMapInfluenceRules.EnsureCurrent(map, progress);
            foreach (CellInfluenceState influence in progress?.cellInfluences ?? new List<CellInfluenceState>())
            {
                if (influence == null || influence.cellIndex < 0 || influence.cellIndex >= map.cells.Length ||
                    influence.value <= 0 || influence.value > 100 ||
                    influence.level != WorldMapInfluenceRules.LevelForValue(influence.value) ||
                    string.IsNullOrWhiteSpace(influence.controllerSectId) ||
                    influence.sourceIds == null || influence.sourceIds.Count == 0 ||
                    presentationInfluenceByCell.ContainsKey(influence.cellIndex)) continue;
                presentationInfluenceByCell.Add(influence.cellIndex, influence);
            }

            if (showAll)
            {
                foreach (WorldCell cell in map.cells) presentationKnownCellIndices.Add(cell.index);
            }
            else
            {
                foreach (int index in progress?.revealedCellIndices ?? new List<int>())
                    if (index >= 0 && index < map.cells.Length) presentationKnownCellIndices.Add(index);
                foreach (int index in presentationInfluenceByCell.Keys)
                    presentationKnownCellIndices.Add(index);
            }
            foreach (int index in presentationKnownCellIndices)
            {
                string regionId = map.cells[index].regionId;
                if (!string.IsNullOrEmpty(regionId)) presentationKnownRegionIds.Add(regionId);
            }
        }

        private void RefreshPresentationMarkers()
        {
            presentationMarkers = externalPresentationMarkers.Select(CloneMarker).ToList();
            WorldMapContentRules.EnsureCandidates(map, WorldMapSession.Progress);
            List<WorldMapPresentationMarker> contentMarkers =
                WorldMapPresentationMarkerFactory.CreateContentMarkers(map, WorldMapSession.Progress);
            presentationMarkers.AddRange(contentMarkers);
            presentationMarkers.AddRange(WorldMapPresentationMarkerFactory.CreateEnvironmentHintMarkers(
                map, WorldMapSession.Progress));
            HashSet<int> contentCells = new HashSet<int>(contentMarkers.Select(item => item.cellIndex));
            presentationMarkers.AddRange(WorldMapPresentationMarkerFactory.CreatePointOfInterestMarkers(map)
                .Where(item => !contentCells.Contains(item.cellIndex)));

            int caveIndex = PlayerManager.Instance?.playerData?.founding?.selectedWorldCellIndex ?? -1;
            if (map?.cells != null && caveIndex >= 0 && caveIndex < map.cells.Length)
            {
                MapSiteData sectBase = WorldMapProgressRules.GetSectBase(WorldMapSession.Progress);
                presentationMarkers.Add(new WorldMapPresentationMarker
                {
                    id = sectBase?.siteId ?? "player_cave",
                    label = sectBase?.siteName ?? "待建洞府",
                    kind = sectBase == null ? WorldMapMarkerKind.Cave : WorldMapMarkerKind.FactionSeat,
                    cellIndex = caveIndex
                });
            }
        }

        private void BuildBoundaries()
        {
            WorldMapGeometryBuffer buffer = new WorldMapGeometryBuffer();
            foreach (WorldCell cell in map.cells)
            {
                bool cellKnown = CanShowGameplayCell(cell.index);
                for (int direction = 0; direction < 6; direction++)
                {
                    int neighborIndex = map.GetIndex(map.GetNeighbor(cell.coord, direction));
                    if (neighborIndex <= cell.index) continue;
                    bool neighborKnown = CanShowGameplayCell(neighborIndex);
                    WorldCell neighbor = map.cells[neighborIndex];
                    if (cell.regionId != neighbor.regionId &&
                        WorldMapRegionPresentationPolicy.ShowRegionBoundary(lastZoomLevel, cellKnown, neighborKnown))
                    {
                        Vector2 regionCenter = Center(cell.coord);
                        float regionFirst = Mathf.Deg2Rad * (60f * direction - 30f);
                        float regionSecond = Mathf.Deg2Rad * (60f * (direction + 1) - 30f);
                        float regionWidth = lastZoomLevel == WorldMapZoomLevel.Far ? 0.10f :
                            lastZoomLevel == WorldMapZoomLevel.Mid ? 0.065f : 0.045f;
                        buffer.AddLine(regionCenter + new Vector2(Mathf.Cos(regionFirst), Mathf.Sin(regionFirst)) * PresentationHexRadius,
                            regionCenter + new Vector2(Mathf.Cos(regionSecond), Mathf.Sin(regionSecond)) * PresentationHexRadius,
                            regionWidth, new Color(0.88f, 0.82f, 0.62f,
                                lastZoomLevel == WorldMapZoomLevel.Far ? 0.58f : 0.38f));
                    }
                    if (!cellKnown || !neighborKnown) continue;
                    bool cellWater = IsWater(cell.landform);
                    bool neighborWater = IsWater(neighbor.landform);
                    bool coastBoundary = cellWater != neighborWater;
                    bool landformBoundary = viewMode == WorldMapViewMode.Landform &&
                                             !cellWater && !neighborWater &&
                                             BoundaryClass(cell.landform) != BoundaryClass(neighbor.landform);
                    bool biomeBoundary = viewMode == WorldMapViewMode.Biome &&
                                         cell.biome != neighbor.biome;
                    if (!coastBoundary && !landformBoundary && !biomeBoundary) continue;

                    Vector2 center = Center(cell.coord);
                    float firstAngle = Mathf.Deg2Rad * (60f * direction - 30f);
                    float secondAngle = Mathf.Deg2Rad * (60f * (direction + 1) - 30f);
                    Color color = coastBoundary
                        ? new Color(0.92f, 0.88f, 0.68f, 0.85f)
                        : landformBoundary
                            ? new Color(0.08f, 0.08f, 0.07f, 0.46f)
                            : new Color(0.48f, 0.40f, 0.64f, 0.42f);
                    float width = coastBoundary ? 0.09f : landformBoundary ? 0.052f : 0.038f;
                    buffer.AddLine(
                        center + new Vector2(Mathf.Cos(firstAngle), Mathf.Sin(firstAngle)) * PresentationHexRadius,
                        center + new Vector2(Mathf.Cos(secondAngle), Mathf.Sin(secondAngle)) * PresentationHexRadius,
                        width, color);
                }
            }
            AddMeshObject("LandformBoundaries", buffer, Layer("Boundaries"), 1);
        }

        private static bool IsWater(LandformType landform) =>
            landform == LandformType.DeepWater || landform == LandformType.ShallowWater;

        private static int BoundaryClass(LandformType landform)
        {
            switch (landform)
            {
                case LandformType.Plain:
                case LandformType.Coast: return 1;
                case LandformType.Hill: return 2;
                case LandformType.Mountain: return 3;
                default: return 0;
            }
        }

        private void BuildPresentationLayers()
        {
            if (mapCamera == null || map?.cells == null) return;
            float projectedDiameter = ProjectedHexDiameter();
            lastDensityTier = WorldMapPresentationPolicy.GetDensityTier(projectedDiameter);
            lastZoomLevel = WorldMapRegionPresentationPolicy.GetZoomLevel(projectedDiameter);
            lastPresentationOrthographicSize = mapCamera.orthographicSize;

            WorldMapGeometryBuffer terrain = new WorldMapGeometryBuffer();
            foreach (WorldMapTerrainIconPlacement placement in
                     WorldMapPresentationPolicy.BuildTerrainIconPlacements(
                         map, viewMode, projectedDiameter, presentationMarkers.Where(marker =>
                             WorldMapRegionPresentationPolicy.ShowMarker(marker.kind, lastZoomLevel))))
            {
                bool known = CanShowGameplayCell(placement.cellIndex);
                // 未知区域在全图缩放时保留低密度、低透明度的粗略地形符号，
                // 但其它缩放层级仍遵守认知遮蔽。
                if (!known && lastDensityTier != WorldMapIconDensityTier.Hidden) continue;
                Color iconColor = TerrainIconColor(placement.kind);
                if (!known) iconColor.a *= 0.28f;
                WorldMapIconGeometry.AddTerrainIcon(terrain, placement.kind,
                    Center(map.cells[placement.cellIndex].coord), 0.72f, iconColor);
            }
            AddMeshObject("TerrainIcons", terrain, Layer("TerrainIcons"), 2);

            float pixelsPerWorldUnit = Mathf.Max(0.001f, Screen.height / (2f * mapCamera.orthographicSize));
            float markerSize = Mathf.Clamp(25f / pixelsPerWorldUnit, 0.65f, 2.35f);
            float alpha = WorldMapPresentationPolicy.MarkerAlpha(viewMode);
            WorldMapGeometryBuffer factions = new WorldMapGeometryBuffer();
            WorldMapGeometryBuffer locations = new WorldMapGeometryBuffer();
            WorldMapGeometryBuffer confirmedLocations = new WorldMapGeometryBuffer();
            WorldMapGeometryBuffer contentHints = new WorldMapGeometryBuffer();
            WorldMapGeometryBuffer environmentHints = new WorldMapGeometryBuffer();
            WorldMapGeometryBuffer caves = new WorldMapGeometryBuffer();
            foreach (WorldMapPresentationMarker marker in presentationMarkers)
            {
                if (marker.cellIndex < 0 || marker.cellIndex >= map.cells.Length ||
                    (marker.kind != WorldMapMarkerKind.ContentHint && !CanShowGameplayCell(marker.cellIndex)) ||
                    !WorldMapRegionPresentationPolicy.ShowMarker(marker.kind, lastZoomLevel) ||
                    !WorldMapPresentationPolicy.MarkerVisible(marker, viewMode, lastDensityTier, map.effectiveSeed))
                    continue;
                WorldMapGeometryBuffer target;
                switch (marker.kind)
                {
                    case WorldMapMarkerKind.FactionSeat: target = factions; break;
                    case WorldMapMarkerKind.Cave: target = caves; break;
                    case WorldMapMarkerKind.EnvironmentHint:
                    case WorldMapMarkerKind.EnvironmentMoisture:
                    case WorldMapMarkerKind.EnvironmentMineralVein:
                    case WorldMapMarkerKind.EnvironmentBeastTracks:
                    case WorldMapMarkerKind.EnvironmentRuinedWalls:
                    case WorldMapMarkerKind.EnvironmentSettlementSigns:
                    case WorldMapMarkerKind.EnvironmentCaveSigns: target = environmentHints; break;
                    case WorldMapMarkerKind.ContentHint: target = contentHints; break;
                    case WorldMapMarkerKind.Village:
                    case WorldMapMarkerKind.SpiritSpring:
                    case WorldMapMarkerKind.SpiritMine:
                    case WorldMapMarkerKind.CaveResidence:
                    case WorldMapMarkerKind.BeastLair:
                    case WorldMapMarkerKind.Ruin:
                    case WorldMapMarkerKind.HerbZonePlanned:
                    case WorldMapMarkerKind.HerbZoneDeveloping:
                    case WorldMapMarkerKind.HerbZoneOperational: target = confirmedLocations; break;
                    default: target = locations; break;
                }
                Color color = MarkerColor(marker.kind);
                color.a *= alpha;
                WorldMapIconGeometry.AddMarkerIcon(target, marker.kind,
                    Center(map.cells[marker.cellIndex].coord), markerSize, color);
            }
            AddMeshObject("LocationMarkers", locations, Layer("LocationMarkers"), 6);
            AddMeshObject("EnvironmentHints", environmentHints, Layer("LocationMarkers"), 7);
            AddMeshObject("ContentHints", contentHints, Layer("LocationMarkers"), 8);
            // Keep the sect base above all confirmed/content markers.  Cave is
            // a regular location marker and must not cover the faction seat.
            AddMeshObject("ConfirmedLocations", confirmedLocations, Layer("LocationMarkers"), 9);
            AddMeshObject("CaveMarkers", caves, Layer("LocationMarkers"), 9);
            AddMeshObject("FactionMarkers", factions, Layer("FactionMarkers"), 10);
            if (lastZoomLevel == WorldMapZoomLevel.Near)
            {
                WorldMapGeometryBuffer ambience = new WorldMapGeometryBuffer();
                foreach (WorldCell cell in map.cells)
                {
                    presentationInfluenceByCell.TryGetValue(cell.index, out CellInfluenceState cached);
                    KnowledgeState knowledge = presentationKnownCellIndices.Contains(cell.index)
                        ? KnowledgeState.Known : KnowledgeState.Unknown;
                    if (!WorldMapRegionPresentationPolicy.ShowNearDetail(map.effectiveSeed, cell.index,
                            knowledge, cached?.level ?? InfluenceLevel.None)) continue;
                    WorldMapIconGeometry.AddRegionAmbientIcon(ambience, cell.internalPositionTag,
                        Center(cell.coord), 0.42f, new Color(0.82f, 0.90f, 0.72f, 0.42f));
                }
                AddMeshObject("RegionAmbience", ambience, Layer("RegionAmbience"), 4);
            }
        }

        private void BuildInfluenceOverlay()
        {
            PlayerData sect = PlayerManager.Instance?.playerData;
            FoundingState founding = sect?.founding;
            if (debugViewEnabled || !FoundingRules.HasReachedCave(founding) || map?.cells == null) return;
            WorldMapProgressState progress = WorldMapSession.Progress;
            WorldMapInfluenceRules.EnsureCurrent(map, progress);
            WorldMapGeometryBuffer buffer = new WorldMapGeometryBuffer();
            foreach (CellInfluenceState influence in progress?.cellInfluences ??
                     new List<CellInfluenceState>())
            {
                if (influence == null || influence.level == InfluenceLevel.None ||
                    influence.cellIndex < 0 || influence.cellIndex >= map.cells.Length) continue;
                // 影响力是建宗后的战略地图信息，不依赖该格是否已被探索；
                // 地点详情和行动权限继续通过统一格状态判定，不在表现层改写认知规则。
                if (!WorldMapInfluencePresentation.TryGetOverlayStyle(
                        influence.level, out Color color, out float width)) continue;
                WorldCell cell = map.cells[influence.cellIndex];
                Vector2 center = Center(cell.coord);
                Color fillColor = color;
                fillColor.a *= InfluenceFillAlpha(influence.level);
                for (int corner = 0; corner < 6; corner++)
                {
                    float a = Mathf.Deg2Rad * (corner * 60f - 30f);
                    float b = Mathf.Deg2Rad * ((corner + 1) * 60f - 30f);
                    buffer.AddTriangle(center,
                        center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.96f,
                        center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * 0.96f,
                        fillColor);
                }
                for (int corner = 0; corner < 6; corner++)
                {
                    float a = Mathf.Deg2Rad * (corner * 60f - 30f);
                    float b = Mathf.Deg2Rad * ((corner + 1) * 60f - 30f);
                    buffer.AddLine(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.92f,
                        center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * 0.92f, width, color);
                }
            }
            AddMeshObject("SectInfluence", buffer, Layer("Influence"), 5);
        }

        private static bool IsSelectablePlateau(WorldCell cell)
        {
            return cell != null && cell.landform == LandformType.Mountain && cell.isBuildable;
        }

        private void BuildSiteSelectionOverlay()
        {
            FoundingState founding = PlayerManager.Instance?.playerData?.founding;
            if (founding?.stage != FoundingStage.WorldSelection || map?.cells == null) return;
            WorldMapGeometryBuffer buffer = new WorldMapGeometryBuffer();
            Color fillColor = new Color(1f, 0.82f, 0.36f, 0.30f);
            Color outlineColor = new Color(1f, 0.88f, 0.50f, 0.95f);
            foreach (WorldCell cell in map.cells)
            {
                if (!IsSelectablePlateau(cell) || !CanShowGameplayCell(cell.index)) continue;
                Vector2 center = Center(cell.coord);
                for (int corner = 0; corner < 6; corner++)
                {
                    float a = Mathf.Deg2Rad * (corner * 60f - 30f);
                    float b = Mathf.Deg2Rad * ((corner + 1) * 60f - 30f);
                    buffer.AddTriangle(center,
                        center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.90f,
                        center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * 0.90f,
                        fillColor);
                }
                for (int corner = 0; corner < 6; corner++)
                {
                    float a = Mathf.Deg2Rad * (corner * 60f - 30f);
                    float b = Mathf.Deg2Rad * ((corner + 1) * 60f - 30f);
                    buffer.AddLine(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.88f,
                        center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * 0.88f, 0.08f, outlineColor);
                }
            }
            AddMeshObject("SiteSelectionPlateau", buffer, Layer("Selection"), 6);
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

        private void RefreshPresentationForZoom()
        {
            if (map == null || mapCamera == null) return;
            WorldMapIconDensityTier tier =
                WorldMapPresentationPolicy.GetDensityTier(ProjectedHexDiameter());
            WorldMapZoomLevel zoom =
                WorldMapRegionPresentationPolicy.GetZoomLevel(ProjectedHexDiameter());
            bool markerScaleChanged = lastPresentationOrthographicSize <= 0f ||
                                      Mathf.Abs(mapCamera.orthographicSize /
                                                lastPresentationOrthographicSize - 1f) >= 0.12f;
            if (tier != lastDensityTier || zoom != lastZoomLevel || markerScaleChanged) Rebuild();
            else RefreshRegionLabels();
        }

        private float ProjectedHexDiameter() =>
            mapCamera == null
                ? 0f
                : PresentationHexRadius * Screen.height / Mathf.Max(0.001f, mapCamera.orthographicSize);

        private static Color TerrainIconColor(WorldMapTerrainIconKind kind)
        {
            switch (kind)
            {
                case WorldMapTerrainIconKind.Water: return new Color(0.72f, 0.90f, 1f, 0.82f);
                case WorldMapTerrainIconKind.Plain: return new Color(0.10f, 0.22f, 0.08f, 0.72f);
                case WorldMapTerrainIconKind.Hill: return new Color(0.20f, 0.15f, 0.07f, 0.78f);
                case WorldMapTerrainIconKind.Mountain: return new Color(0.12f, 0.13f, 0.15f, 0.86f);
                case WorldMapTerrainIconKind.Forest: return new Color(0.04f, 0.20f, 0.08f, 0.80f);
                default: return new Color(0.96f, 0.99f, 1f, 0.94f);
            }
        }

        private static Color MarkerColor(WorldMapMarkerKind kind)
        {
            switch (kind)
            {
                case WorldMapMarkerKind.FactionSeat: return new Color(0.98f, 0.32f, 0.22f);
                case WorldMapMarkerKind.Village: return new Color(0.98f, 0.82f, 0.32f);
                case WorldMapMarkerKind.Cave: return new Color(1f, 0.72f, 0.08f);
                case WorldMapMarkerKind.CaveResidence: return new Color(0.95f, 0.56f, 0.20f);
                case WorldMapMarkerKind.ContentHint: return new Color(0.72f, 0.86f, 1f, 0.86f);
                case WorldMapMarkerKind.SpiritSpring: return new Color(0.20f, 0.84f, 1f);
                case WorldMapMarkerKind.SpiritMine: return new Color(0.68f, 0.78f, 0.96f);
                case WorldMapMarkerKind.BeastLair: return new Color(0.92f, 0.34f, 0.18f);
                case WorldMapMarkerKind.Ruin: return new Color(0.78f, 0.70f, 0.56f);
                case WorldMapMarkerKind.HerbZonePlanned: return new Color(0.46f, 0.72f, 0.43f, 0.70f);
                case WorldMapMarkerKind.HerbZoneDeveloping: return new Color(0.34f, 0.84f, 0.38f, 0.86f);
                case WorldMapMarkerKind.HerbZoneOperational: return new Color(0.22f, 0.94f, 0.34f, 1f);
                case WorldMapMarkerKind.EnvironmentHint: return new Color(0.72f, 0.90f, 0.78f, 0.58f);
                case WorldMapMarkerKind.EnvironmentMoisture: return new Color(0.24f, 0.88f, 0.96f, 0.72f);
                case WorldMapMarkerKind.EnvironmentMineralVein: return new Color(0.86f, 0.68f, 0.28f, 0.72f);
                case WorldMapMarkerKind.EnvironmentBeastTracks: return new Color(0.92f, 0.40f, 0.28f, 0.72f);
                case WorldMapMarkerKind.EnvironmentRuinedWalls: return new Color(0.70f, 0.64f, 0.72f, 0.72f);
                case WorldMapMarkerKind.EnvironmentSettlementSigns: return new Color(0.42f, 0.84f, 0.46f, 0.72f);
                case WorldMapMarkerKind.EnvironmentCaveSigns: return new Color(0.70f, 0.48f, 0.92f, 0.72f);
                default: return new Color(0.38f, 0.88f, 1f);
            }
        }

        private void AddMeshObject(string name, WorldMapGeometryBuffer buffer, Transform parent, int order)
        {
            if (buffer == null || buffer.triangles.Count == 0) return;
            Mesh mesh = buffer.CreateMesh(name + "Mesh");
            GameObject obj = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            generatedObjects.Add(obj);
        }

        private Color CellColor(WorldCell cell)
        {
            Color color;
            if (!CanShowGameplayCell(cell.index))
                // 未认知格显示压暗的地形色，只保留大概的地形轮廓。
            {
                Color hidden = Color.Lerp(LandformColor(cell.landform),
                    new Color(0.03f, 0.035f, 0.04f), 0.6f);
                color = lastZoomLevel == WorldMapZoomLevel.Far
                    ? Color.Lerp(hidden, RegionColor(cell.regionId), 0.12f) : hidden;
                return ApplyCellVariation(color, cell, 0.02f);
            }
            if (lastZoomLevel == WorldMapZoomLevel.Far && viewMode == WorldMapViewMode.Landform)
                color = Color.Lerp(LandformFillColor(cell), RegionColor(cell.regionId), 0.12f);
            else
            {
                switch (viewMode)
                {
                    case WorldMapViewMode.Landform: color = LandformFillColor(cell); break;
                    case WorldMapViewMode.Height:
                        color = Color.Lerp(Color.black, Color.white, Mathf.Clamp01(cell.height)); break;
                    case WorldMapViewMode.Temperature: color = TemperatureColor(cell.temperature); break;
                    case WorldMapViewMode.Moisture: color = MoistureColor(cell.moisture); break;
                    case WorldMapViewMode.Biome: color = TerrainColor(cell); break;
                    case WorldMapViewMode.AuraConcentration: color = AuraColor(cell.totalAura); break;
                    case WorldMapViewMode.DominantElement: color = DominantElementColor(cell); break;
                    default: color = new Color(0.12f, 0.13f, 0.14f); break;
                }
            }
            return ApplyCellVariation(color, cell, 0.07f);
        }

        private Color ApplyCellVariation(Color color, WorldCell cell, float strength)
        {
            if (cell == null || strength <= 0f) return color;
            int variation = SeedDerivation.Derive(map?.effectiveSeed ?? 0, "cell-tint-" + cell.index) & 0xff;
            float delta = (variation / 255f - 0.5f) * 2f * strength;
            return delta >= 0f
                ? Color.Lerp(color, Color.white, delta)
                : Color.Lerp(color, Color.black, -delta);
        }

        private static Color LandformColor(LandformType landform)
        {
            switch (landform)
            {
                case LandformType.DeepWater: return new Color(0.04f, 0.15f, 0.32f);
                case LandformType.ShallowWater: return new Color(0.08f, 0.38f, 0.60f);
                case LandformType.Coast: return new Color(0.82f, 0.73f, 0.43f);
                case LandformType.Plain: return new Color(0.40f, 0.66f, 0.28f);
                case LandformType.Hill: return new Color(0.48f, 0.44f, 0.24f);
                default: return new Color(0.48f, 0.49f, 0.51f);
            }
        }

        private Color RegionColor(string regionId)
        {
            presentationRegionById.TryGetValue(regionId ?? string.Empty, out MapRegionData region);
            if (region == null) return new Color(0.32f, 0.36f, 0.34f);
            switch (region.regionType)
            {
                case MapRegionType.MountainRange: return new Color(0.52f, 0.54f, 0.62f);
                case MapRegionType.SmallHill:
                case MapRegionType.Hills: return new Color(0.50f, 0.46f, 0.31f);
                case MapRegionType.Forest: return new Color(0.16f, 0.42f, 0.22f);
                case MapRegionType.Valley: return new Color(0.42f, 0.58f, 0.30f);
                case MapRegionType.Desert: return new Color(0.72f, 0.58f, 0.30f);
                case MapRegionType.Swamp: return new Color(0.24f, 0.48f, 0.42f);
                case MapRegionType.Lake: return new Color(0.16f, 0.44f, 0.66f);
                case MapRegionType.OpenWater: return new Color(0.08f, 0.26f, 0.48f);
                default: return new Color(0.46f, 0.58f, 0.32f);
            }
        }

        private void CreateRegionLabelHud(Transform canvas)
        {
            regionLabelRoot = CreateFullscreenLabelRoot(canvas, "RegionLabels");
            nearDetailLabelRoot = CreateFullscreenLabelRoot(canvas, "RegionDetailLabels");
        }

        private static RectTransform CreateFullscreenLabelRoot(Transform parent, string name)
        {
            GameObject root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private void RefreshRegionLabels()
        {
            HideLabelPool(regionLabelPool);
            HideLabelPool(nearDetailLabelPool);
            if (map?.regions == null || mapCamera == null || regionLabelRoot == null) return;
            WorldMapLabelSafeArea safeArea = WorldMapRegionPresentationPolicy.CreateGameplaySafeArea(
                Screen.width, Screen.height);
            float canvasScale = GetLabelCanvasScale();
            if (lastZoomLevel == WorldMapZoomLevel.Near)
            {
                List<WorldMapDetailLabelCandidate> detailCandidates = new List<WorldMapDetailLabelCandidate>();
                foreach (WorldCell cell in map.cells)
                {
                    presentationInfluenceByCell.TryGetValue(cell.index, out CellInfluenceState cached);
                    KnowledgeState knowledge = presentationKnownCellIndices.Contains(cell.index)
                        ? KnowledgeState.Known : KnowledgeState.Unknown;
                    if (!WorldMapRegionPresentationPolicy.ShowNearDetail(map.effectiveSeed, cell.index,
                            knowledge, cached?.level ?? InfluenceLevel.None)) continue;
                    Vector3 screen = mapCamera.WorldToScreenPoint(Center(cell.coord));
                    Vector2 preferred = MeasureLabel(
                        WorldMapRegionRules.PositionLabel(cell.internalPositionTag), 12f);
                    Vector2 size = WorldMapRegionPresentationPolicy.LabelScreenSize(
                        preferred, canvasScale, 10f, 6f, 56f, 22f);
                    float width = size.x;
                    float height = size.y;
                    float labelY = screen.y - height * 0.5f - 4f;
                    detailCandidates.Add(new WorldMapDetailLabelCandidate
                    {
                        cellIndex = cell.index,
                        influenceLevel = cached?.level ?? InfluenceLevel.None,
                        isSelected = cell.index == selectedCellIndex,
                        isInViewport = screen.z > 0f && screen.x >= 0f && screen.x <= Screen.width &&
                                       labelY >= 0f && labelY <= Screen.height,
                        isInSafeArea = safeArea.Contains(screen.x, labelY, width, height),
                        screenX = screen.x,
                        screenY = labelY,
                        width = width,
                        height = height
                    });
                }
                List<WorldMapDetailLabelCandidate> detailLabels =
                    WorldMapRegionPresentationPolicy.SelectNearDetailLabels(detailCandidates, map.effectiveSeed);
                for (int index = 0; index < detailLabels.Count; index++)
                {
                    WorldMapDetailLabelCandidate candidate = detailLabels[index];
                    WorldCell cell = map.cells[candidate.cellIndex];
                    TMP_Text label = GetPooledLabel(nearDetailLabelPool, nearDetailLabelRoot, index, 12f);
                    label.text = WorldMapRegionRules.PositionLabel(cell.internalPositionTag);
                    label.rectTransform.anchoredPosition = ScreenToCanvas(nearDetailLabelRoot,
                        candidate.screenX, candidate.screenY);
                    label.rectTransform.sizeDelta = new Vector2(
                        candidate.width / canvasScale,
                        candidate.height / canvasScale);
                    label.color = new Color(0.84f, 0.92f, 0.76f, 0.78f);
                }
                return;
            }

            string selectedRegionId = selectedCellIndex >= 0 && selectedCellIndex < map.cells.Length
                ? map.cells[selectedCellIndex].regionId : null;
            List<WorldMapRegionLabelCandidate> candidates = new List<WorldMapRegionLabelCandidate>();
            foreach (MapRegionData region in map.regions.Where(item => item != null &&
                         item.centerCellIndex >= 0 && item.centerCellIndex < map.cells.Length))
            {
                bool selected = region.regionId == selectedRegionId;
                int anchorCellIndex = selected ? selectedCellIndex : region.centerCellIndex;
                Vector3 screen = mapCamera.WorldToScreenPoint(Center(map.cells[anchorCellIndex].coord));
                bool visible = screen.z > 0f && screen.x >= 0f && screen.x <= Screen.width &&
                               screen.y >= 0f && screen.y <= Screen.height;
                bool known = presentationKnownRegionIds.Contains(region.regionId);
                string label = region.regionName + "·" + WorldMapRegionRules.RegionTypeLabel(region.regionType);
                Vector2 preferred = MeasureLabel(label, 16f);
                Vector2 size = WorldMapRegionPresentationPolicy.LabelScreenSize(
                    preferred, canvasScale, 12f, 8f, 72f, 28f);
                float width = size.x;
                float height = size.y;
                candidates.Add(new WorldMapRegionLabelCandidate
                {
                    regionId = region.regionId,
                    cellIndex = anchorCellIndex,
                    displayPriority = region.displayPriority,
                    isKnown = known,
                    isSelected = selected,
                    isInViewport = visible,
                    isInSafeArea = safeArea.Contains(screen.x, screen.y, width, height),
                    screenX = screen.x,
                    screenY = screen.y,
                    width = width,
                    height = height
                });
            }
            List<WorldMapRegionLabelCandidate> chosen =
                WorldMapRegionPresentationPolicy.SelectRegionLabels(candidates, lastZoomLevel);
            for (int index = 0; index < chosen.Count; index++)
            {
                WorldMapRegionLabelCandidate candidate = chosen[index];
                if (!presentationRegionById.TryGetValue(candidate.regionId, out MapRegionData region)) continue;
                TMP_Text label = GetPooledLabel(regionLabelPool, regionLabelRoot, index, 16f);
                label.text = region.regionName + "·" + WorldMapRegionRules.RegionTypeLabel(region.regionType);
                label.rectTransform.anchoredPosition = ScreenToCanvas(regionLabelRoot,
                    candidate.screenX, candidate.screenY);
                label.rectTransform.sizeDelta = new Vector2(
                    candidate.width / canvasScale,
                    candidate.height / canvasScale);
                label.color = candidate.isKnown ? new Color(1f, 0.94f, 0.78f, 0.92f) :
                    new Color(0.82f, 0.84f, 0.82f, 0.68f);
            }
        }

        private float GetLabelCanvasScale()
        {
            float scale = regionLabelRoot == null ? 0f : regionLabelRoot.lossyScale.x;
            if (scale <= 0f && hudCanvas != null) scale = hudCanvas.transform.lossyScale.x;
            return scale > 0f ? scale : 1f;
        }

        private Vector2 MeasureLabel(string text, float fontSize)
        {
            if (labelMeasure == null)
            {
                if (regionLabelRoot == null)
                    return new Vector2(Mathf.Max(1f, text.Length * fontSize), Mathf.Max(1f, fontSize * 1.4f));
                labelMeasure = RuntimeUIFactory.Text(regionLabelRoot, string.Empty,
                    Mathf.RoundToInt(fontSize), 24f);
                labelMeasure.alignment = TextAlignmentOptions.Center;
                labelMeasure.enableWordWrapping = false;
                labelMeasure.overflowMode = TextOverflowModes.Overflow;
                labelMeasure.raycastTarget = false;
                labelMeasure.color = new Color(0f, 0f, 0f, 0f);
            }
            labelMeasure.fontSize = fontSize;
            return labelMeasure.GetPreferredValues(text);
        }

        private static TMP_Text GetPooledLabel(List<TMP_Text> pool, RectTransform root, int index, float size)
        {
            while (pool.Count <= index)
            {
                TMP_Text text = RuntimeUIFactory.Text(root, string.Empty, Mathf.RoundToInt(size), 24f);
                text.alignment = TextAlignmentOptions.Center;
                text.raycastTarget = false;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Overflow;
                pool.Add(text);
            }
            TMP_Text label = pool[index];
            label.fontSize = size;
            label.gameObject.SetActive(true);
            return label;
        }

        private static void HideLabelPool(IEnumerable<TMP_Text> pool)
        {
            foreach (TMP_Text label in pool) if (label != null) label.gameObject.SetActive(false);
        }

        private static Vector2 ScreenToCanvas(RectTransform root, float x, float y)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(root,
                new Vector2(x, y), null, out Vector2 local) ? local : Vector2.zero;
        }

        private string DebugRegionSummary()
        {
            if (!debugViewEnabled || map?.cells == null || selectedCellIndex < 0 || selectedCellIndex >= map.cells.Length)
                return string.Empty;
            WorldCell cell = map.cells[selectedCellIndex];
            return $"\n[调试] Zoom={lastZoomLevel}｜Region={cell.regionId}｜Position={cell.internalPositionTag}";
        }

        private static Color LandformFillColor(WorldCell cell)
        {
            if (cell.landform == LandformType.Mountain && cell.isBuildable)
                return Color.Lerp(LandformColor(LandformType.Mountain), new Color(0.62f, 0.55f, 0.40f), 0.55f);
            Color baseColor = LandformColor(cell.landform);
            Color biomeTint;
            switch (cell.biome)
            {
                case BiomeType.Desert: biomeTint = new Color(0.95f, 0.70f, 0.28f); break;
                case BiomeType.Wetland: biomeTint = new Color(0.20f, 0.68f, 0.62f); break;
                case BiomeType.TemperateForest: biomeTint = new Color(0.12f, 0.42f, 0.18f); break;
                case BiomeType.Rainforest: biomeTint = new Color(0.05f, 0.56f, 0.30f); break;
                case BiomeType.Tundra: biomeTint = new Color(0.62f, 0.72f, 0.78f); break;
                case BiomeType.Snowfield: biomeTint = new Color(0.90f, 0.94f, 1f); break;
                case BiomeType.Alpine: biomeTint = new Color(0.56f, 0.60f, 0.72f); break;
                case BiomeType.Coast: biomeTint = new Color(0.82f, 0.78f, 0.44f); break;
                default: biomeTint = new Color(0.52f, 0.66f, 0.34f); break;
            }
            // Landform remains the dominant fill; biome tint supplies a second,
            // low-contrast cue without exposing hidden-cell environmental data.
            return Color.Lerp(baseColor, biomeTint, 0.30f);
        }

        private static Color TemperatureColor(float value)
        {
            float normalized = Mathf.Clamp01(value);
            if (normalized < 0.33f)
                return Color.Lerp(new Color(0.08f, 0.18f, 0.75f),
                    new Color(0.10f, 0.85f, 0.90f), normalized / 0.33f);
            if (normalized < 0.66f)
                return Color.Lerp(new Color(0.10f, 0.85f, 0.90f),
                    new Color(0.95f, 0.85f, 0.18f), (normalized - 0.33f) / 0.33f);
            return Color.Lerp(new Color(0.95f, 0.85f, 0.18f),
                new Color(0.90f, 0.10f, 0.08f), (normalized - 0.66f) / 0.34f);
        }

        private static Color MoistureColor(float value)
        {
            float normalized = Mathf.Clamp01(value);
            if (normalized < 0.5f)
                return Color.Lerp(new Color(0.58f, 0.32f, 0.12f),
                    new Color(0.28f, 0.70f, 0.28f), normalized * 2f);
            return Color.Lerp(new Color(0.28f, 0.70f, 0.28f),
                new Color(0.10f, 0.35f, 0.92f), (normalized - 0.5f) * 2f);
        }

        private static Color DominantElementColor(WorldCell cell)
        {
            SpiritElement dominant = SpiritElement.Metal;
            float maximum = cell.elementalAura.metal;
            if (cell.elementalAura.wood > maximum)
            { dominant = SpiritElement.Wood; maximum = cell.elementalAura.wood; }
            if (cell.elementalAura.water > maximum)
            { dominant = SpiritElement.Water; maximum = cell.elementalAura.water; }
            if (cell.elementalAura.fire > maximum)
            { dominant = SpiritElement.Fire; maximum = cell.elementalAura.fire; }
            if (cell.elementalAura.earth > maximum) dominant = SpiritElement.Earth;
            return Color.Lerp(new Color(0.025f, 0.025f, 0.03f), ElementColor(dominant),
                0.15f + Mathf.Clamp01(cell.totalAura) * 0.85f);
        }

        private void CreateObservabilityHud(Transform canvas)
        {
            observabilityToggle = RuntimeUIFactory.Button(canvas, "地图调试", 38);
            RectTransform toggleRect = observabilityToggle.GetComponent<RectTransform>();
            toggleRect.anchorMin = toggleRect.anchorMax = new Vector2(0f, 0f);
            toggleRect.pivot = new Vector2(0f, 0f);
            toggleRect.anchoredPosition = new Vector2(152f, 12f);
            toggleRect.sizeDelta = new Vector2(130f, 38f);
            observabilityToggle.onClick.AddListener(() => SetDebugViewEnabled(!debugViewEnabled));

            observabilityRoot = new GameObject("WorldMapObservability", typeof(RectTransform),
                typeof(Image), typeof(VerticalLayoutGroup));
            observabilityRoot.transform.SetParent(canvas, false);
            RectTransform rect = observabilityRoot.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(12f, 0f);
            rect.sizeDelta = new Vector2(410f, -24f);
            // 上端避开顶部资源栏，下端避开左下角的地图调试按钮。
            rect.offsetMin = new Vector2(rect.offsetMin.x, 58f);
            rect.offsetMax = new Vector2(rect.offsetMax.x, -64f);
            observabilityRoot.GetComponent<Image>().color = new Color(0.04f, 0.04f, 0.04f, 0.86f);
            VerticalLayoutGroup layout = observabilityRoot.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 7f;
            layout.childForceExpandHeight = false;

            AddObservabilityText(observabilityRoot.transform, "世界地图", 25, 38);
            GameObject tabs = new GameObject("Tabs", typeof(RectTransform),
                typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            tabs.transform.SetParent(observabilityRoot.transform, false);
            tabs.GetComponent<LayoutElement>().preferredHeight = 38f;
            HorizontalLayoutGroup tabLayout = tabs.GetComponent<HorizontalLayoutGroup>();
            tabLayout.spacing = 5f;
            tabLayout.childForceExpandWidth = true;
            AddObservabilityButton(tabs.transform, "视图", () => ShowObservabilityPage(0));
            AddObservabilityButton(tabs.transform, "统计", () => ShowObservabilityPage(1));
            AddObservabilityButton(tabs.transform, "参数", () => ShowObservabilityPage(2));

            Transform viewPage = CreateObservabilityScrollPage("ViewPage");
            AddViewButton(viewPage, "地貌", WorldMapViewMode.Landform);
            AddViewButton(viewPage, "高度", WorldMapViewMode.Height);
            AddViewButton(viewPage, "温度", WorldMapViewMode.Temperature);
            AddViewButton(viewPage, "湿度", WorldMapViewMode.Moisture);
            AddViewButton(viewPage, "群系", WorldMapViewMode.Biome);
            AddViewButton(viewPage, "灵气浓度", WorldMapViewMode.AuraConcentration);
            AddViewButton(viewPage, "五行主属性", WorldMapViewMode.DominantElement);
            AddViewButton(viewPage, "灵脉路径", WorldMapViewMode.SpiritVeinPaths);
            GameObject legendGraphic = new GameObject("MapLegendSymbols", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(WorldMapLegendGraphic), typeof(LayoutElement));
            legendGraphic.transform.SetParent(viewPage, false);
            legendGraphic.GetComponent<LayoutElement>().preferredHeight = 164f;
            legendText = AddObservabilityText(viewPage, string.Empty, 15, 210);

            Transform statisticsPage = CreateObservabilityScrollPage("StatisticsPage");
            statisticsText = AddObservabilityText(statisticsPage, "尚未生成地图。", 15, 1200);
            Transform parametersPage = CreateObservabilityScrollPage("ParametersPage");
            parametersText = AddObservabilityText(parametersPage,
                "参数随地图快照保存；正式游戏中只读。", 15, 620);

            ShowObservabilityPage(0);
            SetDebugViewEnabled(false);
        }

        private Transform CreateObservabilityScrollPage(string name)
        {
            GameObject page = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(ScrollRect));
            page.transform.SetParent(observabilityRoot.transform, false);
            LayoutElement pageLayout = page.GetComponent<LayoutElement>();
            pageLayout.flexibleHeight = 1f;
            pageLayout.minHeight = 180f;

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(page.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.12f);

            GameObject content = new GameObject("Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            VerticalLayoutGroup contentLayout = content.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 5f;
            contentLayout.padding = new RectOffset(5, 5, 5, 5);
            contentLayout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = page.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            observabilityPages.Add(page);
            return content.transform;
        }

        private void AddViewButton(Transform parent, string label, WorldMapViewMode mode) =>
            AddObservabilityButton(parent, label, () => SetViewMode(mode));

        private void ShowObservabilityPage(int index)
        {
            for (int i = 0; i < observabilityPages.Count; i++)
                observabilityPages[i].SetActive(i == index);
        }

        private void SetObservabilityVisible(bool visible)
        {
            if (observabilityRoot != null) observabilityRoot.SetActive(visible);
        }

        private void SetDebugToggleVisible(bool visible)
        {
            if (observabilityToggle != null) observabilityToggle.gameObject.SetActive(visible);
            if (!visible) SetDebugViewEnabled(false);
        }

        private void SetDebugViewEnabled(bool enabled)
        {
            debugViewEnabled = enabled;
            if (!enabled && viewMode != WorldMapViewMode.Landform)
                viewMode = WorldMapViewMode.Landform;
            SetObservabilityVisible(enabled);
            if (map != null)
            {
                Rebuild();
                RefreshLegend();
                RefreshDetails();
            }
        }

        private void RefreshObservability()
        {
            RefreshLegend();
            RefreshStatistics();
            RefreshParameters();
        }

        private void RefreshLegend()
        {
            if (legendText == null) return;
            switch (viewMode)
            {
                case WorldMapViewMode.Landform:
                    legendText.text = "离散地貌色；显示海岸和地貌边界、河流与地貌图标。";
                    break;
                case WorldMapViewMode.Height:
                    legendText.text = "固定 0–1：黑色（低）→白色（高）。";
                    break;
                case WorldMapViewMode.Temperature:
                    legendText.text = "固定 0–1：蓝→青→黄→红。";
                    break;
                case WorldMapViewMode.Moisture:
                    legendText.text = "固定 0–1：褐→绿→蓝。";
                    break;
                case WorldMapViewMode.Biome:
                    legendText.text = "离散群系色；显示海岸边界、河流与地貌图标。";
                    break;
                case WorldMapViewMode.AuraConcentration:
                    legendText.text = "固定 0–1：暗蓝→紫；标记降低透明度。";
                    break;
                case WorldMapViewMode.DominantElement:
                    legendText.text = "金米白、木绿、水蓝、火红、土黄；明暗表示总灵气。";
                    break;
                default:
                    legendText.text = "中性背景；路径颜色表示五行，大型灵脉更宽；仅保留洞府标记。";
                    break;
            }
        }

        private void RefreshStatistics()
        {
            if (statisticsText == null) return;
            if (map?.cells == null || map.cells.Length == 0)
            {
                statisticsText.text = "尚未生成地图。";
                return;
            }
            WorldMapStatistics statistics = WorldMapStatisticsCalculator.Calculate(map);
            StringBuilder text = new StringBuilder();
            text.AppendLine($"种子：{map.userSeed}  实际：{map.effectiveSeed}  版本：{map.generationVersion}");
            text.AppendLine($"尺寸：{map.width}×{map.height}  格数：{map.cells.Length}");
            text.AppendLine("\n地貌");
            foreach (LandformStatistic item in statistics.landforms)
                text.AppendLine($"{WorldMapCellDetailsFormatter.LandformLabel(item.type)}  {item.count}  {item.percentage:0.00}%");
            text.AppendLine("\n群系");
            foreach (BiomeStatistic item in statistics.biomes)
                text.AppendLine($"{WorldMapCellDetailsFormatter.BiomeLabel(item.type)}  {item.count}  {item.percentage:0.00}%");
            text.AppendLine($"\n高度 最小 {statistics.height.min:0.000} 中位 {statistics.height.median:0.000} 最大 {statistics.height.max:0.000}");
            AppendHistogram(text, "湿度分布", statistics.moisture);
            AppendHistogram(text, "灵气分布", statistics.aura);
            text.AppendLine($"灵气到达上限：{statistics.auraAtCapCount}（{statistics.auraAtCapPercentage:0.00}%）");
            text.AppendLine($"灵脉路径引用：{statistics.spiritVeinPathReferenceCount}");
            text.AppendLine($"路径去重格：{statistics.spiritVeinDistinctPathCellCount}");
            text.AppendLine($"路径重复率：{statistics.spiritVeinPathDuplicateRate * 100f:0.00}%");
            text.AppendLine($"影响区去重格：{statistics.spiritVeinInfluenceCellCount}（{statistics.spiritVeinInfluencePercentage:0.00}%）");
            statisticsText.text = text.ToString();
        }

        private static void AppendHistogram(StringBuilder text, string title, IEnumerable<HistogramBin> bins)
        {
            text.AppendLine("\n" + title);
            int index = 0;
            foreach (HistogramBin bin in bins)
            {
                string upperBracket = index == 9 ? "]" : ")";
                text.AppendLine($"[{bin.lowerInclusive:0.0},{bin.upperExclusive:0.0}{upperBracket} {bin.count}  {bin.percentage:0.00}%");
                index++;
            }
        }

        private void RefreshParameters()
        {
            if (parametersText == null) return;
            MapGenerationSettings settings = map?.generationSettings;
            if (settings == null)
            {
                parametersText.text = "地图没有有效的参数快照。";
                return;
            }
            parametersText.text =
                $"正式游戏参数只读\n\n种子 {settings.seed}\n尺寸 {settings.width}×{settings.height}\n生成版本 {settings.generationVersion}\n\n" +
                $"地貌\n深水 {settings.terrain.deepWaterThreshold:0.###}\n海平面 {settings.terrain.seaLevel:0.###}\n" +
                $"平原上限 {settings.terrain.plainUpperThreshold:0.###}\n丘陵上限 {settings.terrain.hillUpperThreshold:0.###}\n\n" +
                $"气候\n纬度降温 {settings.climate.latitudeCoolingStrength:0.###}\n温度噪声 {settings.climate.temperatureNoiseStrength:0.###}\n" +
                $"海拔降温 {settings.climate.elevationCoolingStrength:0.###}\n湿度噪声 {settings.climate.moistureNoiseStrength:0.###}\n" +
                $"距水增湿 {settings.climate.waterProximityMoistureStrength:0.###}\n河流增湿 {settings.climate.riverMoistureBoost:0.###}\n\n" +
                $"河流\n最小汇水量 {settings.rivers.minimumAccumulatedFlow:0.###}\n最低源头高度 {settings.rivers.minimumSourceHeight:0.###}\n" +
                $"最短支流 {settings.rivers.minimumBranchLength}\n\n" +
                $"灵脉\n大型数量 {Range(settings.spiritVeins.largeCount)}\n中型数量 {Range(settings.spiritVeins.mediumCount)}\n" +
                $"大型长度 {Range(settings.spiritVeins.largeLength)}\n中型长度 {Range(settings.spiritVeins.mediumLength)}\n" +
                $"大型半径 {Range(settings.spiritVeins.largeRadius)}\n中型半径 {Range(settings.spiritVeins.mediumRadius)}";
        }

        private static string Range(InclusiveIntRange range) =>
            range == null ? "无" : $"{range.min}–{range.max}";

        private static TMP_Text AddObservabilityText(Transform parent, string value, float size, float height)
        {
            GameObject obj = new GameObject("Text", typeof(RectTransform),
                typeof(TextMeshProUGUI), typeof(LayoutElement));
            obj.transform.SetParent(parent, false);
            TMP_Text text = obj.GetComponent<TMP_Text>();
            text.text = value;
            text.fontSize = size;
            text.color = Color.white;
            text.enableWordWrapping = true;
            obj.GetComponent<LayoutElement>().preferredHeight = height;
            return text;
        }

        private static Button AddObservabilityButton(Transform parent, string label,
            UnityEngine.Events.UnityAction action)
        {
            GameObject obj = new GameObject(label, typeof(RectTransform), typeof(Image),
                typeof(Button), typeof(LayoutElement));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<Image>().color = new Color(0.28f, 0.21f, 0.12f);
            obj.GetComponent<LayoutElement>().preferredHeight = 38f;
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(action);
            TMP_Text text = AddObservabilityText(obj.transform, label, 17, 38f);
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }
    }
}
