using System;
using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Physics;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Data;
using Airerage.Runtime.Input;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using SimInput = Airerage.Core.Input.PlayerInput;

namespace Airerage.Net
{
    /// <summary>
    /// オンライン接続の検証用シーン（Sandbox_Online）の進行と表示（docs/10_online_plan.md T2-7）。
    /// 画面左上のボタンで、ホストとして待ち受けるか、アドレスを指定して参加する。ホストが「Start」を押すと、
    /// そのとき参加している全員（最大4人）で始まる。通信とロールバックは OnlineSession が受け持ち、ここは入力と表示だけを扱う。
    /// 位置のリセットはできない（全員のシミュレーションを同じに保つため、入力以外で状態を変えない）。
    /// 巻き戻しでキャラクターの位置が飛んだときは、表示だけを少しずつ寄せる（CorrectionSmoother。T2-8）。
    /// 検証のため、自分が送るパケットに遅延と欠落を加えられる（往復の遅延を模擬するには全員で同じ値を選ぶ）。
    /// ステージ（T3-8）: シーンのステージの範囲（KnockoutBounds）、ギミック（StageZoneAuthoring）、出現位置（_spawnPoints）を使う。
    /// 右上の全員の情報（ScoreboardHud）、コンボの表示、効果音などは ISimulationFeed で読ませる。
    /// ロビーでは、画面の中央に待機所（WaitingRoomPanel。だれがつながっていて、どの番号がまだ空いているか）を、ホストにも
    /// クライアントにも出す。ホストはその下の「START」で始める。
    /// </summary>
    public sealed class OnlineRunner : MonoBehaviour, ISimulationFeed, IMatchControls
    {
        [SerializeField] private CharacterConfigAsset _character;
        [SerializeField] private InputActionAsset _inputActions;
        [Tooltip("試合の前にカメラを向ける位置。_spawnPoints がなければ、ここを中心に横に並べて出す")]
        [SerializeField] private Transform _spawnPoint;
        [Tooltip("プレイヤーごとの出現位置（番号の順）。人数より少なければ _spawnPoint を中心に横に並べる")]
        [SerializeField] private Transform[] _spawnPoints = new Transform[0];
        [Tooltip("プレイヤーごとの色（右上の表示に使う）")]
        [SerializeField] private Color[] _playerColors = new Color[0];
        [Tooltip("BGM の切り替えに使う（なくてもよい）")]
        [SerializeField] private GameAudio _audio;
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
        [Tooltip("プレイヤーネーム（空なら P1〜P4）。前に入れた名前を PlayerPrefs から読む")]
        [SerializeField] private string _playerName = string.Empty;
        [SerializeField] private ushort _port = 7777;
        [Tooltip("ロールバックの設定（すべて【仮】。T2-9 で決める）")]
        [SerializeField] private RollbackSettings _rollback = new RollbackSettings();
        [Tooltip("巻き戻しで位置が飛んだときの表示の補正（すべて【仮】）")]
        [SerializeField] private CorrectionSmoothingSettings _smoothing = new CorrectionSmoothingSettings();
        [Tooltip("試合のルール（すべて【仮】。docs/12）。ホストのものを全員が使う。ロビーで「Timed match」を外すと時間の制限なし")]
        [SerializeField] private MatchRules _match = new MatchRules();

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
        private OnlineStage _stage;
        private int _notifiedTick = -1;
        private bool _showDetails;
        private bool _timedMatch = true;
        private InputSystemAdapter _input;
        private OnlineSession _session;
        private string _portText;
        private CorrectionSmoother _smoother;
        private string _message = string.Empty;
        private GUIStyle _style;
        private int _delayPreset;
        private int _dropPreset;
        private WaitingRoomPanel _waitingRoom;
        private GUIStyle _footer;

        /// <summary>今のセッション（始めていなければ null）。</summary>
        public OnlineSession Session => _session;

        /// <summary>このシーンのステージ（地形、出現位置、ステージの範囲、ギミック）。</summary>
        public OnlineStage Stage => _stage;

