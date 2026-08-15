Shader "Cultivation4X/Map/Static Grass"
{
    Properties
    {
        _MainTex ("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _ColorTop ("Top Color", Color) = (1,1,1,1)
        _ColorBottom ("Bottom Color", Color) = (1,1,1,1)
        _MaskClip ("Alpha Clip", Range(0,1)) = 0.35
        _Glossiness ("Smoothness", Range(0,1)) = 0.05
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _ColorTop;
        fixed4 _ColorBottom;
        half _MaskClip;
        half _Glossiness;
        struct Input { float2 uv_MainTex; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 source = tex2D(_MainTex, IN.uv_MainTex);
            clip(source.a - _MaskClip);
            fixed4 tint = lerp(_ColorBottom, _ColorTop, saturate(IN.uv_MainTex.y));
            o.Albedo = source.rgb * tint.rgb;
            o.Alpha = 1;
            o.Smoothness = _Glossiness;
        }
        ENDCG
    }
    FallBack "Transparent/Cutout/VertexLit"
}
