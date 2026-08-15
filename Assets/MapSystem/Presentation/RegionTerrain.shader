Shader "Cultivation4X/Map/Region Terrain"
{
    Properties
    {
        _GrassTex ("Grass", 2D) = "white" {}
        _DirtTex ("Dirt", 2D) = "white" {}
        _StoneTex ("Stone", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _WorldTiling ("World Tiling", Float) = 0.55
    }
    SubShader
    {
        Tags { "Queue"="Geometry+4" "RenderType"="Opaque" }
        Cull Off

        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert
        #include "UnityCG.cginc"

        sampler2D _GrassTex;
        sampler2D _DirtTex;
        sampler2D _StoneTex;
        fixed4 _Color;
        float _WorldTiling;
        float3 _WorldMapCurveOrigin;
        float _WorldMapCurveStrength;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            fixed4 color : COLOR;
            INTERNAL_DATA
        };

        void vert(inout appdata_full vertex, out Input outputData)
        {
            UNITY_INITIALIZE_OUTPUT(Input, outputData);
            float3 worldPosition = mul(unity_ObjectToWorld, vertex.vertex).xyz;
            float2 curveDelta = worldPosition.xz - _WorldMapCurveOrigin.xz;
            worldPosition.y -= _WorldMapCurveStrength * dot(curveDelta, curveDelta);
            vertex.vertex = mul(unity_WorldToObject, float4(worldPosition, 1.0));
        }

        fixed3 Triplanar(sampler2D textureSampler, float3 position, float3 normal)
        {
            float3 blend = pow(abs(normal), 4.0);
            blend /= max(blend.x + blend.y + blend.z, 0.0001);
            fixed3 x = tex2D(textureSampler, position.zy * _WorldTiling).rgb;
            fixed3 y = tex2D(textureSampler, position.xz * _WorldTiling).rgb;
            fixed3 z = tex2D(textureSampler, position.xy * _WorldTiling).rgb;
            return x * blend.x + y * blend.y + z * blend.z;
        }

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            float3 normal = normalize(WorldNormalVector(input, float3(0,0,1)));
            float slope = 1.0 - saturate(normal.y);
            fixed3 grass = Triplanar(_GrassTex, input.worldPos, normal);
            fixed3 dirt = Triplanar(_DirtTex, input.worldPos, normal);
            fixed3 stone = Triplanar(_StoneTex, input.worldPos, normal);
            float dirtWeight = smoothstep(0.10, 0.38, slope);
            float stoneWeight = smoothstep(0.30, 0.64, slope);
            fixed3 terrain = lerp(grass, dirt, dirtWeight);
            terrain = lerp(terrain, stone, stoneWeight);
            output.Albedo = terrain * input.color.rgb * _Color.rgb;
            output.Metallic = 0.0;
            output.Smoothness = 0.04;
            output.Occlusion = 1.0;
            output.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
