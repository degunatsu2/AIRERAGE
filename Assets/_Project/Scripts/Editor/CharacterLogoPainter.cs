using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Airerage.Editor
{
    /// <summary>
    /// キャラクターのロゴ（仮の絵。docs/13 T5-8。ユーザーの案、2026-10-16）を描いて、テクスチャのアセット（.asset。テキスト形式なので
    /// Git LFS を使わない）に保存する。本番の絵ができたら、キャラクターのアセットの Logo を差し替える（差し替えた絵は上書きしない）。
    /// - STANDARD: 白い、ふつうの人型。
    /// - SPACE TRAVELER: 紫の、土星のような惑星。真ん中に斜めの切れ込み。
    /// - STORM RUNNER: 黄緑の、風。
    /// - RISK TAKER: 黄色の、お金が降ってくる金庫。
    /// - PASTRY CHEF: クリーム色の、コック帽（ふくらんだ 3 つの山と帯）。
    /// - SOLDERER: 電気の水色の、稲妻（RISK TAKER の黄色・STORM RUNNER の黄緑と見分けられる色）。
    /// - THIEF: 茶色の巾着袋に、斜めの銀のナイフが重なる。
    /// 形は距離の関数（負なら中）で書き、縁はなめらかにする。形ごとに暗い縁取りをつける（どの背景でも読めるように）。
    /// 座標は 0〜1（左上が 0、下が 1）。
    /// </summary>
    internal static class CharacterLogoPainter
    {
        /// <summary>ロゴの大きさ（ピクセル）。</summary>
        public const int Size = 96;

        public const string Folder = "Assets/_Project/Data/Characters/Logos";

        private const float Outline = 0.03f;
        private static readonly Color OutlineColor = new Color(0.02f, 0.03f, 0.06f, 0.75f);

        public static readonly Color StandardColor = Color.white;
        public static readonly Color SpaceTravelerColor = new Color(0.66f, 0.4f, 1f);
        public static readonly Color StormRunnerColor = new Color(0.68f, 1f, 0.2f);
        public static readonly Color RiskTakerColor = new Color(1f, 0.84f, 0.15f);
        public static readonly Color PastryChefColor = new Color(1f, 0.93f, 0.74f);
        public static readonly Color SoldererColor = new Color(0.36f, 0.88f, 1f);
        public static readonly Color ThiefColor = new Color(0.74f, 0.47f, 0.24f);
        private static readonly Color ThiefStringColor = new Color(0.9f, 0.7f, 0.42f);
        private static readonly Color KnifeColor = new Color(0.86f, 0.89f, 0.94f);
        private static readonly Color KnifeHandleColor = new Color(0.3f, 0.18f, 0.1f);

        private delegate float Shape(Vector2 p);

        private struct Layer
        {
            public Shape Shape;
            public Color Color;
            public bool Cut;
        }

        /// <summary>STANDARD: 頭と肩の、ふつうの人型（白）。</summary>
        public static Color[] Standard()
        {
            var head = new Vector2(0.5f, 0.27f);
            var layers = new List<Layer>();
            // 肩（下は平らに切る）。
            Fill(layers, p => Mathf.Max(RoundBox(p, new Vector2(0.5f, 0.8f), new Vector2(0.29f, 0.26f), 0.2f), p.y - 0.9f), StandardColor);
            Cut(layers, p => Circle(p, head, 0.185f));
            Fill(layers, p => Circle(p, head, 0.14f), StandardColor);
            return Rasterize(layers);
        }

        /// <summary>SPACE TRAVELER: 惑星の真ん中に斜めの切れ込み。切れ込みの向きに、惑星の外へ輪が出る（紫）。</summary>
        public static Color[] SpaceTraveler()
        {
            var center = new Vector2(0.5f, 0.5f);
            const float radius = 0.29f;
            const float angle = 26f;
            var layers = new List<Layer>();
            Fill(layers, p => Circle(p, center, radius), SpaceTravelerColor);
            Cut(layers, p => Mathf.Abs(Rotate(p - center, angle).y) - 0.032f);
            Fill(layers, p =>
            {
                Vector2 q = Rotate(p - center, angle);
                float ring = Mathf.Abs(Ellipse(q, new Vector2(0.47f, 0.1f))) - 0.024f;
                // 惑星の後ろに回る部分は描かない（惑星の縁から少し離す）。
                return Mathf.Max(ring, -(Circle(p, center, radius) - 0.035f));
            }, SpaceTravelerColor);
            return Rasterize(layers);
        }

        /// <summary>STORM RUNNER: 先が渦を巻く 3 本の風の線（黄緑）。</summary>
        public static Color[] StormRunner()
        {
            const float width = 0.04f;
            var layers = new List<Layer>();
            // 上: 右の先が上へ巻く。
            Fill(layers, p => Mathf.Min(Segment(p, new Vector2(0.14f, 0.34f), new Vector2(0.6f, 0.34f), width),
                Arc(p, new Vector2(0.6f, 0.24f), 0.1f, 90f, -240f, width)), StormRunnerColor);
            // 真ん中: いちばん長い。
            Fill(layers, p => Segment(p, new Vector2(0.08f, 0.53f), new Vector2(0.86f, 0.53f), width), StormRunnerColor);
            // 下: 右の先が下へ巻く。
            Fill(layers, p => Mathf.Min(Segment(p, new Vector2(0.22f, 0.72f), new Vector2(0.64f, 0.72f), width),
                Arc(p, new Vector2(0.64f, 0.82f), 0.1f, -90f, 240f, width)), StormRunnerColor);
            return Rasterize(layers);
        }

        /// <summary>PASTRY CHEF: ふくらんだ 3 つの山のコック帽と、下の帯（帯に横のみぞ。クリーム色）。</summary>
        public static Color[] PastryChef()
        {
            var layers = new List<Layer>();
            // ふくらんだ上（3 つの円と、つなぐ箱）。
            Fill(layers, p => Mathf.Min(Mathf.Min(Circle(p, new Vector2(0.3f, 0.42f), 0.17f), Circle(p, new Vector2(0.7f, 0.42f), 0.17f)),
                Mathf.Min(Circle(p, new Vector2(0.5f, 0.3f), 0.21f), RoundBox(p, new Vector2(0.5f, 0.52f), new Vector2(0.25f, 0.1f), 0.02f))),
                PastryChefColor);
            // 山の間のくぼみ。
            Cut(layers, p => Mathf.Min(Segment(p, new Vector2(0.37f, 0.36f), new Vector2(0.39f, 0.55f), 0.012f),
                Segment(p, new Vector2(0.63f, 0.36f), new Vector2(0.61f, 0.55f), 0.012f)));
            // 帯。
            Fill(layers, p => RoundBox(p, new Vector2(0.5f, 0.74f), new Vector2(0.24f, 0.1f), 0.03f), PastryChefColor);
            Cut(layers, p => Segment(p, new Vector2(0.3f, 0.74f), new Vector2(0.7f, 0.74f), 0.012f));
            return Rasterize(layers);
        }

        /// <summary>SOLDERER: 斜めに走る稲妻と、その両側に小さな火花（電気の水色）。</summary>
        public static Color[] Solderer()
        {
            var layers = new List<Layer>();
            // 稲妻（上は平らで、真ん中で段違いに折れ、下は尖る）。
            Vector2[] bolt =
            {
                new Vector2(0.56f, 0.04f), new Vector2(0.74f, 0.04f), new Vector2(0.6f, 0.38f), new Vector2(0.8f, 0.38f),
                new Vector2(0.36f, 0.96f), new Vector2(0.46f, 0.54f), new Vector2(0.24f, 0.54f),
            };
            Fill(layers, p => Polygon(p, bolt), SoldererColor);
            // 火花（短い線の組）。
            Fill(layers, p => Mathf.Min(Segment(p, new Vector2(0.14f, 0.28f), new Vector2(0.24f, 0.34f), 0.022f),
                Segment(p, new Vector2(0.12f, 0.42f), new Vector2(0.22f, 0.42f), 0.022f)), SoldererColor);
            Fill(layers, p => Mathf.Min(Segment(p, new Vector2(0.86f, 0.62f), new Vector2(0.76f, 0.66f), 0.022f),
                Segment(p, new Vector2(0.88f, 0.76f), new Vector2(0.78f, 0.74f), 0.022f)), SoldererColor);
            return Rasterize(layers);
        }

        /// <summary>THIEF: 口を紐で絞った茶色の巾着袋に、左下から右上へ斜めの銀のナイフ（つばと暗い柄）が重なる。</summary>
        public static Color[] Thief()
        {
            var layers = new List<Layer>();
            // 袋（丸い胴と、絞った首と、口のひだ）。
            Fill(layers, p => Mathf.Min(Circle(p, new Vector2(0.47f, 0.63f), 0.27f),
                RoundBox(p, new Vector2(0.47f, 0.35f), new Vector2(0.085f, 0.06f), 0.02f)), ThiefColor);
            Fill(layers, p => Mathf.Min(Mathf.Min(Circle(p, new Vector2(0.35f, 0.25f), 0.07f), Circle(p, new Vector2(0.59f, 0.25f), 0.07f)),
                Circle(p, new Vector2(0.47f, 0.22f), 0.08f)), ThiefColor);
            // 絞った紐と、垂れた端。
            Fill(layers, p => Mathf.Min(Segment(p, new Vector2(0.36f, 0.34f), new Vector2(0.58f, 0.34f), 0.018f),
                Segment(p, new Vector2(0.58f, 0.34f), new Vector2(0.66f, 0.44f), 0.016f)), ThiefStringColor);
            // ナイフ（刃は左下の根元から右上の切っ先へ。背はまっすぐで、刃先の側が切っ先の手前でふくらむ）。
            Vector2[] blade =
            {
                new Vector2(0.285f, 0.705f), new Vector2(0.805f, 0.228f), new Vector2(0.87f, 0.19f), new Vector2(0.8f, 0.31f),
                new Vector2(0.355f, 0.775f),
            };
            Fill(layers, p => Polygon(p, blade), KnifeColor);
            Fill(layers, p => Segment(p, new Vector2(0.245f, 0.665f), new Vector2(0.395f, 0.815f), 0.024f), KnifeColor);
            Fill(layers, p => Mathf.Min(Segment(p, new Vector2(0.3f, 0.76f), new Vector2(0.17f, 0.89f), 0.036f),
                Circle(p, new Vector2(0.15f, 0.91f), 0.04f)), KnifeHandleColor);
            return Rasterize(layers);
        }

        /// <summary>RISK TAKER: 扉とダイヤルのある金庫に、上からコインとお札が降る（黄色）。</summary>
        public static Color[] RiskTaker()
        {
            var safe = new Vector2(0.5f, 0.7f);
            var layers = new List<Layer>();
            // 本体と、扉のみぞ、ダイヤル、脚。
            Fill(layers, p => RoundBox(p, safe, new Vector2(0.28f, 0.21f), 0.05f), RiskTakerColor);
            Cut(layers, p => Mathf.Abs(RoundBox(p, safe, new Vector2(0.21f, 0.14f), 0.03f)) - 0.014f);
            Cut(layers, p => Mathf.Abs(Circle(p, safe, 0.075f)) - 0.016f);
            Cut(layers, p => Mathf.Min(Segment(p, safe + new Vector2(-0.04f, 0f), safe + new Vector2(0.04f, 0f), 0.012f),
                Segment(p, safe + new Vector2(0f, -0.04f), safe + new Vector2(0f, 0.04f), 0.012f)));
            Fill(layers, p => Mathf.Min(RoundBox(p, new Vector2(0.32f, 0.94f), new Vector2(0.05f, 0.03f), 0.01f),
                RoundBox(p, new Vector2(0.68f, 0.94f), new Vector2(0.05f, 0.03f), 0.01f)), RiskTakerColor);
            // 降ってくるコイン（真ん中に輪）とお札（傾いた長方形に、真ん中の丸）。
            foreach (Vector3 coin in new[] { new Vector3(0.17f, 0.22f, 0.075f), new Vector3(0.56f, 0.1f, 0.07f), new Vector3(0.86f, 0.33f, 0.065f) })
            {
                var c = new Vector2(coin.x, coin.y);
                float r = coin.z;
                Fill(layers, p => Circle(p, c, r), RiskTakerColor);
                Cut(layers, p => Mathf.Abs(Circle(p, c, r * 0.55f)) - 0.008f);
            }
            foreach (Vector3 bill in new[] { new Vector3(0.37f, 0.3f, 18f), new Vector3(0.78f, 0.14f, -22f) })
            {
                var c = new Vector2(bill.x, bill.y);
                float tilt = bill.z;
                Fill(layers, p => RoundBox(Rotate(p - c, tilt), Vector2.zero, new Vector2(0.105f, 0.055f), 0.01f), RiskTakerColor);
                Cut(layers, p => Circle(Rotate(p - c, tilt), Vector2.zero, 0.024f));
            }
            return Rasterize(layers);
        }

        /// <summary>
        /// ロゴを path のテクスチャのアセットに保存する（あれば同じアセットに描き直す。GUID は変えない）。pixels は Rasterize の結果。
        /// </summary>
        public static Texture2D Write(string path, Color[] pixels)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                string folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
                {
                    string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
                    AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
                }
                texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = Path.GetFileNameWithoutExtension(path) };
                Fill(texture, pixels);
                AssetDatabase.CreateAsset(texture, path);
                return texture;
            }
            Fill(texture, pixels);
            EditorUtility.SetDirty(texture);
            return texture;
        }

        private static void Fill(Texture2D texture, Color[] pixels)
        {
            texture.filterMode = FilterMode.Trilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(pixels);
            texture.Apply(true);
        }

        private static void Fill(List<Layer> layers, Shape shape, Color color)
        {
            layers.Add(new Layer { Shape = shape, Color = color });
        }

        private static void Cut(List<Layer> layers, Shape shape)
        {
            layers.Add(new Layer { Shape = shape, Cut = true });
        }

        // 形を順に重ねる（色は不透明さを掛けた値で足す）。塗る形は、先に暗い縁取りを敷く。切る形は、それまでに描いたものを消す。
        // テクスチャは下の行から並ぶので、上下を反転して返す。
        private static Color[] Rasterize(List<Layer> layers)
        {
            var pixels = new Color[Size * Size];
            const float pixel = 1f / Size;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector2((x + 0.5f) * pixel, (y + 0.5f) * pixel);
                    Color sum = Color.clear;
                    foreach (Layer layer in layers)
                    {
                        float d = layer.Shape(p);
                        if (layer.Cut)
                        {
                            sum *= 1f - Coverage(d, pixel);
                            continue;
                        }
                        sum = Over(sum, OutlineColor, Coverage(d - Outline, pixel) * OutlineColor.a);
                        sum = Over(sum, layer.Color, Coverage(d, pixel) * layer.Color.a);
                    }
                    Color result = sum.a > 1e-5f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a) : Color.clear;
                    pixels[(Size - 1 - y) * Size + x] = result;
                }
            }
            return pixels;
        }

        private static Color Over(Color sum, Color color, float alpha)
        {
            return new Color(color.r * alpha, color.g * alpha, color.b * alpha, alpha) + sum * (1f - alpha);
        }

        // 距離 d（0〜1 の座標）の、1 ピクセルの中で形が占める割合。
        private static float Coverage(float d, float pixel)
        {
            return Mathf.Clamp01(0.5f - d / pixel);
        }

        private static float Circle(Vector2 p, Vector2 center, float radius)
        {
            return (p - center).magnitude - radius;
        }

        private static float RoundBox(Vector2 p, Vector2 center, Vector2 half, float round)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - half + new Vector2(round, round);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - round;
        }

        // 太さ 2 × width の、両端の丸い線。
        private static float Segment(Vector2 p, Vector2 a, Vector2 b, float width)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude - width;
        }

        // 中心 center・半径 radius の円の、角度 from から sweep 度ぶんの弧（太さ 2 × width、両端は丸い）。角度は右が 0、下が 90（y は下向き）。
        private static float Arc(Vector2 p, Vector2 center, float radius, float from, float sweep, float width)
        {
            Vector2 d = p - center;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float t = Mathf.DeltaAngle(from, angle);
            float along = sweep >= 0f ? Mathf.Repeat(t, 360f) : Mathf.Repeat(-t, 360f);
            if (along <= Mathf.Abs(sweep))
            {
                return Mathf.Abs(d.magnitude - radius) - width;
            }
            Vector2 start = center + Direction(from) * radius;
            Vector2 end = center + Direction(from + sweep) * radius;
            return Mathf.Min((p - start).magnitude, (p - end).magnitude) - width;
        }

        // 多角形（頂点を順に並べる。凹んでいてもよい）までの距離（中は負）。
        private static float Polygon(Vector2 p, Vector2[] points)
        {
            float distance = (p - points[0]).sqrMagnitude;
            float sign = 1f;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i, i++)
            {
                Vector2 e = points[j] - points[i];
                Vector2 w = p - points[i];
                Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / e.sqrMagnitude);
                distance = Mathf.Min(distance, b.sqrMagnitude);
                bool c0 = p.y >= points[i].y;
                bool c1 = p.y < points[j].y;
                bool c2 = e.x * w.y > e.y * w.x;
                if ((c0 && c1 && c2) || (!c0 && !c1 && !c2))
                {
                    sign = -sign;
                }
            }
            return sign * Mathf.Sqrt(distance);
        }

        // 楕円（半径 radii）までのおおよその距離。
        private static float Ellipse(Vector2 q, Vector2 radii)
        {
            float k0 = new Vector2(q.x / radii.x, q.y / radii.y).magnitude;
            float k1 = new Vector2(q.x / (radii.x * radii.x), q.y / (radii.y * radii.y)).magnitude;
            return k1 > 1e-6f ? k0 * (k0 - 1f) / k1 : -Mathf.Min(radii.x, radii.y);
        }

        // 画面の上で反時計回りに degrees 度（y が下向きなので、符号を合わせる）回した座標系での p。
        private static Vector2 Rotate(Vector2 p, float degrees)
        {
            float a = -degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(a);
            float s = Mathf.Sin(a);
            return new Vector2(c * p.x + s * p.y, -s * p.x + c * p.y);
        }

        private static Vector2 Direction(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
    }
}
