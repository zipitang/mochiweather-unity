using UnityEngine;

namespace MochiWeather
{
    /// <summary>
    /// 毛绒玩偶的「生命感」来源 —— 呼吸、眨眼、摇晃、动作、说话。
    ///
    /// 设计原则：
    ///   1. **一切运动都用连续相位驱动**，不用 AnimationClip。
    ///      这样情绪切换时不会「播完一段再切」，而是随时可以插值过去，
    ///      呼吸节奏、毛长、姿态都能平滑过渡。
    ///   2. **姿态变形每帧从基线重算**，不做 `+=` 累加。
    ///      （在 Android 版上踩过这个坑：1.02^60 ≈ 3.3 倍，几秒后球会膨胀到糊满屏）
    ///   3. 情绪决定「底子」（呼吸多快、缩多团、毛多炸），
    ///      动作是叠加在上面的一次性事件，两者互不吞并。
    /// </summary>
    [RequireComponent(typeof(MochiFurBall))]
    [DisallowMultipleComponent]
    public class MochiWeatherAnimator : MonoBehaviour
    {
        [Header("引用")]
        public MochiFurBall ball;

        [Header("呼吸")]
        [Tooltip("呼吸时竖直方向的缩放幅度")]
        [Range(0f, 0.15f)] public float breathAmplitude = 0.035f;

        [Header("眨眼")]
        public float blinkIntervalMin = 2.4f;
        public float blinkIntervalMax = 6.2f;
        [Tooltip("一次眨眼（闭→开）的耗时")]
        public float blinkDuration = 0.13f;

        [Header("闲置摇摆")]
        [Tooltip("身体左右轻晃的角度")]
        public float swayDegrees = 2.6f;
        [Tooltip("摇晃频率")]
        public float swayRate = 0.22f;

        [Header("说话")]
        [Tooltip("说话时嘴部开合频率")]
        public float mouthRate = 14f;

        // ---------------- 状态 ----------------

        private Emotion _emotion = Emotion.Cozy;
        private float _breathPhase;
        private float _swayPhase;

        private float _blinkTimer;
        private bool _blinking;
        private float _blinkT;

        private MochiAction? _action;
        private float _actionT;
        private float _actionDuration;

        private bool _speaking;
        private float _mouthPhase;
        private float _speakPulse;

        // 平滑过渡量（切情绪时不要「啪」地跳过去）
        private float _curlCurrent;
        private float _furScaleCurrent = 1f;
        private Vector3 _basePos;

        public Emotion CurrentEmotion => _emotion;
        public bool IsSpeaking => _speaking;

        // ========================================================

        private void Awake()
        {
            if (ball == null) ball = GetComponent<MochiFurBall>();
        }

        private void Start()
        {
            if (ball == null)
            {
                Debug.LogError("[MochiWeather] MochiWeatherAnimator 找不到 MochiFurBall");
                enabled = false;
                return;
            }
            if (ball.Posture == null) ball.Build();

            _basePos = ball.Posture.localPosition;
            _blinkTimer = Random.Range(blinkIntervalMin, blinkIntervalMax);
            ApplyEmotion(_emotion, instant: true);
        }

        private void Update()
        {
            if (ball == null || ball.Posture == null) return;
            float dt = Time.deltaTime;

            // ---------- 1. 情绪参数平滑逼近 ----------
            float curlTarget = EmotionPalette.CurlAmount(_emotion);
            float furTarget = EmotionPalette.FurLengthScale(_emotion);

            // 动作会临时改写这两个量（例如受惊时毛全部炸开）
            GetActionModifiers(out float curlBias, out float furBias);

            _curlCurrent = Mathf.Lerp(_curlCurrent, Mathf.Clamp01(curlTarget + curlBias), 1f - Mathf.Exp(-6f * dt));
            _furScaleCurrent = Mathf.Lerp(_furScaleCurrent, furTarget + furBias, 1f - Mathf.Exp(-6f * dt));

            // ---------- 2. 呼吸 ----------
            float breathRate = EmotionPalette.BreathRate(_emotion);
            _breathPhase += breathRate * Mathf.PI * 2f * dt;
            if (_breathPhase > Mathf.PI * 2f) _breathPhase -= Mathf.PI * 2f;

            float breath = Mathf.Sin(_breathPhase);
            // 说话时呼吸更急促一点，像在喘气
            float breathAmp = breathAmplitude * (1f + _speakPulse * 0.8f)
                              * Mathf.Lerp(0.6f, 1f, 1f - _curlCurrent * 0.5f);

            // 收缩姿态：整体压扁并下沉
            float squash = 1f - _curlCurrent * 0.14f;
            float scaleY = (1f + breath * breathAmp) * squash;
            float scaleXZ = (1f - breath * breathAmp * 0.55f) * (1f + _curlCurrent * 0.06f);

            // ---------- 3. 闲置摇摆 + 动作姿态 ----------
            _swayPhase += swayRate * Mathf.PI * 2f * dt;
            float sway = Mathf.Sin(_swayPhase) * swayDegrees * (1f - _curlCurrent * 0.5f);

            GetActionTransform(out Vector3 posOffset, out Vector3 eulerOffset, out float extraSquash);

            // 每帧从基线重算 —— 绝不用 += 累加
            var posture = ball.Posture;
            posture.localScale = new Vector3(scaleXZ, scaleY * extraSquash.y, scaleXZ * extraSquash.x);
            posture.localPosition = _basePos
                                    + new Vector3(0f, posOffset.y - _curlCurrent * ball.bodyRadius * 0.10f, 0f);
            posture.localRotation = Quaternion.Euler(
                eulerOffset.x + _curlCurrent * 6f,
                eulerOffset.y + sway,
                eulerOffset.z + Mathf.Sin(_swayPhase * 1.7f) * 1.4f);

            // ---------- 4. 眨眼 ----------
            UpdateBlink(dt);

            // ---------- 5. 说话 ----------
            _speakPulse = Mathf.Lerp(_speakPulse, _speaking ? 1f : 0f, 1f - Mathf.Exp(-8f * dt));
            UpdateMouth(dt);

            // ---------- 6. 毛长（情绪决定，动作叠加） ----------
            ball.SetFurLengthScale(_furScaleCurrent);

            // ---------- 7. 接触阴影跟着高度走 ----------
            float lift = Mathf.Max(0f, posOffset.y) / Mathf.Max(0.0001f, ball.bodyRadius);
            ball.SetShadowStrength(Mathf.Clamp01(1f - lift * 0.9f) * (1f - _curlCurrent * 0.15f));

            // ---------- 8. 动作计时 ----------
            if (_action.HasValue)
            {
                _actionT += dt;
                if (_actionT >= _actionDuration) _action = null;
            }
        }

