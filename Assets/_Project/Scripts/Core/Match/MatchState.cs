using System;
using System.Collections.Generic;
using Airerage.Core.Economy;
using Airerage.Core.Simulation;

namespace Airerage.Core.Match
{
    /// <summary>
    /// 試合の進み具合のうち、ティック数とルールだけでは決まらないもの（アディショナルタイムと勝者。docs/12_match_plan.md T4-8）。
    /// ワールドの状態に持ち、巻き戻しとチェックサムに乗せる。値がないものは -1（None）。
    /// </summary>
    public struct MatchState : IEquatable<MatchState>
    {
        /// <summary>アディショナルタイムが始まったティック（なければ -1）。</summary>
        public int OvertimeStartTick;

        /// <summary>試合が終わったティック（このティックからは Finished。まだなら -1）。</summary>
        public int EndTick;

        /// <summary>アディショナルタイムで拠点にたどり着いて勝った人（なければ -1。順位は確定コインで決める）。</summary>
        public int Winner;

        /// <summary>始まったときの状態（どれもまだない）。</summary>
        public static MatchState None => new MatchState { OvertimeStartTick = -1, EndTick = -1, Winner = -1 };

        public bool HasOvertime => OvertimeStartTick >= 0;
        public bool HasEnded => EndTick >= 0;

        public bool Equals(MatchState other)
        {
            return OvertimeStartTick == other.OvertimeStartTick && EndTick == other.EndTick && Winner == other.Winner;
        }

        public override bool Equals(object obj)
        {
            return obj is MatchState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(OvertimeStartTick, EndTick, Winner);
        }

        public override string ToString()
        {
            return $"overtime={OvertimeStartTick} end={EndTick} winner={Winner}";
        }
    }

    /// <summary>
    /// 試合の段階と、アディショナルタイムの決まり（docs/12_match_plan.md 4.2、T4-8。D-25、D-45 とその細部の決定）。
    /// - 時間切れ（カウントダウン＋試合の長さのティック）で、確定コインで順位をつける。逆転できる人がいれば
    ///   アディショナルタイム（MatchRules.OvertimeTicks。5 秒）に入る。いなければ、そこで終わる。
    /// - 逆転できる人: 確定＋所持が、ほかの人の確定の最大以上（同点も含む。D-45）。ただし、ただ一人の首位（確定がほかの全員より
    ///   多い人）は数えない（首位は、拠点に着けば勝ちで終わるが、首位がいるだけではアディショナルタイムに入らない）。
    /// - アディショナルタイム中: 拠点に触れた人のうち、確定＋所持がほかの人の確定の最大以上の人がいれば、その人の勝ちで終わる
    ///   （首位が着いても首位の勝ち。同じティックに何人もいれば、確定＋所持が多い人、同じなら番号の小さい人）。逆転できる人が
    ///   いなくなったら、その場で終わる。時間が過ぎたら終わる（確定コインの順位のまま）。拠点は移動し続ける。
    /// 段階は、ティック数とルールと MatchState から決まる（PhaseAt）。
    /// </summary>
    public static class MatchFlow
    {
        /// <summary>ティック tick（それまでに進めたティック数）からの1ティックが、試合のどの段階か。</summary>
        public static MatchPhase PhaseAt(MatchRules rules, in MatchState state, int tick)
        {
            if (tick < rules.CountdownTicks)
            {
                return MatchPhase.Countdown;
            }
            if (state.HasEnded && tick >= state.EndTick)
            {
                return MatchPhase.Finished;
            }
            if (!rules.HasTimeLimit || tick < rules.CountdownTicks + rules.DurationTicks)
            {
                return MatchPhase.Playing;
            }
            return state.HasOvertime ? MatchPhase.Overtime : MatchPhase.Finished;
        }

        /// <summary>
        /// 今の段階の残り（ティック）: カウントダウン中は始まるまで、試合中は時間切れまで（時間の制限がなければ -1）、
        /// アディショナルタイム中はその終わりまで、終わった後は 0。
        /// </summary>
        public static int TicksLeftAt(MatchRules rules, in MatchState state, int tick)
        {
            switch (PhaseAt(rules, state, tick))
            {
                case MatchPhase.Countdown:
                    return rules.CountdownTicks - tick;
                case MatchPhase.Playing:
                    return rules.HasTimeLimit ? rules.CountdownTicks + rules.DurationTicks - tick : -1;
                case MatchPhase.Overtime:
                    return Math.Max(0, state.OvertimeStartTick + rules.OvertimeTicks - tick);
                default:
                    return 0;
            }
        }

