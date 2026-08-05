using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public class TraitDatabase : MonoBehaviour
{
    public static TraitDatabase Instance { get; private set; }
    private readonly Dictionary<string, TraitDefinition> traits = new Dictionary<string, TraitDefinition>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MapTestBootstrap.IsTestScene) return;
        if (Instance == null) new GameObject("TraitDatabase").AddComponent<TraitDatabase>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyUtility.MarkPersistent(gameObject);
        foreach (TextAsset file in Resources.LoadAll<TextAsset>("Configs/Traits"))
        {
            try
            {
                List<TraitDefinition> loaded = JsonConvert.DeserializeObject<List<TraitDefinition>>(file.text);
                foreach (TraitDefinition trait in loaded)
                {
                    if (trait == null || string.IsNullOrWhiteSpace(trait.id) || traits.ContainsKey(trait.id))
                    { Debug.LogError($"特质配置无效或重复: {file.name}"); continue; }
                    traits.Add(trait.id, trait);
                }
            }
            catch (Exception exception) { Debug.LogError($"特质配置解析失败 {file.name}: {exception.Message}"); }
        }
    }

    public TraitDefinition Get(string id)
    {
        traits.TryGetValue(id, out TraitDefinition result);
        return result;
    }
}
