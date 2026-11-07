using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 試合へ入るときの黒い帯（D-87 の T10 シネマスコープ。試合の始まりの演出 D-46 と同じ形）: 上と下の黒い帯が、右上がりの斜めの
    /// 線で合わさって画面を閉じ、白い線が斜めに走って、帯が上下へ少し傾きながら開く。Close・Slash・Open（0〜1）で描く。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CurtainGraphic : MaskableGraphic
    {
        private static readonly Color GlowColor = new Color(0.35f, 0.85f, 1f);

        [SerializeField, HideInInspector] private float _close;
        [SerializeField, HideInInspector] private float _slash;
        [SerializeField, HideInInspector] private float _open;

        public float Close => _close;
        public float Open => _open;

        public void Set(float close, float slash, float open)
        {
            _close = Mathf.Clamp01(close);
            _slash = Mathf.Clamp01(slash);
            _open = Mathf.Clamp01(open);
            SetVerticesDirty();
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_close <= 0f && _slash <= 0f)
            {
                return;
            }
            Rect r = GetPixelAdjustedRect();
            float h = r.height;
            float tilt = h * 0.093f;
            float k = 2f * tilt / Mathf.Max(1f, r.width);
            float cy = r.center.y;
            float left = r.xMin - 120f;
            float right = r.xMax + 120f;
            float LineY(float x) => cy + (x - r.center.x) * k;

            float travel = h * 0.62f;
            float closed = (1f - UiEase.InCubic(_close)) * travel;
            float open = UiEase.OutCubic(_open);
            var topOffset = new Vector2(-70f * open, closed + open * h * 0.7f);
            var bottomOffset = new Vector2(70f * open, -closed - open * h * 0.7f);
            Color black = new Color(0f, 0f, 0f, color.a);

            // 上の帯（下の辺が斜めの線。1 ピクセル重ねて隙間をなくす）と、下の帯。
            Quad(vh, new Vector2(left, r.yMax + 20f) + topOffset, new Vector2(right, r.yMax + 20f) + topOffset,
                new Vector2(right, LineY(right) - 1f) + topOffset, new Vector2(left, LineY(left) - 1f) + topOffset, black, black);
            Quad(vh, new Vector2(left, LineY(left) + 1f) + bottomOffset, new Vector2(right, LineY(right) + 1f) + bottomOffset,
                new Vector2(right, r.yMin - 20f) + bottomOffset, new Vector2(left, r.yMin - 20f) + bottomOffset, black, black);

            // 斜めの白い線（真ん中から両側へ伸び、開くと薄れる）と、その周りの水色の光。
            if (_slash > 0f && _open < 1f)
            {
                float half = (r.width * 0.6f + 120f) * UiEase.OutCubic(_slash);
                var dir = new Vector2(1f, k).normalized;
                Vector2 n = new Vector2(-dir.y, dir.x);
                Vector2 c = r.center;
                float fade = 1f - open;
                Color glow = GlowColor;
                glow.a = 0.35f * fade;
                Color clear = glow;
                clear.a = 0f;
                Line(vh, c - dir * half, c + dir * half, n, 18f, glow, clear);
                Color core = new Color(1f, 1f, 1f, fade);
                Line(vh, c - dir * half, c + dir * half, n, 4f, core, core);
            }
        }

        // 太さ width の線（真ん中が center の色、外の縁が edge の色）。
        private static void Line(VertexHelper vh, Vector2 a, Vector2 b, Vector2 n, float width, Color center, Color edge)
        {
            Vector2 o = n * width * 0.5f;
            Quad(vh, a + o, b + o, b, a, edge, center);
            Quad(vh, a, b, b - o, a - o, center, edge);
        }

        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color top, Color bottom)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, top, Vector2.zero);
            vh.AddVert(b, top, Vector2.zero);
            vh.AddVert(c, bottom, Vector2.zero);
            vh.AddVert(d, bottom, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
