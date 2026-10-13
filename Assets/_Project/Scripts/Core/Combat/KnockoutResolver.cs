using System.Collections.Generic;
using Degunatsu.Core.Simulation;

namespace Degunatsu.Core.Combat
{
    /// <summary>
    /// ステージの外に出たキャラクターを撃破にする（docs/11_combat_plan.md T3-3）。SimWorld が命中を当てた後に呼ぶ。
    /// - 撃破の手柄は、最後にダメージを与えたキャラクター（D-23）。期限はなく、どれだけ前の攻撃でも手柄になる（D-40）。
    ///   だれにも攻撃されていなければ、手柄はだれにもつかない（扱いは Phase 4 で決める）。
    /// - 撃破されたキャラクターは、すぐに出現位置に戻る（確認用の復帰。復帰後の無敵などは Phase 4）。
    ///   撃破の数とゲージ【仮】を引き継ぎ、ワイヤー、攻撃、最後にダメージを与えた人などは戻す。
    /// - 番号の順に調べる（同じティックに複数が外に出ても、結果が順番で変わらない）。
    /// </summary>
    public static class KnockoutResolver
    {
        /// <summary>spawns はキャラクターごとの出現したときの状態（同じ番号・同じ数）。</summary>
        public static void Resolve(List<CharacterState> states, IReadOnlyList<CharacterState> spawns, StageBounds bounds)
        {
            for (int i = 0; i < states.Count; i++)
            {
                CharacterState state = states[i];
                if (bounds.Contains(state.Position))
                {
                    continue;
                }
                if (state.Combat.HasLastAttacker)
                {
                    CharacterState attacker = states[state.Combat.LastAttacker];
                    attacker.Combat.Knockouts++;
                    states[state.Combat.LastAttacker] = attacker;
                }
                states[i] = Respawn(states[i], spawns[i]);
            }
        }

        /// <summary>出現したときの状態に戻す。撃破の数（撃破された数は +1）、ゲージ、前のティックのボタン、ギミックの数を引き継ぐ。</summary>
        private static CharacterState Respawn(in CharacterState state, in CharacterState spawn)
        {
            CharacterState respawned = spawn;
            respawned.Combat.Knockouts = state.Combat.Knockouts;
            respawned.Combat.Falls = state.Combat.Falls + 1;
            respawned.Combat.Gauge = state.Combat.Gauge;
            // 押し続けているボタンを、戻った直後に押したとみなさない（ワイヤーの射出などが勝手に起きないように）。
            respawned.PreviousButtons = state.PreviousButtons;
            respawned.JumpPadCount = state.JumpPadCount;
            respawned.BoostRingCount = state.BoostRingCount;
            return respawned;
        }
    }
}
