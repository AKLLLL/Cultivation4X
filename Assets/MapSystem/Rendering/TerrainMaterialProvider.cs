using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 负责五个战略地表纹理槽 → 颜色/材质的映射，以后替换真实美术资源时只修改这里。
    /// 第一版使用内置 Standard shader，水面为半透明。
    /// </summary>
    public static class TerrainMaterialProvider
    {
        private const int TransparentRenderQueue = 3000;

        /// <summary>地类 → 基础颜色（与 2D 表现相近，深/浅水共用水面蓝色）。</summary>
        public static Color ColorFor(LandformType landform)
        {
            switch (landform)
            {
                case LandformType.DeepWater:
                case LandformType.ShallowWater: return new Color(0.09f, 0.34f, 0.52f);
                case LandformType.Coast: return new Color(0.78f, 0.75f, 0.60f);
                case LandformType.Plain: return new Color(0.42f, 0.60f, 0.29f);
                case LandformType.Hill: return new Color(0.38f, 0.48f, 0.24f);
                case LandformType.Mountain: return new Color(0.45f, 0.45f, 0.48f);
                default: return Color.magenta;
            }
        }

        /// <summary>纹理槽 → 代表地类（用于复用既有水、沙、草、泥、石材质配置）。</summary>
        public static LandformType RepresentativeLandform(int submeshIndex)
        {
            switch (submeshIndex)
            {
                case TerrainMeshGenerator.WaterSubmesh: return LandformType.ShallowWater;
                case TerrainMeshGenerator.CoastSubmesh: return LandformType.Coast;
                case TerrainMeshGenerator.PlainSubmesh: return LandformType.Plain;
                case TerrainMeshGenerator.HillSubmesh: return LandformType.Hill;
                case TerrainMeshGenerator.MountainSubmesh: return LandformType.Mountain;
                default: return LandformType.Plain;
            }
        }

        /// <summary>是否为水面分组（半透明）。</summary>
        public static bool IsWaterGroup(LandformType landform) =>
            landform == LandformType.DeepWater || landform == LandformType.ShallowWater;

        /// <summary>按地类创建运行时材质；水面使用半透明渲染队列。</summary>
        public static Material CreateMaterial(LandformType landform)
        {
            Shader shader = Shader.Find("Standard");
            Material material = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            material.name = "TerrainMaterial_" + landform;
            Color color = ColorFor(landform);
            if (IsWaterGroup(landform))
            {
                color.a = 0.8f;
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = TransparentRenderQueue;
            }
            else
            {
                material.SetFloat("_Mode", 0f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                material.SetInt("_ZWrite", 1);
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = -1;
            }
            material.color = color;
            return material;
        }
    }
}
