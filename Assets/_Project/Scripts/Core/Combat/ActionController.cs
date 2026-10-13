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
    /// - 種類は、押したときに地上か空中かと、移動のスティックの向きで決める（上下を優先。【仮】）。
    /// - 攻撃中の移動・ジャンプ・ワイヤーの入力の扱いは CharacterSimulator が決める（ControlInput）。
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

        /// <summary>今の攻撃の段階（攻撃中でなければ null）。</summary>
        public static AttackPhase? CurrentPhase(in CharacterState state, CombatTuning combat)
        {
            if (state.Action.Kind != ActionKind.Attack)
            {
                return null;
            }
            return combat.Get(state.Action.Attack).PhaseAt(state.Action.Ticks);
        }

        /// <summary>移動のスティックの向きから攻撃の種類を選ぶ。上下と横が同じ大きさ（斜め）なら上下を優先する【仮】。</summary>
        public static AttackSlot SelectAttack(bool grounded, Vector2 move, float deadzone)
        {
            float x = MathF.Abs(move.X);
            float y = MathF.Abs(move.Y);
            int direction = 0; // 0: ニュートラル、1: 横、2: 上、3: 下
            if (y >= deadzone && y >= x)
            {
                direction = move.Y > 0f ? 2 : 3;
            }
            else if (x >= deadzone)
            {
                direction = 1;
            }
            return (AttackSlot)((grounded ? (int)AttackSlot.GroundNeutral : (int)AttackSlot.AirNeutral) + direction);
        }

        private static bool CanStartAttack(in CharacterState state)
        {
            WirePhase wire = state.Wire.Phase;
            return wire != WirePhase.Firing && wire != WirePhase.Attached;
        }
    }
}
