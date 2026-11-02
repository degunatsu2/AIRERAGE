using System;
using System.Collections.Generic;
using Airerage.Core.Input;
using Airerage.Core.Simulation;

namespace Airerage.Core.Rollback
{
    /// <summary>
    /// 観戦（docs/19。ユーザーの要望、2026-11-02）: 試合に参加しない人が、確定した全員の入力（RollbackSession.TickConfirmed をホストが
    /// 送ったもの）でワールドを進める。予測も巻き戻しもしない（確定した入力だけなので、参加している人と同じ結果になる）。その分、
    /// 参加している人より少し遅れて見える。通信はしない（届いた入力を AddInputs で渡す）。
    /// </summary>
    public sealed class SpectatorSession
    {
        private readonly SimWorld _world;
        private readonly Dictionary<int, PlayerInput[]> _pending = new Dictionary<int, PlayerInput[]>();

        /// <param name="world">参加している人と同じに作ったワールド（キャラクターも同じ順に追加し、まだ進めていない）。</param>
        public SpectatorSession(SimWorld world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            if (world.CharacterCount < 1)
            {
                throw new ArgumentException("ワールドにキャラクターがいません", nameof(world));
            }
        }

        public SimWorld World => _world;

        /// <summary>次に計算するティック。</summary>
        public int CurrentTick => _world.Tick;

        /// <summary>今のティックから途切れずに届いている入力のティックの数（すぐに進められる数）。</summary>
        public int BufferedTicks
        {
            get
            {
                int count = 0;
                while (_pending.ContainsKey(_world.Tick + count))
                {
                    count++;
                }
                return count;
            }
        }

        /// <summary>次のティックの入力が届いているか。</summary>
        public bool CanStep => _pending.ContainsKey(_world.Tick);

        /// <summary>
        /// ティック tick の全員の入力（プレイヤーの番号の順。人数はワールドのキャラクターの数）を受け取る（写して持つ）。もう計算したティックは
        /// 捨てる。同じティックに違う入力が届いたら InvalidOperationException（送り手の誤り）。
        /// </summary>
        public void AddInputs(int tick, IReadOnlyList<PlayerInput> inputs)
        {
            if (inputs == null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }
            if (inputs.Count != _world.CharacterCount)
            {
                throw new ArgumentException($"入力の数（{inputs.Count}）が人数（{_world.CharacterCount}）と違う", nameof(inputs));
            }
            if (tick < _world.Tick)
            {
                return;
            }
            if (_pending.TryGetValue(tick, out PlayerInput[] existing))
            {
                for (int p = 0; p < existing.Length; p++)
                {
                    if (!existing[p].Equals(inputs[p]))
                    {
                        throw new InvalidOperationException($"ティック {tick} のプレイヤー {p} に、前と違う入力が届きました");
                    }
                }
                return;
            }
            var copy = new PlayerInput[inputs.Count];
            for (int p = 0; p < copy.Length; p++)
            {
                copy[p] = inputs[p];
            }
            _pending[tick] = copy;
        }

        /// <summary>次のティックの入力が届いていれば、1 ティック進めて true。届いていなければ何もせず false。</summary>
        public bool Step()
        {
            int tick = _world.Tick;
            if (!_pending.TryGetValue(tick, out PlayerInput[] inputs))
            {
                return false;
            }
            _pending.Remove(tick);
            _world.Step(inputs);
            return true;
        }
    }
}
