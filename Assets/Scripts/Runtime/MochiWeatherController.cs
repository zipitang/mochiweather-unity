using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace MochiWeather
{
    // ============================================================
    // 服务端数据契约
    // ------------------------------------------------------------
    // 用 JsonUtility 解析，所以这里必须是「固定字段名」的类。
    // 字段名要和服务端 schemas.py 的 snake_case 完全一致。
    // ============================================================

    [Serializable]
    public class ApiEnvelope
    {
        public bool ok;
        public string message;
    }

    [Serializable]
    public class SceneEnvelope
    {
        public bool ok;
        public string message;
        public ScenePayload data;
    }

    [Serializable]
    public class ScenePayload
    {
        public WeatherPayload weather;
        public EmotionPayload emotion;
    }

    [Serializable]
    public class WeatherPayload
    {
        public string condition;
        public string text;
        public float temperature;
        public float feels_like;
        public int humidity;
        public float wind_speed;
        public string wind_dir;
        public string city;
        public string source;
        public float temp_max;
        public float temp_min;
        public string jie_qi;
    }

    [Serializable]
    public class EmotionPayload
    {
        public string emotion;
        public string action;
        public string dialogue;
        public string weather_tip;
        public float intensity;
        public string source;
    }

    [Serializable]
    public class ChatEnvelope
    {
        public bool ok;
        public string message;
        public ChatPayload data;
    }

    [Serializable]
    public class ChatPayload
    {
        public string reply;
        public string emotion;
        public string action;
        public string source;
    }

    internal class WeatherQueryBody
    {
        public string city;
    }

    internal class ChatRequestBody
    {
        public string user_id;
        public string message;
    }

    // ============================================================

    /// <summary>
    /// 把云端「天气 + 情绪」拉下来，转成玩偶的表演。
    ///
    /// 这是 Unity 端唯一的网络入口。设计要点：
    ///   1. **定时轮询而非长连接** —— 天气变化是分钟级的，轮询足够且最简单；
    ///   2. **服务端不可达时自动切到本地场景** —— 工程在没有网、没有后端时
    ///      打开也不该是一片死寂，会退化成一份内置的「晴天惬意」数据；
    ///   3. 情绪/动作/天气全部由服务端决定，Unity 这边不做任何业务判断，
    ///      保持「一个大脑在云端，多个端只是显示器」的架构。
    /// </summary>
    [DisallowMultipleComponent]
    public class MochiWeatherController : MonoBehaviour
    {
        [Header("引用")]
        public MochiFurBall ball;
        public MochiWeatherAnimator animator;
        [Tooltip("可选的背景天幕（挂 MochiBackdrop 材质的 Renderer），会随天气换色")]
        public Renderer backdrop;

        [Header("服务端")]
        [Tooltip("MochiWeather-Server 地址")]
        public string apiBaseUrl = "http://127.0.0.1:8000";
        [Tooltip("与服务端 .env 里的 MOCHI_API_TOKEN 一致")]
        public string apiToken = "mochi-dev-token";
        [Tooltip("关注的城市")]
        public string city = "无锡";
        [Tooltip("用户标识，用于情绪日记与对话记忆")]
        public string userId = "unity";

        [Header("刷新")]
        [Tooltip("自动刷新间隔（秒）。天气是分钟级变化，600s 足够")]
        public float refreshSeconds = 600f;
        [Tooltip("单个请求超时（秒）")]
        public float requestTimeout = 20f;
        [Tooltip("启动即刷新一次")]
        public bool refreshOnStart = true;

        [Header("调试")]
        [Tooltip("不联网，直接用内置的假数据驱动动画")]
        public bool offlineMock = false;

        /// <summary>场景刷新完成时触发，UI 层可以订阅它来更新文字。</summary>
        public event Action<WeatherPayload, EmotionPayload> OnSceneUpdated;

        /// <summary>对话回复到达时触发（第一个参数是台词）。</summary>
        public event Action<string> OnReply;

        public WeatherPayload LastWeather { get; private set; }
        public EmotionPayload LastEmotion { get; private set; }
        public bool LastRequestFailed { get; private set; }

        private Coroutine _loop;

        // ========================================================

        private void Awake()
        {
            if (ball == null) ball = GetComponentInChildren<MochiFurBall>();
            if (animator == null) animator = GetComponentInChildren<MochiWeatherAnimator>();
        }

        private void OnEnable()
        {
            if (offlineMock)
            {
                ApplyMockScene();
                return;
            }
            if (refreshOnStart) RefreshNow();
            if (_loop == null) _loop = StartCoroutine(RefreshLoop());
        }

        private void OnDisable()
        {
            if (_loop != null) { StopCoroutine(_loop); _loop = null; }
        }

        /// <summary>立刻刷一次（面板上改完城市可以手动调它）。</summary>
        public void RefreshNow()
        {
            StopAllCoroutines();
            _loop = null;
            StartCoroutine(FetchScene());
            if (!offlineMock) _loop = StartCoroutine(RefreshLoop());
        }

        private IEnumerator RefreshLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(Mathf.Max(30f, refreshSeconds));
                yield return FetchScene();
            }
        }

        // ========================================================
        // 天气 + 情绪
        // ========================================================

        private IEnumerator FetchScene()
        {
            string url = $"{apiBaseUrl.TrimEnd('/')}/api/scene?user_id={UnityWebRequest.EscapeURL(userId)}";
            string body = JsonUtility.ToJson(new WeatherQueryBody { city = city });

            using (var req = BuildPost(url, body))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    LastRequestFailed = true;
                    Debug.LogWarning($"[MochiWeather] 场景请求失败（{req.responseCode} {req.error}），改用本地场景");
                    ApplyMockScene();
                    yield break;
                }

                SceneEnvelope env = null;
                try { env = JsonUtility.FromJson<SceneEnvelope>(req.downloadHandler.text); }
                catch (Exception e) { Debug.LogWarning($"[MochiWeather] 场景解析失败：{e.Message}"); }

                if (env == null || !env.ok || env.data == null || env.data.weather == null)
                {
                    LastRequestFailed = true;
                    ApplyMockScene();
                    yield break;
                }

                LastRequestFailed = false;
                ApplyScene(env.data.weather, env.data.emotion);
            }
        }

        private void ApplyScene(WeatherPayload w, EmotionPayload e)
        {
            LastWeather = w;
            LastEmotion = e;

            if (animator != null)
            {
                if (e != null && !string.IsNullOrEmpty(e.emotion))
                    animator.SetEmotion(e.emotion);

                // 风力直接驱动毛发摆动：风越大毛越飘
                float wind = Mathf.Clamp01((w.wind_speed / 40f)) * 0.8f + 0.12f;
                animator.SetWind(WindDirection(w.wind_dir), wind);
            }

            ApplyBackdrop(w, IsNight());

            Debug.Log($"[MochiWeather] 场景已更新：{w.city} {w.text} {w.temperature}°C " +
                      $"（源={w.source}，情绪={e?.emotion}/{e?.action}）");

            OnSceneUpdated?.Invoke(w, e);
        }

        /// <summary>内置场景 —— 服务端不可达时也不至于「死机脸」。</summary>
        private void ApplyMockScene()
        {
            var w = new WeatherPayload
            {
                condition = "sunny",
                text = "晴",
                temperature = 24f,
                feels_like = 24f,
                humidity = 55,
                wind_speed = 8f,
                wind_dir = "东北",
                city = string.IsNullOrEmpty(city) ? "本地" : city,
                source = "unity-mock",
                temp_max = 27f,
                temp_min = 16f
            };
            var e = new EmotionPayload
            {
                emotion = "cozy",
                action = "wave",
                dialogue = "唔…先自己待一会儿~",
                intensity = 0.3f,
                source = "unity-mock"
            };
            ApplyScene(w, e);
        }

        // ========================================================
        // 对话
        // ========================================================

        /// <summary>
        /// 说一句话给麻薯，拿到回复后自动触发 [OnReply] 并让玩偶「说」出来。
        /// 语音链路由外层负责：ASR 转文字 → 这里 → TTS 读 reply。
        /// </summary>
        public void SendMessage(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            StartCoroutine(FetchReply(text));
        }

        private IEnumerator FetchReply(string text)
        {
            string url = $"{apiBaseUrl.TrimEnd('/')}/api/chat";
            string body = JsonUtility.ToJson(new ChatRequestBody { user_id = userId, message = text });

            using (var req = BuildPost(url, body))
            {
                if (animator != null) animator.SetSpeaking(true);

                yield return req.SendWebRequest();

                if (animator != null) animator.SetSpeaking(false);

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[MochiWeather] 对话请求失败：{req.error}");
                    yield break;
                }

                ChatEnvelope env = null;
                try { env = JsonUtility.FromJson<ChatEnvelope>(req.downloadHandler.text); }
                catch (Exception ex) { Debug.LogWarning($"[MochiWeather] 对话解析失败：{ex.Message}"); }

                if (env == null || !env.ok || env.data == null) yield break;

                if (animator != null)
                {
                    animator.SetEmotion(env.data.emotion);
                    animator.PlayAction(env.data.action);
                }

                Debug.Log($"[MochiWeather] 麻薯回复：{env.data.reply}（源={env.data.source}）");
                OnReply?.Invoke(env.data.reply);
            }
        }

        // ========================================================

        private UnityWebRequest BuildPost(string url, string json)
        {
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(payload);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", $"Bearer {apiToken}");
            req.timeout = Mathf.Max(3, Mathf.RoundToInt(requestTimeout));
            return req;
        }

        /// <summary>让天幕随天气换色。</summary>
        private void ApplyBackdrop(WeatherPayload w, bool night)
        {
            if (backdrop == null) return;
            EmotionPalette.SkyColors(w.condition, night, out Color bottom, out Color top);

            var mat = backdrop.material;
            if (mat == null) return;
            if (mat.HasProperty("_BottomColor")) mat.SetColor("_BottomColor", bottom);
            if (mat.HasProperty("_TopColor")) mat.SetColor("_TopColor", top);
        }

        private static bool IsNight()
        {
            int h = DateTime.Now.Hour;
            return h >= 19 || h < 6;
        }

        /// <summary>中文风向 → 世界方向向量（用于毛发摆动方向）。</summary>
        private static Vector3 WindDirection(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return Vector3.right;
            if (dir.Contains("北")) return Vector3.back;
            if (dir.Contains("南")) return Vector3.forward;
            if (dir.Contains("西")) return Vector3.left;
            if (dir.Contains("东")) return Vector3.right;
            return Vector3.right;
        }
    }
}
