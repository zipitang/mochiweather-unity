using UnityEngine;

namespace MochiWeather
{
    /// <summary>
    /// 情绪类型 —— 与 MochiWeather-Server 的 emotion 字段一一对应。
    /// 保持枚举名与云端字符串一致，联网时可以直接解析。
    /// </summary>
    public enum Emotion
    {
        Happy,    // 开朗
        Sleepy,   // 慵懒
        Sad,      // 忧郁
        Excited,  // 兴奋
        Scared,   // 害怕
        Cozy,     // 惬意
        Hot,      // 好热
        Cold      // 好冷
    }

    /// <summary>
    /// 动作指令 —— 与云端 action 字段对应，驱动一次性动画。
    /// </summary>
    public enum MochiAction
    {
        Bounce,    // 弹跳
        CurlUp,    // 缩团
        Sneeze,    // 喷嚏
        FurStand,  // 毛发竖起
        Sleep,     // 睡觉
        Shake,     // 发抖
        Wave       // 挥手
    }

    /// <summary>
    /// 情绪 → 视觉参数映射表。
    ///
    /// 这是整个 3D 模块的「美术决策中心」：情绪的差异不写在动画代码里，
    /// 而是收敛成一张表 —— 换色、换毛长、换呼吸频率都只改这一处。
    /// </summary>
    public static class EmotionPalette
    {
        /// <summary>身体色调（乘在基础毛色上）。</summary>
        public static Color BodyTint(Emotion e)
        {
            switch (e)
            {
                case Emotion.Happy:   return new Color(1.00f, 0.98f, 0.92f);
                case Emotion.Sleepy:  return new Color(0.94f, 0.94f, 1.00f);
                case Emotion.Sad:     return new Color(0.86f, 0.90f, 0.94f);
                case Emotion.Excited: return new Color(1.00f, 0.99f, 0.98f);
                case Emotion.Scared:  return new Color(0.97f, 0.92f, 0.96f);
                case Emotion.Cozy:    return new Color(1.00f, 0.96f, 0.92f);
                case Emotion.Hot:     return new Color(1.00f, 0.93f, 0.86f);
                case Emotion.Cold:    return new Color(0.90f, 0.95f, 1.00f);
                default:              return Color.white;
            }
        }

        /// <summary>毛尖颜色 —— 让毛在逆光时透出对应情绪的色相。</summary>
        public static Color FurTip(Emotion e)
        {
            switch (e)
            {
                case Emotion.Happy:   return new Color(1.00f, 0.97f, 0.85f);
                case Emotion.Sleepy:  return new Color(0.93f, 0.95f, 1.00f);
                case Emotion.Sad:     return new Color(0.88f, 0.93f, 0.97f);
                case Emotion.Excited: return new Color(1.00f, 0.99f, 0.94f);
                case Emotion.Scared:  return new Color(0.98f, 0.90f, 0.96f);
                case Emotion.Cozy:    return new Color(1.00f, 0.96f, 0.90f);
                case Emotion.Hot:     return new Color(1.00f, 0.90f, 0.82f);
                case Emotion.Cold:    return new Color(0.92f, 0.97f, 1.00f);
                default:              return Color.white;
            }
        }

        /// <summary>情绪强度下的毛长倍率：越激动的情绪毛越炸。</summary>
        public static float FurLengthScale(Emotion e)
        {
            switch (e)
            {
                case Emotion.Excited: return 1.25f;
                case Emotion.Scared:  return 1.35f;   // 毛竖起
                case Emotion.Cold:    return 1.20f;
                case Emotion.Hot:     return 0.85f;   // 热到趴下来，毛塌一点
                case Emotion.Sleepy:  return 0.92f;
                case Emotion.Sad:     return 0.95f;
                default:              return 1.00f;
            }
        }

        /// <summary>呼吸频率（次/秒）。越安静越慢。</summary>
        public static float BreathRate(Emotion e)
        {
            switch (e)
            {
                case Emotion.Sleepy:  return 0.28f;
                case Emotion.Cozy:    return 0.42f;
                case Emotion.Sad:     return 0.35f;
                case Emotion.Happy:   return 0.62f;
                case Emotion.Excited: return 0.85f;
                case Emotion.Scared:  return 1.10f;
                case Emotion.Hot:     return 0.55f;
                case Emotion.Cold:    return 0.70f;
                default:              return 0.45f;
            }
        }