        // ========================================================
        // 对外接口
        // ========================================================

        /// <summary>切换情绪（云端 emotion 字段直接喂进来）。</summary>
        public void SetEmotion(Emotion e)
        {
            if (_emotion == e) return;
            _emotion = e;
            ApplyEmotion(e, instant: false);
        }

        /// <summary>切换情绪（字符串来自服务端，如 "happy" / "sleepy"）。</summary>
        public void SetEmotion(string key) => SetEmotion(EmotionPalette.ParseEmotion(key));

        /// <summary>触发一次性动作（云端 action 字段）。</summary>
        public void PlayAction(MochiAction a)
        {
            _action = a;
            _actionT = 0f;
            _actionDuration = ActionDuration(a);

            // 动作一开头的两三帧就该看出形变，所以这里不做缓入
            if (a == MochiAction.FurStand) ball.SetFurRise(1f);
        }

        public void PlayAction(string key) => PlayAction(EmotionPalette.ParseAction(key));

        /// <summary>说话开关（驱动嘴部与呼吸节奏）。</summary>
        public void SetSpeaking(bool speaking) => _speaking = speaking;

        /// <summary>被戳一下 —— 最轻量的即时反馈，不需要走云端。</summary>
        public void Poke()
        {
            PlayAction(MochiAction.Bounce);
            ball.SetMouthOpen(0.6f);
        }

        /// <summary>让玩偶看向世界坐标里的某个点（例如鼠标 / 手指）。</summary>
        public void LookAt(Vector3 worldPoint) => ball.LookAt(worldPoint);

        /// <summary>风力（由天气驱动），direction 为世界方向。</summary>
        public void SetWind(Vector3 direction, float strength) => ball.SetWind(direction, strength);

        // ========================================================
        // 内部
        // ========================================================

        private void ApplyEmotion(Emotion e, bool instant)
        {
            ball.SetFurTint(EmotionPalette.BodyTint(e));
            if (instant)
            {
                _curlCurrent = EmotionPalette.CurlAmount(e);
                _furScaleCurrent = EmotionPalette.FurLengthScale(e);
            }
            // 害怕 / 好冷 → 毛自然竖起
            float rise = (e == Emotion.Scared) ? 0.55f
                       : (e == Emotion.Cold) ? 0.30f
                       : 0f;
            ball.SetFurRise(rise);
        }

        private void UpdateBlink(float dt)
        {
            if (_blinking)
            {
                _blinkT += dt;
                float k = _blinkT / blinkDuration;
                if (k >= 1f)
                {
                    _blinking = false;
                    ball.SetEyeOpen(1f);
                    _blinkTimer = Random.Range(blinkIntervalMin, blinkIntervalMax);
                }
                else
                {
                    // 闭 → 开，用 sin 让中段保持闭合
                    ball.SetEyeOpen(Mathf.Abs(Mathf.Cos(k * Mathf.PI)));
                }
                return;
            }

            // 睡着了就不眨眼了 —— 眯成一条线
            if (_emotion == Emotion.Sleepy) { ball.SetEyeOpen(0.12f); return; }

            _blinkTimer -= dt;
            if (_blinkTimer <= 0f)
            {
                _blinking = true;
                _blinkT = 0f;
            }
        }

