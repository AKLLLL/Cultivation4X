using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    public enum SpiritualVeinOrigin { Natural = 0, Cultivated = 1, Artificial = 2 }

    [Serializable]
    public sealed class ResourceNodeDefinition
    {
        public string id;
        public string resourceId;
        public int baseOutput;
        public List<BiomeType> biomeRequirements = new List<BiomeType>();
        public bool requiresVeinElement;
        public SpiritElement requiredVeinElement;
    }

    [Serializable]
    public sealed class SpiritualVeinDefinition
    {
        public string id;
        public SpiritualVeinOrigin origin = SpiritualVeinOrigin.Natural;
        public int grade;
        public float outputMultiplier = 1f;
    }

    [Serializable]
    public sealed class ResourceNodeRuntime
    {
        public string nodeId;
        public string definitionId;
        public string siteId;
        public string regionId;
        public int cellIndex = -1;
        public int lastCalculatedOutput;
        public int lastSettledLost;
        public float productionRemainder;
        public int lastSettledMonth;
    }

    [Serializable]
    public sealed class SpiritualVeinRuntime
    {
        public string veinId;
        public string sourceVeinId;
        public string definitionId;
        public string regionId;
        public SpiritElement element;
        public int grade;
        public SpiritualVeinOrigin origin = SpiritualVeinOrigin.Natural;
    }

    public static class ResourceDefinitionDatabase
    {
        private static List<ResourceNodeDefinition> nodeDefinitions;
        private static List<SpiritualVeinDefinition> veinDefinitions;

        public static IReadOnlyList<ResourceNodeDefinition> Nodes =>
            nodeDefinitions ?? (nodeDefinitions = Load<ResourceNodeDefinition>("Configs/Resources/ResourceNodes"));
        public static IReadOnlyList<SpiritualVeinDefinition> Veins =>
            veinDefinitions ?? (veinDefinitions = Load<SpiritualVeinDefinition>("Configs/Resources/SpiritualVeins"));

        public static ResourceNodeDefinition GetNode(string id) => Nodes.FirstOrDefault(item => item?.id == id);
        public static SpiritualVeinDefinition GetVein(string id) => Veins.FirstOrDefault(item => item?.id == id);
        public static void ResetForTests() { nodeDefinitions = null; veinDefinitions = null; }

        private static List<T> Load<T>(string path)
        {
            TextAsset asset = Resources.Load<TextAsset>(path);
            if (asset == null) return new List<T>();
            return JsonConvert.DeserializeObject<List<T>>(asset.text) ?? new List<T>();
        }
    }
}
