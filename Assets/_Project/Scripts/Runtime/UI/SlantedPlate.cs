using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 右へ傾いた平行四辺形の札（uGUI。D-65、D-69。右上の札 GuidePlate と同じ形）。中の色は Graphic の color、縁は BorderColor。
    /// 上の辺が下の辺より Slant（高さに対する割合）だけ右にずれる。
    /// 選んだときと決定したときの演出（D-88。ユーザーが選んだ B1・B2・B3・B9・B10。2026-11-07）も、この札が自分で描いて動かす
    /// （画面の切り替えで札の写しを動かすときも、演出が途中で止まらないように。動きの時刻は写しにも引き継ぐ）:
    /// - 導火線の縁（B1）: 選んだ瞬間、白い光（FuseColor）が縁を 1 周する（RunFuse）。選んでいる間は縁がゆっくり明滅する（Glow）。
    /// - 残像（B2）: 札が動いたとき、2 枚の水色の残像が遅れて追いかけて消える（Trail）。
    /// - 裏の札（B3）: 選んでいる間、水色の札が左下へずれて出る（BackTarget）。
    /// - チルト（B9）: マウスの位置へ札がわずかに傾き（Lean）、光の反射が付いてくる（Sheen）。
    /// - 決定の光（B10）: 決定した瞬間、札が白く光ってから本来の色へ戻る（Flash）。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SlantedPlate : MaskableGraphic
    {
        /// <summary>導火線の光が 1 周する秒数。</summary>
        public const float FuseSeconds = 0.46f;
        /// <summary>決定の白い光が消えるまでの秒数。</summary>
        public const float FlashSeconds = 0.24f;
        /// <summary>選んで札が動く秒数（MenuOption の動きと同じ。残像はこの動きから遅れる）。</summary>
        public const float MoveSeconds = 0.3f;
        /// <summary>選んで札が動くときの行きすぎの大きさ（UiEase.OutBack）。</summary>
        public const float MoveOvershoot = 1.8f;

        private const float TrailSeconds = 0.42f;
        private const float TrailLag = 0.045f;
        private const float BackSeconds = 0.18f;
        private const float GlowPeriod = 1.8f;
        private const int SheenSlices = 20;

        [Tooltip("傾き（高さに対する、上の辺の右へのずれの割合）")]
        [SerializeField] private float _slant = 0.35f;
        [Tooltip("縁の太さ（ピクセル）。0 なら縁なし")]
        [SerializeField] private float _borderWidth = 3f;
        [SerializeField] private Color _borderColor = new Color(1f, 1f, 1f, 0.6f);
        [Tooltip("裏の札・残像・縁の明滅の色")]
        [SerializeField] private Color _accentColor = new Color(0.35f, 0.85f, 1f);
        [Tooltip("導火線の光の芯の色（ふつうは白。メインメニューは項目の色。2026-11-07）")]
        [SerializeField] private Color _fuseColor = Color.white;

        // 演出の状態（写しにも引き継ぐため保存する値にする。時刻は Time.unscaledTime）。
        [SerializeField, HideInInspector] private float _fuseAt = -10f;
        [SerializeField, HideInInspector] private float _flashAt = -10f;
        [SerializeField, HideInInspector] private float _trailAt = -10f;
        [SerializeField, HideInInspector] private Vector2 _trailDelta;
        [SerializeField, HideInInspector] private bool _glow;
        [SerializeField, HideInInspector] private float _back;
        [SerializeField, HideInInspector] private float _backTarget;
        [SerializeField, HideInInspector] private Vector2 _lean;
        [SerializeField, HideInInspector] private Vector3 _sheen;

        /// <summary>傾き（高さに対する、上の辺の右へのずれの割合）。</summary>
        public float Slant => _slant;

        public Color BorderColor
        {
            get => _borderColor;
            set
            {
                if (_borderColor != value)
                {
                    _borderColor = value;
                    SetVerticesDirty();
                }
            }
        }

        public Color AccentColor
        {
            get => _accentColor;
            set
            {
                _accentColor = value;
                SetVerticesDirty();
            }
        }

        /// <summary>導火線の光の芯の色（B1）。</summary>
        public Color FuseColor
        {
            get => _fuseColor;
            set
            {
                _fuseColor = value;
                SetVerticesDirty();
            }
        }

        /// <summary>選んでいる間、縁をゆっくり明滅させるか（B1）。</summary>
        public bool Glow
        {
            get => _glow;
            set
            {
                if (_glow != value)
                {
                    _glow = value;
                    SetVerticesDirty();
                }
            }
        }

        /// <summary>裏の札を出すか（0 か 1。B3）。</summary>
        public float BackTarget
        {
            get => _backTarget;
            set => _backTarget = Mathf.Clamp01(value);
        }

        /// <summary>裏の札の今の出かた（0〜1。テスト用）。</summary>
        public float Back => _back;

        /// <summary>導火線の光が縁を回っている途中か（テスト用）。</summary>
        public bool FuseRunning => Now - _fuseAt < FuseSeconds;

        /// <summary>決定の白い光の途中か（テスト用）。</summary>
        public bool Flashing => Now - _flashAt < FlashSeconds;

        private static float Now => Time.unscaledTime;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(float slant, float borderWidth, Color fill, Color border)
        {
            _slant = slant;
            _borderWidth = borderWidth;
            color = fill;
            _borderColor = border;
            SetVerticesDirty();
        }

        /// <summary>白い光に縁を 1 周させる（B1。選んだ瞬間）。</summary>
        public void RunFuse()
        {
            _fuseAt = Now;
            SetVerticesDirty();
        }

        /// <summary>白く光ってから本来の色へ戻る（B10。決定した瞬間）。</summary>
        public void Flash()
        {
            _flashAt = Now;
            SetVerticesDirty();
        }

        /// <summary>札が delta だけ動き始めた（B2。残像が遅れて追いかける）。</summary>
        public void Trail(Vector2 delta)
        {
            _trailAt = Now;
            _trailDelta = delta;
            SetVerticesDirty();
        }

        /// <summary>マウスの方向への傾き（B9。-1〜1。0 で傾けない）と、光の反射（x は札の左端 0〜右端 1、strength は 0〜1）。</summary>
        public void SetLean(Vector2 lean, float sheenX, float strength)
        {
            var sheen = new Vector3(sheenX, strength, 0f);
            if (lean != _lean || sheen != _sheen)
            {
                _lean = lean;
                _sheen = sheen;
                SetVerticesDirty();
            }
        }

        /// <summary>演出を全部止めて元に戻す（画面を閉じたとき）。</summary>
        public void ResetEffects()
        {
            _fuseAt = -10f;
            _flashAt = -10f;
            _trailAt = -10f;
            _glow = false;
            _back = 0f;
            _backTarget = 0f;
            _lean = Vector2.zero;
            _sheen = Vector3.zero;
            SetVerticesDirty();
        }

        private void Update()
        {
            float now = Now;
            bool dirty = _glow || now - _fuseAt < FuseSeconds + 0.05f || now - _flashAt < FlashSeconds + 0.05f
                || now - _trailAt < TrailSeconds + 0.05f;
            if (!Mathf.Approximately(_back, _backTarget))
            {
                _back = Mathf.MoveTowards(_back, _backTarget, Time.unscaledDeltaTime / BackSeconds);
                dirty = true;
            }
            if (dirty)
            {
                SetVerticesDirty();
            }
        }

        // 選んで札が動く曲線（MenuOption も同じものを使う）。
        public static float MoveEase(float t) => UiEase.OutBack(t, MoveOvershoot);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float shift = r.height * _slant;
            float now = Application.isPlaying ? Now : -100f;
            Vector2[] outer = Inset(r, shift, 0f);

            // 残像（B2）: 札の動きから遅れた位置に、左が濃い水色の札を 2 枚。
            float trail = now - _trailAt;
            if (trail >= 0f && trail < TrailSeconds && _trailDelta != Vector2.zero)
            {
                float self = MoveEase(trail / MoveSeconds);
                for (int k = 0; k < 2; k++)
                {
                    float lagged = MoveEase(Mathf.Max(0f, trail - TrailLag * (k + 1)) / MoveSeconds);
                    Vector2 offset = (lagged - self) * _trailDelta;
                    float alpha = (0.5f - 0.18f * k) * (1f - trail / TrailSeconds);
                    Color left = _accentColor;
                    left.a = alpha;
                    Color right = _accentColor;
                    right.a = alpha * 0.1f;
                    AddQuad(vh, r, Offset(outer, offset), new[] { left, right, right, left });
                }
            }

            // 裏の札（B3）: 左下へずれた水色の札。出るときは左から伸びる。
            if (_back > 0.001f)
            {
                float a = UiEase.OutCubic(_back);
                var back = new Vector2[4];
                float stretch = 0.4f + 0.6f * a;
                for (int i = 0; i < 4; i++)
                {
                    back[i] = new Vector2(r.xMin + (outer[i].x - r.xMin) * stretch, outer[i].y) + new Vector2(-9f, -7f) * a;
                }
                Color c = _accentColor;
                c.a = 0.95f * a;
                AddQuad(vh, r, back, Uniform(c));
            }

            // 中（決定の瞬間は白く光る。B10）。
            Color fill = color;
            float flash = now - _flashAt;
            if (flash >= 0f && flash < FlashSeconds)
            {
                float f = 1f - flash / FlashSeconds;
                float amount = f * f * 0.95f;
                fill = Color.Lerp(fill, Color.white, amount);
                fill.a = Mathf.Max(color.a, amount);
            }
            AddQuad(vh, r, outer, Uniform(fill));

            // 光の反射（B9）: マウスの位置を中心に、細い縦の帯で白を重ねる。
            if (_sheen.y > 0.01f)
            {
                for (int i = 0; i < SheenSlices; i++)
                {
                    float u0 = (float)i / SheenSlices;
                    float u1 = (float)(i + 1) / SheenSlices;
                    Color c0 = SheenColor(u0);
                    Color c1 = SheenColor(u1);
                    var quad = new[]
                    {
                        Vector2.Lerp(outer[0], outer[1], u0), Vector2.Lerp(outer[0], outer[1], u1),
                        Vector2.Lerp(outer[3], outer[2], u1), Vector2.Lerp(outer[3], outer[2], u0),
                    };
                    AddQuad(vh, r, quad, new[] { c0, c1, c1, c0 });
                }
            }

            if (_borderWidth <= 0f || _borderColor.a <= 0f)
            {
                return;
            }

            // 縁の外の柔らかい光（B1。選んでいる間の明滅）。
            if (_glow && Application.isPlaying)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(now * Mathf.PI * 2f / GlowPeriod);
                Vector2[] halo = Inset(r, shift, -(3f + 5f * pulse));
                Color edge = _accentColor;
                edge.a = 0.45f * (0.4f + 0.6f * pulse);
                Color clear = _accentColor;
                clear.a = 0f;
                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    AddQuad(vh, r, new[] { halo[i], halo[j], outer[j], outer[i] }, new[] { clear, clear, edge, edge });
                }
            }

            // 縁（内の四隅は縁の太さだけ内へ）。
            Vector2[] inner = Inset(r, shift, _borderWidth);
            Color border = _borderColor;
            if (_glow && Application.isPlaying)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(now * Mathf.PI * 2f / GlowPeriod);
                border = Color.Lerp(border, Color.white, 0.25f * pulse);
            }
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                AddQuad(vh, r, new[] { outer[i], outer[j], inner[j], inner[i] }, Uniform(border));
            }

            // 導火線の光（B1）: 左上から時計回りに、尾の消える白い光が縁を 1 周する。外側に柔らかい光。
            float fuse = now - _fuseAt;
            if (fuse >= 0f && fuse < FuseSeconds)
            {
                float t = fuse / FuseSeconds;
                float head = UiEase.InOutCubic(t);
                float alpha = t < 0.86f ? 1f : (1f - t) / 0.14f;
                Vector2[] path = Clockwise(outer);
                Vector2[] pathIn = Clockwise(Inset(r, shift, Mathf.Max(3.5f, _borderWidth + 0.5f)));
                Vector2[] pathOut = Clockwise(Inset(r, shift, -7f));
                Color core = _fuseColor;
                core.a = alpha;
                AddFuse(vh, r, path, pathIn, head, 0.12f, core, core);
                Color glowColor = Color.Lerp(_accentColor, Color.white, 0.5f);
                glowColor.a = 0.45f * alpha;
                Color clearGlow = glowColor;
                clearGlow.a = 0f;
                AddFuse(vh, r, pathOut, path, head, 0.12f, clearGlow, glowColor);
            }
        }

        private Color SheenColor(float u)
        {
            float d = (u - _sheen.x) / 0.22f;
            return new Color(1f, 1f, 1f, _sheen.y * 0.26f * Mathf.Exp(-d * d));
        }

        // 縁の太さ d だけ内へ寄せた四隅（左下、右下、右上、左上）。d が負なら外へ広げる。
        private Vector2[] Inset(Rect r, float shift, float d)
        {
            float side = d * Mathf.Sqrt(1f + _slant * _slant);
            return new[]
            {
                new Vector2(r.xMin + side + d * _slant, r.yMin + d), new Vector2(r.xMax - shift - side + d * _slant, r.yMin + d),
                new Vector2(r.xMax - side - d * _slant, r.yMax - d), new Vector2(r.xMin + shift + side - d * _slant, r.yMax - d),
            };
        }

        private static Vector2[] Offset(Vector2[] corners, Vector2 offset)
        {
            var moved = new Vector2[corners.Length];
            for (int i = 0; i < corners.Length; i++)
            {
                moved[i] = corners[i] + offset;
            }
            return moved;
        }

        // 左上から時計回り（左上、右上、右下、左下）に並べ直す。
        private static Vector2[] Clockwise(Vector2[] corners) => new[] { corners[3], corners[2], corners[1], corners[0] };

        private static Color[] Uniform(Color c) => new[] { c, c, c, c };

        // 縁に沿った光の帯（path と pathIn の間。頭が head、長さ length。尾は透明 tailColor、頭は headColor）。
        private void AddFuse(VertexHelper vh, Rect r, Vector2[] path, Vector2[] pathIn, float head, float length, Color tailColor, Color headColor)
        {
            float[] stops = PathStops(path);
            float start = head - length;
            const int steps = 12;
            for (int i = 0; i < steps; i++)
            {
                float a = start + length * i / steps;
                float b = start + length * (i + 1) / steps;
                // 角をまたぐ区間は角で分ける。
                float corner = CornerBetween(stops, a, b);
                if (corner > a && corner < b)
                {
                    AddFuseSegment(vh, r, path, pathIn, stops, a, corner, start, length, tailColor, headColor);
                    AddFuseSegment(vh, r, path, pathIn, stops, corner, b, start, length, tailColor, headColor);
                }
                else
                {
                    AddFuseSegment(vh, r, path, pathIn, stops, a, b, start, length, tailColor, headColor);
                }
            }
        }

        private void AddFuseSegment(VertexHelper vh, Rect r, Vector2[] path, Vector2[] pathIn, float[] stops, float a, float b, float start,
            float length, Color tailColor, Color headColor)
        {
            Color ca = Color.Lerp(tailColor, headColor, (a - start) / length);
            Color cb = Color.Lerp(tailColor, headColor, (b - start) / length);
            AddQuad(vh, r, new[] { PointOn(path, stops, a), PointOn(path, stops, b), PointOn(pathIn, stops, b), PointOn(pathIn, stops, a) },
                new[] { ca, cb, cb, ca });
        }

        // 周の長さに対する各角の位置（0〜1）。
        private static float[] PathStops(Vector2[] path)
        {
            var stops = new float[path.Length + 1];
            float total = 0f;
            for (int i = 0; i < path.Length; i++)
            {
                total += Vector2.Distance(path[i], path[(i + 1) % path.Length]);
                stops[i + 1] = total;
            }
            for (int i = 1; i < stops.Length; i++)
            {
                stops[i] /= Mathf.Max(0.0001f, total);
            }
            return stops;
        }

        private static float Wrap(float u) => u - Mathf.Floor(u);

        private static Vector2 PointOn(Vector2[] path, float[] stops, float u)
        {
            u = Wrap(u);
            for (int i = 0; i < path.Length; i++)
            {
                if (u <= stops[i + 1])
                {
                    float t = Mathf.InverseLerp(stops[i], stops[i + 1], u);
                    return Vector2.Lerp(path[i], path[(i + 1) % path.Length], t);
                }
            }
            return path[0];
        }

        // a〜b（周をまたいでもよい）の間にある角の位置（なければ -1）。
        private static float CornerBetween(float[] stops, float a, float b)
        {
            float baseTurn = Mathf.Floor(a);
            for (int turn = 0; turn < 2; turn++)
            {
                for (int i = 0; i < stops.Length; i++)
                {
                    float corner = baseTurn + turn + stops[i];
                    if (corner > a && corner < b)
                    {
                        return corner;
                    }
                }
            }
            return -1f;
        }

        // 頂点を足す（チルト B9 の傾きで、マウスに近い辺を少し大きく見せる）。
        private void AddQuad(VertexHelper vh, Rect r, Vector2[] corners, Color[] colors)
        {
            int start = vh.currentVertCount;
            for (int i = 0; i < 4; i++)
            {
                vh.AddVert(Warp(r, corners[i]), colors[i], Vector2.zero);
            }
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        private Vector2 Warp(Rect r, Vector2 p)
        {
            if (_lean == Vector2.zero)
            {
                return p;
            }
            Vector2 c = r.center;
            float nx = (p.x - c.x) / Mathf.Max(1f, r.width * 0.5f);
            float ny = (p.y - c.y) / Mathf.Max(1f, r.height * 0.5f);
            p.y = c.y + (p.y - c.y) * (1f + 0.07f * _lean.x * nx);
            p.x = c.x + (p.x - c.x) * (1f + 0.025f * _lean.y * ny);
            return p;
        }
    }
}
