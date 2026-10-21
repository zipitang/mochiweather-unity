using System.Collections.Generic;
using UnityEngine;

namespace MochiWeather
{
    /// <summary>
    /// 毛绒球本体 —— 程序化搭建「身体 + 多层毛发 + 五官」。
    ///
    /// 结构：
    ///   MochiRoot
    ///    ├─ Posture        姿态节点（缩团 / 挤压拉伸 / 倾斜都作用在这一层）
    ///    │   ├─ Body       身体球（MochiBody 着色器）
    ///    │   ├─ Fur        毛发壳层容器（N 层同网格球，逐层沿法线外推）
    ///    │   └─ Face       五官
    ///    └─ ContactShadow  地面柔影（不跟着姿态变形）
    ///
    /// 之所以把「姿态」和「毛发」分两个节点：姿态是整体形变，
    /// 毛发需要的是「每层的法线偏移 + 风的方向」，两者混在一起会互相打架。
    /// </summary>
    [DisallowMultipleComponent]
    public class MochiFurBall : MonoBehaviour
    {
        [Header("尺寸")]
        [Tooltip("身体球半径（世界单位）")]
        public float bodyRadius = 0.5f;

        [Header("毛发")]
        [Tooltip("毛发壳层数量。40 起才饱满；20 层边缘能看出「碎屑感」")]
        [Range(4, 64)] public int shellCount = 40;
        [Tooltip("毛长（球半径 0.5 时给 0.085 ≈ 17%，真实绒毛量级是 15%~20%）")]
        [Range(0.01f, 0.5f)] public float furLength = 0.085f;
        [Tooltip("细丝密度（切向格数）。只在程序化分支里用得上")]
        [Range(1f, 80f)] public float furDensity = 26f;
        [Tooltip("细分级别。3 → 1280 面，足够撑起毛发")]
        [Range(1, 4)] public int subdivisions = 3;

        [Header("毛皮贴图（由 skill 的 gen_fur_textures.py 生成，放在 Resources 下）")]
        [Tooltip("毛长图：R = 毛长（长短天然不一）G = 细丝 B = 遮罩露本体")]
        public string furTexPath = "Fur/tex_fur";
        [Tooltip("风场图：RG = 流向")]
        public string windTexPath = "Fur/tex_wind";

        [Header("毛长图采样 · 最容易调错的一组")]
        [Tooltip("平铺次数。球面 UV 下 9 左右；给 1~3 会让正面全是大色块，怎么调都像云")]
        public float texScale = 9.0f;
        [Tooltip("毛长阈值 = 疏密总开关")]
        public float furCut = 1.15f;
        [Tooltip("毛长对比度：把灰度压成离散斑块，边界才会碎成「一撮一撮」，而不是等高线")]
        public float lenContrast = 1.35f;
        [Tooltip("B 通道遮罩强度（露出本体球）")]
        public float maskAmount = 0.35f;
        [Tooltip("毛尖开始淡出的高度。低于 0.72 会让最外几层几乎全透明，看起来像雾")]
        public float tipFadeStart = 0.82f;

        [Header("UV 流场风 · 几何一动不动，所以不会撕开层间缝隙")]
        public float windScale = 1.6f;
        public float windSpeed = 0.35f;
        [Tooltip("整体飘动幅度")]
        public float windIntens = 0.022f;
        [Tooltip("越大越只有毛尖在动")]
        public float windInfluence = 1.2f;
        [Tooltip("打散强度：打破平铺带来的重复感")]
        public float styleAmount = 0.030f;

        [Header("造型")]
        [Tooltip("统一倾倒方向。缺了它正面只能看到毛的横截面，像一块织物")]
        public Vector3 groomDir = new Vector3(0.35f, -0.15f, 0.92f);
        [Range(0f, 0.5f)] public float groomAmount = 0.10f;
        [Tooltip("毛根压暗。这个明暗层次是「毛绒感」的主要来源")]
        [Range(0f, 1.5f)] public float rootOcclusion = 0.85f;

        [Header("没有贴图时的兜底")]
        [Tooltip("找不到毛皮贴图时自动切到程序化细丝分支（用的是修掉 bug 的切向距离裁切）")]
        public bool fallbackToProceduralStrand = true;

        [Header("外观")]
        public Color furRootColor = new Color(0.99f, 0.93f, 0.87f);
        public Color furTipColor  = new Color(1.00f, 0.97f, 0.92f);
        public Color bodyColor    = new Color(0.99f, 0.94f, 0.89f);
        public Color bodyShade    = new Color(0.94f, 0.84f, 0.78f);
        public Color faceColor    = new Color(0.28f, 0.20f, 0.18f);

