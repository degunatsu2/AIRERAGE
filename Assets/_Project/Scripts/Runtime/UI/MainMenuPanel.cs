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
    /// 試合の画面との境目（D-92。ユーザーが選んだ E2 + E3。2026-11-09）: 境目の端を帯と同じ向きに傾け（高さの真ん中が元の位置）、その外に
    /// 細くなっていく帯を 3 本、さらにその外に菱形のドットを斜めの格子に並べ、外へ行くほど小さくして試合の画面へほどけさせる。ドットの
    /// 大きさは高さでもゆるやかに変わり、間隔に緩急が付く。帯とドットの色は、その端の地の色（右の端は帯の淡い色、左の端は白）。
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
        // 境目の外の段々の帯（端からの距離・幅・濃さ。ピクセル）。
        private static readonly Vector3[] EdgeStrips = { new Vector3(14f, 30f, 0.85f), new Vector3(56f, 18f, 0.62f), new Vector3(88f, 9f, 0.42f) };
        // 菱形のドット: 端からの始まり、横の間隔（縦は半分ずつずらした斜めの格子）、列の数、濃さ。
        private const float DotStart = 114f;
        private const float DotStep = 20f;
        private const int DotColumns = 13;
        private const float DotAlpha = 0.8f;
        // 高さでの大きさの緩急（2 つの波を重ねて、規則正しすぎない変化にする。周期はピクセル）。
        private const float RhythmLong = 470f;
        private const float RhythmShort = 190f;

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
        [Tooltip("試合の画面との境目（傾けて、段々の帯と菱形のドットでほどけさせる端）")]
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

            // 試合の画面との境目: 段々の帯と、ほどけていく菱形のドット（右の端は帯の色、左の端は地の色）。
            if (right)
            {
                AddEdge(vh, r, r.xMax, 1f, _index >= 0 ? band : paper);
            }
            if (left)
            {
                AddEdge(vh, r, r.xMin, -1f, paper);
            }
        }

        // 高さの真ん中で x = edge を通る傾いた端の外（side が 1 なら右、-1 なら左）に、段々の帯と菱形のドットを描く。
        private static void AddEdge(VertexHelper vh, Rect r, float edge, float side, Color c)
        {
            float mid = r.center.y;
            float half = r.height * 0.5f * EdgeLean;
            foreach (Vector3 s in EdgeStrips)
            {
                float a = edge + side * s.x;
                float b = edge + side * (s.x + s.y);
                float lo = Mathf.Min(a, b);
                float hi = Mathf.Max(a, b);
                Color strip = c;
                strip.a *= s.z;
                AddRaw(vh, new[] { new Vector2(lo - half, r.yMin), new Vector2(lo + half, r.yMax), new Vector2(hi + half, r.yMax), new Vector2(hi - half, r.yMin) }, strip);
            }
            float rowStep = DotStep * 0.5f;
            int row = 0;
            for (float y = r.yMin + rowStep * 0.5f; y < r.yMax; y += rowStep, row++)
            {
                float edgeX = edge + EdgeLean * (y - mid);
                float rhythm = DotRhythm(r.yMax - y);
                for (int col = 0; col < DotColumns; col++)
                {
                    float fade = Mathf.Pow(1f - (float)col / DotColumns, 1.15f);
                    float size = DotStep * 0.5f * fade * (0.35f + 0.65f * rhythm);
                    if (size < 0.7f)
                    {
                        continue;
                    }
                    float x = edgeX + side * (DotStart + col * DotStep + (row % 2 == 1 ? DotStep * 0.5f : 0f));
                    Color dot = c;
                    dot.a *= DotAlpha * (0.6f + 0.4f * fade);
                    AddRaw(vh, new[] { new Vector2(x, y + size), new Vector2(x + size, y), new Vector2(x, y - size), new Vector2(x - size, y) }, dot);
                }
            }
        }

        /// <summary>
        /// 菱形のドットの、高さでの大きさの緩急（0〜1。depth は上の端からのピクセル）。長い波と短い波を重ねて、ところどころ大きく
        /// （詰まって）、ところどころ小さく（まばらに）なる。
        /// </summary>
        public static float DotRhythm(float depth)
        {
            float wave = 0.55f + 0.3f * Mathf.Cos(2f * Mathf.PI * depth / RhythmLong + 0.6f) + 0.2f * Mathf.Cos(2f * Mathf.PI * depth / RhythmShort + 2.1f);
            return Mathf.Clamp01(wave);
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
            ClipX(Polygon, r.xMin, _edges == EdgeSides.Both ? EdgeLean : 0f, mid, true);
            ClipX(Polygon, r.xMax, _edges != EdgeSides.None ? EdgeLean : 0f, mid, false);
            if (Polygon.Count < 3)
            {
                return;
            }
            AddConvex(vh, Polygon, c);
        }

        // 切らずに、凸の多角形として足す（境目の外の飾り）。
        private static void AddRaw(VertexHelper vh, Vector2[] points, Color c)
        {
            Polygon.Clear();
            Polygon.AddRange(points);
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
