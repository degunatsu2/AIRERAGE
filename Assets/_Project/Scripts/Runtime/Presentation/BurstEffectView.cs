using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// 一瞬の光（広がる輪 2 つと光の筋。文字と画面の閃光は任意）。docs/11_combat_plan.md T3-7。
    /// 命中の火花、FATAL の虹色の光と「FATAL!」（ユーザーの要望、2026-10-13）、ワイヤーキャンセルの光に使う。
    /// Play で始め、_duration 秒で消える。見た目の動きは表示の時間で決める（シミュレーションには影響しない）。
    /// </summary>
    public sealed class BurstEffectView : MonoBehaviour
    {
        private const int CirclePoints = 48;
        private const float Z = -0.8f;

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

        private Vector3 _center;
        private float _startedAt = -10f;
        private GUIStyle _style;
        private Gradient _gradient;

        /// <summary>今光っているか。</summary>
        public bool IsPlaying => Time.time - _startedAt < _duration;

        public string Label => _label;

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

        private void Awake()
        {
            SetEnabled(false);
        }

        /// <summary>center で光らせ始める（光っている途中なら、そこから始め直す）。</summary>
        public void Play(Vector3 center)
        {
            _center = center;
            _startedAt = Time.time;
        }

        private void Update()
        {
            float progress = (Time.time - _startedAt) / _duration;
            if (progress < 0f || progress >= 1f)
            {
                SetEnabled(false);
                return;
            }
            float ease = 1f - (1f - progress) * (1f - progress);
            float alpha = 1f - progress;
            float hueShift = Time.time * 1.5f;

            DrawCircle(_ring, Mathf.Lerp(_startRadius, _endRadius, ease), Mathf.Lerp(_width * 2f, _width * 0.3f, progress), alpha, hueShift);
            DrawCircle(_innerRing, Mathf.Lerp(_startRadius * 0.5f, _endRadius * 0.65f, ease), _width * 0.8f, alpha * 0.8f, hueShift + 0.5f);
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

        private void DrawCircle(LineRenderer line, float radius, float width, float alpha, float hueShift)
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
                line.SetPosition(i, _center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, Z));
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
        }

        private void OnGUI()
        {
            if (!IsPlaying)
            {
                return;
            }
            float progress = (Time.time - _startedAt) / _duration;
            if (_screenFlash > 0f && progress < 0.2f)
            {
                Color flash = _rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time * 3f, 1f), 0.3f, 1f) : _color;
                flash.a = _screenFlash * (1f - progress / 0.2f);
                Color before = GUI.color;
                GUI.color = flash;
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = before;
            }
            if (string.IsNullOrEmpty(_label) || _camera == null)
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
                _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            // 最初に大きく出て、少し縮みながら消える。
            float pop = progress < 0.15f ? Mathf.Lerp(1.8f, 1.2f, progress / 0.15f) : Mathf.Lerp(1.2f, 1f, (progress - 0.15f) / 0.85f);
            _style.fontSize = Mathf.RoundToInt(_fontSize * pop);
            var rect = new Rect(screen.x - 250f, Screen.height - screen.y - 50f, 500f, 100f);
            float alpha = progress < 0.7f ? 1f : 1f - (progress - 0.7f) / 0.3f;

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f * alpha);
            GUI.Label(new Rect(rect.x + 3f, rect.y + 3f, rect.width, rect.height), _label, _style);
            Color text = _rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time * 2f, 1f), 0.8f, 1f) : _color;
            text.a = alpha;
            GUI.color = text;
            GUI.Label(rect, _label, _style);
            GUI.color = previous;
        }
    }
}
