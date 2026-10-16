using System;
using System.Numerics;
using Airerage.Core.Common;
using Airerage.Core.Input;
using Airerage.Core.Movement;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
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
    ///   ほかの行動を始めたりしたら、ためるのをやめる（何も出ない）。AttackData.HoldRooted の技は、ためている間その場から動かない
    ///   （向きは変えられる。RISK TAKER のオールイン）。
    /// - 攻撃を始めるたびに CombatState.AttackSerial を 1 増やす（攻撃 1 回ごとの番号）。
    /// - 走り続ける地上技（AttackData.Run。STORM RUNNER）: TotalTicks で終わらず、別の行動（ジャンプ・ワイヤー・ガード・空中の攻撃・
    ///   技）の入力で終わって、そのティックにその行動を始める（当たっていなくてもよい）。地上技の入力は無視する。だんだん加速し、
    ///   逆向きに入力すると反転する。足場が途切れたら・壁にぶつかったら浅い角度で跳ぶ（AfterMove）。
    /// - 行動の速さの強化（SpeedBoosts）の間は、攻撃のフレームを速く進める（ヒットストップ・のけぞり・ガードは速くしない）。
    /// - 効果中にもう一度出せる技（行動の速さの強化）は、効果中なら止めているクールタイムのままでも出せる（CanRecast）。
    /// - 続けて使える特殊攻撃（CombatTuning.Special1MaxCharges が 2 以上。ワープポータル）は、回数が残っていれば出せる。減っている間は
    ///   クールタイムごとに 1 つずつ戻る（C-6）。コズミックレイ（必殺技）は、自分のワープポータルが 2 つあるときだけ出せる。
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
                    AdvanceAttack(ref state, config);
                    if (combat.Get(state.Action.Attack).Run)
                    {
                        if (UpdateRun(ref state, input, config))
                        {
                            return;
                        }
                        // 別の行動で走るのをやめた。このティックに、その行動を始める。
                        break;
                    }
                    if (state.Action.Ticks < LengthOf(state, config))
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
            StartAttack(ref state, slot, combat, input);
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
            state.Combat.HoldAim = input.Aim;
            state.Combat.AttackBufferTicksLeft = 0;
            // 押したときに横を入れていれば、その向きを向く。ためている間は向きを変えられ、離したときの向きで出す。
            if (state.Combat.BufferedFacing != 0)
            {
                state.Facing = state.Combat.BufferedFacing;
                state.Combat.BufferedFacing = 0;
            }
            return true;
        }

        /// <summary>その場から動かずにためている（AttackData.HoldRooted）か。移動・ジャンプ・ワイヤーの入力を無視する。</summary>
        public static bool IsRootedHold(in CharacterState state, CombatTuning combat)
        {
            return state.Combat.Holding && combat.Get(state.Combat.HoldSlot).HoldRooted;
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
                // ためている間の照準（ミサイルの狙いの表示）。
                state.Combat.HoldAim = input.Aim;
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
            StartAttack(ref state, slot, combat, input);
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
                case AttackSlot.Special1:
                    return combat.Special1MaxCharges > 1 ? state.Combat.Special1Spent < combat.Special1MaxCharges
                        : state.Combat.Special1CooldownTicksLeft == 0 || CanRecast(state, slot, combat);
                case AttackSlot.Special2:
                    return combat.Special2MaxCharges > 1 ? state.Combat.Special2Spent < combat.Special2MaxCharges
                        : state.Combat.Special2CooldownTicksLeft == 0 || CanRecast(state, slot, combat);
                case AttackSlot.Super:
                    // コズミックレイは、自分のワープポータルが 2 つないと出せない（docs/14 1.1）。
                    return state.Combat.Gauge >= combat.SuperGaugeCost
                        && (combat.Super.Effect != SkillEffect.CosmicRay || state.Combat.PortalCount >= 2);
                default: return true;
            }
        }

        /// <summary>
        /// 今の攻撃の長さ（ティック。ふつうは AttackData.TotalTicks。効果中にもう一度出した突風は、溜めと硬直の合計）。
        /// </summary>
        public static int LengthOf(in CharacterState state, CharacterConfig config)
        {
            if (state.Action.Recast)
            {
                SpeedBoostTuning boost = config.Skills.SpeedBoost;
                return boost.GustWindupTicks + boost.GustRecoveryTicks;
            }
            return config.Combat.Get(state.Action.Attack).TotalTicks;
        }

        /// <summary>
        /// 効果中にもう一度出せる技（行動の速さの強化。もう一度出すと強化を解いて突風を撃つ）を、今もう一度出せるか。
        /// </summary>
        public static bool CanRecast(in CharacterState state, AttackSlot slot, CombatTuning combat)
        {
            int cooldown = slot == AttackSlot.Special1 ? state.Combat.Special1CooldownTicksLeft
                : slot == AttackSlot.Special2 ? state.Combat.Special2CooldownTicksLeft : 0;
            return cooldown == CombatState.CooldownWhileActive && combat.Get(slot).Effect == SkillEffect.SpeedBoost
                && state.Combat.SpeedBoostTicksLeft > 0;
        }

        /// <summary>
        /// アクション中にシミュレーションへ渡す入力。攻撃中・のけぞり中・その場でためている間は、移動・ジャンプ・ワイヤーの入力を
        /// 無視する（空中では勢いのまま動く）。それ以外の Free ならそのまま。
        /// </summary>
        public static PlayerInput ControlInput(in CharacterState state, PlayerInput input, CombatTuning combat)
        {
            if (state.Action.IsFree && !IsRootedHold(state, combat))
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
        public static void ApplyMotion(ref CharacterState state, CharacterConfig config)
        {
            if (state.Action.Kind != ActionKind.Attack)
            {
                return;
            }
            CombatTuning combat = config.Combat;
            AttackData attack = combat.Get(state.Action.Attack);
            float scale = SpeedBoosts.ScaleOf(state, config);
            if (attack.Run)
            {
                ApplyRun(ref state, config.Skills.Run, scale);
                return;
            }
            if (state.Action.Recast && state.Action.Ticks < config.Skills.SpeedBoost.GustWindupTicks)
            {
                // 突風の溜め: ほとんど止まる（撃つと反動で下がる。SkillRules）。
                state.Velocity *= config.Skills.SpeedBoost.GustWindupBrake;
                return;
            }
            // 行動の速さの強化でフレームを飛ばしたティックは、飛ばしたフレームの分も順に調べる（1 ティックだけの動きを落とさない）。
            int ticks = state.Action.Ticks;
            int from = ticks - Math.Max(1, state.Action.Advance) + 1;
            if (state.Action.Attack == combat.KoAttack && from <= attack.FirstHitTick && attack.FirstHitTick <= ticks)
            {
                state.Action.StrikeSpeed = state.Velocity.Length();
            }
            for (int t = from; t <= ticks; t++)
            {
                foreach (AttackMotion motion in attack.Motions)
                {
                    if (motion.IsActiveAt(t) && (!motion.RequiresHit || state.Action.Connected))
                    {
                        state.Velocity.X = motion.VelocityX * state.Facing * scale;
                        state.Velocity.Y = motion.VelocityY * scale;
                    }
                }
            }
        }

        // 走る速さを当てる（反転の間は止まっていく）。縦の速さは重力と跳ぶ動きのまま。
        private static void ApplyRun(ref CharacterState state, RunTuning run, float scale)
        {
            if (state.Action.RunSpeed <= 0f)
            {
                // 走り始めたティック。
                state.Action.RunSpeed = run.InitialSpeed;
            }
            float speed = state.Action.RunSpeed * scale;
            if (state.Action.TurnTicksLeft > 0)
            {
                speed *= (float)state.Action.TurnTicksLeft / run.TurnTicks;
            }
            state.Velocity.X = speed * state.Facing;
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
            CountDown(ref state.Combat.SpeedBoostTicksLeft);
            CountDown(ref state.Combat.SkyBoostTicksLeft);
            CountDown(ref state.Combat.WarpCooldownTicks);
            Recharge(ref state.Combat.Special1Spent, ref state.Combat.Special1CooldownTicksLeft, combat.Special1MaxCharges, combat.Special1CooldownTicks);
            Recharge(ref state.Combat.Special2Spent, ref state.Combat.Special2CooldownTicksLeft, combat.Special2MaxCharges, combat.Special2CooldownTicks);
            // 行動の速さの強化が終わったら（時間切れ・撃破）、止めていたクールタイムを数え始める。
            if (state.Combat.SpeedBoostTicksLeft == 0)
            {
                ReleaseBoostCooldown(ref state.Combat.Special1CooldownTicksLeft, combat, AttackSlot.Special1);
                ReleaseBoostCooldown(ref state.Combat.Special2CooldownTicksLeft, combat, AttackSlot.Special2);
            }
        }

        private static void ReleaseBoostCooldown(ref int cooldown, CombatTuning combat, AttackSlot slot)
        {
            if (cooldown == CombatState.CooldownWhileActive && combat.Get(slot).Effect == SkillEffect.SpeedBoost)
            {
                cooldown = combat.CooldownTicksOf(slot);
            }
        }

        // 攻撃のフレームを進める（行動の速さの強化の間は速く。1 ティックに 2 つまで。SpeedBoosts）。
        private static void AdvanceAttack(ref CharacterState state, CharacterConfig config)
        {
            int advance = 1;
            int percent = SpeedBoosts.PercentOf(state, config);
            if (percent > 0)
            {
                state.Action.TickRemainder += percent;
                if (state.Action.TickRemainder >= 100)
                {
                    state.Action.TickRemainder -= 100;
                    advance = 2;
                }
            }
            state.Action.Ticks += advance;
            state.Action.Advance = advance;
        }

        // 走り続ける地上技（AttackData.Run。SkillTuning.Run）。走り続けるなら true。別の行動の入力があったら、走るのをやめて false
        // （呼ぶ側が、このティックにその行動を始める）。地上技の入力は無視する（走ったまま）。
        private static bool UpdateRun(ref CharacterState state, in InputFrame input, CharacterConfig config)
        {
            RunTuning run = config.Skills.Run;
            CombatTuning combat = config.Combat;
            ref ActionState action = ref state.Action;
            if (action.RunSpeed <= 0f)
            {
                action.RunSpeed = run.InitialSpeed;
            }
            bool other = (input.WasPressed(InputButtons.Jump) && CanJump(state, config.Movement))
                || (input.WasPressed(InputButtons.Wire) && state.Wire.Phase == WirePhase.Ready)
                || (input.WasPressed(InputButtons.Guard) && state.Combat.GuardCooldownTicksLeft == 0);
            if (!other && state.Combat.AttackBufferTicksLeft > 0)
            {
                AttackSlot next = BufferedSlot(state);
                if (next == AttackSlot.Ground)
                {
                    state.Combat.AttackBufferTicksLeft = 0;
                }
                else
                {
                    other = CanUse(state, next, combat);
                }
            }
            if (other)
            {
                state.Action = default;
                return false;
            }

            // 同じ相手には RehitTicks に 1 回まで当たる（最後に当たってから RehitTicks たったら、当てた相手の記録を消す）。
            if (action.HitMask != 0 && action.Ticks - action.LastHitTicks >= run.RehitTicks)
            {
                action.HitMask = 0;
            }
            if (action.TurnTicksLeft > 0)
            {
                action.TurnTicksLeft--;
                if (action.TurnTicksLeft == 0)
                {
                    state.Facing = -state.Facing;
                    action.RunSpeed = run.InitialSpeed;
                }
            }
            else if (state.Grounded && input.Move.X * state.Facing <= -config.Movement.MoveDeadzone)
            {
                // 逆向きに入力した: 止まってから、速さを初めに戻して逆向きに走る（ユーザーの回答、2026-10-16）。
                action.TurnTicksLeft = run.TurnTicks;
            }
            else if (state.Grounded)
            {
                action.RunSpeed = MathF.Min(run.MaxSpeed, action.RunSpeed + run.Acceleration * SimTime.DeltaTime);
            }
            return true;
        }

        /// <summary>
        /// 移動と接地の後に呼ぶ。走り続ける地上技なら、足場が途切れたとき・壁にぶつかったときに浅い角度で跳ぶ（壁なら反対側へ）。
        /// wasGrounded はこのティックの移動の前に接地していたか、velocityBeforeMove は移動の前の速度。
        /// </summary>
        public static void AfterMove(ref CharacterState state, bool wasGrounded, Vector3 velocityBeforeMove, CharacterConfig config)
        {
            if (state.Action.Kind != ActionKind.Attack || !config.Combat.Get(state.Action.Attack).Run || state.Action.TurnTicksLeft > 0)
            {
                return;
            }
            RunTuning run = config.Skills.Run;
            float speed = state.Action.RunSpeed * SpeedBoosts.ScaleOf(state, config);
            float rise = speed * MathF.Tan(run.JumpAngleDeg * (MathF.PI / 180f));
            bool forward = velocityBeforeMove.X * state.Facing > 1f;
            if (forward && MathF.Abs(state.Velocity.X) < MathF.Abs(velocityBeforeMove.X) * 0.5f)
            {
                // 壁にぶつかった: 壁ジャンプの要領で反対側へ。
                state.Facing = -state.Facing;
                state.Velocity = new Vector3(state.Facing * speed, rise, 0f);
                state.Grounded = false;
                state.CoyoteTicksLeft = 0;
                return;
            }
            if (wasGrounded && !state.Grounded && state.Velocity.Y <= 0f)
            {
                // 足場が途切れた。
                state.Velocity.Y = rise;
                state.CoyoteTicksLeft = 0;
            }
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
                    StartAttack(ref state, BufferedSlot(state), config.Combat, input);
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

            int wireCancelFrom = state.Action.Recast ? Math.Max(attack.WireCancelFromTick, config.Skills.SpeedBoost.GustWindupTicks + 1)
                : attack.WireCancelFromTick;
            if (input.WasPressed(InputButtons.Wire) && state.Action.Ticks >= wireCancelFrom
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
                    StartAttack(ref state, next, combat, input);
                }
                return;
            }
            if (state.Combat.AttackCancelUsed || (state.Action.Attack == AttackSlot.Ground && next == AttackSlot.Ground))
            {
                return;
            }
            state.Action = default;
            if (!TryBeginHold(ref state, next, input, combat))
            {
                StartAttack(ref state, next, combat, input);
            }
            state.Combat.AttackCancelUsed = true;
        }

        // 攻撃を始める（先行入力を使い切る）。押したときに横を入れていれば、その向きを向く。照準の方向を覚える（照準の向きに置く技）。
        // 特殊攻撃ならクールタイムを始め、必殺技ならゲージを使う。
        private static void StartAttack(ref CharacterState state, AttackSlot slot, CombatTuning combat, in InputFrame input)
        {
            if (state.Combat.BufferedFacing != 0)
            {
                state.Facing = state.Combat.BufferedFacing;
            }
            bool recast = CanRecast(state, slot, combat);
            state.Action = new ActionState
            {
                Kind = ActionKind.Attack,
                Attack = slot,
                Ticks = 0,
                Advance = 1,
                Aim = input.Aim,
                Recast = recast,
            };
            state.Combat.AttackBufferTicksLeft = 0;
            state.Combat.AttackSerial++;
            switch (slot)
            {
                case AttackSlot.Special1:
                    Spend(ref state.Combat.Special1Spent, ref state.Combat.Special1CooldownTicksLeft, combat.Special1MaxCharges, combat.Special1CooldownTicks);
                    break;
                case AttackSlot.Special2:
                    Spend(ref state.Combat.Special2Spent, ref state.Combat.Special2CooldownTicksLeft, combat.Special2MaxCharges, combat.Special2CooldownTicks);
                    break;
                case AttackSlot.Super:
                    state.Combat.Gauge -= combat.SuperGaugeCost;
                    break;
            }
        }

        // 特殊攻撃を 1 回使う。続けて使える技（スタック）は回数を 1 つ減らし、戻るのを待っていなければクールタイムを始める。
        // ふつうの技は、クールタイムを始める。
        private static void Spend(ref int spent, ref int cooldown, int maxCharges, int cooldownTicks)
        {
            if (maxCharges > 1)
            {
                spent++;
                if (cooldown <= 0)
                {
                    cooldown = cooldownTicks;
                }
                return;
            }
            cooldown = cooldownTicks;
        }

        // スタックの技: クールタイムが終わったら 1 つ戻し、まだ減っていれば次のクールタイムを始める。
        private static void Recharge(ref int spent, ref int cooldown, int maxCharges, int cooldownTicks)
        {
            if (maxCharges > 1 && cooldown == 0 && spent > 0)
            {
                spent--;
                if (spent > 0)
                {
                    cooldown = cooldownTicks;
                }
            }
        }

        // Locomotion と同じ条件で、このティックにジャンプできるか（地上・コヨーテタイム・空中ジャンプの残り）。
        private static bool CanJump(in CharacterState state, MovementTuning movement)
        {
            return state.Grounded || state.CoyoteTicksLeft > 0 || state.AirJumpsUsed < movement.AirJumps;
        }
    }
}
