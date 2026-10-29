using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Economy;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Skills;

namespace Airerage.Core.Simulation
{
    /// <summary>
    /// シミュレーション全体。固定ティックで全キャラクターを進める。
    /// Unity 側の SimulationRunner や、ネットワークの予測・再計算から同じように呼ぶ。
    /// </summary>
    public sealed class SimWorld
    {
        private readonly CharacterSimulator _simulator;
        private readonly HitResolver _hits = new HitResolver();
        private readonly List<CharacterState> _states = new List<CharacterState>();
        private readonly List<CharacterConfig> _configs = new List<CharacterConfig>();
        private readonly List<CharacterState> _spawns = new List<CharacterState>();
        private readonly CoinField _coins = new CoinField();
        private readonly List<KnockoutRecord> _knockouts = new List<KnockoutRecord>();
        private readonly WorldObjects _objects = new WorldObjects();
        private readonly List<HitRecord> _projectileHits = new List<HitRecord>();
        private readonly WorldCollision _collision;
        private readonly List<StagePlatform> _platforms = new List<StagePlatform>();
        private readonly List<StageTrap> _traps = new List<StageTrap>();
        private readonly StageState _stage = new StageState();
        // 効かせるギミック（ステージのギミック ＋ THIEF が置いたギミック。毎ティックの初めに作り直す。ThiefRules.RefreshZones）。
        private readonly List<StageZone> _zoneList = new List<StageZone>();
        // 今の調整値（THIEF がコピーした Q を持っていれば、その技にしたもの。ThiefRules.ConfigFor）。状態から決まる。
        private readonly List<CharacterConfig> _tickConfigs = new List<CharacterConfig>();
        // 分身の状態（THIEF の E。キャラクターごと。持ち主の CombatState.CloneTicksLeft が 0 より大きい間だけ動く）。
        private readonly List<CharacterState> _clones = new List<CharacterState>();
        // 分身の動き（EASY の CPU の、移動とワイヤーだけ）で覚えていること（分身と一緒に保存して巻き戻す）と、その考え方。
        private readonly List<Ai.CpuMotionMemory> _cloneMinds = new List<Ai.CpuMotionMemory>();
        private readonly Ai.CpuMotion _cloneMotion = new Ai.CpuMotion(Ai.CpuTuning.For(ThiefRules.CloneCpuLevel));
        // このティックに押したボタン（THIEF のもう一度の E・R）。
        private readonly List<InputButtons> _pressed = new List<InputButtons>();
        private readonly ThiefContext _thief;
        private MatchState _match = MatchState.None;

