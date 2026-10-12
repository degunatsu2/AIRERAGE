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

        private readonly List<string> _errors = new List<string>();
        private BoxTerrain _terrain;
        private InputSystemAdapter _input;
        private OnlineSession _session;
        private string _portText;
        private string _message = string.Empty;
        private GUIStyle _style;

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
            UpdateViews();
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
                CharacterState previous = _session.PreviousStates[p];
                CharacterState current = _session.World.GetState(p);
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
            CharacterState previous = _session.PreviousStates[_session.LocalPlayer];
            CharacterState current = _session.World.GetState(_session.LocalPlayer);
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

            GUILayout.BeginArea(new Rect(10f, 10f, 420f, 400f), GUI.skin.box);
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
                if (_session.IsHost)
                {
                    GUILayout.Label($"Checksums compared {_session.ComparedChecksums}  Desyncs {_session.Desyncs.Count}", _style);
                    if (_session.Desyncs.Count > 0)
                    {
                        GUILayout.Label(_session.Desyncs[_session.Desyncs.Count - 1], _style);
                    }
                }
            }

            if (GUILayout.Button("Leave"))
            {
                Leave();
                ExitGui();
            }
        }

        // 画面の項目が変わったので、このイベントでの描画をやめる（IMGUI の配置の計算と食い違わないように）。
        private static void ExitGui()
        {
            GUI.enabled = true;
            GUIUtility.ExitGUI();
        }
    }
}
