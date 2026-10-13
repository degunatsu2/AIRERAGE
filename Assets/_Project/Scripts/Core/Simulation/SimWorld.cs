using System;
using System.Collections.Generic;
using System.Numerics;
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

        /// <param name="bounds">ステージの範囲。外に出たキャラクターは撃破になる（T3-3）。null なら撃破はない。</param>
        /// <param name="zones">ステージのギミック（ジャンプ台など）。渡した順によらず、決まった順（StageZone.CompareTo）に並べて効かせる。</param>
        /// <param name="rules">試合のルール（カウントダウン、制限時間、復帰。docs/12）。null なら MatchRules.FreePlay()。不正なら InvalidTuningException。</param>
        public SimWorld(ICollisionQuery collision, StageBounds? bounds = null, IReadOnlyList<StageZone> zones = null, MatchRules rules = null)
        {
            Rules = rules ?? MatchRules.FreePlay();
            Rules.Validate().ThrowIfInvalid();
            var sorted = new List<StageZone>();
            if (zones != null)
            {
                sorted.AddRange(zones);
                sorted.Sort((a, b) => a.CompareTo(b));
            }
            Zones = sorted;
            _simulator = new CharacterSimulator(collision, sorted);
            Bounds = bounds;
        }

        /// <summary>ステージの範囲（null なら撃破はない）。</summary>
        public StageBounds? Bounds { get; }

        /// <summary>ステージのギミック（効かせる順）。</summary>
        public IReadOnlyList<StageZone> Zones { get; }

        /// <summary>試合のルール。</summary>
        public MatchRules Rules { get; }

        /// <summary>次に進めるティックの、試合の段階（ティック数だけから決まる）。</summary>
        public MatchPhase Phase => Rules.PhaseAt(Tick);

        /// <summary>直前の Step で当たった命中（表示とデバッグ用。状態ではないので、巻き戻しでは戻らない）。</summary>
        public IReadOnlyList<HitRecord> LastHits => _hits.LastHits;

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

        /// <summary>全状態（ティック数と全キャラクターの状態）を snapshot に書き写す（ロールバックの保存）。</summary>
        public void SaveTo(WorldSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            snapshot.Capture(Tick, _states);
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
            Tick = snapshot.Tick;
        }

        /// <summary>今の全状態のチェックサム（WorldSnapshot.Checksum と同じ値）。</summary>
        public ulong Checksum()
        {
            return StateCodec.WorldChecksum(Tick, _states);
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
            if (Bounds.HasValue)
            {
                KnockoutResolver.Resolve(_states, _spawns, Bounds.Value, Rules);
            }
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