        /// <param name="bounds">ステージの範囲。外に出たキャラクターは撃破になる（T3-3）。null なら撃破はない。</param>
        /// <param name="zones">ステージのギミック（ジャンプ台など）。渡した順によらず、決まった順（StageZone.CompareTo）に並べて効かせる。</param>
        /// <param name="rules">試合のルール（カウントダウン、制限時間、復帰、コインの出現。docs/12）。null なら MatchRules.FreePlay()。不正なら InvalidTuningException。</param>
        /// <param name="coinSpawns">コインの出現地点（docs/12 T4-4）。渡した順によらず、決まった順（CoinSpawnPoint.CompareTo）に並べて番号をつける。</param>
        /// <param name="bankSpots">拠点の候補の地点（docs/12 T4-7）。渡した順によらず、決まった順（BankSpot.CompareTo）に並べて番号をつける。</param>
        /// <param name="platforms">動く足場と扉（ユーザーの要望、2026-10-26）。渡した順によらず、決まった順（StagePlatform.CompareTo）に並べて番号をつける。</param>
        /// <param name="traps">ステージの仕掛け（赤外線とミサイル、ピッチャーマシン。ユーザーの要望、2026-10-26）。決まった順（StageTrap.CompareTo）に
        /// 並べて番号をつけ、ボールの道のりを collision（動かない地形）から求める。</param>
        public SimWorld(ICollisionQuery collision, StageBounds? bounds = null, IReadOnlyList<StageZone> zones = null, MatchRules rules = null,
            IReadOnlyList<CoinSpawnPoint> coinSpawns = null, IReadOnlyList<BankSpot> bankSpots = null,
            IReadOnlyList<StagePlatform> platforms = null, IReadOnlyList<StageTrap> traps = null)
        {
            if (platforms != null)
            {
                _platforms.AddRange(platforms);
                _platforms.Sort((a, b) => a.CompareTo(b));
            }
            if (traps != null)
            {
                _traps.AddRange(traps);
                _traps.Sort((a, b) => a.CompareTo(b));
                foreach (StageTrap trap in _traps)
                {
                    trap.Prepare(collision);
                }
            }
            _stage.Reset(_platforms.Count, _traps.Count);
            var banks = new List<BankSpot>();
            if (bankSpots != null)
            {
                banks.AddRange(bankSpots);
                banks.Sort((a, b) => a.CompareTo(b));
            }
            BankSpots = banks;
            var spawnPoints = new List<CoinSpawnPoint>();
            if (coinSpawns != null)
            {
                spawnPoints.AddRange(coinSpawns);
                spawnPoints.Sort((a, b) => a.CompareTo(b));
            }
            CoinSpawns = spawnPoints;
            Rules = rules ?? MatchRules.FreePlay();
            Rules.Validate().ThrowIfInvalid();
            var sorted = new List<StageZone>();
            if (zones != null)
            {
                sorted.AddRange(zones);
                sorted.Sort((a, b) => a.CompareTo(b));
            }
            Zones = sorted;
            _zoneList.AddRange(sorted);
            Coins = new CoinLedger(_coins);
            _collision = new WorldCollision(collision);
            _simulator = new CharacterSimulator(_collision, _zoneList);
            Bounds = bounds;
            _thief = new ThiefContext(sorted, _traps, Rules, BankSpots, _clones, _cloneMinds);
        }

        /// <summary>ワールドに置かれた物と投射物（docs/13 T5-6）。状態の一部（巻き戻しとチェックサムに乗る）。表示用に読むだけにする。</summary>
        public WorldObjects Objects => _objects;

        /// <summary>地形に、置かれた物のアンカーポイントを加えた当たりの調べ方（照準の表示の予測に使う）。</summary>
        public ICollisionQuery Collision => _collision;

        /// <summary>動く足場と扉（番号の順。CastHit.Body・WireState.AnchorBody は番号 + 1）。</summary>
        public IReadOnlyList<StagePlatform> Platforms => _platforms;

        /// <summary>ステージの仕掛け（番号の順）。</summary>
        public IReadOnlyList<StageTrap> Traps => _traps;

        /// <summary>扉の開き具合と仕掛けの時刻（状態の一部。巻き戻しとチェックサムに乗る）。表示用に読むだけにする。</summary>
        public StageState Stage => _stage;

        /// <summary>ステージの範囲（null なら撃破はない）。</summary>
        public StageBounds? Bounds { get; }

        /// <summary>ステージのギミック（効かせる順）。</summary>
        public IReadOnlyList<StageZone> Zones { get; }

        /// <summary>
        /// 今効かせているギミック（ステージのギミック Zones の後に、THIEF が置いたギミックの写しが続く。次の Step の初めに作り直す）。
        /// 表示用に読むだけにする。
        /// </summary>
        public IReadOnlyList<StageZone> ActiveZones => _zoneList;

        /// <summary>試合のルール。</summary>
        public MatchRules Rules { get; }

        /// <summary>コイン台帳（コインの増減はすべてここを通す。docs/12 T4-3）。状態は CoinField と各キャラクターの Coins。</summary>
        public CoinLedger Coins { get; }

        /// <summary>コインの出現地点（番号の順）。</summary>
        public IReadOnlyList<CoinSpawnPoint> CoinSpawns { get; }

        /// <summary>拠点の候補の地点（番号の順。先頭が試合の始まりの拠点）。今の拠点は BankRules.SpotAt(Rules, BankSpots.Count, Tick)。</summary>
        public IReadOnlyList<BankSpot> BankSpots { get; }

