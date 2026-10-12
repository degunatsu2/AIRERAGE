using System;
using Degunatsu.Core.Common;
using Degunatsu.Core.Input;
using Degunatsu.Core.Movement;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Wire;

namespace Degunatsu.Core.Simulation
{
    /// <summary>
    /// キャラクター1人分の1ティックの処理の順序を決める。
    /// 入力 → ワイヤーのフェーズ → 速度（ワイヤーまたは通常移動）→ 移動と衝突 → ロープ長の補正 → 接地。
    /// </summary>
    public sealed class CharacterSimulator
    {
        private readonly KinematicMotor _motor;
        private readonly WireController _wire;

        public CharacterSimulator(ICollisionQuery collision)
        {
            if (collision == null)
            {
                throw new ArgumentNullException(nameof(collision));
            }
            _motor = new KinematicMotor(collision);
            _wire = new WireController(collision, _motor);
        }

        public void Step(ref CharacterState state, PlayerInput input, CharacterConfig config)
        {
            var frame = new InputFrame(input, state.PreviousButtons);
            MovementTuning movement = config.Movement;
            var halfExtents = movement.HalfExtents;

            _motor.Depenetrate(ref state.Position, halfExtents);
            UpdateFacing(ref state, frame, movement);

            _wire.UpdatePhase(ref state, frame, config.Wire);

            bool attached = state.Wire.Phase == WirePhase.Attached;
            if (attached)
            {
                state.Velocity = _wire.ComputeAttachedVelocity(ref state, frame, config.Wire, movement.Gravity);
                state.JumpBufferTicksLeft = 0;
            }
            else
            {
                Locomotion.UpdateVelocity(ref state, frame, movement);
            }

            System.Numerics.Vector3 beforeMove = state.Position;
            _motor.Move(ref state.Position, ref state.Velocity, halfExtents, state.Velocity * SimTime.DeltaTime);

            if (attached)
            {
                _wire.UpdateRopeWrap(ref state, beforeMove, config.Wire);
                _wire.ApplyRopeConstraint(ref state, halfExtents);
            }

            Locomotion.ApplyGroundState(ref state, _motor.ProbeGround(state.Position, halfExtents), movement);

            // ゲームプレイは XY 平面（docs/01_game_design.md 4章）。
            state.Position.Z = 0f;
            state.Velocity.Z = 0f;
            state.PreviousButtons = input.Buttons;
        }

        private static void UpdateFacing(ref CharacterState state, in InputFrame input, MovementTuning movement)
        {
            if (input.Move.X >= movement.MoveDeadzone)
            {
                state.Facing = 1;
            }
            else if (input.Move.X <= -movement.MoveDeadzone)
            {
                state.Facing = -1;
            }
        }
    }
}
