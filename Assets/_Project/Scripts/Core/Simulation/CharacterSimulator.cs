using System;
using System.Collections.Generic;
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
    /// 入力 → アクション（攻撃）→ ワイヤーのフェーズ → 速度（ワイヤーまたは通常移動）→ 移動と衝突 → ロープ長の補正 → 接地
    /// → のけぞり中の跳ね返り → ステージのギミック（StageZones）。
    /// アクション中（攻撃中・のけぞり中）は、移動・ジャンプ・ワイヤーの入力を無視し、向きも変えない（ActionController.ControlInput）。
    /// キャンセル（攻撃→ジャンプ、攻撃→ワイヤー）で Free に戻ったティックは、入力をそのまま使い、このティックにジャンプ・射出する。
    /// ヒットストップの間は、何も進めずに止まる（ボタンの前の状態も変えないので、押し続けたボタンは止まりが解けてから効く。
    /// 止まっている間に押して離した攻撃（押したときの向き）とジャンプも、止まりが解けてから効く）。
    /// 撃破されて復帰を待っている間も、何も進めない（docs/12 T4-2）。
    /// </summary>
    public sealed class CharacterSimulator
    {
        private readonly KinematicMotor _motor;
        private readonly WireController _wire;
        private readonly IReadOnlyList<StageZone> _zones;

        /// <param name="zones">ステージのギミック（効かせる順に並べたもの）。null ならない。</param>
        public CharacterSimulator(ICollisionQuery collision, IReadOnlyList<StageZone> zones = null)
        {
            if (collision == null)
            {
                throw new ArgumentNullException(nameof(collision));
            }
            _motor = new KinematicMotor(collision);
            _wire = new WireController(collision, _motor);
            _zones = zones;
        }

        public void Step(ref CharacterState state, PlayerInput input, CharacterConfig config)
        {
            if (state.Combat.IsWaitingToRespawn)
            {
                // 撃破されて復帰を待っている間は動かない（ボタンの前の状態だけ更新し、待ち終わった直後に押したとみなさない）。
                state.Combat.RespawnTicksLeft--;
                CountDownInvincibility(ref state);
                state.PreviousButtons = input.Buttons;
                return;
            }
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
            System.Numerics.Vector3 startPosition = state.Position;
            if (state.Action.IsFree)
            {
                UpdateFacing(ref state, frame, movement);
            }

            CountDownInvincibility(ref state);
            ActionController.Update(ref state, frame, config);
            if (state.Action.Kind == ActionKind.Attack)
            {
                // 復帰した後の無敵は、自分が攻撃を出すと終わる【仮】（無敵のまま一方的に攻撃できないように）。
                state.Combat.InvincibleTicksLeft = 0;
            }
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
            System.Numerics.Vector3 velocityBeforeMove = state.Velocity;
            _motor.Move(ref state.Position, ref state.Velocity, halfExtents, state.Velocity * SimTime.DeltaTime);

            if (attached)
            {
                _wire.UpdateRopeWrap(ref state, beforeMove, config.Wire);
                _wire.ApplyRopeConstraint(ref state, halfExtents);
            }

            Locomotion.ApplyGroundState(ref state, _motor.ProbeGround(state.Position, halfExtents), movement);
            Bounce(ref state, velocityBeforeMove, config.Combat);
            StageZones.Apply(ref state, startPosition, new System.Numerics.Vector2(halfExtents.X, halfExtents.Y), _zones);

            // ゲームプレイは XY 平面（docs/01_game_design.md 4章）。
            state.Position.Z = 0f;
            state.Velocity.Z = 0f;
            state.PreviousButtons = input.Buttons;
        }

        /// <summary>
        /// のけぞり中に床・壁・天井へ勢いよくぶつかったら跳ね返る（ユーザーの要望、2026-10-13）。地形は軸平行の箱なので、
        /// ぶつかると速度の X か Y が消える（床は、移動の衝突か接地のどちらかで消える）。消えた成分が BounceMinSpeed 以上なら、
        /// 逆向きに BounceRestitution 倍で返す。床で跳ね返ったら接地をやめる。接地の後に呼ぶ。
        /// </summary>
        private static void Bounce(ref CharacterState state, System.Numerics.Vector3 before, CombatTuning combat)
        {
            if (state.Action.Kind != ActionKind.Hitstun)
            {
                return;
            }
            bool bounced = false;
            if (MathF.Abs(before.X) >= combat.BounceMinSpeed && MathF.Abs(state.Velocity.X) < MathF.Abs(before.X) * 0.5f)
            {
                state.Velocity.X = -before.X * combat.BounceRestitution;
                bounced = true;
            }
            if (MathF.Abs(before.Y) >= combat.BounceMinSpeed && MathF.Abs(state.Velocity.Y) < MathF.Abs(before.Y) * 0.5f)
            {
                state.Velocity.Y = -before.Y * combat.BounceRestitution;
                if (state.Velocity.Y > 0f)
                {
                    state.Grounded = false;
                }
                bounced = true;
            }
            if (bounced)
            {
                state.Combat.BounceCount++;
            }
        }

        private static void CountDownInvincibility(ref CharacterState state)
        {
            if (state.Combat.InvincibleTicksLeft > 0)
            {
                state.Combat.InvincibleTicksLeft--;
            }
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
