using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 決定の衝撃（D-88 の B10。ユーザーの選択、2026-11-07）: 札の中心から広がって消える水色の輪と、札の右から上へ散って落ちる金色の
    /// 火花（コインの火花）。札（MenuOption）の子に置き、決定したときに Play する。自分で動くので、画面の切り替えで札の写しを
    /// 動かしている間も続く。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UiBurst : MaskableGraphic
    {
        public const float Seconds = 0.8f;
        private const float RingSeconds = 0.44f;
        private const int RingSegments = 48;
        private const int Sparks = 14;
        private static readonly Color RingColor = new Color(0.35f, 0.85f, 1f);
        private static readonly Color SparkColor = new Color(1f, 0.82f, 0.3f);

        [SerializeField, HideInInspector] private float _playedAt = -10f;
        [SerializeField, HideInInspector] private int _seed;

        /// <summary>動いている途中か（テスト用）。</summary>
        public bool Playing => Time.unscaledTime - _playedAt < Seconds;

        public void Play()
        {
            _playedAt = Time.unscaledTime;
            _seed = Random.Range(1, int.MaxValue);
            SetVerticesDirty();
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        private void Update()
        {
            if (Time.unscaledTime - _playedAt < Seconds + 0.05f)
            {
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float t = Application.isPlaying ? Time.unscaledTime - _playedAt : -1f;
            if (t < 0f || t >= Seconds)
            {
                return;
            }
            Rect r = GetPixelAdjustedRect();
            Vector2 center = r.center;

            // 輪（横長の楕円。広がりながら細く薄くなる）。
            if (t < RingSeconds)
            {
                float p = UiEase.OutCubic(t / RingSeconds);
                float rx = r.width * Mathf.Lerp(0.47f, 0.9f, p);
                float ry = r.height * Mathf.Lerp(0.55f, 1.8f, p);
                float width = Mathf.Lerp(4f, 1.2f, p);
                Color c = RingColor;
                c.a = 1f - p;
                for (int i = 0; i < RingSegments; i++)
                {
                    float a0 = Mathf.PI * 2f * i / RingSegments;
                    float a1 = Mathf.PI * 2f * (i + 1) / RingSegments;
                    var o0 = new Vector2(Mathf.Cos(a0) * rx, Mathf.Sin(a0) * ry);
                    var o1 = new Vector2(Mathf.Cos(a1) * rx, Mathf.Sin(a1) * ry);
                    var i0 = new Vector2(Mathf.Cos(a0) * (rx - width), Mathf.Sin(a0) * (ry - width));
                    var i1 = new Vector2(Mathf.Cos(a1) * (rx - width), Mathf.Sin(a1) * (ry - width));
                    Quad(vh, center + i0, center + i1, center + o1, center + o0, c);
                }
            }

            // 火花（菱形。上へ散って、重さで落ちながら小さく消える）。
            var rng = new System.Random(_seed);
            var origin = new Vector2(r.xMax - r.height * 0.8f, center.y);
            for (int k = 0; k < Sparks; k++)
            {
                double angle = (10.0 + rng.NextDouble() * 160.0) * Mathf.Deg2Rad;
                float speed = 120f + (float)rng.NextDouble() * 260f;
                float life = 0.56f + (float)rng.NextDouble() * 0.2f;
                float s = t / life;
                if (s >= 1f)
                {
                    continue;
                }
                float travel = UiEase.OutCubic(s);
                var pos = origin + new Vector2((float)System.Math.Cos(angle), (float)System.Math.Sin(angle)) * speed * travel
                    - new Vector2(0f, 140f * s * s);
                float size = Mathf.Lerp(8f, 3f, s);
                Color c = SparkColor;
                c.a = s < 0.45f ? 1f : 1f - (s - 0.45f) / 0.55f;
                Quad(vh, pos + new Vector2(0f, size), pos + new Vector2(size, 0f), pos + new Vector2(0f, -size), pos + new Vector2(-size, 0f), c);
            }
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
