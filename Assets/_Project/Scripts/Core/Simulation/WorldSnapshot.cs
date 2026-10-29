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
        private CharacterState[] _clones = new CharacterState[0];
        private Ai.CpuMotionMemory[] _cloneMinds = new Ai.CpuMotionMemory[0];

        /// <summary>このスナップショットのティック数（次に計算するティック）。</summary>
        public int Tick { get; private set; }

        public int CharacterCount => _states.Length;

        /// <summary>ワールド全体のコイン（フィールドのコインなど）の写し。</summary>
        public CoinField Coins { get; } = new CoinField();

        /// <summary>置かれた物と投射物の写し。</summary>
        public WorldObjects Objects { get; } = new WorldObjects();

        /// <summary>アディショナルタイムと勝者の写し。</summary>
        public MatchState Match { get; private set; } = MatchState.None;

        /// <summary>扉の開き具合（StageState。扉のないステージでは空）。</summary>
        public StageState Stage { get; } = new StageState();

        public CharacterState GetState(int index)
        {
            if (index < 0 || index >= CharacterCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"キャラクターの番号は 0〜{CharacterCount - 1}");
            }
            return _states[index];
        }

        /// <summary>キャラクター index の分身の状態（THIEF の E。写していなければ既定の値）。</summary>
        public CharacterState GetClone(int index)
        {
            return index >= 0 && index < _clones.Length ? _clones[index] : default;
        }

        /// <summary>キャラクター index の分身の動きで覚えていること（写していなければ何も覚えていない状態）。</summary>
        public Ai.CpuMotionMemory GetCloneMind(int index)
        {
            return index >= 0 && index < _cloneMinds.Length ? _cloneMinds[index] : Ai.CpuMotionMemory.Initial;
        }

        /// <summary>全状態のチェックサム（ティック数と全キャラクターの状態。PC が違っても同じ値）。</summary>
        public ulong Checksum()
        {
            return StateCodec.WorldChecksum(Tick, _states, Coins, Match, Objects, Stage, _clones, _cloneMinds);
        }

        internal void Capture(int tick, IReadOnlyList<CharacterState> states, CoinField coins, MatchState match, WorldObjects objects,
            StageState stage = null, IReadOnlyList<CharacterState> clones = null, IReadOnlyList<Ai.CpuMotionMemory> cloneMinds = null)
        {
            int cloneCount = clones != null ? clones.Count : 0;
            if (_clones.Length != cloneCount)
            {
                _clones = new CharacterState[cloneCount];
            }
            for (int i = 0; i < cloneCount; i++)
            {
                _clones[i] = clones[i];
            }
            int mindCount = cloneMinds != null ? cloneMinds.Count : 0;
            if (_cloneMinds.Length != mindCount)
            {
                _cloneMinds = new Ai.CpuMotionMemory[mindCount];
            }
            for (int i = 0; i < mindCount; i++)
            {
                _cloneMinds[i] = cloneMinds[i];
            }
            if (stage != null)
            {
                Stage.CopyFrom(stage);
            }
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
