using System.Collections.Generic;
using Airerage.Core.Simulation;

namespace Airerage.Core.Match
{
    /// <summary>
    /// 試合の順位（docs/12_match_plan.md T4-1、T4-7）: スコア（確定コイン。D-25）が多い順。同じなら仮に【仮】、撃破した数が
    /// 多い順、撃破された数が少ない順、番号の小さい順（同点のときのアディショナルタイムは T4-8 で作る。D-45）。
    /// 状態だけから決まるので、全員の画面で同じ順位になる。
    /// </summary>
    public static class MatchResults
    {
        /// <summary>
        /// 順位の順に並べた、キャラクターの番号。winner（アディショナルタイムで勝った人。なければ -1）がいれば、いちばん上（チーム対戦なら
        /// そのチームの全員が上）。チーム対戦ならチームの確定の合計の順で、同じチームの人は並べてまとめる（TeamRules.ScoreOf）。
        /// </summary>
        public static int[] Rank(IReadOnlyList<CharacterState> states, int winner = -1)
        {
            var order = new int[states.Count];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }
            // 挿入ソート（人数は 8 人まで。並べ方を言語の実装に任せず、同じ順になるようにする）。
            for (int i = 1; i < order.Length; i++)
            {
                int current = order[i];
                int j = i - 1;
                while (j >= 0 && Before(states, current, order[j], winner))
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = current;
            }
            return order;
        }

        /// <summary>a が b より上の順位か。</summary>
        private static bool Before(IReadOnlyList<CharacterState> states, int a, int b, int winner)
        {
            bool aWins = winner >= 0 && TeamRules.Allies(states, a, winner);
            bool bWins = winner >= 0 && TeamRules.Allies(states, b, winner);
            if (aWins != bWins)
            {
                return aWins;
            }
            int aScore = TeamRules.ScoreOf(states, a);
            int bScore = TeamRules.ScoreOf(states, b);
            if (aScore != bScore)
            {
                return aScore > bScore;
            }
            byte aTeam = states[a].Combat.Team;
            byte bTeam = states[b].Combat.Team;
            if (aTeam != bTeam)
            {
                // 同じ合計のチームどうしは、チームの番号の順（同じチームの人を並べてまとめる）。
                return aTeam < bTeam;
            }
            CharacterState sa = states[a];
            CharacterState sb = states[b];
            if (sa.Coins.Banked != sb.Coins.Banked)
            {
                return sa.Coins.Banked > sb.Coins.Banked;
            }
            if (sa.Combat.Knockouts != sb.Combat.Knockouts)
            {
                return sa.Combat.Knockouts > sb.Combat.Knockouts;
            }
            if (sa.Combat.Falls != sb.Combat.Falls)
            {
                return sa.Combat.Falls < sb.Combat.Falls;
            }
            return a < b;
        }
    }
}
