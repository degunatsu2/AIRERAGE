using System.Collections.Generic;
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
        /// <summary>spawns はキャラクターごとの出現したときの状態（同じ番号・同じ数）。</summary>
        public static void Resolve(List<CharacterState> states, IReadOnlyList<CharacterState> spawns, StageBounds bounds, MatchRules rules)
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
                states[i] = Respawn(states[i], spawns[i], rules);
            }
        }

        /// <summary>
        /// 出現したときの状態に戻し、復帰を待たせる。撃破の数（撃破された数は +1）、ゲージ、前のティックのボタン、ギミックの数、
        /// コインを引き継ぐ。
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
            // コインはなくさない（撃破のドロップは T4-6 で、台帳を通して落とす）。
            respawned.Coins = state.Coins;
            return respawned;
        }
    }
}
