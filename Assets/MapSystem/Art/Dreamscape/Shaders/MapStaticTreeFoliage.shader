Shader "Cultivation4X/Map/Static Tree Foliage"
{
    Properties
    {
        _FoliageColorMap ("Color Map", 2D) = "white" {}
        _MainTex ("Fallback Color Map", 2D) = "white" {}
        _FoliageColorTop ("Top Color", Color) = (1,1,1,1)
        _FoliageColorBottom ("Bottom Color", Color) = (1,1,1,1)
        _MaskClipValue ("Alpha Clip", Range(0,1)) = 0.35
        _Glossiness ("Smoothness", Range(0,1)) = 0.05
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        sampler2D _FoliageColorMap;
        fixed4 _FoliageColorTop;
        fixed4 _FoliageColorBottom;
        half _MaskClipValue;
        half _Glossiness;
        struct Input { float2 uv_FoliageColorMap; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 source = tex2D(_FoliageColorMap, IN.uv_FoliageColorMap);
            clip(source.a - _MaskClipValue);
            fixed4 tint = lerp(_FoliageColorBottom, _FoliageColorTop,
                saturate(IN.uv_FoliageColorMap.y));
            o.Albedo = source.rgb * tint.rgb;
            o.Alpha = 1;
            o.Smoothness = _Glossiness;
        }
        ENDCG
    }
    FallBack "Transparent/Cutout/VertexLit"
}
