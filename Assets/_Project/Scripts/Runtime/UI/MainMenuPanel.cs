using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メニューの白い背景（D-91。ユーザーが選んだ M2「項目の色の帯」を白地に。2026-11-08）: 不透明な白っぽい地に、うすい斜線の地模様、
    /// 選んでいる項目の色の大きな斜めの帯（札と同じ向きに 18° 傾く）と、その左の細い帯。項目を変えると、帯の色が 0.25 秒で変わり、帯が
    /// 項目ごとに少し右へずれる（0.35 秒）。見本のページ（メインメニューの背景の案）と同じ割合で描く。
    /// 帯は項目の色を白に寄せた淡い色にし、左の端だけ項目の色そのものの線を引く（項目の色の裏の札が帯に溶けて一部しか見えなかったので、
    /// 帯を淡くして裏の札を浮かせた。ユーザーの指摘、2026-11-08）。
    /// 試合の画面との境目（D-92 で端を帯と同じ向きに傾けた。高さの真ん中が元の位置）の模様は D-94 の H19「ぬいめ」の手直し（2026-11-14 の
    /// ユーザーの決定。同じ日に、光る粒と境目の外の 2 列の縫い目をなくした）: 白い背景の内側の縁の近くに、境目と同じ向きの角の丸い短い線の
    /// 縫い目（画面の色）を 1 列。境目はぼかす（くっきりしすぎて浮いて見えないように。内と外へ EdgeFeather の幅で地の色から透明へ移る）。
    /// 縫い目はぼかさない。
    /// メインメニューのほか、オプション・ステージ選択・アイテムを選ぶ画面・CPU LEVEL の左（右の端だけ）、キャラクター選択の真ん中（左右の
    /// 端）、ネットワークの画面と待機所の全面（端なし）に置く。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MainMenuPanel : MaskableGraphic
    {
        /// <summary>帯の色が変わる秒数。</summary>
        public const float ColorSeconds = 0.25f;
        /// <summary>帯が動く秒数。</summary>
        public const float MoveSeconds = 0.35f;

        // 帯の傾き（tan 18°）、項目ごとに帯が右へずれる量（地の幅に対する割合）、細い帯の幅と濃さ、帯の淡さ（白に寄せる割合）と左の端の線の太さ。
        private const float Skew = 0.3249f;
        private const float BandStep = 0.02f;
        private const float StripWidth = 0.04f;
        private const float StripAlpha = 0.55f;
        private const float BandTint = 0.55f;
        private const float EdgeWidth = 9f;
        // 斜線の地模様（見本は 115° の向きの 14 ピクセルごとの 2 ピクセルの線。画面の大きさに合わせて広げる）。
        private const float StripePeriod = 24f;
        private const float StripeWidth = 3f;
        private const float StripeLean = 0.4663f;

        /// <summary>試合の画面との境目の傾き（帯と同じ。高さ 1 に対する横のずれ）。</summary>
        public const float EdgeLean = Skew;
        // 境目の縫い目（ピクセル）: 境目からの距離（外が正。負は白い背景の内側）・太さ・1 本の長さ・間。白い背景の内側の 1 列だけ（境目の外の
        // 2 列はなくした。内側の列は 16 から少し内へ。2026-11-14 のユーザーの要望）。
        private static readonly Stitch[] Stitches =
        {
            new Stitch(-22f, 3.5f, 14f, 9f),
        };
        // 縫い目の角を丸くする半円の分割の数。
        private const int CapSegments = 5;
        // 境目のぼかしの幅（ピクセル。境目を真ん中に、内側の半分は地の色のまま外側へ透明になる）。16・10 では強すぎた（2026-11-16 の
        // ユーザーの指摘。4 に）。
        private const float EdgeFeatherWidth = 4f;
        // 2 列目の縫い目の色を作るとき、画面の色を白に寄せる割合。
        private const float StitchTint = 0.3f;

        private readonly struct Stitch
        {
            public readonly float Offset;
            public readonly float Width;
            public readonly float Length;
            public readonly float Gap;

            public Stitch(float offset, float width, float length, float gap)
            {
                Offset = offset;
                Width = width;
                Length = length;
                Gap = gap;
            }
        }

        /// <summary>境目の外の縫い目の列の数（テスト用）。</summary>
        public static int OuterStitchCount
        {
            get
            {
                int n = 0;
                foreach (Stitch stitch in Stitches)
                {
                    n += stitch.Offset > 0f ? 1 : 0;
                }
                return n;
            }
        }

        /// <summary>白い背景の内側の縫い目の列の数（テスト用）。</summary>
        public static int InnerStitchCount => Stitches.Length - OuterStitchCount;

        /// <summary>境目のぼかしの幅（ピクセル。テスト用）。</summary>
        public static float EdgeFeather => EdgeFeatherWidth;

        /// <summary>
        /// 高さの真ん中で x = edge を通る傾いた境目から、外へ offset（side が 1 なら右、-1 なら左）の所に、高さ y0〜y1 の縫い目の線を
        /// 引くときの、角の丸みの中心の 2 点（線の太さ width。両端の丸みの分だけ内側。境目と同じ向きに並ぶ）。
        /// </summary>
        public static void StitchEnds(float edge, float side, float mid, float offset, float y0, float y1, float width, out Vector2 a, out Vector2 b)
        {
            float half = width * 0.5f;
            float ya = y0 + half;
            float yb = y1 - half;
            a = new Vector2(edge + EdgeLean * (ya - mid) + side * offset, ya);
            b = new Vector2(edge + EdgeLean * (yb - mid) + side * offset, yb);
        }

        /// <summary>試合の画面との境目がどこにあるか。</summary>
        public enum EdgeSides
        {
            /// <summary>画面いっぱい（境目なし）。</summary>
            None,
            /// <summary>右の端だけ（左は画面の端）。</summary>
            Right,
            /// <summary>左右の端（キャラクター選択の真ん中）。</summary>
            Both,
        }

        [SerializeField] private Color _paper = new Color(0.95f, 0.96f, 0.975f, 1f);
        [SerializeField] private Color _stripe = new Color(0f, 0f, 0f, 0.05f);
        [Tooltip("帯の左端（地の幅に対する割合。真ん中の高さで）")]
        [SerializeField] private float _bandLeft = 0.5f;
        [Tooltip("細い帯の左端（地の幅に対する割合）")]
        [SerializeField] private float _stripLeft = 0.43f;
        [Tooltip("試合の画面との境目（傾けて、縫い目と光る粒で飾る端）")]
        [SerializeField] private EdgeSides _edges;

        // 帯の今の状態（画面の切り替えの写しにも引き継ぐため、保存する値にする）。
        [SerializeField, HideInInspector] private int _index = -1;
        [SerializeField, HideInInspector] private Color _fromColor = Color.clear;
        [SerializeField, HideInInspector] private Color _toColor = Color.clear;
        [SerializeField, HideInInspector] private float _colorAt = -10f;
        [SerializeField, HideInInspector] private float _fromLeft = 0.5f;
        [SerializeField, HideInInspector] private float _toLeft = 0.5f;
        [SerializeField, HideInInspector] private float _moveAt = -10f;

        private static readonly List<Vector2> Polygon = new List<Vector2>();
        private static readonly List<Vector2> Clipped = new List<Vector2>();

        /// <summary>今選んでいる項目（テスト用。まだなければ -1）。</summary>
        public int Index => _index;

        /// <summary>帯の今の色（テスト用）。</summary>
        public Color BandColor => CurrentColor(Time.unscaledTime);

        /// <summary>帯の左端の今の位置（地の幅に対する割合。真ん中の高さで。テスト用）。</summary>
        public float BandLeftNow => CurrentLeft(Time.unscaledTime);

        /// <summary>地の色（テスト用）。</summary>
        public Color Paper => _paper;

        /// <summary>境目（テスト用）。</summary>
        public EdgeSides Edges => _edges;

        /// <summary>帯の左端・細い帯の左端（地の幅に対する割合）と、試合の画面との境目（シーンのビルダー用）。</summary>
        public void Configure(float bandLeft, float stripLeft, EdgeSides edges)
        {
            _bandLeft = bandLeft;
            _stripLeft = stripLeft;
            _edges = edges;
            _fromLeft = bandLeft;
            _toLeft = bandLeft;
            SetVerticesDirty();
        }

        /// <summary>帯を色 accent にする（項目のない画面。動かさない）。</summary>
        public void SetAccent(Color accent)
        {
            if (_index == 0 && _toColor == new Color(accent.r, accent.g, accent.b, 1f))
            {
                return;
            }
            Select(0, accent);
        }

        /// <summary>index 番目の項目を、色 accent で選んだ。</summary>
        public void Select(int index, Color accent)
        {
            float now = Time.unscaledTime;
            accent.a = 1f;
            // 初めてのとき・再生していないとき・出していないとき（画面を出す前に色を決める）は、動かさずにすぐその形にする。
            bool first = _index < 0 || !Application.isPlaying || !isActiveAndEnabled;
            _fromColor = first ? accent : CurrentColor(now);
            _toColor = accent;
            _colorAt = first ? -10f : now;
            float left = _bandLeft + BandStep * Mathf.Max(0, index);
            _fromLeft = first ? left : CurrentLeft(now);
            _toLeft = left;
            _moveAt = first ? -10f : now;
            _index = index;
            SetVerticesDirty();
        }

        /// <summary>other の帯の状態をそのまま写す（画面の切り替えの写し用）。</summary>
        public void CopyState(MainMenuPanel other)
        {
            if (other == null)
            {
                return;
            }
            _index = other._index;
            _fromColor = other._fromColor;
            _toColor = other._toColor;
            _colorAt = other._colorAt;
            _fromLeft = other._fromLeft;
            _toLeft = other._toLeft;
            _moveAt = other._moveAt;
            color = other.color;
            SetVerticesDirty();
        }

        private Color CurrentColor(float now)
        {
            float t = (now - _colorAt) / ColorSeconds;
            return t >= 1f ? _toColor : Color.Lerp(_fromColor, _toColor, UiEase.CssEase(t));
        }

        private float CurrentLeft(float now)
        {
            float t = (now - _moveAt) / MoveSeconds;
            return t >= 1f ? _toLeft : Mathf.LerpUnclamped(_fromLeft, _toLeft, UiEase.SnapOut(t));
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now - _colorAt < ColorSeconds + 0.05f || now - _moveAt < MoveSeconds + 0.05f)
            {
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float now = Application.isPlaying ? Time.unscaledTime : 100000f;
            float tint = color.a;
            bool right = _edges != EdgeSides.None;
            bool left = _edges == EdgeSides.Both;
            // 傾いた端は高さの真ん中で元の位置を通り、上は外へ、下は内へずれる。
            float reach = r.height * 0.5f * EdgeLean;
            float xMin = r.xMin - (left ? reach : 0f);
            float xMax = r.xMax + (right ? reach : 0f);

            Color paper = _paper;
            paper.a *= tint;
            AddPolygon(vh, r, new[] { new Vector2(xMin, r.yMin), new Vector2(xMin, r.yMax), new Vector2(xMax, r.yMax), new Vector2(xMax, r.yMin) }, paper);

            // 斜線の地模様（札と同じ向きに、上が右へ傾いた細い線）。
            Color stripe = _stripe;
            stripe.a *= tint;
            float rise = r.height * StripeLean;
            for (float x = xMin - rise; x < xMax; x += StripePeriod)
            {
                AddPolygon(vh, r, new[]
                {
                    new Vector2(x, r.yMin), new Vector2(x + rise, r.yMax), new Vector2(x + rise + StripeWidth, r.yMax), new Vector2(x + StripeWidth, r.yMin),
                }, stripe);
            }

            Color band = paper;
            if (_index >= 0)
            {
                Color accent = CurrentColor(now);
                accent.a *= tint;
                float half = r.height * 0.5f * Skew;
                // 細い帯。
                Color strip = accent;
                strip.a *= StripAlpha;
                float stripLeft = r.xMin + r.width * _stripLeft;
                float stripRight = stripLeft + r.width * StripWidth;
                AddPolygon(vh, r, Slanted(r, stripLeft, stripRight, half), strip);
                // 大きな帯（右は地の外まで）。項目の色を白に寄せた淡い色で、左の端だけ項目の色そのものの線。
                band = Color.Lerp(accent, new Color(1f, 1f, 1f, accent.a), BandTint);
                float bandLeft = r.xMin + r.width * CurrentLeft(now);
                AddPolygon(vh, r, Slanted(r, bandLeft, xMax + r.width + half, half), band);
                AddPolygon(vh, r, Slanted(r, bandLeft, bandLeft + EdgeWidth, half), accent);
            }

            // 試合の画面との境目: ぼかしと縫い目（右の端の 1 列目とぼかしは帯の色、左の端は地の色）。
            if (right || left)
            {
                Color accent = _index >= 0 ? CurrentColor(now) : _paper;
                accent.a = tint;
                if (right)
                {
                    AddEdge(vh, r, r.xMax, 1f, _index >= 0 ? band : paper, accent, _index >= 0);
                }
                if (left)
                {
                    AddEdge(vh, r, r.xMin, -1f, paper, accent, _index >= 0);
                }
            }
        }

        // 高さの真ん中で x = edge を通る傾いた端（side が 1 なら外は右、-1 なら左）に、ぼかしと縫い目を描く。c はその端の地の色、accent は
        // 画面の色（hasAccent でなければ地の色。内側の縫い目は斜線の地模様の色を濃くした色にする）。
        private void AddEdge(VertexHelper vh, Rect r, float edge, float side, Color c, Color accent, bool hasAccent)
        {
            float mid = r.center.y;
            // ぼかし: 地は境目より EdgeFeatherWidth の半分だけ内側で切ってあるので、そこから境目の外の半分まで、地の色から透明へ。
            float half = EdgeFeatherWidth * 0.5f;
            Color clear = c;
            clear.a = 0f;
            float lowInner = edge + EdgeLean * (r.yMin - mid) - side * half;
            float highInner = edge + EdgeLean * (r.yMax - mid) - side * half;
            AddGradientQuad(vh,
                new Vector2(lowInner, r.yMin), new Vector2(highInner, r.yMax),
                new Vector2(highInner + side * EdgeFeatherWidth, r.yMax), new Vector2(lowInner + side * EdgeFeatherWidth, r.yMin), c, clear);

            Color white = new Color(1f, 1f, 1f, accent.a);
            Color pastel = Color.Lerp(accent, white, StitchTint);
            Color inner = hasAccent ? accent : new Color(_stripe.r, _stripe.g, _stripe.b, Mathf.Min(1f, _stripe.a * 8f) * accent.a);
            for (int i = 0; i < Stitches.Length; i++)
            {
                // 内側の列は画面の色。外の列（今は使っていない）は、1 列目がその端の地の色、2 列目からは画面の色を白に寄せた色。
                Stitch stitch = Stitches[i];
                Color color = stitch.Offset < 0f ? inner : i == 0 ? c : hasAccent ? pastel : c;
                AddStitches(vh, r, edge, side, mid, stitch, color);
            }
        }

        // a〜b（内側の 2 辺）は色 inner、c〜d（外側の 2 辺）は色 outer の四角形。
        private static void AddGradientQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color inner, Color outer)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, inner, Vector2.zero);
            vh.AddVert(b, inner, Vector2.zero);
            vh.AddVert(c, outer, Vector2.zero);
            vh.AddVert(d, outer, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        // 境目から stitch.Offset の所に、境目と同じ向きの角の丸い短い線を並べる（地の上下の端で切る）。
        private static void AddStitches(VertexHelper vh, Rect r, float edge, float side, float mid, Stitch stitch, Color c)
        {
            float period = stitch.Length + stitch.Gap;
            // 境目に沿った向き（上が右へ傾く）。線の長さは高さで測る。
            Vector2 along = new Vector2(EdgeLean, 1f).normalized;
            Vector2 across = new Vector2(along.y, -along.x);
            float half = stitch.Width * 0.5f;
            for (float y0 = r.yMin + stitch.Gap * 0.5f; y0 < r.yMax; y0 += period)
            {
                float y1 = Mathf.Min(y0 + stitch.Length, r.yMax);
                if (y1 - y0 < stitch.Width)
                {
                    continue;
                }
                StitchEnds(edge, side, mid, stitch.Offset, y0, y1, stitch.Width, out Vector2 a, out Vector2 b);
                // 輪郭は b の側の半円（across → along → -across）と a の側の半円（-across → -along → across）。
                Polygon.Clear();
                for (int end = 0; end < 2; end++)
                {
                    Vector2 center = end == 0 ? b : a;
                    float sign = end == 0 ? 1f : -1f;
                    for (int k = 0; k <= CapSegments; k++)
                    {
                        float t = Mathf.PI * k / CapSegments;
                        Polygon.Add(center + (across * Mathf.Cos(t) + along * Mathf.Sin(t)) * (half * sign));
                    }
                }
                AddConvex(vh, Polygon, c);
            }
        }

        // 真ん中の高さで left〜right の、上が右へ half だけずれた平行四辺形。
        private static Vector2[] Slanted(Rect r, float left, float right, float half)
        {
            return new[]
            {
                new Vector2(left - half, r.yMin), new Vector2(left + half, r.yMax), new Vector2(right + half, r.yMax), new Vector2(right - half, r.yMin),
            };
        }

        // 地の左右の端（境目なら傾いた線、画面の端ならまっすぐな線）で切ってから、凸の多角形として足す。
        private void AddPolygon(VertexHelper vh, Rect r, Vector2[] points, Color c)
        {
            Polygon.Clear();
            Polygon.AddRange(points);
            float mid = r.center.y;
            // 境目の端は、ぼかしの半分だけ内側で切る（そこから外へ AddEdge のぼかしでつなぐ）。
            bool leftEdge = _edges == EdgeSides.Both;
            bool rightEdge = _edges != EdgeSides.None;
            ClipX(Polygon, r.xMin + (leftEdge ? EdgeFeatherWidth * 0.5f : 0f), leftEdge ? EdgeLean : 0f, mid, true);
            ClipX(Polygon, r.xMax - (rightEdge ? EdgeFeatherWidth * 0.5f : 0f), rightEdge ? EdgeLean : 0f, mid, false);
            if (Polygon.Count < 3)
            {
                return;
            }
            AddConvex(vh, Polygon, c);
        }

        private static void AddConvex(VertexHelper vh, List<Vector2> points, Color c)
        {
            int start = vh.currentVertCount;
            for (int i = 0; i < points.Count; i++)
            {
                vh.AddVert(points[i], c, Vector2.zero);
            }
            for (int i = 1; i + 1 < points.Count; i++)
            {
                vh.AddTriangle(start, start + i, start + i + 1);
            }
        }

        // 線 x = edge + lean × (y − bottom)（lean が 0 なら縦の線）の片側（keepRight なら右、でなければ左）だけを残す（Sutherland–Hodgman）。
        private static void ClipX(List<Vector2> polygon, float edge, float lean, float bottom, bool keepRight)
        {
            Clipped.Clear();
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                float da = a.x - (edge + lean * (a.y - bottom));
                float db = b.x - (edge + lean * (b.y - bottom));
                bool aIn = keepRight ? da >= 0f : da <= 0f;
                bool bIn = keepRight ? db >= 0f : db <= 0f;
                if (aIn)
                {
                    Clipped.Add(a);
                }
                if (aIn != bIn)
                {
                    float t = da / (da - db);
                    Clipped.Add(Vector2.Lerp(a, b, t));
                }
            }
            polygon.Clear();
            polygon.AddRange(Clipped);
        }
    }
}
