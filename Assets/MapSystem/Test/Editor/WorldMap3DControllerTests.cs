using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cultivation4X.WorldMap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class WorldMap3DControllerTests
{
    [TearDown]
    public void TearDown()
    {
        WorldMapSession.Clear();
        PlayerManager.Instance = null;
    }

    private static WorldMap CreateSmallMap(int seed = 7401)
    {
        return WorldGenerator.Generate(new MapGenerationSettings
        {
            width = 16,
            height = 16,
            seed = seed
        });
    }

    [Test]
    public void OldWorldMapPresenter_NoLongerRegistersRuntimeBootstrap()
    {
        MethodInfo bootstrap = typeof(WorldMapPresenter).GetMethod("Bootstrap",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNull(bootstrap, "旧 2D Presenter 不得再通过 RuntimeInitializeOnLoadMethod 自举");
    }

    [Test]
    public void RenderPipeline_AppliesExplicitKnowledgeInput()
    {
        WorldMap map = CreateSmallMap();
        var progress = new WorldMapProgressState
        {
            revealedCellIndices = new List<int> { 0, 1 },
            influenceSources = new List<InfluenceSourceData>
            {
                new InfluenceSourceData
                {
                    sourceId = "base", sourceType = InfluenceSourceType.SectBase,
                    cellIndex = 10, controllerSectId = "player_sect",
                    baseStrength = 100, radius = 1, isActive = true
                }
            },
            isInfluenceDirty = true
        };
        GameObject root = new GameObject("RenderPipelineTest");
        try
        {
            WorldMapRenderPipeline pipeline = root.AddComponent<WorldMapRenderPipeline>();
            pipeline.ApplyMap(map, progress, 0, false);
            HashSet<int> known = WorldMapInfluenceRules.CollectKnownCellIndices(map, progress, false);
            Assert.AreEqual(known.Count, pipeline.KnownCellCount);
            Assert.AreSame(map, pipeline.CurrentMap);

            pipeline.ApplyMap(map, progress, 0, true);
            Assert.AreEqual(map.cells.Length, pipeline.KnownCellCount);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void RenderPipeline_FirstApplyImmediatelyRendersHintMarker()
    {
        WorldMap map = CreateSmallMap(7402);
        var progress = new WorldMapProgressState
        {
            mapSites = new List<MapSiteData>
            {
                new MapSiteData
                {
                    siteId = "hint", siteName = "不应显示", siteType = MapSiteType.Ruin,
                    cellIndex = 1, revealState = MapContentRevealState.Hinted
                }
            }
        };
        GameObject root = new GameObject("InitialHintRenderTest");
        try
        {
            WorldMapRenderPipeline pipeline = root.AddComponent<WorldMapRenderPipeline>();
            MapIconRenderer icons = root.AddComponent<MapIconRenderer>();
            typeof(WorldMapRenderPipeline).GetField("iconRenderer",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(pipeline, icons);

            pipeline.ApplyMap(map, progress, 0, false);

            Assert.AreEqual(1, icons.IconCount,
                "首次 ApplyMap 必须同步刷新动态图标，不能等待后续进度变化");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void InteractionController_SelectCellRaisesEventOnce()
    {
        GameObject root = new GameObject("InteractionTest");
        try
        {
            WorldMapInteractionController interaction =
                root.AddComponent<WorldMapInteractionController>();
            int received = 0;
            interaction.CellPicked += index => received++;
            interaction.SelectCell(4);
            interaction.SelectCell(4);
            Assert.AreEqual(4, interaction.SelectedCellIndex);
            Assert.AreEqual(1, received);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void Controller_AppliesCurrentWorldAndRefreshesHud()
    {
        WorldMap map = CreateSmallMap();
        WorldMapSession.Set(map, new WorldMapProgressState());
        GameObject root = new GameObject("WorldMap3DControllerTest");
        try
        {
            GameObject pipelineObject = new GameObject("RenderPipeline");
            pipelineObject.transform.SetParent(root.transform, false);
            WorldMapRenderPipeline pipeline = pipelineObject.AddComponent<WorldMapRenderPipeline>();
            GameObject interactionObject = new GameObject("Interaction");
            interactionObject.transform.SetParent(root.transform, false);
            WorldMapInteractionController interaction =
                interactionObject.AddComponent<WorldMapInteractionController>();
            GameObject hudObject = new GameObject("HUD");
            hudObject.transform.SetParent(root.transform, false);
            WorldMapHudController hud = hudObject.AddComponent<WorldMapHudController>();

            WorldMap3DController controller = root.AddComponent<WorldMap3DController>();
            SetPrivateField(controller, "renderPipeline", pipeline);
            SetPrivateField(controller, "interaction", interaction);
            SetPrivateField(controller, "hud", hud);
            InvokePrivate(controller, "Awake");
            InvokePrivate(hud, "Awake");

            controller.ApplyCurrentWorld();
            controller.RefreshPresentation();

            Assert.AreSame(map, pipeline.CurrentMap);
            Assert.AreEqual(map.cells.Length, pipeline.KnownCellCount,
                "无玩家立宗状态时应按全知模式显示");
            Assert.IsFalse(hud.gameObject.GetComponentsInChildren<TMPro.TMP_Text>(true)
                    .Any(text => text != null && text.text.Contains("地图调试")),
                "正式游戏世界地图不得包含地图调试 UI");
            Assert.Greater(hud.gameObject.GetComponentsInChildren<TMPro.TMP_Text>(true).Length, 0,
                "HUD 应创建详情文本");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void Controller_SectPlacementReusesWorldRendererAndHidesDataOverlays()
    {
        WorldMap map = CreateSmallMap();
        WorldMapSession.Set(map, new WorldMapProgressState());
        GameObject playerObject = new GameObject("PlayerManager");
        PlayerManager player = playerObject.AddComponent<PlayerManager>();
        player.playerData = new PlayerData
        {
            founding = new FoundingState
            {
                initialized = true,
                completed = false,
                stage = FoundingStage.WorldSelection,
                selectedWorldCellIndex = -1
            }
        };
        PlayerManager.Instance = player;
        GameObject cameraObject = EnsureMainCamera();
        GameObject root = new GameObject("WorldMap3DControllerPlacementTest");
        try
        {
            GameObject pipelineObject = new GameObject("RenderPipeline");
            pipelineObject.transform.SetParent(root.transform, false);
            WorldMapRenderPipeline pipeline = pipelineObject.AddComponent<WorldMapRenderPipeline>();
            GameObject terrainObject = new GameObject("TerrainRenderer");
            terrainObject.transform.SetParent(root.transform, false);
            TerrainRenderer terrainRenderer = terrainObject.AddComponent<TerrainRenderer>();
            SetPrivateField(pipeline, "terrainRenderer", terrainRenderer);
            GameObject interactionObject = new GameObject("Interaction");
            interactionObject.transform.SetParent(root.transform, false);
            WorldMapInteractionController interaction =
                interactionObject.AddComponent<WorldMapInteractionController>();
            GameObject hudObject = new GameObject("HUD");
            hudObject.transform.SetParent(root.transform, false);
            WorldMapHudController hud = hudObject.AddComponent<WorldMapHudController>();

            WorldMap3DController controller = root.AddComponent<WorldMap3DController>();
            SetPrivateField(controller, "renderPipeline", pipeline);
            SetPrivateField(controller, "interaction", interaction);
            SetPrivateField(controller, "hud", hud);
            InvokePrivate(controller, "Awake");
            InvokePrivate(hud, "Awake");

            controller.ApplyCurrentWorld();

            Assert.AreEqual(WorldMapViewMode.SectPlacement, controller.CurrentWorldMapViewMode);
            Assert.AreSame(map, pipeline.CurrentMap);
            Assert.IsTrue(pipeline.SectPlacementMode);
            Assert.IsTrue(terrainRenderer.gameObject.activeSelf,
                "SectPlacement 必须复用 WorldMapRenderer");
            Assert.IsTrue(hud.gameObject.activeSelf, "选址阶段应显示精简 PlacementPanel");
            Assert.IsTrue(hud.HudCanvas != null && hud.HudCanvas.gameObject.activeSelf);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(playerObject);
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
        }
    }

    [Test]
    public void Controller_ConfirmPlacementSiteAdvancesStageAndReturnsToWorldView()
    {
        GameObject playerObject = new GameObject("PlayerManager");
        PlayerManager player = playerObject.AddComponent<PlayerManager>();
        PlayerManager.Instance = player;
        player.InitializeNewFoundingGame(7402);
        WorldMap map = WorldMapSession.Current;
        Assert.NotNull(map);
        GameObject npcObject = new GameObject("NPCManager");
        NPCManager npcs = npcObject.AddComponent<NPCManager>();
        NPCManager.Instance = npcs;
        var founders = player.playerData.founding.candidates.Take(3)
            .Select(candidate => candidate.candidateId).ToList();
        Assert.IsTrue(player.ConfirmFounderSelection(founders, out string founderReason), founderReason);
        Assert.IsTrue(player.SelectFoundingTechnique("qingmu", out string techniqueReason), techniqueReason);
        Assert.IsTrue(player.ConfirmSectFounding("测试宗", out string identityReason), identityReason);
        Assert.AreEqual(FoundingStage.WorldSelection, player.playerData.founding.stage);
        GameObject cameraObject = EnsureMainCamera();
        GameObject root = new GameObject("WorldMap3DControllerConfirmTest");
        try
        {
            GameObject pipelineObject = new GameObject("RenderPipeline");
            pipelineObject.transform.SetParent(root.transform, false);
            WorldMapRenderPipeline pipeline = pipelineObject.AddComponent<WorldMapRenderPipeline>();
            GameObject interactionObject = new GameObject("Interaction");
            interactionObject.transform.SetParent(root.transform, false);
            WorldMapInteractionController interaction =
                interactionObject.AddComponent<WorldMapInteractionController>();
            GameObject hudObject = new GameObject("HUD");
            hudObject.transform.SetParent(root.transform, false);
            WorldMapHudController hud = hudObject.AddComponent<WorldMapHudController>();

            WorldMap3DController controller = root.AddComponent<WorldMap3DController>();
            SetPrivateField(controller, "renderPipeline", pipeline);
            SetPrivateField(controller, "interaction", interaction);
            SetPrivateField(controller, "hud", hud);
            InvokePrivate(controller, "Awake");
            InvokePrivate(hud, "Awake");
            controller.ApplyCurrentWorld();
            Assert.AreEqual(WorldMapViewMode.SectPlacement, controller.CurrentWorldMapViewMode);

            int buildable = System.Array.FindIndex(map.cells,
                cell => cell != null && cell.isBuildable);
            Assert.GreaterOrEqual(buildable, 0);
            Assert.IsTrue(player.ConfirmWorldSite(buildable, out string reason), reason);
            controller.RefreshPresentation();

            Assert.AreEqual(FoundingStage.Cave,
                PlayerManager.Instance.playerData.founding.stage);
            Assert.AreEqual(WorldMapViewMode.WorldExplore, controller.CurrentWorldMapViewMode);
            Assert.IsFalse(pipeline.SectPlacementMode);
            Assert.IsTrue(pipeline.gameObject.activeSelf, "确认选址后应恢复 3D 世界地图");
            Assert.IsTrue(hud.gameObject.activeSelf, "确认选址后应恢复 3D HUD");
            Assert.AreSame(map, pipeline.CurrentMap);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(playerObject);
            Object.DestroyImmediate(npcObject);
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
        }
    }

    [Test]
    public void ClickedCell_ResolvesWorldLocationFromCellReference()
    {
        WorldMap map = WorldGenerator.Generate(new MapGenerationSettings
            { width = 32, height = 24, seed = 7001 });
        int sect = map.cells.First(cell => cell != null && cell.isBuildable &&
            cell.landform != LandformType.Mountain &&
            map.GetNeighborIndices(cell.index).Any(index => map.cells[index] != null &&
                map.cells[index].isBuildable && map.cells[index].landform != LandformType.Mountain)).index;
        WorldLocation village = WorldLocationRules.CreateStarterVillage(map, sect);
        Assert.NotNull(village);
        WorldMapSession.Set(map, new WorldMapProgressState());
        GameObject root = new GameObject("WorldLocationClickTest");
        try
        {
            GameObject pipelineObject = new GameObject("RenderPipeline");
            pipelineObject.transform.SetParent(root.transform, false);
            WorldMapRenderPipeline pipeline = pipelineObject.AddComponent<WorldMapRenderPipeline>();
            GameObject interactionObject = new GameObject("Interaction");
            interactionObject.transform.SetParent(root.transform, false);
            WorldMapInteractionController interaction =
                interactionObject.AddComponent<WorldMapInteractionController>();
            GameObject hudObject = new GameObject("HUD");
            hudObject.transform.SetParent(root.transform, false);
            WorldMapHudController hud = hudObject.AddComponent<WorldMapHudController>();

            WorldMap3DController controller = root.AddComponent<WorldMap3DController>();
            SetPrivateField(controller, "renderPipeline", pipeline);
            SetPrivateField(controller, "interaction", interaction);
            SetPrivateField(controller, "hud", hud);
            InvokePrivate(controller, "Awake");
            InvokePrivate(hud, "Awake");

            int villageCell = map.GetIndex(new HexCoord(village.position.x, village.position.y));
            interaction.SelectCell(villageCell);
            Assert.AreSame(village, controller.SelectedLocation);

            int emptyCell = System.Array.FindIndex(map.cells,
                cell => cell != null && string.IsNullOrEmpty(cell.locationId));
            interaction.SelectCell(emptyCell);
            Assert.IsNull(controller.SelectedLocation);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void GameFlowState_MapsFoundingStagesToFlow()
    {
        Assert.AreEqual(GameFlowState.MainMenu,
            GameFlowStateManager.StateForFounding(null));
        Assert.AreEqual(GameFlowState.CharacterSetup,
            GameFlowStateManager.StateForFounding(new FoundingState
            { initialized = true, stage = FoundingStage.CandidateSelection }));
        Assert.AreEqual(GameFlowState.CharacterSetup,
            GameFlowStateManager.StateForFounding(new FoundingState
            { initialized = true, stage = FoundingStage.TechniqueSelection }));
        Assert.AreEqual(GameFlowState.CharacterSetup,
            GameFlowStateManager.StateForFounding(new FoundingState
            { initialized = true, stage = FoundingStage.SectConfirmation }));
        Assert.AreEqual(GameFlowState.SectPlacement,
            GameFlowStateManager.StateForFounding(new FoundingState
            { initialized = true, stage = FoundingStage.WorldSelection }));
        Assert.AreEqual(GameFlowState.WorldMap,
            GameFlowStateManager.StateForFounding(new FoundingState
            { initialized = true, stage = FoundingStage.Cave }));
        Assert.AreEqual(GameFlowState.WorldMap,
            GameFlowStateManager.StateForFounding(new FoundingState
            { initialized = true, stage = FoundingStage.Completed }));
    }

    [Test]
    public void Controller_CharacterSetupHidesWorldAndPlacementUi()
    {
        WorldMap map = CreateSmallMap();
        WorldMapSession.Set(map, new WorldMapProgressState());
        GameObject flowObject = new GameObject("GameFlowStateManager");
        GameFlowStateManager flow = flowObject.AddComponent<GameFlowStateManager>();
        SetPropertyValue(flow, "Current", GameFlowState.CharacterSetup);
        GameObject playerObject = new GameObject("PlayerManager");
        PlayerManager player = playerObject.AddComponent<PlayerManager>();
        player.playerData = new PlayerData
        {
            founding = new FoundingState
            {
                initialized = true,
                completed = false,
                stage = FoundingStage.CandidateSelection
            }
        };
        PlayerManager.Instance = player;
        GameObject root = new GameObject("WorldMap3DControllerCharacterSetupTest");
        try
        {
            GameObject pipelineObject = new GameObject("RenderPipeline");
            pipelineObject.transform.SetParent(root.transform, false);
            WorldMapRenderPipeline pipeline = pipelineObject.AddComponent<WorldMapRenderPipeline>();
            GameObject terrainObject = new GameObject("TerrainRenderer");
            terrainObject.transform.SetParent(root.transform, false);
            TerrainRenderer terrainRenderer = terrainObject.AddComponent<TerrainRenderer>();
            SetPrivateField(pipeline, "terrainRenderer", terrainRenderer);
            GameObject interactionObject = new GameObject("Interaction");
            interactionObject.transform.SetParent(root.transform, false);
            WorldMapInteractionController interaction =
                interactionObject.AddComponent<WorldMapInteractionController>();
            GameObject hudObject = new GameObject("HUD");
            hudObject.transform.SetParent(root.transform, false);
            WorldMapHudController hud = hudObject.AddComponent<WorldMapHudController>();

            WorldMap3DController controller = root.AddComponent<WorldMap3DController>();
            SetPrivateField(controller, "renderPipeline", pipeline);
            SetPrivateField(controller, "interaction", interaction);
            SetPrivateField(controller, "hud", hud);
            InvokePrivate(controller, "Awake");
            InvokePrivate(hud, "Awake");
            controller.ApplyCurrentWorld();

            Assert.IsFalse(terrainRenderer.gameObject.activeSelf,
                "CharacterSetup 不应启用世界地形渲染器");
            Assert.IsFalse(hud.gameObject.activeSelf, "CharacterSetup 不应显示 WorldMap HUD");
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(playerObject);
            Object.DestroyImmediate(flowObject);
        }
    }

    [Test]
    public void Controller_MainMenuWithoutMapHidesWorldHud()
    {
        GameObject flowObject = new GameObject("GameFlowStateManager");
        GameFlowStateManager flow = flowObject.AddComponent<GameFlowStateManager>();
        SetPropertyValue(flow, "Current", GameFlowState.MainMenu);
        GameObject root = new GameObject("WorldMap3DControllerMainMenuTest");
        try
        {
            GameObject pipelineObject = new GameObject("RenderPipeline");
            pipelineObject.transform.SetParent(root.transform, false);
            WorldMapRenderPipeline pipeline = pipelineObject.AddComponent<WorldMapRenderPipeline>();
            GameObject interactionObject = new GameObject("Interaction");
            interactionObject.transform.SetParent(root.transform, false);
            WorldMapInteractionController interaction =
                interactionObject.AddComponent<WorldMapInteractionController>();
            GameObject hudObject = new GameObject("HUD");
            hudObject.transform.SetParent(root.transform, false);
            WorldMapHudController hud = hudObject.AddComponent<WorldMapHudController>();

            WorldMap3DController controller = root.AddComponent<WorldMap3DController>();
            SetPrivateField(controller, "renderPipeline", pipeline);
            SetPrivateField(controller, "interaction", interaction);
            SetPrivateField(controller, "hud", hud);
            InvokePrivate(controller, "Awake");
            InvokePrivate(hud, "Awake");
            controller.ApplyCurrentWorld();

            Assert.IsFalse(hud.gameObject.activeSelf,
                "尚无 WorldMap 数据时主菜单也不应显示 WorldMap HUD");
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(flowObject);
        }
    }

    private static void SetPropertyValue(object target, string name, object value)
    {
        PropertyInfo property = target.GetType().GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(target, value, null);
    }

    private static GameObject EnsureMainCamera()
    {
        GameObject existing = Camera.main == null ? null : Camera.main.gameObject;
        if (existing != null) return null;
        GameObject cameraObject = new GameObject("TestMainCamera", typeof(Camera));
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = false;
        camera.transform.position = new Vector3(0f, 50f, 0f);
        camera.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
        return cameraObject;
    }

    private static void SetPrivateField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string name)
    {
        MethodInfo method = target.GetType().GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(target, null);
    }
}