        // ---- 运行时 ----
        private Transform _posture;
        private Transform _face;
        private readonly List<MeshRenderer> _shells = new List<MeshRenderer>();
        private Material _furMat;
        private Material _bodyMat;
        private Material _faceMat;
        private Material _shadowMat;

        private Transform _eyeL, _eyeR, _mouth;
        private Transform _shadow;

        private float _furLengthScale = 1f;
        private float _riseAmount;         // 竖直方向「毛竖起」
        private Vector3 _windDir = Vector3.right;
        private float _windStrength = 0.1f;

        private Mesh _sphereMesh;

        public Transform Posture => _posture;
        public Transform FaceRoot => _face;

        // ========================================================
        // 构建
        // ========================================================

        /// <summary>程序化生成整个毛球。构造完可以直接用，不需要美术资源。</summary>
        public void Build()
        {
            _sphereMesh = MeshFactory.CreateIcosphere(Mathf.Clamp(subdivisions, 1, 4), bodyRadius);

            // ---------- 材质 ----------
            Shader furShader = Shader.Find("MochiWeather/FurShell");
            Shader bodyShader = Shader.Find("MochiWeather/MochiBody");
            Shader blobShader = Shader.Find("MochiWeather/ShadowBlob");

            _furMat = new Material(furShader) { name = "MochiFur" };
            _furMat.SetColor("_FurColor", furRootColor);
            _furMat.SetColor("_FurTipColor", furTipColor);
            _furMat.SetFloat("_ShellCount", shellCount);
            _furMat.SetFloat("_FurDensity", furDensity);
            _furMat.SetFloat("_FurLength", furLength);
            ApplyFurQualitySettings();

            _bodyMat = new Material(bodyShader) { name = "MochiBodyMat" };
            _bodyMat.SetColor("_BaseColor", bodyColor);
            _bodyMat.SetColor("_ShadeColor", bodyShade);

            _faceMat = new Material(bodyShader) { name = "MochiFaceMat" };
            _faceMat.SetColor("_BaseColor", faceColor);
            _faceMat.SetColor("_ShadeColor", faceColor * 0.6f);
            _faceMat.SetFloat("_RimStrength", 0.25f);
            _faceMat.SetFloat("_SSSStrength", 0.1f);

            _shadowMat = new Material(blobShader) { name = "MochiShadowMat" };

            // ---------- 节点层级 ----------
            _posture = new GameObject("Posture").transform;
            _posture.SetParent(transform, false);

            // 身体
            var body = new GameObject("Body");
            body.transform.SetParent(_posture, false);
            var bodyFilter = body.AddComponent<MeshFilter>();
            bodyFilter.sharedMesh = _sphereMesh;
            var bodyRenderer = body.AddComponent<MeshRenderer>();
            bodyRenderer.sharedMaterial = _bodyMat;
            bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bodyRenderer.receiveShadows = false;

            // 毛发壳层
            var furRoot = new GameObject("Fur");
            furRoot.transform.SetParent(_posture, false);
            _shells.Clear();
            for (int i = 0; i < shellCount; i++)
            {
                var shell = new GameObject($"Shell_{i:D2}");
                shell.transform.SetParent(furRoot.transform, false);
                var mf = shell.AddComponent<MeshFilter>();
                mf.sharedMesh = _sphereMesh;
                var mr = shell.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _furMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                // 每层用 MaterialPropertyBlock 单独指定壳层索引，
                // 这样 N 层共享同一个材质，省下 N 份材质实例。
                var mpb = new MaterialPropertyBlock();
                mr.GetPropertyBlock(mpb);
                mpb.SetFloat("_ShellIndex", i);
                mr.SetPropertyBlock(mpb);

                _shells.Add(mr);
            }

            // 五官
            _face = new GameObject("Face").transform;
            _face.SetParent(_posture, false);
            BuildFace();

            // 接触阴影
            var shadow = new GameObject("ContactShadow");
            shadow.transform.SetParent(transform, false);
            var shadowFilter = shadow.AddComponent<MeshFilter>();
            shadowFilter.sharedMesh = MeshFactory.CreateQuad(1f);
            var shadowRenderer = shadow.AddComponent<MeshRenderer>();
            shadowRenderer.sharedMaterial = _shadowMat;
            shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shadowRenderer.receiveShadows = false;

            // 方片默认躺在 XY 平面，转成平铺在地面
            shadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shadow.transform.localPosition = new Vector3(0f, -bodyRadius * 1.02f, 0f);
            shadow.transform.localScale = new Vector3(bodyRadius * 3.0f, bodyRadius * 3.0f, 1f);
            _shadow = shadow.transform;

            ApplyStaticVisuals();
        }

