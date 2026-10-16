using System;
using System.Collections.Generic;
using Airerage.Core.Economy;
using Airerage.Core.Match;
using Airerage.Core.Objects;

namespace Airerage.Core.Simulation
{
    /// <summary>
    /// SimWorld の全状態（ティック数、全キャラクターの状態、ワールド全体のコイン、試合の進み具合）の写し（docs/10_online_plan.md T2-1）。
    /// ロールバックで巻き戻すために保存する。毎ティック作り直さずに済むよう、同じインスタンスに上書きして使い回せる。
    /// 調整値は試合中に変わらない前提なので含めない。
    /// </summary>
    public sealed class WorldSnapshot
    {
        private CharacterState[] _states = new CharacterState[0];

        /// <summary>このスナップショットのティック数（次に計算するティック）。</summary>
        public int Tick { get; private set; }

        public int CharacterCount => _states.Length;

        /// <summary>ワールド全体のコイン（フィールドのコインなど）の写し。</summary>
        public CoinField Coins { get; } = new CoinField();

        /// <summary>置かれた物と投射物の写し。</summary>
        public WorldObjects Objects { get; } = new WorldObjects();

        /// <summary>アディショナルタイムと勝者の写し。</summary>
        public MatchState Match { get; private set; } = MatchState.None;

        public CharacterState GetState(int index)
        {
            if (index < 0 || index >= CharacterCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"キャラクターの番号は 0〜{CharacterCount - 1}");
            }
            return _states[index];
        }

        /// <summary>全状態のチェックサム（ティック数と全キャラクターの状態。PC が違っても同じ値）。</summary>
        public ulong Checksum()
        {
            return StateCodec.WorldChecksum(Tick, _states, Coins, Match, Objects);
        }

        internal void Capture(int tick, IReadOnlyList<CharacterState> states, CoinField coins, MatchState match, WorldObjects objects)
        {
            Coins.CopyFrom(coins);
            Objects.CopyFrom(objects);
            Match = match;
            if (_states.Length != states.Count)
            {
                _states = new CharacterState[states.Count];
            }
            for (int i = 0; i < states.Count; i++)
            {
                _states[i] = states[i];
            }
            Tick = tick;
        }
    }
}