        /// <summary>身体整体的前倾 / 收缩姿态（0 = 挺立，1 = 缩成一团）。</summary>
        public static float CurlAmount(Emotion e)
        {
            switch (e)
            {
                case Emotion.Sleepy:  return 0.55f;
                case Emotion.Sad:     return 0.65f;
                case Emotion.Scared:  return 0.60f;
                case Emotion.Cold:    return 0.45f;
                case Emotion.Hot:     return 0.35f;
                case Emotion.Cozy:    return 0.18f;
                case Emotion.Excited: return 0.0f;
                case Emotion.Happy:   return 0.0f;
                default:              return 0.15f;
            }
        }

        /// <summary>中文名（用于界面显示）。</summary>
        public static string Display(Emotion e)
        {
            switch (e)
            {
                case Emotion.Happy:   return "开朗";
                case Emotion.Sleepy:  return "慵懒";
                case Emotion.Sad:     return "忧郁";
                case Emotion.Excited: return "兴奋";
                case Emotion.Scared:  return "害怕";
                case Emotion.Cozy:    return "惬意";
                case Emotion.Hot:     return "好热";
                case Emotion.Cold:    return "好冷";
                default:              return "惬意";
            }
        }

        /// <summary>从云端字符串解析（非法值回退 Cozy）。</summary>
        public static Emotion ParseEmotion(string key)
        {
            if (string.IsNullOrEmpty(key)) return Emotion.Cozy;
            switch (key.Trim().ToLowerInvariant())
            {
                case "happy":   return Emotion.Happy;
                case "sleepy":  return Emotion.Sleepy;
                case "sad":     return Emotion.Sad;
                case "excited": return Emotion.Excited;
                case "scared":  return Emotion.Scared;
                case "cozy":    return Emotion.Cozy;
                case "hot":     return Emotion.Hot;
                case "cold":    return Emotion.Cold;
                default:        return Emotion.Cozy;
            }
        }

        /// <summary>从云端字符串解析动作（非法值回退 Wave）。</summary>
        public static MochiAction ParseAction(string key)
        {
            if (string.IsNullOrEmpty(key)) return MochiAction.Wave;
            switch (key.Trim().ToLowerInvariant())
            {
                case "bounce":    return MochiAction.Bounce;
                case "curl_up":   return MochiAction.CurlUp;
                case "sneeze":    return MochiAction.Sneeze;
                case "fur_stand": return MochiAction.FurStand;
                case "sleep":     return MochiAction.Sleep;
                case "shake":     return MochiAction.Shake;
                case "wave":      return MochiAction.Wave;
                default:          return MochiAction.Wave;
            }
        }

        /// <summary>天气状况 → 天幕配色（底 / 顶）。</summary>
        public static void SkyColors(string condition, bool night, out Color bottom, out Color top)
        {
            string c = (condition ?? "sunny").Trim().ToLowerInvariant();
            switch (c)
            {
                case "rainy":
                    bottom = new Color(0.72f, 0.78f, 0.82f);
                    top    = new Color(0.44f, 0.53f, 0.62f);
                    break;
                case "storm":
                    bottom = new Color(0.62f, 0.64f, 0.70f);
                    top    = new Color(0.28f, 0.31f, 0.40f);
                    break;
                case "snowy":
                    bottom = new Color(0.92f, 0.95f, 0.99f);
                    top    = new Color(0.70f, 0.79f, 0.90f);
                    break;
                case "cloudy":
                case "overcast":
                    bottom = new Color(0.86f, 0.88f, 0.90f);
                    top    = new Color(0.62f, 0.68f, 0.74f);
                    break;
                case "foggy":
                    bottom = new Color(0.88f, 0.89f, 0.88f);
                    top    = new Color(0.68f, 0.70f, 0.70f);
                    break;
                case "hazy":
                    bottom = new Color(0.90f, 0.86f, 0.79f);
                    top    = new Color(0.68f, 0.63f, 0.56f);
                    break;
                case "windy":
                    bottom = new Color(0.88f, 0.92f, 0.94f);
                    top    = new Color(0.58f, 0.70f, 0.78f);
                    break;
                default: // sunny
                    bottom = new Color(0.99f, 0.94f, 0.86f);
                    top    = new Color(0.60f, 0.78f, 0.94f);
                    break;
            }

            if (night)
            {
                bottom *= 0.32f;
                top *= 0.22f;
                bottom.a = top.a = 1f;
            }
        }

        /// <summary>天气 → 风力驱动的毛发摆动强度。</summary>
        public static float WindStrength(string condition)
        {
            switch ((condition ?? "").Trim().ToLowerInvariant())
            {
                case "windy":  return 0.85f;
                case "storm":  return 1.20f;
                case "snowy":  return 0.45f;
                case "rainy":  return 0.35f;
                case "cloudy": return 0.22f;
                case "overcast": return 0.25f;
                default:       return 0.12f;
            }
        }
    }
}
