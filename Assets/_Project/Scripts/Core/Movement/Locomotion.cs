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

            state.Velocity.X = ComputeHorizontalVelocity(state.Velocity.X, inputX, state.Grounded, tuning, dt, groundDrift);

            float fallSpeedBefore = state.Velocity.Y;
            state.Velocity.Y -= tuning.Gravity * dt;
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

        private static float ComputeHorizontalVelocity(float vx, float inputX, bool grounded, MovementTuning tuning, float dt, float groundDrift)
        {
            float target = inputX * (grounded || tuning.AirRunSpeed <= 0f ? tuning.RunSpeed : tuning.AirRunSpeed);
            if (grounded)
            {
                return SimMath.MoveTowards(vx, target + groundDrift, tuning.GroundAcceleration * dt);
            }

            if (inputX == 0f)
            {
                return SimMath.MoveTowards(vx, 0f, tuning.AirDrag * dt);
            }

            // ワイヤー解除後などで走行速度を超えている場合、同じ向きに入力している間は勢いを保つ。
            bool sameDirection = MathF.Sign(vx) == MathF.Sign(inputX);
            if (sameDirection && MathF.Abs(vx) > MathF.Abs(target))
            {
                return vx;
            }
            return SimMath.MoveTowards(vx, target, tuning.AirAcceleration * dt);
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
