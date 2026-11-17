using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// タイトルロゴ「AIRERAGE」（D-96。ユーザーの決定、2026-11-16: 案の I2 アンカー × F2 ラムダ × シアンを手直し）。
    /// 文字・アンカーの枠・ワイヤーを図形のメッシュで描く（大きくしてもぼやけない）。形は <see cref="TitleLogoShape"/>、時間の流れは
    /// <see cref="TitleLogoTimeline"/>。
    /// - 文字（横棒のない Λ・3 本線の E）とアンカーの枠は白黒の色（<see cref="Ink"/>）、ワイヤー（フック）とアンカーの中の菱形はシアン
    ///   （<see cref="Accent"/>）。アンカーは文字の右。
    /// - ワイヤーは文字とアンカーの枠の後ろを通り、最初の A の横棒の位置を通って、2 つの E の縦線になり、アンカーの中心へつながる。E の横線は
    ///   ワイヤーの斜めの線から始まる（イタリック体の E のような見た目）。
    /// - 出したとき（<see cref="Play"/>）: 文字が左から 1 文字ずつ入り、ワイヤーが角ごとに止まりながら（角ごとのイーズアウト）伸びる。
    ///   曲がる瞬間に角に小さな菱形の輪、アンカーにつながる瞬間に中の菱形がはねて枠が少しふくらむ。
    /// 演出を始めた時刻はシリアライズしてあるので、画面の切り替えの写し（Instantiate）も同じ所から続く。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TitleLogo : MaskableGraphic
    {
        [Tooltip("文字とアンカーの枠の色（暗い背景では白、白い背景では黒）")]
        [SerializeField] private Color _ink = Color.white;
        [Tooltip("ワイヤー（フック）とアンカーの中の菱形の色")]
        [SerializeField] private Color _accent = new Color(0.22f, 0.78f, 1f, 1f);
        [SerializeField, HideInInspector] private float _playedAt = -1000f;

        private readonly List<Vector2> _scratch = new List<Vector2>(16);

        public Color Ink => _ink;
        public Color Accent => _accent;

        /// <summary>演出を始めてからの秒数（テスト用。演出を始めていなければとても大きい数）。</summary>
        public float Elapsed => Application.isPlaying ? Time.unscaledTime - _playedAt : float.MaxValue;

        /// <summary>演出の途中か。</summary>
        public bool Playing => Elapsed < TitleLogoTimeline.Duration;

        /// <summary>色を決める（シーンのビルダー用）。</summary>
        public void Configure(Color ink, Color accent)
        {
            _ink = ink;
            _accent = accent;
            raycastTarget = false;
            SetVerticesDirty();
        }

        /// <summary>出す演出を始めからする。</summary>
        public void Play()
        {
            _playedAt = Time.unscaledTime;
            SetVerticesDirty();
        }

        /// <summary>演出を t 秒の所にする（途中のコマを撮るテスト用。そこから時間は進む）。</summary>
        public void Seek(float t)
        {
            _playedAt = Time.unscaledTime - t;
            SetVerticesDirty();
        }

        /// <summary>演出を終わりにする。</summary>
        public void Complete()
        {
            _playedAt = -1000f;
            SetVerticesDirty();
        }

        private void Update()
        {
            float t = Elapsed;
            if (t >= -0.1f && t < TitleLogoTimeline.Duration + 0.1f)
            {
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            Populate(vh);
            // 斜めの辺のギザギザを消す（形の外周に画面の 1 ピクセルのぼかし。UiEdgeSmoothing。2026-11-16 のユーザーの要望）。
            UiEdgeSmoothing.Apply(vh, this);
        }

        private void Populate(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            Rect b = TitleLogoShape.Bounds;
            float scale = Mathf.Min(r.width / b.width, r.height / b.height);
            if (scale <= 0f)
            {
                return;
            }
            var map = new Mapper(r.center, b.center, scale);
            float t = Elapsed;
            Color tint = color;
            Color ink = _ink * tint;
            Color accent = _accent * tint;

            // 1. ワイヤー（いちばん奥。文字と枠の後ろを通る）。
            float length = TitleLogoTimeline.WireLength(t);
            if (length > 0f)
            {
                TitleLogoShape.WireUpTo(length, _scratch);
                if (_scratch.Count >= 2)
                {
                    // ワイヤーは文字と同じ太さなので、鋭い角を尖らせると E の上の線より上へはみ出す。角は面取りにする。
                    Mesh.BevelStroke(vh, _scratch, TitleLogoShape.WireWidth, accent, map);
                }
            }

            // 2. 文字（左から 1 文字ずつ、左からすべり込んで濃くなる）。
            for (int i = 0; i < TitleLogoShape.LetterCount; i++)
            {
                TitleLogoTimeline.Letter(t, i, out float alpha, out float shift);
                if (alpha <= 0f)
                {
                    continue;
                }
                Color c = ink;
                c.a *= alpha;
                var offset = new Vector2(shift, 0f);
                foreach (Vector2[] line in TitleLogoShape.LetterLines(i))
                {
                    Mesh.Stroke(vh, Offset(line, offset), false, TitleLogoShape.StrokeWidth, c, map);
                }
                foreach (Vector2[] quad in TitleLogoShape.LetterQuads(i))
                {
                    Mesh.Fill(vh, Offset(quad, offset), c, map);
                }
            }

            // 3. アンカーの枠（文字の後に出て、つながる瞬間に少しふくらむ）。
            Vector2 center = TitleLogoShape.AnchorCenter;
            TitleLogoTimeline.Frame(t, out float frameAlpha, out float frameScale);
            if (frameAlpha > 0f)
            {
                Color c = ink;
                c.a *= frameAlpha;
                Mesh.Stroke(vh, TitleLogoShape.Diamond(center, TitleLogoShape.AnchorHalf * frameScale), true, TitleLogoShape.FrameWidth, c, map);
            }

            // 4. 中の菱形（ワイヤーがつながった瞬間にはねて出る）。
            float inner = TitleLogoTimeline.InnerScale(t);
            if (inner > 0f)
            {
                Mesh.Fill(vh, TitleLogoShape.Diamond(center, TitleLogoShape.InnerHalf * inner), accent, map);
            }

            // 5. 控えめな形の動き: 曲がる角の小さな菱形の輪と、つながったときに枠から広がる輪。
            for (int k = 0; k < TitleLogoTimeline.CornerCount; k++)
            {
                if (TitleLogoTimeline.CornerPulse(t, k, out float half, out float a))
                {
                    Color c = accent;
                    c.a *= a;
                    Mesh.Stroke(vh, TitleLogoShape.Diamond(TitleLogoShape.WirePoint(k + 1), half), true, TitleLogoShape.PulseWidth, c, map);
                }
            }
            if (TitleLogoTimeline.ConnectRing(t, out float ring, out float ringAlpha))
            {
                Color c = accent;
                c.a *= ringAlpha;
                Mesh.Stroke(vh, TitleLogoShape.Diamond(center, TitleLogoShape.AnchorHalf * ring), true, TitleLogoShape.PulseWidth, c, map);
            }
        }

        private static Vector2[] Offset(Vector2[] points, Vector2 offset)
        {
            if (offset == Vector2.zero)
            {
                return points;
            }
            var moved = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                moved[i] = points[i] + offset;
            }
            return moved;
        }

        // ロゴの座標（字の高さ 100、y は下向き）から、画面の座標（y は上向き）へ。
        private readonly struct Mapper
        {
            private readonly Vector2 _rectCenter;
            private readonly Vector2 _shapeCenter;
            private readonly float _scale;

            public Mapper(Vector2 rectCenter, Vector2 shapeCenter, float scale)
            {
                _rectCenter = rectCenter;
                _shapeCenter = shapeCenter;
                _scale = scale;
            }

            public Vector2 Map(Vector2 p) => new Vector2(_rectCenter.x + (p.x - _shapeCenter.x) * _scale, _rectCenter.y - (p.y - _shapeCenter.y) * _scale);

            public float Scale => _scale;
        }

        private static class Mesh
        {
            // 太さ width の線（角は尖らせ（とがり過ぎは MiterLimit で切る）、端はまっすぐ切る。SVG の stroke と同じ）。
            public static void Stroke(VertexHelper vh, IList<Vector2> points, bool closed, float width, Color color, Mapper map)
            {
                int n = points.Count;
                if (n < 2)
                {
                    return;
                }
                float half = width * 0.5f;
                int start = vh.currentVertCount;
                int count = closed ? n + 1 : n;
                for (int j = 0; j < count; j++)
                {
                    int i = j % n;
                    Vector2 p = points[i];
                    Vector2 left;
                    Vector2 right;
                    bool hasPrev = closed || i > 0;
                    bool hasNext = closed || i < n - 1;
                    Vector2 dPrev = hasPrev ? (p - points[(i - 1 + n) % n]).normalized : Vector2.zero;
                    Vector2 dNext = hasNext ? (points[(i + 1) % n] - p).normalized : Vector2.zero;
                    if (hasPrev && hasNext)
                    {
                        Vector2 n0 = new Vector2(-dPrev.y, dPrev.x);
                        Vector2 n1 = new Vector2(-dNext.y, dNext.x);
                        Vector2 miter = (n0 + n1).normalized;
                        float dot = Vector2.Dot(miter, n0);
                        float length = dot > 0.0001f ? half / dot : half;
                        length = Mathf.Min(length, half * TitleLogoShape.MiterLimit);
                        left = p + miter * length;
                        right = p - miter * length;
                    }
                    else
                    {
                        Vector2 d = hasNext ? dNext : dPrev;
                        Vector2 normal = new Vector2(-d.y, d.x);
                        left = p + normal * half;
                        right = p - normal * half;
                    }
                    vh.AddVert(map.Map(left), color, Vector2.zero);
                    vh.AddVert(map.Map(right), color, Vector2.zero);
                }
                for (int j = 0; j < count - 1; j++)
                {
                    int a = start + j * 2;
                    vh.AddTriangle(a, a + 1, a + 3);
                    vh.AddTriangle(a, a + 3, a + 2);
                }
            }

            // 太さ width の折れ線（区間ごとの四角と、角の外側を斜めに平らに切った三角。端はまっすぐ切る）。
            public static void BevelStroke(VertexHelper vh, IList<Vector2> points, float width, Color color, Mapper map)
            {
                float half = width * 0.5f;
                for (int i = 0; i < points.Count - 1; i++)
                {
                    Vector2 a = points[i];
                    Vector2 b = points[i + 1];
                    Vector2 d = (b - a).normalized;
                    if (d == Vector2.zero)
                    {
                        continue;
                    }
                    Vector2 n = new Vector2(-d.y, d.x) * half;
                    Fill(vh, new[] { a + n, b + n, b - n, a - n }, color, map);
                }
                for (int i = 1; i < points.Count - 1; i++)
                {
                    Vector2 p = points[i];
                    Vector2 d0 = (p - points[i - 1]).normalized;
                    Vector2 d1 = (points[i + 1] - p).normalized;
                    float turn = d0.x * d1.y - d0.y * d1.x;
                    if (Mathf.Abs(turn) < 0.0001f)
                    {
                        continue;
                    }
                    // 曲がる向きの反対側（外側）の、2 つの区間の縁の端を結ぶ。
                    float side = turn > 0f ? -1f : 1f;
                    Vector2 n0 = new Vector2(-d0.y, d0.x) * (half * side);
                    Vector2 n1 = new Vector2(-d1.y, d1.x) * (half * side);
                    Fill(vh, new[] { p, p + n0, p + n1 }, color, map);
                }
            }

            // 凸の多角形を塗る。
            public static void Fill(VertexHelper vh, IList<Vector2> points, Color color, Mapper map)
            {
                int start = vh.currentVertCount;
                for (int i = 0; i < points.Count; i++)
                {
                    vh.AddVert(map.Map(points[i]), color, Vector2.zero);
                }
                for (int i = 1; i < points.Count - 1; i++)
                {
                    vh.AddTriangle(start, start + i, start + i + 1);
                }
            }
        }
    }

    /// <summary>
    /// タイトルロゴの形（字の高さ 100 の座標。x は右、y は下向き。<see cref="TitleLogo"/> とテストが使う）。
    /// 文字は案のページの F2 ラムダ（線の太さ 13・字間 16・横棒のない Λ・3 本線の E）。E の縦線はワイヤーで、16° 傾いて文字の下まで
    /// 伸び、E の横線はその斜めの線から始まる。ワイヤーの上がる区間（A の横棒の位置を通る区間と、E から E へ渡る区間）は平行。
    /// </summary>
    public static class TitleLogoShape
    {
        public const float Cap = 100f;
        public const float StrokeWidth = 13f;
        /// <summary>ワイヤーの太さ（文字の線と同じ。2026-11-16 のユーザーの訂正）。</summary>
        public const float WireWidth = StrokeWidth;
        public const float FrameWidth = 11f;
        public const float PulseWidth = 2.6f;
        public const float MiterLimit = 6f;
        public const float Track = 16f;
        /// <summary>E の縦線（ワイヤー）の傾き（tan 16°。上ほど右）。</summary>
        public const float Lean = 0.2867f;
        /// <summary>E の縦線が文字の下へ伸びる長さ。</summary>
        public const float Drop = 20f;
        /// <summary>ワイヤーの始まりの x（A の左の外）。</summary>
        public const float StartX = -36f;
        /// <summary>文字の右の端からアンカーの枠の外の角までの間。</summary>
        public const float AnchorGap = 34f;
        /// <summary>アンカーの枠（菱形の中心から角まで）と中の菱形の大きさ。</summary>
        public const float AnchorHalf = 56f;
        public const float InnerHalf = 25f;

        public const string Word = "AIRERAGE";
        public static int LetterCount => Word.Length;

        private static readonly float[] Widths = { 86f, 13f, 70f, 74f, 70f, 86f, 72f, 74f };
        private static readonly float[] Lefts = BuildLefts();
        private static readonly Vector2[] Wire = BuildWire();
        private static readonly float[] WireCumulative = BuildCumulative();

        /// <summary>E の文字の番号（ワイヤーが縦線になる）。</summary>
        public static readonly int[] EIndices = { 3, 7 };

        public static float Left(int letter) => Lefts[letter];
        public static float Width(int letter) => Widths[letter];

        /// <summary>文字の右の端（字間を含まない）。</summary>
        public static float WordRight => Lefts[LetterCount - 1] + Widths[LetterCount - 1];

        /// <summary>アンカーの中心（文字の高さの真ん中）。</summary>
        public static Vector2 AnchorCenter => Wire[Wire.Length - 1];

        /// <summary>ワイヤーの折れ線の点の数と、k 番目の点（0 が始まり、最後がアンカーの中心）。</summary>
        public static int WirePointCount => Wire.Length;
        public static Vector2 WirePoint(int k) => Wire[k];

        /// <summary>ワイヤーの長さ（始まりからアンカーの中心まで）と、k 番目の点までの長さ。</summary>
        public static float WireTotal => WireCumulative[WireCumulative.Length - 1];
        public static float WireLengthTo(int k) => WireCumulative[k];

        /// <summary>描く範囲（線の太さ・尖った角を含む）。</summary>
        public static Rect Bounds
        {
            get
            {
                float outer = AnchorHalf + FrameWidth * 0.5f * 1.4143f;
                float xMin = StartX - WireWidth;
                float xMax = AnchorCenter.x + outer + 2f;
                float yMin = Mathf.Min(-12f, AnchorCenter.y - outer) - 2f;
                float yMax = Mathf.Max(Cap - StrokeWidth * 0.5f + Drop + WireWidth * 1.1f, AnchorCenter.y + outer) + 2f;
                return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            }
        }

        /// <summary>E の縦線（ワイヤー）の、高さ y での x（e は E の文字の番号）。</summary>
        public static float SpineX(int e, float y)
        {
            float bottom = Cap - StrokeWidth * 0.5f;
            return Lefts[e] + WireWidth * 0.5f + Lean * (bottom - y);
        }

        /// <summary>文字 i の線（中心線の折れ線）。</summary>
        public static IEnumerable<Vector2[]> LetterLines(int i)
        {
            float k = StrokeWidth * 0.5f;
            float l = Lefts[i] + k;
            float r = Lefts[i] + Widths[i] - k;
            float top = k;
            float bottom = Cap - k;
            float mid = Cap * 0.5f;
            float c = (l + r) * 0.5f;
            // 線の端はまっすぐ切る。A と R の脚は LetterQuads で（上と下を水平に切る）。重なると、出てくる途中の半透明で濃く見えるので、
            // 線どうしは重ねない。
            switch (Word[i])
            {
                case 'I':
                    yield return new[] { new Vector2(l, 0f), new Vector2(l, Cap) };
                    break;
                case 'R':
                    yield return new[] { new Vector2(l, Cap), new Vector2(l, top), new Vector2(r, top), new Vector2(r, mid), new Vector2(l + k, mid) };
                    break;
                case 'G':
                    yield return new[]
                    {
                        new Vector2(r + k, top), new Vector2(l, top), new Vector2(l, bottom), new Vector2(r, bottom), new Vector2(r, mid), new Vector2(c + 4f, mid),
                    };
                    break;
            }
        }

        /// <summary>A の頂の平らな所の幅の半分（頂は字の上の線で平らに切る）。</summary>
        public const float ApexHalf = 4.4f;

        // 斜めの線を上と下で水平に切った形の、横の太さ（線の太さ StrokeWidth を斜めに測った幅）。slope は高さ 1 あたりの横のずれ。
        private static float SlantedThickness(float slope) => StrokeWidth * Mathf.Sqrt(1f + slope * slope);

        /// <summary>
        /// 文字 i の塗る形（凸の多角形）: A の左右の半分（頂は字の上の線で平らに、脚は下の線で水平に切る）、R の脚（上は横線の下、下は字の
        /// 下の線で水平に切り、右下の角を字の右の端に合わせる）、E の横線（左の端はワイヤーの斜めの線に沿う）。
        /// </summary>
        public static IEnumerable<Vector2[]> LetterQuads(int i)
        {
            float k = StrokeWidth * 0.5f;
            float x0 = Lefts[i];
            float w = Widths[i];
            if (Word[i] == 'A')
            {
                float c = x0 + w * 0.5f;
                float slope = (c - ApexHalf - x0) / Cap;
                float thick = SlantedThickness(slope);
                // 内側の線が真ん中の線と交わる高さ（脚の内側の股）。
                float crotch = Cap - (c - x0 - thick) / slope;
                yield return new[]
                {
                    new Vector2(c - ApexHalf, 0f), new Vector2(c, 0f), new Vector2(c, crotch), new Vector2(x0 + thick, Cap), new Vector2(x0, Cap),
                };
                yield return new[]
                {
                    new Vector2(c, 0f), new Vector2(c + ApexHalf, 0f), new Vector2(x0 + w, Cap), new Vector2(x0 + w - thick, Cap), new Vector2(c, crotch),
                };
                yield break;
            }
            if (Word[i] == 'R')
            {
                float l = x0 + k;
                float r = x0 + w - k;
                float top = Cap * 0.5f + k;
                float start = l + (r - l) * 0.42f;
                // 下の端の中心を、右下の角が字の右の端にそろうように決める（傾きで太さが変わるので数回くり返して合わせる）。
                float end = x0 + w - k;
                for (int n = 0; n < 6; n++)
                {
                    end = x0 + w - SlantedThickness((end - start) / (Cap - top)) * 0.5f;
                }
                float half = SlantedThickness((end - start) / (Cap - top)) * 0.5f;
                yield return new[]
                {
                    new Vector2(start - half, top), new Vector2(start + half, top), new Vector2(end + half, Cap), new Vector2(end - half, Cap),
                };
                yield break;
            }
            if (Word[i] != 'E')
            {
                yield break;
            }
            float right = Lefts[i] + Widths[i];
            float[] centers = { k, Cap * 0.5f, Cap - k };
            for (int b = 0; b < centers.Length; b++)
            {
                float y0 = centers[b] - k;
                float y1 = centers[b] + k;
                float barRight = b == 1 ? right - 8f : right;
                yield return new[]
                {
                    new Vector2(SpineX(i, y0), y0), new Vector2(barRight, y0), new Vector2(barRight, y1), new Vector2(SpineX(i, y1), y1),
                };
            }
        }

        /// <summary>菱形（中心 center、中心から角まで half。上から時計回り）。</summary>
        public static Vector2[] Diamond(Vector2 center, float half)
        {
            return new[]
            {
                center + new Vector2(0f, -half), center + new Vector2(half, 0f), center + new Vector2(0f, half), center + new Vector2(-half, 0f),
            };
        }

        /// <summary>ワイヤーの始まりから length までの折れ線（points に入れる）。</summary>
        public static void WireUpTo(float length, List<Vector2> points)
        {
            points.Clear();
            points.Add(Wire[0]);
            for (int k = 1; k < Wire.Length; k++)
            {
                if (length >= WireCumulative[k])
                {
                    points.Add(Wire[k]);
                    continue;
                }
                float segment = WireCumulative[k] - WireCumulative[k - 1];
                float p = segment > 0f ? (length - WireCumulative[k - 1]) / segment : 1f;
                if (p > 0.0001f)
                {
                    points.Add(Vector2.Lerp(Wire[k - 1], Wire[k], p));
                }
                return;
            }
        }

        private static float[] BuildLefts()
        {
            var lefts = new float[Widths.Length];
            float x = 0f;
            for (int i = 0; i < Widths.Length; i++)
            {
                lefts[i] = x;
                x += Widths[i] + Track;
            }
            return lefts;
        }

        // ワイヤーの折れ線: 始まり → 1 つ目の E の左上 → その下 → 2 つ目の E の左上 → その下 → 横へ → アンカーの中心。最後は、2 つ目の E の
        // 下で曲がった後に水平に右へ伸び、アンカーの左下の辺に垂直に入れる所（中心から左下 45° の線の上）でもう一度曲がって、辺の真ん中から
        // 中心へつながる（案の I2 のアイコンと同じ入り方。2026-11-16 のユーザーの訂正）。
        private static Vector2[] BuildWire()
        {
            float top = StrokeWidth * 0.5f;
            float below = Cap - StrokeWidth * 0.5f + Drop;
            int e1 = 3;
            int e2 = 7;
            var p1 = new Vector2(SpineX(e1, top), top);
            var p2 = new Vector2(SpineX(e1, below), below);
            var p3 = new Vector2(SpineX(e2, top), top);
            var p4 = new Vector2(SpineX(e2, below), below);
            float rise = (p2.y - p3.y) / (p3.x - p2.x);
            var p0 = new Vector2(StartX, p1.y + rise * (p1.x - StartX));
            float mid = Cap * 0.5f;
            float wordRight = Lefts[Widths.Length - 1] + Widths[Widths.Length - 1];
            float outer = AnchorHalf + FrameWidth * 0.5f * 1.4143f;
            var p6 = new Vector2(wordRight + AnchorGap + outer, mid);
            var p5 = new Vector2(p6.x - (p4.y - mid), p4.y);
            return new[] { p0, p1, p2, p3, p4, p5, p6 };
        }

        private static float[] BuildCumulative()
        {
            var sums = new float[Wire.Length];
            for (int k = 1; k < Wire.Length; k++)
            {
                sums[k] = sums[k - 1] + Vector2.Distance(Wire[k - 1], Wire[k]);
            }
            return sums;
        }
    }

    /// <summary>
    /// タイトルロゴの演出の時間の流れ（秒）。ワイヤーは角（キーフレーム）ごとにイーズアウトし、直線では速く、角で急に止まる
    /// （ユーザーの要望、2026-11-16）。
    /// </summary>
    public static class TitleLogoTimeline
    {
        public const float LetterStagger = 0.045f;
        public const float LetterSeconds = 0.42f;
        public const float LetterSlide = 22f;
        public const float FrameDelay = 0.28f;
        public const float FrameSeconds = 0.4f;
        public const float WireStart = 0.4f;
        /// <summary>区間の時間 = 基本 + 長さ × 係数（長い区間ほど少し長いが、速さは長い区間ほど速い）。</summary>
        public const float SegmentBase = 0.09f;
        public const float SegmentPerUnit = 0.00042f;
        /// <summary>角で止まっている時間。</summary>
        public const float CornerHold = 0.035f;
        public const float PulseSeconds = 0.3f;
        public const float PopSeconds = 0.34f;
        public const float RingSeconds = 0.46f;

        private static readonly float[] Starts = BuildStarts();

        /// <summary>曲がる角の数（ワイヤーの点のうち始まりとアンカーの中心を除く）。</summary>
        public static int CornerCount => TitleLogoShape.WirePointCount - 2;

        /// <summary>区間 k（k 番目の点から k+1 番目の点まで）の始まりの時刻と長さ。</summary>
        public static float SegmentStart(int k) => Starts[k];
        public static float SegmentSeconds(int k) =>
            SegmentBase + SegmentPerUnit * (TitleLogoShape.WireLengthTo(k + 1) - TitleLogoShape.WireLengthTo(k));

        /// <summary>k 番目の点に着く時刻（1 は最初の角、最後はアンカーにつながる時刻）。</summary>
        public static float ArriveAt(int point) => Starts[point - 1] + SegmentSeconds(point - 1);

        /// <summary>アンカーにつながる時刻。</summary>
        public static float ConnectAt => ArriveAt(TitleLogoShape.WirePointCount - 1);

        /// <summary>演出が終わる時刻。</summary>
        public static float Duration => ConnectAt + Mathf.Max(PopSeconds, RingSeconds);

        /// <summary>時刻 t に描くワイヤーの長さ。</summary>
        public static float WireLength(float t)
        {
            int segments = TitleLogoShape.WirePointCount - 1;
            if (t >= ConnectAt)
            {
                return TitleLogoShape.WireTotal;
            }
            for (int k = segments - 1; k >= 0; k--)
            {
                if (t < Starts[k])
                {
                    continue;
                }
                float p = Mathf.Clamp01((t - Starts[k]) / SegmentSeconds(k));
                float from = TitleLogoShape.WireLengthTo(k);
                float to = TitleLogoShape.WireLengthTo(k + 1);
                return Mathf.Lerp(from, to, OutQuart(p));
            }
            return 0f;
        }

        /// <summary>文字 i の濃さと、左からのずれ。</summary>
        public static void Letter(float t, int i, out float alpha, out float shift)
        {
            float p = Mathf.Clamp01((t - i * LetterStagger) / LetterSeconds);
            alpha = UiEase.OutCubic(p);
            shift = -LetterSlide * (1f - UiEase.OutCubic(p));
        }

        /// <summary>アンカーの枠の濃さと大きさ（出てくるときと、つながったときのふくらみ）。</summary>
        public static void Frame(float t, out float alpha, out float scale)
        {
            float p = Mathf.Clamp01((t - FrameDelay) / FrameSeconds);
            alpha = UiEase.OutCubic(p);
            scale = Mathf.LerpUnclamped(0.82f, 1f, UiEase.OutBack(p, 1.6f));
            float c = t - ConnectAt;
            if (c >= 0f && c < PopSeconds)
            {
                // つながった瞬間に 6% ふくらんで戻る。
                float q = c / PopSeconds;
                scale *= 1f + 0.06f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(q * 1.6f)) * (1f - q);
            }
        }

        /// <summary>中の菱形の大きさ（つながるまで 0。つながるとはねて 1）。</summary>
        public static float InnerScale(float t)
        {
            float c = t - ConnectAt;
            if (c < 0f)
            {
                return 0f;
            }
            return Mathf.Max(0f, UiEase.OutBack(Mathf.Clamp01(c / PopSeconds), 2.2f));
        }

        /// <summary>角 k（0 が最初の角）の小さな菱形の輪（出ていれば true。中心から角までと濃さ）。</summary>
        public static bool CornerPulse(float t, int k, out float half, out float alpha)
        {
            float c = t - ArriveAt(k + 1);
            if (c < 0f || c >= PulseSeconds)
            {
                half = 0f;
                alpha = 0f;
                return false;
            }
            float p = c / PulseSeconds;
            half = Mathf.Lerp(6f, 21f, UiEase.OutCubic(p));
            alpha = 0.85f * (1f - p);
            return true;
        }

        /// <summary>つながったときに枠から広がる輪（出ていれば true。枠に対する大きさと濃さ）。</summary>
        public static bool ConnectRing(float t, out float scale, out float alpha)
        {
            float c = t - ConnectAt;
            if (c < 0f || c >= RingSeconds)
            {
                scale = 0f;
                alpha = 0f;
                return false;
            }
            float p = c / RingSeconds;
            scale = Mathf.Lerp(1f, 1.38f, UiEase.OutCubic(p));
            alpha = 0.6f * (1f - p);
            return true;
        }

        public static float OutQuart(float p)
        {
            float q = 1f - Mathf.Clamp01(p);
            return 1f - q * q * q * q;
        }

        private static float[] BuildStarts()
        {
            int segments = TitleLogoShape.WirePointCount - 1;
            var starts = new float[segments];
            float t = WireStart;
            for (int k = 0; k < segments; k++)
            {
                starts[k] = t;
                t += SegmentBase + SegmentPerUnit * (TitleLogoShape.WireLengthTo(k + 1) - TitleLogoShape.WireLengthTo(k)) + CornerHold;
            }
            return starts;
        }
    }
}