        /// <summary>シミュレーションが進んだときに通知する（ISimulationFeed。巻き戻しで計算し直したときも含む）。</summary>
        public event Action Ticked;

        private bool Running => _session != null && _session.World != null;

        // 表示と効果音の読み取り口（ISimulationFeed）。番号はプレイヤーの番号。
        int ISimulationFeed.CharacterCount => Running ? _session.PlayerCount : 0;
        int ISimulationFeed.LocalCharacter => Running ? _session.LocalPlayer : -1;
        CharacterState ISimulationFeed.GetState(int index) => _session.World.GetState(index);
        CharacterConfig ISimulationFeed.GetConfig(int index) => _session.World.GetConfig(index);
        string ISimulationFeed.LabelOf(int index) => LabelOf(index);
        int ISimulationFeed.CurrentTick => Running ? _session.World.Tick : 0;
        MatchRules ISimulationFeed.Rules => Running ? _session.World.Rules : null;
        MatchState ISimulationFeed.Match => Running ? _session.World.Match : MatchState.None;
        CoinField ISimulationFeed.Coins => Running ? _session.World.Coins.Field : null;
        IReadOnlyList<CoinSpawnPoint> ISimulationFeed.CoinSpawns => Running ? _session.World.CoinSpawns : (IReadOnlyList<CoinSpawnPoint>)Array.Empty<CoinSpawnPoint>();
        IReadOnlyList<BankSpot> ISimulationFeed.BankSpots => Running ? _session.World.BankSpots : (IReadOnlyList<BankSpot>)Array.Empty<BankSpot>();
        Color ISimulationFeed.ColorOf(int index) => index < _playerColors.Length ? _playerColors[index] : Color.white;

        /// <summary>ホストとして待ち受ける（port が 0 なら空いているポート）。失敗したら false で、理由を画面に出す。</summary>
        public bool StartHost(ushort port)
        {
            return Open(() => OnlineSession.Host(port, _stage, _character.Config, _rollback));
        }

        /// <summary>address:port のホストに参加する。失敗したら false で、理由を画面に出す。</summary>
        public bool StartJoin(string address, ushort port)
        {
            return Open(() => OnlineSession.Join(address, port, _stage, _rollback));
        }

        /// <summary>試合を始める（ホストのみ）。ロビーの選択で、時間の制限ありか、なしか。</summary>
        public void StartMatch()
        {
            _session.StartMatch(RulesForNextMatch());
        }

        /// <summary>結果の画面の「Restart」を押せるか（ホストだけ。IMatchControls）。</summary>
        public bool CanRestart => _session != null && _session.IsHost && _session.State == OnlineSession.SessionState.Running;

        /// <summary>同じ人で試合を始め直す（ホストのみ。ロビーで選んだルールで）。</summary>
        public void Restart()
        {
            if (!CanRestart)
            {
                return;
            }
            _session.RestartMatch(RulesForNextMatch());
            _smoother = null;
            _notifiedTick = -1;
        }

