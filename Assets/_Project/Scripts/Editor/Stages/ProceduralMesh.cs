using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// ステージの飾りとギミックの見た目を精巧にするための網（2026-11-20 の要望: 灯台・オーブン・蛇口・やかん・ロールケーキ・悪魔の手など）。
    /// 回転体（輪郭を Y の軸の周りに回す。灯台の塔・ドーム・やかん・爪）、渦巻きの帯（ロールケーキの断面）、管（道に沿った太さのある線。
    /// 蛇口の首・悪魔の手の指）。網は形から決まる（作り直しても同じ）ので、アセットとして Meshes/Stages/&lt;stage&gt;/ に保存して GUID を保つ。
    /// 見た目だけ（当たりはない）。
    /// </summary>
    internal static class ProceduralMesh
    {
        private const string MeshRoot = "Assets/_Project/Meshes/Stages";

        /// <summary>
        /// 回転体。profile は下から上への (半径, 高さ) の並び。segments は周りの分割の数。上下の端の半径が 0 でなければ蓋をする
        /// （caps が false なら蓋をしない。輪の金具・筒の口など、真ん中が抜けている物）。UV は u が周り、v が輪郭に沿った長さの割合。
        /// </summary>
        public static Mesh Lathe(string stage, string name, Vector2[] profile, int segments, bool caps = true)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float total = 0f;
            var lengths = new float[profile.Length];
            for (int i = 1; i < profile.Length; i++)
            {
                total += Vector2.Distance(profile[i - 1], profile[i]);
                lengths[i] = total;
            }
            // 輪郭の辺ごとに頂点を分ける（角をはっきり出す）。
            for (int i = 0; i < profile.Length - 1; i++)
            {
                Vector2 a = profile[i];
                Vector2 b = profile[i + 1];
                Vector2 edge = b - a;
                // 輪郭の外向きの法線（半径の向きと高さの向き）。
                var outward = new Vector2(edge.y, -edge.x).normalized;
                int start = vertices.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float angle = s * Mathf.PI * 2f / segments;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);
                    var n = new Vector3(outward.x * cos, outward.y, outward.x * sin);
                    vertices.Add(new Vector3(a.x * cos, a.y, a.x * sin));
                    vertices.Add(new Vector3(b.x * cos, b.y, b.x * sin));
                    normals.Add(n);
                    normals.Add(n);
                    float u = (float)s / segments;
                    uvs.Add(new Vector2(u, total > 0f ? lengths[i] / total : 0f));
                    uvs.Add(new Vector2(u, total > 0f ? lengths[i + 1] / total : 1f));
                }
                for (int s = 0; s < segments; s++)
                {
                    int p = start + s * 2;
                    triangles.Add(p);
                    triangles.Add(p + 1);
                    triangles.Add(p + 2);
                    triangles.Add(p + 2);
                    triangles.Add(p + 1);
                    triangles.Add(p + 3);
                }
            }
            if (caps)
            {
                Cap(vertices, normals, uvs, triangles, profile[0], segments, false);
                Cap(vertices, normals, uvs, triangles, profile[profile.Length - 1], segments, true);
            }
            return Save(stage, name, vertices, normals, uvs, triangles);
        }

        // 端の蓋（半径が 0 なら作らない）。
        private static void Cap(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles, Vector2 ring, int segments, bool top)
        {
            if (ring.x <= 1e-4f)
            {
                return;
            }
            var n = top ? Vector3.up : Vector3.down;
            int center = vertices.Count;
            vertices.Add(new Vector3(0f, ring.y, 0f));
            normals.Add(n);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int s = 0; s <= segments; s++)
            {
                float angle = s * Mathf.PI * 2f / segments;
                vertices.Add(new Vector3(ring.x * Mathf.Cos(angle), ring.y, ring.x * Mathf.Sin(angle)));
                normals.Add(n);
                uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(angle), 0.5f + 0.5f * Mathf.Sin(angle)));
            }
            for (int s = 0; s < segments; s++)
            {
                if (top)
                {
                    triangles.Add(center);
                    triangles.Add(center + s + 2);
                    triangles.Add(center + s + 1);
                }
                else
                {
                    triangles.Add(center);
                    triangles.Add(center + s + 1);
                    triangles.Add(center + s + 2);
                }
            }
        }

        /// <summary>
        /// 渦巻きの帯（XY の面の上。表は -Z、カメラ向き）。半径 inner から outer まで turns 回まわる、幅 width の帯。
        /// </summary>
        public static Mesh Spiral(string stage, string name, float inner, float outer, float turns, float width, int steps)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float angle = t * turns * Mathf.PI * 2f;
                float r = Mathf.Lerp(inner, outer, t);
                float half = width * 0.5f * Mathf.Lerp(0.4f, 1f, Mathf.Min(1f, t * 6f));
                var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                vertices.Add(dir * Mathf.Max(0f, r - half));
                vertices.Add(dir * (r + half));
                normals.Add(Vector3.back);
                normals.Add(Vector3.back);
                uvs.Add(new Vector2(0f, t));
                uvs.Add(new Vector2(1f, t));
                if (i > 0)
                {
                    int p = i * 2;
                    triangles.Add(p - 2);
                    triangles.Add(p);
                    triangles.Add(p - 1);
                    triangles.Add(p - 1);
                    triangles.Add(p);
                    triangles.Add(p + 1);
                }
            }
            return Save(stage, name, vertices, normals, uvs, triangles);
        }

        /// <summary>
        /// 管（path の点を通る、太さ radius〈点ごとに radii で変えられる〉の筒）。sides は周りの分割の数。両端に蓋をする。
        /// </summary>
        public static Mesh Tube(string stage, string name, Vector3[] path, float[] radii, int sides)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            Vector3 up = Vector3.up;
            for (int i = 0; i < path.Length; i++)
            {
                Vector3 forward = i < path.Length - 1 ? path[i + 1] - path[i] : path[i] - path[i - 1];
                if (i > 0 && i < path.Length - 1)
                {
                    forward = path[i + 1] - path[i - 1];
                }
                forward.Normalize();
                if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.95f)
                {
                    up = Vector3.forward;
                }
                Vector3 right = Vector3.Cross(up, forward).normalized;
                Vector3 normalUp = Vector3.Cross(forward, right).normalized;
                up = normalUp;
                float radius = radii[Mathf.Min(i, radii.Length - 1)];
                for (int s = 0; s <= sides; s++)
                {
                    float angle = s * Mathf.PI * 2f / sides;
                    Vector3 n = right * Mathf.Cos(angle) + normalUp * Mathf.Sin(angle);
                    vertices.Add(path[i] + n * radius);
                    normals.Add(n);
                    uvs.Add(new Vector2((float)s / sides, (float)i / (path.Length - 1)));
                }
                if (i > 0)
                {
                    int a = (i - 1) * (sides + 1);
                    int b = i * (sides + 1);
                    for (int s = 0; s < sides; s++)
                    {
                        triangles.Add(a + s);
                        triangles.Add(a + s + 1);
                        triangles.Add(b + s);
                        triangles.Add(a + s + 1);
                        triangles.Add(b + s + 1);
                        triangles.Add(b + s);
                    }
                }
            }
            // 両端の蓋（端の輪の真ん中）。
            for (int end = 0; end < 2; end++)
            {
                int ring = end == 0 ? 0 : (path.Length - 1) * (sides + 1);
                Vector3 axis = end == 0 ? (path[0] - path[1]).normalized : (path[path.Length - 1] - path[path.Length - 2]).normalized;
                int center = vertices.Count;
                vertices.Add(end == 0 ? path[0] : path[path.Length - 1]);
                normals.Add(axis);
                uvs.Add(new Vector2(0.5f, end));
                for (int s = 0; s < sides; s++)
                {
                    if (end == 0)
                    {
                        triangles.Add(center);
                        triangles.Add(ring + s + 1);
                        triangles.Add(ring + s);
                    }
                    else
                    {
                        triangles.Add(center);
                        triangles.Add(ring + s);
                        triangles.Add(ring + s + 1);
                    }
                }
            }
            return Save(stage, name, vertices, normals, uvs, triangles);
        }

        /// <summary>網を置く（影は落とす）。</summary>
        public static GameObject Place(string name, Transform parent, Mesh mesh, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// 光の板（XY の面の 1×1。表は -Z、カメラ向き）。頂点の色は白（頂点の色を掛けるパーティクル用のシェーダー〈Airerage/Wisp など〉を、
        /// 粒でない板に使う。Unity の Quad には頂点の色がない）。
        /// </summary>
        public static Mesh GlowCard(string stage, string name)
        {
            var vertices = new List<Vector3> { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            var normals = new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            var uvs = new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            var triangles = new List<int> { 0, 3, 2, 0, 2, 1 };
            Mesh mesh = Save(stage, name, vertices, normals, uvs, triangles);
            mesh.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>
        /// 半径 1 の円盤（XY の面。表は -Z、カメラ向き）を、中心からの輪 rings 本 × 周り segments に分けたもの。UV は u が周りの角度
        /// （0〜1）、v が中心からの距離（0〜1）。ワープゲートの渦（WormholeView が頂点を撓ませる）に使う。
        /// </summary>
        public static Mesh Disc(string stage, string name, int rings, int segments)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float radius = r / (float)rings;
                for (int s = 0; s <= segments; s++)
                {
                    float angle = s * Mathf.PI * 2f / segments;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
                    normals.Add(Vector3.back);
                    uvs.Add(new Vector2(s / (float)segments, radius));
                }
            }
            int row = segments + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * row + s;
                    int b = a + row;
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(b + 1);
                    triangles.Add(a);
                    triangles.Add(b + 1);
                    triangles.Add(a + 1);
                }
            }
            return Save(stage, name, vertices, normals, uvs, triangles);
        }

        private static Mesh Save(string stage, string name, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            string folder = $"{MeshRoot}/{stage}";
            string path = $"{folder}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                SandboxWireSceneBuilder.EnsureFolder(folder);
                mesh = new Mesh { name = name };
                AssetDatabase.CreateAsset(mesh, path);
            }
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
