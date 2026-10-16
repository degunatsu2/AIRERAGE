using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Simulation;

namespace Airerage.Core.Economy
{
    /// <summary>
    /// コインの効率のボーナス（フィーバータイム。docs/14 1.3、C-17）。効率が上がっている間（CombatState.CoinBoostTicksLeft）に
    /// 拾ったコインと戦闘の報酬に、BonusPercent % を足す。端数は 1/1000 枚単位でため（CombatState.CoinBonusMilli）、ティックの終わりに
    /// 1 枚以上になった分を台帳の CoinBonus（生成）で所持コインに加える。拠点での確定には足さない。
    /// </summary>
    public static class CoinBonusRules
    {
        /// <summary>amount 枚を得たときのボーナスをためる（効率が上がっていなければ何もしない）。</summary>
        public static void Accrue(ref CharacterState state, CharacterConfig config, int amount)
        {
            if (state.Combat.CoinBoostTicksLeft <= 0 || amount <= 0)
            {
                return;
            }
            state.Combat.CoinBonusMilli += amount * config.Skills.CoinBoost.BonusPercent * 10;
        }

        /// <summary>ティックの終わりに、1 枚以上になったボーナスを所持コインに加える。tick は進めた後のティック。</summary>
        public static void Mint(CoinLedger ledger, List<CharacterState> states, int tick)
        {
            for (int i = 0; i < states.Count; i++)
            {
                CharacterState state = states[i];
                int amount = state.Combat.CoinBonusMilli / 1000;
                if (amount <= 0)
                {
                    continue;
                }
                state.Combat.CoinBonusMilli -= amount * 1000;
                states[i] = state;
                ledger.Reward(new CoinEventId(tick, CoinEventKind.CoinBonus, i), states, i, amount);
            }
        }
    }
}
