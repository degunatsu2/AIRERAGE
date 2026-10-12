using System;
using System.Collections.Generic;
using System.Numerics;
using Degunatsu.Core.Input;
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
        private readonly List<CharacterState> _states = new List<CharacterState>();
        private readonly List<CharacterConfig> _configs = new List<CharacterConfig>();

        public SimWorld(ICollisionQuery collision)
        {
            _simulator = new CharacterSimulator(collision);
        }

        /// <summary>これまでに進めたティック数。</summary>
        public int Tick { get; private set; }

        public int CharacterCount => _states.Count;

        /// <summary>キャラクターを追加し、その番号を返す。調整値が不正なら InvalidTuningException。</summary>
        public int AddCharacter(CharacterConfig config, Vector3 spawnPosition, int facing = 1)
        {
            ValidateConfig(config);
            _configs.Add(config);
            _states.Add(CharacterState.Spawn(spawnPosition, facing));
            return _states.Count - 1;
        }

        public CharacterState GetState(int index)
        {
            return _states[index];
        }

        /// <summary>状態を直接書き換える（位置のリセット、テスト、ネットワークの照合で使う）。</summary>
        public void SetState(int index, CharacterState state)
        {
            _states[index] = state;
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

            for (int i = 0; i < _states.Count; i++)
            {
                CharacterState state = _states[i];
                _simulator.Step(ref state, inputs[i], _configs[i]);
                _states[i] = state;
            }
            Tick++;
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
