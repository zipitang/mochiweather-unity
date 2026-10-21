using System.Collections.Generic;
using UnityEngine;

namespace MochiWeather
{
    /// <summary>
    /// 程序化网格工厂。
    ///
    /// 整个 3D 模块不依赖任何美术资源文件（.fbx / .obj / 贴图），
    /// 所有几何体都在运行时用代码生成 —— 这样工程导入到任何 Unity 版本
    /// 都能直接跑，不会出现「模型丢失」这类资源引用问题。
    /// </summary>
    public static class MeshFactory
    {
        // ========================================================
        // 正二十面体细分球（Icosphere）
        // ========================================================
        // 相比 UV 球，Icosphere 的三角形分布均匀，不会在两极堆积，
        // 因此做壳层毛发时不会出现「极点毛扎成一撮」的丑现象。

        private static readonly float GoldRatio = (1f + Mathf.Sqrt(5f)) * 0.5f;

        /// <summary>
        /// 生成细分球。subdivisions 建议 2~4：
        /// 3 → 约 1280 面；4 → 约 5120 面。壳层毛发用 3 即可。
        /// </summary>
        public static Mesh CreateIcosphere(int subdivisions, float radius)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var cache = new Dictionary<long, int>();

            // 十二个初始顶点
            float t = GoldRatio;
            AddVertex(verts, cache, new Vector3(-1, t, 0) * radius);
            AddVertex(verts, cache, new Vector3(1, t, 0) * radius);
            AddVertex(verts, cache, new Vector3(-1, -t, 0) * radius);
            AddVertex(verts, cache, new Vector3(1, -t, 0) * radius);
            AddVertex(verts, cache, new Vector3(0, -1, t) * radius);
            AddVertex(verts, cache, new Vector3(0, 1, t) * radius);
            AddVertex(verts, cache, new Vector3(0, -1, -t) * radius);
            AddVertex(verts, cache, new Vector3(0, 1, -t) * radius);
            AddVertex(verts, cache, new Vector3(t, 0, -1) * radius);
            AddVertex(verts, cache, new Vector3(t, 0, 1) * radius);
            AddVertex(verts, cache, new Vector3(-t, 0, -1) * radius);
            AddVertex(verts, cache, new Vector3(-t, 0, 1) * radius);

            // 二十个初始面
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            for (int i = 0; i < faces.Length; i += 3)
                tris.Add(faces[i]); // 索引已是按 AddVertex 的顺序 0..11

            // 细分：每个三角形一分为四，中点投影回球面
            for (int s = 0; s < subdivisions; s++)
            {
                var newTris = new List<int>(tris.Count * 4);
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Midpoint(verts, cache, a, b, radius);
                    int bc = Midpoint(verts, cache, b, c, radius);
                    int ca = Midpoint(verts, cache, c, a, radius);

                    newTris.Add(a); newTris.Add(ab); newTris.Add(ca);
                    newTris.Add(b); newTris.Add(bc); newTris.Add(ab);
                    newTris.Add(c); newTris.Add(ca); newTris.Add(bc);
                    newTris.Add(ab); newTris.Add(bc); newTris.Add(ca);
                }
                tris = newTris;
            }

            // 归一化到半径
            var positions = new Vector3[verts.Count];
            var normals = new Vector3[verts.Count];
            var uvs = new Vector2[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                var dir = verts[i].normalized;
                positions[i] = dir * radius;
                normals[i] = dir;
                uvs[i] = new Vector2(
                    0.5f + Mathf.Atan2(dir.z, dir.x) / (2f * Mathf.PI),
                    0.5f - Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) / Mathf.PI);
            }

            var mesh = new Mesh { name = "MochiIcosphere" };
            if (positions.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddVertex(List<Vector3> verts, Dictionary<long, int> cache, Vector3 v)
        {
            verts.Add(v);
        }

        private static int Midpoint(
            List<Vector3> verts, Dictionary<long, int> cache, int a, int b, float radius)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int idx)) return idx;

            var mid = ((verts[a] + verts[b]) * 0.5f).normalized * radius;
            verts.Add(mid);
            idx = verts.Count - 1;
            cache[key] = idx;
            return idx;
        }

        // ========================================================
        // 圆弧条带（用来做嘴巴 / 眉毛）
        // ========================================================

        /// <summary>
        /// 生成一段圆弧条带（沿 XZ 平面），用于嘴部的微笑弧线。
        /// </summary>
        /// <param name="radius">弧线半径</param>
        /// <param name="thickness">线宽</param>
        /// <param name="startDeg">起始角（0° = 正右，逆时针）</param>
        /// <param name="endDeg">结束角</param>
        /// <param name="zOffset">沿 Z 的偏移（贴合球面用）</param>
        public static Mesh CreateArcStrip(
            float radius, float thickness, float startDeg, float endDeg, float zOffset, int segments = 24)
        {
            segments = Mathf.Max(2, segments);
            var verts = new Vector3[(segments + 1) * 2];
            var normals = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[(segments + 1) * 2];
            var tris = new List<int>(segments * 6);

            float half = thickness * 0.5f;
            for (int i = 0; i <= segments; i++)
            {
                float tt = i / (float)segments;
                float ang = Mathf.Lerp(startDeg, endDeg, tt) * Mathf.Deg2Rad;
                float x = Mathf.Cos(ang) * radius;
                float y = Mathf.Sin(ang) * radius;

                // 弧线两端收细，看起来像画上去的笔触而不是硬边色块
                float taper = Mathf.Sin(tt * Mathf.PI) * 0.5f + 0.5f;
                float h = half * taper;

                verts[i * 2] = new Vector3(x, y + h, zOffset);
                verts[i * 2 + 1] = new Vector3(x, y - h, zOffset);
                normals[i * 2] = Vector3.back;
                normals[i * 2 + 1] = Vector3.back;
                uvs[i * 2] = new Vector2(tt, 1f);
                uvs[i * 2 + 1] = new Vector2(tt, 0f);

                if (i < segments)
                {
                    int b = i * 2;
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                }
            }

            var mesh = new Mesh { name = "MochiArc" };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>生成一块居中的单位方片（1x1，朝向 +Z）。</summary>
        public static Mesh CreateQuad(float size = 1f)
        {
            float h = size * 0.5f;
            var mesh = new Mesh { name = "MochiQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-h, -h, 0), new Vector3(h, -h, 0),
                new Vector3(-h, h, 0), new Vector3(h, h, 0)
            };
            mesh.uv = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1)
            };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
