using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 文字の演出 W17「シェブロン」の印（D-90。2026-11-08）: 項目の名前の前に、項目の色の「›››」を 3 つ並べる。選んでいる間だけ出し
    /// （0.06 秒待って 0.12 秒で出る）、左から 0.15 秒ずつ遅れて、0.9 秒の周期のうち初めの 40% だけ明るく光る（ほかは 25% の濃さ）。
    /// 見本のページ（文字の演出の案）と同じ動き。字体に頼らず、斜めの太い線 2 本で描く。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ChevronMarks : MaskableGraphic
    {
        /// <summary>光る周期（秒）。</summary>
        public const float Period = 0.9f;
        /// <summary>隣の印が光り始めるまでの秒数。</summary>
        public const float Stagger = 0.15f;

        private const int Count = 3;
        private const float LitShare = 0.4f;
        private const float DimAlpha = 0.25f;
        private const float ShowDelay = 0.06f;
        private const float ShowSeconds = 0.12f;
        // 印 1 つの幅・線の太さ・間隔・高さ（札の高さに対する割合。見本は高さ 58 の札で 6・2.4・8・11 ピクセル）。
        private const float MarkWidth = 6f / 58f;
        private const float Stroke = 2.4f / 58f;
        private const float Step = 8f / 58f;
        private const float MarkHeight = 11f / 58f;
        // 文字の斜体に合わせて、上を右へずらす割合。
        private const float Italic = 0.2f;

        // 画面の切り替えの写しにも引き継ぐため、保存する値にする。
        [SerializeField, HideInInspector] private bool _shown;
        [SerializeField, HideInInspector] private float _shownAt = -10f;

        /// <summary>出しているか（テスト用）。</summary>
        public bool Shown => _shown;

        /// <summary>印の並びの幅（ピクセル。札の高さ plateHeight から決める）。</summary>
        public static float WidthFor(float plateHeight) => (Step * (Count - 1) + MarkWidth + Stroke) * plateHeight;

        /// <summary>出す・隠す（隠すときはすぐ消える）。</summary>
        public void Show(bool shown)
        {
            if (_shown == shown)
            {
                return;
            }
            _shown = shown;
            _shownAt = Time.unscaledTime;
            SetVerticesDirty();
        }

        /// <summary>other の出かたをそのまま写す（画面の切り替えの写し用）。</summary>
        public void CopyState(ChevronMarks other)
        {
            if (other == null || (other._shown == _shown && other._shownAt == _shownAt))
            {
                return;
            }
            _shown = other._shown;
            _shownAt = other._shownAt;
            color = other.color;
            SetVerticesDirty();
        }

        /// <summary>k 番目（左から 0）の印の濃さ（0〜1。t は出してからの秒数）。</summary>
        public static float MarkAlpha(int k, float t)
        {
            float own = t - Stagger * k;
            if (own < 0f)
            {
                return DimAlpha;
            }
            return Mathf.Repeat(own, Period) < Period * LitShare ? 1f : DimAlpha;
        }

        private void Update()
        {
            if (_shown)
            {
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!_shown || !Application.isPlaying)
            {
                return;
            }
            float t = Time.unscaledTime - _shownAt;
            float fade = UiEase.Clamp01((t - ShowDelay) / ShowSeconds);
            if (fade <= 0f)
            {
                return;
            }
            // 札の高さは、この印の枠の高さ（ビルダーが札と同じ高さで置く）。
            Rect r = GetPixelAdjustedRect();
            float plate = r.height;
            float width = MarkWidth * plate;
            float stroke = Stroke * plate;
            float half = MarkHeight * plate * 0.5f;
            float y0 = r.center.y;
            for (int k = 0; k < Count; k++)
            {
                float x0 = r.xMin + Step * plate * k;
                Color c = color;
                c.a *= MarkAlpha(k, t) * fade;
                // 上の線（左上 → 先）と下の線（先 → 左下）。
                AddQuad(vh, new[]
                {
                    P(x0, y0 + half), P(x0 + stroke, y0 + half), P(x0 + width + stroke, y0), P(x0 + width, y0),
                }, c, y0);
                AddQuad(vh, new[]
                {
                    P(x0 + width, y0), P(x0 + width + stroke, y0), P(x0 + stroke, y0 - half), P(x0, y0 - half),
                }, c, y0);
            }
        }

        private static Vector2 P(float x, float y) => new Vector2(x, y);

        private static void AddQuad(VertexHelper vh, Vector2[] p, Color c, float centerY)
        {
            int start = vh.currentVertCount;
            for (int i = 0; i < 4; i++)
            {
                // 斜体: 真ん中より上を右へ、下を左へ。
                var pos = new Vector2(p[i].x + (p[i].y - centerY) * Italic, p[i].y);
                vh.AddVert(pos, c, Vector2.zero);
            }
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
