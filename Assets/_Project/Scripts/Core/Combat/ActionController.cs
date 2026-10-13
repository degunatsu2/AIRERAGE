using System;
using System.Numerics;
using Degunatsu.Core.Input;
using Degunatsu.Core.Movement;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;

namespace Degunatsu.Core.Combat
{
    /// <summary>
    /// アクション状態の遷移とキャンセルの可否を1か所で決める（docs/01_game_design.md 6.2、6.3。docs/11_combat_plan.md T3-4）。
    /// のけぞりに入るのは命中のとき（HitResolver）。
    /// - 攻撃は Free のときに攻撃ボタンを押すと始まる（先行入力 AttackBufferTicks。種類と向きは押したときのスティックで決める）。
    ///   ワイヤーを射出中は始められない。
    ///   接続中に押すと、ワイヤーを解除して慣性を引き継ぎ、攻撃を始める（ワイヤー→攻撃。D-18、D-19）。
    /// - 種類は、地上なら方向に関係なく地上技、空中なら移動のスティックの向きで決める（上下を優先。【仮】。D-35）。
    /// - 攻撃中のキャンセル（同じティックに複数を押したら、ジャンプ → ワイヤー → 攻撃 の順に優先）:
    ///   - 攻撃→ジャンプ: その攻撃が当たっていて、ジャンプできる（地上か、空中ジャンプが残っている）とき（D-42）。
    ///   - 攻撃→ワイヤー: 攻撃のデータの WireCancelFromTick 以降で、ワイヤーを射出できて、クールタイムがないとき。
    ///     当たっていなくてもよい。使うとクールタイム（WireCancelCooldownTicks）が始まる（D-20、P-1）。
    ///   - 地上技→空中の攻撃: 地上技が当たっていて、空中にいるとき。回数に数えない（D-42）。
    ///   - 攻撃→攻撃: その攻撃が当たっていて、今の連携でまだ使っていないとき（1回まで。D-17）。地上技→地上技はできない。
    /// - 連携（攻撃→攻撃の回数）は、ワイヤーで接続したとき、攻撃がキャンセルされずに最後まで終わったとき、のけぞったとき、
    ///   Free で着地しているときに戻る。ジャンプのキャンセルでは戻らない（ユーザーとの合意、2026-10-13）。
    /// - 攻撃中・のけぞり中の移動・ジャンプ・ワイヤーの入力は無視する（ControlInput）。攻撃のデータが決める動きは ApplyMotion。
    /// </summary>
    public static class ActionController
    {

