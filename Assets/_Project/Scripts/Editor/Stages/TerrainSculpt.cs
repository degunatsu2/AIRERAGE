using System;
using UnityEditor;
using UnityEngine;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// 背景の地面の形（山・尾根・火口の壁・火山・氷山）を、雑音で起伏をつけた高さの網で作る（2026-11-18 の要望: 背景の小物と空だけでなく、
    /// 地面のモデリングもしっかり。前は箱や 45° 回した箱だった）。網は種から決まる（作り直しても同じ形）ので、メッシュのアセットとして
    /// ステージの材質のフォルダの隣（Meshes）に保存して GUID を保つ。描くのは Airerage/TerrainRock（斜面と高さで岩・雪・灰・溶岩の光）。
    /// 見た目だけ（当たりはない）。
    /// </summary>
    internal static class TerrainSculpt
    {
        private const string MeshRoot = "Assets/_Project/Meshes/Stages";

        /// <summary>高さの網の形。範囲（x・z）と格子の細かさ、高さを返す関数（x, z → y）。</summary>
        public readonly struct Field
        {
            public readonly float MinX;
            public readonly float MaxX;
            public readonly float MinZ;
            public readonly float MaxZ;
            public readonly float Cell;
            public readonly Func<float, float, float> Height;

            public Field(float minX, float maxX, float minZ, float maxZ, float cell, Func<float, float, float> height)
            {
                MinX = minX;
                MaxX = maxX;
                MinZ = minZ;
                MaxZ = maxZ;
                Cell = cell;
                Height = height;
            }
        }

        /// <summary>高さの網を作って置く（stage はメッシュのフォルダの名前）。</summary>
        public static GameObject Place(StageDecor decor, string stage, string name, Field field, Material material)
        {
            Mesh mesh = LoadOrCreateMesh(stage, name);
            Fill(mesh, field);
            var go = new GameObject(name);
            go.transform.SetParent(decor.Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            EditorUtility.SetDirty(mesh);
            return go;
        }

        /// <summary>
        /// 円錐（底の半径 0.5・高さ 1・底は y 0。底の面つき）の網。木の葉の段などに使う。segments は周りの分け方。
        /// </summary>
        public static Mesh Cone(string stage, string name, int segments)
        {
            Mesh mesh = LoadOrCreateMesh(stage, name);
            var vertices = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            float slope = 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f);
                float mid = (a0 + a1) * 0.5f;
                Vector3 n = new Vector3(Mathf.Cos(mid), slope, Mathf.Sin(mid)).normalized;
                int b = vertices.Count;
                vertices.Add(p0);
                vertices.Add(Vector3.up);
                vertices.Add(p1);
                normals.Add(new Vector3(Mathf.Cos(a0), slope, Mathf.Sin(a0)).normalized);
                normals.Add(n);
                normals.Add(new Vector3(Mathf.Cos(a1), slope, Mathf.Sin(a1)).normalized);
                triangles.Add(b);
                triangles.Add(b + 1);
                triangles.Add(b + 2);
                // 底の面。
                int c = vertices.Count;
                vertices.Add(Vector3.zero);
                vertices.Add(p0);
                vertices.Add(p1);
                normals.Add(Vector3.down);
                normals.Add(Vector3.down);
                normals.Add(Vector3.down);
                triangles.Add(c);
                triangles.Add(c + 1);
                triangles.Add(c + 2);
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>網を置く（見た目だけ）。</summary>
        public static GameObject PlaceMesh(StageDecor decor, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(decor.Root, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// 遠くの雪山の連なり（尖った尾根の雑音。海から立ち上がり、高い所と平らな所に雪。真ん中は低め。霧で空の地平線の色に溶ける）。
        /// FROST COAST と埠頭の奥。front〜back は奥行き、baseY は根元の高さ（海より下）、seed で形を変える。
        /// </summary>
        public static GameObject PlaceSnowRange(StageDecor decor, string stage, float front, float back, float baseY, int seed)
        {
            Material material = RockMaterial(decor, "SnowMountain", m =>
            {
                m.SetColor("_RockColor", new Color(0.36f, 0.4f, 0.47f));
                m.SetColor("_RockDark", new Color(0.16f, 0.19f, 0.24f));
                m.SetColor("_TopColor", new Color(0.95f, 0.97f, 1f));
                m.SetFloat("_TopSlope", 0.5f);
                m.SetFloat("_TopHeight", baseY + 18f);
                m.SetFloat("_TopAmount", 1f);
                m.SetFloat("_Strata", 0.15f);
                m.SetFloat("_NoiseScale", 0.06f);
                m.SetColor("_GlowColor", Color.black);
                m.SetFloat("_Cracks", 0f);
                m.SetFloat("_Ambient", 1f);
            });
            float rampEnd = front + (back - front) * 0.42f;
            return Place(decor, stage, "FarMountains", new Field(-360f, 360f, front, back, 4f, (x, z) =>
            {
                float range = Mathf.Pow(Ridged(x * 0.011f, z * 0.011f, seed, 5), 1.25f) * 95f;
                float rise = Smooth(front, rampEnd, z);
                float valley = 1f - 0.55f * Mathf.Exp(-(x * x) / (2f * 70f * 70f));
                return baseY + (range * valley + Fbm(x * 0.03f, z * 0.03f, seed + 6, 3) * 8f) * rise;
            }), material);
        }

        /// <summary>
        /// たくさんの小さな箱（観客・座席など）を 1 つの網にまとめて置くための入れ物。材質ごとに網の部分を分ける（GameObject を
        /// 何千も作らないため）。下の面と奥の面は作らない（カメラは手前から見るので見えない。保存する網を小さくする）。
        /// </summary>
        public sealed class BoxBatch
        {
            private readonly System.Collections.Generic.List<Vector3> _vertices = new System.Collections.Generic.List<Vector3>();
            private readonly System.Collections.Generic.List<Vector3> _normals = new System.Collections.Generic.List<Vector3>();
            private readonly System.Collections.Generic.List<int>[] _triangles;

            public BoxBatch(int materials)
            {
                _triangles = new System.Collections.Generic.List<int>[materials];
                for (int i = 0; i < materials; i++)
                {
                    _triangles[i] = new System.Collections.Generic.List<int>();
                }
            }

            /// <summary>箱の数。</summary>
            public int Count { get; private set; }

            /// <summary>
            /// 中心 center・大きさ size の箱を、material 番目の材質で足す（yaw は縦の軸の回転、度）。underside なら下の面も作る
            /// （屋根など、下から見える物）。
            /// </summary>
            public void Add(Vector3 center, Vector3 size, int material, float yaw = 0f, bool underside = false)
            {
                Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
                Vector3 h = size * 0.5f;
                // 上・前・左・右の 4 面。
                Face(center, rotation, new Vector3(0f, h.y, 0f), new Vector3(h.x, 0f, 0f), new Vector3(0f, 0f, h.z), Vector3.up, material);
                Face(center, rotation, new Vector3(0f, 0f, -h.z), new Vector3(h.x, 0f, 0f), new Vector3(0f, h.y, 0f), Vector3.back, material);
                Face(center, rotation, new Vector3(-h.x, 0f, 0f), new Vector3(0f, 0f, -h.z), new Vector3(0f, h.y, 0f), Vector3.left, material);
                Face(center, rotation, new Vector3(h.x, 0f, 0f), new Vector3(0f, 0f, h.z), new Vector3(0f, h.y, 0f), Vector3.right, material);
                if (underside)
                {
                    Face(center, rotation, new Vector3(0f, -h.y, 0f), new Vector3(h.x, 0f, 0f), new Vector3(0f, 0f, h.z), Vector3.down, material);
                }
                Count++;
            }

            // 面（中心 + offset、横 u・縦 v の半分の長さ、外向き normal）。表が外を向くように三角形を並べる。
            private void Face(Vector3 center, Quaternion rotation, Vector3 offset, Vector3 u, Vector3 v, Vector3 normal, int material)
            {
                int b = _vertices.Count;
                Vector3 c = center + rotation * offset;
                Vector3 ru = rotation * u;
                Vector3 rv = rotation * v;
                Vector3 n = rotation * normal;
                _vertices.Add(c - ru - rv);
                _vertices.Add(c + ru - rv);
                _vertices.Add(c + ru + rv);
                _vertices.Add(c - ru + rv);
                for (int i = 0; i < 4; i++)
                {
                    _normals.Add(n);
                }
                // Unity では Cross(b - a, c - a) の向きが表。(0, 1, 2) の表は Cross(u, v) の向き。
                bool flip = Vector3.Dot(Vector3.Cross(ru, rv), n) < 0f;
                System.Collections.Generic.List<int> t = _triangles[material];
                if (flip)
                {
                    t.Add(b);
                    t.Add(b + 2);
                    t.Add(b + 1);
                    t.Add(b);
                    t.Add(b + 3);
                    t.Add(b + 2);
                }
                else
                {
                    t.Add(b);
                    t.Add(b + 1);
                    t.Add(b + 2);
                    t.Add(b);
                    t.Add(b + 2);
                    t.Add(b + 3);
                }
            }

            /// <summary>網にまとめて置く（stage はメッシュのフォルダの名前。materials は Add の番号の順）。</summary>
            public GameObject Place(StageDecor decor, string stage, string name, Material[] materials)
            {
                Mesh mesh = LoadOrCreateMesh(stage, name);
                mesh.Clear();
                mesh.indexFormat = _vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.subMeshCount = _triangles.Length;
                for (int i = 0; i < _triangles.Length; i++)
                {
                    mesh.SetTriangles(_triangles[i], i);
                }
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
                var go = new GameObject(name);
                go.transform.SetParent(decor.Root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = materials;
                return go;
            }
        }

        /// <summary>地面の材質（Airerage/TerrainRock。ステージの材質のフォルダに保存）。</summary>
        public static Material RockMaterial(StageDecor decor, string name, Action<Material> setup)
        {
            return SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(name, "Airerage/TerrainRock", setup, decor.Folder);
        }

        // 格子の頂点を高さで持ち上げ、法線は隣の高さから求める。
        private static void Fill(Mesh mesh, Field field)
        {
            int nx = Mathf.Max(2, Mathf.CeilToInt((field.MaxX - field.MinX) / field.Cell) + 1);
            int nz = Mathf.Max(2, Mathf.CeilToInt((field.MaxZ - field.MinZ) / field.Cell) + 1);
            if (nx * nz > 65000)
            {
                throw new InvalidOperationException($"背景の地面の網が細かすぎる（{nx} × {nz}）");
            }
            var vertices = new Vector3[nx * nz];
            var normals = new Vector3[nx * nz];
            float dx = (field.MaxX - field.MinX) / (nx - 1);
            float dz = (field.MaxZ - field.MinZ) / (nz - 1);
            for (int j = 0; j < nz; j++)
            {
                float z = field.MinZ + j * dz;
                for (int i = 0; i < nx; i++)
                {
                    float x = field.MinX + i * dx;
                    float y = field.Height(x, z);
                    vertices[j * nx + i] = new Vector3(x, y, z);
                    float hx = field.Height(x + dx * 0.5f, z) - field.Height(x - dx * 0.5f, z);
                    float hz = field.Height(x, z + dz * 0.5f) - field.Height(x, z - dz * 0.5f);
                    normals[j * nx + i] = new Vector3(-hx / dx, 1f, -hz / dz).normalized;
                }
            }
            var triangles = new int[(nx - 1) * (nz - 1) * 6];
            int t = 0;
            for (int j = 0; j < nz - 1; j++)
            {
                for (int i = 0; i < nx - 1; i++)
                {
                    int a = j * nx + i;
                    int b = a + 1;
                    int c = a + nx;
                    int d = c + 1;
                    triangles[t++] = a;
                    triangles[t++] = c;
                    triangles[t++] = b;
                    triangles[t++] = b;
                    triangles[t++] = c;
                    triangles[t++] = d;
                }
            }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        private static Mesh LoadOrCreateMesh(string stage, string name)
        {
            string folder = $"{MeshRoot}/{stage}";
            string path = $"{folder}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null)
            {
                return mesh;
            }
            SandboxWireSceneBuilder.EnsureFolder(folder);
            mesh = new Mesh { name = name };
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ---- 雑音（種から決まる。Unity の乱数は使わない） ----

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        /// <summary>なめらかな値の雑音（0〜1）。</summary>
        public static float Noise(float x, float y, int seed)
        {
            int ix = Mathf.FloorToInt(x);
            int iy = Mathf.FloorToInt(y);
            float fx = x - ix;
            float fy = y - iy;
            float ux = fx * fx * (3f - 2f * fx);
            float uy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy, seed);
            float b = Hash(ix + 1, iy, seed);
            float c = Hash(ix, iy + 1, seed);
            float d = Hash(ix + 1, iy + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
        }

        /// <summary>重ねた雑音（0〜1 のあたり）。</summary>
        public static float Fbm(float x, float y, int seed, int octaves)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float total = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Noise(x, y, seed + i * 31) * amplitude;
                total += amplitude;
                x = x * 2.03f + 17.1f;
                y = y * 2.03f - 9.7f;
                amplitude *= 0.5f;
            }
            return sum / total;
        }

        /// <summary>尖った尾根の雑音（0〜1。峰が鋭い）。</summary>
        public static float Ridged(float x, float y, int seed, int octaves)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float total = 0f;
            float weight = 1f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Noise(x, y, seed + i * 31) * 2f - 1f);
                n *= n * weight;
                weight = Mathf.Clamp01(n * 1.6f);
                sum += n * amplitude;
                total += amplitude;
                x = x * 2.03f + 17.1f;
                y = y * 2.03f - 9.7f;
                amplitude *= 0.5f;
            }
            return sum / total;
        }

        /// <summary>なめらかに 0 → 1（edge0〜edge1）。</summary>
        public static float Smooth(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// 火山の円錐（中心 cx, cz、高さ height、裾の半径 radius、頂上の火口の半径 crater）。裾ほどゆるく、頂上は火口でくぼむ。
        /// </summary>
        public static float Volcano(float x, float z, float cx, float cz, float height, float radius, float crater)
        {
            float r = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
            if (r >= radius)
            {
                return 0f;
            }
            float t = 1f - r / radius;
            float cone = height * Mathf.Pow(t, 1.6f);
            if (r < crater)
            {
                float inside = 1f - r / crater;
                cone -= height * 0.12f * inside * inside;
            }
            return cone;
        }
    }
}
