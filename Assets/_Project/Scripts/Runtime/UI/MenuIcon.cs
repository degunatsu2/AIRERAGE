using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メインメニューの項目の小さな絵（D-88 の B12。ユーザーの選択、2026-11-07）。線で描き、項目を選んでいる間だけ動く:
    /// SINGLE は人が跳ね、LOCAL は 2 つのコントローラーが交互に揺れ、NETWORK は地球が回り、TRAINING は的から輪が広がり、
    /// OPTIONS は歯車が回り、QUIT は扉が開いて矢印が入っていく。選んでいる間は水色、選んでいないときは白。
    /// 札（MenuOption）が Selected を決める（画面の切り替えで写しを動かしている間も、写しのまま動き続ける）。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MenuIcon : MaskableGraphic
    {
        public enum Shape
        {
            Person,
            Pads,
            Globe,
            Target,
            Gear,
            Door,
        }

        private static readonly Color IdleColor = new Color(1f, 1f, 1f, 0.8f);
        private static readonly Color ActiveColor = new Color(0.35f, 0.85f, 1f);
        // 絵は 28 × 28 の枠で決めた座標（真ん中が 0。上が +）を、Graphic の大きさに合わせて広げる。
        private const float DesignSize = 28f;

        [SerializeField] private Shape _shape;
        [SerializeField, HideInInspector] private bool _selected;
        [SerializeField, HideInInspector] private float _selectedAt;

        public Shape Kind => _shape;

        /// <summary>選んでいるか（選んでいる間だけ動く）。</summary>
        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected != value)
                {
                    _selected = value;
                    _selectedAt = Time.unscaledTime;
                    SetVerticesDirty();
                }
            }
        }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Shape shape)
        {
            _shape = shape;
            raycastTarget = false;
            SetVerticesDirty();
        }

        private void Update()
        {
            if (_selected)
            {
                SetVerticesDirty();
            }
        }

        private float _unit;
        private Vector2 _center;
        private Color _ink;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            _unit = Mathf.Min(r.width, r.height) / DesignSize;
            _center = r.center;
            _ink = _selected ? ActiveColor : IdleColor;
            _ink.a *= color.a;
            float t = _selected && Application.isPlaying ? Time.unscaledTime - _selectedAt : 0f;
            const float w = 1.9f;
            switch (_shape)
            {
                case Shape.Person:
                {
                    // 跳ねる（0.62 秒ごとに上へ 5 跳んで戻る）。
                    float phase = Mathf.Repeat(t, 0.62f) / 0.62f;
                    float hop = !_selected ? 0f : phase < 0.3f ? UiEase.OutCubic(phase / 0.3f) : 1f - UiEase.InCubic((phase - 0.3f) / 0.7f);
                    var o = new Vector2(0f, 5f * hop);
                    Circle(vh, o + new Vector2(0f, 7.5f), 3.2f, w, 20);
                    Line(vh, o + new Vector2(0f, 3.5f), o + new Vector2(0f, -4.5f), w);
                    Line(vh, o + new Vector2(-5.5f, 0.5f), o + new Vector2(5.5f, 0.5f), w);
                    Line(vh, o + new Vector2(0f, -4.5f), o + new Vector2(-4.5f, -11f), w);
                    Line(vh, o + new Vector2(0f, -4.5f), o + new Vector2(4.5f, -11f), w);
                    break;
                }
                case Shape.Pads:
                {
                    // 2 つのコントローラーが交互に ±12° 揺れる。
                    float swing = _selected ? 12f * Mathf.Sin(t * Mathf.PI * 2f / 1f) : 0f;
                    Pad(vh, new Vector2(-7.7f, 0f), swing, true, w);
                    Pad(vh, new Vector2(7.7f, 0f), -swing, false, w);
                    break;
                }
                case Shape.Globe:
                {
                    // 経線の楕円が細くなって太くなる（回って見える）。
                    float spin = _selected ? Mathf.Abs(Mathf.Cos(t * Mathf.PI / 1.3f)) : 1f;
                    Circle(vh, Vector2.zero, 10.5f, w, 36);
                    Ellipse(vh, Vector2.zero, Mathf.Max(0.4f, 4.6f * spin), 10.5f, w, 36);
                    Line(vh, new Vector2(-10.5f, 0f), new Vector2(10.5f, 0f), w);
                    Line(vh, new Vector2(-8.6f, 5.5f), new Vector2(8.6f, 5.5f), w);
                    Line(vh, new Vector2(-8.6f, -5.5f), new Vector2(8.6f, -5.5f), w);
                    break;
                }
                case Shape.Target:
                {
                    Circle(vh, Vector2.zero, 10.5f, w, 36);
                    Circle(vh, Vector2.zero, 5.8f, w, 28);
                    Disc(vh, Vector2.zero, 1.7f, 12);
                    if (_selected)
                    {
                        // 的から広がって消える輪（1.1 秒ごと）。
                        float p = Mathf.Repeat(t, 1.1f) / 1.1f;
                        Color keep = _ink;
                        _ink.a *= 1f - p;
                        Circle(vh, Vector2.zero, 10.5f * (0.4f + 1.1f * UiEase.OutCubic(p)), w, 36);
                        _ink = keep;
                    }
                    break;
                }
                case Shape.Gear:
                {
                    // 0.9 秒ごとに 45° 回る（8 本の歯なので、元と同じ形に戻る）。
                    float step = Mathf.Repeat(t, 0.9f) / 0.9f;
                    float angle = _selected ? 45f * UiEase.OutBack(step / 0.45f, 1.5f) : 0f;
                    Circle(vh, Vector2.zero, 4.2f, w, 20);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = (angle + i * 45f) * Mathf.Deg2Rad;
                        var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        Line(vh, dir * 6.6f, dir * 11.5f, w * 1.15f);
                    }
                    break;
                }
                case Shape.Door:
                {
                    // 扉の枠と、開いていく扉（1.4 秒ごと）と、扉へ入っていく矢印。
                    float cycle = Mathf.Repeat(t, 1.4f) / 1.4f;
                    float open = !_selected ? 0f
                        : cycle < 0.15f ? 0f
                        : cycle < 0.5f ? UiEase.InOutCubic((cycle - 0.15f) / 0.35f)
                        : cycle < 0.65f ? 1f
                        : 1f - UiEase.InOutCubic((cycle - 0.65f) / 0.35f);
                    var a = new Vector2(-5f, -11f);
                    var b = new Vector2(10f, 11f);
                    Rectangle(vh, a, b, w);
                    float panel = Mathf.Lerp(b.x - a.x, (b.x - a.x) * 0.3f, open);
                    Color keep = _ink;
                    _ink.a *= 0.28f;
                    Fill(vh, a, new Vector2(a.x + panel, b.y));
                    _ink = keep;
                    // 矢印は扉が開くと少し扉の中へ進む。
                    float push = 2.5f * open;
                    Line(vh, new Vector2(-12.5f + push, 0f), new Vector2(-3.5f + push, 0f), w);
                    Line(vh, new Vector2(-6.5f + push, 3.5f), new Vector2(-3f + push, 0f), w);
                    Line(vh, new Vector2(-6.5f + push, -3.5f), new Vector2(-3f + push, 0f), w);
                    break;
                }
            }
        }

        private void Pad(VertexHelper vh, Vector2 center, float degrees, bool cross, float w)
        {
            float rad = degrees * Mathf.Deg2Rad;
            Vector2 Rot(Vector2 p) => center + new Vector2(p.x * Mathf.Cos(rad) - p.y * Mathf.Sin(rad), p.x * Mathf.Sin(rad) + p.y * Mathf.Cos(rad));
            var c = new[] { new Vector2(-5.7f, -4.7f), new Vector2(5.7f, -4.7f), new Vector2(5.7f, 4.7f), new Vector2(-5.7f, 4.7f) };
            for (int i = 0; i < 4; i++)
            {
                Line(vh, Rot(c[i]), Rot(c[(i + 1) % 4]), w);
            }
            if (cross)
            {
                Line(vh, Rot(new Vector2(-3f, 0f)), Rot(new Vector2(1f, 0f)), w * 0.9f);
                Line(vh, Rot(new Vector2(-1f, -2f)), Rot(new Vector2(-1f, 2f)), w * 0.9f);
            }
            else
            {
                Disc(vh, Rot(new Vector2(1.6f, 0.3f)), 1.4f, 10);
            }
        }

        private Vector2 P(Vector2 design) => _center + design * _unit;

        private void Line(VertexHelper vh, Vector2 a, Vector2 b, float width)
        {
            Vector2 pa = P(a);
            Vector2 pb = P(b);
            Vector2 d = pb - pa;
            if (d.sqrMagnitude < 0.0001f)
            {
                return;
            }
            Vector2 n = new Vector2(-d.y, d.x).normalized * width * _unit * 0.5f;
            // 端を少し伸ばして、つなぎ目の隙間をなくす。
            Vector2 e = d.normalized * width * _unit * 0.5f;
            Quad(vh, pa - e - n, pb + e - n, pb + e + n, pa - e + n);
        }

        private void Circle(VertexHelper vh, Vector2 c, float radius, float width, int segments)
        {
            Ellipse(vh, c, radius, radius, width, segments);
        }

        private void Ellipse(VertexHelper vh, Vector2 c, float rx, float ry, float width, int segments)
        {
            float h = width * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.PI * 2f * i / segments;
                float a1 = Mathf.PI * 2f * (i + 1) / segments;
                var u0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var u1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                Quad(vh,
                    P(c + new Vector2(u0.x * (rx - h), u0.y * (ry - h))), P(c + new Vector2(u1.x * (rx - h), u1.y * (ry - h))),
                    P(c + new Vector2(u1.x * (rx + h), u1.y * (ry + h))), P(c + new Vector2(u0.x * (rx + h), u0.y * (ry + h))));
            }
        }

        private void Disc(VertexHelper vh, Vector2 c, float radius, int segments)
        {
            int start = vh.currentVertCount;
            vh.AddVert(P(c), _ink, Vector2.zero);
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.PI * 2f * i / segments;
                vh.AddVert(P(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius), _ink, Vector2.zero);
            }
            for (int i = 0; i < segments; i++)
            {
                vh.AddTriangle(start, start + 1 + i, start + 2 + i);
            }
        }

        private void Rectangle(VertexHelper vh, Vector2 min, Vector2 max, float width)
        {
            Line(vh, new Vector2(min.x, min.y), new Vector2(max.x, min.y), width);
            Line(vh, new Vector2(max.x, min.y), new Vector2(max.x, max.y), width);
            Line(vh, new Vector2(max.x, max.y), new Vector2(min.x, max.y), width);
            Line(vh, new Vector2(min.x, max.y), new Vector2(min.x, min.y), width);
        }

        private void Fill(VertexHelper vh, Vector2 min, Vector2 max)
        {
            Quad(vh, P(min), P(new Vector2(max.x, min.y)), P(max), P(new Vector2(min.x, max.y)));
        }

        private void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, _ink, Vector2.zero);
            vh.AddVert(b, _ink, Vector2.zero);
            vh.AddVert(c, _ink, Vector2.zero);
            vh.AddVert(d, _ink, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
