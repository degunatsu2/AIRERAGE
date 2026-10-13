using System;
using System.Collections.Generic;
using Degunatsu.Core.Input;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Rollback;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;
using Degunatsu.Runtime.Data;
using Degunatsu.Runtime.Input;
using Degunatsu.Runtime.Presentation;
using Degunatsu.Runtime.Simulation;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using SimInput = Degunatsu.Core.Input.PlayerInput;

namespace Degunatsu.Net
{
    /// <summary>
    /// オンライン接続の検証用シーン（Sandbox_Online）の進行と表示（docs/10_online_plan.md T2-7）。
    /// 画面左上のボタンで、ホストとして待ち受けるか、アドレスを指定して参加する。ホストが「Start」を押すと、
    /// そのとき参加している全員（最大4人）で始まる。通信とロールバックは OnlineSession が受け持ち、ここは入力と表示だけを扱う。
    /// 位置のリセットはできない（全員のシミュレーションを同じに保つため、入力以外で状態を変えない）。
    /// 巻き戻しでキャラクターの位置が飛んだときは、表示だけを少しずつ寄せる（CorrectionSmoother。T2-8）。
    /// 検証のため、自分が送るパケットに遅延と欠落を加えられる（往復の遅延を模擬するには全員で同じ値を選ぶ）。
    /// </summary>
    public sealed class OnlineRunner : MonoBehaviour
    {
        [SerializeField] private CharacterConfigAsset _character;
        [SerializeField] private InputActionAsset _inputActions;
        [SerializeField] private Transform _spawnPoint;
        [Tooltip("地形として読み込むコライダーの層")]
        [SerializeField] private LayerMask _terrainMask = 1;
        [Tooltip("マウスポインターでの照準に使うカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("自分のキャラクターを追従するカメラ")]
        [SerializeField] private CinemachineCamera _followCamera;
        [Tooltip("プレイヤーごとの表示（最大4人分。番号の順）")]
        [SerializeField] private CharacterView[] _characterViews = new CharacterView[0];
        [SerializeField] private WireView[] _wireViews = new WireView[0];
        [SerializeField] private AimIndicatorView _aimIndicator;
        [SerializeField] private string _address = "127.0.0.1";
        [SerializeField] private ushort _port = 7777;
        [Tooltip("ロールバックの設定（すべて【仮】。T2-9 で決める）")]
        [SerializeField] private RollbackSettings _rollback = new RollbackSettings();
        [Tooltip("巻き戻しで位置が飛んだときの表示の補正（すべて【仮】）")]
        [SerializeField] private CorrectionSmoothingSettings _smoothing = new CorrectionSmoothingSettings();

        // 遅延の模擬の選択肢（docs/10_online_plan.md T2-9 の評価の条件）。自分が送る側の遅延なので、往復はこの2倍。
        private static readonly (string Label, int DelayMs, int JitterMs)[] DelayPresets =
        {
            ("Off", 0, 0),
            ("RTT 50", 25, 3),
            ("RTT 100", 50, 5),
            ("RTT 150", 75, 8),
        };
        private static readonly int[] DropPresets = { 0, 2, 5 };
        private static readonly string[] DelayLabels = Array.ConvertAll(DelayPresets, preset => preset.Label);
        private static readonly string[] DropLabels = Array.ConvertAll(DropPresets, percent => $"Loss {percent}%");

        private readonly List<string> _errors = new List<string>();
        private BoxTerrain _terrain;
        private InputSystemAdapter _input;
        private OnlineSession _session;
        private string _portText;
        private CorrectionSmoother _smoother;
        private string _message = string.Empty;
        private GUIStyle _style;
        private int _delayPreset;
        private int _dropPreset;

        /// <summary>今のセッション（始めていなければ null）。</summary>
        public OnlineSession Session => _session;

        /// <summary>ホストとして待ち受ける（port が 0 なら空いているポート）。失敗したら false で、理由を画面に出す。</summary>
        public bool StartHost(ushort port)
        {
            return Open(() => OnlineSession.Host(port, _terrain, _spawnPoint.position.ToNumerics(), _character.Config, _rollback));
        }

        /// <summary>address:port のホストに参加する。失敗したら false で、理由を画面に出す。</summary>
        public bool StartJoin(string address, ushort port)
        {
            return Open(() => OnlineSession.Join(address, port, _terrain, _spawnPoint.position.ToNumerics(), _rollback));
        }

        /// <summary>試合を始める（ホストのみ）。</summary>
        public void StartMatch()
        {
            _session.StartMatch();
        }

        /// <summary>セッションを閉じて、最初の画面に戻る。</summary>
        public void Leave()
        {
            _session?.Dispose();
            _session = null;
            _smoother = null;
            _delayPreset = 0;
            _dropPreset = 0;
            UpdateViews();
        }

        private void Awake()
        {
            if (_character == null || _inputActions == null || _spawnPoint == null || _camera == null)
            {
                Debug.LogError($"{name}: CharacterConfigAsset、InputActionAsset、出現位置、Camera のどれかが設定されていません", this);
                enabled = false;
                return;
            }
            if (_characterViews.Length < OnlineSession.MaxPlayers || _wireViews.Length < OnlineSession.MaxPlayers)
            {
                Debug.LogError($"{name}: キャラクターとワイヤーの表示が {OnlineSession.MaxPlayers} 人分ありません", this);
                enabled = false;
                return;
            }
            _rollback.Validate();
            _smoothing.Validate();

            // 複数のプレイヤーを同じ PC で動かすとき（Multiplayer Play Mode）、操作していないウィンドウも止めない。
            Application.runInBackground = true;

            _terrain = SceneTerrainBuilder.Build(_terrainMask, _errors);
            foreach (string error in _errors)
            {
                Debug.LogError(error, this);
            }
            _input = new InputSystemAdapter(_inputActions);
            _portText = _port.ToString();
            UpdateViews();
        }

        private void OnEnable()
        {
            _input?.Enable();
        }

        private void OnDisable()
        {
            _input?.Disable();
        }

        private void OnDestroy()
        {
            _session?.Dispose();
            _session = null;
            _input?.Dispose();
        }

        private void Update()
        {
            if (_session == null)
            {
                return;
            }
            if (_session.State == OnlineSession.SessionState.Running)
            {
                // 試合の前に押したボタンを、最初のティックに持ち込まない。
                _input.Poll(_camera, _character.Config.Wire.AimDeadzone);
            }
            _session.Update(Time.deltaTime, ConsumeLocalInput);
            UpdateSmoothing(Time.deltaTime);
            UpdateViews();
        }

        private void UpdateSmoothing(float deltaTime)
        {
            if (_session.World == null)
            {
                return;
            }
            if (_smoother == null)
            {
                _smoother = new CorrectionSmoother(_session.PlayerCount, _smoothing);
            }
            for (int p = 0; p < _session.PlayerCount; p++)
            {
                _smoother.AddCorrection(p, _session.ConsumeCorrection(p).ToUnity());
            }
            _smoother.Update(deltaTime);
        }

        // 表示に使う、直前と今のティックの状態（表示の補正のずれを足す）。
        private void DisplayedStates(int player, out CharacterState previous, out CharacterState current)
        {
            previous = _session.PreviousStates[player];
            current = _session.World.GetState(player);
            if (_smoother == null)
            {
                return;
            }
            System.Numerics.Vector3 offset = _smoother.Offset(player).ToNumerics();
            previous.Position += offset;
            current.Position += offset;
        }

        private SimInput ConsumeLocalInput(int tick)
        {
            // 照準の方向は、今の自分の位置から求める（入力遅延の間の移動は小さいので無視する）。
            return _input.ConsumeTick(_session.World.GetState(_session.LocalPlayer).Position);
        }

        private bool Open(Func<OnlineSession> open)
        {
            Leave();
            try
            {
                _session = open();
                _message = string.Empty;
                return true;
            }
            catch (InvalidOperationException e)
            {
                _message = e.Message;
                Debug.LogError($"{name}: 接続を始められません: {e.Message}", this);
            }
            catch (ArgumentException e)
            {
                _message = e.Message;
                Debug.LogError($"{name}: 接続を始められません: {e.Message}", this);
            }
            return false;
        }

        private void UpdateViews()
        {
            bool running = _session != null && _session.World != null;
            int count = running ? _session.PlayerCount : 0;
            for (int p = 0; p < _characterViews.Length; p++)
            {
                bool shown = p < count;
                _characterViews[p].gameObject.SetActive(shown);
                _wireViews[p].gameObject.SetActive(shown);
            }
            if (_aimIndicator != null)
            {
                _aimIndicator.gameObject.SetActive(running);
            }
            if (!running)
            {
                if (_followCamera != null)
                {
                    _followCamera.Follow = _spawnPoint;
                }
                return;
            }

            float alpha = Mathf.Clamp01(_session.Alpha);
            WireTuning wire = _session.World.GetConfig(_session.LocalPlayer).Wire;
            Vector3 halfExtents = _session.World.GetConfig(_session.LocalPlayer).Movement.HalfExtents.ToUnity();
            for (int p = 0; p < count; p++)
            {
                DisplayedStates(p, out CharacterState previous, out CharacterState current);
                _characterViews[p].Render(previous, current, alpha, halfExtents);
                _wireViews[p].Render(previous, current, alpha, wire);
            }

            Transform local = _characterViews[_session.LocalPlayer].transform;
            if (_followCamera != null && _followCamera.Follow != local)
            {
                _followCamera.Follow = local;
            }
            RenderAim(alpha, wire);
        }

        private void RenderAim(float alpha, WireTuning wire)
        {
            if (_aimIndicator == null)
            {
                return;
            }
            // 表示の位置（補間した位置）から、今の入力で射出したときの結果を予測して示す。
            DisplayedStates(_session.LocalPlayer, out CharacterState previous, out CharacterState current);
            Vector3 origin = Vector3.Lerp(previous.Position.ToUnity(), current.Position.ToUnity(), alpha);
            System.Numerics.Vector3 simOrigin = origin.ToNumerics();
            var frame = new InputFrame(_input.Peek(simOrigin), current.PreviousButtons);
            WireShotPreview preview = WireTargeting.PredictShot(simOrigin, frame.Aim, current.Facing, wire, _terrain);
            _aimIndicator.Render(origin, current.Wire, preview, wire.MaxLength);
        }

        private void OnGUI()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 14 };
                _style.normal.textColor = Color.white;
            }

