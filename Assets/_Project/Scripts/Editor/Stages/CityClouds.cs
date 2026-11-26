using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// ビル街（SKYLINE の昼夜）の雲海（2026-11-25 の要望: ステージは非常に高い高層ビルの上。足場のビルの根元を雲で隠し、遥か下に大通りが
    /// 見えるように。docs/15 6.71）。
    /// 2026-11-26 の要望（雲の厚い・薄いは密度ではなく横幅と縦幅のこと。手前の雲も奥の雲も Z・Y の平面になっていて不自然。もっと立体的に）で、
    /// 平らな板（水平の層と縦の土手）をやめ、横幅・奥行き・高さのある立体の雲のかたまり（積雲）を並べて雲海にした:
    /// - かたまりの形: 球をなめらかにつないだ積雲（綿のかたまりが平らな底の上に載った形。上は丸いこぶ、底は平ら）。距離の場（SDF）から
    ///   Surface Nets で網にする（作り直しても同じ形。Assets/_Project/Meshes/Stages/City/CloudPuff*）。頂点の色に、かたまりの中の高さ（r）と
    ///   外への開け具合（g。くぼみほど小さい）を入れ、シェーダー（Airerage/CloudPuff）で光と影を付ける。
    /// - 並べ方（手前は厚く、奥は薄く。差は前より小さく）: 手前（足場のビルのまわり）は高さのあるかたまりを、ところどころ切れ間を空けて
    ///   （切れ間から遥か下の大通りがのぞく）。奥へ行くほど横に広く背の低いかたまりにして、ほぼ途切れない雲海に。近いかたまりほど大きく
    ///   単純、遠いほど重なって細かく見える（空のアートの考え方）。高層ビル（足場のビル・西新宿・遠くの地区）は雲海を突き抜けて立つ。
    /// - 昼は日の当たる面が白く影が青みの灰色、夜は月明かりの淡い紫と、下から街の明かり（ネオンのピンク）。
    /// 表示だけ（当たりはない。撃破の線より下）。
    /// </summary>
    internal static class CityClouds
    {
        /// <summary>雲海のだいたいの上の高さ（撃破の線 −22 のすぐ下。ワールドの y）。</summary>
        public const float SeaTop = -27f;

        /// <summary>雲のてっぺんの上限（撃破の線 −22 より下。落ちかけのプレイヤーを雲で隠さない）。</summary>
        public const float TopLimit = -24f;

        private const string MeshFolder = "Assets/_Project/Meshes/Stages/City";

        // かたまりの形の種類（手前用の背の高い積雲、真ん中用の少し粗い積雲、奥用の平たい層積雲）と、網の細かさ（1 単位あたりのます目の数。
        // 遠いほど粗くして三角形を減らす）。
        private const int TallVariants = 8;
        private const int MidVariants = 6;
        private const int FlatVariants = 5;
        private const int TallResolution = 14;
        private const int MidResolution = 10;
        private const int FlatResolution = 9;

        // 並べる帯: 奥行き z の範囲・横の範囲・ます目の大きさ（x と z）・埋める割合・かたまりの幅・高さ・底の高さ・平たい形か。
        // 手前ほど背が高く（縦幅）、奥ほど横に広く背が低い（横幅）。手前は切れ間が多く、奥はほぼ途切れない。
        // Kind: 0 手前の積雲・1 真ん中の積雲・2 奥の平たい層積雲。
        private static readonly (float ZMin, float ZMax, float XHalf, float CellX, float CellZ, float Fill, float WidthMin, float WidthMax,
            float HeightMin, float HeightMax, float BaseMin, float BaseMax, int Kind)[] Bands =
        {
            (-60f, 140f, 300f, 40f, 34f, 0.85f, 40f, 75f, 12f, 20f, -46f, -40f, 0),
            (140f, 420f, 600f, 80f, 60f, 0.92f, 80f, 140f, 10f, 16f, -48f, -42f, 1),
            (420f, 990f, 1200f, 150f, 100f, 1f, 180f, 320f, 7f, 12f, -50f, -44f, 2),
        };

        public static void Build(StageDecor decor, bool night)
        {
            var root = new GameObject("CloudSea").transform;
            root.SetParent(decor.Root, false);
            var tall = new Mesh[TallVariants];
            for (int i = 0; i < tall.Length; i++)
            {
                tall[i] = Cumulus($"CloudPuffTall{i}", 101 + i, false, TallResolution);
            }
            var mid = new Mesh[MidVariants];
            for (int i = 0; i < mid.Length; i++)
            {
                mid[i] = Cumulus($"CloudPuffMid{i}", 151 + i, false, MidResolution);
            }
            var flat = new Mesh[FlatVariants];
            for (int i = 0; i < flat.Length; i++)
            {
                flat[i] = Cumulus($"CloudPuffFlat{i}", 201 + i, true, FlatResolution);
            }
            var pools = new[] { tall, mid, flat };
            // 夜は月明かりの当たる上の面を淡い紫に、影を濃い藍に、下からの街の明かり（ネオンのピンク）を強めに。
            Material material = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("CloudPuff", "Airerage/CloudPuff", m =>
            {
                m.SetColor("_TopColor", night ? new Color(0.6f, 0.54f, 0.78f) : new Color(1f, 0.99f, 0.97f));
                m.SetColor("_ShadeColor", night ? new Color(0.09f, 0.07f, 0.17f) : new Color(0.58f, 0.66f, 0.79f));
                m.SetColor("_GlowColor", night ? new Color(0.42f, 0.11f, 0.33f) : Color.black);
                m.SetFloat("_Opacity", 0.97f);
                m.SetFloat("_Softness", 0.35f);
                // 夜は陰を少し強く（月明かりは弱いので、包み込みを小さく・もこもこを濃く）。
                m.SetFloat("_Wrap", night ? 0.3f : 0.55f);
                m.SetFloat("_DetailScale", 0.12f);
                m.SetFloat("_DetailStrength", night ? 0.5f : 0.35f);
                m.SetFloat("_Lining", night ? 0.25f : 0.6f);
                m.SetVector("_Drift", new Vector4(0.4f, 0f, 0.15f, 0f));
                m.SetFloat("_NearFade", 12f);
                m.enableInstancing = true;
            }, decor.Folder);

            // 乱数はこの雲海だけのもの（decor.Random を使うと、後に並べるビルの並びが変わるため）。
            var random = new System.Random(4711);
            int placed = 0;
            foreach (var band in Bands)
            {
                for (float z = band.ZMin; z < band.ZMax; z += band.CellZ)
                {
                    // 行ごとに横へずらす（並びが格子に見えないように）。
                    float rowShift = Range(random, 0f, band.CellX);
                    for (float x = -band.XHalf - rowShift; x < band.XHalf; x += band.CellX)
                    {
                        if (random.NextDouble() > band.Fill)
                        {
                            continue;
                        }
                        float width = Range(random, band.WidthMin, band.WidthMax);
                        float height = Range(random, band.HeightMin, band.HeightMax);
                        float depth = width * Range(random, 0.55f, 0.85f);
                        var position = new Vector3(x + Range(random, 0f, band.CellX), Range(random, band.BaseMin, band.BaseMax),
                            z + Range(random, 0f, band.CellZ));
                        Mesh[] pool = pools[band.Kind];
                        Mesh mesh = pool[random.Next(pool.Length)];
                        // 左右の向きは鏡に映して変える（縦の軸で回すと、網の三角形の奥から手前の並びが崩れるため）。
                        float mirror = random.NextDouble() < 0.5 ? -1f : 1f;
                        // 形の網は幅 2・高さ 1・奥行き 2 ほどの大きさ。てっぺんは TopLimit まで（高すぎる形は背を縮める）。
                        height = Mathf.Min(height, (TopLimit - position.y) / Mathf.Max(0.1f, mesh.bounds.max.y));
                        var scale = new Vector3(width * 0.5f * mirror, height, depth * 0.5f);
                        var go = new GameObject("CloudPuff");
                        go.transform.SetParent(root, false);
                        go.transform.SetPositionAndRotation(position, Quaternion.identity);
                        go.transform.localScale = scale;
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var renderer = go.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = material;
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                        placed++;
                    }
                }
            }
            Debug.Log($"雲海: 立体の雲のかたまりを {placed} 個置いた（{(night ? "夜" : "昼")}）");
        }

        private static float Range(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        // ---- かたまりの形（距離の場と Surface Nets） ----

        private struct Ball
        {
            public Vector3 Center;
            public float Radius;
        }

        // 積雲の球の並び（種から決まる）。高い形は真ん中が盛り上がったこぶの山、平たい形は横に広い低いこぶの並び。
        private static List<Ball> Balls(int seed, bool flat)
        {
            var random = new System.Random(seed);
            var balls = new List<Ball>();
            // 胴（底に沿った大きな球）。
            int body = flat ? random.Next(6, 9) : random.Next(4, 7);
            for (int i = 0; i < body; i++)
            {
                float t = body == 1 ? 0f : i / (float)(body - 1) * 2f - 1f;
                float radius = flat ? Range(random, 0.3f, 0.42f) : Range(random, 0.34f, 0.5f) * (1f - Mathf.Abs(t) * 0.35f);
                balls.Add(new Ball
                {
                    Center = new Vector3(t * (flat ? 0.75f : 0.62f) + Range(random, -0.06f, 0.06f), flat ? 0.22f : radius * 0.75f,
                        Range(random, -0.32f, 0.32f)),
                    Radius = radius,
                });
            }
            // 上のこぶ（真ん中ほど高い）。
            int domes = flat ? random.Next(3, 6) : random.Next(3, 6);
            for (int i = 0; i < domes; i++)
            {
                float x = Range(random, -0.5f, 0.5f);
                float radius = flat ? Range(random, 0.18f, 0.28f) : Range(random, 0.22f, 0.36f) * (1f - Mathf.Abs(x) * 0.5f);
                float y = flat ? Range(random, 0.38f, 0.5f) : Range(random, 0.55f, 0.75f) * (1f - Mathf.Abs(x) * 0.45f);
                balls.Add(new Ball { Center = new Vector3(x, y, Range(random, -0.22f, 0.22f)), Radius = radius });
            }
            // 小さなこぶ（表面のもこもこ）。大きい球の上の半分に載せる。
            int bumps = random.Next(8, 13);
            int large = balls.Count;
            for (int i = 0; i < bumps; i++)
            {
                Ball parent = balls[random.Next(large)];
                var direction = new Vector3(Range(random, -1f, 1f), Range(random, 0.2f, 1f), Range(random, -1f, 1f)).normalized;
                float radius = parent.Radius * Range(random, 0.32f, 0.5f);
                balls.Add(new Ball { Center = parent.Center + direction * parent.Radius * 0.92f, Radius = radius });
            }
            return balls;
        }

        // 積雲の距離の場（負が中）: 球のなめらかな和、底を平らに切る、表面を少し波打たせる。
        private static float Field(Vector3 p, List<Ball> balls, float baseY)
        {
            const float Blend = 0.12f;
            float d = float.MaxValue;
            foreach (Ball ball in balls)
            {
                float sphere = Vector3.Distance(p, ball.Center) - ball.Radius;
                d = d == float.MaxValue ? sphere : SmoothMin(d, sphere, Blend);
            }
            d += (ValueNoise(p * 7f) - 0.5f) * 0.035f;
            // 平らな底（なめらかに角を丸めて切る）。
            return SmoothMax(d, baseY - p.y, 0.05f);
        }

        private static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        private static float SmoothMax(float a, float b, float k)
        {
            return -SmoothMin(-a, -b, k);
        }

        private static float Hash(int x, int y, int z)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + z * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (float)int.MaxValue;
            }
        }

        private static float ValueNoise(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x);
            int y = Mathf.FloorToInt(p.y);
            int z = Mathf.FloorToInt(p.z);
            float fx = p.x - x;
            float fy = p.y - y;
            float fz = p.z - z;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            fz = fz * fz * (3f - 2f * fz);
            float c00 = Mathf.Lerp(Hash(x, y, z), Hash(x + 1, y, z), fx);
            float c10 = Mathf.Lerp(Hash(x, y + 1, z), Hash(x + 1, y + 1, z), fx);
            float c01 = Mathf.Lerp(Hash(x, y, z + 1), Hash(x + 1, y, z + 1), fx);
            float c11 = Mathf.Lerp(Hash(x, y + 1, z + 1), Hash(x + 1, y + 1, z + 1), fx);
            return Mathf.Lerp(Mathf.Lerp(c00, c10, fy), Mathf.Lerp(c01, c11, fy), fz);
        }

        /// <summary>
        /// 積雲の網を作る（Surface Nets: 場の符号が変わるます目ごとに 1 つの頂点を置き、符号が変わる辺のまわりの 4 つのます目をつなぐ）。
        /// 大きさはだいたい幅 2（x −1〜1）・高さ 1（y 0〜1）・奥行き 2。法線は場の傾き。三角形は奥（z が大きい）から手前の順に並べる。
        /// </summary>
        private static Mesh Cumulus(string name, int seed, bool flat, int resolution)
        {
            List<Ball> balls = Balls(seed, flat);
            const float BaseY = 0.04f;
            // 小さなこぶが大きな球の外へはみ出す分も含めて、場の外（正）で囲む。
            var min = new Vector3(-1.55f, -0.05f, -1.15f);
            var max = new Vector3(1.55f, flat ? 0.95f : 1.35f, 1.15f);
            float step = 1f / resolution;
            int nx = Mathf.CeilToInt((max.x - min.x) / step);
            int ny = Mathf.CeilToInt((max.y - min.y) / step);
            int nz = Mathf.CeilToInt((max.z - min.z) / step);
            var values = new float[(nx + 1) * (ny + 1) * (nz + 1)];
            int Index(int i, int j, int k) => (k * (ny + 1) + j) * (nx + 1) + i;
            Vector3 Point(int i, int j, int k) => min + new Vector3(i, j, k) * step;
            for (int k = 0; k <= nz; k++)
            {
                for (int j = 0; j <= ny; j++)
                {
                    for (int i = 0; i <= nx; i++)
                    {
                        values[Index(i, j, k)] = Field(Point(i, j, k), balls, BaseY);
                    }
                }
            }
            // ます目ごとの頂点（符号が変わる辺の交点の平均）。
            var cellVertex = new int[nx * ny * nz];
            int Cell(int i, int j, int k) => (k * ny + j) * nx + i;
            var vertices = new List<Vector3>();
            int[,] corners = { { 0, 0, 0 }, { 1, 0, 0 }, { 0, 1, 0 }, { 1, 1, 0 }, { 0, 0, 1 }, { 1, 0, 1 }, { 0, 1, 1 }, { 1, 1, 1 } };
            int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            var corner = new float[8];
            for (int k = 0; k < nz; k++)
            {
                for (int j = 0; j < ny; j++)
                {
                    for (int i = 0; i < nx; i++)
                    {
                        int inside = 0;
                        for (int c = 0; c < 8; c++)
                        {
                            corner[c] = values[Index(i + corners[c, 0], j + corners[c, 1], k + corners[c, 2])];
                            if (corner[c] < 0f)
                            {
                                inside++;
                            }
                        }
                        if (inside == 0 || inside == 8)
                        {
                            cellVertex[Cell(i, j, k)] = -1;
                            continue;
                        }
                        Vector3 sum = Vector3.zero;
                        int crossings = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            float a = corner[edges[e, 0]];
                            float b = corner[edges[e, 1]];
                            if ((a < 0f) == (b < 0f))
                            {
                                continue;
                            }
                            float t = a / (a - b);
                            var pa = new Vector3(corners[edges[e, 0], 0], corners[edges[e, 0], 1], corners[edges[e, 0], 2]);
                            var pb = new Vector3(corners[edges[e, 1], 0], corners[edges[e, 1], 1], corners[edges[e, 1], 2]);
                            sum += Vector3.Lerp(pa, pb, t);
                            crossings++;
                        }
                        cellVertex[Cell(i, j, k)] = vertices.Count;
                        vertices.Add(min + (new Vector3(i, j, k) + sum / crossings) * step);
                    }
                }
            }
            // 符号が変わる辺のまわりの 4 つのます目を四角形でつなぐ（向きは場の傾きに合わせる）。
            var quads = new List<int[]>();
            void Quad(int a, int b, int c, int d)
            {
                if (a < 0 || b < 0 || c < 0 || d < 0)
                {
                    return;
                }
                quads.Add(new[] { a, b, c, d });
            }
            for (int k = 0; k <= nz; k++)
            {
                for (int j = 0; j <= ny; j++)
                {
                    for (int i = 0; i <= nx; i++)
                    {
                        float here = values[Index(i, j, k)];
                        if (i < nx && j > 0 && k > 0 && j < ny && k < nz && (here < 0f) != (values[Index(i + 1, j, k)] < 0f))
                        {
                            Quad(cellVertex[Cell(i, j - 1, k - 1)], cellVertex[Cell(i, j, k - 1)], cellVertex[Cell(i, j, k)], cellVertex[Cell(i, j - 1, k)]);
                        }
                        if (j < ny && i > 0 && k > 0 && i < nx && k < nz && (here < 0f) != (values[Index(i, j + 1, k)] < 0f))
                        {
                            Quad(cellVertex[Cell(i - 1, j, k - 1)], cellVertex[Cell(i, j, k - 1)], cellVertex[Cell(i, j, k)], cellVertex[Cell(i - 1, j, k)]);
                        }
                        if (k < nz && i > 0 && j > 0 && i < nx && j < ny && (here < 0f) != (values[Index(i, j, k + 1)] < 0f))
                        {
                            Quad(cellVertex[Cell(i - 1, j - 1, k)], cellVertex[Cell(i, j - 1, k)], cellVertex[Cell(i, j, k)], cellVertex[Cell(i - 1, j, k)]);
                        }
                    }
                }
            }
            // 法線（場の傾き）・頂点の色（r: かたまりの中の高さ、g: 外への開け具合）。
            var normals = new Vector3[vertices.Count];
            var colors = new Color[vertices.Count];
            float top = float.MinValue;
            foreach (Vector3 v in vertices)
            {
                top = Mathf.Max(top, v.y);
            }
            const float H = 0.01f;
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 p = vertices[i];
                var gradient = new Vector3(
                    Field(p + new Vector3(H, 0f, 0f), balls, BaseY) - Field(p - new Vector3(H, 0f, 0f), balls, BaseY),
                    Field(p + new Vector3(0f, H, 0f), balls, BaseY) - Field(p - new Vector3(0f, H, 0f), balls, BaseY),
                    Field(p + new Vector3(0f, 0f, H), balls, BaseY) - Field(p - new Vector3(0f, 0f, H), balls, BaseY));
                Vector3 n = gradient.sqrMagnitude > 1e-12f ? gradient.normalized : Vector3.up;
                normals[i] = n;
                // くぼみ: 法線の向きへ少し出た所が、まだ雲に近い（場が小さい）ほど暗い。
                const float Reach = 0.18f;
                float open = Mathf.Clamp01(Field(p + n * Reach, balls, BaseY) / Reach);
                float height = Mathf.Clamp01((p.y - BaseY) / Mathf.Max(0.01f, top - BaseY));
                colors[i] = new Color(height, Mathf.Lerp(0.35f, 1f, open), 0f, 1f);
            }
            // 三角形: 四角形を 2 つに分け、法線と向きを合わせ、奥から手前の順に並べる。
            var triangles = new List<(float Z, int A, int B, int C)>();
            foreach (int[] q in quads)
            {
                AddTriangle(triangles, vertices, normals, q[0], q[1], q[2]);
                AddTriangle(triangles, vertices, normals, q[0], q[2], q[3]);
            }
            triangles.Sort((x, y) => y.Z.CompareTo(x.Z));
            var indices = new int[triangles.Count * 3];
            for (int t = 0; t < triangles.Count; t++)
            {
                indices[t * 3] = triangles[t].A;
                indices[t * 3 + 1] = triangles[t].B;
                indices[t * 3 + 2] = triangles[t].C;
            }

            string path = $"{MeshFolder}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                SandboxWireSceneBuilder.EnsureFolder(MeshFolder);
                mesh = new Mesh { name = name };
                AssetDatabase.CreateAsset(mesh, path);
            }
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // 三角形を、面の向きが場の外向き（頂点の法線の平均）と合うように加える。
        private static void AddTriangle(List<(float Z, int A, int B, int C)> triangles, List<Vector3> vertices, Vector3[] normals, int a, int b, int c)
        {
            Vector3 pa = vertices[a];
            Vector3 pb = vertices[b];
            Vector3 pc = vertices[c];
            Vector3 face = Vector3.Cross(pb - pa, pc - pa);
            Vector3 outward = normals[a] + normals[b] + normals[c];
            float z = (pa.z + pb.z + pc.z) / 3f;
            // Unity の表の面は Cross(b - a, c - a) の向き（Quad の基本の形で確かめた）。外向きと同じなら a, b, c のまま。
            if (Vector3.Dot(face, outward) > 0f)
            {
                triangles.Add((z, a, b, c));
            }
            else
            {
                triangles.Add((z, a, c, b));
            }
        }
    }
}
