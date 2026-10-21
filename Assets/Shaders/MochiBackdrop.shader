Shader "MochiWeather/Backdrop"
{
    // ============================================================
    //  MochiWeather · 天幕背景
    // ------------------------------------------------------------
    //  一块正对相机的竖直渐变面片，上下两端各取一种颜色，
    //  中间用 smoothstep 过渡，模拟「天空从地平线向天顶变深」。
    //  天气变化时由 C# 只改两个颜色即可完成换天。
    //  完全 Unlit，不受光照影响。
    // ============================================================

    Properties
    {
        _TopColor    ("顶部颜色", Color) = (0.62, 0.79, 0.94, 1)
        _BottomColor ("底部颜色", Color) = (0.98, 0.93, 0.86, 1)
        _MidPoint    ("过渡位置", Range(0, 1)) = 0.45
        _MidSharp    ("过渡硬度", Range(0.05, 1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Background" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            fixed4 _TopColor;
            fixed4 _BottomColor;
            float  _MidPoint;
            float  _MidSharp;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.vertex.xy + 0.5;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = smoothstep(_MidPoint - _MidSharp * 0.5,
                                     _MidPoint + _MidSharp * 0.5,
                                     saturate(i.uv.y));
                return lerp(_BottomColor, _TopColor, t);
            }
            ENDCG
        }
    }
    Fallback Off
}
