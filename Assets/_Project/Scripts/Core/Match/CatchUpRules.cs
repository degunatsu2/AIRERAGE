using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Simulation;

namespace Airerage.Core.Match
{
    /// <summary>
    /// 逆転の要素（ユーザーの要望、2026-11-02）。
    /// - 順位の恩恵: 確定コインの順位（RankOf。自分より確定コインが多い人の数 + 1。同じなら同じ順位）が 2 位なら毎秒 0.5%、3 位なら 1.0%、
    ///   4 位以下なら 2.0% のゲージを得る（MatchRules.CatchUpGaugeRank2〜4）。1 位は何もない。試合中とアディショナルタイムに、試合が始まって
    ///   から 1 秒ごと（カウントダウンの後から数えて 60 ティックごと）に加える。試合から抜けた人は順位にも数えない。
    /// - WANTED: 所持コインが MatchRules.WantedHeldCoins（100 枚）以上の人（IsWanted）。被弾ドロップが倍になる（CoinDropRules.HitDropAmount）。
    ///   順位の恩恵は確定コインを増やさずに持ち歩くほうが得になるので、持ち歩きすぎを不利にする。
    /// どちらも状態（確定・所持コイン）とティック数とルールだけから決まり、ほかの状態を持たない（巻き戻しても全員で同じ）。
    /// </summary>
    public static class CatchUpRules
    {
        /// <summary>所持コイン held の人が WANTED か。</summary>
        public static bool IsWanted(MatchRules rules, int held)
        {
            return rules != null && held >= rules.WantedHeldCoins;
        }

        /// <summary>キャラクター index の確定コインの順位（1 位から。抜けた人は数えない。index が抜けていれば 0）。</summary>
        public static int RankOf(IReadOnlyList<CharacterState> states, int index)
        {
            if (states[index].Combat.HasLeft)
            {
                return 0;
            }
            int banked = states[index].Coins.Banked;
            int rank = 1;
            for (int i = 0; i < states.Count; i++)
            {
                if (i != index && !states[i].Combat.HasLeft && states[i].Coins.Banked > banked)
                {
                    rank++;
                }
            }
            return rank;
        }

        /// <summary>順位 rank の人が 1 秒ごとに得るゲージ（0.1% 単位。1 位と、順位のない人は 0）。</summary>
        public static int GaugePerSecond(MatchRules rules, int rank)
        {
            switch (rank)
            {
                case 2: return rules.CatchUpGaugeRank2;
                case 3: return rules.CatchUpGaugeRank3;
                default: return rank >= 4 ? rules.CatchUpGaugeRank4 : 0;
            }
        }

        /// <summary>
        /// 拠点の確定の後に呼ぶ（tick は進めた後のティック）。playing（試合中かアディショナルタイム）で、試合が始まってから 1 秒たつごとに、
        /// 順位に応じたゲージを加える（全員の順位を先に決めてから加える。番号の順）。
        /// </summary>
        public static void Step(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, MatchRules rules, int tick, bool playing)
        {
            int elapsed = tick - rules.CountdownTicks;
            if (!playing || elapsed <= 0 || elapsed % SimTime.TicksPerSecond != 0)
            {
                return;
            }
            // 順位はゲージで変わらないので、加えながら決めても同じ。
            for (int i = 0; i < states.Count; i++)
            {
                int amount = GaugePerSecond(rules, RankOf(states, i));
                if (amount <= 0)
                {
                    continue;
                }
                CharacterState state = states[i];
                CombatTuning.AddGauge(ref state.Combat, amount, configs[i].Skills.CoinBoost);
                states[i] = state;
            }
        }
    }
}
