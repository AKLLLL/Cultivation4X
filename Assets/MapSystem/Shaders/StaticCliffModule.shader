Shader "Cultivation4X/Map/Static Cliff Module"
{
    Properties
    {
        _MainTex ("Palette", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 150

        CGPROGRAM
        #pragma surface surf Lambert addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Tint;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input input, inout SurfaceOutput output)
        {
            fixed4 color = tex2D(_MainTex, input.uv_MainTex) * _Tint;
            output.Albedo = color.rgb;
            output.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
