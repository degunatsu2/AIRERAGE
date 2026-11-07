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
    /// メインメニューのほか、オプション・ステージ選択・アイテムを選ぶ画面・CPU LEVEL の左、ネットワークの画面と待機所の全面、キャラクター
    /// 選択の真ん中にも置く（帯の位置と、左右の端の傾きを変えられる）。
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

        [SerializeField] private Color _paper = new Color(0.95f, 0.96f, 0.975f, 1f);
        [SerializeField] private Color _stripe = new Color(0f, 0f, 0f, 0.05f);
        [Tooltip("帯の左端（地の幅に対する割合。真ん中の高さで）")]
        [SerializeField] private float _bandLeft = 0.5f;
        [Tooltip("細い帯の左端（地の幅に対する割合）")]
        [SerializeField] private float _stripLeft = 0.43f;
        [Tooltip("地の左右の端の傾き（高さに対する、上の端の右へのずれの割合。0 なら四角）")]
        [SerializeField] private float _slant;

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

        /// <summary>帯の左端・細い帯の左端（地の幅に対する割合）と、地の左右の端の傾き（シーンのビルダー用）。</summary>
        public void Configure(float bandLeft, float stripLeft, float slant)
        {
            _bandLeft = bandLeft;
            _stripLeft = stripLeft;
            _slant = slant;
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

            Color paper = _paper;
            paper.a *= tint;
            AddPolygon(vh, r, new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin) }, paper);
            // 左右の端が傾いているときは、地の外へ出た分を切る（下の AddPolygon が端で切る）。

            // 斜線の地模様（札と同じ向きに、上が右へ傾いた細い線）。
            Color stripe = _stripe;
            stripe.a *= tint;
            float rise = r.height * StripeLean;
            for (float x = r.xMin - rise; x < r.xMax; x += StripePeriod)
            {
                AddPolygon(vh, r, new[]
                {
                    new Vector2(x, r.yMin), new Vector2(x + rise, r.yMax), new Vector2(x + rise + StripeWidth, r.yMax), new Vector2(x + StripeWidth, r.yMin),
                }, stripe);
            }

            if (_index < 0)
            {
                return;
            }
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
            Color band = Color.Lerp(accent, new Color(1f, 1f, 1f, accent.a), BandTint);
            float left = r.xMin + r.width * CurrentLeft(now);
            AddPolygon(vh, r, Slanted(r, left, r.xMax + r.width + half, half), band);
            AddPolygon(vh, r, Slanted(r, left, left + EdgeWidth, half), accent);
        }

        // 真ん中の高さで left〜right の、上が右へ half だけずれた平行四辺形。
        private static Vector2[] Slanted(Rect r, float left, float right, float half)
        {
            return new[]
            {
                new Vector2(left - half, r.yMin), new Vector2(left + half, r.yMax), new Vector2(right + half, r.yMax), new Vector2(right - half, r.yMin),
            };
        }

        // 地の左右の端（傾いていることもある）で切ってから、凸の多角形として足す。
        private void AddPolygon(VertexHelper vh, Rect r, Vector2[] points, Color c)
        {
            Polygon.Clear();
            Polygon.AddRange(points);
            float shift = r.height * _slant;
            ClipX(Polygon, r.xMin, shift / Mathf.Max(1f, r.height), r.yMin, true);
            ClipX(Polygon, r.xMax - shift, shift / Mathf.Max(1f, r.height), r.yMin, false);
            if (Polygon.Count < 3)
            {
                return;
            }
            int start = vh.currentVertCount;
            for (int i = 0; i < Polygon.Count; i++)
            {
                vh.AddVert(Polygon[i], c, Vector2.zero);
            }
            for (int i = 1; i + 1 < Polygon.Count; i++)
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
