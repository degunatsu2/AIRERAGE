using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 白い背景の上の見出しの前の印（D-92 の T7 の印だけ。2026-11-09 のユーザーの決定。細い線は使わない）: 大きな見出しとロゴの前に、画面の
    /// 色の菱形と、右下にずらした淡い色の影。小見出しの前に、画面の色の「//」。印は文字の左の余白に置き、文字の位置は動かさない（ほかの
    /// 札の縁と見出しの 1 文字目をそろえたままにする）。見出しの文字の子に置き、親と同じ大きさに広げる。
    /// 色は、親の見出しに縁取り（TitleStroke）があればその画面の色、なければこの印の色（AccentTint で画面の色に塗る）。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TitleMark : MaskableGraphic
    {
        /// <summary>印の形。</summary>
        public enum Shape
        {
            /// <summary>菱形と淡い色の影（大きな見出しとロゴ）。</summary>
            Diamond,
            /// <summary>「//」（小見出し）。</summary>
            Slashes,
        }

        // 大きさは文字の大きさに対する割合（見本のページと同じ: 菱形は 0.32 文字、間は 0.35 文字、影は 0.2 文字ずらす）。
        private const float DiamondSize = 0.32f;
        private const float DiamondGap = 0.35f;
        private const float DiamondShadow = 0.2f;
        private const float SlashHeight = 0.72f;
        private const float SlashWidth = 0.13f;
        private const float SlashLean = 0.32f;
        private const float SlashSpacing = 0.24f;
        private const float SlashGap = 0.3f;

        private static TextGenerator _generator;

        [SerializeField] private Shape _shape;
        [SerializeField] private Text _title;

        /// <summary>印の形（テスト用）。</summary>
        public Shape Kind => _shape;

        /// <summary>見出しの文字。</summary>
        public Text Title => _title;

        /// <summary>今の印の色（親の縁取りがあればその画面の色）。</summary>
        public Color Accent
        {
            get
            {
                TitleStroke stroke = _title != null ? _title.GetComponent<TitleStroke>() : null;
                if (stroke != null)
                {
                    Color c = stroke.Accent;
                    c.a = color.a;
                    return c;
                }
                return color;
            }
        }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Text title, Shape shape)
        {
            _title = title;
            _shape = shape;
            SetVerticesDirty();
        }

        /// <summary>
        /// 見出しの文字の左端と、文字の上下の真ん中（見出しの文字の座標。この印は親と同じ大きさなので同じ座標）。字の網を作り直して求めるので、
        /// 並べ方（左寄せ・真ん中）と斜体のずれも入る。文字がなければ false。
        /// </summary>
        public bool TryGetTextStart(out float left, out float middle)
        {
            left = 0f;
            middle = 0f;
            if (_title == null || string.IsNullOrEmpty(_title.text) || _title.font == null)
            {
                return false;
            }
            Rect r = _title.rectTransform.rect;
            if (_generator == null)
            {
                _generator = new TextGenerator();
            }
            _generator.Populate(_title.text, _title.GetGenerationSettings(r.size));
            var verts = _generator.verts;
            float unit = 1f / Mathf.Max(0.0001f, _title.pixelsPerUnit);
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            // 1 行目の文字だけ（見出しは 1 行。網は 1 文字 4 頂点）。
            int count = Mathf.Min(verts.Count, 4 * Mathf.Max(1, _generator.characterCountVisible));
            for (int i = 0; i < count; i++)
            {
                Vector3 p = verts[i].position * unit;
                minX = Mathf.Min(minX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            if (minX == float.MaxValue)
            {
                return false;
            }
            left = minX;
            middle = (minY + maxY) * 0.5f;
            return true;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!TryGetTextStart(out float left, out float middle))
            {
                return;
            }
            float em = _title.fontSize;
            Color accent = Accent;
            if (_shape == Shape.Diamond)
            {
                float half = em * DiamondSize * 0.5f;
                var center = new Vector2(left - em * DiamondGap - half, middle);
                Color shadow = TitleStroke.Pastel(accent);
                shadow.a = accent.a;
                float shift = em * DiamondShadow;
                AddDiamond(vh, center + new Vector2(shift, -shift), half, shadow);
                AddDiamond(vh, center, half, accent);
            }
            else
            {
                float h = em * SlashHeight * 0.5f;
                float w = em * SlashWidth;
                float lean = em * SlashLean * 0.5f;
                float right = left - em * SlashGap;
                // 2 本の斜めの棒（上が右へ傾く）。x は真ん中の高さでの棒の左端。右の棒の右端が文字から SlashGap だけ離れる。
                for (int i = 0; i < 2; i++)
                {
                    float x = right - w - (1 - i) * em * SlashSpacing;
                    AddQuad(vh, new Vector2(x - lean, middle - h), new Vector2(x + lean, middle + h), new Vector2(x + lean + w, middle + h),
                        new Vector2(x - lean + w, middle - h), accent);
                }
            }
        }

        private static void AddDiamond(VertexHelper vh, Vector2 c, float half, Color color)
        {
            AddQuad(vh, c + new Vector2(0f, half), c + new Vector2(half, 0f), c + new Vector2(0f, -half), c + new Vector2(-half, 0f), color);
        }

        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(c, color, Vector2.zero);
            vh.AddVert(d, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