        /// <summary>コインの出現が増える終盤か（試合の残りが MatchRules.LateGameTicks 以下、またはアディショナルタイム）。</summary>
        public static bool IsLateGame(MatchRules rules, int tick)
        {
            return rules.HasTimeLimit && rules.LateGameTicks > 0
                && tick >= rules.CountdownTicks + rules.DurationTicks - rules.LateGameTicks;
        }

        /// <summary>キャラクター index が、拠点にたどり着けば勝てるか（確定＋所持が、ほかの人の確定の最大以上）。</summary>
        public static bool CanWinAtTheBank(IReadOnlyList<CharacterState> states, int index)
        {
            int total = states[index].Coins.Banked + states[index].Coins.Held;
            return total >= BestOtherBanked(states, index);
        }

        /// <summary>ただ一人の首位（確定がほかの全員より多い）か。</summary>
        public static bool IsSoleLeader(IReadOnlyList<CharacterState> states, int index)
        {
            return states.Count == 1 || states[index].Coins.Banked > BestOtherBanked(states, index);
        }

        /// <summary>逆転できる人（ただ一人の首位を除いて、拠点にたどり着けば勝てる人）がいるか。</summary>
        public static bool AnyChallenger(IReadOnlyList<CharacterState> states)
        {
            for (int i = 0; i < states.Count; i++)
            {
                if (!IsSoleLeader(states, i) && CanWinAtTheBank(states, i))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 時間切れの瞬間に呼ぶ（tick は時間切れのティック）。逆転できる人がいればアディショナルタイムを始め、いなければ終える。
        /// </summary>
        public static void DecideAtTimeUp(ref MatchState state, IReadOnlyList<CharacterState> states, int tick)
        {
            if (state.HasOvertime || state.HasEnded)
            {
                return;
            }
            if (AnyChallenger(states))
            {
                state.OvertimeStartTick = tick;
            }
            else
            {
                state.EndTick = tick;
            }
        }

        /// <summary>
        /// アディショナルタイムの1ティック（SimWorld が、コインの拾得の後・拠点での確定の前に呼ぶ）。tick は進めた後のティック。
        /// 勝つ人が拠点に着いた、逆転できる人がいなくなった、時間が過ぎた、のどれかで試合を終える（次のティックから Finished）。
        /// </summary>
        public static void StepOvertime(ref MatchState state, IReadOnlyList<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            IReadOnlyList<BankSpot> spots, MatchRules rules, int tick)
        {
            if (!state.HasOvertime || state.HasEnded)
            {
                return;
            }
            int winner = -1;
            int best = -1;
            if (spots != null && spots.Count > 0)
            {
                System.Numerics.Vector2 bank = spots[BankRules.SpotAt(rules, spots.Count, tick - 1)].Position;
                for (int i = 0; i < states.Count; i++)
                {
                    CharacterState s = states[i];
                    if (s.Combat.IsWaitingToRespawn || s.Combat.IsAway || !CanWinAtTheBank(states, i)
                        || !BankRules.Touches(s.Position, configs[i].Movement.HalfExtents, bank, rules.BankRadius))
                    {
                        continue;
                    }
                    int total = s.Coins.Banked + s.Coins.Held;
                    if (total > best)
                    {
                        best = total;
                        winner = i;
                    }
                }
            }
            if (winner >= 0)
            {
                state.Winner = winner;
                state.EndTick = tick;
            }
            else if (!AnyChallenger(states) || tick >= state.OvertimeStartTick + rules.OvertimeTicks)
            {
                state.EndTick = tick;
            }
        }

        private static int BestOtherBanked(IReadOnlyList<CharacterState> states, int index)
        {
            int best = 0;
            for (int i = 0; i < states.Count; i++)
            {
                if (i != index)
                {
                    best = Math.Max(best, states[i].Coins.Banked);
                }
            }
            return best;
        }
    }
}
