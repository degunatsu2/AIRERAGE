using System;
using Degunatsu.Core.Common;
using Degunatsu.Core.Input;
using Degunatsu.Core.Simulation;

namespace Degunatsu.Core.Movement
{
    /// <summary>
    /// ワイヤーに接続していないときの速度の計算（走行、空中制御、重力、ジャンプ）。
    /// 位置の更新と衝突は KinematicMotor が行う。
    /// </summary>
    public static class Locomotion
    {
        public static void UpdateVelocity(ref CharacterState state, in InputFrame input, MovementTuning tuning)
        {
            float dt = SimTime.DeltaTime;
            float inputX = MathF.Abs(input.Move.X) >= tuning.MoveDeadzone ? input.Move.X : 0f;

            state.Velocity.X = ComputeHorizontalVelocity(state.Velocity.X, inputX, state.Grounded, tuning, dt);

            state.Velocity.Y -= tuning.Gravity * dt;
            if (state.Velocity.Y < -tuning.MaxFallSpeed)
            {
                state.Velocity.Y = -tuning.MaxFallSpeed;
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

        private static float ComputeHorizontalVelocity(float vx, float inputX, bool grounded, MovementTuning tuning, float dt)
        {
            float target = inputX * tuning.RunSpeed;
            if (grounded)
            {
                return SimMath.MoveTowards(vx, target, tuning.GroundAcceleration * dt);
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
