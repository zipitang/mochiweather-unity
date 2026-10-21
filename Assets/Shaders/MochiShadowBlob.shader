Shader "MochiWeather/ShadowBlob"
{
    // ============================================================
    //  MochiWeather · 接触阴影
    // ------------------------------------------------------------
    //  麻薯坐在桌上时脚下的一团柔和暗影。用径向渐变 + 平方衰减，
    //  比真实的 shadow map 更软、更符合「毛绒玩偶」的观感，
    //  而且不依赖任何光照贴图 / 阴影设置。
    //  颜色与大小由 C# 按情绪强度调。
    // ============================================================

    Properties
    {
        _Color    ("阴影颜色", Color) = (0.35, 0.28, 0.24, 0.42)
        _Falloff  ("衰减硬度", Range(0.5, 6)) = 2.2
        _Strength ("强度", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

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

            fixed4 _Color;
            float  _Falloff;
            float  _Strength;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.vertex.xy + 0.5;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float d = length(i.uv - 0.5) * 2.0;
                float a = pow(saturate(1.0 - d), _Falloff) * _Color.a * _Strength;
                return fixed4(_Color.rgb, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
