using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 斜めの札（SlantedPlate）と同じ形の中の一部を塗る板（キャラクター選択の演出。D-89）。札の左の辺に平行な 2 本の線（札の幅の From〜To。
    /// 0 が左の辺、1 が右の辺）の間を、左の色 Left から右の色 Right へのグラデーションで塗る。Soft なら真ん中がいちばん濃く、両端が透明な
    /// 光の帯にする（光の掃き）。色の流れ込み（C2）、衝撃波で一瞬光る（C14）、光の掃き（C19）に使う。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SlantedFill : MaskableGraphic
    {
        [Tooltip("傾き（SlantedPlate と同じ。高さに対する、上の辺の右へのずれの割合）")]
        [SerializeField] private float _slant = 0.1f;
        [SerializeField] private float _from;
        [SerializeField] private float _to;
        [SerializeField] private Color _left = Color.white;
        [SerializeField] private Color _right = Color.white;
        [SerializeField] private bool _soft;

        /// <summary>塗る範囲の左（札の幅の割合。範囲の外は切る）。</summary>
        public float From => _from;

        /// <summary>塗る範囲の右。</summary>
        public float To => _to;

        /// <summary>傾きと、光の帯にするかを設定する。</summary>
        public void Configure(float slant, bool soft)
        {
            _slant = slant;
            _soft = soft;
            raycastTarget = false;
            SetVerticesDirty();
        }

        /// <summary>塗る範囲と色を変える（変わったときだけ描き直す）。</summary>
        public void Set(float from, float to, Color left, Color right)
        {
            if (Mathf.Approximately(from, _from) && Mathf.Approximately(to, _to) && left == _left && right == _right)
            {
                return;
            }
            _from = from;
            _to = to;
            _left = left;
            _right = right;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float from = Mathf.Clamp01(_from);
            float to = Mathf.Clamp01(_to);
            if (to <= from || (_left.a <= 0f && _right.a <= 0f))
            {
                return;
            }
            Rect r = GetPixelAdjustedRect();
            float shift = r.height * _slant;
            float width = r.width - shift;
            if (!_soft)
            {
                Quad(vh, r, shift, width, from, to, ColorAt(from), ColorAt(to));
                return;
            }
            // 光の帯: 真ん中（元の範囲の真ん中。切った後でも同じ所）がいちばん濃い。
            float mid = (_from + _to) * 0.5f;
            Color peak = Color.Lerp(_left, _right, 0.5f);
            Color clearLeft = peak;
            clearLeft.a = 0f;
            float m = Mathf.Clamp(mid, from, to);
            if (m > from)
            {
                Quad(vh, r, shift, width, from, m, Fade(clearLeft, peak, from), Fade(clearLeft, peak, m));
            }
            if (to > m)
            {
                Quad(vh, r, shift, width, m, to, FadeRight(peak, clearLeft, m), FadeRight(peak, clearLeft, to));
            }
        }

        private Color ColorAt(float u) => Color.Lerp(_left, _right, Mathf.InverseLerp(_from, _to, u));

        // 帯の左半分の、u の所の色（左の端で透明、真ん中で濃い）。
        private Color Fade(Color clear, Color peak, float u)
        {
            float mid = (_from + _to) * 0.5f;
            return Color.Lerp(clear, peak, Mathf.InverseLerp(_from, mid, u));
        }

        // 帯の右半分の、u の所の色（真ん中で濃い、右の端で透明）。
        private Color FadeRight(Color peak, Color clear, float u)
        {
            float mid = (_from + _to) * 0.5f;
            return Color.Lerp(peak, clear, Mathf.InverseLerp(mid, _to, u));
        }

        private static void Quad(VertexHelper vh, Rect r, float shift, float width, float u0, float u1, Color c0, Color c1)
        {
            var bl = new Vector2(r.xMin + width * u0, r.yMin);
            var br = new Vector2(r.xMin + width * u1, r.yMin);
            var tr = new Vector2(r.xMin + shift + width * u1, r.yMax);
            var tl = new Vector2(r.xMin + shift + width * u0, r.yMax);
            int start = vh.currentVertCount;
            vh.AddVert(bl, c0, Vector2.zero);
            vh.AddVert(br, c1, Vector2.zero);
            vh.AddVert(tr, c1, Vector2.zero);
            vh.AddVert(tl, c0, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
