using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class UIManagerStackingTests
{
    private readonly List<Object> objects = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        UIManager.Instance = null;
        ResetSectWorldInstance();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in objects)
            if (item != null) Object.DestroyImmediate(item);
        objects.Clear();
        UIManager.Instance = null;
        ResetSectWorldInstance();
    }

    private static void ResetSectWorldInstance()
    {
        System.Reflection.FieldInfo field = typeof(SectWorldInterface).GetField(
            "<Instance>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(null, null);
    }

    private T Add<T>(string name) where T : Component
    {
        GameObject go = new GameObject(name);
        objects.Add(go);
        return go.AddComponent<T>();
    }

    private static RectTransform PanelUnder(Transform parent, string name)
    {
        return RuntimeUIFactory.Panel(parent, name,
            new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.9f));
    }

    private static int Sort(GameObject panel) =>
        panel.GetComponent<Canvas>().sortingOrder;

    private GameObject CreateSectWorldRoot()
    {
        GameObject root = new GameObject("SectWorldRoot");
        objects.Add(root);
        RuntimeUIFactory.Canvas(root, 930);
        return root;
    }

    [Test]
    public void SceneStyleSubPanel_OpenedAfterLayout_GetsHigherSorting()
    {
        UIManager ui = Add<UIManager>("UIManager");
        UIManager.Instance = ui;

        GameObject sectRoot = CreateSectWorldRoot();
        RectTransform layout = PanelUnder(sectRoot.transform, "SectLayout");
        objects.Add(layout.gameObject);

        GameObject sceneRoot = new GameObject("SceneRoot");
        objects.Add(sceneRoot);
        RuntimeUIFactory.Canvas(sceneRoot, 0);
        RectTransform warehouse = PanelUnder(sceneRoot.transform, "WarehousePanel");
        objects.Add(warehouse.gameObject);

        ui.OpenPanel(layout.gameObject);
        ui.OpenPanel(warehouse.gameObject);

        Assert.IsTrue(warehouse.GetComponent<Canvas>().overrideSorting);
        Assert.Greater(Sort(warehouse.gameObject), Sort(layout.gameObject),
            $"后打开的子面板层级必须高于布局面板（layout={Sort(layout.gameObject)}, warehouse={Sort(warehouse.gameObject)}）");
    }

    [Test]
    public void RuntimeStyleSubPanel_OpenedAfterLayout_GetsHigherSorting()
    {
        UIManager ui = Add<UIManager>("UIManager");
        UIManager.Instance = ui;

        GameObject sectRoot = CreateSectWorldRoot();
        RectTransform layout = PanelUnder(sectRoot.transform, "SectLayout");
        objects.Add(layout.gameObject);

        GameObject alchemyRoot = new GameObject("AlchemyPanel");
        objects.Add(alchemyRoot);
        RuntimeUIFactory.Canvas(alchemyRoot, 845);
        RectTransform alchemy = PanelUnder(alchemyRoot.transform, "AlchemyRoom");
        objects.Add(alchemy.gameObject);

        ui.OpenPanel(layout.gameObject);
        ui.OpenPanel(alchemy.gameObject);

        Assert.Greater(Sort(alchemy.gameObject), Sort(layout.gameObject),
            $"后打开的子面板层级必须高于布局面板（layout={Sort(layout.gameObject)}, alchemy={Sort(alchemy.gameObject)}）");
    }

    [Test]
    public void ReopeningActiveSubPanel_RestacksAboveLayout()
    {
        UIManager ui = Add<UIManager>("UIManager");
        UIManager.Instance = ui;

        GameObject sectRoot = CreateSectWorldRoot();
        RectTransform layout = PanelUnder(sectRoot.transform, "SectLayout");
        objects.Add(layout.gameObject);

        GameObject sceneRoot = new GameObject("SceneRoot");
        objects.Add(sceneRoot);
        RuntimeUIFactory.Canvas(sceneRoot, 0);
        RectTransform warehouse = PanelUnder(sceneRoot.transform, "WarehousePanel");
        objects.Add(warehouse.gameObject);

        ui.OpenPanel(warehouse.gameObject);   // 例如先通过资源栏打开
        ui.OpenPanel(layout.gameObject);      // 布局后打开
        int before = Sort(warehouse.gameObject);

        ui.OpenPanel(warehouse.gameObject);   // 布局中再次点击库藏（面板已打开）
        int after = Sort(warehouse.gameObject);

        Assert.Greater(after, Sort(layout.gameObject),
            $"再次打开已激活的子面板应提升到布局之上（layout={Sort(layout.gameObject)}, warehouse={after}）");
        Assert.Greater(after, before);
    }

}
