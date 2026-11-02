using System;
using System.Collections.Generic;
using Airerage.Core.Simulation;

namespace Airerage.Core.Match
{
    /// <summary>
    /// チーム対戦（docs/19_team_plan.md。D-78〜D-86 は推奨どおりに決定、2026-11-02）の決まり。チームの番号は CharacterState.Combat.Team
    /// （1〜MaxTeams。0 ならチームのない個人戦）で、試合の初めに決まる（SimWorld.SetTeam）。
    /// - 味方（同じチームの人。Allies）には、攻撃・投射物・置かれた物の害のある効果が当たらない（D-81）。例外は THIEF の Q の 1 段目の弾で、
    ///   味方にも当たって Q をコピーする（ダメージはない。D-82）。
    /// - 手持ち（所持）のコインは一人ずつ、確定コインはチームの合計（ScoreOf。一人ずつの確定の記録も残る。D-80）。順位・勝敗・アディショナル
    ///   タイム・撃破報酬の順位の差・逆転の恩恵は、チームの合計で比べる（D-83、D-86）。チームがなければ一人ずつの確定コインで、今までと同じ。
    /// 状態とルールだけから決まる（巻き戻しても全員で同じ）。
    /// </summary>
    public static class TeamRules
    {
        /// <summary>チームの数の上限（待機所の 4 色）。</summary>
        public const int MaxTeams = 4;

        /// <summary>チーム対戦か（だれかにチームの番号がある）。</summary>
        public static bool IsTeamMatch(IReadOnlyList<CharacterState> states)
        {
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].Combat.Team != 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>状態 a と b の人が、同じチームの味方か（チームのない人どうしは味方ではない。同じ人かは調べない）。</summary>
        public static bool Allies(in CharacterState a, in CharacterState b)
        {
            return a.Combat.Team != 0 && a.Combat.Team == b.Combat.Team;
        }

        /// <summary>a と b が同じ人か、同じチームの味方か。</summary>
        public static bool Allies(IReadOnlyList<CharacterState> states, int a, int b)
        {
            if (a == b)
            {
                return true;
            }
            if (a < 0 || b < 0 || a >= states.Count || b >= states.Count)
            {
                return false;
            }
            byte team = states[a].Combat.Team;
            return team != 0 && team == states[b].Combat.Team;
        }

        /// <summary>
        /// キャラクター index の順位をつける確定コイン: チーム対戦ならチームの全員（抜けた人も含む。入れたコインは残る）の確定の合計、
        /// 個人戦なら本人の確定。
        /// </summary>
        public static int ScoreOf(IReadOnlyList<CharacterState> states, int index)
        {
            byte team = states[index].Combat.Team;
            if (team == 0)
            {
                return states[index].Coins.Banked;
            }
            int total = 0;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].Combat.Team == team)
                {
                    total += states[i].Coins.Banked;
                }
            }
            return total;
        }

        /// <summary>
        /// チーム（個人戦なら本人）の確定＋所持の合計（アディショナルタイムの「拠点に着けば逆転できるか」に使う。所持はチームの全員の分を
        /// 足す: 味方が確定すればチームの確定になるので）。
        /// </summary>
        public static int PotentialOf(IReadOnlyList<CharacterState> states, int index)
        {
            byte team = states[index].Combat.Team;
            if (team == 0)
            {
                return states[index].Coins.Banked + states[index].Coins.Held;
            }
            int total = 0;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].Combat.Team != team)
                {
                    continue;
                }
                // 抜けた人の所持コインは、もう確定できないので数えない。
                total += states[i].Coins.Banked + (states[i].Combat.HasLeft ? 0 : states[i].Coins.Held);
            }
            return total;
        }

        /// <summary>
        /// キャラクター index の順位（1 から）: 自分より ScoreOf が多い相手（チーム対戦ならほかのチーム、個人戦ならほかの人）の数 + 1。同じなら
        /// 同じ順位。skipLeft なら、試合から抜けた人（チームのない人だけ。チームは残った人がいれば続く）を数えない。
        /// </summary>
        public static int RankOf(IReadOnlyList<CharacterState> states, int index, bool skipLeft)
        {
            int score = ScoreOf(states, index);
            int rank = 1;
            uint counted = 0;
            for (int i = 0; i < states.Count; i++)
            {
                if (Allies(states, index, i))
                {
                    continue;
                }
                byte team = states[i].Combat.Team;
                if (team == 0 && skipLeft && states[i].Combat.HasLeft)
                {
                    continue;
                }
                // チームは 1 回だけ数える（番号 0〜3 のビット）。チームのない人は、人ごと（番号 8 からのビット）。
                uint bit = team != 0 ? 1u << (team - 1) : 1u << (8 + i);
                if ((counted & bit) != 0)
                {
                    continue;
                }
                counted |= bit;
                if (ScoreOf(states, i) > score)
                {
                    rank++;
                }
            }
            return rank;
        }

        /// <summary>
        /// index と順位を争う相手（別のチームの人。個人戦なら自分以外）のうち、ScoreOf がいちばん多い値（相手がいなければ 0）。
        /// </summary>
        public static int BestRivalScore(IReadOnlyList<CharacterState> states, int index)
        {
            int best = 0;
            for (int i = 0; i < states.Count; i++)
            {
                if (!Allies(states, index, i))
                {
                    best = Math.Max(best, ScoreOf(states, i));
                }
            }
            return best;
        }

        /// <summary>
        /// 均等に分けるボタン（D-78。ユーザーの決定、2026-11-02）: 人数配分 sizes（チームごとの人数。合計が人数 count と同じ）のとおりに、
        /// count 人を種 seed で並べ替えて順に割り当てる。返り値は人ごとのチームの番号（1 から）。配分が合わなければ例外。
        /// </summary>
        public static byte[] Distribute(int count, IReadOnlyList<int> sizes, int seed)
        {
            if (sizes == null || sizes.Count < 2 || sizes.Count > MaxTeams)
            {
                throw new ArgumentException($"チームは 2〜{MaxTeams} 個（{sizes?.Count ?? 0} 個）", nameof(sizes));
            }
            int total = 0;
            foreach (int size in sizes)
            {
                if (size < 1)
                {
                    throw new ArgumentException("どのチームにも 1 人以上が要る", nameof(sizes));
                }
                total += size;
            }
            if (total != count)
            {
                throw new ArgumentException($"人数配分の合計（{total}）が人数（{count}）と違う", nameof(sizes));
            }
            // 人の並びを、種で決まる順に並べ替える（Fisher-Yates。StableHash で決まるので全員で同じ）。
            var order = new int[count];
            for (int i = 0; i < count; i++)
            {
                order[i] = i;
            }
            for (int i = count - 1; i > 0; i--)
            {
                int j = Common.StableHash.Range(seed, 7700, i, i + 1);
                int swap = order[i];
                order[i] = order[j];
                order[j] = swap;
            }
            var teams = new byte[count];
            int next = 0;
            for (int t = 0; t < sizes.Count; t++)
            {
                for (int k = 0; k < sizes[t]; k++)
                {
                    teams[order[next++]] = (byte)(t + 1);
                }
            }
            return teams;
        }
    }
}
