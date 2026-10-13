using System;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Common;
using Degunatsu.Core.Input;
using Degunatsu.Core.Movement;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Wire;

namespace Degunatsu.Core.Simulation
{
    /// <summary>
    /// キャラクター1人分の1ティックの処理の順序を決める。
    /// 入力 → アクション（攻撃）→ ワイヤーのフェーズ → 速度（ワイヤーまたは通常移動）→ 移動と衝突 → ロープ長の補正 → 接地。
    /// アクション中（攻撃中・のけぞり中）は、移動・ジャンプ・ワイヤーの入力を無視し、向きも変えない（ActionController.ControlInput）。
    /// キャンセル（攻撃→ジャンプ、攻撃→ワイヤー）で Free に戻ったティックは、入力をそのまま使い、このティックにジャンプ・射出する。
    /// ヒットストップの間は、何も進めずに止まる（ボタンの前の状態も変えないので、押し続けたボタンは止まりが解けてから効く。
    /// 止まっている間に押して離した攻撃（押したときの向き）とジャンプも、止まりが解けてから効く）。
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
            if (state.Combat.HitstopTicksLeft > 0)
            {
                state.Combat.HitstopTicksLeft--;
                ActionController.RecordHitstopPresses(ref state, input, config);
                return;
            }

            InputFrame frame = ActionController.TakeHitstopPresses(ref state, input, out PlayerInput effectiveInput);
            MovementTuning movement = config.Movement;
            var halfExtents = movement.HalfExtents;

            _motor.Depenetrate(ref state.Position, halfExtents);
            if (state.Action.IsFree)
            {
                UpdateFacing(ref state, frame, movement);
            }

            ActionController.Update(ref state, frame, config);
            if (!state.Action.IsFree)
            {
                frame = new InputFrame(ActionController.ControlInput(state, effectiveInput), state.PreviousButtons);
                state.JumpBufferTicksLeft = 0;
            }

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
                ActionController.ApplyMotion(ref state, config.Combat);
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
