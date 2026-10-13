using System;
using System.Numerics;
using Degunatsu.Core.Input;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;

namespace Degunatsu.Core.Combat
{
    /// <summary>
    /// アクション状態の遷移を1か所で決める（docs/01_game_design.md 6.2、6.3）。今は攻撃の開始と終了、のけぞりの終了。
    /// のけぞりに入るのは命中のとき（HitResolver）。
    /// - 攻撃は Free のときに攻撃ボタンを押すと始まる。ワイヤーを射出中・接続中は始められない
    ///   （ワイヤー→攻撃のキャンセルは docs/11_combat_plan.md の T3-4 で作る）。
    /// - 種類は、地上なら方向に関係なく地上技、空中なら移動のスティックの向きで決める（上下を優先。【仮】。D-35）。
    /// - 攻撃中の移動・ジャンプ・ワイヤーの入力は無視する（ControlInput）。攻撃のデータが決める動き（踏み込み、飛び出し、飛び込み）は ApplyMotion。
    /// </summary>
    public static class ActionController
    {
        /// <summary>
        /// 移動の前に呼ぶ。攻撃中・のけぞり中ならティックを進めて、終わったら Free に戻す。Free なら攻撃を始めるかを決める。
        /// </summary>
        public static void Update(ref CharacterState state, in InputFrame input, CombatTuning combat, float moveDeadzone)
        {
            if (state.Action.Kind != ActionKind.Free)
            {
                state.Action.Ticks++;
                if (state.Action.Ticks < EndTick(state.Action, combat))
                {
                    return;
                }
                state.Action = default;
            }

            if (input.WasPressed(InputButtons.Attack) && CanStartAttack(state))
            {
                state.Action = new ActionState
                {
                    Kind = ActionKind.Attack,
                    Attack = SelectAttack(state.Grounded, input.Move, moveDeadzone),
                    Ticks = 0,
                };
            }
        }

        /// <summary>
        /// アクション中にシミュレーションへ渡す入力。攻撃中・のけぞり中は移動・ジャンプ・ワイヤーの入力を無視する
        /// （空中では勢いのまま動く）。Free ならそのまま。
        /// </summary>
        public static PlayerInput ControlInput(in CharacterState state, PlayerInput input)
        {
            if (state.Action.IsFree)
            {
                return input;
            }
            return new PlayerInput(0, 0, input.Buttons & ~(InputButtons.Jump | InputButtons.Wire));
        }

        /// <summary>
        /// 通常の移動の計算の後に呼ぶ。攻撃のデータが決める速度を当てる（踏み込み、飛び出し、飛び込み。X は向いている方向が正）。
        /// 決めていない間は慣性のまま。撃破技なら、最初の判定が出るティックに、その時点の（慣性の）速さを覚える（D-38）。
        /// </summary>
        public static void ApplyMotion(ref CharacterState state, CombatTuning combat)
        {
            if (state.Action.Kind != ActionKind.Attack)
            {
                return;
            }
            AttackData attack = combat.Get(state.Action.Attack);
            int ticks = state.Action.Ticks;
            if (state.Action.Attack == combat.KoAttack && ticks == attack.FirstHitTick)
            {
                state.Action.StrikeSpeed = state.Velocity.Length();
            }
            foreach (AttackMotion motion in attack.Motions)
            {
                if (motion.IsActiveAt(ticks))
                {
                    state.Velocity.X = motion.VelocityX * state.Facing;
                    state.Velocity.Y = motion.VelocityY;
                }
            }
        }

        /// <summary>今の攻撃の段階（攻撃中でなければ null）。</summary>
        public static AttackPhase? CurrentPhase(in CharacterState state, CombatTuning combat)
        {
            if (state.Action.Kind != ActionKind.Attack)
            {
                return null;
            }
            return combat.Get(state.Action.Attack).PhaseAt(state.Action.Ticks);
        }

        /// <summary>
        /// 攻撃の種類を選ぶ。地上なら方向に関係なく地上技（D-35）。空中なら移動のスティックの向きで、
        /// 上下と横が同じ大きさ（斜め）なら上下を優先する【仮】。
        /// </summary>
        public static AttackSlot SelectAttack(bool grounded, Vector2 move, float deadzone)
        {
            if (grounded)
            {
                return AttackSlot.Ground;
            }
            float x = MathF.Abs(move.X);
            float y = MathF.Abs(move.Y);
            if (y >= deadzone && y >= x)
            {
                return move.Y > 0f ? AttackSlot.AirUp : AttackSlot.AirDown;
            }
            return x >= deadzone ? AttackSlot.AirSide : AttackSlot.AirNeutral;
        }

        // Free に戻る Ticks。攻撃は始めたティック（Ticks = 0）から数える。のけぞりは、命中したティック（すでに動いた後）の
        // 次から数えるので、ヒットストップの後ちょうど Duration ティックの間のけぞる。
        private static int EndTick(in ActionState action, CombatTuning combat)
        {
            return action.Kind == ActionKind.Attack ? combat.Get(action.Attack).TotalTicks : action.Duration + 1;
        }

        private static bool CanStartAttack(in CharacterState state)
        {
            WirePhase wire = state.Wire.Phase;
            return wire != WirePhase.Firing && wire != WirePhase.Attached;
        }
    }
}