        /// <summary>ゲームを終える（エディタでは再生を止める）。</summary>
        public void Quit()
        {
            Leave();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>ホストが使う試合のルール（値はシーンの設定。テストで短くするのに使う）。</summary>
        public MatchRules HostRules => _match;

        /// <summary>時間の制限ありで始めるか（ホストのロビーの選択。テスト用に公開）。</summary>
        public bool TimedMatch
        {
            get => _timedMatch;
            set => _timedMatch = value;
        }

        // 次の試合のルール: ロビーの選択（時間の制限なしなら制限だけをなくす）と、試合ごとの乱数の種（100 枚のコインの場所など）。
        private MatchRules RulesForNextMatch()
        {
            MatchRules rules = _match.Clone();
            if (!_timedMatch)
            {
                rules.DurationTicks = 0;
            }
            rules.Seed = Environment.TickCount;
            return rules;
        }

        /// <summary>セッションを閉じて、最初の画面に戻る。</summary>
        public void Leave()
        {
            _session?.Dispose();
            _session = null;
            _smoother = null;
            _delayPreset = 0;
            _dropPreset = 0;
            _notifiedTick = -1;
            UpdateViews();
            Ticked?.Invoke();
        }

        private void Awake()
        {
            if (_character == null || _inputActions == null || _spawnPoint == null || _camera == null)
            {
                Debug.LogError($"{name}: CharacterConfigAsset、InputActionAsset、出現位置、Camera のどれかが設定されていません", this);
                enabled = false;
                return;
            }
            if (string.IsNullOrEmpty(_playerName))
            {
                _playerName = OnlineSession.SanitizeName(PlayerPrefs.GetString(PlayerNameKey, string.Empty));
            }
            if (_characterViews.Length < OnlineSession.MaxPlayers || _wireViews.Length < OnlineSession.MaxPlayers)
            {
                Debug.LogError($"{name}: キャラクターとワイヤーの表示が {OnlineSession.MaxPlayers} 人分ありません", this);
                enabled = false;
                return;
            }
            _rollback.Validate();
            _smoothing.Validate();
            _match.Validate().ThrowIfInvalid();

            // 複数のプレイヤーを同じ PC で動かすとき（Multiplayer Play Mode）、操作していないウィンドウも止めない。
            Application.runInBackground = true;

            _terrain = SceneTerrainBuilder.Build(_terrainMask, _errors);
            StageBounds? bounds = KnockoutBounds.Find(_errors);
            List<StageZone> zones = StageZoneAuthoring.Collect(_errors);
            List<CoinSpawnPoint> coinSpawns = CoinSpawnAuthoring.Collect(_errors);
            List<BankSpot> bankSpots = BankSpotAuthoring.Collect(_errors);
            _stage = new OnlineStage(_terrain, SpawnPositions(), bounds, zones, coinSpawns, bankSpots);
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
            _waitingRoom?.Dispose();
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
            if (Running && _session.World.Tick != _notifiedTick)
            {
                _notifiedTick = _session.World.Tick;
                Ticked?.Invoke();
            }
        }

        // 出現位置（_spawnPoints が人数分なければ、_spawnPoint を中心に横に並べる。OnlineStage.SpawnPosition）。
        private List<System.Numerics.Vector3> SpawnPositions()
        {
            var spawns = new List<System.Numerics.Vector3>();
            foreach (Transform spawn in _spawnPoints)
            {
                if (spawn != null)
                {
                    spawns.Add(spawn.position.ToNumerics());
                }
            }
            if (spawns.Count < OnlineSession.MaxPlayers)
            {
                spawns.Clear();
                spawns.Add(_spawnPoint.position.ToNumerics());
            }
            return spawns;
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
                _session.LocalName = _playerName;
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
                _characterViews[p].RenderAction(current, _session.World.GetConfig(p).Combat, _session.World.Rules.RespawnDelayTicks);
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

            // 試合中は小さくして、コンボの表示（左）と重ならないようにする（詳細は「Details」で開く）。
            bool compact = Running && !_showDetails;
            GUILayout.BeginArea(new Rect(10f, 10f, 460f, compact ? 120f : 600f), GUI.skin.box);
            if (_session == null)
            {
                DrawConnectPanel();
            }
            else
            {
                DrawSessionPanel(compact);
            }
            GUILayout.EndArea();

            if (_session != null && _session.State == OnlineSession.SessionState.Lobby)
            {
                DrawWaitingRoom();
            }
        }

        // 待機所: 画面の中央に、番号ごとの札。ホストは下に時間の制限の切り替えと「START」、クライアントは待っている案内。
        private void DrawWaitingRoom()
        {
            if (_waitingRoom == null)
            {
                _waitingRoom = new WaitingRoomPanel();
                _footer = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
                _footer.normal.textColor = Color.white;
            }
            const float padding = 18f;
            float width = WaitingRoomPanel.PlateWidth + padding * 2f;
            float height = WaitingRoomPanel.Height + 70f + padding * 2f;
            var box = new Rect((Screen.width - width) * 0.5f, Mathf.Max(10f, (Screen.height - height) * 0.5f), width, height);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = previous;

            string subtitle = _session.IsHost
                ? $"Hosting on port {_session.Port}. Friends join with your address."
                : "Connected. The host starts the match.";
            float y = _waitingRoom.Draw(new Vector2(box.x + padding, box.y + padding), _session.Roster, _session.LocalPlayer,
                index => index < _playerColors.Length ? _playerColors[index] : Color.white, subtitle);

            var footer = new Rect(box.x + padding, y + 4f, WaitingRoomPanel.PlateWidth, 56f);
            if (_session.IsHost)
            {
                int seconds = _match.DurationTicks / SimTime.TicksPerSecond;
                _timedMatch = GUI.Toggle(new Rect(footer.x, footer.y, footer.width, 20f), _timedMatch,
                    $" Timed match ({seconds / 60}:{seconds % 60:00}). Off = free play");
                if (GUI.Button(new Rect(footer.x, footer.y + 24f, footer.width, 32f), "START"))
                {
                    StartMatch();
                    ExitGui();
                }
            }
            else
            {
                int dots = 1 + (int)(Time.unscaledTime * 2f) % 3;
                GuidePlate.DrawText(footer, "Waiting for the host to start" + new string('.', dots), _footer, new Color(1f, 1f, 1f, 0.8f));
            }
        }

        private const string PlayerNameKey = "Airerage.PlayerName";

        /// <summary>表示用の名前（入力したプレイヤーネーム。なければ P1〜P4）。</summary>
        public string LabelOf(int index)
        {
            IReadOnlyList<string> names = _session != null ? _session.PlayerNames : null;
            string name = names != null && index >= 0 && index < names.Count ? names[index] : string.Empty;
            return string.IsNullOrEmpty(name) ? $"P{index + 1}" : name;
        }

        /// <summary>自分のプレイヤーネーム（テスト用にも公開。セッションにも渡す）。</summary>
        public string PlayerName
        {
            get => _playerName;
            set
            {
                _playerName = OnlineSession.SanitizeName(value);
                if (_session != null)
                {
                    _session.LocalName = _playerName;
                }
            }
        }

        // プレイヤーネームの入力欄（ロビーの前とロビーで）。変えたら覚えておき、セッションにも渡す。
        private void DrawNameField()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _style, GUILayout.Width(70f));
            string edited = GUILayout.TextField(_playerName, OnlineSession.MaxNameLength, GUILayout.Width(160f));
            GUILayout.Label("(empty = P1-P4)", _style);
            GUILayout.EndHorizontal();
            if (edited != _playerName)
            {
                PlayerName = edited;
                PlayerPrefs.SetString(PlayerNameKey, _playerName);
            }
        }

        private void DrawConnectPanel()
        {
            GUILayout.Label("Online (T2-7)", _style);
            DrawNameField();
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

        private void DrawSessionPanel(bool compact)
        {
            GUILayout.Label(_session.Status, _style);
            if (Running)
            {
                GUILayout.BeginHorizontal();
                _showDetails = GUILayout.Toggle(_showDetails, "Details");
                if (_audio != null)
                {
                    bool bgm = GUILayout.Toggle(_audio.BgmEnabled, "BGM");
                    if (bgm != _audio.BgmEnabled)
                    {
                        _audio.BgmOn = bgm;
                    }
                }
                if (GUILayout.Button("Leave"))
                {
                    Leave();
                    ExitGui();
                }
                GUILayout.EndHorizontal();
                if (compact)
                {
                    if (_session.IsHost && _session.Desyncs.Count > 0)
                    {
                        GUILayout.Label($"Desyncs {_session.Desyncs.Count}", _style);
                    }
                    return;
                }
            }
            if (_session.State == OnlineSession.SessionState.Lobby)
            {
                DrawNameField();
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

            if (!Running && GUILayout.Button("Leave"))
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
