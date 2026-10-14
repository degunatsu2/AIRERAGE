using System.Collections.Generic;
using System.Numerics;
using Degunatsu.Core.Match;
using Degunatsu.Core.Simulation;

namespace Degunatsu.Core.Combat
{
    /// <summary>
    /// ステージの外に出たキャラクターを撃破にする（docs/11_combat_plan.md T3-3、docs/12_match_plan.md T4-2）。
    /// SimWorld が命中を当てた後に呼ぶ。
    /// - 撃破の手柄は、最後にダメージを与えたキャラクター（D-23）。期限はなく、どれだけ前の攻撃でも手柄になる（D-40）。
    ///   だれにも攻撃されていなければ、手柄はだれにもつかない（扱いは Phase 4 で決める）。
    /// - 撃破されたキャラクターは出現位置へ移り、MatchRules.RespawnDelayTicks の間は復帰を待つ（動かず、攻撃も受けない。
    ///   表示では見えない）。復帰した後は MatchRules.InvincibleTicks の間、無敵（CharacterSimulator が数える）。
    ///   撃破の数とゲージ【仮】を引き継ぎ、ワイヤー、攻撃、最後にダメージを与えた人などは戻す。
    /// - 復帰を待っている間は、外に出ても撃破にしない。
    /// - 番号の順に調べる（同じティックに複数が外に出ても、結果が順番で変わらない）。
    /// </summary>
    public static class KnockoutResolver
    {
        /// <summary>
        /// spawns はキャラクターごとの出現したときの状態（同じ番号・同じ数）。knockouts を渡すと、このティックの撃破を番号の順に加える
        /// （撃破ドロップに使う。docs/12 T4-6）。
        /// </summary>
        public static void Resolve(List<CharacterState> states, IReadOnlyList<CharacterState> spawns, StageBounds bounds, MatchRules rules,
            List<KnockoutRecord> knockouts = null)
        {
            for (int i = 0; i < states.Count; i++)
            {
                CharacterState state = states[i];
                if (state.Combat.IsWaitingToRespawn || bounds.Contains(state.Position))
                {
                    continue;
                }
                if (state.Combat.HasLastAttacker)
                {
                    CharacterState attacker = states[state.Combat.LastAttacker];
                    attacker.Combat.Knockouts++;
                    states[state.Combat.LastAttacker] = attacker;
                }
                knockouts?.Add(new KnockoutRecord(i, state.Combat.HasLastAttacker ? state.Combat.LastAttacker : -1,
                    new Vector2(state.Position.X, state.Position.Y)));
                states[i] = Respawn(states[i], spawns[i], rules);
            }
        }

        /// <summary>
        /// 出現したときの状態に戻し、復帰を待たせる。撃破の数（撃破された数は +1）、ゲージ、前のティックのボタン、ギミックの数、
        /// コイン、まだ払っていないダメージ報酬の元を引き継ぐ。
        /// 無敵は、待つ間と復帰した後の両方の長さにする。
        /// </summary>
        private static CharacterState Respawn(in CharacterState state, in CharacterState spawn, MatchRules rules)
        {
            CharacterState respawned = spawn;
            respawned.Combat.Knockouts = state.Combat.Knockouts;
            respawned.Combat.Falls = state.Combat.Falls + 1;
            respawned.Combat.Gauge = state.Combat.Gauge;
            respawned.Combat.RespawnTicksLeft = rules.RespawnDelayTicks;
            respawned.Combat.InvincibleTicksLeft = rules.RespawnDelayTicks + rules.InvincibleTicks;
            // 押し続けているボタンを、戻った直後に押したとみなさない（ワイヤーの射出などが勝手に起きないように）。
            respawned.PreviousButtons = state.PreviousButtons;
            respawned.JumpPadCount = state.JumpPadCount;
            respawned.BoostRingCount = state.BoostRingCount;
            // コインはここではなくさない（撃破ドロップは CoinDropRules が、台帳を通して落とす。T4-6）。
            respawned.Coins = state.Coins;
            // 撃破で終わったコンボのダメージ報酬は、このティックの後で CombatRewardRules が払う（T4-5）。
            respawned.Combat.RewardDamage = state.Combat.RewardDamage;
            respawned.Combat.RewardAttacker = state.Combat.RewardAttacker;
            return respawned;
        }
    }

    /// <summary>撃破1つ（KnockoutResolver.Resolve）。</summary>
    public readonly struct KnockoutRecord
    {
        public readonly int Victim;

        /// <summary>手柄のキャラクター（だれにも攻撃されていなければ -1。自滅）。</summary>
        public readonly int Attacker;

        /// <summary>ステージの外に出た位置。</summary>
        public readonly Vector2 Position;

        public KnockoutRecord(int victim, int attacker, Vector2 position)
        {
            Victim = victim;
            Attacker = attacker;
            Position = position;
        }
    }
}
