Shader "Cultivation4X/StrategicTerrain"
{
    Properties
    {
        _MainTex ("Ground Texture", 2D) = "white" {}
        _SandTex ("Sand Texture", 2D) = "white" {}
        _GrassTex ("Grass Texture", 2D) = "white" {}
        _DirtTex ("Dirt Texture", 2D) = "white" {}
        _StoneTex ("Stone Texture", 2D) = "white" {}
        [Normal] _SandNormal ("Sand Normal", 2D) = "bump" {}
        [Normal] _GrassNormal ("Grass Normal", 2D) = "bump" {}
        [Normal] _DirtNormal ("Dirt Normal", 2D) = "bump" {}
        [Normal] _StoneNormal ("Stone Normal", 2D) = "bump" {}
        _UseTerrainBlend ("Use Terrain Blend", Range(0,1)) = 0
        _TerrainNormalStrength ("Terrain Normal Strength", Range(0,1.5)) = 0.55
        _Color ("Tint", Color) = (1,1,1,1)
        _TextureStrength ("Texture Strength", Range(0,1)) = 0.30
        _TextureContrast ("Texture Contrast", Range(0.5,2.5)) = 1.15
        _TextureOnly ("Texture Only Debug", Range(0,1)) = 0
        _WorldTiling ("World Tiling", Float) = 0.46
        _MacroStrength ("Macro Variation Strength", Range(0,0.35)) = 0.16
        _MacroScale ("Macro Variation Scale", Float) = 0.065
        _TextureColorBlend ("Authored Texture Color Blend", Range(0,0.5)) = 0.12
        _Brightness ("Brightness", Range(0.75,1.35)) = 1.08
        _LinearColorLift ("Linear Color Lift", Range(0,1)) = 0.30
        _Saturation ("Saturation", Range(0,1)) = 0.78
        _TerrainLightingStrength ("Terrain Lighting Strength", Range(0,1)) = 0.72
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Cull Off

        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        sampler2D _SandTex;
        sampler2D _GrassTex;
        sampler2D _DirtTex;
        sampler2D _StoneTex;
        sampler2D _SandNormal;
        sampler2D _GrassNormal;
        sampler2D _DirtNormal;
        sampler2D _StoneNormal;
        fixed4 _Color;
        float _UseTerrainBlend;
        float _TerrainNormalStrength;
        float _TextureStrength;
        float _TextureContrast;
        float _TextureOnly;
        float _WorldTiling;
        float _MacroStrength;
        float _MacroScale;
        float _TextureColorBlend;
        float _Brightness;
        float _LinearColorLift;
        float _Saturation;
        float _TerrainLightingStrength;
        float3 _WorldMapCurveOrigin;
        float _WorldMapCurveStrength;
        float _TerrainDistanceFadeStart;
        float _TerrainDistanceFadeEnd;
        fixed4 _TerrainDistanceFadeColor;
        float _TerrainDistanceFadeStrength;

        struct Input
        {
            float3 worldPos;
            fixed4 color : COLOR;
            float4 terrainWeights;
            float climateMoisture;
        };

        void vert(inout appdata_full vertex, out Input outputData)
        {
            UNITY_INITIALIZE_OUTPUT(Input, outputData);
            outputData.terrainWeights = vertex.texcoord1;
            outputData.climateMoisture = vertex.texcoord2.x;
            float3 worldPosition = mul(unity_ObjectToWorld, vertex.vertex).xyz;
            float2 curveDelta = worldPosition.xz - _WorldMapCurveOrigin.xz;
            worldPosition.y -= _WorldMapCurveStrength * dot(curveDelta, curveDelta);
            vertex.vertex = mul(unity_WorldToObject, float4(worldPosition, 1.0));
        }

        float Hash21(float2 samplePosition)
        {
            samplePosition = frac(samplePosition * float2(123.34, 456.21));
            samplePosition += dot(samplePosition, samplePosition + 45.32);
            return frac(samplePosition.x * samplePosition.y);
        }

        float ValueNoise(float2 samplePosition)
        {
            float2 cell = floor(samplePosition);
            float2 local = frac(samplePosition);
            local = local * local * (3.0 - 2.0 * local);
            float a = Hash21(cell);
            float b = Hash21(cell + float2(1, 0));
            float c = Hash21(cell + float2(0, 1));
            float d = Hash21(cell + float2(1, 1));
            return lerp(lerp(a, b, local.x), lerp(c, d, local.x), local.y);
        }

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            float2 textureUv = input.worldPos.xz * _WorldTiling;
            fixed3 textureColor = tex2D(_MainTex, textureUv).rgb;
            float4 terrainWeights = max(input.terrainWeights, 0.0);
            terrainWeights /= max(dot(terrainWeights, float4(1,1,1,1)), 0.0001);

            fixed3 sandTexture = tex2D(_SandTex, textureUv).rgb;
            fixed3 grassTexture = tex2D(_GrassTex, textureUv).rgb;
            fixed3 dirtTexture = tex2D(_DirtTex, textureUv).rgb;
            fixed3 stoneTexture = tex2D(_StoneTex, textureUv).rgb;
            fixed3 sandNormal = UnpackNormal(tex2D(_SandNormal, textureUv));
            fixed3 grassNormal = UnpackNormal(tex2D(_GrassNormal, textureUv));
            fixed3 dirtNormal = UnpackNormal(tex2D(_DirtNormal, textureUv));
            fixed3 stoneNormal = UnpackNormal(tex2D(_StoneNormal, textureUv));

            // 网格通道已经由生物群系给出沙 / 草 / 泥 / 石基础比例。湿度与噪声只在
            // 草和泥之间进行小幅局部转移，不再决定整个地表属于哪一种材质。
            float moisture = saturate(input.climateMoisture);
            float localCoverNoise =
                ValueNoise(input.worldPos.xz * 0.16 + float2(61.2, 14.8)) * 0.62 +
                ValueNoise(input.worldPos.xz * 0.47 + float2(7.4, 83.1)) * 0.38;
            float dryDetail = saturate((0.52 - moisture) * 0.22 +
                (localCoverNoise - 0.5) * 0.12) * saturate(_UseTerrainBlend);
            float grassToDirt = min(terrainWeights.y, terrainWeights.y * dryDetail);
            terrainWeights.y -= grassToDirt;
            terrainWeights.z += grassToDirt;
            terrainWeights /= max(dot(terrainWeights, float4(1,1,1,1)), 0.0001);
            float soilExposure = terrainWeights.z * saturate(_UseTerrainBlend);

            // 草与泥不能按常量比例均匀混成橄榄色。权重决定区域覆盖率，连续噪声
            // 决定区域内部哪些位置真正露出泥土，从而形成可读的草地/裸土斑块。
            float grassDirtMass = terrainWeights.y + terrainWeights.z;
            float dirtBias = terrainWeights.z - terrainWeights.y * 0.18;
            float dirtPatchMask = smoothstep(0.30, 0.64,
                dirtBias + (localCoverNoise - 0.5) * 0.92);
            fixed3 grassDirtTexture = lerp(grassTexture, dirtTexture, dirtPatchMask);
            fixed3 blendedTerrainTexture =
                sandTexture * terrainWeights.x +
                grassDirtTexture * grassDirtMass +
                stoneTexture * terrainWeights.w;
            textureColor = lerp(textureColor, blendedTerrainTexture, saturate(_UseTerrainBlend));
            fixed3 grassDirtNormal = lerp(grassNormal, dirtNormal, dirtPatchMask);
            fixed3 blendedTerrainNormal =
                sandNormal * terrainWeights.x +
                grassDirtNormal * grassDirtMass +
                stoneNormal * terrainWeights.w;
            blendedTerrainNormal = normalize(blendedTerrainNormal);
            fixed3 contrastedTexture = saturate((textureColor - 0.5) * _TextureContrast + 0.5);
            fixed textureLuminance = dot(contrastedTexture, fixed3(0.299, 0.587, 0.114));
            fixed detailValue = lerp(0.52, 1.48, textureLuminance);
            fixed3 detail = lerp(fixed3(1,1,1), fixed3(detailValue, detailValue, detailValue),
                _TextureStrength);
            float macroNoise = ValueNoise(input.worldPos.xz * _MacroScale) * 0.68 +
                ValueNoise(input.worldPos.xz * (_MacroScale * 2.13) + 17.7) * 0.32;
            fixed macroDetail = lerp(1.0 - _MacroStrength, 1.0 + _MacroStrength,
                saturate(macroNoise));

            fixed3 vertexColor = input.color.rgb * _Color.rgb;
#ifndef UNITY_COLORSPACE_GAMMA
            vertexColor = lerp(GammaToLinearSpace(vertexColor), vertexColor, _LinearColorLift);
#endif
            fixed3 biomeTextured = saturate(vertexColor * detail * macroDetail * _Brightness);
            fixed3 authoredTextured = saturate(textureColor * macroDetail * _Brightness);
            // 裸土和岩石需保留各自综合色，但限制局部综合色上限，避免把地形重新染成
            // 大块纯棕色；湿润草原则继续接受数据层的大尺度色调。
            float dirtPatchInfluence = dirtPatchMask * grassDirtMass;
            float localTextureColorBlend = max(_TextureColorBlend,
                saturate(dirtPatchInfluence * 0.82 + terrainWeights.w * 0.68 +
                    terrainWeights.x * 0.72 + soilExposure * 0.12));
            biomeTextured = lerp(biomeTextured, authoredTextured, localTextureColorBlend);
            biomeTextured = lerp(biomeTextured, textureColor, _TextureOnly);

            // 低饱和哑光：颜色向亮度靠拢，避免“塑料草绿”。
            float terrainLuminance = dot(biomeTextured, fixed3(0.299, 0.587, 0.114));
            biomeTextured = lerp(fixed3(terrainLuminance, terrainLuminance, terrainLuminance),
                biomeTextured, _Saturation);

            // 大气透视：距离越远越向雾色靠拢。
            float viewDistance = distance(input.worldPos, _WorldSpaceCameraPos);
            float distanceFade = smoothstep(_TerrainDistanceFadeStart, _TerrainDistanceFadeEnd,
                viewDistance) * saturate(_TerrainDistanceFadeStrength);
            biomeTextured = lerp(biomeTextured, _TerrainDistanceFadeColor.rgb, distanceFade);

            float litWeight = saturate(_TerrainLightingStrength);
            output.Albedo = biomeTextured * litWeight;
            output.Emission = biomeTextured * (1.0 - litWeight);
            output.Metallic = 0.0;
            output.Smoothness = 0.06;
            output.Occlusion = 1.0;
            output.Normal = normalize(lerp(fixed3(0, 0, 1), blendedTerrainNormal,
                saturate(_TerrainNormalStrength * _UseTerrainBlend)));
            output.Alpha = input.color.a * _Color.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
