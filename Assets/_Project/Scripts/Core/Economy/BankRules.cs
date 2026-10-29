using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Common;
using Airerage.Core.Match;
using Airerage.Core.Simulation;

namespace Airerage.Core.Economy
{
    /// <summary>拠点（バンク）の候補の地点（docs/12_match_plan.md T4-7）。シーンに置く。</summary>
    public readonly struct BankSpot : IEquatable<BankSpot>, IComparable<BankSpot>
    {
        /// <summary>拠点の中心（m）。</summary>
        public readonly Vector2 Position;

        /// <summary>試合が始まったときの拠点か（並べると先頭になる。どれにもなければ、並べた順の最初）。</summary>
        public readonly bool IsFirst;

        public BankSpot(Vector2 position, bool isFirst = false)
        {
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
            {
                throw new ArgumentException($"拠点の位置が有限の値ではありません: {position}", nameof(position));
            }
            Position = position;
            IsFirst = isFirst;
        }

        /// <summary>決まった順（最初の拠点、X、Y の順）。どの PC でも同じ番号になるように、SimWorld が並べる。</summary>
        public int CompareTo(BankSpot other)
        {
            if (IsFirst != other.IsFirst)
            {
                return IsFirst ? -1 : 1;
            }
            int byX = Position.X.CompareTo(other.Position.X);
            return byX != 0 ? byX : Position.Y.CompareTo(other.Position.Y);
        }

        public bool Equals(BankSpot other)
        {
            return Position.Equals(other.Position) && IsFirst == other.IsFirst;
        }

        public override bool Equals(object obj)
        {
            return obj is BankSpot other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Position.GetHashCode() * 2 + (IsFirst ? 1 : 0);
        }

        public override string ToString()
        {
            return IsFirst ? $"Bank({Position}, first)" : $"Bank({Position})";
        }
    }

    /// <summary>
    /// 拠点（バンク。docs/12_match_plan.md T4-7、D-24、D-25）の決まり。拠点は1つで、候補の地点を移る。
    /// - 場所: ティック数とルールだけから決まる（状態を持たない）。試合が始まったときは最初の地点（BankSpot.IsFirst）。
    ///   MatchRules.BankMoveIntervalTicks（40 秒）ごとに、今と違う地点へ移る。移り先は試合の種（MatchRules.Seed）で選ぶ。
    ///   移る MatchRules.BankNoticeTicks（10 秒）前から、次の地点を予告する（表示だけ）。
    /// - 確定: 試合中、キャラクターの箱が拠点の円（半径 MatchRules.BankRadius）に触れていれば、所持コインを全部確定する
    ///   （毎ティック。中にいる間に拾ったコインもすぐ確定）。敵が中にいても止まらない。復帰を待っている人は確定しない。
    ///   SimWorld が、コインの拾得（FieldCoinRules）の後に呼ぶ。増減は CoinLedger.Bank を通す。
    /// </summary>
    public static class BankRules
    {
        // 移り先を選ぶ乱数の、ほかの選択と分けるための数。
        private const int MoveSalt = 200;

        /// <summary>ティック tick（それまでに進めたティック数。MatchRules.PhaseAt と同じ）で、試合が始まってから何回移ったか。</summary>
        public static int MovesAt(MatchRules rules, int tick)
        {
            int elapsed = Math.Max(0, tick - rules.CountdownTicks);
            return elapsed / rules.BankMoveIntervalTicks;
        }

        /// <summary>ティック tick での拠点の地点の番号（候補がなければ -1）。</summary>
        public static int SpotAt(MatchRules rules, int spotCount, int tick)
        {
            return SpotAfterMoves(rules, spotCount, MovesAt(rules, tick));
        }

        /// <summary>
        /// 次に移る地点の番号（候補が 1 つ以下なら、移らないので -1）と、移るまでのティック。
        /// 予告するのは、移るまでが MatchRules.BankNoticeTicks 以下のとき（IsNoticeAt）。
        /// </summary>
        public static int NextSpotAt(MatchRules rules, int spotCount, int tick, out int ticksUntilMove)
        {
            int elapsed = Math.Max(0, tick - rules.CountdownTicks);
            int moves = elapsed / rules.BankMoveIntervalTicks;
            ticksUntilMove = (moves + 1) * rules.BankMoveIntervalTicks - elapsed + Math.Max(0, rules.CountdownTicks - tick);
            return spotCount > 1 ? SpotAfterMoves(rules, spotCount, moves + 1) : -1;
        }

        /// <summary>ティック tick が、次の地点を予告する間か（候補が 2 つ以上で、移るまでが BankNoticeTicks 以下）。</summary>
        public static bool IsNoticeAt(MatchRules rules, int spotCount, int tick)
        {
            return NextSpotAt(rules, spotCount, tick, out int ticksUntilMove) >= 0 && ticksUntilMove <= rules.BankNoticeTicks;
        }

        /// <summary>moves 回移った後の地点の番号。最初は 0（並べた先頭）で、移るたびに今と違う地点を種で選ぶ。</summary>
        public static int SpotAfterMoves(MatchRules rules, int spotCount, int moves)
        {
            if (spotCount <= 0)
            {
                return -1;
            }
            int spot = 0;
            if (spotCount == 1)
            {
                return spot;
            }
            for (int k = 1; k <= moves; k++)
            {
                spot = (spot + 1 + StableHash.Range(rules.Seed, MoveSalt, k, spotCount - 1)) % spotCount;
            }
            return spot;
        }

        /// <summary>キャラクターの箱（中心 position、半分の大きさ half）が、拠点の円に触れているか。</summary>
        public static bool Touches(Vector3 position, Vector3 half, Vector2 bank, float radius)
        {
            float closestX = Math.Clamp(bank.X, position.X - half.X, position.X + half.X);
            float closestY = Math.Clamp(bank.Y, position.Y - half.Y, position.Y + half.Y);
            float dx = bank.X - closestX;
            float dy = bank.Y - closestY;
            return dx * dx + dy * dy <= radius * radius;
        }

        /// <param name="tick">このティック（進めた後のティック。取引のイベント ID に使う）。</param>
        /// <param name="playing">試合中か（確定は試合中だけ）。</param>
        public static void Step(CoinLedger ledger, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            IReadOnlyList<BankSpot> spots, MatchRules rules, int tick, bool playing)
        {
            if (!playing || spots == null || spots.Count == 0)
            {
                return;
            }
            // 進める前のティックの場所（表示が読む SpotAt(CurrentTick) と、進めた 1 ティックだけずれる）。
            Vector2 bank = spots[SpotAt(rules, spots.Count, tick - 1)].Position;
            for (int i = 0; i < states.Count; i++)
            {
                CharacterState state = states[i];
                if (state.Combat.IsWaitingToRespawn || state.Combat.IsAway || state.Coins.Held <= 0
                    || !Touches(state.Position, configs[i].Movement.HalfExtents, bank, rules.BankRadius))
                {
                    continue;
                }
                ledger.Bank(new CoinEventId(tick, CoinEventKind.Bank, i), states, i, state.Coins.Held);
            }
        }
    }
}
