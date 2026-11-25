using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 一瞬の光（広がる輪 2 つと光の筋。文字と画面の閃光は任意）。docs/11_combat_plan.md T3-7。
    /// 命中の火花、FATAL の虹色の光と「FATAL!」（ユーザーの要望、2026-10-13）、ワイヤーキャンセルの光に使う。
    /// Play で始め、_duration 秒で消える。見た目の動きは表示の時間で決める（シミュレーションには影響しない）。
    /// 2026-11-24 の決定（D-101）で、次の見せ方を足した:
    /// - 色が重なって光る文字（J7。_labelLook が Prism）: 赤・緑・青に分かれた 3 枚の文字が左右から寄って 1 枚に重なり、白く光ってから
    ///   虹色のグラデーションが流れる。後ろに虹色に光るにじみ（J9 のネオンの発光）を影として落とす。光の帯は通さない（JACKPOT）。
    /// - 空気の塊の衝撃波（F6。_shockwave）: 中心から背景をゆがませる透明な輪（ScreenShockwaves）と、輪の内側の色収差（FATAL）。
    /// 2026-11-25 の要望（ワイヤーキャンセル・技・必殺技は回る星 S5 より元の演出が好み。元の演出を精巧に）で、回る星をやめて元の
    /// 輪 2 つと星形の筋に戻し、細部（HasDetail。_core があるとき）を足した: 線は芯が白く縁がやわらかい光の線（Airerage/GlowLine）、
    /// 星形の筋は先ほど細くとがる。半拍遅れて追いかける細い輪、逆向きに回る小さな星形（大きな星の先の間を向く）、星の先のきらめき
    /// （4 点の光が瞬きながら消える）、出始めの芯の閃光（キャラクターより奥）。
    /// </summary>
    public sealed class BurstEffectView : MonoBehaviour
    {
        /// <summary>文字の見せ方。</summary>
        public enum LabelLook
        {
            /// <summary>大きく出て少し縮みながら消える（白く光ってから本来の色へ）。</summary>
            Pop,

            /// <summary>赤・緑・青の 3 枚が寄って重なり、虹色に流れる（J7）。後ろに虹色のにじみの影。</summary>
            Prism,
        }

        private const int CirclePoints = 48;
        private const float Z = -0.8f;

        /// <summary>芯の閃光を描く奥行き（キャラクターより奥。m）。</summary>
        public const float BehindZ = 0.7f;

        /// <summary>追いかける輪の遅れ（_duration に対する割合）と、芯の閃光の長さ（同じ）。</summary>
        public const float EchoLag = 0.18f;
        public const float CoreLife = 0.45f;

        /// <summary>細部のある光の線の太さの倍率（光の線は縁がやわらかいので、元の線と同じに見えるよう太めに）。</summary>
        public const float DetailWidthScale = 1.5f;

        /// <summary>色が重なって光る文字が 1 枚に重なるまで（秒）。</summary>
        public const float PrismMergeSeconds = 0.3f;

        [SerializeField] private LineRenderer _ring;
        [SerializeField] private LineRenderer _innerRing;
        [SerializeField] private LineRenderer _rays;
        [Tooltip("文字の位置を画面に写すカメラ（文字がなければ不要）")]
        [SerializeField] private Camera _camera;
        [Tooltip("中心の上に出す文字（空なら出さない）")]
        [SerializeField] private string _label = string.Empty;
        [SerializeField] private Color _color = Color.white;
        [Tooltip("虹色にする（色は時間と位置で変わる）")]
        [SerializeField] private bool _rainbow;
        [Tooltip("始めの一瞬、画面全体を光らせる強さ（0 なら光らせない）")]
        [SerializeField] private float _screenFlash;
        [SerializeField] private float _duration = 0.25f;
        [SerializeField] private float _startRadius = 0.2f;
        [SerializeField] private float _endRadius = 1.2f;
        [SerializeField] private int _rayCount = 8;
        [SerializeField] private float _rayLength = 1.6f;
        [SerializeField] private float _width = 0.12f;
        [SerializeField] private int _fontSize = 48;
        [Tooltip("広がる輪と光の筋を描く（文字だけの見せ方では描かない）")]
        [SerializeField] private bool _drawRings = true;
        [Header("細部（2026-11-25。なければ元の輪と筋だけ）")]
        [Tooltip("半拍遅れて追いかける細い輪")]
        [SerializeField] private LineRenderer _echoRing;
        [Tooltip("逆向きに回る小さな星形の筋")]
        [SerializeField] private LineRenderer _counterRays;
        [Tooltip("出始めの芯の閃光（Airerage/GlowStar の丸い光の板。キャラクターより奥）")]
        [SerializeField] private MeshRenderer _core;
        [Tooltip("星の先のきらめき（Airerage/GlowStar の 4 点の光の板。筋の数まで使う）")]
        [SerializeField] private MeshRenderer[] _sparkles = new MeshRenderer[0];
        [Tooltip("文字の見せ方と、文字を出す長さ（秒。0 なら _duration）")]
        [SerializeField] private LabelLook _labelLook = LabelLook.Pop;
        [SerializeField] private float _labelSeconds;
        [Tooltip("空気の塊の衝撃波（背景をゆがませる。ScreenShockwaves）を出す、と大きさの倍率")]
        [SerializeField] private bool _shockwave;
        [SerializeField] private float _shockwaveScale = 1f;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private Vector3 _center;
        private float _startedAt = -10f;
        private GUIStyle _style;
        private Gradient _gradient;
        private MaterialPropertyBlock _block;
        private AnimationCurve _taper;
        private int _taperRays;

        /// <summary>今光っているか。</summary>
        public bool IsPlaying => Time.time - _startedAt < Mathf.Max(_duration, LabelDuration);

        public string Label => _label;

        /// <summary>細部（芯の閃光・きらめきなど）があるか（テスト用）と、芯の閃光の板・今出ているきらめきの数。</summary>
        public bool HasDetail => _core != null;

        public MeshRenderer Core => _core;

        public int VisibleSparkles
        {
            get
            {
                int count = 0;
                foreach (MeshRenderer sparkle in _sparkles)
                {
                    if (sparkle != null && sparkle.enabled)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>文字の見せ方・衝撃波を出すか（テスト用）。</summary>
        public LabelLook Look => _labelLook;

        public bool MakesShockwave => _shockwave;

        /// <summary>輪と光の筋を描くか（テスト用）。</summary>
        public bool DrawsRings => _drawRings;

        private float LabelDuration => _labelSeconds > 0f ? _labelSeconds : _duration;

        /// <summary>シーンのビルダー用の設定。</summary>
        public void Configure(Color color, bool rainbow, float duration, float startRadius, float endRadius, int rayCount, float rayLength,
            float width, string label, int fontSize, float screenFlash)
        {
            _color = color;
            _rainbow = rainbow;
            _duration = duration;
            _startRadius = startRadius;
            _endRadius = endRadius;
            _rayCount = rayCount;
            _rayLength = rayLength;
            _width = width;
            _label = label ?? string.Empty;
            _fontSize = fontSize;
            _screenFlash = screenFlash;
        }

        /// <summary>文字の見せ方を決める（シーンのビルダー用）。seconds が 0 なら _duration の間。</summary>
        public void ConfigureLabel(LabelLook look, float seconds)
        {
            _labelLook = look;
            _labelSeconds = seconds;
        }

        /// <summary>輪と光の筋を描くか（シーンのビルダー用）。</summary>
        public void ConfigureRings(bool draw)
        {
            _drawRings = draw;
        }

        /// <summary>空気の塊の衝撃波を出す（F6。シーンのビルダー用）。</summary>
        public void ConfigureShockwave(bool enabled, float scale)
        {
            _shockwave = enabled;
            _shockwaveScale = scale;
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            SetEnabled(false);
        }

        /// <summary>center で光らせ始める（光っている途中なら、そこから始め直す）。</summary>
        public void Play(Vector3 center)
        {
            _center = center;
            _startedAt = Time.time;
            if (_shockwave)
            {
                ScreenShockwaves.Play(center, _shockwaveScale);
            }
        }

        /// <summary>文字を label にして、center で光らせ始める（バンクハイストの枚数など、毎回変わる文字）。</summary>
        public void Play(Vector3 center, string label)
        {
            _label = label ?? string.Empty;
            Play(center);
        }

        private void Update()
        {
            float age = Time.time - _startedAt;
            float progress = age / _duration;
            if (progress < 0f || progress >= 1f)
            {
                SetEnabled(false);
                return;
            }
            if (!_drawRings)
            {
                SetEnabled(false);
                return;
            }
            float ease = 1f - (1f - progress) * (1f - progress);
            float alpha = 1f - progress;
            float hueShift = Time.time * 1.5f;
            // 光の線（細部あり）は縁がやわらかく細く見えるので、元の線と同じ太さに見えるよう太めに描く。
            float width = HasDetail ? _width * DetailWidthScale : _width;

            DrawCircle(_ring, Mathf.Lerp(_startRadius, _endRadius, ease), Mathf.Lerp(width * 2f, width * 0.3f, progress), alpha, hueShift, Z);
            DrawCircle(_innerRing, Mathf.Lerp(_startRadius * 0.5f, _endRadius * 0.65f, ease), width * 0.8f, alpha * 0.8f, hueShift + 0.5f, Z);
            float inner = Mathf.Lerp(_startRadius, _endRadius * 0.5f, ease);
            float outer = inner + _rayLength * (1f - 0.5f * progress);
            float rotation = Time.time * 1.2f;
            if (_rays != null && _rayCount > 0)
            {
                DrawStar(_rays, rotation, inner, outer, Mathf.Lerp(width * 1.2f, width * 0.2f, progress), alpha, hueShift + 0.25f, 0f);
            }
            if (HasDetail)
            {
                RenderDetail(age, progress, alpha, hueShift, rotation, inner, outer);
            }
        }

        // 星形の筋（先と谷を交互に結んだ輪）。細部があるときは先ほど細くとがる（幅の曲線）。whiten は白へ寄せる割合。
        private void DrawStar(LineRenderer line, float rotation, float inner, float outer, float width, float alpha, float hueShift, float whiten)
        {
            int points = _rayCount * 2;
            line.positionCount = points;
            line.loop = true;
            for (int i = 0; i < points; i++)
            {
                float angle = rotation + Mathf.PI * i / _rayCount;
                float radius = i % 2 == 0 ? outer : inner;
                line.SetPosition(i, _center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, Z));
            }
            if (HasDetail)
            {
                line.widthCurve = TaperCurve();
            }
            line.widthMultiplier = width;
            Paint(line, alpha, hueShift, whiten);
            line.enabled = true;
        }

        // 細部: 追いかける輪・逆に回る小さな星形・星の先のきらめき・芯の閃光。
        private void RenderDetail(float age, float progress, float alpha, float hueShift, float rotation, float inner, float outer)
        {
            _block = _block ?? new MaterialPropertyBlock();
            // 半拍遅れて追いかける細い輪（白っぽく、外の輪の少し内側を広がる）。
            float echo = Mathf.Clamp01((age - _duration * EchoLag) / (_duration * (1f - EchoLag)));
            if (_echoRing != null)
            {
                if (echo > 0f)
                {
                    float echoEase = 1f - (1f - echo) * (1f - echo);
                    DrawCircle(_echoRing, Mathf.Lerp(_startRadius, _endRadius * 0.86f, echoEase), _width * 0.35f, (1f - echo) * 0.6f, hueShift + 0.75f, Z, 0.45f);
                }
                else
                {
                    _echoRing.enabled = false;
                }
            }
            // 逆向きに回る小さな星形（先は大きな星の先の間を向く）。
            if (_counterRays != null && _rayCount > 0)
            {
                float smallInner = inner * 0.55f;
                float smallOuter = smallInner + _rayLength * 0.55f * (1f - 0.6f * progress);
                DrawStar(_counterRays, -Time.time * 1.8f + Mathf.PI / _rayCount, smallInner, smallOuter,
                    Mathf.Lerp(_width * 0.7f, _width * 0.15f, progress), alpha * 0.75f, hueShift + 0.5f, 0.45f);
            }
            // 星の先のきらめき（先の向きにそろえた 4 点の光。1 つずつずらして瞬き、外へ少し流れて消える）。
            float sparkleSize = 0.18f + 0.08f * _rayLength;
            for (int i = 0; i < _sparkles.Length; i++)
            {
                MeshRenderer sparkle = _sparkles[i];
                if (sparkle == null)
                {
                    continue;
                }
                if (i >= _rayCount || progress < 0.04f)
                {
                    sparkle.enabled = false;
                    continue;
                }
                float angle = rotation + Mathf.PI * 2f * i / _rayCount;
                float twinkle = 0.55f + 0.45f * Mathf.Sin(age * 40f + i * 1.7f);
                float size = sparkleSize * Mathf.Pow(1f - progress, 0.6f) * twinkle * OutBack(Mathf.Clamp01((progress - 0.04f) / 0.12f));
                if (size <= 0.001f)
                {
                    sparkle.enabled = false;
                    continue;
                }
                float reach = outer * 1.04f + 0.25f * progress;
                Transform tr = sparkle.transform;
                tr.position = _center + new Vector3(Mathf.Cos(angle) * reach, Mathf.Sin(angle) * reach, Z - 0.02f);
                tr.rotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
                tr.localScale = Vector3.one * size;
                Color color = _rainbow ? Color.HSVToRGB(Mathf.Repeat(hueShift + i / (float)_rayCount, 1f), 0.6f, 1f) : Color.Lerp(_color, Color.white, 0.5f);
                color.a = alpha;
                SetQuadColor(sparkle, color);
            }
            // 出始めの芯の閃光（丸い光が一瞬ふくらんで消える。キャラクターより奥）。
            float coreK = age / (_duration * CoreLife);
            if (coreK < 1f)
            {
                float coreSize = Mathf.Max(1.2f, _endRadius * 0.8f) * Mathf.Lerp(0.6f, 1.25f, 1f - (1f - coreK) * (1f - coreK));
                Transform tr = _core.transform;
                tr.position = new Vector3(_center.x, _center.y, _center.z + BehindZ);
                tr.rotation = Quaternion.identity;
                tr.localScale = Vector3.one * coreSize;
                Color color = _rainbow ? Color.HSVToRGB(Mathf.Repeat(hueShift, 1f), 0.35f, 1f) : Color.Lerp(_color, Color.white, 0.6f);
                color.a = (1f - coreK) * (1f - coreK);
                SetQuadColor(_core, color);
            }
            else
            {
                _core.enabled = false;
            }
        }

        private void SetQuadColor(MeshRenderer quad, Color color)
        {
            quad.GetPropertyBlock(_block);
            _block.SetColor(ColorId, color);
            quad.SetPropertyBlock(_block);
            quad.enabled = true;
        }

        // 星形の筋の幅の曲線: 先（偶数の点）で細く、谷で太い。筋の数が変わったときだけ作り直す。
        private AnimationCurve TaperCurve()
        {
            if (_taper != null && _taperRays == _rayCount)
            {
                return _taper;
            }
            int points = _rayCount * 2;
            var keys = new Keyframe[points + 1];
            for (int i = 0; i <= points; i++)
            {
                keys[i] = new Keyframe(i / (float)points, i % 2 == 0 ? 0.3f : 1f);
            }
            _taper = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                SetLinearTangents(_taper, i);
            }
            _taperRays = _rayCount;
            return _taper;
        }

        // キーの間を直線でつなぐ（エディタの AnimationUtility を使わずに、前後のキーへの傾きを入れる）。
        private static void SetLinearTangents(AnimationCurve curve, int index)
        {
            Keyframe key = curve[index];
            if (index > 0)
            {
                Keyframe previous = curve[index - 1];
                key.inTangent = (key.value - previous.value) / (key.time - previous.time);
            }
            if (index < curve.length - 1)
            {
                Keyframe next = curve[index + 1];
                key.outTangent = (next.value - key.value) / (next.time - key.time);
            }
            curve.MoveKey(index, key);
        }

        private static float OutBack(float k)
        {
            const float c = 1.9f;
            const float c3 = c + 1f;
            float m = k - 1f;
            return 1f + c3 * m * m * m + c * m * m;
        }

        private void DrawCircle(LineRenderer line, float radius, float width, float alpha, float hueShift, float z, float whiten = 0f)
        {
            if (line == null)
            {
                return;
            }
            line.positionCount = CirclePoints;
            line.loop = true;
            for (int i = 0; i < CirclePoints; i++)
            {
                float angle = Mathf.PI * 2f * i / CirclePoints;
                line.SetPosition(i, _center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, z));
            }
            line.widthMultiplier = width;
            Paint(line, alpha, hueShift, whiten);
            line.enabled = true;
        }

        // 虹色なら、線に沿って色相を一周させる（時間で回す）。そうでなければ単色。
        private void Paint(LineRenderer line, float alpha, float hueShift, float whiten = 0f)
        {
            _gradient = _gradient ?? new Gradient();
            if (!_rainbow)
            {
                Color color = Color.Lerp(_color, Color.white, whiten);
                color.a *= alpha;
                _gradient.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                    new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(color.a, 1f) });
                line.colorGradient = _gradient;
                return;
            }
            var colors = new GradientColorKey[7];
            for (int i = 0; i < colors.Length; i++)
            {
                float t = i / (colors.Length - 1f);
                colors[i] = new GradientColorKey(Color.HSVToRGB(Mathf.Repeat(t + hueShift, 1f), 0.85f * (1f - whiten), 1f), t);
            }
            _gradient.SetKeys(colors, new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha, 1f) });
            line.colorGradient = _gradient;
        }

        private void SetEnabled(bool enabled)
        {
            if (_ring != null)
            {
                _ring.enabled = enabled;
            }
            if (_innerRing != null)
            {
                _innerRing.enabled = enabled;
            }
            if (_rays != null)
            {
                _rays.enabled = enabled;
            }
            if (_echoRing != null)
            {
                _echoRing.enabled = enabled;
            }
            if (_counterRays != null)
            {
                _counterRays.enabled = enabled;
            }
            if (_core != null)
            {
                _core.enabled = enabled;
            }
            foreach (MeshRenderer sparkle in _sparkles)
            {
                if (sparkle != null)
                {
                    sparkle.enabled = enabled;
                }
            }
        }

        private void OnGUI()
        {
            float age = Time.time - _startedAt;
            if (age < 0f || age >= Mathf.Max(_duration, LabelDuration))
            {
                return;
            }
            float progress = age / _duration;
            if (_screenFlash > 0f && progress < 0.2f)
            {
                Color flash = _rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time * 3f, 1f), 0.3f, 1f) : _color;
                flash.a = _screenFlash * (1f - progress / 0.2f);
                Color before = GUI.color;
                GUI.color = flash;
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = before;
            }
            if (string.IsNullOrEmpty(_label) || _camera == null || age >= LabelDuration)
            {
                return;
            }
            Vector3 screen = _camera.WorldToScreenPoint(_center + new Vector3(0f, 2.2f, 0f));
            if (screen.z <= 0f)
            {
                return;
            }
            if (_style == null)
            {
                _style = TextFlash.Style(_fontSize, FontStyle.Bold, TextAnchor.MiddleCenter);
            }
            if (_labelLook == LabelLook.Prism)
            {
                DrawPrismLabel(new Vector2(screen.x, Screen.height - screen.y), age);
                return;
            }
            float labelProgress = age / LabelDuration;
            // 最初に大きく出て、少し縮みながら消える。
            float pop = labelProgress < 0.15f ? Mathf.Lerp(1.8f, 1.2f, labelProgress / 0.15f) : Mathf.Lerp(1.2f, 1f, (labelProgress - 0.15f) / 0.85f);
            _style.fontSize = Mathf.RoundToInt(_fontSize * pop);
            var rect = new Rect(screen.x - 250f, Screen.height - screen.y - 50f, 500f, 100f);
            float alpha = labelProgress < 0.7f ? 1f : 1f - (labelProgress - 0.7f) / 0.3f;

            Color text = _rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time * 2f, 1f), 0.8f, 1f) : _color;
            text.a = alpha;
            // 出た瞬間は白く光り、すぐに本来の色へ（TextFlash）。
            TextFlash.Draw(rect, _label, _style, TextFlash.Apply(text, age), 0.85f, 3f);
        }

        // J7: 赤・緑・青の 3 枚が左右から寄って重なる → 重なった瞬間に白く光る → 文字ごとに色相をずらした虹色が流れる。後ろに虹色の
        // ネオンのにじみ（J9 の発光）を、右下へずらした影として落とす。最後の 25% で消える。光の帯は通さない（2026-11-24 の要望）。
        private void DrawPrismLabel(Vector2 center, float age)
        {
            float total = LabelDuration;
            float fade = age > total * 0.75f ? 1f - (age - total * 0.75f) / (total * 0.25f) : 1f;
            float height = Screen.height;
            Color before = GUI.color;
            if (age < PrismMergeSeconds)
            {
                // 寄ってくる 3 枚（重なるほど明るく）。
                float k = age / PrismMergeSeconds;
                float ease = 1f - Mathf.Pow(1f - k, 3f);
                float spread = (1f - ease) * 0.07f * height;
                _style.fontSize = Mathf.RoundToInt(_fontSize * 1.15f);
                DrawWord(center + new Vector2(-spread, spread * 0.2f), i => new Color(1f, 0.15f, 0.3f, 0.85f));
                DrawWord(center, i => new Color(0.2f, 1f, 0.45f, 0.85f));
                DrawWord(center + new Vector2(spread, -spread * 0.2f), i => new Color(0.25f, 0.45f, 1f, 0.85f));
                GUI.color = before;
                return;
            }
            float since = age - PrismMergeSeconds;
            float pop = Mathf.Lerp(1.45f, 1f, OutBack(Mathf.Clamp01(since / 0.25f)));
            _style.fontSize = Mathf.RoundToInt(_fontSize * pop);
            float flow = Time.time * 0.6f;
            // 虹色のにじみの影（右下へずらし、周りに何枚も薄く重ねてぼかす。ネオン管のように少し揺らぐ）。
            float flicker = 0.85f + 0.15f * Mathf.Sin(Time.time * 47f);
            var shadow = center + new Vector2(0.006f, 0.009f) * height;
            for (int ring = 0; ring < 2; ring++)
            {
                float radius = (ring == 0 ? 0.014f : 0.007f) * height;
                const int Taps = 12;
                for (int tap = 0; tap < Taps; tap++)
                {
                    float angle = tap * Mathf.PI * 2f / Taps;
                    var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    float tapHue = tap / (float)Taps;
                    DrawWord(shadow + offset, i =>
                    {
                        Color glow = Color.HSVToRGB(Mathf.Repeat(tapHue + flow - i * 0.08f, 1f), 0.9f, 1f);
                        glow.a = (ring == 0 ? 0.09f : 0.14f) * fade * flicker;
                        return glow;
                    });
                }
            }
            // 文字のすぐ周りの発光（2026-11-25 の要望: 文字をもう少しだけ発光させる）: 文字と同じ色相の淡い色を、文字の周りに 2 重の輪で
            // 薄く重ねる（外ほど薄い）。暗い縁取りより外へにじむ。
            float breathe = 0.85f + 0.15f * Mathf.Sin(Time.time * 9f);
            for (int ring = 0; ring < 2; ring++)
            {
                float radius = (ring == 0 ? 0.011f : 0.0055f) * height;
                const int Taps = 12;
                for (int tap = 0; tap < Taps; tap++)
                {
                    float angle = (tap + ring * 0.5f) * Mathf.PI * 2f / Taps;
                    var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    DrawWord(center + offset, i =>
                    {
                        Color glow = Color.HSVToRGB(Mathf.Repeat(flow - i * 0.08f, 1f), 0.45f, 1f);
                        glow.a = (ring == 0 ? 0.07f : 0.12f) * fade * breathe;
                        return glow;
                    });
                }
            }
            // 暗い縁取り（読みやすさ）。
            for (int tap = 0; tap < 8; tap++)
            {
                float angle = tap * Mathf.PI * 0.25f;
                var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 3f;
                DrawWord(center + offset, i => new Color(0.1f, 0.06f, 0.18f, 0.8f * fade));
            }
            // 本体: 重なった瞬間は白く光り、虹色へ。文字ごとに色相をずらして流す。
            DrawWord(center, i =>
            {
                Color rainbow = Color.HSVToRGB(Mathf.Repeat(flow - i * 0.08f, 1f), 0.62f, 1f);
                Color shown = TextFlash.Apply(rainbow, since);
                shown.a = fade;
                return shown;
            });
            GUI.color = before;
        }

        // 文字を 1 文字ずつ、真ん中を center にそろえて並べて描く（色は文字の番号ごと）。
        private void DrawWord(Vector2 center, System.Func<int, Color> colorOf)
        {
            float total = 0f;
            var widths = new float[_label.Length];
            for (int i = 0; i < _label.Length; i++)
            {
                widths[i] = _style.CalcSize(new GUIContent(_label[i].ToString())).x;
                total += widths[i];
            }
            float x = center.x - total * 0.5f;
            float h = _style.fontSize * 1.6f;
            for (int i = 0; i < _label.Length; i++)
            {
                GUI.color = colorOf(i);
                GUI.Label(new Rect(x, center.y - h * 0.5f, widths[i] + 4f, h), _label[i].ToString(), _style);
                x += widths[i];
            }
        }
    }
}