        private void UpdateMouth(float dt)
        {
            if (_speakPulse > 0.01f)
            {
                _mouthPhase += mouthRate * Mathf.PI * 2f * dt;
                // 两个不同频率叠加，避免「节拍器」式的机械感
                float open = (Mathf.Sin(_mouthPhase) * 0.5f + 0.5f) * 0.75f
                             + (Mathf.Sin(_mouthPhase * 2.7f) * 0.5f + 0.5f) * 0.25f;
                ball.SetMouthOpen(open * _speakPulse);
            }
            else if (!_action.HasValue)
            {
                ball.SetMouthOpen(0f);
            }
        }

        private static float ActionDuration(MochiAction a)
        {
            switch (a)
            {
                case MochiAction.Bounce:   return 0.85f;
                case MochiAction.CurlUp:   return 1.60f;
                case MochiAction.Sneeze:   return 0.75f;
                case MochiAction.FurStand: return 1.30f;
                case MochiAction.Sleep:    return 2.40f;
                case MochiAction.Shake:    return 1.10f;
                case MochiAction.Wave:     return 1.60f;
                default:                   return 1.0f;
            }
        }

        /// <summary>动作对「缩团度 / 毛长」的偏置。</summary>
        private void GetActionModifiers(out float curlBias, out float furBias)
        {
            curlBias = 0f;
            furBias = 0f;
            if (!_action.HasValue) return;

            float t = Mathf.Clamp01(_actionT / Mathf.Max(0.0001f, _actionDuration));
            switch (_action.Value)
            {
                case MochiAction.CurlUp:
                    // 缩到最紧再松开
                    curlBias = Mathf.Sin(t * Mathf.PI) * 0.45f;
                    break;
                case MochiAction.FurStand:
                    furBias = Mathf.Sin(t * Mathf.PI) * 0.45f;
                    break;
                case MochiAction.Sneeze:
                    // 打喷嚏的瞬间毛炸一下
                    furBias = Mathf.Exp(-t * 6f) * 0.35f;
                    break;
                case MochiAction.Shake:
                    furBias = Mathf.Sin(t * Mathf.PI) * 0.15f;
                    break;
                case MochiAction.Sleep:
                    curlBias = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t * 2.2f)) * 0.5f;
                    break;
            }
        }

        /// <summary>动作对「位置 / 旋转 / 额外挤压」的贡献。</summary>
        private void GetActionTransform(out Vector3 posOffset, out Vector3 eulerOffset, out Vector3 squash)
        {
            posOffset = Vector3.zero;
            eulerOffset = Vector3.zero;
            squash = Vector3.one;
            if (!_action.HasValue) return;

            float t = Mathf.Clamp01(_actionT / Mathf.Max(0.0001f, _actionDuration));
            float r = ball.bodyRadius;

            switch (_action.Value)
            {
                case MochiAction.Bounce:
                {
                    // 阻尼正弦：弹起来再落回去，落地时压扁一点
                    float h = Mathf.Sin(t * Mathf.PI) * r * 0.85f;
                    float land = Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 2f)) ;
                    posOffset.y = h;
                    squash = new Vector3(1f + land * 0.10f, 1f - land * 0.12f, 1f + land * 0.10f);
                    break;
                }
                case MochiAction.Sneeze:
                {
                    // 后仰蓄力 → 前甩
                    float back = Mathf.Sin(Mathf.Min(t, 0.45f) / 0.45f * Mathf.PI * 0.5f);
                    float fwd = t > 0.45f ? Mathf.Sin((t - 0.45f) / 0.55f * Mathf.PI) : 0f;
                    eulerOffset.x = -back * 9f + fwd * 14f;
                    posOffset.y = back * r * 0.05f - fwd * r * 0.06f;
                    break;
                }
                case MochiAction.Shake:
                {
                    // 高频抖动，幅度指数衰减
                    float decay = Mathf.Exp(-t * 5f);
                    float j = Mathf.Sin(t * 46f) * decay;
                    eulerOffset.z = j * 7f;
                    posOffset.x = j * r * 0.045f;
                    break;
                }
                case MochiAction.Wave:
                {
                    // 左右点头打招呼
                    float decay = Mathf.Exp(-t * 2.2f);
                    eulerOffset.y = Mathf.Sin(t * Mathf.PI * 4f) * 16f * decay;
                    posOffset.y = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * r * 0.10f;
                    break;
                }
                case MochiAction.CurlUp:
                {
                    posOffset.y = -Mathf.Sin(t * Mathf.PI) * r * 0.16f;
                    break;
                }
                case MochiAction.Sleep:
                {
                    // 缓缓躺下
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t * 2.0f));
                    eulerOffset.z = k * 12f;
                    posOffset.y = -k * r * 0.12f;
                    break;
                }
                case MochiAction.FurStand:
                {
                    posOffset.y = Mathf.Sin(t * Mathf.PI) * r * 0.06f;
                    break;
                }
            }
        }
    }
}
