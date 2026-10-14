using System;
using System.Collections.Generic;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Match;
using Degunatsu.Core.Simulation;

namespace Degunatsu.Core.Economy
{
    /// <summary>
    /// 戦闘の報酬（docs/12_match_plan.md T4-5、docs/01_game_design.md 3.3。ユーザーの決定、2026-10-14）。増減はすべて
    /// CoinLedger.Reward を通す（生成）。同じ相手からの上限と減衰はない。
    /// - ダメージ報酬: コンボが終わったときに、そのコンボのダメージの MatchRules.DamageRewardPercent（端数は切り捨て、少なくとも
    ///   1 枚）を、当てていた人の所持コインに加える。コンボが終わるのは、のけぞりが終わったとき、別の人の新しいコンボになったとき
    ///   （HitResolver.LastEndedCombos）、撃破されたとき。ガードされた攻撃では払わない。
    /// - 撃破報酬: 撃破の手柄の人（最後にダメージを与えた人。D-23、D-40）の所持コインに、KoRewardBase を加える。撃破した相手が
    ///   自分より上の順位（確定コインの順。同じ数なら同じ順位）にいれば、順位の差 × KoRewardPerRank（上限 KoRewardMaxBonus）を
    ///   上乗せする。自滅（手柄なし）では払わない。
    /// SimWorld が、撃破とドロップを決めた後に呼ぶ（払う順は、置き換わったコンボ、終わったコンボ（番号の順）、撃破（番号の順））。
    /// </summary>
    public static class CombatRewardRules
    {
        /// <summary>ダメージ damage のコンボが終わったときのダメージ報酬（枚）。</summary>
        public static int DamageRewardAmount(MatchRules rules, int damage)
        {
            if (damage <= 0 || rules.DamageRewardPercent <= 0)
            {
                return 0;
            }
            return Math.Max(1, damage * rules.DamageRewardPercent / 100);
        }

        /// <summary>キャラクター index の順位（1 から。確定コインがより多い人の数 + 1。同じ数なら同じ順位）。</summary>
        public static int RankOf(IReadOnlyList<CharacterState> states, int index)
        {
            int banked = states[index].Coins.Banked;
            int rank = 1;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].Coins.Banked > banked)
                {
                    rank++;
                }
            }
            return rank;
        }

        /// <summary>attacker が victim を撃破したときの撃破報酬（枚。自分自身なら 0）。順位は今の確定コインで決める。</summary>
        public static int KoRewardAmount(MatchRules rules, IReadOnlyList<CharacterState> states, int attacker, int victim)
        {
            if (attacker < 0 || attacker == victim)
            {
                return 0;
            }
            int above = RankOf(states, attacker) - RankOf(states, victim);
            int bonus = above > 0 ? Math.Min(rules.KoRewardMaxBonus, above * rules.KoRewardPerRank) : 0;
            return rules.KoRewardBase + bonus;
        }

        /// <param name="endedCombos">このティックに、別の人の新しいコンボに置き換わって終わったコンボ（HitResolver.LastEndedCombos）。</param>
        /// <param name="knockouts">このティックの撃破（KnockoutResolver）。</param>
        /// <param name="tick">このティック（進めた後のティック）。</param>
        public static void Step(CoinLedger ledger, List<CharacterState> states, IReadOnlyList<ComboEnd> endedCombos,
            IReadOnlyList<KnockoutRecord> knockouts, MatchRules rules, int tick)
        {
            // 置き換わったコンボ。同じ人が1ティックに何回も置き換わることがあるので、連番は一覧の中の番号にする。
            for (int i = 0; i < endedCombos.Count; i++)
            {
                ComboEnd end = endedCombos[i];
                PayDamage(ledger, states, end.Attacker, end.Victim, end.Damage, i, rules, tick);
            }

            // のけぞりが終わった（撃破されて復帰を待っている）コンボ。
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState victim = states[v];
                if (victim.Combat.RewardDamage <= 0
                    || (victim.Action.Kind == ActionKind.Hitstun && !victim.Combat.IsWaitingToRespawn))
                {
                    continue;
                }
                int damage = victim.Combat.RewardDamage;
                int attacker = victim.Combat.RewardAttacker;
                victim.Combat.RewardDamage = 0;
                states[v] = victim;
                PayDamage(ledger, states, attacker, v, damage, endedCombos.Count, rules, tick);
            }

            // 撃破報酬。報酬は所持コインに入るので、順位（確定コイン）は払っても変わらない。
            foreach (KnockoutRecord ko in knockouts)
            {
                int amount = KoRewardAmount(rules, states, ko.Attacker, ko.Victim);
                if (amount > 0)
                {
                    ledger.Reward(new CoinEventId(tick, CoinEventKind.KoReward, ko.Victim), states, ko.Attacker, amount);
                }
            }
        }

        private static void PayDamage(CoinLedger ledger, List<CharacterState> states, int attacker, int victim, int damage, int sequence,
            MatchRules rules, int tick)
        {
            int amount = DamageRewardAmount(rules, damage);
            if (amount > 0 && attacker >= 0 && attacker < states.Count && attacker != victim)
            {
                ledger.Reward(new CoinEventId(tick, CoinEventKind.DamageReward, victim, sequence), states, attacker, amount);
            }
        }
    }
}
