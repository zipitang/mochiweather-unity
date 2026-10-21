// =============================================================================
//  MochiWeather · 真 3D 毛绒外壳着色器（Built-in Render Pipeline）
// -----------------------------------------------------------------------------
//  本文件由 skill「fur-shell-rendering」的参考实现同步而来
//  （.workbuddy/skills/fur-shell-rendering/assets/FurShell-Unity.shader）。
//  与旧版的区别：
//    · 毛长改由 tex_fur.png 的 R 通道驱动（不再是各向同性 3D 噪声）
//    · 动态改用 UV 流场 —— 几何一动不动，因此不会撕开层间缝隙
//    · 每层采样坐标错开；壳层高度用 t=(k+1)/N（避开与本体球的 z-fighting）
//    · 修掉 length(local) 3D 距离裁丝、毛等长笔直两个老 bug
//  贴图由 scripts/gen_fur_textures.py 生成，已放在 Assets/Resources/Fur/。
//  MochiFurBall.cs 负责逐层喂 _ShellIndex，并从 Resources 加载这两张贴图。
// =============================================================================

Shader "MochiWeather/FurShell"
{
    Properties
    {
        [Header(毛皮贴图 · 由 gen_fur_textures.py 生成)]
        _FurTex        ("毛长图  R=毛长 G=细丝 B=遮罩露本体", 2D) = "white" {}
        _WindTex       ("风场图  RG=流向", 2D) = "gray" {}

        [Header(颜色)]
        _FurColor      ("毛根颜色", Color) = (0.98, 0.90, 0.83, 1)
        _FurTipColor   ("毛尖颜色", Color) = (1.00, 0.96, 0.91, 1)
        _RimColor      ("毛尖透光色", Color) = (1.00, 0.97, 0.92, 1)
        _Tint          ("整体色调（天气/情绪驱动）", Color) = (1, 1, 1, 1)

        [Header(壳层)]
        _ShellIndex    ("壳层索引（由脚本逐层驱动）", Float) = 0
        _ShellCount    ("壳层总数（40 起才饱满，20 层边缘看得出碎屑感）", Float) = 40
        _FurLength     ("毛长（球半径 0.5 时给 0.085 ≈ 17%）", Range(0.0, 0.5)) = 0.085
        _Fluffiness    ("蓬松度（>1 = 毛尖外扩炸开）", Range(0, 3)) = 1.0

        [Header(毛长图采样 · 最容易调错的一组)]
        _TexScale      ("平铺次数（球面 UV 下 9 左右，见 SKILL 标定公式）", Range(0.5, 40)) = 9.0
        _FurCut        ("毛长阈值 = 疏密总开关", Range(0.0, 3.0)) = 1.15
        _LenContrast   ("毛长对比度（把灰度压成离散斑块）", Range(0.5, 8.0)) = 1.35
        _MaskAmount    ("B 通道遮罩强度（露本体）", Range(0.0, 2.0)) = 0.35
        _EdgeSoft      ("毛尖柔边宽度", Range(0.005, 0.30)) = 0.055
        _TipFadeStart  ("毛尖开始淡出的高度（低于 0.72 会像雾）", Range(0.5, 0.99)) = 0.82
        [Toggle(_UV_FROM_MESH)] _UVFromMesh ("改用网格自带 UV（默认由位置算球面 UV）", Float) = 0

        [Header(UV 流场风 · 不动几何)]
        _WindScale     ("风向场平铺次数", Range(0.2, 12)) = 1.6
        _WindSpeed     ("风速", Range(0.0, 3.0)) = 0.35
        _WindIntens    ("风力（整体飘动幅度）", Range(0.0, 0.10)) = 0.022
        _WindInfluence ("毛尖权重指数（越大则只有毛尖动）", Range(0.3, 4.0)) = 1.2
        _StyleAmount   ("打散强度（打破平铺重复感）", Range(0.0, 0.10)) = 0.030
        _StylePower    ("打散作用的高度指数", Range(0.5, 4.0)) = 1.5

        [Header(造型)]
        _GroomDir      ("倾倒方向 (xyz)", Vector) = (0.35, -0.15, 0.92, 0)
        _GroomAmount   ("倾倒强度", Range(0.0, 0.5)) = 0.10
        _RootOcclusion ("毛根压暗（毛绒感主要来源）", Range(0, 1.5)) = 0.85
        _RimPower      ("透光/边缘光强度", Range(0, 8)) = 2.2
        _AlphaCutout   ("像素裁切阈值", Range(0.0, 1.0)) = 0.02

        [Header(径向交互 · 指针戳上去毛被压平)]
        _Track         ("追踪点 0..3 (xyz=世界坐标 w=强度)", Vector) = (0, 0, 0, 0)
        _TrackRadius   ("作用半径", Range(0.01, 2.0)) = 0.38
        _TrackBite     ("压平强度", Range(0.0, 4.0)) = 1.6
        _TrackPush     ("推开强度（毛让开）", Range(0.0, 0.5)) = 0.06

        [Header(程序化细丝分支 · 没有贴图时的兜底)]
        [Toggle(_STRAND_PROCEDURAL)] _StrandProc ("改用程序化细丝（无需贴图）", Float) = 0
        _FurDensity    ("细丝密度（切向格数）", Range(4, 80)) = 26
        _FurThickness  ("细丝粗细", Range(0.02, 0.6)) = 0.24
        _CellDropout   ("随机丢格比例（打破均匀感）", Range(0.0, 0.5)) = 0.06
        _StrandFlow    ("流场对细丝格子的位移倍率", Range(0.0, 1.0)) = 0.25

        [Header(兼容旧驱动的别名 · 新工程不要用)]
        _WindDirection ("[旧] 风/倾倒偏移向量 (xyz)", Vector) = (0, 0, 0, 0)
        _WindStrength  ("[旧] 风力（乘在 _WindDirection 上）", Range(0, 5)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "TransparentCutout"
            "Queue"           = "AlphaTest"     // 必须在不透明本体之后
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Cull Off          // 毛是半透细丝，双面渲染才有体积感
            ZWrite On         // 见文件末「关于深度写入」说明
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma shader_feature_local _STRAND_PROCEDURAL
            #pragma shader_feature_local _UV_FROM_MESH
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNrm : TEXCOORD1;
                float3 objPos   : TEXCOORD2;   // 外推之后
                float3 objBase  : TEXCOORD3;   // 外推之前（用来算稳定的球面 UV）
                float3 objNrm   : TEXCOORD4;
                float2 uv       : TEXCOORD5;
                float  shellT   : TEXCOORD6;   // 0 = 毛根, 1 = 毛尖
            };

            sampler2D _FurTex, _WindTex;

            fixed4 _FurColor, _FurTipColor, _RimColor, _Tint;
            float  _ShellIndex, _ShellCount, _FurLength, _Fluffiness;
            float  _TexScale, _FurCut, _LenContrast, _MaskAmount, _EdgeSoft, _TipFadeStart;
            float  _WindScale, _WindSpeed, _WindIntens, _WindInfluence, _StyleAmount, _StylePower;
            float4 _GroomDir;
            float  _GroomAmount, _RootOcclusion, _RimPower, _AlphaCutout;
            float4 _Track[4];
            float  _TrackRadius, _TrackBite, _TrackPush;
            float  _FurDensity, _FurThickness, _CellDropout, _StrandFlow;
            float4 _WindDirection;
            float  _WindStrength;
            float  _TimeOffset;    // 由脚本喂，保证 EditMode 下调参也能看到风在动

            // ---------- 噪声（只有程序化分支用得上） ----------
            float hash31(float3 p)
            {
                p = frac(p * 0.3183099 + float3(0.1, 0.2, 0.3));
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // 由位置现算球面 UV。不依赖网格的 UV 通道，任意网格都能用。
            float2 sphereUV(float3 p)
            {
                float3 n = normalize(p);
                return float2(atan2(n.z, n.x) * 0.1591549430 + 0.5,
                              asin(clamp(n.y, -1.0, 1.0)) * 0.3183098862 + 0.5);
            }

            v2f vert(appdata v)
            {
                v2f o;
                // t = (k+1)/N，**不是** k/N —— 最内层壳体必须离开表面一点点，
                // 否则它和本体球共面，会出现 z-fighting 的麻点。
                float t = saturate((_ShellIndex + 1.0) / max(_ShellCount, 1.0));
                o.shellT = t;

                float3 nrm  = normalize(v.normal);
                float3 base = v.vertex.xyz;
                float3 pos  = base;

                // ① 壳层外推。t 从 1/N 起、取不到 0 —— 天然避开与本体球的 z-fighting。
                //    _Fluffiness > 1 时毛尖额外外扩，形成「炸毛」轮廓。
                float fluff = 1.0 + t * t * (_Fluffiness - 1.0);
                pos += nrm * (_FurLength * t * fluff);

                // ② 统一倾倒（groom）。只依赖 t，不含任何逐顶点随机量，
                //    因此相邻顶点的偏移一致 —— 层次依然嵌套，不会撕开缝隙。
                //    _WindDirection 是旧驱动留下的别名：它把整个偏移向量塞在 xyz 里。
                float3 lean = _GroomDir.xyz * _GroomAmount + _WindDirection.xyz * _WindStrength;
                float  leanLen = length(lean);
                if (leanLen > 0.0001)
                {
                    // 幅度封顶在 100% 毛长：超过这个量就不是「毛倒伏」而是「毛被拽走了」
                    float capped = min(leanLen, 1.0);
                    pos += (lean / leanLen) * (capped * _FurLength * t * t);
                }

                o.objPos   = pos;
                o.objBase  = base;
                o.objNrm   = nrm;
                o.uv       = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, float4(pos, 1.0)).xyz;
                o.worldNrm = UnityObjectToWorldNormal(nrm);
                o.pos      = UnityObjectToClipPos(float4(pos, 1.0));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = i.shellT;

            #ifdef _UV_FROM_MESH
                float2 uv0 = i.uv;
            #else
                float2 uv0 = sphereUV(i.objBase);
            #endif

                // ================= 偏移量：全部在 UV 空间里累加 =================
                // 这里一动都不动几何，所以层与层之间永远不会被撕开。
                float2 styleDrift = (tex2D(_FurTex, uv0 * (_TexScale * 0.37) + 13.7).gb - 0.5)
                                    * _StyleAmount * pow(t, _StylePower);

                // 风：把采样坐标按风向旋转，再沿 +x 滚动 —— 于是流场是「沿风向扫过」的
                float2 wd = _WindDirection.xy;
                if (dot(wd, wd) < 1e-6) wd = float2(1.0, 0.0);
                wd = normalize(wd);
                float2x2 R  = float2x2( wd.x, -wd.y, wd.y, wd.x);   // 转到风坐标系
                float2x2 Rt = float2x2( wd.x,  wd.y, -wd.y, wd.x);  // 转回物体坐标系

                float2 wuv = mul(R, uv0 * _WindScale)
                           + (_Time.y + _TimeOffset) * _WindSpeed * float2(1.0, 0.31);
                float2 wind = mul(Rt, (tex2D(_WindTex, wuv).rg - 0.5) * 2.0) * _WindIntens;
                float  infl = pow(t, _WindInfluence);

                // ---- 径向交互：指针戳上来 → 毛让开 + 被压平 ----
                float  bite = 0.0;
                float2 push = float2(0, 0);
                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    // 这句必须有：未使用的追踪点若参与 normalize(零向量) 会出 NaN，
                    // 0 × NaN = NaN，整片毛会直接消失。
                    if (_Track[k].w > 0.0)
                    {
                        float3 dv = i.worldPos - _Track[k].xyz;
                        float  dl = max(length(dv), 1e-5);
                        float  kk = (1.0 - smoothstep(_TrackRadius * 0.55, _TrackRadius, dl)) * _Track[k].w;
                        bite += kk;
                        push += (dv.xy / dl) * kk * _TrackPush;
                    }
                }

                float2 drift = wind * infl + styleDrift + push;

                // ================= 裁切 =================
                float cut, strandFine;

            #ifdef _STRAND_PROCEDURAL
                // ---- B 分支：程序化细丝，不需要贴图 ----
                // 每层把格子错开（否则 N 层叠成同一张点阵图），流场直接推格子。
                // 注意这里推到物体空间、不是 UV：没有 UV 的网格也能用。
                float3 sc   = i.objBase * _FurDensity
                            + _ShellIndex * float3(1.37, 0.71, 1.93)
                            + float3(drift * _FurDensity * _StrandFlow, 0);
                float3 cell = floor(sc);
                float3 loc  = frac(sc) - 0.5;

                // 关键：只取切向分量。3D 距离 length(loc) 会被球面斜着切开，
                // 毛被剁成随机碎点（看起来像「球上贴圆点」）。
                float3 tang = loc - i.objNrm * dot(loc, i.objNrm);
                float  d    = length(tang);

                float  r      = _FurThickness * (1.0 - t * 0.65);         // 根部粗、尖端细
                strandFine    = 1.0 - smoothstep(r * 0.55, r, d);
                float  lenRan = 0.75 + 0.60 * hash31(cell + 3.7);         // 毛长随机倍率：等长会像刺猬
                float  drop   = step(_CellDropout, hash31(cell + 11.3));  // 随机丢格，打破均匀
                cut = _FurCut * 0.42 * lenRan * drop;
            #else
                // ---- A 分支（推荐）：R = 毛长（天然长短不一）, B = 遮罩 ----
                // 每层采样坐标必须错开，否则 N 层叠成同一张点阵图
                float2 suv = uv0 * _TexScale + _ShellIndex * float2(1.37, 0.71) + drift;
                float4 a4  = tex2D(_FurTex, suv);
                float  lenTex = pow(saturate(a4.r), _LenContrast);
                cut        = lenTex * _FurCut - a4.b * _MaskAmount;
                strandFine = a4.g * 0.5 + 0.75;   // G 通道只做轻微明暗，不参与裁切
            #endif

                float alpha = smoothstep(0.0, _EdgeSoft, cut - t * (1.0 + bite * _TrackBite));
                alpha *= strandFine;
                alpha *= 1.0 - smoothstep(_TipFadeStart, 1.0, t);   // 毛尖自然消散
                clip(alpha - _AlphaCutout);

                // ================= 颜色与光照 =================
                float3 baseCol = lerp(_FurColor.rgb, _FurTipColor.rgb, t) * _Tint.rgb;

                // 毛根压暗：这个明暗层次是「毛绒感」的主要来源。
                // 千万别在这之后再套一层会一起提亮暗部的色调压缩，否则深色五官会消失。
                baseCol *= lerp(1.0 - _RootOcclusion, 1.0, pow(t, 0.75));

                float3 N = normalize(i.worldNrm);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float  ndl = dot(N, L) * 0.5 + 0.5;             // half-lambert：毛发散射，暗面不死黑
                float3 diffuse = _LightColor0.rgb * ndl;
                float3 ambient = ShadeSH9(float4(N, 1.0));

                float3 V   = normalize(_WorldSpaceCameraPos - i.worldPos);
                float  rim = pow(saturate(1.0 - saturate(dot(N, V))), _RimPower);
                float3 rimCol = _RimColor.rgb * rim * (0.35 + t * 0.9);   // 越靠毛尖越透光

                float3 col = baseCol * (diffuse * 0.85 + ambient) + rimCol;
                return fixed4(col, alpha * _FurColor.a);
            }
            ENDCG
        }
    }

    Fallback "Diffuse"
}

// =============================================================================
//  关于「深度写入」（ZWrite On）
// -----------------------------------------------------------------------------
//  本 shader 用 ZWrite On + AlphaTest 队列，并且要求**从内层（_ShellIndex = 0）
//  向外层依次绘制**。对凸网格（球、胶囊）来说这是正确的：
//
//    · 正面半球：外层顶点比内层更靠近相机 → 后画的盖在先画的上面 →
//      由内向外恰好等于「由远及近」，混合顺序正确。
//    · 背面半球：被不透明本体挡在深度后面 → 直接被 ZTest 剔掉，不会透出来。
//    · 轮廓外沿：本体没有覆盖到这里，深度是背景 → 正常绘制。
//
//  如果网格是**凹的**，或者必须用 ZWrite Off + Transparent 队列，
//  请让脚本按「到相机的距离」排序绘制，否则会出现前后颠倒。
// =============================================================================