            GUILayout.BeginArea(new Rect(10f, 10f, 460f, 560f), GUI.skin.box);
            if (_session == null)
            {
                DrawConnectPanel();
            }
            else
            {
                DrawSessionPanel();
            }
            GUILayout.EndArea();
        }

        private void DrawConnectPanel()
        {
            GUILayout.Label("Online (T2-7)", _style);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Address", _style, GUILayout.Width(70f));
            _address = GUILayout.TextField(_address, GUILayout.Width(160f));
            GUILayout.Label("Port", _style, GUILayout.Width(40f));
            _portText = GUILayout.TextField(_portText, GUILayout.Width(70f));
            GUILayout.EndHorizontal();

            bool validPort = ushort.TryParse(_portText, out ushort port);
            GUILayout.BeginHorizontal();
            GUI.enabled = validPort;
            if (GUILayout.Button("Host"))
            {
                StartHost(port);
                ExitGui();
            }
            if (GUILayout.Button("Join"))
            {
                StartJoin(_address, port);
                ExitGui();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (!validPort)
            {
                GUILayout.Label("Port must be 0-65535", _style);
            }
            if (_message.Length > 0)
            {
                GUILayout.Label(_message, _style);
            }
        }

        private void DrawSessionPanel()
        {
            GUILayout.Label(_session.Status, _style);
            if (_session.IsHost && _session.State == OnlineSession.SessionState.Lobby)
            {
                GUILayout.Label($"Players: {_session.LobbyPlayerCount} / {OnlineSession.MaxPlayers}", _style);
                if (GUILayout.Button("Start"))
                {
                    StartMatch();
                    ExitGui();
                }
            }

            RollbackSession rollback = _session.Session;
            if (rollback != null)
            {
                GUILayout.Label($"Tick {rollback.CurrentTick}  Confirmed {rollback.ConfirmedTick}", _style);
                GUILayout.Label($"Delay {_rollback.InputDelayTicks}F  Max prediction {_rollback.MaxPredictionTicks}F", _style);
                GUILayout.Label($"Rollbacks {rollback.RollbackCount}  Longest {rollback.LongestRollback}F  Resimulated {rollback.ResimulatedTicks}F", _style);
                GUILayout.Label($"Stalled {_session.StalledFrames}F  Time sync waits {_session.TimeSyncWaitFrames}F  Ahead {_session.Exchange.TicksAhead:0.0}F", _style);
                GUILayout.Label($"Corrections {_session.CorrectionCount}  Largest {_session.LargestCorrection:0.00}m", _style);
                if (_session.IsHost)
                {
                    GUILayout.Label($"Checksums compared {_session.ComparedChecksums}  Desyncs {_session.Desyncs.Count}", _style);
                    if (_session.Desyncs.Count > 0)
                    {
                        GUILayout.Label(_session.Desyncs[_session.Desyncs.Count - 1], _style);
                    }
                }
            }

            DrawTestControls();

            if (GUILayout.Button("Leave"))
            {
                Leave();
                ExitGui();
            }
        }

        // 検証用: 表示の補正の切り替えと、自分が送るパケットの遅延・欠落の模擬。
        private void DrawTestControls()
        {
            _smoothing.Enabled = GUILayout.Toggle(_smoothing.Enabled, "Smooth corrections");
            int delay = GUILayout.Toolbar(_delayPreset, DelayLabels);
            int drop = GUILayout.Toolbar(_dropPreset, DropLabels);
            if (delay != _delayPreset || drop != _dropPreset)
            {
                _delayPreset = delay;
                _dropPreset = drop;
                var (_, delayMs, jitterMs) = DelayPresets[delay];
                _session.Link.SetSimulatedConditions(delayMs, jitterMs, DropPresets[drop]);
            }
            GUILayout.Label($"Sending: +{_session.Link.SimulatedDelayMs}±{_session.Link.SimulatedJitterMs}ms, loss {_session.Link.SimulatedDropPercent}% (set the same on every player)", _style);
        }

        // 画面の項目が変わったので、このイベントでの描画をやめる（IMGUI の配置の計算と食い違わないように）。
        private static void ExitGui()
        {
            GUI.enabled = true;
            GUIUtility.ExitGUI();
        }
    }
}
