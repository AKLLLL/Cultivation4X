Shader "Cultivation4X/StrategicHexGrid"
{
    Properties
    {
        _Color ("Color", Color) = (0.08,0.10,0.08,0.38)
        _WidthScale ("Width Scale", Float) = 1
        _FogInfluence ("Fog Influence", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Offset -1, -1
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            fixed4 _Color;
            float _WidthScale;
            float _FogInfluence;
            float3 _WorldMapCurveOrigin;
            float _WorldMapCurveStrength;
            struct appdata { float4 vertex : POSITION; float2 sideOffset : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; UNITY_FOG_COORDS(0) };
            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPosition = mul(unity_ObjectToWorld, v.vertex).xyz;
                worldPosition.xz += v.sideOffset * (_WidthScale - 1.0);
                float2 curveDelta = worldPosition.xz - _WorldMapCurveOrigin.xz;
                worldPosition.y -= _WorldMapCurveStrength * dot(curveDelta, curveDelta);
                o.vertex = UnityWorldToClipPos(worldPosition);
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = _Color;
                fixed4 fogged = color;
                UNITY_APPLY_FOG(i.fogCoord, fogged);
                color.rgb = lerp(color.rgb, fogged.rgb, saturate(_FogInfluence));
                return color;
            }
            ENDCG
        }
    }
}
