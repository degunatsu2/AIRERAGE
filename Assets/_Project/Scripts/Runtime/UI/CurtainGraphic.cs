using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 試合へ入るときのゲート（D-99 の T12。2026-11-17 のユーザーの決定）: 行き先のステージの色の扉が左右から閉じて真ん中で合わさり、
    /// 合わせ目に名前の札（白い面にステージの色の縁と裏の札）。扉が開くと試合の始まりの演出（シネマスコープの帯つき）が見える。
    /// Close（0〜1。扉が閉じる）・Plate（0〜1。札の出方）・Open（0〜1。扉が開く）で描く。札の文字は ScreenCurtain の Text。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CurtainGraphic : MaskableGraphic
    {
        /// <summary>札の大きさ（キャンバスの単位）。</summary>
        public static readonly Vector2 PlateSize = new Vector2(760f, 150f);

        [SerializeField, HideInInspector] private float _close;
        [SerializeField, HideInInspector] private float _plate;
        [SerializeField, HideInInspector] private float _open;
        [SerializeField, HideInInspector] private Color _door = new Color(0.07f, 0.09f, 0.13f);
        [SerializeField, HideInInspector] private Color _accent = new Color(0.35f, 0.85f, 1f);

        public float Close => _close;
        public float Open => _open;
        public float Plate => _plate;
        public Color Accent => _accent;

        public void Set(float close, float plate, float open)
        {
            _close = Mathf.Clamp01(close);
            _plate = Mathf.Clamp01(plate);
            _open = Mathf.Clamp01(open);
            SetVerticesDirty();
        }

        /// <summary>扉の色（行き先のステージの色）。扉は暗く沈めた色、縁はステージの色。</summary>
        public void SetStageColor(Color accent)
        {
            _accent = new Color(accent.r, accent.g, accent.b, 1f);
            _door = Color.Lerp(new Color(0.05f, 0.06f, 0.09f), _accent, 0.22f);
            SetVerticesDirty();
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
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
            if (_close <= 0f)
            {
                return;
            }
            Rect r = GetPixelAdjustedRect();
            float half = r.width * 0.5f + 40f;
            // 扉の合わせ目の位置（閉じる: 外から真ん中へ減速して。開く: 真ん中から外へ加速して）。
            float shut = UiEase.OutCubic(_close) * (1f - UiEase.InOutCubic(_open));
            float leftEdge = r.xMin - 40f + half * shut;
            float rightEdge = r.xMax + 40f - half * shut;
            float top = r.yMax + 20f;
            float bottom = r.yMin - 20f;
            Door(vh, r.xMin - 40f, leftEdge, top, bottom, 1f);
            Door(vh, rightEdge, r.xMax + 40f, top, bottom, -1f);
            if (_plate > 0f)
            {
                DrawPlate(vh, r.center);
            }
        }

        // 扉 1 枚（x0〜x1）。face は合わせ目の向き（左の扉は +1、右の扉は -1）。合わせ目にステージの色の帯と白い細い線、少し内側に
        // もう 1 本のステージの色の線、横に 2 本の薄いレール。
        private void Door(VertexHelper vh, float x0, float x1, float top, float bottom, float face)
        {
            if (x1 <= x0)
            {
                return;
            }
            Rect(vh, x0, x1, top, bottom, _door);
            float seam = face > 0f ? x1 : x0;
            Color accent = _accent;
            Color rail = _accent;
            rail.a = 0.35f;
            float height = top - bottom;
            Rect(vh, Mathf.Min(seam, seam - face * 16f), Mathf.Max(seam, seam - face * 16f), top, bottom, accent);
            Rect(vh, Mathf.Min(seam - face * 16f, seam - face * 19f), Mathf.Max(seam - face * 16f, seam - face * 19f), top, bottom, Color.white);
            Rect(vh, Mathf.Min(seam - face * 42f, seam - face * 48f), Mathf.Max(seam - face * 42f, seam - face * 48f), top, bottom, accent);
            float inner = face > 0f ? Mathf.Max(x0, seam - 420f) : seam;
            float outer = face > 0f ? seam : Mathf.Min(x1, seam + 420f);
            Rect(vh, inner, outer, bottom + height * 0.3f + 3f, bottom + height * 0.3f, rail);
            Rect(vh, inner, outer, bottom + height * 0.7f + 3f, bottom + height * 0.7f, rail);
        }

        // 合わせ目の名前の札: ステージの色の裏の札、白い面、ステージの色の縁。弾んで大きくなり、開くときに縮んで消える。
        private void DrawPlate(VertexHelper vh, Vector2 center)
        {
            float pop = UiEase.OutBack(_plate);
            Vector2 size = PlateSize * pop;
            float lean = size.y * 0.3f;
            Slanted(vh, center + new Vector2(10f, -8f), size, lean, _accent);
            Slanted(vh, center, size + new Vector2(8f, 8f), lean, _accent);
            Slanted(vh, center, size, lean, Color.white);
        }

        private static void Slanted(VertexHelper vh, Vector2 center, Vector2 size, float lean, Color color)
        {
            float hw = size.x * 0.5f;
            float hh = size.y * 0.5f;
            Quad(vh, center + new Vector2(-hw + lean, hh), center + new Vector2(hw + lean, hh), center + new Vector2(hw - lean, -hh),
                center + new Vector2(-hw - lean, -hh), color);
        }

        private static void Rect(VertexHelper vh, float x0, float x1, float top, float bottom, Color color)
        {
            Quad(vh, new Vector2(x0, top), new Vector2(x1, top), new Vector2(x1, bottom), new Vector2(x0, bottom), color);
        }

        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
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
