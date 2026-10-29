using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Match;
using Airerage.Core.Simulation;

namespace Airerage.Core.Combat
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
        /// configs を渡すと、続けて出している途中の連続技（イナズマステップ）を撃破で終え、出した回数のクールタイムを数え始める。
        public static void Resolve(List<CharacterState> states, IReadOnlyList<CharacterState> spawns, StageBounds bounds, MatchRules rules,
            List<KnockoutRecord> knockouts = null, IReadOnlyList<CharacterConfig> configs = null)
        {
            for (int i = 0; i < states.Count; i++)
            {
                CharacterState state = states[i];
                // 溺れて沈み続けた人も撃破（水の底に地形があって、撃破の線まで沈めないとき）。
                bool drowned = state.Combat.DrownTicks >= StageZones.DrownKnockoutTicks;
                if (state.Combat.IsWaitingToRespawn || state.Combat.IsAway || (bounds.Contains(state.Position) && !drowned))
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
                states[i] = Respawn(states[i], spawns[i], rules, configs != null ? configs[i].Combat : null);
            }
        }

        /// <summary>
        /// 試合から抜けた人の状態（docs/15 T6-10）: 出現したときの状態に戻して（技・ワイヤー・動きをやめる）、抜けた印をつける。撃破の数、
        /// ゲージ、コイン（所持も確定も）、前のティックのボタン、ギミックの数を引き継ぐ。復帰は待たない（抜けた印でずっと同じ扱い）。
        /// </summary>
        public static CharacterState Departed(in CharacterState state, in CharacterState spawn)
        {
            CharacterState departed = spawn;
            departed.Combat.HasLeft = true;
            departed.Combat.Knockouts = state.Combat.Knockouts;
            departed.Combat.Falls = state.Combat.Falls;
            departed.Combat.Gauge = state.Combat.Gauge;
            departed.Combat.AttackSerial = state.Combat.AttackSerial;
            departed.PreviousButtons = state.PreviousButtons;
            departed.JumpPadCount = state.JumpPadCount;
            departed.BoostRingCount = state.BoostRingCount;
            departed.Coins = state.Coins;
            return departed;
        }

        /// <summary>
        /// 出現したときの状態に戻し、復帰を待たせる。撃破の数（撃破された数は +1）、ゲージ、前のティックのボタン、ギミックの数、
        /// コイン、まだ払っていないダメージ報酬の元を引き継ぐ。
        /// 無敵は、待つ間と復帰した後の両方の長さにする。
        /// </summary>
        private static CharacterState Respawn(in CharacterState state, in CharacterState spawn, MatchRules rules, CombatTuning combat)
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
            respawned.Combat.SplashCount = state.Combat.SplashCount;
            respawned.Combat.CaptureCount = state.Combat.CaptureCount;
            // JUGGERNAUT: フラックの弾道の段階は、その試合の間は覚えておく（ユーザーの案、2026-10-29）。表示が数える回数も引き継ぐ。
            respawned.Combat.FlakLevelShift = state.Combat.FlakLevelShift;
            respawned.Combat.FlakShotCount = state.Combat.FlakShotCount;
            respawned.Combat.MineBlastCount = state.Combat.MineBlastCount;
            respawned.Combat.MaydayRejectCount = state.Combat.MaydayRejectCount;
            // コインはここではなくさない（撃破ドロップは CoinDropRules が、台帳を通して落とす。T4-6）。
            respawned.Coins = state.Coins;
            // 撃破で終わったコンボのダメージ報酬は、このティックの後で CombatRewardRules が払う（T4-5）。
            respawned.Combat.RewardDamage = state.Combat.RewardDamage;
            respawned.Combat.RewardAttacker = state.Combat.RewardAttacker;
            // 攻撃の番号は増え続ける（壊せる物に、前の攻撃と同じ番号で当たらないように）。効果が続いている技（セーフティボックスなど）の
            // 止めているクールタイムは残す（撃破されても、2 つ目を置けないように）。
            respawned.Combat.AttackSerial = state.Combat.AttackSerial;
            if (state.Combat.ChainCount > 0 && combat != null)
            {
                // 連続技の途中で撃破された: そこで終え、出した回数のクールタイムを数え始める（止めたままにすると、ずっと ACTIVE で使えなくなる。
                // ユーザーの指摘、2026-10-22）。
                respawned.Combat.ChainCount = state.Combat.ChainCount;
                ActionController.EndChain(ref respawned.Combat, combat);
            }
            else if (state.Combat.Special1CooldownTicksLeft == CombatState.CooldownWhileActive)
            {
                respawned.Combat.Special1CooldownTicksLeft = CombatState.CooldownWhileActive;
            }
            if (state.Combat.Special2CooldownTicksLeft == CombatState.CooldownWhileActive)
            {
                respawned.Combat.Special2CooldownTicksLeft = CombatState.CooldownWhileActive;
            }
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
