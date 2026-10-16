using System;
using System.Numerics;
using Airerage.Core.Input;
using Airerage.Core.Movement;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;

namespace Airerage.Core.Combat
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
    /// - ガード（T3-5。D-21）: Free のときにガードボタンを押すと、GuardTicks の間ガードする（全方向・空中可）。
    ///   クールタイム中・射出中はできない。接続中に押すと、ワイヤーを解除して慣性を引き継ぐ【仮】。攻撃とガードを同時に押したらガード。
    ///   ガードが終わってから GuardCooldownTicks の間は次のガードができない。攻撃を受けたときの結果は HitResolver が決める。
    ///   ガードの途中でも、攻撃・ワイヤーでキャンセルして動ける（ユーザーの要望、2026-10-13）。ただし攻撃をガードした後の
    ///   ガード硬直の間はできない（CancelFromTick）。キャンセルしてもガードは終わったことになり、クールタイムが始まる。
    /// - 攻撃中・のけぞり中・ガード中の移動・ジャンプ・ワイヤーの入力は無視する（ControlInput）。攻撃のデータが決める動きは ApplyMotion。
    /// - 特殊攻撃と必殺技（docs/13 T5-2、T5-3。D-53、D-55）: 出し方とキャンセルは通常攻撃と同じ（先行入力、ワイヤー→技、
    ///   ガード→技、当たったときの攻撃→技・技→攻撃）。ただし、攻撃（技を含む）→特殊攻撃・必殺技のキャンセルは、攻撃→攻撃の
    ///   1 回とは別枠で、数えず、1 回を使った後でもできる（当たったときだけ。ユーザーの要望、2026-10-15）。技→通常攻撃は 1 回に数える。
    ///   地上でも空中でも同じ技。違うのは使う条件だけ: 特殊攻撃はそれぞれのクールタイム（使い始めたときから数える）が 0 のとき、
    ///   必殺技はゲージが SuperGaugeCost 以上のとき（出したときに減る）。条件を満たさない間は出ず、先行入力の間に満たせば出る。
    ///   同じティックに複数を押したら、必殺技 → 特殊攻撃 2 → 特殊攻撃 1 → 通常攻撃 の順に優先する【仮】。
    /// - 長押しの技（AttackData.HoldMaxTicks。docs/13 T5-6）: 出せるときにボタンを押したままなら、技を出さずにためる（Free のまま
    ///   動ける。CombatState.Holding）。離したときに技を出し、ためた長さを ActionState.Charge に入れる。ためている間に攻撃を受けたり、
    ///   ほかの行動を始めたりしたら、ためるのをやめる（何も出ない）。
    /// - 攻撃を始めるたびに CombatState.AttackSerial を 1 増やす（攻撃 1 回ごとの番号）。
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
            UpdateTimers(ref state, input, combat, config.Movement.MoveDeadzone);
            if (state.Wire.Phase == WirePhase.Attached)
            {
                state.Combat.AttackCancelUsed = false;
            }
            if (state.Combat.Holding && UpdateHold(ref state, input, config))
            {
                return;
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

                case ActionKind.Guard:
                    state.Action.Ticks++;
                    if (state.Action.Ticks < state.Action.Duration)
                    {
                        if (state.Action.Ticks >= state.Action.CancelFromTick)
                        {
                            TryCancelGuard(ref state, input, config);
                        }
                        return;
                    }
                    EndGuard(ref state, combat);
                    break;
            }

            if (state.Grounded)
            {
                state.Combat.AttackCancelUsed = false;
            }
            if (input.WasPressed(InputButtons.Guard) && state.Combat.GuardCooldownTicksLeft == 0 && state.Wire.Phase != WirePhase.Firing)
            {
                StartGuard(ref state, config);
                return;
            }
            if (state.Combat.AttackBufferTicksLeft == 0 || state.Wire.Phase == WirePhase.Firing)
            {
                return;
            }
            AttackSlot slot = BufferedSlot(state);
            if (!CanUse(state, slot, combat))
            {
                return;
            }
            if (TryBeginHold(ref state, slot, input, combat))
            {
                return;
            }
            if (state.Wire.Phase == WirePhase.Attached)
            {
                WireController.Release(ref state, WireReleaseReason.Attack, config.Wire);
            }
            StartAttack(ref state, slot, combat);
        }

        /// <summary>技の枠のボタン（特殊攻撃と必殺技。それ以外は攻撃ボタン）。</summary>
        public static InputButtons ButtonOf(AttackSlot slot)
        {
            switch (slot)
            {
                case AttackSlot.Special1: return InputButtons.Special1;
                case AttackSlot.Special2: return InputButtons.Special2;
                case AttackSlot.Super: return InputButtons.Super;
                default: return InputButtons.Attack;
            }
        }

        // 長押しの技で、ボタンを押したままなら、ためるのを始める（先行入力は使い切る）。始めたら true。
        private static bool TryBeginHold(ref CharacterState state, AttackSlot slot, in InputFrame input, CombatTuning combat)
        {
            if (combat.Get(slot).HoldMaxTicks <= 0 || !input.IsHeld(ButtonOf(slot)))
            {
                return false;
            }
            state.Combat.Holding = true;
            state.Combat.HoldSlot = slot;
            state.Combat.HoldTicks = 0;
            state.Combat.AttackBufferTicksLeft = 0;
            return true;
        }

        // ためている技: 押している間はためる。離したら技を出す（出したら true）。Free でなくなったら、ためるのをやめる。
        private static bool UpdateHold(ref CharacterState state, in InputFrame input, CharacterConfig config)
        {
            CombatTuning combat = config.Combat;
            AttackSlot slot = state.Combat.HoldSlot;
            if (!state.Action.IsFree)
            {
                state.Combat.Holding = false;
                state.Combat.HoldTicks = 0;
                return false;
            }
            if (input.IsHeld(ButtonOf(slot)))
            {
                if (state.Combat.HoldTicks < combat.Get(slot).HoldMaxTicks)
                {
                    state.Combat.HoldTicks++;
                }
                return false;
            }
            int charge = state.Combat.HoldTicks;
            state.Combat.Holding = false;
            state.Combat.HoldTicks = 0;
            if (!CanUse(state, slot, combat) || state.Wire.Phase == WirePhase.Firing)
            {
                return false;
            }
            if (state.Wire.Phase == WirePhase.Attached)
            {
                WireController.Release(ref state, WireReleaseReason.Attack, config.Wire);
            }
            StartAttack(ref state, slot, combat);
            state.Action.Charge = charge;
            return true;
        }

        /// <summary>
        /// 技を使う条件を満たしているか（特殊攻撃はクールタイムが 0、必殺技はゲージが足りる。通常攻撃はいつでも true）。
        /// 表示（使えるかどうか）にも使う。
        /// </summary>
        public static bool CanUse(in CharacterState state, AttackSlot slot, CombatTuning combat)
        {
            switch (slot)
            {
                case AttackSlot.Special1: return state.Combat.Special1CooldownTicksLeft == 0;
                case AttackSlot.Special2: return state.Combat.Special2CooldownTicksLeft == 0;
                case AttackSlot.Super: return state.Combat.Gauge >= combat.SuperGaugeCost;
                default: return true;
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
            BufferPresses(ref state, frame, config.Combat, config.Movement.MoveDeadzone);
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
                if (motion.IsActiveAt(ticks) && (!motion.RequiresHit || state.Action.Connected))
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

        // 攻撃・技のボタンの先行入力（押したときのスティックの向きも覚える）と、ワイヤーキャンセル・ガード・特殊攻撃のクールタイムを進める。
        private static void UpdateTimers(ref CharacterState state, in InputFrame input, CombatTuning combat, float deadzone)
        {
            if (!BufferPresses(ref state, input, combat, deadzone) && state.Combat.AttackBufferTicksLeft > 0)
            {
                state.Combat.AttackBufferTicksLeft--;
            }
            CountDown(ref state.Combat.WireCancelCooldownTicksLeft);
            CountDown(ref state.Combat.GuardCooldownTicksLeft);
            CountDown(ref state.Combat.Special1CooldownTicksLeft);
            CountDown(ref state.Combat.Special2CooldownTicksLeft);
            CountDown(ref state.Combat.CoinBoostTicksLeft);
        }

        private static void CountDown(ref int ticksLeft)
        {
            if (ticksLeft > 0)
            {
                ticksLeft--;
            }
        }

        // ガードを攻撃・ワイヤーでキャンセルする。ワイヤーはこのティックの移動とワイヤーの処理で射出する。
        private static void TryCancelGuard(ref CharacterState state, in InputFrame input, CharacterConfig config)
        {
            if (input.WasPressed(InputButtons.Wire) && state.Wire.Phase == WirePhase.Ready)
            {
                EndGuard(ref state, config.Combat);
                return;
            }
            if (state.Combat.AttackBufferTicksLeft > 0 && CanUse(state, BufferedSlot(state), config.Combat))
            {
                EndGuard(ref state, config.Combat);
                if (!TryBeginHold(ref state, BufferedSlot(state), input, config.Combat))
                {
                    StartAttack(ref state, BufferedSlot(state), config.Combat);
                }
            }
        }

        private static void EndGuard(ref CharacterState state, CombatTuning combat)
        {
            state.Action = default;
            state.Combat.GuardCooldownTicksLeft = combat.GuardCooldownTicks;
        }

        // ガードを始める。接続中ならワイヤーを解除する。攻撃の先行入力は捨てる（ガードの後に勝手に攻撃しないように）。
        private static void StartGuard(ref CharacterState state, CharacterConfig config)
        {
            if (state.Wire.Phase == WirePhase.Attached)
            {
                WireController.Release(ref state, WireReleaseReason.Guard, config.Wire);
            }
            state.Action = new ActionState
            {
                Kind = ActionKind.Guard,
                Duration = config.Combat.GuardTicks,
                CancelFromTick = 1,
            };
            state.Combat.AttackBufferTicksLeft = 0;
        }

        // このティックに押した攻撃・技のボタンを先行入力にする（優先: 必殺技 → 特殊攻撃 2 → 特殊攻撃 1 → 通常攻撃）。押していれば true。
        private static bool BufferPresses(ref CharacterState state, in InputFrame input, CombatTuning combat, float deadzone)
        {
            if (input.WasPressed(InputButtons.Super))
            {
                BufferAttack(ref state, input, combat, deadzone, AttackSlot.Super);
            }
            else if (input.WasPressed(InputButtons.Special2))
            {
                BufferAttack(ref state, input, combat, deadzone, AttackSlot.Special2);
            }
            else if (input.WasPressed(InputButtons.Special1))
            {
                BufferAttack(ref state, input, combat, deadzone, AttackSlot.Special1);
            }
            else if (input.WasPressed(InputButtons.Attack))
            {
                BufferAttack(ref state, input, combat, deadzone, SelectAttack(false, input.Move, deadzone));
            }
            else
            {
                return false;
            }
            return true;
        }

        private static void BufferAttack(ref CharacterState state, in InputFrame input, CombatTuning combat, float deadzone, AttackSlot slot)
        {
            state.Combat.AttackBufferTicksLeft = Math.Max(1, combat.AttackBufferTicks);
            state.Combat.BufferedAttack = slot;
            state.Combat.BufferedFacing = (sbyte)(input.Move.X >= deadzone ? 1 : input.Move.X <= -deadzone ? -1 : 0);
        }

        // 先行入力から出す技（通常攻撃は地上なら地上技、空中なら押したときの向きの技。特殊攻撃と必殺技はそのまま）。
        private static AttackSlot BufferedSlot(in CharacterState state)
        {
            AttackSlot buffered = state.Combat.BufferedAttack;
            return state.Grounded && !CombatTuning.IsSpecial(buffered) ? AttackSlot.Ground : buffered;
        }

        // 攻撃中のキャンセル。キャンセルしたら、Free にする（ジャンプ・ワイヤーは、このティックの移動とワイヤーの処理で行う）か、
        // 次の攻撃を始める。
        private static void TryCancel(ref CharacterState state, in InputFrame input, CharacterConfig config)
        {
            CombatTuning combat = config.Combat;
            AttackData attack = combat.Get(state.Action.Attack);
            bool hit = state.Action.Connected;

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
            AttackSlot next = BufferedSlot(state);
            if (!CanUse(state, next, combat))
            {
                return;
            }
            // 特殊攻撃と必殺技へのキャンセルは、攻撃→攻撃の 1 回とは別枠（数えず、使った後でもできる。ユーザーの要望、2026-10-15）。
            // 回数はそれぞれのクールタイムとゲージで限られる。地上技→空中の攻撃も数えない（D-42）。
            bool groundToAir = state.Action.Attack == AttackSlot.Ground && !state.Grounded;
            if (CombatTuning.IsSpecial(next) || groundToAir)
            {
                // 長押しの技なら、攻撃を終えてためるのを始める。
                state.Action = default;
                if (!TryBeginHold(ref state, next, input, combat))
                {
                    StartAttack(ref state, next, combat);
                }
                return;
            }
            if (state.Combat.AttackCancelUsed || (state.Action.Attack == AttackSlot.Ground && next == AttackSlot.Ground))
            {
                return;
            }
            StartAttack(ref state, next, combat);
            state.Combat.AttackCancelUsed = true;
        }

        // 攻撃を始める（先行入力を使い切る）。押したときに横を入れていれば、その向きを向く。
        // 特殊攻撃ならクールタイムを始め、必殺技ならゲージを使う。
        private static void StartAttack(ref CharacterState state, AttackSlot slot, CombatTuning combat)
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
            state.Combat.AttackSerial++;
            switch (slot)
            {
                case AttackSlot.Special1:
                    state.Combat.Special1CooldownTicksLeft = combat.Special1CooldownTicks;
                    break;
                case AttackSlot.Special2:
                    state.Combat.Special2CooldownTicksLeft = combat.Special2CooldownTicks;
                    break;
                case AttackSlot.Super:
                    state.Combat.Gauge -= combat.SuperGaugeCost;
                    break;
            }
        }

        // Locomotion と同じ条件で、このティックにジャンプできるか（地上・コヨーテタイム・空中ジャンプの残り）。
        private static bool CanJump(in CharacterState state, MovementTuning movement)
        {
            return state.Grounded || state.CoyoteTicksLeft > 0 || state.AirJumpsUsed < movement.AirJumps;
        }
    }
}
