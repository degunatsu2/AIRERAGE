using Airerage.Core.Simulation;
using Airerage.Core.Wire;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ワイヤーが外れたことを分かりやすく示す（プレイテスト 2026-10-13 の #7）。外れた位置に×印と「MISS」を出し、
    /// 少しずつ消す（届かなかった）。ボタンを離して途中でやめたときは「CANCEL」を出す。接続できない物に当たったときは、
    /// 撃った側へ跳ね返る青い火花と「BLOCK!」を出す（文字は少し跳ねる。ユーザーの要望、2026-10-21）。
    /// 自分のキャラクター（ISimulationFeed.LocalCharacter）だけを見る。トレーニング・検証用のシーン（SimulationRunner）と、ネットワーク対戦
    /// （OnlineRunner。ユーザーの要望、2026-10-21）で使う。ローカル対戦では使わない。巻き戻しがあっても耐えるように、ティックではなく
    /// 画面のフレームごとに、回収の段階に入った瞬間を前のフレームと比べて見つける。
    /// 状態を読むだけで、書き換えない。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class MissIndicatorView : MonoBehaviour
    {
        private const int CrossPoints = 5;

        // BLOCK! の火花（撃った側へ扇形に広がる線の数と、広がる角度・長さ・広がりきるまでの秒数）。
        private const int SparkRays = 6;
        private const float SparkSpreadDeg = 150f;
        private const float SparkLength = 0.95f;
        private const float SparkSeconds = 0.18f;

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [SerializeField] private Camera _camera;
        [SerializeField] private Color _missColor = new Color(1f, 0.25f, 0.25f, 0.95f);
        [SerializeField] private Color _cancelColor = new Color(0.35f, 0.35f, 0.4f, 0.9f);
        [SerializeField] private Color _blockColor = new Color(0.3f, 0.65f, 1f, 0.95f);
        [Tooltip("表示してから消えるまでの時間（秒）")]
        [SerializeField] private float _duration = 0.8f;
        [Tooltip("×印の大きさ（m、中心から端まで）")]
        [SerializeField] private float _crossSize = 0.45f;
        [SerializeField] private int _fontSize = 28;

        private LineRenderer _cross;
        private GUIStyle _style;
        private ISimulationFeed _feed;
        private SimulationRunner _runner;
        private WirePhase _lastPhase = WirePhase.Ready;
        private int _lastCharacter = -1;
        private float _shownAt = -1f;
        private Color _color;
        private bool _blocked;
        private Vector3 _bounceDirection;

        /// <summary>今表示しているか。</summary>
        public bool IsShowing => _shownAt >= 0f && Time.time - _shownAt < _duration;

        /// <summary>最後に外れた位置。</summary>
        public Vector3 LastPoint { get; private set; }

        /// <summary>最後に表示した文字（"MISS"、"CANCEL" か "BLOCK!"）。</summary>
        public string LastLabel { get; private set; } = string.Empty;

        private void Awake()
        {
            _cross = GetComponent<LineRenderer>();
            _cross.useWorldSpace = true;
            _cross.positionCount = CrossPoints;
            _cross.enabled = false;
            _feed = _source as ISimulationFeed;
            _runner = _source as SimulationRunner;
            if (_feed == null || _camera == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）か Camera が設定されていません", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (_runner != null)
            {
                _runner.PlayerReset += Hide;
            }
        }

        private void OnDisable()
        {
            if (_runner != null)
            {
                _runner.PlayerReset -= Hide;
            }
        }

        // 自分のワイヤーが回収の段階に入った瞬間（外れた）を見つける。
        private void Watch()
        {
            int local = _feed.LocalCharacter;
            if (local < 0 || local >= _feed.CharacterCount)
            {
                _lastCharacter = -1;
                return;
            }
            WireState wire = _feed.GetState(local).Wire;
            bool entered = wire.Phase == WirePhase.Retracting && _lastPhase != WirePhase.Retracting && _lastCharacter == local;
            _lastPhase = wire.Phase;
            _lastCharacter = local;
            if (entered)
            {
                OnMissed(wire);
            }
        }

        private void OnMissed(in WireState wire)
        {
            bool canceled = wire.LastMissReason == WireMissReason.ButtonReleased || wire.LastMissReason == WireMissReason.ButtonToggled
                || wire.LastMissReason == WireMissReason.Interrupted;
            if (wire.LastMissReason == WireMissReason.HitNonAttachable)
            {
                ShowBlock(wire.HookPosition.ToUnity(), -wire.HookDirection.ToUnity());
                return;
            }
            Show(wire.HookPosition.ToUnity(), canceled ? "CANCEL" : "MISS", canceled ? _cancelColor : _missColor);
        }

        // 接続できない物に当たった: 撃った側（back）へ跳ね返る火花と BLOCK!。
        private void ShowBlock(Vector3 point, Vector3 back)
        {
            LastPoint = point;
            LastLabel = "BLOCK!";
            _color = _blockColor;
            _shownAt = Time.time;
            _blocked = true;
            back.z = 0f;
            _bounceDirection = back.sqrMagnitude > 1e-6f ? back.normalized : Vector3.up;
            _cross.positionCount = SparkRays * 2;
            DrawSparks(0f);
            _cross.enabled = true;
        }

        // 火花: point から、跳ね返る向きを中心に扇形に伸びる線（線ごとに中心へ戻る折れ線）。progress は 0〜1。
        private void DrawSparks(float progress)
        {
            float length = SparkLength * (0.25f + 0.75f * (1f - (1f - progress) * (1f - progress)));
            float baseAngle = Mathf.Atan2(_bounceDirection.y, _bounceDirection.x);
            for (int k = 0; k < SparkRays; k++)
            {
                float angle = baseAngle + (k / (float)(SparkRays - 1) - 0.5f) * SparkSpreadDeg * Mathf.Deg2Rad;
                float ray = length * (k % 2 == 0 ? 1f : 0.65f);
                Vector3 tip = LastPoint + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * ray;
                _cross.SetPosition(k * 2, LastPoint + (tip - LastPoint) * 0.3f * progress);
                _cross.SetPosition(k * 2 + 1, tip);
            }
        }

        private void Show(Vector3 point, string label, Color color)
        {
            LastPoint = point;
            LastLabel = label;
            _color = color;
            _shownAt = Time.time;
            _blocked = false;
            _cross.positionCount = CrossPoints;
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
            Watch();
            if (!IsShowing)
            {
                _cross.enabled = false;
                return;
            }
            Color color = FadedColor();
            _cross.startColor = color;
            _cross.endColor = color;
            if (_blocked)
            {
                DrawSparks(Mathf.Clamp01((Time.time - _shownAt) / SparkSeconds));
            }
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
            // BLOCK! は跳ね返る向きへ少し飛び出し、最初に大きく出てから元の大きさに戻る。
            float age = Time.time - _shownAt;
            Vector3 offset = new Vector3(0f, _crossSize + 0.5f, 0f);
            if (_blocked)
            {
                offset += _bounceDirection * (0.6f * Mathf.Clamp01(age / 0.25f)) + Vector3.up * (0.5f * Mathf.Sin(Mathf.Clamp01(age / 0.35f) * Mathf.PI));
            }
            Vector3 screen = _camera.WorldToScreenPoint(LastPoint + offset);
            if (screen.z < 0f)
            {
                return;
            }
            _style.fontSize = _blocked ? Mathf.RoundToInt(_fontSize * (1f + 0.5f * Mathf.Clamp01(1f - age / 0.2f))) : _fontSize;
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
