using System;
using System.Numerics;
using Degunatsu.Core.Input;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;

namespace Degunatsu.Core.Combat
{
    /// <summary>
    /// アクション状態の遷移を1か所で決める（docs/01_game_design.md 6.2、6.3）。今は攻撃の開始と終了だけ。
    /// - 攻撃は Free のときに攻撃ボタンを押すと始まる。ワイヤーを射出中・接続中は始められない
    ///   （ワイヤー→攻撃のキャンセルは docs/11_combat_plan.md の T3-3 で作る）。
    /// - 種類は、地上なら方向に関係なく地上技、空中なら移動のスティックの向きで決める（上下を優先。【仮】。D-35）。
    /// - 攻撃中の移動・ジャンプ・ワイヤーの入力は無視する（ControlInput）。攻撃のデータが決める移動（踏み込み、飛び出し）は ApplyMotion。
    /// </summary>
    public static class ActionController
    {
        /// <summary>移動の前に呼ぶ。攻撃中ならティックを進めて、終わったら Free に戻す。Free なら攻撃を始めるかを決める。</summary>
        public static void Update(ref CharacterState state, in InputFrame input, CombatTuning combat, float moveDeadzone)
        {
            if (state.Action.Kind == ActionKind.Attack)
            {
                state.Action.Ticks++;
                if (state.Action.Ticks < combat.Get(state.Action.Attack).TotalTicks)
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
        /// アクション中にシミュレーションへ渡す入力。攻撃中は移動・ジャンプ・ワイヤーの入力を無視する
        /// （地上では止まり、空中では勢いのまま落ちる）。Free ならそのまま。
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
        /// 通常の移動の計算の後に呼ぶ。攻撃のデータが決める速度を当てる: 持続の間の踏み込み（DashWhileActive）と、
        /// 硬直に入るティックの飛び出し（LaunchAtRecovery。地上技は必ず上へ）。X は向いている方向を正とする。
        /// </summary>
        public static void ApplyMotion(ref CharacterState state, CombatTuning combat)
        {
            if (state.Action.Kind != ActionKind.Attack)
            {
                return;
            }
            AttackData attack = combat.Get(state.Action.Attack);
            int ticks = state.Action.Ticks;
            if (attack.DashWhileActive && attack.PhaseAt(ticks) == AttackPhase.Active)
            {
                state.Velocity.X = attack.DashVelocityX * state.Facing;
                state.Velocity.Y = attack.DashVelocityY;
            }
            if (attack.LaunchAtRecovery && ticks == attack.StartupTicks + attack.ActiveTicks)
            {
                state.Velocity.X = attack.LaunchVelocityX * state.Facing;
                state.Velocity.Y = attack.LaunchVelocityY;
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

        private static bool CanStartAttack(in CharacterState state)
        {
            WirePhase wire = state.Wire.Phase;
            return wire != WirePhase.Firing && wire != WirePhase.Attached;
        }
    }
}
