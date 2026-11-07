using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 放射状の速さの線（D-87 の T4 スカイダイブ。ユーザーの指摘「集中線が少しチープ」で、単純な直線に見えないように作り直した。
    /// 2026-11-07）: 線は内側が細く尖り、中ほどがいちばん明るく、両端は透明に消える。太い薄い線を重ねて光（glow）にする。
    /// 長さ・太さ・明るさはばらつかせ、中心の近くには描かない。Progress（0〜1）で外へ流れながら現れて消える。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SpeedLines : MaskableGraphic
    {
        private const int Count = 64;
        private static readonly Color GlowColor = new Color(0.55f, 0.88f, 1f);

        [SerializeField, HideInInspector] private float _progress = -1f;
        [SerializeField, HideInInspector] private int _seed = 1;
        [SerializeField, HideInInspector] private Vector2 _center;

        /// <summary>進み（0〜1。範囲の外なら描かない）。</summary>
        public float Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                SetVerticesDirty();
            }
        }

        /// <summary>線の中心（この Graphic の中の位置）と、並びを決める種。</summary>
        public void Configure(Vector2 center, int seed)
        {
            _center = center;
            _seed = seed;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_progress < 0f || _progress > 1f)
            {
                return;
            }
            Rect r = GetPixelAdjustedRect();
            float reach = Mathf.Sqrt(r.width * r.width + r.height * r.height) * 0.55f;
            // 明るさの山（0.35 で最も明るく、終わりで消える）と、外への流れ。
            float envelope = _progress < 0.35f ? UiEase.OutCubic(_progress / 0.35f) : 1f - UiEase.InCubic((_progress - 0.35f) / 0.65f);
            float drift = 0.25f * UiEase.OutCubic(_progress);
            var rng = new System.Random(_seed);
            for (int i = 0; i < Count; i++)
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float inner = reach * (0.22f + 0.2f * (float)rng.NextDouble());
                float length = reach * (0.35f + 0.45f * (float)rng.NextDouble());
                float width = 2f + 4f * (float)rng.NextDouble();
                float alpha = (0.35f + 0.6f * (float)rng.NextDouble()) * envelope;
                inner += reach * drift;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 a = _center + dir * inner;
                Vector2 b = _center + dir * (inner + length);
                // 光（太く薄い）と芯（細く明るい）。
                Streak(vh, a, b, width * 3.2f, new Color(GlowColor.r, GlowColor.g, GlowColor.b, alpha * 0.22f));
                Streak(vh, a, b, width, new Color(1f, 1f, 1f, alpha));
            }
        }

        // 内側の端は尖って透明、35% の所で太く明るく、外側の端へ細く透明になる線。
        private static void Streak(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c)
        {
            Vector2 d = b - a;
            Vector2 n = new Vector2(-d.y, d.x).normalized;
            Vector2 mid = Vector2.Lerp(a, b, 0.35f);
            Color clear = c;
            clear.a = 0f;
            int start = vh.currentVertCount;
            vh.AddVert(a, clear, Vector2.zero);
            vh.AddVert(mid + n * width * 0.5f, c, Vector2.zero);
            vh.AddVert(mid - n * width * 0.5f, c, Vector2.zero);
            vh.AddVert(b + n * width * 0.15f, clear, Vector2.zero);
            vh.AddVert(b - n * width * 0.15f, clear, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 1, start + 3, start + 4);
            vh.AddTriangle(start + 1, start + 4, start + 2);
        }
    }
}