        /// <summary>构建眼睛与嘴。</summary>
        private void BuildFace()
        {
            // 眼睛：两个扁球，贴在球面偏前的位置
            _eyeL = CreateEye("EyeL", new Vector3(-0.20f, 0.10f, 0.86f));
            _eyeR = CreateEye("EyeR", new Vector3(0.20f, 0.10f, 0.86f));

            // 嘴：一段向上的弧线（微笑）
            var mouth = new GameObject("Mouth");
            mouth.transform.SetParent(_face, false);
            var mf = mouth.AddComponent<MeshFilter>();
            mf.sharedMesh = MeshFactory.CreateArcStrip(
                radius: 0.13f, thickness: 0.030f,
                startDeg: 205f, endDeg: 335f,
                zOffset: 0f, segments: 20);
            var mr = mouth.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _faceMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mouth.transform.localPosition = new Vector3(0f, -0.22f, 0.90f);
            _mouth = mouth.transform;
        }

        private Transform CreateEye(string name, Vector3 dir)
        {
            var eye = new GameObject(name);
            eye.transform.SetParent(_face, false);

            var mf = eye.AddComponent<MeshFilter>();
            mf.sharedMesh = _sphereMesh; // 复用同一个球网格，只做缩放

            var mr = eye.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _faceMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            Vector3 n = dir.normalized;
            eye.transform.localPosition = n * (bodyRadius * 1.01f);
            // 让眼睛朝外，同时保持竖直（玩偶的眼睛不该歪）
            eye.transform.localRotation = Quaternion.LookRotation(n, Vector3.up);
            eye.transform.localScale = new Vector3(
                bodyRadius * 0.20f, bodyRadius * 0.26f, bodyRadius * 0.10f);
            return eye.transform;
        }

        private void ApplyStaticVisuals()
        {
            SetFurTint(Color.white);
            SetFurLengthScale(1f);
        }

        /// <summary>
        /// 把「毛长图 + UV 流场 + 倾倒」这一整套参数写进材质。
        ///
        /// 这是毛绒质感的关键所在，比壳层数量重要得多：
        /// 毛长图一换，整只玩偶的「毛感」就完全不一样了。
        /// </summary>
        private void ApplyFurQualitySettings()
        {
            if (_furMat == null) return;

            var furTex  = Resources.Load<Texture2D>(furTexPath);
            var windTex = Resources.Load<Texture2D>(windTexPath);

            if (furTex != null && windTex != null)
            {
                _furMat.SetTexture("_FurTex", furTex);
                _furMat.SetTexture("_WindTex", windTex);
                _furMat.DisableKeyword("_STRAND_PROCEDURAL");
            }
            else if (fallbackToProceduralStrand)
            {
                // 没有贴图时不静默退化成一个光滑的毛球 —— 那会被当成「毛没渲染出来」。
                // 切到程序化细丝分支，并明确说清楚质感会差一档。
                _furMat.EnableKeyword("_STRAND_PROCEDURAL");
                _furMat.SetFloat("_FurThickness", 0.24f);
                _furMat.SetFloat("_StrandFlow", 0.25f);
                Debug.LogWarning(
                    "[MochiFurBall] 未找到毛皮贴图（Resources/" + furTexPath + " 与 " +
                    windTexPath + "），已退回程序化细丝分支 —— 能看，但质感明显更差。" +
                    "用 skills/fur-shell-rendering/scripts/gen_fur_textures.py 生成即可。");
            }

            _furMat.SetFloat("_TexScale", texScale);
            _furMat.SetFloat("_FurCut", furCut);
            _furMat.SetFloat("_LenContrast", lenContrast);
            _furMat.SetFloat("_MaskAmount", maskAmount);
            _furMat.SetFloat("_TipFadeStart", tipFadeStart);
            _furMat.SetFloat("_WindScale", windScale);
            _furMat.SetFloat("_WindSpeed", windSpeed);
            _furMat.SetFloat("_WindIntens", windIntens);
            _furMat.SetFloat("_WindInfluence", windInfluence);
            _furMat.SetFloat("_StyleAmount", styleAmount);
            _furMat.SetVector("_GroomDir", new Vector4(groomDir.x, groomDir.y, groomDir.z, 0f));
            _furMat.SetFloat("_GroomAmount", groomAmount);
            _furMat.SetFloat("_RootOcclusion", rootOcclusion);
        }

