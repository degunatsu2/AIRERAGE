using System;
using System.Collections.Generic;
using Airerage.Core.Ai;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;
using Airerage.Runtime.Data;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Input;
using Airerage.Runtime.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using SimInput = Airerage.Core.Input.PlayerInput;

namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// ローカル対戦の試合（docs/15 T6-5。D-67）: 1 台の PC で 2〜4 人が、それぞれの機器（キーボード＋マウス、ゲームパッド）で操作する。
    /// 試合の仕組みはオンラインと同じ SimWorld（試合のルール、コイン、拠点、撃破、結果）で、ロールバックはしない。
    /// - 参加した人（機器とキャラクター）と時間の制限は GameFlow から受け取る（ローカル対戦の参加の画面で決める）。シーンを直接再生した
    ///   ときは、キーボード＋マウスとつながっているゲームパッドで参加する（足りなければ 2 人目は操作なしで立つ）。
    /// - 人ごとに、入力のアセットを複製して機器を限る（InputActionAsset.devices）。照準は人ごとの機器で決まる（マウスはキーボードの人だけ）。
    /// - カメラは全員が入る 1 つのカメラ（SharedCamera）。技の札は人ごと（SkillHud の _player）、コンボはだれのものでも出す（ComboHud）。
    /// - 結果の REMATCH で同じ人で始め直し、CHARACTER SELECT で同じ人のままキャラクター選択へ、MAIN MENU でメニューへ（IMatchControls。
    ///   結果の画面は ResultsMenu。docs/15 T6-8）。Esc・Start で一時停止（IPauseTarget）。
    /// - シングルモード（docs/16 T7-2）: CPU の人（LocalPlayerSetup.IsCpu）は、CpuPlayer が入力を作る（機器は読まない。照準の表示と技の札は出さない）。
    /// ステージの地形・範囲・ギミック・コインの出現地点・拠点・出現位置は、読み込んだステージのシーンから探す（D-66）。
    /// </summary>
    public sealed class LocalMatchRunner : MonoBehaviour, ISimulationFeed, IMatchControls, IPauseTarget, ICpuRoster
    {
        public const int MaxPlayers = 4;

        // 1フレームで進める最大のティック数（SimulationRunner と同じ。技術的な上限）。
        private const int MaxTicksPerFrame = 5;

        [Tooltip("選べるキャラクターの一覧（参加の画面と同じ番号）")]
        [SerializeField] private CharacterRoster _roster;
        [SerializeField] private InputActionAsset _inputActions;
        [Tooltip("地形として読み込むコライダーの層")]
        [SerializeField] private LayerMask _terrainMask = 1;
        [Tooltip("マウスポインターでの照準に使うカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("プレイヤーごとの表示（4 人分。番号の順）")]
        [SerializeField] private CharacterView[] _characterViews = new CharacterView[0];
        [SerializeField] private WireView[] _wireViews = new WireView[0];
        [SerializeField] private AimIndicatorView[] _aimIndicators = new AimIndicatorView[0];
        [Tooltip("プレイヤーごとの色（P1〜P4）")]
        [SerializeField] private Color[] _playerColors = new Color[0];
        [Tooltip("試合のルール（すべて【仮】。docs/12）。参加の画面で時間の制限を外すと、制限だけをなくす")]
        [SerializeField] private MatchRules _match = new MatchRules();

        private readonly List<LocalPlayerSetup> _players = new List<LocalPlayerSetup>();
        private readonly List<InputActionAsset> _assets = new List<InputActionAsset>();
        private readonly List<InputSystemAdapter> _inputs = new List<InputSystemAdapter>();
        private BoxTerrain _terrain;
        private StageBounds? _bounds;
        private List<StageZone> _zones;
        private List<CoinSpawnPoint> _coinSpawns;
        private List<BankSpot> _bankSpots;
        private List<StagePlatform> _platforms;
        private List<Vector3> _spawns;
        private SimWorld _world;
        private SimInput[] _tickInputs = new SimInput[0];
        private CpuPlayer[] _cpus = new CpuPlayer[0];
        private CharacterState[] _previous = new CharacterState[0];
        private GameFlow _flow;
        private bool _timed = true;
        private float _accumulator;
        private bool _inputBlocked;

        /// <summary>1ティック進めるたびに通知する（ISimulationFeed）。</summary>
        public event Action Ticked;

        /// <summary>参加している人（P1 から順。テスト用）。</summary>
        public IReadOnlyList<LocalPlayerSetup> Players => _players;

        /// <summary>シミュレーションの速さの倍率（1 = 等速、0 = 一時停止。IPauseTarget）。</summary>
        public float SpeedScale { get; set; } = 1f;

        /// <summary>対戦なので、一時停止のメニューに RESET POSITION は出さない（IPauseTarget）。</summary>
        public bool CanResetPosition => false;

        /// <summary>ゲームの入力を止めるか（一時停止のメニューを開いている間。IPauseTarget）。</summary>
        public bool InputBlocked
        {
            get => _inputBlocked;
            set
            {
                if (_inputBlocked && !value)
                {
                    foreach (InputSystemAdapter input in _inputs)
                    {
                        input.SuppressHeld();
                    }
                }
                _inputBlocked = value;
            }
        }

        /// <summary>結果の Restart はいつでも押せる（IMatchControls）。</summary>
        public bool CanRestart => _world != null;

        /// <summary>今の試合のワールド（テスト用）。</summary>
        public SimWorld World => _world;

        /// <summary>次の試合（Restart）のルール（値はシーンの設定。テストで短くするのに使う）。</summary>
        public MatchRules MatchRules => _match;

        // 表示と効果音の読み取り口（ISimulationFeed）。番号はプレイヤーの番号（P1 が 0）。この PC の全員が操作するので LocalCharacter はない。
        public int CharacterCount => _world == null ? 0 : _world.CharacterCount;
        int ISimulationFeed.LocalCharacter => -1;
        public CharacterState GetState(int index) => _world.GetState(index);
        CharacterConfig ISimulationFeed.GetConfig(int index) => _world.GetConfig(index);
        public CharacterConfigAsset CharacterOf(int index) => index >= 0 && index < _players.Count ? _roster.Get(_players[index].Character) : null;
        public string LabelOf(int index) => IsCpu(index) ? $"CPU{index + 1}" : $"P{index + 1}";

        /// <summary>キャラクター index を CPU が操作しているか（ICpuRoster）。</summary>
        public bool IsCpu(int index) => index >= 0 && index < _players.Count && _players[index].IsCpu;

        /// <summary>キャラクター index の CPU（人なら null。テスト用）。</summary>
        public CpuPlayer CpuOf(int index) => index >= 0 && index < _cpus.Length ? _cpus[index] : null;
        public Color ColorOf(int index) => index >= 0 && index < _playerColors.Length ? _playerColors[index] : Color.white;
        public int CurrentTick => _world == null ? 0 : _world.Tick;
        MatchRules ISimulationFeed.Rules => _world?.Rules;
        MatchState ISimulationFeed.Match => _world != null ? _world.Match : MatchState.None;
        CoinField ISimulationFeed.Coins => _world?.Coins.Field;
        WorldObjects ISimulationFeed.Objects => _world?.Objects;
        IReadOnlyList<StagePlatform> ISimulationFeed.Platforms => _world?.Platforms;
        StageState ISimulationFeed.Stage => _world?.Stage;
        IReadOnlyList<CoinSpawnPoint> ISimulationFeed.CoinSpawns => _world != null ? _world.CoinSpawns : (IReadOnlyList<CoinSpawnPoint>)Array.Empty<CoinSpawnPoint>();
        IReadOnlyList<BankSpot> ISimulationFeed.BankSpots => _world != null ? _world.BankSpots : (IReadOnlyList<BankSpot>)Array.Empty<BankSpot>();

        private void Awake()
        {
            if (_roster == null || _roster.Count == 0 || _inputActions == null || _camera == null)
            {
                Debug.LogError($"{name}: キャラクターの一覧・入力・カメラのどれかが設定されていません", this);
                enabled = false;
                return;
            }
            if (_characterViews.Length < MaxPlayers || _wireViews.Length < MaxPlayers || _aimIndicators.Length < MaxPlayers)
            {
                Debug.LogError($"{name}: キャラクター・ワイヤー・照準の表示が {MaxPlayers} 人分ありません", this);
                enabled = false;
                return;
            }
            _spawns = SpawnPointAuthoring.Collect();
            if (_spawns.Count == 0)
            {
                Debug.LogError($"{name}: ステージの出現位置（SpawnPointAuthoring）がありません", this);
                enabled = false;
                return;
            }
            var errors = new List<string>();
            _terrain = SceneTerrainBuilder.Build(_terrainMask, errors);
            _bounds = KnockoutBounds.Find(errors);
            _zones = StageZoneAuthoring.Collect(errors);
            _coinSpawns = CoinSpawnAuthoring.Collect(errors);
            _bankSpots = BankSpotAuthoring.Collect(errors);
            _platforms = StagePlatformAuthoring.Collect(errors);
            foreach (string error in errors)
            {
                Debug.LogError(error, this);
            }
            _match.Validate().ThrowIfInvalid();

            // 参加した人: App から始めたら参加の画面で決めた人、シーンを直接再生したら、今つながっている機器。
            _flow = FindFirstObjectByType<GameFlow>();
            if (_flow != null && _flow.LocalPlayers.Count > 0)
            {
                _players.AddRange(_flow.LocalPlayers);
                _timed = _flow.LocalTimed;
            }
            else
            {
                _players.AddRange(DevicePlayers(_roster.Count));
            }
            if (_players.Count > MaxPlayers)
            {
                _players.RemoveRange(MaxPlayers, _players.Count - MaxPlayers);
            }
            foreach (LocalPlayerSetup player in _players)
            {
                player.Character = _roster.Clamp(player.Character);
                // 人ごとに入力のアセットを複製し、その人の機器だけを読む（機器がなければ読まない）。
                InputActionAsset asset = Instantiate(_inputActions);
                asset.devices = player.Devices;
                _assets.Add(asset);
                _inputs.Add(new InputSystemAdapter(asset));
            }
            StartMatch();
        }

        private void OnEnable()
        {
            foreach (InputSystemAdapter input in _inputs)
            {
                input.Enable();
            }
        }

        private void OnDisable()
        {
            foreach (InputSystemAdapter input in _inputs)
            {
                input.Disable();
            }
        }

        private void OnDestroy()
        {
            foreach (InputSystemAdapter input in _inputs)
            {
                input.Dispose();
            }
            foreach (InputActionAsset asset in _assets)
            {
                Destroy(asset);
            }
        }

        /// <summary>
        /// シーンを直接再生したときの参加: キーボード＋マウスが P1、つながっているゲームパッドが P2 から。2 人に満たなければ、操作しない人を
        /// 足す（動かない相手）。キャラクターは番号の順に一覧から。
        /// </summary>
        public static List<LocalPlayerSetup> DevicePlayers(int characterCount)
        {
            var players = new List<LocalPlayerSetup>();
            if (Keyboard.current != null)
            {
                var devices = new List<InputDevice> { Keyboard.current };
                if (Mouse.current != null)
                {
                    devices.Add(Mouse.current);
                }
                players.Add(new LocalPlayerSetup(devices.ToArray(), "KEYBOARD", 0));
            }
            foreach (Gamepad gamepad in Gamepad.all)
            {
                players.Add(new LocalPlayerSetup(new InputDevice[] { gamepad }, $"GAMEPAD {players.Count + 1}", players.Count));
            }
            while (players.Count < 2)
            {
                players.Add(new LocalPlayerSetup(new InputDevice[0], "NONE", players.Count));
            }
            for (int i = 0; i < players.Count; i++)
            {
                players[i].Character = characterCount > 0 ? players[i].Character % characterCount : 0;
            }
            return players;
        }

        /// <summary>同じ人で試合を始め直す（IMatchControls）。</summary>
        public void Restart()
        {
            StartMatch();
        }

        /// <summary>キャラクター選択へ戻れるか（App のシーンから始めたときだけ。IMatchControls）。</summary>
        public bool CanReturnToCharacterSelect => _flow != null;

        /// <summary>同じ人（機器）とキャラクターで、ローカル対戦のキャラクター選択へ戻る（IMatchControls。docs/15 T6-8）。</summary>
        public void ReturnToCharacterSelect()
        {
            if (_flow != null && !_flow.IsLoading)
            {
                _flow.ReturnToLocalSelect();
            }
        }

        /// <summary>メニューへ戻る（App のシーンから始めていなければ、再生を止める。IMatchControls）。</summary>
        public void Quit()
        {
            if (_flow != null)
            {
                if (!_flow.IsLoading)
                {
                    _flow.ShowMenu();
                }
                return;
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>何もしない（対戦では出現位置に戻さない。IPauseTarget）。</summary>
        public void ResetPlayer()
        {
        }

        // 新しい試合のワールドを作る（試合ごとに乱数の種を変える。100 枚のコインの場所など）。
        private void StartMatch()
        {
            MatchRules rules = _match.Clone();
            if (!_timed)
            {
                rules.DurationTicks = 0;
            }
            rules.Seed = Environment.TickCount;
            _world = new SimWorld(_terrain, _bounds, _zones, rules, _coinSpawns, _bankSpots, _platforms);
            int count = _players.Count;
            for (int p = 0; p < count; p++)
            {
                Vector3 spawn = SpawnPosition(p, count);
                _world.AddCharacter(_roster.Get(_players[p].Character).Config, spawn.ToNumerics(), SpawnFacing(spawn, count));
            }
            _tickInputs = new SimInput[count];
            _previous = new CharacterState[count];
            _cpus = new CpuPlayer[count];
            for (int p = 0; p < count; p++)
            {
                if (_players[p].IsCpu)
                {
                    _cpus[p] = new CpuPlayer(p, CpuTuning.For(_players[p].CpuLevel), rules.Seed + p);
                }
            }
            for (int p = 0; p < count; p++)
            {
                _previous[p] = _world.GetState(p);
            }
            _accumulator = 0f;
            for (int p = 0; p < _characterViews.Length; p++)
            {
                bool shown = p < count;
                _characterViews[p].gameObject.SetActive(shown);
                _wireViews[p].gameObject.SetActive(shown);
                _aimIndicators[p].gameObject.SetActive(shown && !IsCpu(p));
            }
            Ticked?.Invoke();
        }

        // 出現位置: ステージの出現位置の番号の順（人数より少なければ、最初の位置から横に並べる）。
        private Vector3 SpawnPosition(int player, int count)
        {
            if (_spawns.Count >= count)
            {
                return _spawns[player];
            }
            return _spawns[0] + new Vector3((player - (count - 1) * 0.5f) * 3f, 0f, 0f);
        }

        // 出現したときの向き: 全員の出現位置の中心の方を向く。
        private int SpawnFacing(Vector3 spawn, int count)
        {
            float center = 0f;
            for (int p = 0; p < count; p++)
            {
                center += SpawnPosition(p, count).x;
            }
            return spawn.x <= center / count ? 1 : -1;
        }

        private void Update()
        {
            for (int p = 0; p < _inputs.Count && !_inputBlocked; p++)
            {
                _inputs[p].Poll(_camera, _world.GetConfig(p).Wire.AimDeadzone);
            }
            _accumulator += Time.deltaTime * SpeedScale;
            int ticks = 0;
            while (_accumulator >= SimTime.DeltaTime && ticks < MaxTicksPerFrame)
            {
                StepTick();
                _accumulator -= SimTime.DeltaTime;
                ticks++;
            }
            if (ticks == MaxTicksPerFrame && _accumulator >= SimTime.DeltaTime)
            {
                // 追いつけない分は捨てる（ゲームの時間が実時間より遅れる）。
                _accumulator = 0f;
            }
            Render(SpeedScale > 0f ? _accumulator / SimTime.DeltaTime : 1f);
        }

        private void StepTick()
        {
            for (int p = 0; p < _previous.Length; p++)
            {
                _previous[p] = _world.GetState(p);
                _tickInputs[p] = _inputs[p].ConsumeTick(_previous[p].Position);
                if (_cpus[p] != null)
                {
                    _tickInputs[p] = _cpus[p].Think(_world);
                }
            }
            _world.Step(_tickInputs);
            Ticked?.Invoke();
        }

        private void Render(float alpha)
        {
            for (int p = 0; p < _previous.Length; p++)
            {
                CharacterState current = _world.GetState(p);
                CharacterConfig config = _world.GetConfig(p);
                _characterViews[p].Render(_previous[p], current, alpha, config.Movement.HalfExtents.ToUnity());
                _characterViews[p].RenderAction(current, config.Combat, _world.Rules.RespawnDelayTicks, config.Movement);
                _characterViews[p].SetCostume(CharacterOf(p));
                _wireViews[p].Render(_previous[p], current, alpha, config.Wire);

                if (_cpus[p] != null)
                {
                    continue;
                }
                // 照準: 表示の位置（補間した位置）から、その人の今の入力で射出したときの結果を予測して示す（強化と置かれた物も含める）。
                Vector3 origin = Vector3.Lerp(_previous[p].Position.ToUnity(), current.Position.ToUnity(), alpha);
                System.Numerics.Vector3 simOrigin = origin.ToNumerics();
                var frame = new InputFrame(_inputs[p].Peek(simOrigin), current.PreviousButtons);
                WireTuning wire = Airerage.Core.Skills.SpeedBoosts.EffectiveConfig(current, config).Wire;
                WireShotPreview preview = WireTargeting.PredictShot(simOrigin, frame.Aim, current.Facing, wire, _world.Collision);
                _aimIndicators[p].Render(origin, current.Wire, preview, wire.MaxLength);
            }
        }
    }
}
