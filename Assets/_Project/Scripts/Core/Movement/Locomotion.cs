using System;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Input;
using Airerage.Core.Simulation;

namespace Airerage.Core.Movement
{
    /// <summary>
    /// ワイヤーに接続していないときの速度の計算（走行、空中制御、重力、ジャンプ）。
    /// 位置の更新と衝突は KinematicMotor が行う。
    /// </summary>
    public static class Locomotion
    {
        /// <param name="groundDrift">地面に立っているときに足す、走る速さの目標（m/s。横向きの風で流される分。StageZones.GroundDrift）。</param>
        public static void UpdateVelocity(ref CharacterState state, in InputFrame input, MovementTuning tuning, float groundDrift = 0f)
        {
            float dt = SimTime.DeltaTime;
            float inputX = MathF.Abs(input.Move.X) >= tuning.MoveDeadzone ? input.Move.X : 0f;

            // のけぞり中（吹き飛び）は横の上限で切らない（D-43 と同じ考え）。
            bool knockback = state.Action.Kind == ActionKind.Hitstun;
            bool landing = state.Grounded && state.GroundedTicks <= tuning.LandingGraceTicks;
            state.Velocity.X = ComputeHorizontalVelocity(state.Velocity.X, inputX, state.Grounded, landing, knockback, tuning, dt, groundDrift);

            float fallSpeedBefore = state.Velocity.Y;
            state.Velocity.Y -= tuning.Gravity * dt;
            if (!knockback && state.Velocity.Y < -tuning.FallSpeedSoftStart)
            {
                // 前の上限（FallSpeedSoftStart）を超えて落ちる速さは、上限に近いほどゆっくり伸びる（SpeedRamp。2026-11-03 のユーザーの要望）。
                float down = SpeedRamp.Limit(-fallSpeedBefore, -state.Velocity.Y, tuning.FallSpeedSoftStart, tuning.MaxFallSpeed,
                    SpeedRamp.ScaleFor(tuning.FallSpeedSoftStart, tuning.MaxFallSpeed, tuning.Gravity, tuning.SpeedRampSeconds));
                state.Velocity.Y = -down;
            }
            if (state.Velocity.Y < -tuning.MaxFallSpeed)
            {
                // のけぞり中は、吹き飛びの下向きの速さを上限で切らない（重力で上限より速くならないだけ。D-43）。
                bool keepKnockback = state.Action.Kind == ActionKind.Hitstun;
                state.Velocity.Y = keepKnockback ? MathF.Min(fallSpeedBefore, -tuning.MaxFallSpeed) : -tuning.MaxFallSpeed;
            }

            UpdateJump(ref state, input, tuning);
        }

        /// <summary>
        /// 移動後の接地状態を反映し、コヨーテタイムと空中ジャンプの回数を更新する。
        /// </summary>
        public static void ApplyGroundState(ref CharacterState state, bool groundBelow, MovementTuning tuning)
        {
            state.Grounded = groundBelow && state.Velocity.Y <= 0f;
            // 着地してからのティック数（着地の直後は勢いを落とさない。LandingGraceTicks）。
            state.GroundedTicks = state.Grounded ? Math.Min(state.GroundedTicks + 1, GroundedTicksCap) : 0;
            if (state.Grounded)
            {
                state.CoyoteTicksLeft = tuning.CoyoteTicks;
                state.AirJumpsUsed = 0;
                if (state.Velocity.Y < 0f)
                {
                    state.Velocity.Y = 0f;
                }
            }
            else if (state.CoyoteTicksLeft > 0)
            {
                state.CoyoteTicksLeft--;
            }
        }

        /// <summary>GroundedTicks の上限（数え続けないように）。</summary>
        public const int GroundedTicksCap = 1000;

        private static float ComputeHorizontalVelocity(float vx, float inputX, bool grounded, bool landing, bool knockback, MovementTuning tuning,
            float dt, float groundDrift)
        {
            float target = inputX * (grounded || tuning.AirRunSpeed <= 0f ? tuning.RunSpeed : tuning.AirRunSpeed);
            float result;
            if (grounded)
            {
                // 着地の直後（LandingGraceTicks）は、走る速さより速い勢いを、逆の向きに入れていなければ落とさない（すぐにジャンプすれば
                // 勢いを保ったまま空中へ戻れる。2026-11-03 のユーザーの要望）。その後は急速に走る速さへ落ちる。
                bool overspeed = MathF.Abs(vx) > MathF.Abs(target + groundDrift) && MathF.Abs(vx) > tuning.RunSpeed;
                bool against = inputX != 0f && MathF.Sign(inputX) != MathF.Sign(vx);
                result = landing && overspeed && !against ? vx : SimMath.MoveTowards(vx, target + groundDrift, tuning.GroundAcceleration * dt);
                return knockback ? result : MathF.Max(-tuning.MaxGroundSpeed, MathF.Min(tuning.MaxGroundSpeed, result));
            }

            if (inputX == 0f)
            {
                result = SimMath.MoveTowards(vx, 0f, tuning.AirDrag * dt);
            }
            else if (MathF.Sign(vx) == MathF.Sign(inputX) && MathF.Abs(vx) > MathF.Abs(target))
            {
                // ワイヤー解除後などで走行速度を超えている場合、同じ向きに入力している間は勢いを保つ。
                result = vx;
            }
            else
            {
                result = SimMath.MoveTowards(vx, target, tuning.AirAcceleration * dt);
            }
            return knockback ? result : MathF.Max(-tuning.MaxAirSpeed, MathF.Min(tuning.MaxAirSpeed, result));
        }

        private static void UpdateJump(ref CharacterState state, in InputFrame input, MovementTuning tuning)
        {
            bool pressed = input.WasPressed(InputButtons.Jump);
            bool canGroundJump = state.Grounded || state.CoyoteTicksLeft > 0;

            if (state.JumpSquatTicksLeft > 0)
            {
                // 地上のジャンプの発生を待っている（JumpStartupTicks）。待ち終わったら、足場に残っていれば跳ぶ。
                state.JumpSquatTicksLeft--;
                if (state.JumpSquatTicksLeft == 0 && canGroundJump)
                {
                    state.CoyoteTicksLeft = 0;
                    state.Grounded = false;
                    state.Velocity.Y = tuning.JumpSpeed;
                }
                return;
            }

            if (pressed && !canGroundJump && state.AirJumpsUsed < tuning.AirJumps)
            {
                state.AirJumpsUsed++;
                state.JumpBufferTicksLeft = 0;
                state.Velocity.Y = tuning.JumpSpeed;
                return;
            }

            if (pressed)
            {
                state.JumpBufferTicksLeft = tuning.JumpBufferTicks;
            }

            if (state.JumpBufferTicksLeft > 0 && canGroundJump)
            {
                state.JumpBufferTicksLeft = 0;
                if (tuning.JumpStartupTicks > 0)
                {
                    state.JumpSquatTicksLeft = tuning.JumpStartupTicks;
                    return;
                }
                state.CoyoteTicksLeft = 0;
                state.Grounded = false;
                state.Velocity.Y = tuning.JumpSpeed;
                return;
            }

            if (state.JumpBufferTicksLeft > 0)
            {
                state.JumpBufferTicksLeft--;
            }
        }
    }
}
