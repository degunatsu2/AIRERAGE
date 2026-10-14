using System;
using System.Collections.Generic;
using System.Numerics;
using Degunatsu.Core.Economy;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Input;
using Degunatsu.Core.Match;
using Degunatsu.Core.Physics;

namespace Degunatsu.Core.Simulation
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

        /// <param name="bounds">ステージの範囲。外に出たキャラクターは撃破になる（T3-3）。null なら撃破はない。</param>
        /// <param name="zones">ステージのギミック（ジャンプ台など）。渡した順によらず、決まった順（StageZone.CompareTo）に並べて効かせる。</param>
        /// <param name="rules">試合のルール（カウントダウン、制限時間、復帰、コインの出現。docs/12）。null なら MatchRules.FreePlay()。不正なら InvalidTuningException。</param>
        /// <param name="coinSpawns">コインの出現地点（docs/12 T4-4）。渡した順によらず、決まった順（CoinSpawnPoint.CompareTo）に並べて番号をつける。</param>
        /// <param name="bankSpots">拠点の候補の地点（docs/12 T4-7）。渡した順によらず、決まった順（BankSpot.CompareTo）に並べて番号をつける。</param>
        public SimWorld(ICollisionQuery collision, StageBounds? bounds = null, IReadOnlyList<StageZone> zones = null, MatchRules rules = null,
            IReadOnlyList<CoinSpawnPoint> coinSpawns = null, IReadOnlyList<BankSpot> bankSpots = null)
        {
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
            Coins = new CoinLedger(_coins);
            _simulator = new CharacterSimulator(collision, sorted);
            Bounds = bounds;
        }

        /// <summary>ステージの範囲（null なら撃破はない）。</summary>
        public StageBounds? Bounds { get; }

        /// <summary>ステージのギミック（効かせる順）。</summary>
        public IReadOnlyList<StageZone> Zones { get; }

        /// <summary>試合のルール。</summary>
        public MatchRules Rules { get; }

        /// <summary>コイン台帳（コインの増減はすべてここを通す。docs/12 T4-3）。状態は CoinField と各キャラクターの Coins。</summary>
        public CoinLedger Coins { get; }

        /// <summary>コインの出現地点（番号の順）。</summary>
        public IReadOnlyList<CoinSpawnPoint> CoinSpawns { get; }

        /// <summary>拠点の候補の地点（番号の順。先頭が試合の始まりの拠点）。今の拠点は BankRules.SpotAt(Rules, BankSpots.Count, Tick)。</summary>
        public IReadOnlyList<BankSpot> BankSpots { get; }

        /// <summary>次に進めるティックの、試合の段階（ティック数だけから決まる）。</summary>
        public MatchPhase Phase => Rules.PhaseAt(Tick);

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
            CharacterState spawn = CharacterState.Spawn(spawnPosition, facing);
            _spawns.Add(spawn);
            _states.Add(spawn);
            return _states.Count - 1;
        }

        public CharacterState GetState(int index)
        {
            return _states[index];
        }

        /// <summary>状態を直接書き換える（位置のリセット、テスト、入力の再生で使う）。</summary>
        public void SetState(int index, CharacterState state)
        {
            _states[index] = state;
        }

        /// <summary>全状態（ティック数、全キャラクターの状態、ワールド全体のコイン）を snapshot に書き写す（ロールバックの保存）。</summary>
        public void SaveTo(WorldSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            snapshot.Capture(Tick, _states, _coins);
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
            }
            _coins.CopyFrom(snapshot.Coins);
            Tick = snapshot.Tick;
        }

        /// <summary>今の全状態のチェックサム（WorldSnapshot.Checksum と同じ値）。</summary>
        public ulong Checksum()
        {
            return StateCodec.WorldChecksum(Tick, _states, _coins);
        }

        public CharacterConfig GetConfig(int index)
        {
            return _configs[index];
        }

        public void SetConfig(int index, CharacterConfig config)
        {
            ValidateConfig(config);
            _configs[index] = config;
        }

        /// <summary>
        /// 1ティック進める。inputs はキャラクターの番号順で、数が一致しなければ ArgumentException。
        /// 全員を進めた後に、攻撃の命中を決めて当て（HitResolver）、ステージの外に出たキャラクターを撃破にする（KnockoutResolver）。
        /// 被弾と撃破でコインを落とす（CoinDropRules。docs/12 T4-6）。
        /// 最後に、フィールドのコインの出現・拾得・消滅を決め（FieldCoinRules。docs/12 T4-4）、拠点に触れている人の所持コインを
        /// 確定する（BankRules。T4-7）。
        /// 試合の段階（Phase）がカウントダウンの間は入力を無視し、終わった後は全員を止める（ティック数だけ進める）。
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
            for (int i = 0; i < _states.Count; i++)
            {
                CharacterState state = _states[i];
                _simulator.Step(ref state, phase == MatchPhase.Countdown ? default : inputs[i], _configs[i]);
                _states[i] = state;
            }
            Tick++;
            _hits.Resolve(_states, _configs, Tick);
            CoinDropRules.DropOnHits(Coins, _states, _hits.LastHits, Rules, Tick);
            if (Bounds.HasValue)
            {
                KnockoutResolver.Resolve(_states, _spawns, Bounds.Value, Rules, _knockouts);
                CoinDropRules.DropOnKnockouts(Coins, _states, _knockouts, Bounds.Value, Rules, Tick);
            }
            FieldCoinRules.Step(Coins, _states, _configs, CoinSpawns, Rules, Tick, phase == MatchPhase.Playing);
            BankRules.Step(Coins, _states, _configs, BankSpots, Rules, Tick, phase == MatchPhase.Playing);
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
