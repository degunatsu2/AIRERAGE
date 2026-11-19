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
                ApplyPhotos(m, Photos.Rock, Photos.Snow, 0.06f, 0.5f, 0.5f);
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

        private const string PhotoRoot = "Assets/_Project/ThirdParty/PolyHaven";

        /// <summary>
        /// 地面の写真の絵（Poly Haven。CC0。2026-11-19 の要望）の名前と、絵の平均の明るさの逆数（線形の色の輝度の平均から。
        /// シェーダーは絵の明暗だけを掛けるので、平均が 1 になるようにする）。
        /// </summary>
        public static class Photos
        {
            public const string Rock = "rock_face_03";
            public const string Snow = "snow_02";
            public const string Grass = "leafy_grass";
            public const string Soil = "forest_ground_04";
            public const string Concrete = "concrete_floor_02";

            public static float Gain(string id)
            {
                switch (id)
                {
                    case Rock: return 6.2f;
                    case Snow: return 2.58f;
                    case Grass: return 4.19f;
                    case Soil: return 8.9f;
                    case Concrete: return 5.9f;
                    default: throw new ArgumentException($"地面の写真の絵の明るさが分からない: {id}");
                }
            }
        }

        /// <summary>
        /// 地面の材質に写真の絵を当てる（rock は岩の部分、top は上の色の部分。scale は 1m あたりのくり返し、strength は明暗の強さ、
        /// normal は凹凸の強さ）。
        /// </summary>
        public static void ApplyPhotos(Material m, string rock, string top, float scale, float strength, float normal)
        {
            m.SetTexture("_RockTex", PhotoTexture(rock, false));
            m.SetTexture("_RockNormal", PhotoTexture(rock, true));
            m.SetFloat("_RockTexGain", Photos.Gain(rock));
            m.SetTexture("_TopTex", PhotoTexture(top, false));
            m.SetTexture("_TopNormal", PhotoTexture(top, true));
            m.SetFloat("_TopTexGain", Photos.Gain(top));
            m.SetFloat("_TexScale", scale);
            m.SetFloat("_TexStrength", strength);
            m.SetFloat("_NormalStrength", normal);
        }

        // 写真の絵を読む（法線の絵は法線として読み込み直す）。なければ例外（黙って白い地面にしない）。
        private static Texture2D PhotoTexture(string id, bool normal)
        {
            string path = $"{PhotoRoot}/{id}/{id}_{(normal ? "nor_gl" : "diff")}_1k.jpg";
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                TextureImporterType type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (importer.textureType != type || importer.sRGBTexture == normal || importer.wrapMode != TextureWrapMode.Repeat || importer.anisoLevel < 8)
                {
                    importer.textureType = type;
                    importer.sRGBTexture = !normal;
                    importer.wrapMode = TextureWrapMode.Repeat;
                    importer.mipmapEnabled = true;
                    importer.anisoLevel = 8;
                    importer.SaveAndReimport();
                }
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                throw new InvalidOperationException($"地面の写真の絵がない: {path}（git lfs pull で取り込めているか確かめる）");
            }
            return texture;
        }

        /// <summary>
        /// 三角柱（底の幅 1・高さ 1・奥行き 1。底の中心が原点、頂は y 1。手前・奥の三角と 2 つの斜面と底）の網。切妻・ペディメント・
        /// ドーマーの屋根に使う（大きさは置くときの倍率で）。stage はメッシュのフォルダの名前。
        /// </summary>
        public static Mesh Prism(string stage, string name)
        {
            Mesh mesh = LoadOrCreateMesh(stage, name);
            var a = new Vector3(-0.5f, 0f, -0.5f);
            var b = new Vector3(0.5f, 0f, -0.5f);
            var c = new Vector3(0f, 1f, -0.5f);
            var d = new Vector3(-0.5f, 0f, 0.5f);
            var e = new Vector3(0.5f, 0f, 0.5f);
            var f = new Vector3(0f, 1f, 0.5f);
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            // 面を足す（Unity の表は Cross(b - a, c - a) の向き。外向きになるように並べ替える）。
            void Face(params Vector3[] corners)
            {
                Vector3 center = Vector3.zero;
                foreach (Vector3 corner in corners)
                {
                    center += corner;
                }
                center /= corners.Length;
                Vector3 normal = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
                bool outward = Vector3.Dot(normal, center - new Vector3(0f, 0.33f, 0f)) > 0f;
                int start = vertices.Count;
                vertices.AddRange(corners);
                for (int i = 1; i < corners.Length - 1; i++)
                {
                    triangles.Add(start);
                    triangles.Add(start + (outward ? i : i + 1));
                    triangles.Add(start + (outward ? i + 1 : i));
                }
            }
            Face(a, b, c);
            Face(d, e, f);
            Face(a, c, f, d);
            Face(b, e, f, c);
            Face(a, d, e, b);
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>
        /// 写真の絵を貼った地面の箱の材質（Airerage/TerrainRock を使う。世界の位置から投影するので、箱の大きさで絵が伸びない）。
        /// 上を向いた面は top の色と絵、横の面は side の色と絵。
        /// </summary>
        public static Material GroundMaterial(StageDecor decor, string name, Color side, string sidePhoto, Color top, string topPhoto, float scale)
        {
            return RockMaterial(decor, name, m =>
            {
                m.SetColor("_RockColor", side);
                m.SetColor("_RockDark", side * 0.6f);
                m.SetColor("_TopColor", top);
                m.SetFloat("_TopSlope", 0.6f);
                m.SetFloat("_TopHeight", -1000f);
                m.SetFloat("_TopAmount", 1f);
                m.SetFloat("_Strata", 0f);
                m.SetFloat("_NoiseScale", 0.2f);
                m.SetColor("_GlowColor", Color.black);
                m.SetFloat("_Cracks", 0f);
                m.SetFloat("_Ambient", 0.9f);
                ApplyPhotos(m, sidePhoto, topPhoto, scale, 0.65f, 0.6f);
            });
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
