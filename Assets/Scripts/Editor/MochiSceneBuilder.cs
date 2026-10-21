#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MochiWeather.EditorTools
{
    /// <summary>
    /// 一键把演示场景搭出来。
    ///
    /// 为什么用脚本而不是提交一个 .unity 场景文件：
    ///   场景文件里存的是「引用 + 序列化后的组件参数」，换个 Unity 版本或
    ///   少一个脚本 GUID 就会变成一堆 Missing。用代码搭，任何时候都是好的。
    ///
    /// 用法：菜单栏 → MochiWeather → 生成演示场景
    /// </summary>
    public static class MochiSceneBuilder
    {
        private const string SceneDir = "Assets/Scenes";
        private const string ScenePath = "Assets/Scenes/MochiDemo.unity";

        [MenuItem("MochiWeather/生成演示场景", priority = 0)]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------- 相机 ----------
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.10f);
            cam.fieldOfView = 34f;
            // 略微俯视，眼睛正好落在画面上三分之一处
            camGo.transform.position = new Vector3(0f, 0.72f, 2.35f);
            camGo.transform.rotation = Quaternion.Euler(6f, 0f, 0f);
            camGo.AddComponent<AudioListener>();

            // ---------- 灯光 ----------
            var keyGo = new GameObject("Key Light");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.96f, 0.90f);
            key.intensity = 1.15f;
            key.shadows = LightShadows.None;   // 毛球用接触阴影，不需要实时阴影贴图
            keyGo.transform.rotation = Quaternion.Euler(38f, -28f, 0f);

            var fillGo = new GameObject("Fill Light");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.62f, 0.74f, 0.95f);
            fill.intensity = 0.42f;
            fill.shadows = LightShadows.None;
            fillGo.transform.rotation = Quaternion.Euler(-24f, 148f, 0f);

            // ---------- 天幕 ----------
            GameObject backdrop = BuildBackdrop();

            // ---------- 毛绒玩偶 ----------
            var root = new GameObject("Mochi");

            var furBall = root.AddComponent<MochiFurBall>();
            furBall.bodyRadius = 0.5f;
            // 40 层：20 层时轮廓上还能看出「碎屑感」，40 层才真正叠出体积
            furBall.shellCount = 40;
            furBall.furLength = 0.085f;
            furBall.furDensity = 26f;
            furBall.subdivisions = 3;

            var animator = root.AddComponent<MochiWeatherAnimator>();
            animator.ball = furBall;

            var controller = root.AddComponent<MochiWeatherController>();
            controller.ball = furBall;
            controller.animator = animator;
            controller.backdrop = backdrop != null ? backdrop.GetComponent<Renderer>() : null;

            // 生成时先离线跑起来 —— 不问服务端也能立刻看到玩偶在呼吸
            controller.offlineMock = false;
            controller.refreshOnStart = true;

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[MochiWeather] 演示场景已生成（未保存）。运行即可看到毛绒玩偶。");
        }

        [MenuItem("MochiWeather/生成并保存场景", priority = 1)]
        public static void BuildAndSave()
        {
            BuildScene();
            Directory.CreateDirectory(SceneDir);
            EditorSceneManager.SaveScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.Refresh();
            Debug.Log($"[MochiWeather] 场景已保存到 {ScenePath}");
        }

        [MenuItem("MochiWeather/生成玩偶到当前场景", priority = 20)]
        public static void AddMochiToCurrentScene()
        {
            var root = new GameObject("Mochi");
            var furBall = root.AddComponent<MochiFurBall>();
            var animator = root.AddComponent<MochiWeatherAnimator>();
            animator.ball = furBall;
            var controller = root.AddComponent<MochiWeatherController>();
            controller.ball = furBall;
            controller.animator = animator;

            Undo.RegisterCreatedObjectUndo(root, "Add Mochi");
            Selection.activeGameObject = root;
            Debug.Log("[MochiWeather] 玩偶已加入当前场景");
        }

        private static GameObject BuildBackdrop()
        {
            Shader shader = Shader.Find("MochiWeather/Backdrop");
            if (shader == null)
            {
                Debug.LogWarning("[MochiWeather] 找不到 MochiWeather/Backdrop 着色器，跳过天幕");
                return null;
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Backdrop";
            Object.DestroyImmediate(quad.GetComponent<Collider>());

            var mat = new Material(shader) { name = "MochiBackdropMat" };
            EmotionPalette.SkyColors("sunny", false, out Color bottom, out Color top);
            mat.SetColor("_BottomColor", bottom);
            mat.SetColor("_TopColor", top);
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // 放在玩偶背后、朝向相机
            quad.transform.position = new Vector3(0f, 0.9f, 3.2f);
            quad.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            quad.transform.localScale = new Vector3(9f, 5.2f, 1f);
            return quad;
        }
    }
}
#endif
