Shader "Unlit/VertexColor"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags
        {
            "Queue"="Geometry"
            "RenderType"="Opaque"
            "IgnoreProjector"="True"
        }
        Cull Off
        Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
#ifndef UNITY_COLORSPACE_GAMMA
                // 线性色彩空间下把顶点色从 sRGB 转回线性，避免画面整体发白。
                i.color.rgb = GammaToLinearSpace(i.color.rgb);
#endif
                return i.color;
            }
            ENDCG
        }
    }
}
