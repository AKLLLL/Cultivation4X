using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// SampleScene 的 3D 世界地图总控制器（表现组件，不是全局 Manager）。
    /// 职责被拆成三块：RenderPipeline 只渲染显式输入；InteractionController 只做
    /// 选格输入；HudController 只做 HUD 展示与回调。本组件只负责：
    /// 生命周期、事件胶水、数据准备与玩法命令转发。
    /// 未来 MapSnapshot 落地时，仅本类的数据准备代码需要换成快照读取。
    /// </summary>
    public sealed class WorldMap3DController : MonoBehaviour
    {
        [SerializeField] private WorldMapRenderPipeline renderPipeline;
        [SerializeField] private WorldMapInteractionController interaction;
        [SerializeField] private WorldMapHudController hud;
        private bool foundingTransitionPending;
        private static int gameFlowDiagSequence;
        private TerrainRenderer.MapDetailLevel lastSelectionDetailLevel =
            TerrainRenderer.MapDetailLevel.Mid;

        public WorldMapViewMode CurrentWorldMapViewMode =>
            IsSiteSelectionMode ? WorldMapViewMode.SectPlacement :
            ResolvedFlowState == GameFlowState.WorldMap ? WorldMapViewMode.WorldExplore :
            WorldMapViewMode.WorldExplore;

        /// <summary>最近一次点击格解析出的 WorldLocation；没有地点时为 null。</summary>
        public WorldLocation SelectedLocation { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (MapTestBootstrap.IsTestScene) return;
            if (FindObjectOfType<WorldMap3DController>() != null) return;
            GameObject prefab = Resources.Load<GameObject>("Prefab/WorldMap3D");
            if (prefab == null)
            {
                Debug.LogWarning("未找到 Resources/Prefab/WorldMap3D.prefab，跳过 3D 地图接入。");
                return;
            }
            Instantiate(prefab);
        }

        private void Awake()
        {
            if (renderPipeline == null) renderPipeline = GetComponentInChildren<WorldMapRenderPipeline>(true);
            if (interaction == null) interaction = GetComponentInChildren<WorldMapInteractionController>(true);
            if (hud == null) hud = GetComponentInChildren<WorldMapHudController>(true);
            if (hud != null)
            {
                hud.OnConfirmSite = ConfirmSite;
                hud.LocationActionRequested += HandleLocationAction;
                hud.LocationMissionsRequested += OpenLocationMissions;
                hud.CellInteractionRequested += HandleCellInteraction;
            }
            if (interaction != null) interaction.CellPicked += HandleCellPicked;
        }

        private IEnumerator Start()
        {
            while (SaveManager.Instance == null || !SaveManager.Instance.IsInitializationComplete)
                yield return null;
            while (GameFlowStateManager.Instance == null)
                yield return null;
            GameFlowStateManager.Instance.Refresh();
            if (PlayerManager.Instance != null)
                PlayerManager.Instance.OnFoundingChanged += HandleGameplayChanged;
            WorldMapSession.ProgressChanged += HandleGameplayChanged;
            GameFlowStateManager.Instance.StateChanged += HandleFlowStateChanged;
            ApplyCurrentWorld();
        }

        private void Update()
        {
            WorldMap map = WorldMapSession.Current;
            if (map?.cells == null || interaction == null) return;
            if (!IsWorldMapActive && !IsSiteSelectionMode) return;

            bool panelOpen = UIManager.Instance != null && UIManager.Instance.HasOpenPanels;
            if (renderPipeline != null && renderPipeline.TerrainRenderer != null)
                renderPipeline.TerrainRenderer.SetPointerInputBlocked(panelOpen);

            bool overUi = EventSystem.current != null &&
                          EventSystem.current.IsPointerOverGameObject();
            bool overMapHud = hud != null && hud.IsPointerOverHudControl();
            bool allowClick = IsSiteSelectionMode ? !overMapHud : !overUi;
            interaction.UpdateInput(map, allowClick);
        }

        private GameFlowState ResolvedFlowState
        {
            get
            {
                if (GameFlowStateManager.Instance != null)
                    return GameFlowStateManager.Instance.Current;
                // 测试或兼容环境没有 GameFlowStateManager 时，退回 FoundingStage 推导；
                // 连 PlayerManager 都没有时保持旧测试语义：直接按世界地图处理。
                if (PlayerManager.Instance == null) return GameFlowState.WorldMap;
                return GameFlowStateManager.StateForFounding(
                    PlayerManager.Instance?.playerData?.founding);
            }
        }

        private bool IsWorldMapActive =>
            ResolvedFlowState == GameFlowState.WorldMap;

        private bool IsSiteSelectionMode =>
            ResolvedFlowState == GameFlowState.SectPlacement;

        private bool HasSectBase =>
            WorldMapProgressRules.GetSectBase(WorldMapSession.Progress) != null;

        private bool RevealAll
        {
            get
            {
                FoundingState founding = PlayerManager.Instance?.playerData?.founding;
                return founding == null || !FoundingRules.HasReachedCave(founding);
            }
        }

        private void HandleCellPicked(int cellIndex)
        {
            SelectedLocation = ResolveLocationAt(cellIndex);
            RefreshPresentation();
        }

        private void OpenLocationMissions(WorldLocation location)
        {
            if (location == null) return;
            MissionPanel panel = FindObjectOfType<MissionPanel>(true);
            if (panel == null)
            {
                GameDebugConfig.LogWorldMapWarning("MissionPanel 尚未初始化");
                return;
            }
            panel.OpenLocationMissions(location);
        }

        private void HandleLocationAction(WorldLocation location, LocationAction action)
        {
            if (action == null) return;
            switch (action.actionType)
            {
                case LocationActionType.Explore:
                    OpenSelectedAction(MapActionType.Explore);
                    break;
                case LocationActionType.ManageLabor:
                {
                    VillageLaborPanel panel = FindObjectOfType<VillageLaborPanel>(true);
                    if (panel != null) panel.Open(location);
                    else GameDebugConfig.LogWorldMapWarning("VillageLaborPanel 尚未初始化");
                    break;
                }
                case LocationActionType.ViewStatus:
                    GameDebugConfig.LogWorldMap($"[WorldLocation] ViewStatus location={location?.name}");
                    break;
                case LocationActionType.ManageSect:
                    SectWorldInterface.Instance?.OpenSectLayout();
                    break;
                case LocationActionType.DevelopResourceNode:
                    OpenSelectedAction(MapActionType.DevelopResourceNode);
                    break;
                default:
                    GameDebugConfig.LogWorldMap($"[WorldLocation] action requested " +
                          $"location={location?.name} action={action?.displayName}");
                    break;
            }
        }

        private void HandleCellInteraction(CellInteractionOption option)
        {
            if (option == null) return;
            switch (option.optionType)
            {
                case CellInteractionOptionType.Explore:
                    OpenSelectedAction(MapActionType.Explore);
                    break;
                default:
                    GameDebugConfig.LogWorldMap($"[CellInteraction] 未支持的格子交互: {option.id}");
                    break;
            }
        }

        private WorldLocation ResolveLocationAt(int cellIndex)
        {
            WorldMap map = WorldMapSession.Current;
            if (map?.cells == null || cellIndex < 0 || cellIndex >= map.cells.Length)
                return null;
            return map.GetLocationAt(map.cells[cellIndex]);
        }

        private void HandleGameplayChanged()
        {
            if (foundingTransitionPending) return;
            RefreshPresentation();
        }
        private void HandleFlowStateChanged(GameFlowState state)
        {
            gameFlowDiagSequence++;
            GameDebugConfig.LogWorldMap($"[GameFlowDiag][{gameFlowDiagSequence}] " +
                      $"WorldMap3DController.HandleFlowStateChanged({state})");
            RefreshPresentation();
        }

        public void ApplyCurrentWorld()
        {
            if (renderPipeline == null) return;
            WorldMap map = WorldMapSession.Current;
            WorldMapProgressState progress = WorldMapSession.Progress;
            if (map?.cells == null)
            {
                // MainMenu 阶段可能还没有 WorldMap；仍必须按 GameState 启停 UI。
                RefreshFlowUiOnly();
                return;
            }
            if (progress == null)
            {
                progress = new WorldMapProgressState();
                WorldMapSession.Set(map, progress);
            }
            PrepareProgressData(map, progress);
            RefreshFromFlowState(map, progress);
        }

        public void RefreshPresentation()
        {
            if (renderPipeline == null) return;
            WorldMap map = WorldMapSession.Current;
            WorldMapProgressState progress = WorldMapSession.Progress;
            if (map?.cells == null)
            {
                RefreshFlowUiOnly();
                return;
            }
            if (progress == null)
            {
                progress = new WorldMapProgressState();
                WorldMapSession.Set(map, progress);
            }
            PrepareProgressData(map, progress);
            RefreshFromFlowState(map, progress);
        }

        private void RefreshFlowUiOnly()
        {
            if (GameFlowStateManager.Instance != null) GameFlowStateManager.Instance.Refresh();
            gameFlowDiagSequence++;
            GameDebugConfig.LogWorldMap($"[GameFlowDiag][{gameFlowDiagSequence}] RefreshFlowUiOnly " +
                      $"flowState={ResolvedFlowState} mapIsNull={WorldMapSession.Current?.cells == null}");
            switch (ResolvedFlowState)
            {
                case GameFlowState.SectPlacement:
                    // 尚无 WorldMap 时无法初始化选址渲染，只确保世界 UI 关闭。
                    ShowCharacterSetupUiState();
                    break;
                case GameFlowState.WorldMap:
                    if (WorldMapSession.Current?.cells != null) break;
                    ShowCharacterSetupUiState();
                    break;
                default:
                    ShowCharacterSetupUiState();
                    break;
            }
        }

        private void RefreshFromFlowState(WorldMap map, WorldMapProgressState progress)
        {
            if (GameFlowStateManager.Instance != null) GameFlowStateManager.Instance.Refresh();
            gameFlowDiagSequence++;
            GameDebugConfig.LogWorldMap($"[GameFlowDiag][{gameFlowDiagSequence}] RefreshFromFlowState " +
                      $"flowState={ResolvedFlowState} mapCells={(map?.cells != null ? map.cells.Length : 0)}");
            switch (ResolvedFlowState)
            {
                case GameFlowState.MainMenu:
                case GameFlowState.CharacterSetup:
                    ShowCharacterSetupUiState();
                    break;
                case GameFlowState.SectPlacement:
                    ShowPlacementUiState(map, progress);
                    break;
                default:
                    ShowWorldMapUiState(map, progress);
                    break;
            }
        }

        /// <summary>MainMenu / CharacterSetup 共用：关闭世界表现与 HUD，只保留立宗面板。</summary>
        private void ShowCharacterSetupUiState()
        {
            LogGameFlowUiStep("ShowCharacterSetupUiState", "enter");
            if (renderPipeline != null)
            {
                renderPipeline.SetSectPlacementMode(false);
                renderPipeline.SetPresentationsActive(false);
            }
            if (renderPipeline != null && renderPipeline.TerrainRenderer != null)
                renderPipeline.TerrainRenderer.SetSectPlacementCameraMode(false);
            if (interaction != null) interaction.gameObject.SetActive(false);
            if (hud != null)
            {
                hud.SetVisible(false);
                hud.gameObject.SetActive(false);
            }
            SectWorldInterface.Instance?.SetUiVisible(false);
            SetEventUiVisible(false);
            LogGameFlowUiStep("ShowCharacterSetupUiState", "exit");
        }

        /// <summary>
        /// SectPlacement：复用 WorldMapRenderer 与 WorldMapData。隐藏数据覆盖层与正式 HUD，
        /// 只保留地形/Hex Grid 和精简选址 HUD；相机切换为接近垂直的观察模式。
        /// </summary>
        private void ShowPlacementUiState(WorldMap map, WorldMapProgressState progress)
        {
            LogGameFlowUiStep("ShowPlacementUiState", "enter");
            if (renderPipeline == null) return;
            if (renderPipeline.CurrentMap != map)
            {
                renderPipeline.SetSectPlacementMode(true);
                // 选址相机只对准整张地图中心，不自动聚焦推荐格。
                renderPipeline.ApplyMap(map, progress, PlacementCameraFocusCell(map), false);
            }
            else
            {
                renderPipeline.SetSectPlacementMode(true);
                renderPipeline.RefreshDynamicLayers(map, progress, false);
            }
            if (renderPipeline.TerrainRenderer != null)
                renderPipeline.TerrainRenderer.SetSectPlacementCameraMode(true);
            if (interaction != null) interaction.gameObject.SetActive(true);
            if (hud != null)
            {
                hud.gameObject.SetActive(true);
                hud.SetVisible(true);
                hud.Refresh(map, progress, interaction != null ? interaction.SelectedCellIndex : -1,
                    true, false, false, PlayerManager.Instance?.playerData);
            }
            SectWorldInterface.Instance?.SetUiVisible(false);
            SetEventUiVisible(false);
            if (UIManager.Instance != null) UIManager.Instance.CloseAllPanels();
            LogGameFlowUiStep("ShowPlacementUiState", "exit");
        }

        /// <summary>WorldMap：恢复 Civ6 相机、正式 HUD、资源栏与探索覆盖层。</summary>
        private void ShowWorldMapUiState(WorldMap map, WorldMapProgressState progress)
        {
            LogGameFlowUiStep("ShowWorldMapUiState", "enter");
            if (renderPipeline != null)
            {
                renderPipeline.SetSectPlacementMode(false);
                if (renderPipeline.TerrainRenderer != null)
                    renderPipeline.TerrainRenderer.SetSectPlacementCameraMode(false);
                renderPipeline.SetPresentationsActive(true);
            }
            if (interaction != null) interaction.gameObject.SetActive(true);
            if (hud != null)
            {
                hud.gameObject.SetActive(true);
                hud.SetVisible(true);
            }
            SectWorldInterface.Instance?.SetUiVisible(true);
            SetEventUiVisible(true);
            ApplyWorldView(map, progress);
            LogGameFlowUiStep("ShowWorldMapUiState", "exit");
        }

        private static void SetEventUiVisible(bool visible)
        {
            CharacterEventPanel eventUi = FindObjectOfType<CharacterEventPanel>(true);
            if (eventUi != null) eventUi.SetUiVisible(visible);
        }

        private void LogGameFlowUiStep(string method, string phase)
        {
            gameFlowDiagSequence++;
            GameDebugConfig.LogWorldMap($"[GameFlowDiag][{gameFlowDiagSequence}] {method}.{phase} " +
                      $"flowState={ResolvedFlowState} worldViewMode={CurrentWorldMapViewMode}");
            foreach (WorldMapHudController hudController in
                     UnityEngine.Object.FindObjectsOfType<WorldMapHudController>(true))
            {
                GameDebugConfig.LogWorldMap($"[GameFlowDiag][{gameFlowDiagSequence}] WorldHUD hierarchy: " +
                          $"instance={hudController.name} " +
                          $"activeSelf={hudController.gameObject.activeSelf} " +
                          $"activeInHierarchy={hudController.gameObject.activeInHierarchy} " +
                          $"canvas={hudController.HudCanvasName} " +
                          $"canvasActive={(hudController.HudCanvas != null && hudController.HudCanvas.gameObject.activeSelf)}");
            }
        }

        private void ApplyWorldView(WorldMap map, WorldMapProgressState progress)
        {
            if (renderPipeline.CurrentMap != map)
            {
                renderPipeline.ApplyMap(map, progress, InitialFocusCell(map), RevealAll);
            }
            else
            {
                renderPipeline.RefreshDynamicLayers(map, progress, RevealAll);
                if (renderPipeline.TerrainRenderer != null)
                    lastSelectionDetailLevel = TerrainRenderer.ActiveDetailLevel;
                renderPipeline.RefreshSelection(interaction != null ? interaction.SelectedCellIndex : -1,
                    false);
            }

            if (hud != null)
                hud.Refresh(map, progress, interaction != null ? interaction.SelectedCellIndex : -1,
                    false, HasSectBase, RevealAll, PlayerManager.Instance?.playerData);
        }

        private static void PrepareProgressData(WorldMap map, WorldMapProgressState progress)
        {
            WorldMapContentRules.EnsureCandidates(map, progress);
            WorldMapInfluenceRules.EnsureCurrent(map, progress);
            WorldMapContentRules.RefreshHints(map, progress);
        }

        private int InitialFocusCell(WorldMap map)
        {
            int selected = PlayerManager.Instance?.playerData?.founding?.selectedWorldCellIndex ?? -1;
            if (selected >= 0 && selected < map.cells.Length && map.cells[selected] != null)
                return selected;
            // 与 TerrainTest 一致：未选址新档用战略焦点格展示山体/水面构图，
            // 而不是地图中心（中心可能是海面或平坦低地，看起来像“地形失效”）。
            return WorldMap3DPresentationPolicy.SelectInitialFocusCell(map);
        }

        private static int PlacementCameraFocusCell(WorldMap map)
        {
            int center = map.GetIndex(new HexCoord(map.width / 2, map.height / 2));
            if (center >= 0 && center < map.cells.Length && map.cells[center] != null)
                return center;
            return 0;
        }

        private void ConfirmSite()
        {
            if (PlayerManager.Instance == null || interaction == null ||
                interaction.SelectedCellIndex < 0 || foundingTransitionPending) return;
            int cellIndex = interaction.SelectedCellIndex;
            foundingTransitionPending = true;
            if (!PlayerManager.Instance.ConfirmWorldSite(cellIndex, out string reason))
            {
                foundingTransitionPending = false;
                Debug.LogWarning(reason);
                return;
            }
            if (hud != null) hud.ShowFoundingTransition(true);
            StartCoroutine(FinishFoundingTransition());
        }

        private System.Collections.IEnumerator FinishFoundingTransition()
        {
            yield return new WaitForSecondsRealtime(2.4f);
            foundingTransitionPending = false;
            if (hud != null) hud.ShowFoundingTransition(false);
            RefreshPresentation();
        }

        private void OpenSelectedAction(MapActionType actionType)
        {
            if (interaction == null || interaction.SelectedCellIndex < 0) return;
            MapSiteData site = WorldMapSession.Progress?.mapSites?.FirstOrDefault(item =>
                item != null && item.cellIndex == interaction.SelectedCellIndex &&
                item.siteType != MapSiteType.SectBase);
            if (actionType != MapActionType.Explore && site != null &&
                site.siteType != MapSiteType.SpiritSpring)
                actionType = WorldMapContentRules.ActionForSite(site);
            MapMissionContext context = new MapMissionContext
            {
                actionType = actionType,
                targetCellIndex = interaction.SelectedCellIndex,
                targetSiteId = actionType == MapActionType.Explore ? null : site?.siteId
            };
            MissionPanel panel = FindObjectOfType<MissionPanel>(true);
            if (panel == null)
            {
                Debug.LogWarning("找不到任务面板");
                return;
            }
            if (!panel.OpenMapMission(context, out string reason))
                Debug.LogWarning(reason);
        }

        private void OnDestroy()
        {
            if (interaction != null) interaction.CellPicked -= HandleCellPicked;
            if (hud != null)
            {
                hud.LocationActionRequested -= HandleLocationAction;
                hud.LocationMissionsRequested -= OpenLocationMissions;
                hud.CellInteractionRequested -= HandleCellInteraction;
            }
            if (PlayerManager.Instance != null)
                PlayerManager.Instance.OnFoundingChanged -= HandleGameplayChanged;
            if (GameFlowStateManager.Instance != null)
                GameFlowStateManager.Instance.StateChanged -= HandleFlowStateChanged;
            WorldMapSession.ProgressChanged -= HandleGameplayChanged;
            if (renderPipeline != null) renderPipeline.ClearAll();
        }
    }
}
