using System.Collections.Generic;
using Airerage.Core.Wire;
using Airerage.Runtime.Simulation;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ワイヤーの照準のインジケーター（ユーザーの選択、2026-11-02: 案 C の照準器と文字を中心に、案 B の波紋とグラデーション、案 D の四角い
    /// 点線。たるみはない）。
    /// - 導線: キャラクターから終点までの、四角い点線（暗い縁つき）。点は狙う先へ流れ、先へ行くほど少し大きく、根元の色（ローカル対戦は
    ///   プレイヤーの色）から結果の色へ変わる。明るさの波が先へ走る。
    /// - 終点の照準器: 円と 4 本の目盛り（接続できるときは目盛りが回る）。横に結果の文字（LOCK・BLOCK・OUT）と距離（m）の札。
    /// - 波紋: 接続できるときは、刺さる所に輪が広がる。
    /// - 射程: 全周は薄い点線の円にして、照準の向きの前後 25° だけを明るい弧にする（根元の色）。
    /// 結果の色は 接続できる: 緑 / 接続できない物に当たる: 赤 / 届く接続先がない: 灰色。すぐに射出できない間（回収中・クールダウン中）は薄くする。
    /// 線と印の太さ・文字の大きさは画面の大きさ（ピクセル）で決める（カメラが引いた全画面でも細くならない）。
    /// 予測は WireTargeting.PredictShot（射出と同じ計算）。形は毎フレーム、頂点の色つきのメッシュに作り直す。状態を読むだけで、書き換えない。
    /// 照準器はマウスカーソル（ゲームパッドは右スティックの倒し具合）の所に出し、途中で物に当たるならその点に出す。そのときと、カーソルが
    /// 射程の外のときは、カーソルの所に小さく薄い照準器（カーソルの印【仮】）と、照準器からそこまでの薄い四角い点線を出す（2026-11-12）。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class AimIndicatorView : MonoBehaviour
    {
        // 画面のピクセルでの大きさ（案の画面と同じ。1920x1080 の画面での値）。
        private const float LineWidthPx = 2.5f;
        private const float OutlinePx = 2f;
        private const float DashPeriodPx = 20f;
        private const float DashSmallPx = 6f;
        private const float DashLargePx = 10f;
        private const float DashSpeedPx = 40f;
        private const float ReticleRadiusPx = 20f;
        private const float ReticleWidthPx = 3f;
        private const float TickInnerPx = 6f;
        private const float TickOuterPx = 9f;
        private const float RippleStartPx = 8f;
        private const float RippleEndPx = 38f;
        // カーソルの印【仮】（小さく薄い照準器と、照準器からの薄い点線）。照準器からこれより離れたら出す。
        private const float CursorRadiusPx = 12f;
        private const float CursorWidthPx = 2f;
        private const float CursorTickPx = 5f;
        private const float CursorDashPx = 4f;
        private const float CursorDashPeriodPx = 14f;
        private const float CursorAlpha = 0.6f;
        private const float CursorApartPx = 24f;
        private const float RangeArcWidthPx = 5f;
        private const float RangeRingWidthPx = 2f;
        private const float RangeArcDegrees = 25f;
        private const int RangeSegments = 96;
        private const int ArcSegments = 20;
        private const int ReticleSegments = 40;
        private const float Z = -0.8f;

        private static readonly Color Outline = new Color(0f, 0f, 0f, 0.55f);

        [Tooltip("太さを画面の大きさに合わせるためのカメラ（なければ Camera.main）")]
        [SerializeField] private Camera _camera;

        [Header("色")]
        [SerializeField] private Color _attachColor = new Color(0.15f, 0.95f, 0.4f, 0.95f);
        [SerializeField] private Color _blockedColor = new Color(1f, 0.25f, 0.25f, 0.95f);
        [SerializeField] private Color _outOfRangeColor = new Color(0.72f, 0.74f, 0.78f, 0.85f);
        [Tooltip("射程の弧と導線の根元の色（ローカル対戦はプレイヤーの色。ほかはワイヤーの紫）")]
        [SerializeField] private Color _baseColor = new Color(0.7f, 0.43f, 1f, 0.95f);
        [Tooltip("すぐに射出できないとき（回収中・クールダウン中）の不透明度の倍率")]
        [SerializeField, Range(0f, 1f)] private float _notReadyAlphaScale = 0.35f;

        [Header("形")]
        [Tooltip("導線を、キャラクターの中心からこの距離だけ離して描き始める（m）")]
        [SerializeField] private float _lineStartOffset = 0.6f;
        [Tooltip("画面の高さがこのピクセル数のとき、上の大きさのとおりに描く（画面が小さければ縮める）")]
        [SerializeField] private float _referenceScreenHeight = 1080f;

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _triangles = new List<int>();
        private Mesh _mesh;
        private GUIStyle _label;
        private string _labelText;
        private Color _labelColor;
        private float _alphaScale = 1f;

        /// <summary>照準（導線と照準器）を出しているか（テスト用）。</summary>
        public bool Aiming { get; private set; }

        /// <summary>射程の弧を出しているか（隠れている間は出さない。テスト用）。</summary>
        public bool RangeShown { get; private set; }

        /// <summary>射程の円の中心と半径（m。テスト用）。</summary>
        public Vector3 Origin { get; private set; }
        public float RangeRadius { get; private set; }

        /// <summary>照準器の中心（予測の終点。テスト用）。</summary>
        public Vector3 Target { get; private set; }

        /// <summary>結果の文字（LOCK・BLOCK・OUT。照準を出していなければ null。テスト用）。</summary>
        public string ResultLabel { get; private set; }

        /// <summary>導線の四角の数と、波紋を出しているか（テスト用）。</summary>
        public int DashCount { get; private set; }
        public bool RippleShown { get; private set; }

        /// <summary>カーソルの印を出しているか（照準器がカーソルと離れているとき）と、その位置（テスト用）。</summary>
        public bool CursorShown { get; private set; }
        public Vector3 CursorPoint { get; private set; }

        /// <summary>1 ピクセルが世界の何 m か（キャラクターの深さで。テスト用）。</summary>
        public float MetersPerPixel { get; private set; }

        /// <summary>今の結果の色（不透明度の倍率をかけた後。テスト用）。</summary>
        public Color ResultColor { get; private set; }

        /// <summary>射程の弧と導線の根元の色（ローカル対戦でプレイヤーの色にする）。</summary>
        public Color BaseColor
        {
            get => _baseColor;
            set => _baseColor = value;
        }

        /// <summary>
        /// カーソルの印を出す位置を決める（照準の指定があるときだけ）。マウスで照準していればマウスカーソルの所（射程の外でもそのまま）、
        /// ゲームパッドの右スティックなら、倒し具合で決めた届く先（origin から照準の方向へ）。
        /// </summary>
        public static bool TryGetCursor(Airerage.Runtime.Input.InputSystemAdapter input, in Airerage.Core.Input.PlayerInput shown,
            in Airerage.Core.Input.InputFrame frame, Vector3 origin, WireTuning wire, out Vector3 cursor)
        {
            if (input != null && input.TryGetPointer(out System.Numerics.Vector3 pointer))
            {
                cursor = pointer.ToUnity();
                return true;
            }
            if (shown.HasAim && shown.AimReach != 0 && frame.Aim.LengthSquared() > 1e-6f)
            {
                System.Numerics.Vector2 unit = System.Numerics.Vector2.Normalize(frame.Aim);
                float distance = WireTargeting.AimDistance(wire, frame.Reach);
                cursor = origin + new Vector3(unit.X, unit.Y, 0f) * distance;
                return true;
            }
            cursor = default;
            return false;
        }

        private void Awake()
        {
            _mesh = new Mesh { name = "AimIndicatorMesh" };
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
            }
        }

        private void OnDisable()
        {
            ResultLabel = null;
            _labelText = null;
        }

        /// <summary>
        /// 照準を描く。hidden なら（透明の間。THIEF の E）すべて隠す。hasCursor なら cursor はマウスカーソル（ゲームパッドは右スティックで
        /// 決めた届く先）の位置で、照準器と離れていればカーソルの印を出す。
        /// </summary>
        public void Render(Vector3 origin, in WireState wire, in WireShotPreview preview, float maxLength, bool hidden = false,
            bool hasCursor = false, Vector3 cursor = default)
        {
            CursorShown = false;
            _vertices.Clear();
            _colors.Clear();
            _triangles.Clear();
            Origin = origin;
            RangeRadius = maxLength;
            RangeShown = !hidden;
            DashCount = 0;
            RippleShown = false;
            ResultLabel = null;
            _labelText = null;
            if (hidden)
            {
                Aiming = false;
                Apply();
                return;
            }
            _alphaScale = wire.Phase == WirePhase.Ready ? 1f : _notReadyAlphaScale;
            float px = MetersPerPixelAt(origin);
            MetersPerPixel = px;
            float time = Time.time;

            Vector3 direction = preview.Aim.Direction.ToUnity();
            Vector3 flat = origin;
            flat.z = Z;

            // フックが飛んでいる間と接続中は、照準を出さない（射出できないため）。射程の弧は出す。
            Aiming = wire.Phase != WirePhase.Firing && wire.Phase != WirePhase.Attached;
            Color result = preview.Result == WireShotResult.Attach ? _attachColor
                : preview.Result == WireShotResult.HitNonAttachable ? _blockedColor
                : _outOfRangeColor;
            ResultColor = Scaled(result);

            // カーソルの印: 照準器（途中の物・射程の端）とカーソルが離れているときだけ。
            Vector3 end0 = preview.EndPoint.ToUnity();
            end0.z = Z;
            cursor.z = Z;
            bool showCursor = Aiming && hasCursor && Vector3.Distance(end0, cursor) > CursorApartPx * px;

            // 1 回目は暗い縁、2 回目は色（後から描いたものが上に重なる）。
            for (int pass = 0; pass < 2; pass++)
            {
                bool outline = pass == 0;
                DrawRange(flat, maxLength, direction, px, outline);
                if (showCursor)
                {
                    DrawCursor(end0, cursor, px, outline);
                }
                if (Aiming)
                {
                    DrawAim(flat, direction, preview, result, px, time, outline);
                }
            }
            if (showCursor)
            {
                CursorShown = true;
                CursorPoint = cursor;
            }
            if (Aiming)
            {
                Vector3 end = preview.EndPoint.ToUnity();
                end.z = Z;
                Target = end;
                float distance = Vector3.Distance(new Vector3(origin.x, origin.y, 0f), new Vector3(end.x, end.y, 0f));
                ResultLabel = preview.Result == WireShotResult.Attach ? "LOCK" : preview.Result == WireShotResult.HitNonAttachable ? "BLOCK" : "OUT";
                _labelText = $"{ResultLabel}  {distance:0.0}m";
                _labelColor = ResultColor;
            }
            Apply();
        }

        // 射程: 薄い点線の全周と、照準の向きの前後 RangeArcDegrees の明るい弧。
        private void DrawRange(Vector3 center, float radius, Vector3 direction, float px, bool outline)
        {
            Color baseColor = Scaled(_baseColor);
            if (!outline)
            {
                // 全周は点線（1 区切りおき）で薄く。
                Color faint = baseColor;
                faint.a *= 0.22f;
                for (int i = 0; i < RangeSegments; i += 2)
                {
                    float a0 = i * Mathf.PI * 2f / RangeSegments;
                    float a1 = (i + 1) * Mathf.PI * 2f / RangeSegments;
                    AddArc(center, radius, RangeRingWidthPx * px, a0, a1, 1, faint);
                }
            }
            float aim = Mathf.Atan2(direction.y, direction.x);
            float span = RangeArcDegrees * Mathf.Deg2Rad;
            float width = RangeArcWidthPx * px;
            if (outline)
            {
                AddArc(center, radius, width + OutlinePx * 2f * px, aim - span, aim + span, ArcSegments, Outline * new Color(1f, 1f, 1f, _alphaScale));
            }
            else
            {
                AddArc(center, radius, width, aim - span, aim + span, ArcSegments, baseColor);
            }
        }

        // カーソルの印【仮】: 照準器から薄い四角い点線を伸ばし、カーソルの所に小さく薄い照準器（輪と 4 本の目盛り。回らない）。
        private void DrawCursor(Vector3 end, Vector3 cursor, float px, bool outline)
        {
            Vector3 along = cursor - end;
            float span = along.magnitude;
            Vector3 unit = span > 1e-4f ? along / span : Vector3.right;
            Color color = _baseColor;
            color.a *= CursorAlpha;
            color = Scaled(color);
            Color outlineColor = Outline * new Color(1f, 1f, 1f, _alphaScale * CursorAlpha);
            float period = CursorDashPeriodPx * px;
            float from = (ReticleRadiusPx + 6f) * px;
            float to = span - (CursorRadiusPx + 4f) * px;
            for (float d = from; d < to; d += period)
            {
                float size = CursorDashPx * px;
                Color dash = color;
                dash.a *= 0.6f;
                AddSquare(end + unit * d, unit, outline ? size + OutlinePx * 2f * px : size, outline ? outlineColor : dash);
            }
            float radius = CursorRadiusPx * px;
            float width = (outline ? CursorWidthPx + OutlinePx * 2f : CursorWidthPx) * px;
            AddArc(cursor, radius, width, 0f, Mathf.PI * 2f, ReticleSegments, outline ? outlineColor : color);
            for (int k = 0; k < 4; k++)
            {
                float angle = k * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Vector3 a = cursor + dir * (radius - CursorTickPx * 0.5f * px);
                Vector3 b = cursor + dir * (radius + CursorTickPx * px);
                AddSegment(outline ? a - dir * OutlinePx * px : a, outline ? b + dir * OutlinePx * px : b, width, outline ? outlineColor : color);
            }
        }

        // 導線（四角い点線）・照準器・波紋。
        private void DrawAim(Vector3 origin, Vector3 direction, in WireShotPreview preview, Color result, float px, float time, bool outline)
        {
            Vector3 end = preview.EndPoint.ToUnity();
            end.z = Z;
            float length = Vector3.Distance(origin, end);
            Vector3 start = origin + direction * Mathf.Min(_lineStartOffset, length);
            Vector3 along = end - start;
            float span = along.magnitude;
            Vector3 unit = span > 1e-4f ? along / span : direction;
            Color outlineColor = Outline * new Color(1f, 1f, 1f, _alphaScale);

            // 四角い点線: 流れる位置から、DashPeriodPx ごとに 1 つ。先へ行くほど大きく、根元の色から結果の色へ。明るさの波が先へ走る。
            float period = DashPeriodPx * px;
            float offset = Mathf.Repeat(time * DashSpeedPx * px, period);
            float wave = Mathf.Repeat(time * 1.6f, 1.4f);
            int dashes = 0;
            for (float d = offset; d < span - ReticleRadiusPx * px * 0.6f; d += period)
            {
                float f = span > 0f ? d / span : 0f;
                float bright = Mathf.Max(0f, 1f - Mathf.Abs(f - wave) * 4f);
                float size = (Mathf.Lerp(DashSmallPx, DashLargePx, f) + bright * 2f) * px;
                Vector3 center = start + unit * d;
                if (outline)
                {
                    AddSquare(center, unit, size + OutlinePx * 2f * px, outlineColor);
                }
                else
                {
                    Color color = Color.Lerp(_baseColor, result, f);
                    color = Color.Lerp(color, Color.white, bright * 0.45f);
                    color.a = Mathf.Lerp(0.7f, 1f, bright) * result.a;
                    AddSquare(center, unit, size, Scaled(color));
                }
                dashes++;
            }
            if (!outline)
            {
                DashCount = dashes;
            }

            // 照準器: 円と 4 本の目盛り（接続できるときは回る）。
            float radius = ReticleRadiusPx * px;
            float width = ReticleWidthPx * px;
            float spin = preview.Result == WireShotResult.Attach ? time * 1.2f : 0f;
            Color ring = outline ? outlineColor : Scaled(result);
            float drawWidth = outline ? width + OutlinePx * 2f * px : width;
            AddArc(end, radius, drawWidth, 0f, Mathf.PI * 2f, ReticleSegments, ring);
            for (int k = 0; k < 4; k++)
            {
                float angle = spin + k * Mathf.PI * 0.5f;
                var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Vector3 from = end + dir * (radius - TickInnerPx * px);
                Vector3 to = end + dir * (radius + TickOuterPx * px);
                AddSegment(outline ? from - dir * OutlinePx * px : from, outline ? to + dir * OutlinePx * px : to, drawWidth, ring);
            }

            // 波紋（接続できるときだけ。3 つの輪が広がって消える）。
            if (!outline && preview.Result == WireShotResult.Attach)
            {
                RippleShown = true;
                for (int k = 0; k < 3; k++)
                {
                    float p = Mathf.Repeat(time * 0.9f + k / 3f, 1f);
                    Color color = result;
                    color.a = (1f - p) * result.a;
                    AddArc(end, Mathf.Lerp(RippleStartPx, RippleEndPx, p) * px, width, 0f, Mathf.PI * 2f, ReticleSegments, Scaled(color));
                }
            }
        }

        private void OnGUI()
        {
            if (_labelText == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            Camera camera = _camera != null ? _camera : Camera.main;
            if (camera == null)
            {
                return;
            }
            Vector3 screen = camera.WorldToScreenPoint(Target);
            if (screen.z <= 0f)
            {
                return;
            }
            float scale = ScreenScale();
            if (_label == null)
            {
                _label = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false });
            }
            _label.fontSize = Mathf.Max(10, Mathf.RoundToInt(17f * scale));
            Vector2 size = _label.CalcSize(new GUIContent(_labelText));
            float x = screen.x + (ReticleRadiusPx + 16f) * scale;
            float y = Screen.height - screen.y - (ReticleRadiusPx + 4f) * scale;
            // 画面の右の端からはみ出すなら、照準器の左に出す。
            if (x + size.x + 8f * scale > Screen.width)
            {
                x = screen.x - (ReticleRadiusPx + 16f) * scale - size.x;
            }
            var box = new Rect(x - 8f * scale, y - 15f * scale, size.x + 16f * scale, 30f * scale);
            Color previous = GUI.color;
            GUI.color = new Color(0.03f, 0.05f, 0.08f, 0.78f * _labelColor.a);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = _labelColor;
            GUI.DrawTexture(new Rect(box.x, box.yMax - 2f * scale, box.width, 2f * scale), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, _labelColor.a);
            GUI.Label(new Rect(x, box.y, size.x + 2f, box.height), _labelText, _label);
            GUI.color = previous;
        }

        // 画面の大きさの倍率（基準の高さのときに 1）。
        private float ScreenScale()
        {
            return Mathf.Max(0.5f, Screen.height / Mathf.Max(1f, _referenceScreenHeight));
        }

        // 位置 world の深さで、画面の 1 ピクセル（基準の画面の大きさ）が世界の何 m か。
        private float MetersPerPixelAt(Vector3 world)
        {
            Camera camera = _camera != null ? _camera : Camera.main;
            if (camera == null)
            {
                return 0.035f;
            }
            float height;
            if (camera.orthographic)
            {
                height = camera.orthographicSize * 2f;
            }
            else
            {
                float depth = Mathf.Max(0.1f, Vector3.Dot(world - camera.transform.position, camera.transform.forward));
                height = 2f * depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            }
            // 画面の高さが基準より小さくても、基準のピクセル数で描く（同じ割合に見える）。
            return height / Mathf.Max(1f, _referenceScreenHeight);
        }

        private Color Scaled(Color color)
        {
            color.a *= _alphaScale;
            return color;
        }

        // 中心 center・半径 radius・太さ width の弧（a0 から a1 まで。ラジアン）。
        private void AddArc(Vector3 center, float radius, float width, float a0, float a1, int segments, Color color)
        {
            float inner = Mathf.Max(0f, radius - width * 0.5f);
            float outer = radius + width * 0.5f;
            int first = _vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(a0, a1, i / (float)segments);
                var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                _vertices.Add(center + dir * inner);
                _vertices.Add(center + dir * outer);
                _colors.Add(color);
                _colors.Add(color);
            }
            for (int i = 0; i < segments; i++)
            {
                int v = first + i * 2;
                _triangles.Add(v);
                _triangles.Add(v + 1);
                _triangles.Add(v + 3);
                _triangles.Add(v);
                _triangles.Add(v + 3);
                _triangles.Add(v + 2);
            }
        }

        // 中心 center・一辺 size の四角（辺を unit の向きにそろえる）。
        private void AddSquare(Vector3 center, Vector3 unit, float size, Color color)
        {
            Vector3 a = unit * (size * 0.5f);
            Vector3 b = new Vector3(-unit.y, unit.x, 0f) * (size * 0.5f);
            AddQuad(center - a - b, center + a - b, center + a + b, center - a + b, color);
        }

        // from から to までの、太さ width の線。
        private void AddSegment(Vector3 from, Vector3 to, float width, Color color)
        {
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < 1e-5f)
            {
                return;
            }
            Vector3 side = new Vector3(-along.y, along.x, 0f) / length * (width * 0.5f);
            AddQuad(from - side, to - side, to + side, from + side, color);
        }

        private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            int first = _vertices.Count;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _vertices.Add(d);
            for (int i = 0; i < 4; i++)
            {
                _colors.Add(color);
            }
            _triangles.Add(first);
            _triangles.Add(first + 1);
            _triangles.Add(first + 2);
            _triangles.Add(first);
            _triangles.Add(first + 2);
            _triangles.Add(first + 3);
        }

        private void Apply()
        {
            if (_mesh == null)
            {
                return;
            }
            // 頂点は世界の座標で作ったので、この物の座標に直す（ふつうは原点に置くので変わらない）。
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            for (int i = 0; i < _vertices.Count; i++)
            {
                _vertices[i] = toLocal.MultiplyPoint3x4(_vertices[i]);
            }
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
        }
    }
}
