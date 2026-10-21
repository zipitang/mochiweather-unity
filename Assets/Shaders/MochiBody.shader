Shader "MochiWeather/MochiBody"
{
    // ============================================================
    //  MochiWeather · 身体着色器
    // ------------------------------------------------------------
    //  麻薯的身体不是纯色球，而是「内部有柔光的半透明糯米团」：
    //    1. half-lambert 主光 + 球谐环境光 → 整体柔和，没有硬阴影
    //    2. 厚度渐变（Fresnel 取反）→ 中心透亮、边缘收紧，像糯米皮的质感
    //    3. 边缘光 → 把身体从背景里勾出来
    //    4. 颜色由 _Tint 驱动 → 天气/情绪换色只在 C# 里改一个颜色
    // ============================================================

    Properties
    {
        _BaseColor   ("主体颜色", Color) = (0.99, 0.93, 0.87, 1)
        _ShadeColor  ("暗部颜色", Color) = (0.93, 0.82, 0.76, 1)
        _Tint        ("整体色调（天气驱动）", Color) = (1, 1, 1, 1)
        _RimColor    ("边缘光", Color) = (1, 0.98, 0.95, 1)
        _RimPower    ("边缘光范围", Range(0.5, 8)) = 2.6
        _RimStrength ("边缘光强度", Range(0, 3)) = 0.55
        _SSSStrength ("内部透光", Range(0, 2)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull Back
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNrm : TEXCOORD1;
            };

            fixed4 _BaseColor;
            fixed4 _ShadeColor;
            fixed4 _Tint;
            fixed4 _RimColor;
            float  _RimPower;
            float  _RimStrength;
            float  _SSSStrength;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNrm = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldNrm);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);

                // half-lambert：把明暗交界推到背面，整体柔和
                float ndl = dot(N, L) * 0.5 + 0.5;
                float3 light = _LightColor0.rgb * ndl;

                float3 ambient = ShadeSH9(float4(N, 1.0));

                // 主体：亮部 ← → 暗部渐变
                float3 body = lerp(_ShadeColor.rgb, _BaseColor.rgb, ndl);
                body *= _Tint.rgb;

                // 内部透光：视线方向与光的夹角 → 侧逆光时透亮（糯米团质感）
                float sss = pow(saturate(dot(V, -L)), 2.0) * _SSSStrength;

                // 边缘光
                float rim = pow(saturate(1.0 - saturate(dot(N, V))), _RimPower);
                float3 rimCol = _RimColor.rgb * rim * _RimStrength;

                float3 col = body * (light * 0.9 + ambient) + sss * body + rimCol;
                return fixed4(col, 1.0);
            }
            ENDCG
        }

        // 简单阴影投射（让毛球在桌面上有重量）
        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct v2f { V2F_SHADOW_CASTER; };

            v2f vert(appdata_base v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }

    Fallback "Diffuse"
}
