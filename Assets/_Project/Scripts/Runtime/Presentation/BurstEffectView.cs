using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 一瞬の光（広がる輪 2 つと光の筋。文字と画面の閃光は任意）。docs/11_combat_plan.md T3-7。
    /// 命中の火花、FATAL の虹色の光と「FATAL!」（ユーザーの要望、2026-10-13）、ワイヤーキャンセルの光に使う。
    /// Play で始め、_duration 秒で消える。見た目の動きは表示の時間で決める（シミュレーションには影響しない）。
    /// 2026-11-24 の決定（D-101）で、次の 3 つの見せ方を足した:
    /// - 回る星（S5。_stars があるとき）: 4 点の光の星が 90° 回りながら縮み、少し遅れた残像が 3 枚ついていく。細い輪が 1 本広がる。
    ///   光はすべてキャラクターより奥に描く（ワイヤーキャンセル・技の発動）。輪と光の筋は描かない。
    /// - 色が重なって光る文字（J7。_labelLook が Prism）: 赤・緑・青に分かれた 3 枚の文字が左右から寄って 1 枚に重なり、白く光ってから
    ///   虹色のグラデーションが流れる。後ろに虹色に光るにじみ（J9 のネオンの発光）を影として落とす。光の帯は通さない（JACKPOT）。
    /// - 空気の塊の衝撃波（F6。_shockwave）: 中心から背景をゆがませる透明な輪（ScreenShockwaves）と、輪の内側の色収差（FATAL）。
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

        /// <summary>回る星と、その輪を描く奥行き（キャラクターより奥。m）。</summary>
        public const float BehindZ = 0.7f;

        /// <summary>回る星の残像の遅れ（秒）と、星が回る角度（度）。</summary>
        public const float GhostLag = 0.035f;
        public const float StarSpinDegrees = 90f;

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
        [Tooltip("広がる輪と光の筋を描く（回る星・文字だけの見せ方では描かない）")]
        [SerializeField] private bool _drawRings = true;
        [Tooltip("回る星（S5。0 番目が本体、残りが残像。Airerage/GlowStar の板）と、星の半径（m）")]
        [SerializeField] private MeshRenderer[] _stars = new MeshRenderer[0];
        [SerializeField] private float _starRadius = 2.2f;
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

        /// <summary>今光っているか。</summary>
        public bool IsPlaying => Time.time - _startedAt < Mathf.Max(_duration, LabelDuration);

        public string Label => _label;

        /// <summary>回る星の見せ方か（テスト用）と、その本体の板。</summary>
        public bool HasSpinningStar => _stars.Length > 0;

        public MeshRenderer MainStar => _stars.Length > 0 ? _stars[0] : null;

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

        /// <summary>回る星の見せ方にする（S5。シーンのビルダー用。stars は 0 番目が本体、残りが残像）。輪と光の筋は描かない。</summary>
        public void ConfigureSpinningStar(MeshRenderer[] stars, float radius)
        {
            _stars = stars ?? new MeshRenderer[0];
            _starRadius = radius;
            _drawRings = false;
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
            if (_stars.Length > 0)
            {
                RenderSpinningStar(age);
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

            DrawCircle(_ring, Mathf.Lerp(_startRadius, _endRadius, ease), Mathf.Lerp(_width * 2f, _width * 0.3f, progress), alpha, hueShift, Z);
            DrawCircle(_innerRing, Mathf.Lerp(_startRadius * 0.5f, _endRadius * 0.65f, ease), _width * 0.8f, alpha * 0.8f, hueShift + 0.5f, Z);
            if (_rays != null && _rayCount > 0)
            {
                float inner = Mathf.Lerp(_startRadius, _endRadius * 0.5f, ease);
                float outer = inner + _rayLength * (1f - 0.5f * progress);
                float rotation = Time.time * 1.2f;
                _rays.positionCount = _rayCount * 2;
                _rays.loop = true;
                for (int i = 0; i < _rayCount * 2; i++)
                {
                    float angle = rotation + Mathf.PI * i / _rayCount;
                    float radius = i % 2 == 0 ? outer : inner;
                    _rays.SetPosition(i, _center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, Z));
                }
                _rays.widthMultiplier = Mathf.Lerp(_width * 1.2f, _width * 0.2f, progress);
                Paint(_rays, alpha, hueShift + 0.25f);
                _rays.enabled = true;
            }
        }

        // S5: 4 点の星が 90° 回りながら縮み（出始めは少し行きすぎて広がる）、少し遅れた残像が 3 枚ついていく。細い輪が 1 本広がって消える。
        // 星も輪もキャラクターより奥（BehindZ）に描く。
        private void RenderSpinningStar(float age)
        {
            _block = _block ?? new MaterialPropertyBlock();
            float life = _duration * 0.72f;
            Color baseColor = _rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time * 1.2f, 1f), 0.75f, 1f) : _color;
            for (int i = 0; i < _stars.Length; i++)
            {
                MeshRenderer star = _stars[i];
                if (star == null)
                {
                    continue;
                }
                float t = age - i * GhostLag;
                if (t < 0f)
                {
                    star.enabled = false;
                    continue;
                }
                float k = Mathf.Clamp01(t / life);
                float pop = OutBack(Mathf.Clamp01(t / 0.12f));
                float size = (1f - k * k * k) * pop;
                if (size <= 0.001f)
                {
                    star.enabled = false;
                    continue;
                }
                float spin = (1f - Mathf.Pow(1f - k, 3f)) * StarSpinDegrees;
                Transform tr = star.transform;
                tr.position = new Vector3(_center.x, _center.y, _center.z + BehindZ + i * 0.02f);
                tr.rotation = Quaternion.Euler(0f, 0f, spin);
                tr.localScale = Vector3.one * (_starRadius * 2f * size);
                Color color = i == 0 ? Color.Lerp(baseColor, Color.white, 0.55f) : baseColor;
                color.a = i == 0 ? 1f - 0.3f * k : Mathf.Max(0f, 0.45f - 0.1f * i) * (1f - k * 0.5f);
                star.GetPropertyBlock(_block);
                _block.SetColor(ColorId, color);
                star.SetPropertyBlock(_block);
                star.enabled = true;
            }
            float ringK = Mathf.Clamp01(age / (_duration * 0.65f));
            float ringEase = 1f - Mathf.Pow(1f - ringK, 3f);
            float ringScale = _starRadius / 2.2f;
            DrawCircle(_ring, Mathf.Lerp(0.9f, 2.6f, ringEase) * ringScale, 0.02f + 0.07f * (1f - ringK), 1f - ringK, Time.time * 1.5f, _center.z + BehindZ - _center.z);
            if (_innerRing != null)
            {
                _innerRing.enabled = false;
            }
            if (_rays != null)
            {
                _rays.enabled = false;
            }
        }

        private static float OutBack(float k)
        {
            const float c = 1.9f;
            const float c3 = c + 1f;
            float m = k - 1f;
            return 1f + c3 * m * m * m + c * m * m;
        }

        private void DrawCircle(LineRenderer line, float radius, float width, float alpha, float hueShift, float z)
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
            Paint(line, alpha, hueShift);
            line.enabled = true;
        }

        // 虹色なら、線に沿って色相を一周させる（時間で回す）。そうでなければ単色。
        private void Paint(LineRenderer line, float alpha, float hueShift)
        {
            _gradient = _gradient ?? new Gradient();
            if (!_rainbow)
            {
                Color color = _color;
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
                colors[i] = new GradientColorKey(Color.HSVToRGB(Mathf.Repeat(t + hueShift, 1f), 0.85f, 1f), t);
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
            foreach (MeshRenderer star in _stars)
            {
                if (star != null)
                {
                    star.enabled = enabled;
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
            // 暗い縁取り（読みやすさ）。
            for (int tap = 0; tap < 8; tap++)
            {
                float angle = tap * Mathf.PI * 0.25f;
                var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 3f;
                DrawWord(center + offset, i => new Color(0.1f, 0.06f, 0.18f, 0.9f * fade));
            }
            // 本体: 重なった瞬間は白く光り、虹色へ。文字ごとに色相をずらして流す。
            DrawWord(center, i =>
            {
                Color rainbow = Color.HSVToRGB(Mathf.Repeat(flow - i * 0.08f, 1f), 0.7f, 1f);
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
