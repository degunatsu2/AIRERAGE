using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// ワイヤーが外れたことを分かりやすく示す（プレイテスト 2026-10-13 の #7）。外れた位置に×印と「MISS」を出し、
    /// 少しずつ消す（届かなかった、接続できない地形に当たった）。ボタンを離して途中でやめたときは「CANCEL」を出す。
    /// 状態を読むだけで、書き換えない。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class MissIndicatorView : MonoBehaviour
    {
        private const int CrossPoints = 5;

        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private Camera _camera;
        [SerializeField] private Color _missColor = new Color(1f, 0.25f, 0.25f, 0.95f);
        [SerializeField] private Color _cancelColor = new Color(0.35f, 0.35f, 0.4f, 0.9f);
        [Tooltip("表示してから消えるまでの時間（秒）")]
        [SerializeField] private float _duration = 0.8f;
        [Tooltip("×印の大きさ（m、中心から端まで）")]
        [SerializeField] private float _crossSize = 0.45f;
        [SerializeField] private int _fontSize = 28;

        private LineRenderer _cross;
        private GUIStyle _style;
        private float _shownAt = -1f;
        private Color _color;

        /// <summary>今表示しているか。</summary>
        public bool IsShowing => _shownAt >= 0f && Time.time - _shownAt < _duration;

        /// <summary>最後に外れた位置。</summary>
        public Vector3 LastPoint { get; private set; }

        /// <summary>最後に表示した文字（"MISS" か "CANCEL"）。</summary>
        public string LastLabel { get; private set; } = string.Empty;

        private void Awake()
        {
            _cross = GetComponent<LineRenderer>();
            _cross.useWorldSpace = true;
            _cross.positionCount = CrossPoints;
            _cross.enabled = false;
            if (_runner == null || _camera == null)
            {
                Debug.LogError($"{name}: SimulationRunner か Camera が設定されていません", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (_runner != null)
            {
                _runner.PlayerTicked += OnPlayerTicked;
                _runner.PlayerReset += Hide;
            }
        }

        private void OnDisable()
        {
            if (_runner != null)
            {
                _runner.PlayerTicked -= OnPlayerTicked;
                _runner.PlayerReset -= Hide;
            }
        }

        private void OnPlayerTicked(CharacterState state)
        {
            // 外れたティックだけ、回収のフェーズが 0 ティック目になる。
            WireState wire = state.Wire;
            if (wire.Phase != WirePhase.Retracting || wire.PhaseTicks != 0)
            {
                return;
            }
            bool canceled = wire.LastMissReason == WireMissReason.ButtonReleased || wire.LastMissReason == WireMissReason.ButtonToggled
                || wire.LastMissReason == WireMissReason.Interrupted;
            Show(wire.HookPosition.ToUnity(), canceled ? "CANCEL" : "MISS", canceled ? _cancelColor : _missColor);
        }

        private void Show(Vector3 point, string label, Color color)
        {
            LastPoint = point;
            LastLabel = label;
            _color = color;
            _shownAt = Time.time;
            // ×印（2本の対角線を1本の折れ線で描く。戻る部分は1本目と重なる）。
            float s = _crossSize;
            _cross.SetPosition(0, point + new Vector3(-s, -s, 0f));
            _cross.SetPosition(1, point + new Vector3(s, s, 0f));
            _cross.SetPosition(2, point);
            _cross.SetPosition(3, point + new Vector3(-s, s, 0f));
            _cross.SetPosition(4, point + new Vector3(s, -s, 0f));
            _cross.enabled = true;
        }

        private void Hide()
        {
            _shownAt = -1f;
            _cross.enabled = false;
        }

        private void Update()
        {
            if (!IsShowing)
            {
                _cross.enabled = false;
                return;
            }
            Color color = FadedColor();
            _cross.startColor = color;
            _cross.endColor = color;
        }

        private void OnGUI()
        {
            if (!IsShowing)
            {
                return;
            }
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = _fontSize, fontStyle = FontStyle.Bold };
            }
            Vector3 screen = _camera.WorldToScreenPoint(LastPoint + new Vector3(0f, _crossSize + 0.5f, 0f));
            if (screen.z < 0f)
            {
                return;
            }
            _style.normal.textColor = FadedColor();
            var rect = new Rect(screen.x - 100f, Screen.height - screen.y - 20f, 200f, 40f);
            GUI.Label(rect, LastLabel, _style);
        }

        // 後半で消えていく。
        private Color FadedColor()
        {
            float t = (Time.time - _shownAt) / _duration;
            Color color = _color;
            color.a *= t < 0.5f ? 1f : Mathf.Clamp01(2f * (1f - t));
            return color;
        }
    }
}