        /// <summary>次に進めるティックの、試合の段階（ティック数、ルール、MatchState から決まる。MatchFlow.PhaseAt）。</summary>
        public MatchPhase Phase => MatchFlow.PhaseAt(Rules, _match, Tick);

        /// <summary>アディショナルタイムと勝者（docs/12 T4-8）。状態の一部（巻き戻しとチェックサムに乗る）。</summary>
        public MatchState Match => _match;

        /// <summary>直前の Step で当たった命中（表示とデバッグ用。状態ではないので、巻き戻しでは戻らない）。</summary>
        public IReadOnlyList<HitRecord> LastHits => _hits.LastHits;

        /// <summary>直前の Step の撃破（表示とデバッグ用。状態ではない）。</summary>
        public IReadOnlyList<KnockoutRecord> LastKnockouts => _knockouts;

        /// <summary>これまでに進めたティック数。</summary>
        public int Tick { get; private set; }

        public int CharacterCount => _states.Count;

        /// <summary>キャラクターを追加し、その番号を返す。調整値が不正なら InvalidTuningException。</summary>
        public int AddCharacter(CharacterConfig config, Vector3 spawnPosition, int facing = 1)
        {
            ValidateConfig(config);
            _configs.Add(config);
            _tickConfigs.Add(config);
            CharacterState spawn = CharacterState.Spawn(spawnPosition, facing);
            _spawns.Add(spawn);
            _states.Add(spawn);
            _clones.Add(default);
            _cloneMinds.Add(Ai.CpuMotionMemory.Initial);
            _pressed.Add(InputButtons.None);
            return _states.Count - 1;
        }

        public CharacterState GetState(int index)
        {
            return _states[index];
        }

        /// <summary>キャラクター index の分身の状態（THIEF の E。持ち主の CombatState.CloneTicksLeft が 0 より大きい間だけ意味がある）。</summary>
        public CharacterState GetClone(int index)
        {
            return _clones[index];
        }

        /// <summary>キャラクター index の分身が出ているか。</summary>
        public bool HasClone(int index)
        {
            return _states[index].Combat.CloneTicksLeft > 0;
        }

        /// <summary>状態を直接書き換える（位置のリセット、テスト、入力の再生で使う）。</summary>
        public void SetState(int index, CharacterState state)
        {
            _states[index] = state;
            RefreshConfig(index);
        }

        /// <summary>全状態（ティック数、全キャラクターの状態、ワールド全体のコイン）を snapshot に書き写す（ロールバックの保存）。</summary>
        public void SaveTo(WorldSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            snapshot.Capture(Tick, _states, _coins, _match, _objects, _stage, _clones, _cloneMinds);
        }

        /// <summary>snapshot の全状態に戻す（ロールバックの巻き戻し）。キャラクターの数が違えば ArgumentException。</summary>
        public void LoadFrom(WorldSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            if (snapshot.CharacterCount != _states.Count)
            {
                throw new ArgumentException(
                    $"スナップショットのキャラクターの数（{snapshot.CharacterCount}）がワールド（{_states.Count}）と違います", nameof(snapshot));
            }
            for (int i = 0; i < _states.Count; i++)
            {
                _states[i] = snapshot.GetState(i);
                _clones[i] = snapshot.GetClone(i);
                _cloneMinds[i] = snapshot.GetCloneMind(i);
            }
            _coins.CopyFrom(snapshot.Coins);
            _objects.CopyFrom(snapshot.Objects);
            if (snapshot.Stage.Count == _stage.Count && snapshot.Stage.TrapCount == _stage.TrapCount)
            {
                _stage.CopyFrom(snapshot.Stage);
            }
            _match = snapshot.Match;
            Tick = snapshot.Tick;
            RefreshConfigs();
            ThiefRules.RefreshZones(_zoneList, Zones, _objects);
        }

        /// <summary>今の全状態のチェックサム（WorldSnapshot.Checksum と同じ値）。</summary>
        public ulong Checksum()
        {
            return StateCodec.WorldChecksum(Tick, _states, _coins, _match, _objects, _stage, _clones, _cloneMinds);
        }

