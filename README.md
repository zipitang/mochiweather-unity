# MochiWeather · Unity 3D 毛绒模块

《毛绒天气精灵》的 **真 3D 毛绒** 版本 —— 把 Android 版那个 2D Canvas 画的毛球，
换成真正有体积、有毛发、会随光和风变化的 3D 玩偶。

这个工程是**纯代码搭建**的：没有任何 `.fbx` / `.obj` / 贴图 / 预制体资源。
把所有几何体、材质、场景都在运行时用 C# 生成，好处是——

- 拖到任何 Unity 版本都能直接跑，不会出现「资源丢失」「材质变粉」；
- 毛发的每一根、每一层都能用参数调，不需要回到建模软件重做。

---

## 一、快速开始

1. 用 **Unity 2022.3 LTS（或更新）** 打开本目录。
   > ⚠️ 渲染管线必须选 **Built-in Render Pipeline**。
   > 毛发作弊用的是 `ShadeSH9` 球谐环境光，在 URP / HDRP 下会渲染成粉色。
   > （新建工程默认就是 Built-in，不用改。）
2. 菜单栏 → **MochiWeather → 生成演示场景**
   （想要保存成 `.unity` 文件就用「生成并保存场景」）
3. 按 **Play**。

**不启动服务端也能看到效果** —— 控制器在请求失败时会退化成一份内置的晴天场景，
玩偶照样呼吸、眨眼、摇尾巴。

---

## 二、目录结构

```
Assets/
├── Shaders/
│   ├── FurShell.shader        毛发壳层（Shell Texturing，本模块的核心）
│   ├── MochiBody.shader       身体球：半兰伯特 + 球谐 + 边缘光 + 内部透光
│   ├── MochiBackdrop.shader   背景天幕（随天气换色的竖直渐变）
│   └── MochiShadowBlob.shader 地面接触阴影（径向渐变）
├── Resources/Fur/
│   ├── tex_fur.png            毛长图（R=毛长 G=细丝 B=遮罩露本体）
│   └── tex_wind.png           风场图（RG=流向）
└── Scripts/
    ├── Runtime/
    │   ├── MeshFactory.cs          程序化网格（细分球 / 嘴部弧线 / 方片）
    │   ├── MochiEmotion.cs         情绪枚举 + 「情绪→外观」映射表
    │   ├── MochiFurBall.cs         毛球本体（身体 + N 层毛 + 五官）
    │   ├── MochiWeatherAnimator.cs 呼吸 / 眨眼 / 摇摆 / 动作 / 说话
    │   └── MochiWeatherController.cs 联网取「天气+情绪」
    └── Editor/
        └── MochiSceneBuilder.cs    一键生成场景
```

> 两张贴图是**数据图不是颜色图**：`.meta` 里已经写死 `sRGB 关闭 + Repeat + Mipmap`，
> 不要改成 Sprite，也不要把 sRGB 打开 —— 打开了毛长会整体偏移一档，质感会走样。
> 想换一套毛，用 `skills/fur-shell-rendering/scripts/gen_fur_textures.py` 重新生成即可。

---

## 三、毛发是怎么做的（Shell Texturing）

同一个球网格复制 N 份，每层沿法线往外推 `毛长 × (k+1)/N`，
片元里按「这个位置的毛有多长」决定要不要保留 —— 长毛活到毛尖，短毛早被裁掉，
于是自然形成根部密、尖端疏的外扩轮廓。

| 环节 | 做法 | 为什么 |
|------|------|--------|
| 分层外推 | `pos += normal * _FurLength * t`，`t = (k+1)/N` | `t` **取不到 0**：最内层必须离开表面，否则和本体球共面会出现 z-fighting 麻点 |
| 毛长 | `tex_fur.png` 的 R 通道 | 程序化噪声是各向同性的，只会得到圆颗粒（云）；一张毛长图里同时有长毛短毛 |
| 裁切 | `cut = 毛长^对比度 × _FurCut − B通道遮罩`，只留 `cut > t` | 对比度那一项把灰度压成离散斑块 —— 边界才会碎成「一撮一撮」，而不是地形等高线 |
| 层间错开 | 采样坐标加 `_ShellIndex × (1.37, 0.71)` | 层间距 ≪ 格子尺寸时，40 层会严丝合缝叠成同一张点阵图，完全没有层次 |
| 风 | **UV 流场**：把风场图滚动采样的 RG 当偏移量加到毛长图采样坐标上 | 一个顶点都不动 —— 所以永远不可能撕开层与层之间的缝 |
| 倾倒 | `_GroomDir` 统一让毛朝一侧倒 | 全笔直的话，正面只能看到毛的横截面，看起来像一块织物 |
| 毛根压暗 | `× lerp(1−_RootOcclusion, 1, t^0.75)` | 这个明暗层次是「毛绒感」的主要来源 |

关于风，这里踩过一个很深的坑，值得单独说一句：

