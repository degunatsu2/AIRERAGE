using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 円形ゲージの見た目（ユーザーの決定、2026-10-29。D-76: 「斜めの区切り」の輪に「光る先端（導火線）」とグラデーションを重ねる。
    /// すべての円形ゲージで同じ見た目）。
    /// - 輪: 斜めに切った台形の区切り 16 個。区切りごとに暗い縁と、外側に細い線（残っている区切りは明るく。同じ色のオーラと見分けられるように）。
    /// - 残り: 上（12 時）から時計回りに、残りの分だけ区切りが光る。尾は薄く暗く、先端へ向かって明るくなる。残っていない所は薄い影。
    /// - 先端: 光の玉と、後ろへ流れる小さな粒。残り 25% を切ると、先端が赤みを帯びて脈打ち、光る区切りも明滅する（数え下げのゲージだけ）。
    /// - 出る瞬間は少し大きく出て縮み（0.2 秒）、0 近くまで減って消えるときは白い輪が外へ広がって消える（0.4 秒）。
    /// 1 つのメッシュに毎フレーム描き直す（頂点の色で塗る。材質は Sprites-Default）。持ち主のシーンに置き、持ち主が Destroy で片付ける。
    /// 見た目だけで、状態は読まない（呼ぶ側が割合を渡す）。
    /// </summary>
    public sealed class SlantedRingGauge
    {
        public const int Segments = 16;
        private const float GapRadians = 0.05f;
        private const float SkewRadians = 0.08f;
        private const float FrameExtra = 0.08f;
        private const float RimWidth = 0.03f;
        private const float LowFraction = 0.25f;
        private const float AppearSeconds = 0.2f;
        private const float BurstSeconds = 0.4f;
        private const float FinishFraction = 0.06f;
        private const int GlowPoints = 14;

        private static readonly Color FrameColor = new Color(0.04f, 0.04f, 0.07f, 0.85f);
        private static readonly Color RimColor = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color WarmHead = new Color(1f, 0.55f, 0.5f, 1f);

        private readonly GameObject _object;
        private readonly Mesh _mesh;
        private readonly MeshRenderer _renderer;
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _triangles = new List<int>();

        private bool _showing;
        private float _shownAt = -10f;
        private float _burstAt = -10f;
        private float _lastFraction;
        private Vector3 _lastCenter;
        private float _lastRadius;
        private float _lastWidth;
        private float _lastZ;

        /// <summary>ゲージを描くメッシュを、owner と同じシーンに作る（親は持たない。点は世界の座標）。</summary>
        public SlantedRingGauge(Component owner, string name, Material material, int sortingOrder)
        {
            _object = new GameObject(name);
            if (owner != null && owner.gameObject.scene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(_object, owner.gameObject.scene);
            }
            _mesh = new Mesh { name = name };
            _mesh.MarkDynamic();
            _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _object.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.sortingOrder = sortingOrder;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
        }

        /// <summary>ゲージ（または終わりの白い輪）を描いているか。</summary>
        public bool Visible => _renderer != null && _renderer.enabled;

        /// <summary>ゲージを出しているか（終わりの白い輪だけのときは false）。</summary>
        public bool Showing => _showing;

        /// <summary>縁（区切りごとの暗い縁と外側の線）を描いているか（ゲージを出していれば、いつも描く）。</summary>
        public bool ShowsFrame => _showing && Visible;

        /// <summary>最後に描いた先端の位置。</summary>
        public Vector3 Tip { get; private set; }

        /// <summary>
        /// center の周りに、残りの割合 fraction（0〜1）のゲージを描く。radius は輪の半径、width は輪の太さ（m）、z は奥行き。
        /// countdown なら、残りが少ないときに先端を赤みにして脈打たせる（数え下げのゲージ）。毎フレーム呼ぶ。
        /// </summary>
        public void Draw(Vector3 center, float radius, float width, float fraction, Color color, float z, bool countdown = true)
        {
            float time = Time.time;
            if (!_showing)
            {
                _showing = true;
                _shownAt = time;
                _burstAt = -10f;
            }
            fraction = Mathf.Clamp01(fraction);
            _lastFraction = fraction;
            _lastCenter = center;
            _lastRadius = radius;
            _lastWidth = width;
            _lastZ = z;

            float appear = Mathf.Clamp01((time - _shownAt) / AppearSeconds);
            float scale = 1.15f - 0.15f * (1f - (1f - appear) * (1f - appear));
            float alpha = Mathf.Min(1f, appear * 1.5f);
            bool low = countdown && fraction < LowFraction;
            float pulse = low ? 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(time * 9f)) : 1f;

            Begin();
            Build(center, radius * scale, width * scale, fraction, color, z, alpha, low, pulse, time);
            Finish();
        }

        /// <summary>隠す。0 近くまで減ったゲージを隠すときは、終わりの白い輪を出す（Advance で進める）。</summary>
        public void Hide()
        {
            if (_showing && _lastFraction <= FinishFraction && Time.time - _shownAt > AppearSeconds)
            {
                _burstAt = Time.time;
            }
            _showing = false;
            if (!BurstActive())
            {
                _renderer.enabled = false;
            }
        }

        /// <summary>終わりの白い輪を、すぐに消す（巻き戻しなどで、終わったのではなく消えたとき）。</summary>
        public void Cancel()
        {
            _burstAt = -10f;
            if (!_showing)
            {
                _renderer.enabled = false;
            }
        }

        /// <summary>隠した後の、終わりの白い輪を進める（ゲージを出していない間、毎フレーム呼ぶ）。</summary>
        public void Advance()
        {
            if (_showing)
            {
                return;
            }
            if (!BurstActive())
            {
                if (_renderer.enabled)
                {
                    _renderer.enabled = false;
                }
                return;
            }
            float p = (Time.time - _burstAt) / BurstSeconds;
            Begin();
            float r = _lastRadius * (1f + 0.5f * p);
            float w = Mathf.Max(0.02f, _lastWidth * 1.4f * (1f - p));
            AddArc(_lastCenter, r - w * 0.5f, r + w * 0.5f, 0f, Mathf.PI * 2f, new Color(1f, 1f, 1f, 1f - p), new Color(1f, 1f, 1f, 1f - p), _lastZ, 48);
            Finish();
        }

        /// <summary>メッシュとその入れ物を片付ける。</summary>
        public void Destroy()
        {
            if (_object != null)
            {
                Object.Destroy(_object);
            }
            if (_mesh != null)
            {
                Object.Destroy(_mesh);
            }
        }

        private bool BurstActive()
        {
            return Time.time - _burstAt < BurstSeconds;
        }

        private void Build(Vector3 center, float radius, float width, float fraction, Color color, float z, float alpha, bool low, float pulse,
            float time)
        {
            float step = Mathf.PI * 2f / Segments;
            float half = width * 0.5f;
            float lit = fraction * Segments;
            float litEnd = fraction * Mathf.PI * 2f;
            // 残っていない区切りは暗く薄く、残っている区切りは尾でも色がはっきり見える濃さにする（残りが一目で分かるように）。
            Color track = new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, 0.18f * alpha);
            Color dim = new Color(color.r * 0.8f, color.g * 0.8f, color.b * 0.8f, 0.85f * alpha * pulse);
            Color bright = Color.Lerp(color, Color.white, 0.45f);
            bright.a = alpha * pulse;
            Color frame = FrameColor;
            frame.a *= alpha;
            Color rim = RimColor;
            rim.a *= alpha;
            Color faintRim = rim;
            faintRim.a *= 0.3f;

            // 1. 区切りごとの暗い縁（全部の区切り。輪の形を見せる）。
            for (int i = 0; i < Segments; i++)
            {
                float a0 = i * step + GapRadians * 0.6f;
                float a1 = (i + 1) * step - GapRadians * 0.6f;
                AddSegment(center, radius - half - FrameExtra * 0.5f, radius + half + FrameExtra * 0.5f, a0, a1, frame, frame, z + 0.01f);
            }
            // 2. 残っていない区切りの薄い影と、残っている区切り（尾から先端へ明るくなる）。
            for (int i = 0; i < Segments; i++)
            {
                float a0 = i * step + GapRadians;
                float a1 = (i + 1) * step - GapRadians;
                float amount = Mathf.Clamp01(lit - i);
                if (amount < 1f)
                {
                    float from = a0 + (a1 - a0) * amount;
                    AddSegment(center, radius - half, radius + half, from, a1, track, track, z);
                }
                if (amount > 0f)
                {
                    float to = a0 + (a1 - a0) * amount;
                    Color c0 = Gradient(dim, bright, litEnd > 0f ? a0 / litEnd : 1f);
                    Color c1 = Gradient(dim, bright, litEnd > 0f ? to / litEnd : 1f);
                    AddSegment(center, radius - half, radius + half, a0, to, c0, c1, z - 0.002f);
                }
                // 外側の明るい細い線（残っている区切りだけ明るく）。
                Color edge = amount > 0.5f ? rim : faintRim;
                AddSegment(center, radius + half, radius + half + RimWidth, a0, a1, edge, edge, z - 0.004f);
            }
            // 3. 先端の光の玉と、後ろへ流れる粒。
            if (fraction > 0f)
            {
                float headPulse = low ? 1f + 0.35f * Mathf.Abs(Mathf.Sin(time * 9f)) : 1f;
                Color head = low ? WarmHead : Color.Lerp(color, Color.white, 0.6f);
                head.a = alpha;
                Vector3 tip = Point(center, radius, litEnd, z - 0.006f);
                Tip = tip;
                AddGlow(tip, width * 1.9f * headPulse, head, z - 0.006f);
                AddGlow(tip, width * 0.55f, new Color(1f, 1f, 1f, alpha), z - 0.008f);
                for (int j = 1; j <= 3; j++)
                {
                    float back = Mathf.Max(0f, litEnd - 0.12f * j * Mathf.Min(1f, 1.3f / Mathf.Max(0.3f, radius)));
                    float wobble = Mathf.Sin(time * 7f + j) * width * 0.35f;
                    Vector3 spark = Point(center, radius + wobble, back, z - 0.007f);
                    AddGlow(spark, width * (0.42f - 0.08f * j), new Color(1f, 1f, 1f, alpha * (0.8f - 0.2f * j)), z - 0.007f);
                }
            }
        }

        private static Color Gradient(Color dim, Color bright, float t)
        {
            t = Mathf.Clamp01(t);
            return Color.Lerp(dim, bright, t * t);
        }

        private static Vector3 Point(Vector3 center, float radius, float angle, float z)
        {
            return new Vector3(center.x + Mathf.Sin(angle) * radius, center.y + Mathf.Cos(angle) * radius, z);
        }

        // 斜めに切った区切り 1 つ（外の縁は先へ、内の縁は後ろへずらす）。c0 は始まり、c1 は終わりの色。
        private void AddSegment(Vector3 center, float inner, float outer, float a0, float a1, Color c0, Color c1, float z)
        {
            if (a1 - a0 <= 0.0005f)
            {
                return;
            }
            int baseIndex = _vertices.Count;
            _vertices.Add(Point(center, outer, a0 + SkewRadians, z));
            _vertices.Add(Point(center, outer, a1 + SkewRadians, z));
            _vertices.Add(Point(center, inner, a1 - SkewRadians, z));
            _vertices.Add(Point(center, inner, a0 - SkewRadians, z));
            _colors.Add(c0);
            _colors.Add(c1);
            _colors.Add(c1);
            _colors.Add(c0);
            AddQuad(baseIndex);
        }

        // 細かく区切った輪（終わりの白い輪）。
        private void AddArc(Vector3 center, float inner, float outer, float a0, float a1, Color c0, Color c1, float z, int pieces)
        {
            for (int k = 0; k < pieces; k++)
            {
                float t0 = (float)k / pieces;
                float t1 = (float)(k + 1) / pieces;
                int baseIndex = _vertices.Count;
                _vertices.Add(Point(center, outer, Mathf.Lerp(a0, a1, t0), z));
                _vertices.Add(Point(center, outer, Mathf.Lerp(a0, a1, t1), z));
                _vertices.Add(Point(center, inner, Mathf.Lerp(a0, a1, t1), z));
                _vertices.Add(Point(center, inner, Mathf.Lerp(a0, a1, t0), z));
                _colors.Add(Color.Lerp(c0, c1, t0));
                _colors.Add(Color.Lerp(c0, c1, t1));
                _colors.Add(Color.Lerp(c0, c1, t1));
                _colors.Add(Color.Lerp(c0, c1, t0));
                AddQuad(baseIndex);
            }
        }

        // 光の玉: 真ん中が明るく、外へ向かって透明になる（頂点の色の補間で、やわらかく見える）。
        private void AddGlow(Vector3 at, float radius, Color color, float z)
        {
            int centerIndex = _vertices.Count;
            _vertices.Add(new Vector3(at.x, at.y, z));
            _colors.Add(color);
            var edge = new Color(color.r, color.g, color.b, 0f);
            for (int k = 0; k < GlowPoints; k++)
            {
                float angle = k * Mathf.PI * 2f / GlowPoints;
                _vertices.Add(new Vector3(at.x + Mathf.Cos(angle) * radius, at.y + Mathf.Sin(angle) * radius, z));
                _colors.Add(edge);
            }
            for (int k = 0; k < GlowPoints; k++)
            {
                _triangles.Add(centerIndex);
                _triangles.Add(centerIndex + 1 + (k + 1) % GlowPoints);
                _triangles.Add(centerIndex + 1 + k);
            }
        }

        // 材質（Sprites-Default）は裏面も描くので、向きはそろえなくてよい。
        private void AddQuad(int baseIndex)
        {
            _triangles.Add(baseIndex);
            _triangles.Add(baseIndex + 1);
            _triangles.Add(baseIndex + 2);
            _triangles.Add(baseIndex);
            _triangles.Add(baseIndex + 2);
            _triangles.Add(baseIndex + 3);
        }

        private void Begin()
        {
            _vertices.Clear();
            _colors.Clear();
            _triangles.Clear();
        }

        private void Finish()
        {
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
            _renderer.enabled = true;
        }
    }
}