        /// <summary>
        /// キャラクター index の今の調整値（THIEF がコピーした Q・奪ったギミックを持っていれば、Q をその技にしたもの。ThiefRules.ConfigFor）。
        /// 表示と CPU はこちらを使う。
        /// </summary>
        public CharacterConfig GetConfig(int index)
        {
            return _tickConfigs[index];
        }

        /// <summary>キャラクター index のもとの調整値（SetConfig・AddCharacter で渡したもの）。</summary>
        public CharacterConfig GetBaseConfig(int index)
        {
            return _configs[index];
        }

        public void SetConfig(int index, CharacterConfig config)
        {
            ValidateConfig(config);
            _configs[index] = config;
            RefreshConfig(index);
        }

        private void RefreshConfigs()
        {
            for (int i = 0; i < _states.Count; i++)
            {
                RefreshConfig(i);
            }
        }

        private void RefreshConfig(int index)
        {
            _tickConfigs[index] = ThiefRules.ConfigFor(_states[index], _configs[index], _configs);
        }

        /// <summary>
        /// 1ティック進める。inputs はキャラクターの番号順で、数が一致しなければ ArgumentException。
        /// 全員を進めた後に、攻撃の命中を決めて当て（HitResolver）、ステージの外に出たキャラクターを撃破にする（KnockoutResolver）。
        /// 被弾と撃破でコインを落とし（CoinDropRules。docs/12 T4-6）、終わったコンボと撃破の報酬を払う（CombatRewardRules。T4-5）。
        /// 最後に、フィールドのコインの出現・拾得・消滅を決め（FieldCoinRules。docs/12 T4-4）、拠点に触れている人の所持コインを
        /// 確定する（BankRules。T4-7）。
        /// 技の効果と置かれた物（docs/13 T5-6）: 全員を進めた後に効果を起こし、物を進め（投射物の命中は HitResolver で一緒に当てる）、
        /// 命中の後に寿命が来た・壊れた物を取り除く（SkillRules）。効率のボーナスはティックの終わりに加える（CoinBonusRules）。
        /// 試合の段階（Phase）がカウントダウンの間は入力を無視し、終わった後は全員を止める（ティック数だけ進める）。
        /// 時間切れの瞬間に、アディショナルタイムに入るか終わるかを決め、アディショナルタイム中は勝者と終わりを決める（MatchFlow。T4-8）。
        /// </summary>
        public void Step(IReadOnlyList<PlayerInput> inputs)
        {
            if (inputs == null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }
            if (inputs.Count != _states.Count)
            {
                throw new ArgumentException(
                    $"入力の数（{inputs.Count}）がキャラクターの数（{_states.Count}）と一致しません", nameof(inputs));
            }

            MatchPhase phase = Phase;
            Coins.BeginTick(Tick + 1);
            _knockouts.Clear();
            if (phase == MatchPhase.Finished)
            {
                _hits.Clear();
                Tick++;
                return;
            }
            // THIEF が置いたギミックも、ステージのギミックと一緒に効かせる（このティックの初めの物から決める）。
            ThiefRules.RefreshZones(_zoneList, Zones, _objects);
            // 置かれた物のアンカーポイントは、このティックの初めの物から決める（巻き戻しても全員で同じ）。
            _collision.Refresh(_objects);
            // 動く足場と扉: このティックの位置の当たりを作り、動いた分だけ乗っている人とつないだワイヤーを運ぶ。
            _collision.SetPlatforms(_platforms, _stage, Tick);
            StagePlatforms.Carry(_platforms, _stage, _states, _tickConfigs, Tick);
            for (int i = 0; i < _states.Count; i++)
            {
                CharacterState state = _states[i];
                if (inputs[i].IsHeld(InputButtons.Leave) && !state.Combat.HasLeft)
                {
                    // 試合から抜けた（ネットワーク対戦。全員が同じティックからこの入力を使う。docs/15 T6-10）。
                    state = KnockoutResolver.Departed(state, _spawns[i]);
                }
                PlayerInput input = phase == MatchPhase.Countdown ? default : inputs[i];
                if (state.Combat.IsAway)
                {
                    // メーデーで吊られている間（JUGGERNAUT の R）は、動かずに運ばれる先を選ぶ（JuggernautRules）。
                    _pressed[i] = InputButtons.None;
                    JuggernautRules.StepMayday(ref state, input, _tickConfigs[i], _collision, Bounds);
                    _states[i] = state;
                    continue;
                }
                // このティックに押したボタン（止まっている間は、押したことにしない）。
                _pressed[i] = state.Combat.HitstopTicksLeft > 0 ? InputButtons.None : input.Buttons & ~state.PreviousButtons;
                // 行動の速さの強化の間は、速さを上げた調整値で動かす（SpeedBoosts）。
                _simulator.Step(ref state, input, SpeedBoosts.EffectiveConfig(state, _tickConfigs[i]), Tick, Rules.Seed);
                _states[i] = state;
            }
            // THIEF の分身（移動とワイヤーだけ）。
            ThiefRules.StepClones(this, _clones, _cloneMinds, _cloneMotion, _states, _simulator, _configs, Bounds, Tick, Rules.Seed);
            Tick++;
            // ステージの仕掛け（赤外線とミサイル、ピッチャーマシンのボール）。
            StageTraps.Step(_traps, _stage, _states, _tickConfigs, Tick);
            // ステージの危険に当たった人が、威力に応じてコインを落とす。
            CoinDropRules.DropOnHazards(Coins, _states, Rules, Tick);
            SkillRules.FireEffects(_objects, Coins, _states, _tickConfigs, Tick, _thief, _collision);
            _projectileHits.Clear();
            SkillRules.StepObjects(_objects, _collision, _states, _tickConfigs, Bounds, Tick, _projectileHits);
            ThiefRules.StepObjects(_objects, _collision, _states, _tickConfigs, Bounds, Tick, _projectileHits, _thief, _stage);
            JuggernautRules.StepObjects(_objects, _collision, _states, _tickConfigs, Bounds, Tick, _projectileHits, _platforms, _stage, _pressed);
            _hits.Resolve(_states, _tickConfigs, Tick, _objects, Rules.Seed, _projectileHits);
            ThiefRules.PopClones(_clones, _states, _tickConfigs, _objects, Tick);
            SkillRules.EndObjects(_objects, Coins, _states, _tickConfigs, Rules, Tick, BankSpots,
                phase == MatchPhase.Playing || phase == MatchPhase.Overtime);
            CoinDropRules.DropOnHits(Coins, _states, _hits.LastHits, Rules, Tick);
            if (Bounds.HasValue)
            {
                KnockoutResolver.Resolve(_states, _spawns, Bounds.Value, Rules, _knockouts, _tickConfigs);
                CoinDropRules.DropOnKnockouts(Coins, _states, _knockouts, Bounds.Value, CoinSpawns, Rules, Tick);
            }
            CombatRewardRules.Step(Coins, _states, _hits.LastEndedCombos, _knockouts, Rules, Tick, _tickConfigs);
            bool active = phase == MatchPhase.Playing || phase == MatchPhase.Overtime;
            FieldCoinRules.Step(Coins, _states, _tickConfigs, CoinSpawns, Rules, Tick, active);
            if (phase == MatchPhase.Overtime)
            {
                MatchFlow.StepOvertime(ref _match, _states, _tickConfigs, BankSpots, Rules, Tick);
            }
            BankRules.Step(Coins, _states, _tickConfigs, BankSpots, Rules, Tick, active);
            ThiefRules.StepHeist(Coins, _states, _tickConfigs, _thief, Tick, active, _pressed);
            ThiefRules.AfterStep(_states, _tickConfigs, _configs, _pressed, _thief, Tick, active);
            CoinBonusRules.Mint(Coins, _states, Tick);
            StagePlatforms.UpdateDoors(_platforms, _stage, _states, _tickConfigs, Tick);
            if (phase == MatchPhase.Playing && Rules.HasTimeLimit && Tick == Rules.CountdownTicks + Rules.DurationTicks)
            {
                MatchFlow.DecideAtTimeUp(ref _match, _states, Tick);
            }
            RefreshConfigs();
        }

        private static void ValidateConfig(CharacterConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            config.Validate();
        }
    }
}
