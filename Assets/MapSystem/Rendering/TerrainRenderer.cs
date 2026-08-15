using System.Collections.Generic;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 3D 地形表现层：由调用方传入 WorldMap 生成 chunk 网格并显示。
    /// 不直接读取 WorldMapSession，方便测试注入地图。
    /// </summary>
    public sealed class TerrainRenderer : MonoBehaviour
    {
        public enum MapDetailLevel
        {
            Near = 0,
            Mid = 1,
            Far = 2
        }

        private readonly List<GameObject> farChunkObjects = new List<GameObject>();
        private readonly List<GameObject> nearChunkObjects = new List<GameObject>();
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        private readonly List<Material> ownedMaterials = new List<Material>();
        [SerializeField] private int chunkSize = 16;
        [Header("Terrain surface")]
        [Tooltip("使用共享顶点的连续地表；关闭后回退到原有扁平六边形表面。")]
        [SerializeField] private bool useContinuousTerrainSurface = true;
        [Tooltip("近景连续地表的格内细分级别；远景固定使用最低细分。")]
        [SerializeField, Range(1, 4)] private int continuousSurfaceSubdivisions = 2;
        [Tooltip("仅影响显示网格的高差，不写回 WorldCell.height。")]
        [SerializeField, Range(0.25f, 3f)] private float terrainReliefScale = 1f;
        [Tooltip("在连续地表上混合沙、草、泥、石材质权重；关闭后恢复逐格材质。")]
        [SerializeField] private bool blendContinuousTerrainMaterials = true;
        [Header("Strategic ground textures")]
        [SerializeField] private Texture2D grassTexture;
        [SerializeField] private Texture2D dirtTexture;
        [SerializeField] private Texture2D stoneTexture;
        [SerializeField] private Texture2D sandTexture;
        [SerializeField] private Texture2D grassNormal;
        [SerializeField] private Texture2D dirtNormal;
        [SerializeField] private Texture2D stoneNormal;
        [SerializeField] private Texture2D sandNormal;

        internal Texture2D RegionGrassTexture => grassTexture;
        internal Texture2D RegionDirtTexture => dirtTexture;
        internal Texture2D RegionStoneTexture => stoneTexture;
        [SerializeField, Range(0f, 1f)] private float groundTextureStrength = 0.82f;
        [SerializeField, Range(0.5f, 2.5f)] private float groundTextureContrast = 1.55f;
        [SerializeField, Min(0.01f)] private float groundTextureTiling = 0.46f;
        [SerializeField, Range(0f, 0.35f)] private float groundMacroStrength = 0.22f;
        [SerializeField, Min(0.005f)] private float groundMacroScale = 0.055f;
        [SerializeField, Range(0f, 0.5f)] private float groundTextureColorBlend = 0.10f;
        [SerializeField, Range(0f, 1.5f)] private float groundNormalStrength = 0.55f;
        [SerializeField, Range(0f, 1f)] private float groundSaturation = 0.78f;
        [SerializeField, Range(0f, 1f)] private float groundLightingStrength = 0.72f;
        [SerializeField, Range(0f, 1f)] private float groundAtmosphereStrength = 0.35f;
        [SerializeField] private bool groundTextureOnly;
        [SerializeField, Range(0.75f, 1.35f)] private float groundBrightness = 1f;
        [SerializeField, Range(0f, 1f)] private float groundLinearColorLift = 0.30f;
        // 近景表现参数：压低垂直夸张度、减弱侧壁暗化，降低六边形棋盘感。
        [SerializeField] private float nearHeightScale = 1.15f;
        [SerializeField] private float nearSideDarkenFactor = 0.94f;
        private TerrainMeshAppearance nearAppearance = TerrainMeshAppearance.Default;
        private TerrainMeshAppearance farAppearance = TerrainMeshAppearance.Default;
        private WorldMap3DZoomTier activeTier = WorldMap3DZoomTier.Far;
        // 俯仰角（与水平面的夹角）：90 = 完全俯视。
        // 镜头恢复到曲面实验前的基准；纵深由独立径向曲率表现，不再改坏中远景。
        [SerializeField] private float cameraFieldOfViewDegrees = 30f;
        [SerializeField] private float nearFieldOfViewDegrees = 45f;
        [SerializeField] private float cameraPitchDegrees = 55f;
        [SerializeField] private float cameraPitchFarDegrees = 45f;
        [SerializeField] private float cameraDistanceFactor = 0.85f;
        [SerializeField] private float cameraCurveMaxVisibleHexes = 16f;
        [Header("Near radial curvature")]
        [SerializeField, Range(0f, 0.02f)] private float nearRadialCurvature = 0f;
        [SerializeField, Range(0f, 1f)] private float curvatureNearZoomThreshold = 0.30f;
        [Header("Horizon atmosphere")]
        [SerializeField] private Color horizonFogColor = new Color(0.52f, 0.60f, 0.66f, 1f);
        // Civ VI camera1: Fog_Start / Height = 400/180 near, 1300/880 far.
        [SerializeField] private float nearFogStartHeightFactor = 2.222222f;
        [SerializeField] private float farFogStartHeightFactor = 1.477273f;
        // Civ VI density 0.0010 -> 0.0016. Unity Linear fog用相反的可见跨度表达同一趋势。
        [SerializeField] private float nearFogSpanHeightFactor = 1.5f;
        [SerializeField] private float farFogSpanHeightFactor = 0.9375f;
        // 最近拉近时屏幕横向约可见 5 格（可按手感调整）。
        [SerializeField] private float minVisibleHexes = 5f;
        // 最远可拉到的距离 = 全图适应距离 × 该系数，保证拉远后仍能看到整张地图的轮廓。
        [SerializeField] private float maxZoomOutFactor = 1.15f;
        [SerializeField] private float cameraZoomRatio = 0.88f;
        [Header("Civ VI style camera")]
        [SerializeField, Range(0f, 1f)] private float zoomLevel = 0.35f;
        [SerializeField, Min(0.001f)] private float zoomSpeed = 0.015f;
        [SerializeField, Min(0.01f)] private float zoomSmoothTime = 0.25f;
        [SerializeField, Min(0.01f)] private float panSmoothTime = 0.08f;
        [SerializeField, Min(0.01f)] private float keyboardPanSpeed = 40f;
        [SerializeField] private bool enableEdgePan = true;
        [SerializeField, Min(1f)] private float edgePanBorder = 12f;
        [SerializeField] private float focusHeightOffset = 0f;
        [SerializeField] private AnimationCurve heightCurve = new AnimationCurve(
            new Keyframe(0f, 6f),
            new Keyframe(0.45f, 22f),
            new Keyframe(1f, 210f));
        [SerializeField] private AnimationCurve pitchCurve = new AnimationCurve(
            new Keyframe(0f, 40f),
            new Keyframe(0.5f, 42f),
            new Keyframe(1f, 45f));
        private float currentZoom;
        private float zoomVelocity;
        private Vector3 targetPivot;
        private Vector3 focusVelocity;
        private float cameraYawDegrees;
        private float initialVisibleHexes;
        private int initialFocusCellIndex = -1;
        private WorldMap map;
        private float[] farSurfaceHeights;
        private float[] nearSurfaceHeights;
        private System.Func<Vector2, bool, float> farSurfaceHeightAt;
        private System.Func<Vector2, bool, float> nearSurfaceHeightAt;
        private Vector3 cameraPivot;
        private float cameraDistance;
        private float maxCameraDistance;
        private Vector3 lastPointerPosition;
        private bool atmosphereStateCaptured;
        private bool previousFogEnabled;
        private FogMode previousFogMode;
        private Color previousFogColor;
        private float previousFogDensity;
        private float previousFogStartDistance;
        private float previousFogEndDistance;
        private Vector3 activeCurveOrigin;
        private float activeCurveStrength;
        private int curveRevision;
        private bool pointerInputBlocked;
        private WorldMapClimateDebugView climateDebugView;

        /// <summary>当前显示的战略地表 chunk 数量。</summary>
        public int ChunkCount => activeTier == WorldMap3DZoomTier.Far
            ? farChunkObjects.Count
            : nearChunkObjects.Count;

        /// <summary>远景战略地表网格的 chunk 数量。</summary>
        public int FarChunkCount => farChunkObjects.Count;

        /// <summary>近景战略地表网格的 chunk 数量。</summary>
        public int NearChunkCount => nearChunkObjects.Count;

        /// <summary>当前生效的缩放档位。</summary>
        public WorldMap3DZoomTier ActiveZoomTier => activeTier;

        /// <summary>Civ Zoom 驱动的表现层级：近 0~0.25、中 0.25~0.60、远 0.60~1。</summary>
        public MapDetailLevel CurrentDetailLevel =>
            currentZoom < 0.25f ? MapDetailLevel.Near :
            currentZoom < 0.60f ? MapDetailLevel.Mid : MapDetailLevel.Far;

        /// <summary>当前相机斜向旋转角，供视觉规范测试读取。</summary>
        internal float CameraYawDegrees => cameraYawDegrees;

        /// <summary>默认构图目标的横向可见格数；0 表示沿用整图适配。</summary>
        internal float InitialVisibleHexes => initialVisibleHexes;

        /// <summary>最近视角的横向可见格数。</summary>
        internal float MinimumVisibleHexes => minVisibleHexes;

        /// <summary>文明6参考曲线的端点，供测试核对。</summary>
        internal float CameraFieldOfViewDegrees => cameraFieldOfViewDegrees;
        internal float NearFieldOfViewDegrees => nearFieldOfViewDegrees;
        internal float ActiveFieldOfViewDegrees { get; private set; }
        internal float ActiveCameraDistance => cameraDistance;
        internal float ActiveVisibleHexesAcross { get; private set; }
        internal float ZoomLevel => zoomLevel;
        internal float CurrentZoom => currentZoom;
        internal bool EdgePanEnabled => enableEdgePan;
        internal float CameraHeightForZoom(float zoom) => heightCurve.Evaluate(zoom);
        internal float CameraPitchForZoom(float zoom) => pitchCurve.Evaluate(zoom);
        internal Vector3 TargetPivot => targetPivot;
        internal float CameraNearPitchDegrees => cameraPitchDegrees;
        internal float CameraFarPitchDegrees => cameraPitchFarDegrees;
        internal float CameraCurveMaxVisibleHexes => cameraCurveMaxVisibleHexes;
        internal float NearRadialCurvature => nearRadialCurvature;
        internal float ActiveCurveStrength => activeCurveStrength;
        internal Vector3 ActiveCurveOrigin => activeCurveOrigin;
        internal int CurveRevision => curveRevision;
        internal float NearFogStartHeightFactor => nearFogStartHeightFactor;
        internal float FarFogStartHeightFactor => farFogStartHeightFactor;
        internal WorldMapClimateDebugView ClimateDebugView => climateDebugView;
        internal float GroundTextureStrength => groundTextureStrength;
        internal float GroundTextureContrast => groundTextureContrast;
        internal float GroundTextureTiling => groundTextureTiling;
        internal float GroundMacroStrength => groundMacroStrength;
        internal float GroundMacroScale => groundMacroScale;
        internal float GroundTextureColorBlend => groundTextureColorBlend;
        internal float GroundNormalStrength => groundNormalStrength;
        internal bool GroundTextureOnly => groundTextureOnly;
        internal bool UsesContinuousTerrainSurface => useContinuousTerrainSurface;
        internal int ContinuousSurfaceSubdivisions => continuousSurfaceSubdivisions;
        internal float TerrainReliefScale => terrainReliefScale;
        public bool BlendsContinuousTerrainMaterials => blendContinuousTerrainMaterials;

        internal void SetNearRadialCurvature(float value)
        {
            nearRadialCurvature = Mathf.Clamp(value, 0f, 0.02f);
            Camera camera = Camera.main;
            if (camera != null && map != null) ApplyCameraTransform(camera);
        }

        internal void SetZoomLevel(float value)
        {
            zoomLevel = Mathf.Clamp01(value);
        }

        internal void SetEdgePanEnabled(bool enabled)
        {
            enableEdgePan = enabled;
        }

        internal void SetTerrainReliefScale(float value)
        {
            float clamped = Mathf.Clamp(value, 0.25f, 3f);
            if (Mathf.Approximately(terrainReliefScale, clamped)) return;
            terrainReliefScale = clamped;
            RefreshCurrentMapPreservingView();
        }

        /// <summary>
        /// 调整近景透视强度，同时保持当前横向可见格数不变。
        /// 中景会平滑恢复到基准 FOV，远景不受近景调试值影响。
        /// </summary>
        internal void SetNearFieldOfView(float value)
        {
            nearFieldOfViewDegrees = Mathf.Clamp(value, 20f, 70f);
            Camera camera = Camera.main;
            if (camera == null || map == null) return;
            float visibleHexes = VisibleHexesForCurrentCamera(camera);
            SetFieldOfViewPreservingCoverage(camera,
                FieldOfViewForVisibleHexes(visibleHexes), visibleHexes);
            ApplyCameraTransform(camera);
        }

        internal bool TryCaptureCameraView(out Vector3 pivot, out float visibleHexes)
        {
            pivot = cameraPivot;
            visibleHexes = ActiveVisibleHexesAcross;
            return map != null && Camera.main != null && visibleHexes > 0f;
        }

        internal bool RestoreCameraView(Vector3 pivot, float visibleHexes)
        {
            Camera camera = Camera.main;
            if (camera == null || map == null || visibleHexes <= 0f) return false;
            cameraPivot = pivot;
            ClampPivotToMap();
            camera.fieldOfView = FieldOfViewForVisibleHexes(visibleHexes);
            cameraDistance = Mathf.Clamp(ZoomDistanceForVisibleHexes(camera, visibleHexes),
                ZoomDistanceForVisibleHexes(camera, minVisibleHexes), maxCameraDistance);
            ApplyCameraTransform(camera);
            return true;
        }

        internal void SetClimateDebugView(WorldMapClimateDebugView view)
        {
            if (climateDebugView == view) return;
            climateDebugView = view;
            RefreshCurrentMapPreservingView();
        }

        internal void SetGroundTextureDebug(float strength, float contrast, float tiling, bool textureOnly)
        {
            float clampedStrength = Mathf.Clamp01(strength);
            float clampedContrast = Mathf.Clamp(contrast, 0.5f, 2.5f);
            float clampedTiling = Mathf.Clamp(tiling, 0.05f, 2f);
            if (Mathf.Approximately(groundTextureStrength, clampedStrength) &&
                Mathf.Approximately(groundTextureContrast, clampedContrast) &&
                Mathf.Approximately(groundTextureTiling, clampedTiling) &&
                groundTextureOnly == textureOnly) return;
            groundTextureStrength = clampedStrength;
            groundTextureContrast = clampedContrast;
            groundTextureTiling = clampedTiling;
            groundTextureOnly = textureOnly;
            RefreshCurrentMapPreservingView();
        }

        private void RefreshCurrentMapPreservingView()
        {
            if (map == null) return;
            WorldMap currentMap = map;
            bool captured = TryCaptureCameraView(out Vector3 pivot, out float visibleHexes);
            Render(currentMap);
            if (captured) RestoreCameraView(pivot, visibleHexes);
        }

        internal void SetPointerInputBlocked(bool blocked)
        {
            pointerInputBlocked = blocked;
            if (blocked) lastPointerPosition = Input.mousePosition;
        }

        /// <summary>当前地形网格使用的表现参数，供地点图标按同一高度定位。</summary>
        public static TerrainMeshAppearance ActiveAppearance { get; private set; } = TerrainMeshAppearance.Default;
        public static bool ActiveContinuousSurface { get; private set; }
        /// <summary>当前全局表现层级，供 RegionNameRenderer 等其它表现层读取。</summary>
        public static MapDetailLevel ActiveDetailLevel { get; private set; } = MapDetailLevel.Mid;
        private static float[] activeSurfaceHeights;
        private static System.Func<Vector2, bool, float> activeSurfaceHeightAt;
        private static WorldMap activeSurfaceMap;

        /// <summary>当前表现层在格子中心的高度；连续地表关闭时回退到战略扁平高度。</summary>
        public static float PresentationSurfaceHeight(WorldMap worldMap, WorldCell cell)
        {
            if (cell == null) return 0f;
            if (ActiveContinuousSurface && ReferenceEquals(worldMap, activeSurfaceMap) &&
                activeSurfaceHeights != null &&
                cell.index >= 0 && cell.index < activeSurfaceHeights.Length &&
                worldMap?.cells != null && cell.index < worldMap.cells.Length &&
                ReferenceEquals(worldMap.cells[cell.index], cell))
                return activeSurfaceHeights[cell.index];
            return TerrainMeshGenerator.StrategicSurfaceHeight(cell);
        }

        /// <summary>连续地表在任意 XZ 位置的实际高度；供网格与格内装饰贴合坡面。</summary>
        public static float PresentationSurfaceHeightAt(WorldMap worldMap, Vector2 position,
            WorldCell surfaceCell)
        {
            if (surfaceCell == null) return 0f;
            if (ActiveContinuousSurface && ReferenceEquals(worldMap, activeSurfaceMap) &&
                activeSurfaceHeightAt != null)
            {
                bool water = surfaceCell.landform == LandformType.DeepWater ||
                             surfaceCell.landform == LandformType.ShallowWater;
                return activeSurfaceHeightAt(position, water);
            }
            return PresentationSurfaceHeight(worldMap, surfaceCell);
        }

        /// <summary>
        /// 套用世界地图第一版视觉规范。只调整表现层和初始取景，不修改 WorldMap 数据。
        /// </summary>
        internal void ApplyWorldMapVisualProfile(int focusCellIndex)
        {
            cameraYawDegrees = 12f;
            cameraFieldOfViewDegrees = 40f;
            nearFieldOfViewDegrees = 45f;
            cameraPitchDegrees = 55f;
            cameraPitchFarDegrees = 45f;
            cameraCurveMaxVisibleHexes = 16f;
            nearRadialCurvature = 0f;
            curvatureNearZoomThreshold = 0.30f;
            horizonFogColor = new Color(0.52f, 0.60f, 0.66f, 1f);
            nearFogStartHeightFactor = 5.5f;
            farFogStartHeightFactor = 1300f / 880f;
            nearFogSpanHeightFactor = 4.5f;
            farFogSpanHeightFactor = nearFogSpanHeightFactor / 1.6f;
            initialVisibleHexes = 12f;
            initialFocusCellIndex = focusCellIndex;
            minVisibleHexes = 5f;
            zoomLevel = 0.35f;
            currentZoom = zoomLevel;
            zoomVelocity = 0f;
            focusVelocity = Vector3.zero;
            zoomSmoothTime = 0.25f;
            panSmoothTime = 0.08f;
            enableEdgePan = true;
            edgePanBorder = 12f;
            focusHeightOffset = 0f;
            heightCurve = new AnimationCurve(
                new Keyframe(0f, 6f),
                new Keyframe(0.45f, 22f),
                new Keyframe(1f, 210f));
            pitchCurve = new AnimationCurve(
                new Keyframe(0f, 40f),
                new Keyframe(0.5f, 42f),
                new Keyframe(1f, 45f));
            // 用户最终方向：总高约 2 个六角格，前坡保持 5 环宽。
            nearHeightScale = 1.05f;
            nearSideDarkenFactor = 0.96f;
            farAppearance = new TerrainMeshAppearance
            {
                heightScale = 0.55f,
                sideDarkenFactor = 0.90f
            };
        }

        /// <summary>由调用方传入地图并生成 3D 地形；重复调用会先清理旧表现。</summary>
        public void Render(WorldMap worldMap)
        {
            Clear();
            map = worldMap;
            if (map == null || map.cells == null || map.cells.Length == 0)
            {
                Debug.LogWarning("TerrainRenderer.Render 收到空地图，跳过生成");
                return;
            }
            TerrainPresentationModels.SetClimateDebugMap(map);

            Material[] materials = new Material[TerrainMeshGenerator.SubmeshCount];
            for (int submesh = 0; submesh < materials.Length; submesh++)
            {
                LandformType landform = TerrainMaterialProvider.RepresentativeLandform(submesh);
                Material material = CreateStrategicMaterial(landform);
                materials[submesh] = material;
                ownedMaterials.Add(material);
            }

            Color32 NearColor(WorldCell cell) => climateDebugView == WorldMapClimateDebugView.Normal
                ? TerrainPresentationModels.ColorForCell(cell)
                : TerrainPresentationModels.ColorForClimateDebug(cell, climateDebugView);
            nearAppearance = new TerrainMeshAppearance
            {
                heightScale = Mathf.Max(0.1f, nearHeightScale * terrainReliefScale),
                sideDarkenFactor = Mathf.Clamp01(nearSideDarkenFactor)
            };
            TerrainMeshAppearance effectiveFarAppearance = farAppearance;
            // Far and near meshes are two tessellation levels of the same terrain, not two
            // different reliefs. Keeping the height scale identical removes zoom-tier popping.
            effectiveFarAppearance.heightScale = nearAppearance.heightScale;
            Color32 FarColor(WorldCell cell) => TerrainPresentationModels.FarColorForCell(cell);
            List<Mesh> farMeshes;
            List<Mesh> nearMeshes;
            if (useContinuousTerrainSurface)
            {
                bool blendLandMaterials = blendContinuousTerrainMaterials &&
                                          climateDebugView == WorldMapClimateDebugView.Normal;
                ContinuousTerrainSurfaceBuilder.BuildResult farBuild =
                    ContinuousTerrainSurfaceBuilder.CreateTerrainChunks(map, chunkSize, FarColor,
                        effectiveFarAppearance, 1, false);
                ContinuousTerrainSurfaceBuilder.BuildResult nearBuild =
                    ContinuousTerrainSurfaceBuilder.CreateTerrainChunks(map, chunkSize, NearColor,
                        nearAppearance, continuousSurfaceSubdivisions, blendLandMaterials);
                farMeshes = farBuild.meshes;
                nearMeshes = nearBuild.meshes;
                farSurfaceHeights = farBuild.centerHeights;
                nearSurfaceHeights = nearBuild.centerHeights;
                farSurfaceHeightAt = farBuild.heightAt;
                nearSurfaceHeightAt = nearBuild.heightAt;
            }
            else
            {
                farMeshes = TerrainMeshGenerator.CreateTerrainChunks(map, chunkSize, FarColor,
                    farAppearance);
                nearMeshes = TerrainMeshGenerator.CreateTerrainChunks(map, chunkSize, NearColor,
                    nearAppearance);
                farSurfaceHeights = null;
                nearSurfaceHeights = null;
                farSurfaceHeightAt = null;
                nearSurfaceHeightAt = null;
            }
            ActiveContinuousSurface = useContinuousTerrainSurface;
            activeSurfaceMap = map;
            CreateChunkObjects(farMeshes, farChunkObjects, materials);
            CreateChunkObjects(nearMeshes, nearChunkObjects, materials);

            FitCamera();
            activeTier = (WorldMap3DZoomTier)(int)CurrentDetailLevel;
            ApplyTier(activeTier);
        }

        private Material CreateStrategicMaterial(LandformType landform)
        {
            Shader shader = Shader.Find("Cultivation4X/StrategicTerrain") ??
                            Shader.Find("Unlit/VertexColor");
            Material material = shader != null
                ? new Material(shader)
                : TerrainMaterialProvider.CreateMaterial(landform);
            material.name = "StrategicTerrain_" + landform;
            if (!material.HasProperty("_MainTex")) return material;

            Texture texture = TextureFor(landform);
            material.SetTexture("_MainTex", texture != null ? texture : Texture2D.whiteTexture);
            if (material.HasProperty("_SandTex"))
                material.SetTexture("_SandTex", sandTexture != null ? sandTexture : Texture2D.whiteTexture);
            if (material.HasProperty("_GrassTex"))
                material.SetTexture("_GrassTex", grassTexture != null ? grassTexture : Texture2D.whiteTexture);
            if (material.HasProperty("_DirtTex"))
                material.SetTexture("_DirtTex", dirtTexture != null ? dirtTexture : Texture2D.whiteTexture);
            if (material.HasProperty("_StoneTex"))
                material.SetTexture("_StoneTex", stoneTexture != null ? stoneTexture : Texture2D.whiteTexture);
            if (material.HasProperty("_SandNormal"))
                material.SetTexture("_SandNormal", sandNormal != null ? sandNormal : Texture2D.normalTexture);
            if (material.HasProperty("_GrassNormal"))
                material.SetTexture("_GrassNormal", grassNormal != null ? grassNormal : Texture2D.normalTexture);
            if (material.HasProperty("_DirtNormal"))
                material.SetTexture("_DirtNormal", dirtNormal != null ? dirtNormal : Texture2D.normalTexture);
            if (material.HasProperty("_StoneNormal"))
                material.SetTexture("_StoneNormal", stoneNormal != null ? stoneNormal : Texture2D.normalTexture);
            if (material.HasProperty("_UseTerrainBlend"))
                material.SetFloat("_UseTerrainBlend", useContinuousTerrainSurface &&
                    blendContinuousTerrainMaterials &&
                    climateDebugView == WorldMapClimateDebugView.Normal &&
                    landform == LandformType.Plain ? 1f : 0f);
            if (material.HasProperty("_TextureStrength"))
                material.SetFloat("_TextureStrength", climateDebugView != WorldMapClimateDebugView.Normal ||
                    TerrainMaterialProvider.IsWaterGroup(landform)
                    ? 0f
                    : groundTextureStrength);
            if (material.HasProperty("_TextureContrast"))
                material.SetFloat("_TextureContrast", groundTextureContrast);
            if (material.HasProperty("_TextureOnly"))
                material.SetFloat("_TextureOnly", climateDebugView == WorldMapClimateDebugView.Normal &&
                    !TerrainMaterialProvider.IsWaterGroup(landform) && groundTextureOnly ? 1f : 0f);
            if (material.HasProperty("_WorldTiling"))
                material.SetFloat("_WorldTiling", groundTextureTiling);
            if (material.HasProperty("_MacroStrength"))
                material.SetFloat("_MacroStrength", groundMacroStrength);
            if (material.HasProperty("_MacroScale"))
                material.SetFloat("_MacroScale", groundMacroScale);
            if (material.HasProperty("_TextureColorBlend"))
                material.SetFloat("_TextureColorBlend", groundTextureColorBlend);
            if (material.HasProperty("_Brightness"))
                material.SetFloat("_Brightness", groundBrightness);
            if (material.HasProperty("_LinearColorLift"))
                material.SetFloat("_LinearColorLift", groundLinearColorLift);
            if (material.HasProperty("_Saturation"))
                material.SetFloat("_Saturation", groundSaturation);
            if (material.HasProperty("_TerrainLightingStrength"))
                material.SetFloat("_TerrainLightingStrength",
                    climateDebugView != WorldMapClimateDebugView.Normal ? 0f :
                    TerrainMaterialProvider.IsWaterGroup(landform) ? 0.12f : groundLightingStrength);
            if (material.HasProperty("_TerrainNormalStrength"))
                material.SetFloat("_TerrainNormalStrength", groundNormalStrength);
            return material;
        }

        private Texture2D TextureFor(LandformType landform)
        {
            switch (landform)
            {
                case LandformType.Coast: return sandTexture;
                case LandformType.Plain: return grassTexture;
                case LandformType.Hill: return dirtTexture;
                case LandformType.Mountain: return stoneTexture;
                default: return null;
            }
        }

        private void CreateChunkObjects(List<Mesh> meshes, List<GameObject> targets, Material[] materials)
        {
            foreach (Mesh mesh in meshes)
            {
                ownedMeshes.Add(mesh);
                GameObject chunk = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer),
                    typeof(MeshCollider));
                chunk.transform.SetParent(transform, false);
                chunk.GetComponent<MeshFilter>().sharedMesh = mesh;
                chunk.GetComponent<MeshRenderer>().sharedMaterials = materials;
                chunk.GetComponent<MeshCollider>().sharedMesh = mesh;
                targets.Add(chunk);
            }
        }

        /// <summary>
        /// 从屏幕位置射线命中当前可见地形，并返回底层 WorldCell 索引。
        /// 连续山体不会改变 XZ 六边格布局，因此命中后仍可精确还原原始 Hex。
        /// </summary>
        public bool TryPickCell(Camera camera, Vector2 screenPosition, out int cellIndex)
        {
            cellIndex = -1;
            if (camera == null || map?.cells == null) return false;
            Ray ray = camera.ScreenPointToRay(screenPosition);
            if (activeCurveStrength > 0.000001f &&
                TryIntersectCurvedSurface(ray, out Vector3 curvedPoint))
                return TryGetCellIndexAtWorldPosition(curvedPoint, out cellIndex);

            float nearestDistance = float.PositiveInfinity;
            Vector3 nearestPoint = default;
            bool hitTerrain = false;
            List<GameObject> activeChunks = activeTier == WorldMap3DZoomTier.Far
                ? farChunkObjects
                : nearChunkObjects;
            foreach (GameObject chunk in activeChunks)
            {
                if (chunk == null || !chunk.activeInHierarchy) continue;
                MeshCollider collider = chunk.GetComponent<MeshCollider>();
                if (collider == null || !collider.Raycast(ray, out RaycastHit hit, nearestDistance)) continue;
                nearestDistance = hit.distance;
                nearestPoint = hit.point;
                hitTerrain = true;
            }
            return hitTerrain && TryGetCellIndexAtWorldPosition(nearestPoint, out cellIndex);
        }

        private bool TryIntersectCurvedSurface(Ray ray, out Vector3 point)
        {
            point = default;
            float baseHeight = transform.TransformPoint(Vector3.up *
                TerrainMeshGenerator.LandStrategicHeight).y;
            if (!TryIntersectCurvedPlane(ray, baseHeight, out point)) return false;
            if (!TryGetCellIndexAtWorldPosition(point, out int candidate) ||
                candidate < 0 || candidate >= map.cells.Length || map.cells[candidate] == null)
                return false;
            baseHeight = transform.TransformPoint(Vector3.up *
                PresentationSurfaceHeight(map, map.cells[candidate])).y;
            return TryIntersectCurvedPlane(ray, baseHeight, out point);
        }

        private bool TryIntersectCurvedPlane(Ray ray, float baseHeight, out Vector3 point)
        {
            point = default;
            Vector3 relative = ray.origin - activeCurveOrigin;
            float horizontalDirectionSq = ray.direction.x * ray.direction.x +
                                          ray.direction.z * ray.direction.z;
            float a = activeCurveStrength * horizontalDirectionSq;
            float b = ray.direction.y + 2f * activeCurveStrength *
                (relative.x * ray.direction.x + relative.z * ray.direction.z);
            float c = ray.origin.y - baseHeight + activeCurveStrength *
                (relative.x * relative.x + relative.z * relative.z);
            if (a <= 0.0000001f)
            {
                if (Mathf.Abs(b) <= 0.0000001f) return false;
                float linearDistance = -c / b;
                if (linearDistance <= 0f || float.IsNaN(linearDistance) ||
                    float.IsInfinity(linearDistance)) return false;
                point = ray.GetPoint(linearDistance);
                return true;
            }
            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f) return false;

            float sqrt = Mathf.Sqrt(discriminant);
            float denominator = 2f * a;
            float first = (-b - sqrt) / denominator;
            float second = (-b + sqrt) / denominator;
            float distance = first > 0f ? first : second;
            if (distance <= 0f || float.IsNaN(distance) || float.IsInfinity(distance)) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        /// <summary>把世界坐标映射回当前地图的底层 Hex 索引，供选择框和玩法层复用。</summary>
        public bool TryGetCellIndexAtWorldPosition(Vector3 worldPosition, out int cellIndex)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return TerrainMeshGenerator.TryGetCellIndex(map, local, out cellIndex);
        }

        /// <summary>销毁当前生成的地形网格、材质与 chunk 对象。</summary>
        public void Clear()
        {
            RestoreAtmosphereState();
            SetCurveState(Vector3.zero, 0f);
            foreach (GameObject chunk in farChunkObjects)
            {
                if (chunk != null) DestroyOwned(chunk);
            }
            farChunkObjects.Clear();
            foreach (GameObject chunk in nearChunkObjects)
            {
                if (chunk != null) DestroyOwned(chunk);
            }
            nearChunkObjects.Clear();

            foreach (Mesh mesh in ownedMeshes) DestroyOwned(mesh);
            ownedMeshes.Clear();

            foreach (Material material in ownedMaterials) DestroyOwned(material);
            ownedMaterials.Clear();
            map = null;
            farSurfaceHeights = null;
            nearSurfaceHeights = null;
            farSurfaceHeightAt = null;
            nearSurfaceHeightAt = null;
            ActiveAppearance = TerrainMeshAppearance.Default;
            ActiveContinuousSurface = false;
            activeSurfaceHeights = null;
            activeSurfaceHeightAt = null;
            activeSurfaceMap = null;
        }

        /// <summary>将主相机切换为透视俯视角（2.5D），自动对准地图中心并可按字段微调。</summary>
        public void FitCamera()
        {
            Camera camera = Camera.main;
            if (camera == null || map == null) return;
            camera.orthographic = false;
            camera.fieldOfView = Mathf.Clamp(cameraFieldOfViewDegrees, 20f, 70f);
            float width = (map.width + 0.5f) * Mathf.Sqrt(3f);
            float height = map.height * 1.5f;
            cameraPivot = InitialPivot(width, height);
            float fitDistance = Mathf.Max(width, height) * Mathf.Max(0.5f, cameraDistanceFactor);
            maxCameraDistance = fitDistance * Mathf.Max(1f, maxZoomOutFactor);
            float nearDistance = ZoomDistanceForVisibleHexes(camera, minVisibleHexes);
            CalibrateHeightCurve(nearDistance * 0.9f, maxCameraDistance);
            currentZoom = zoomLevel;
            zoomVelocity = 0f;
            targetPivot = cameraPivot;
            ClampPivotToMap();
            cameraPivot = targetPivot;
            focusVelocity = Vector3.zero;
            cameraDistance = heightCurve.Evaluate(currentZoom);
            ApplyCameraTransform(camera);
        }

        private void CalibrateHeightCurve(float nearDistance, float farDistance)
        {
            if (heightCurve == null || heightCurve.keys.Length < 3)
            {
                heightCurve = new AnimationCurve(
                    new Keyframe(0f, nearDistance),
                    new Keyframe(0.45f, nearDistance + (farDistance - nearDistance) * 0.18f),
                    new Keyframe(1f, farDistance));
                return;
            }
            Keyframe[] keys = heightCurve.keys;
            keys[0].value = nearDistance;
            keys[keys.Length - 1].value = farDistance;
            if (keys.Length >= 2)
                keys[keys.Length / 2].value = nearDistance + (farDistance - nearDistance) * 0.18f;
            heightCurve.keys = keys;
        }

        private Vector3 InitialPivot(float mapWidth, float mapHeight)
        {
            if (map?.cells != null && initialFocusCellIndex >= 0 &&
                initialFocusCellIndex < map.cells.Length && map.cells[initialFocusCellIndex] != null)
            {
                Vector2 center = TerrainMeshGenerator.HexCenter(map.cells[initialFocusCellIndex].coord);
                return new Vector3(center.x, 0f, center.y);
            }
            return new Vector3(mapWidth * 0.5f, 0f, mapHeight * 0.5f);
        }

        private void Update()
        {
            Camera camera = Camera.main;
            if (camera == null || map == null) return;
            HandleZoom(camera);
            HandlePan(camera);
            currentZoom = Mathf.SmoothDamp(currentZoom, zoomLevel, ref zoomVelocity, zoomSmoothTime);
            cameraDistance = heightCurve.Evaluate(currentZoom);
            cameraPivot = Vector3.SmoothDamp(cameraPivot, targetPivot, ref focusVelocity,
                panSmoothTime, Mathf.Infinity, Time.deltaTime);
            ApplyCameraTransform(camera);
            RefreshDetailLevel();
        }

        private void RefreshDetailLevel()
        {
            WorldMap3DZoomTier tier = (WorldMap3DZoomTier)(int)CurrentDetailLevel;
            if (tier == activeTier) return;
            ApplyTier(tier);
        }

        private void RefreshTier(Camera camera)
        {
            WorldMap3DZoomTier tier = WorldMap3DPresentationPolicy.GetZoomTier(
                TerrainPresentationModels.VisibleHexesAcross(camera));
            if (tier == activeTier) return;
            ApplyTier(tier);
        }

        /// <summary>按缩放档位切换远近地表集合；陆地几何保持共面。</summary>
        public void ApplyTier(WorldMap3DZoomTier tier)
        {
            activeTier = tier;
            ActiveDetailLevel = CurrentDetailLevel;
            foreach (GameObject chunk in farChunkObjects)
            {
                if (chunk != null) chunk.SetActive(tier == WorldMap3DZoomTier.Far);
            }
            foreach (GameObject chunk in nearChunkObjects)
            {
                if (chunk != null) chunk.SetActive(tier != WorldMap3DZoomTier.Far);
            }
            ActiveAppearance = tier == WorldMap3DZoomTier.Far
                ? farAppearance
                : nearAppearance;
            activeSurfaceHeights = tier == WorldMap3DZoomTier.Far
                ? farSurfaceHeights
                : nearSurfaceHeights;
            activeSurfaceHeightAt = tier == WorldMap3DZoomTier.Far
                ? farSurfaceHeightAt
                : nearSurfaceHeightAt;
            ApplyTextureTier(tier);
        }

        private void ApplyTextureTier(WorldMap3DZoomTier tier)
        {
            // 远景只显示基础地形色块，不显示细节纹理与法线。
            float microWeight = tier == WorldMap3DZoomTier.Near ? 1f :
                tier == WorldMap3DZoomTier.Mid ? 0.72f : 0f;
            float macroWeight = tier == WorldMap3DZoomTier.Near ? 0.55f :
                tier == WorldMap3DZoomTier.Mid ? 0.85f : 0f;
            float colorWeight = tier == WorldMap3DZoomTier.Near ? 1f :
                tier == WorldMap3DZoomTier.Mid ? 0.75f : 0f;
            float normalWeight = tier == WorldMap3DZoomTier.Near ? 1f :
                tier == WorldMap3DZoomTier.Mid ? 0.48f : 0f;
            for (int submesh = 0; submesh < ownedMaterials.Count &&
                 submesh < TerrainMeshGenerator.SubmeshCount; submesh++)
            {
                Material material = ownedMaterials[submesh];
                if (material == null) continue;
                LandformType landform = TerrainMaterialProvider.RepresentativeLandform(submesh);
                bool textured = climateDebugView == WorldMapClimateDebugView.Normal &&
                                !TerrainMaterialProvider.IsWaterGroup(landform);
                if (material.HasProperty("_UseTerrainBlend"))
                    material.SetFloat("_UseTerrainBlend",
                        tier == WorldMap3DZoomTier.Far ? 0f :
                        material.HasProperty("_UseTerrainBlend") &&
                        useContinuousTerrainSurface && blendContinuousTerrainMaterials &&
                        landform == LandformType.Plain ? 1f : 0f);
                if (material.HasProperty("_TextureStrength"))
                    material.SetFloat("_TextureStrength", textured
                        ? groundTextureStrength * microWeight : 0f);
                if (material.HasProperty("_MacroStrength"))
                    material.SetFloat("_MacroStrength", textured
                        ? Mathf.Clamp(groundMacroStrength * macroWeight, 0f, 0.35f) : 0f);
                if (material.HasProperty("_TextureColorBlend"))
                    material.SetFloat("_TextureColorBlend", textured
                        ? groundTextureColorBlend * colorWeight : 0f);
                if (material.HasProperty("_TerrainNormalStrength"))
                    material.SetFloat("_TerrainNormalStrength", textured
                        ? groundNormalStrength * normalWeight : 0f);
            }
        }

        /// <summary>鼠标滚轮缩放：归一化 zoom 0=最近、1=最远；两端更钝，避免跳过中景。</summary>
        private void HandleZoom(Camera camera)
        {
            if (pointerInputBlocked) return;
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) < 0.01f) return;
            float sensitivity = zoomSpeed;
            if (zoomLevel < 0.2f || zoomLevel > 0.8f) sensitivity *= 0.6f;
            zoomLevel = Mathf.Clamp01(zoomLevel - wheel * sensitivity * 3f);
        }

        /// <summary>仅 WASD 平移；写入平滑焦点。</summary>
        private void HandlePan(Camera camera)
        {
            if (pointerInputBlocked) return;

            Vector3 keyboardInput = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) keyboardInput.z += 1f;
            if (Input.GetKey(KeyCode.S)) keyboardInput.z -= 1f;
            if (Input.GetKey(KeyCode.A)) keyboardInput.x -= 1f;
            if (Input.GetKey(KeyCode.D)) keyboardInput.x += 1f;
            if (keyboardInput.sqrMagnitude < 0.01f) return;

            Vector3 forward = camera.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = camera.transform.right;
            right.y = 0f;
            right.Normalize();
            Vector3 move = (forward * keyboardInput.z + right * keyboardInput.x).normalized;
            float heightFactor = Mathf.Max(0.25f, cameraDistance / 50f);
            targetPivot += move * keyboardPanSpeed * heightFactor * Time.deltaTime;
            ClampPivotToMap();
        }

        /// <summary>把目标焦点限制在地图范围内，Y 跟随当前表现地表高度。</summary>
        private void ClampPivotToMap()
        {
            if (map == null) return;
            float width = (map.width + 0.5f) * Mathf.Sqrt(3f);
            float height = map.height * 1.5f;
            targetPivot.x = Mathf.Clamp(targetPivot.x, 0f, width);
            targetPivot.z = Mathf.Clamp(targetPivot.z, 0f, height);
            targetPivot.y = GroundHeightAt(targetPivot.x, targetPivot.z) + focusHeightOffset;
        }

        private float GroundHeightAt(float x, float z)
        {
            if (map?.cells == null) return 0f;
            if (!TerrainMeshGenerator.TryGetCellIndex(map, new Vector3(x, 0f, z), out int index) ||
                index < 0 || index >= map.cells.Length || map.cells[index] == null)
                return 0f;
            return PresentationSurfaceHeight(map, map.cells[index]);
        }

        /// <summary>
        /// 根据相机 FOV 与宽高比，计算屏幕横向可见 hexesAcross 格时需要的相机距离。
        /// 视角绕 X 轴俯仰，横向不随俯仰角拉伸，因此可直接用水平 FOV 换算。
        /// </summary>
        private float ZoomDistanceForVisibleHexes(Camera camera, float hexesAcross)
        {
            float halfHorizontalFov = Mathf.Atan(
                Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * camera.aspect);
            Vector3 screenRight = Quaternion.Euler(0f, cameraYawDegrees, 0f) * Vector3.right;
            float projectedHexWidth = TerrainMeshGenerator.ProjectedHexWidth(screenRight);
            return Mathf.Max(1f, hexesAcross * projectedHexWidth /
                                  (2f * Mathf.Tan(halfHorizontalFov)));
        }

        private float VisibleHexesForCurrentCamera(Camera camera)
        {
            float minDistance = ZoomDistanceForVisibleHexes(camera, minVisibleHexes);
            return minVisibleHexes * cameraDistance / Mathf.Max(0.0001f, minDistance);
        }

        private void SetFieldOfViewPreservingCoverage(Camera camera, float fieldOfView,
            float visibleHexes)
        {
            camera.fieldOfView = Mathf.Clamp(fieldOfView, 20f, 70f);
            float minDistance = ZoomDistanceForVisibleHexes(camera, minVisibleHexes);
            cameraDistance = Mathf.Clamp(ZoomDistanceForVisibleHexes(camera, visibleHexes),
                minDistance, maxCameraDistance);
        }

        private void ApplyCameraTransform(Camera camera)
        {
            // Civ VI 风格：FOV 固定，高度与俯仰由归一化 zoom 的曲线驱动。
            camera.fieldOfView = Mathf.Clamp(cameraFieldOfViewDegrees, 20f, 70f);
            float visibleHexes = VisibleHexesForCurrentCamera(camera);
            ActiveFieldOfViewDegrees = camera.fieldOfView;
            ActiveVisibleHexesAcross = visibleHexes;
            float pitchDegrees = pitchCurve.Evaluate(currentZoom);
            float zoomT = currentZoom;
            float pitchRadians = Mathf.Clamp(pitchDegrees, 20f, 90f) * Mathf.Deg2Rad;
            Vector3 baseOffset = new Vector3(0f, Mathf.Sin(pitchRadians),
                -Mathf.Cos(pitchRadians)) * cameraDistance;
            Vector3 offset = Quaternion.Euler(0f, cameraYawDegrees, 0f) * baseOffset;
            camera.transform.position = cameraPivot + offset;
            camera.transform.rotation = Quaternion.LookRotation(cameraPivot - camera.transform.position);
            // 近景禁用地面弯曲；中远景仅保留极轻的大气曲率。
            float curvatureWeight = currentZoom < curvatureNearZoomThreshold ? 0f :
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(curvatureNearZoomThreshold, 1f, currentZoom));
            SetCurveState(cameraPivot, nearRadialCurvature * curvatureWeight);
            ApplyHorizonAtmosphere(zoomT, Mathf.Sin(pitchRadians) * cameraDistance);
        }

        internal float CameraPitchForVisibleHexes(float visibleHexes)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(minVisibleHexes,
                Mathf.Max(minVisibleHexes + 0.01f, cameraCurveMaxVisibleHexes), visibleHexes));
            return Mathf.Lerp(cameraPitchDegrees, cameraPitchFarDegrees, t);
        }

        internal float FieldOfViewForVisibleHexes(float visibleHexes)
        {
            return Mathf.Lerp(cameraFieldOfViewDegrees, nearFieldOfViewDegrees,
                PerspectiveWeightForVisibleHexes(visibleHexes));
        }

        internal static float PerspectiveWeightForVisibleHexes(float visibleHexes)
        {
            if (visibleHexes <= WorldMap3DPresentationPolicy.MidViewMinHexes) return 1f;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                WorldMap3DPresentationPolicy.MidViewMinHexes,
                WorldMap3DPresentationPolicy.FarViewMinHexes, visibleHexes));
            return 1f - t;
        }

        internal static float CurveWeightForVisibleHexes(float visibleHexes)
        {
            if (visibleHexes <= WorldMap3DPresentationPolicy.MidViewMinHexes) return 1f;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                WorldMap3DPresentationPolicy.MidViewMinHexes,
                WorldMap3DPresentationPolicy.FarViewMinHexes, visibleHexes));
            return 1f - t;
        }

        internal Vector3 CurveWorldPosition(Vector3 flatWorldPosition)
        {
            Vector2 delta = new Vector2(flatWorldPosition.x - activeCurveOrigin.x,
                flatWorldPosition.z - activeCurveOrigin.z);
            flatWorldPosition.y -= activeCurveStrength * delta.sqrMagnitude;
            return flatWorldPosition;
        }

        internal Vector3 CurveWorldNormal(Vector3 flatWorldPosition)
        {
            Vector3 delta = flatWorldPosition - activeCurveOrigin;
            return new Vector3(2f * activeCurveStrength * delta.x, 1f,
                2f * activeCurveStrength * delta.z).normalized;
        }

        private void SetCurveState(Vector3 origin, float strength)
        {
            strength = Mathf.Max(0f, strength);
            if ((activeCurveOrigin - origin).sqrMagnitude > 0.000001f ||
                !Mathf.Approximately(activeCurveStrength, strength))
                curveRevision++;
            activeCurveOrigin = origin;
            activeCurveStrength = strength;
            Shader.SetGlobalVector("_WorldMapCurveOrigin", activeCurveOrigin);
            Shader.SetGlobalFloat("_WorldMapCurveStrength", activeCurveStrength);
        }

        private void ApplyHorizonAtmosphere(float zoomT, float cameraHeight)
        {
            CaptureAtmosphereState();
            float startFactor = Mathf.Lerp(nearFogStartHeightFactor, farFogStartHeightFactor, zoomT);
            float spanFactor = Mathf.Lerp(nearFogSpanHeightFactor, farFogSpanHeightFactor, zoomT);
            float fogStart = Mathf.Max(1f, cameraHeight * startFactor);
            float fogEnd = fogStart + Mathf.Max(1f, cameraHeight * spanFactor);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = horizonFogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            // 大气透视：远处地表颜色/对比向灰青色靠拢。
            Shader.SetGlobalFloat("_TerrainDistanceFadeStart", fogStart * 0.75f);
            Shader.SetGlobalFloat("_TerrainDistanceFadeEnd", fogEnd);
            Shader.SetGlobalColor("_TerrainDistanceFadeColor", horizonFogColor);
            Shader.SetGlobalFloat("_TerrainDistanceFadeStrength", groundAtmosphereStrength);
        }

        private void CaptureAtmosphereState()
        {
            if (atmosphereStateCaptured) return;
            atmosphereStateCaptured = true;
            previousFogEnabled = RenderSettings.fog;
            previousFogMode = RenderSettings.fogMode;
            previousFogColor = RenderSettings.fogColor;
            previousFogDensity = RenderSettings.fogDensity;
            previousFogStartDistance = RenderSettings.fogStartDistance;
            previousFogEndDistance = RenderSettings.fogEndDistance;
        }

        private void RestoreAtmosphereState()
        {
            if (!atmosphereStateCaptured) return;
            RenderSettings.fog = previousFogEnabled;
            RenderSettings.fogMode = previousFogMode;
            RenderSettings.fogColor = previousFogColor;
            RenderSettings.fogDensity = previousFogDensity;
            RenderSettings.fogStartDistance = previousFogStartDistance;
            RenderSettings.fogEndDistance = previousFogEndDistance;
            atmosphereStateCaptured = false;
        }

        private void OnDisable()
        {
            RestoreAtmosphereState();
            SetCurveState(Vector3.zero, 0f);
        }

        private void OnDestroy()
        {
            RestoreAtmosphereState();
            Clear();
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
