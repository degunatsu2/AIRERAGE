using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 画面を横切る風の筋（D-87 の T1 ガストアウト）: 細い光の線が、行き先と逆の向きへ画面を横切る（両端は透明）。Time（秒）で動かす。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WindStreaks : MaskableGraphic
    {
        private const int Count = 18;

        [SerializeField, HideInInspector] private float _time = -1f;
        [SerializeField, HideInInspector] private int _direction = 1;
        [SerializeField, HideInInspector] private int _seed = 1;

        /// <summary>始まってからの秒数（負なら描かない）。</summary>
        public float Elapsed
        {
            get => _time;
            set
            {
                _time = value;
                SetVerticesDirty();
            }
        }

        /// <summary>向き（1: 右から左へ。-1: 左から右へ）と、並びを決める種。</summary>
        public void Configure(int direction, int seed)
        {
            _direction = direction >= 0 ? 1 : -1;
            _seed = seed;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_time < 0f)
            {
                return;
            }
            Rect r = GetPixelAdjustedRect();
            var rng = new System.Random(_seed);
            for (int i = 0; i < Count; i++)
            {
                float y = r.yMin + r.height * (float)rng.NextDouble();
                float length = 240f + 400f * (float)rng.NextDouble();
                float seconds = 0.36f + 0.2f * (float)rng.NextDouble();
                float delay = 0.16f * (float)rng.NextDouble();
                float thickness = 1.5f + 1.5f * (float)rng.NextDouble();
                float p = (_time - delay) / seconds;
                if (p <= 0f || p >= 1f)
                {
                    continue;
                }
                float alpha = p < 0.2f ? p / 0.2f * 0.9f : 0.9f * (1f - (p - 0.2f) / 0.8f);
                float travel = (r.width + length + 200f) * p;
                float x = _direction > 0 ? r.xMax - travel : r.xMin - length + travel;
                Color mid = new Color(0.92f, 0.98f, 1f, alpha);
                Color clear = new Color(0.92f, 0.98f, 1f, 0f);
                int start = vh.currentVertCount;
                vh.AddVert(new Vector2(x, y - thickness), clear, Vector2.zero);
                vh.AddVert(new Vector2(x + length * 0.4f, y - thickness), mid, Vector2.zero);
                vh.AddVert(new Vector2(x + length, y - thickness), clear, Vector2.zero);
                vh.AddVert(new Vector2(x, y + thickness), clear, Vector2.zero);
                vh.AddVert(new Vector2(x + length * 0.4f, y + thickness), mid, Vector2.zero);
                vh.AddVert(new Vector2(x + length, y + thickness), clear, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 4);
                vh.AddTriangle(start, start + 4, start + 3);
                vh.AddTriangle(start + 1, start + 2, start + 5);
                vh.AddTriangle(start + 1, start + 5, start + 4);
            }
        }
    }
}
