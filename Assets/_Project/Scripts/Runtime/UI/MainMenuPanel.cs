using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メインメニューの左の背景（D-91。ユーザーが選んだ M2「項目の色の帯」を白地に。2026-11-08）: 不透明な白っぽい地に、うすい斜線の地模様、
    /// 選んでいる項目の色の大きな斜めの帯（札と同じ向きに 18° 傾く）と、その左の細い帯。項目を変えると、帯の色が 0.25 秒で変わり、帯が
    /// 項目ごとに少し右へずれる（0.35 秒）。見本のページ（メインメニューの背景の案）と同じ割合で描く。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MainMenuPanel : MaskableGraphic
    {
        /// <summary>帯の色が変わる秒数。</summary>
        public const float ColorSeconds = 0.25f;
        /// <summary>帯が動く秒数。</summary>
        public const float MoveSeconds = 0.35f;

        // 帯の傾き（tan 18°）、帯の左端（地の幅に対する割合。項目ごとに少し右へ）、細い帯の位置・幅・濃さ。
        private const float Skew = 0.3249f;
        private const float BandLeft = 0.5f;
        private const float BandStep = 0.02f;
        private const float StripLeft = 0.43f;
        private const float StripWidth = 0.04f;
        private const float StripAlpha = 0.55f;
        // 斜線の地模様（見本は 115° の向きの 14 ピクセルごとの 2 ピクセルの線。画面の大きさに合わせて広げる）。
        private const float StripePeriod = 24f;
        private const float StripeWidth = 3f;
        private const float StripeLean = 0.4663f;

        [SerializeField] private Color _paper = new Color(0.95f, 0.96f, 0.975f, 1f);
        [SerializeField] private Color _stripe = new Color(0f, 0f, 0f, 0.05f);

        // 帯の今の状態（画面の切り替えの写しにも引き継ぐため、保存する値にする）。
        [SerializeField, HideInInspector] private int _index = -1;
        [SerializeField, HideInInspector] private Color _fromColor = Color.clear;
        [SerializeField, HideInInspector] private Color _toColor = Color.clear;
        [SerializeField, HideInInspector] private float _colorAt = -10f;
        [SerializeField, HideInInspector] private float _fromLeft = BandLeft;
        [SerializeField, HideInInspector] private float _toLeft = BandLeft;
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

        /// <summary>index 番目の項目を、色 accent で選んだ。</summary>
        public void Select(int index, Color accent)
        {
            float now = Time.unscaledTime;
            accent.a = 1f;
            bool first = _index < 0 || !Application.isPlaying;
            _fromColor = first ? accent : CurrentColor(now);
            _toColor = accent;
            _colorAt = first ? -10f : now;
            float left = BandLeft + BandStep * Mathf.Max(0, index);
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
            Color band = CurrentColor(now);
            band.a *= tint;
            float half = r.height * 0.5f * Skew;
            // 細い帯。
            Color strip = band;
            strip.a *= StripAlpha;
            float stripLeft = r.xMin + r.width * StripLeft;
            float stripRight = stripLeft + r.width * StripWidth;
            AddPolygon(vh, r, Slanted(r, stripLeft, stripRight, half), strip);
            // 大きな帯（右は地の外まで）。
            float left = r.xMin + r.width * CurrentLeft(now);
            AddPolygon(vh, r, Slanted(r, left, r.xMax + r.width + half, half), band);
        }

        // 真ん中の高さで left〜right の、上が右へ half だけずれた平行四辺形。
        private static Vector2[] Slanted(Rect r, float left, float right, float half)
        {
            return new[]
            {
                new Vector2(left - half, r.yMin), new Vector2(left + half, r.yMax), new Vector2(right + half, r.yMax), new Vector2(right - half, r.yMin),
            };
        }

        // 地の左右の端で切ってから、凸の多角形として足す。
        private static void AddPolygon(VertexHelper vh, Rect r, Vector2[] points, Color c)
        {
            Polygon.Clear();
            Polygon.AddRange(points);
            ClipX(Polygon, r.xMin, true);
            ClipX(Polygon, r.xMax, false);
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

        // 縦の線 x = edge の片側（keepRight なら右、でなければ左）だけを残す（Sutherland–Hodgman）。
        private static void ClipX(List<Vector2> polygon, float edge, bool keepRight)
        {
            Clipped.Clear();
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                bool aIn = keepRight ? a.x >= edge : a.x <= edge;
                bool bIn = keepRight ? b.x >= edge : b.x <= edge;
                if (aIn)
                {
                    Clipped.Add(a);
                }
                if (aIn != bIn)
                {
                    float t = (edge - a.x) / (b.x - a.x);
                    Clipped.Add(new Vector2(edge, Mathf.Lerp(a.y, b.y, t)));
                }
            }
            polygon.Clear();
            polygon.AddRange(Clipped);
        }
    }
}