| 做法 | 效果 | 问题 |
|---|---|---|
| 顶点位移 | 毛整体摆动 | 壳层被推开，**层间断面露出来**，斜视角下一层碎屑 |
| **UV 流场（现用）** | 毛成片倒伏、流动 | 无 —— 几何一动不动 |

材质用 **共享 material + `MaterialPropertyBlock`** 传 `_ShellIndex`，
40 层只占一份材质实例，按 `0 → N-1`（内 → 外）的顺序绘制。

**没有贴图也能跑**：材质上勾 `_StrandProc` 走程序化细丝分支。
但那一支的裁切必须用**切向距离** `loc − N·dot(loc, N)`；
用 3D 距离 `length(loc)` 的话，球面会把每个格子斜着切开、毛被剁成随机碎点，
看起来像「球上贴了一堆圆点」。

---

## 四、接服务端

场景里 `Mochi` 物体上的 **MochiWeatherController** 面板：

| 字段 | 说明 |
|------|------|
| `Api Base Url` | 你的 MochiWeather-Server 地址 |
| `Api Token` | 与服务端 `.env` 里的 `MOCHI_API_TOKEN` 一致 |
| `City` | 关注的城市，如 `无锡` |
| `User Id` | 用户标识，用于情绪日记与对话记忆 |
| `Refresh Seconds` | 自动刷新间隔，默认 600s |
| `Offline Mock` | 勾上就不联网，用内置假数据 |

它调的是这两个接口：

- `POST /api/scene` → 天气 + 情绪（一步到位，驱动外观）
- `POST /api/chat`  → 对话回复 + 情绪 + 动作（驱动说话与动作）

服务端把 `emotion` 和 `action` 一起返回，Unity 侧**不做任何业务判断**，
只负责把这两个字段演出来 —— 保持「一个大脑在云端，多个端只是显示器」。

想接语音的话，用 `MochiWeatherController.SendMessage(文字)`：
ASR 转出来的文字喂进去，回复通过 `OnReply` 事件抛出来交给 TTS。

---

## 五、调参速查

**顺序很重要**：先定毛的粗细，再定疏密，最后才动壳层数和毛长。
反过来的话，你会一直在「毛太粗」和「毛太少」之间来回打转。

| 先调 | 参数 | 位置 | 建议 |
|------|------|------|------|
| ① 粗细 | `texScale` | MochiFurBall | 9 左右。**给 1~3 会让正面全是大色块，怎么调都像云** |
| ② 疏密 | `furCut` | MochiFurBall | 1.15。调大 → 毛更稀更短 |
| ③ 层次 | `shellCount` | MochiFurBall | 40 起。20 层时轮廓上还能看出碎屑感 |
| ③ 长度 | `furLength` | MochiFurBall | 0.085（球半径 0.5，即 17%） |

想让玩偶更「毛」一点：`texScale` 11~13（毛更细）、`shellCount` 48。
**不要**靠加大 `furLength` 来增加毛量 —— 超过球半径 20% 轮廓会拖出一圈烟，像蒲公英。

其他常用项：

| 参数 | 位置 | 说明 |
|------|------|------|
| `lenContrast` | MochiFurBall | 灰度压成斑块的力度。觉得像云就加大 |
| `tipFadeStart` | MochiFurBall | 0.82。**低于 0.72 最外几层几乎全透明，看起来像雾而不是毛** |
| `groomAmount` | MochiFurBall | 倾倒强度。觉得像织物就加大 |
| `rootOcclusion` | MochiFurBall | 毛根压暗。毛绒感的来源，觉得像塑料球就加大 |
| `windIntens` / `windInfluence` | MochiFurBall | 飘动幅度 / 「只有毛尖在动」的程度 |
| `breathAmplitude` | Animator | 0.035 → 0.05（呼吸更明显）|
| `blinkIntervalMin/Max` | Animator | 调小 → 眨眼更活泼 |

情绪到外观的对应关系全部集中在 `MochiEmotion.cs` 的 `EmotionPalette` 里，
换色、改毛长、改呼吸频率都只改那一张表。

**想在不重编译的情况下试参数**：用
`skills/fur-shell-rendering/assets/fur-lab.html` —— 单文件零依赖，
浏览器打开就能拖滑杆、还能两套方案 A/B 对比。

---

## 六、已知限制

- 只支持 **Built-in RP**；要用 URP 需把 4 个着色器的光照部分改写（`ShadeSH9` → URP 的 SH 接口）。
- 毛发层数 × 三角形数 ≈ 40 × 1280 ≈ 5 万面/玩偶，桌面端无压力；
  手机上若掉帧，先把 `shellCount` 降到 28，再考虑 `subdivisions` 调到 2。
- 壳层渲染做不到单根发丝的穿插：**短发绒毛没问题，长发要用几何发丝**。
- 场景生成器不写 `.unity` 文件，是为了避免脚本 GUID 变化导致 Missing 引用。
- `Resources/Fur/` 下的两张图没开压缩（高频细节会被块压缩抹掉），
  512² 各约 1MB。要压体积请改用 BC7 / ASTC，别用 BC1。