        // ========================================================
        // 对外参数（由动画 / 天气驱动）
        // ========================================================

        /// <summary>整体色调（情绪 / 天气换色）。</summary>
        public void SetFurTint(Color tint)
        {
            if (_furMat != null) _furMat.SetColor("_Tint", tint);
            if (_bodyMat != null) _bodyMat.SetColor("_Tint", tint);
        }

        /// <summary>毛长倍率（情绪越激动毛越炸）。</summary>
        public void SetFurLengthScale(float scale)
        {
            _furLengthScale = Mathf.Max(0.05f, scale);
            PushFurParams();
        }

        /// <summary>毛发摆动（风）。dir 为世界方向。</summary>
        public void SetWind(Vector3 dir, float strength)
        {
            _windDir = dir.sqrMagnitude < 0.0001f ? Vector3.right : dir.normalized;
            _windStrength = Mathf.Max(0f, strength);
            PushFurParams();
        }

        /// <summary>毛竖起（害怕 / 寒风）。0~1。</summary>
        public void SetFurRise(float amount)
        {
            _riseAmount = Mathf.Clamp01(amount);
            PushFurParams();
        }

        private void PushFurParams()
        {
            if (_furMat == null) return;
            _furMat.SetFloat("_FurLength", furLength * _furLengthScale);

            // 风：**不再位移几何**。几何位移会把壳层推开、把层间的断面露出来，
            // 斜着看就是一层碎屑。现在风全部交给 UV 流场：
            //   _WindDirection.xy 决定流场扫过的方向，_WindIntens 决定飘动幅度。
            // 因为一个顶点都不动，所以永远不会撕开层与层之间的缝。
            _furMat.SetVector("_WindDirection",
                new Vector4(_windDir.x, _windDir.y, _windDir.z, 0f));
            _furMat.SetFloat("_WindIntens",
                windIntens * Mathf.Clamp(0.35f + _windStrength * 6f, 0.35f, 3.5f));
            _furMat.SetFloat("_WindStrength", 0f);   // 旧版几何倾倒项：关掉

            // 「毛竖起」= 毛尖外扩 + 减少倾倒。两者一起变才有「炸毛」的感觉。
            _furMat.SetFloat("_Fluffiness", 1f + _riseAmount * 0.9f);
            _furMat.SetFloat("_GroomAmount", groomAmount * (1f - _riseAmount * 0.85f));
        }

        private void Update()
        {
            // 风靠 _Time.y 推进，而 EditMode 下 _Time 不保证在走 —— 编辑器里调参时
            // 会看到毛一动不动。补一个时间偏移进来，让两种模式都能看到毛在飘。
            // 所有壳层共享同一个材质，所以这里一帧只写一次。
            if (_furMat != null)
                _furMat.SetFloat("_TimeOffset", Time.realtimeSinceStartup);
        }

        /// <summary>睁开程度 0(闭)~1(睁)。</summary>
        public void SetEyeOpen(float openness)
        {
            float o = Mathf.Clamp01(openness);
            if (_eyeL != null)
                _eyeL.localScale = new Vector3(
                    _eyeL.localScale.x, bodyRadius * 0.26f * o, _eyeL.localScale.z);
            if (_eyeR != null)
                _eyeR.localScale = new Vector3(
                    _eyeR.localScale.x, bodyRadius * 0.26f * o, _eyeR.localScale.z);
        }

        /// <summary>张嘴程度 0~1（说话时用）。</summary>
        public void SetMouthOpen(float amount)
        {
            if (_mouth == null) return;
            float a = Mathf.Clamp01(amount);
            _mouth.localScale = new Vector3(1f + a * 0.15f, 0.6f + a * 1.6f, 1f);
        }

        /// <summary>接触阴影的浓度（离地越高越淡）。</summary>
        public void SetShadowStrength(float s)
        {
            if (_shadowMat != null) _shadowMat.SetFloat("_Strength", Mathf.Clamp01(s));
        }

        /// <summary>朝向某个方向（让玩偶「看向」目标）。</summary>
        public void LookAt(Vector3 worldTarget)
        {
            if (_face == null) return;
            Vector3 local = _posture.InverseTransformPoint(worldTarget);
            local.y *= 0.35f; // 限制竖直方向的转动，避免翻白眼
            if (local.sqrMagnitude < 0.0001f) return;
            Quaternion target = Quaternion.LookRotation(local.normalized, Vector3.up);
            _face.localRotation = Quaternion.Slerp(_face.localRotation, target, 0.12f);
        }
    }
}
