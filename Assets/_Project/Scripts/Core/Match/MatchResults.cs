using System.Collections.Generic;
using Degunatsu.Core.Simulation;

namespace Degunatsu.Core.Match
{
    /// <summary>
    /// 試合の順位（docs/12_match_plan.md T4-1、T4-7）: スコア（確定コイン。D-25）が多い順。同じなら仮に【仮】、撃破した数が
    /// 多い順、撃破された数が少ない順、番号の小さい順（同点のときのアディショナルタイムは T4-8 で作る。D-45）。
    /// 状態だけから決まるので、全員の画面で同じ順位になる。
    /// </summary>
    public static class MatchResults
    {
        /// <summary>順位の順に並べた、キャラクターの番号。</summary>
        public static int[] Rank(IReadOnlyList<CharacterState> states)
        {
            var order = new int[states.Count];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }
            // 挿入ソート（人数は 4 人まで。並べ方を言語の実装に任せず、同じ順になるようにする）。
            for (int i = 1; i < order.Length; i++)
            {
                int current = order[i];
                int j = i - 1;
                while (j >= 0 && Before(states[current], current, states[order[j]], order[j]))
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = current;
            }
            return order;
        }

        /// <summary>a が b より上の順位か。</summary>
        private static bool Before(in CharacterState a, int aIndex, in CharacterState b, int bIndex)
        {
            if (a.Coins.Banked != b.Coins.Banked)
            {
                return a.Coins.Banked > b.Coins.Banked;
            }
            if (a.Combat.Knockouts != b.Combat.Knockouts)
            {
                return a.Combat.Knockouts > b.Combat.Knockouts;
            }
            if (a.Combat.Falls != b.Combat.Falls)
            {
                return a.Combat.Falls < b.Combat.Falls;
            }
            return aIndex < bIndex;
        }
    }
}
