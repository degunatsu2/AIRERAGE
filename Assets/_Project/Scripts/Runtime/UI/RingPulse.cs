using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 広がって消える色の輪（キャラクター選択の決定の衝撃波。D-89 の C14）。置いた所（この RectTransform の真ん中）から、半径 0 から
    /// MaxRadius まで Seconds 秒で広がり、細く薄くなって消える。自分で動く（Play してから Seconds 秒の間）。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RingPulse : MaskableGraphic
    {
        private const int Segments = 72;

        [SerializeField, HideInInspector] private float _playedAt = -10f;
        [SerializeField] private float _maxRadius = 900f;
        [SerializeField] private float _seconds = 0.56f;

        /// <summary>広がり切る秒数。</summary>
        public float Seconds => _seconds;

        /// <summary>動いている途中か（テスト用）。</summary>
        public bool Playing => Time.unscaledTime - _playedAt < _seconds;

        /// <summary>輪の半径が radius になるまでの秒数（輪が通るのに合わせて、ほかの物を光らせるため）。</summary>
        public float TimeToReach(float radius)
        {
            float y = Mathf.Clamp01(radius / Mathf.Max(1f, _maxRadius));
            // 広がりは UiEase.OutCubic（1 - (1 - t)^3）。その逆。
            return _seconds * (1f - Mathf.Pow(1f - y, 1f / 3f));
        }

        public void Play(Color ringColor, float maxRadius, float seconds)
        {
            color = ringColor;
            _maxRadius = maxRadius;
            _seconds = seconds;
            _playedAt = Time.unscaledTime;
            raycastTarget = false;
            SetVerticesDirty();
        }

        private void Update()
        {
            if (Time.unscaledTime - _playedAt < _seconds + 0.05f)
            {
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float t = Application.isPlaying ? Time.unscaledTime - _playedAt : -1f;
            if (t < 0f || t >= _seconds)
            {
                return;
            }
            float p = UiEase.OutCubic(t / _seconds);
            float radius = _maxRadius * p;
            float width = Mathf.Lerp(10f, 2f, p);
            Color c = color;
            c.a *= 1f - p;
            Vector2 center = GetPixelAdjustedRect().center;
            for (int i = 0; i < Segments; i++)
            {
                float a0 = Mathf.PI * 2f * i / Segments;
                float a1 = Mathf.PI * 2f * (i + 1) / Segments;
                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                int start = vh.currentVertCount;
                vh.AddVert(center + d0 * Mathf.Max(0f, radius - width), c, Vector2.zero);
                vh.AddVert(center + d1 * Mathf.Max(0f, radius - width), c, Vector2.zero);
                vh.AddVert(center + d1 * radius, c, Vector2.zero);
                vh.AddVert(center + d0 * radius, c, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}