        /// <summary>
        /// 移動の前に呼ぶ。攻撃中・のけぞり中ならティックを進めて、終わったら Free に戻す。攻撃中ならキャンセルを決める。
        /// Free なら攻撃を始めるかを決める。
        /// </summary>
        public static void Update(ref CharacterState state, in InputFrame input, CharacterConfig config)
        {
            CombatTuning combat = config.Combat;
            UpdateBuffer(ref state, input, combat, config.Movement.MoveDeadzone);
            if (state.Wire.Phase == WirePhase.Attached)
            {
                state.Combat.AttackCancelUsed = false;
            }

            switch (state.Action.Kind)
            {
                case ActionKind.Hitstun:
                    state.Combat.AttackCancelUsed = false;
                    state.Action.Ticks++;
                    if (state.Action.Ticks < state.Action.Duration + 1)
                    {
                        return;
                    }
                    state.Action = default;
                    break;

                case ActionKind.Attack:
                    state.Action.Ticks++;
                    if (state.Action.Ticks < combat.Get(state.Action.Attack).TotalTicks)
                    {
                        TryCancel(ref state, input, config);
                        return;
                    }
                    // キャンセルされずに最後まで終わった。
                    state.Action = default;
                    state.Combat.AttackCancelUsed = false;
                    break;
            }

            if (state.Grounded)
            {
                state.Combat.AttackCancelUsed = false;
            }
            if (state.Combat.AttackBufferTicksLeft == 0)
            {
                return;
            }
            if (state.Wire.Phase == WirePhase.Attached)
            {
                WireController.Release(ref state, WireReleaseReason.Attack, config.Wire);
            }
            if (state.Wire.Phase != WirePhase.Firing)
            {
                StartAttack(ref state, state.Grounded ? AttackSlot.Ground : state.Combat.BufferedAirAttack);
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
        /// ヒットストップの間に呼ぶ（ほかの処理は止まる）。攻撃を押したら、そのときの向きで先行入力にする（止まっている間は減らない）。
        /// ジャンプを押したら覚えておき、止まりが解けたティックに押したとみなす。
        /// </summary>
        public static void RecordHitstopPresses(ref CharacterState state, PlayerInput input, CharacterConfig config)
        {
            var frame = new InputFrame(input, state.PreviousButtons);
            if (frame.WasPressed(InputButtons.Attack))
            {
                BufferAttack(ref state, frame, config.Combat, config.Movement.MoveDeadzone);
            }
            state.Combat.HitstopPresses |= frame.Pressed & InputButtons.Jump;
        }

        /// <summary>
        /// ヒットストップが解けたティックの入力。止まっている間に押したジャンプを、このティックに押したことにする
        /// （覚えていた押下は消す）。
        /// </summary>
        public static InputFrame TakeHitstopPresses(ref CharacterState state, PlayerInput input, out PlayerInput effectiveInput)
        {
            InputButtons buffered = state.Combat.HitstopPresses;
            state.Combat.HitstopPresses = InputButtons.None;
            effectiveInput = new PlayerInput(input.MoveX, input.MoveY, input.Buttons | buffered, input.AimX, input.AimY);
            return new InputFrame(effectiveInput, state.PreviousButtons & ~buffered);
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

        // 攻撃ボタンの先行入力（押したときのスティックの向きも覚える）と、ワイヤーキャンセルのクールタイムを進める。
        private static void UpdateBuffer(ref CharacterState state, in InputFrame input, CombatTuning combat, float deadzone)
        {
            if (input.WasPressed(InputButtons.Attack))
            {
                BufferAttack(ref state, input, combat, deadzone);
            }
            else if (state.Combat.AttackBufferTicksLeft > 0)
            {
                state.Combat.AttackBufferTicksLeft--;
            }
            if (state.Combat.WireCancelCooldownTicksLeft > 0)
            {
                state.Combat.WireCancelCooldownTicksLeft--;
            }
        }

        private static void BufferAttack(ref CharacterState state, in InputFrame input, CombatTuning combat, float deadzone)
        {
            state.Combat.AttackBufferTicksLeft = Math.Max(1, combat.AttackBufferTicks);
            state.Combat.BufferedAirAttack = SelectAttack(false, input.Move, deadzone);
            state.Combat.BufferedFacing = (sbyte)(input.Move.X >= deadzone ? 1 : input.Move.X <= -deadzone ? -1 : 0);
        }

        // 攻撃中のキャンセル。キャンセルしたら、Free にする（ジャンプ・ワイヤーは、このティックの移動とワイヤーの処理で行う）か、
        // 次の攻撃を始める。
        private static void TryCancel(ref CharacterState state, in InputFrame input, CharacterConfig config)
        {
            CombatTuning combat = config.Combat;
            AttackData attack = combat.Get(state.Action.Attack);
            bool hit = state.Action.HitMask != 0;

            if (hit && input.WasPressed(InputButtons.Jump) && CanJump(state, config.Movement))
            {
                state.Action = default;
                return;
            }

            if (input.WasPressed(InputButtons.Wire) && state.Action.Ticks >= attack.WireCancelFromTick
                && state.Combat.WireCancelCooldownTicksLeft == 0 && state.Wire.Phase == WirePhase.Ready)
            {
                state.Action = default;
                state.Combat.WireCancelCooldownTicksLeft = combat.WireCancelCooldownTicks;
                return;
            }

            if (!hit || state.Combat.AttackBufferTicksLeft == 0)
            {
                return;
            }
            if (state.Action.Attack == AttackSlot.Ground && !state.Grounded)
            {
                StartAttack(ref state, state.Combat.BufferedAirAttack);
                return;
            }
            AttackSlot next = state.Grounded ? AttackSlot.Ground : state.Combat.BufferedAirAttack;
            if (state.Combat.AttackCancelUsed || (state.Action.Attack == AttackSlot.Ground && next == AttackSlot.Ground))
            {
                return;
            }
            StartAttack(ref state, next);
            state.Combat.AttackCancelUsed = true;
        }

        // 攻撃を始める（先行入力を使い切る）。押したときに横を入れていれば、その向きを向く。
        private static void StartAttack(ref CharacterState state, AttackSlot slot)
        {
            if (state.Combat.BufferedFacing != 0)
            {
                state.Facing = state.Combat.BufferedFacing;
            }
            state.Action = new ActionState
            {
                Kind = ActionKind.Attack,
                Attack = slot,
                Ticks = 0,
            };
            state.Combat.AttackBufferTicksLeft = 0;
        }

        // Locomotion と同じ条件で、このティックにジャンプできるか（地上・コヨーテタイム・空中ジャンプの残り）。
        private static bool CanJump(in CharacterState state, MovementTuning movement)
        {
            return state.Grounded || state.CoyoteTicksLeft > 0 || state.AirJumpsUsed < movement.AirJumps;
        }
    }
}
