using System.Numerics;
using Degunatsu.Core.Common;
using Degunatsu.Core.Input;
using Degunatsu.Core.Movement;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire.Models;

namespace Degunatsu.Core.Wire
{
    /// <summary>
    /// ワイヤーの共通制御（両タイプ共通）。フェーズ遷移、照準とエイムアシスト、フックの飛翔と接続判定、
    /// 解除と強制解除、クールダウン、速度の上限、ロープ長の補正を担う。
    /// 接続中の運動だけを IWireMotionModel に任せる。
    /// </summary>
    public sealed class WireController
    {
        // ロープ長を超えたとみなす余裕（m）。
        private const float RopeSlack = 1e-4f;

        private readonly ICollisionQuery _collision;
        private readonly KinematicMotor _motor;

        public WireController(ICollisionQuery collision, KinematicMotor motor)
        {
            _collision = collision;
            _motor = motor;
        }

        /// <summary>
        /// 入力とタイマーに基づいてフェーズを進める。移動の計算より前に呼ぶ。
        /// </summary>
        public void UpdatePhase(ref CharacterState state, in InputFrame input, WireTuning tuning)
        {
            switch (state.Wire.Phase)
            {
                case WirePhase.Ready:
                    if (input.WasPressed(InputButtons.Wire))
                    {
                        Fire(ref state, input, tuning);
                        AdvanceHook(ref state, tuning);
                    }
                    break;

                case WirePhase.Firing:
                    state.Wire.PhaseTicks++;
                    if (tuning.ButtonMode == WireButtonMode.Hold && !input.IsHeld(InputButtons.Wire))
                    {
                        Miss(ref state, WireMissReason.ButtonReleased);
                    }
                    else if (tuning.ButtonMode == WireButtonMode.Toggle && input.WasPressed(InputButtons.Wire))
                    {
                        Miss(ref state, WireMissReason.ButtonToggled);
                    }
                    else
                    {
                        AdvanceHook(ref state, tuning);
                    }
                    break;

                case WirePhase.Attached:
                    state.Wire.PhaseTicks++;
                    UpdateAttached(ref state, input, tuning);
                    break;

                case WirePhase.Retracting:
                    state.Wire.PhaseTicks++;
                    if (state.Wire.PhaseTicks >= tuning.MissRetractTicks)
                    {
                        EnterCooldown(ref state, tuning.CooldownTicks);
                    }
                    break;

                case WirePhase.Cooldown:
                    state.Wire.PhaseTicks++;
                    state.Wire.CooldownTicksLeft--;
                    if (state.Wire.CooldownTicksLeft <= 0)
                    {
                        SetPhase(ref state.Wire, WirePhase.Ready);
                        state.Wire.CooldownTicksLeft = 0;
                    }
                    break;
            }
        }

        /// <summary>
        /// 接続中の速度を運動モデルで計算し、共通の最高速で制限する。ロープ長も更新する。
        /// </summary>
        public Vector3 ComputeAttachedVelocity(ref CharacterState state, in InputFrame input, WireTuning tuning, float gravity)
        {
            IWireMotionModel model = WireMotionModels.Get(tuning.ModelType);
            var context = new WireMotionContext(
                state.Position, state.Velocity, state.Wire.Anchor, state.Wire.RopeLength,
                state.Wire.PhaseTicks, state.Wire.AttachSpeed,
                input.Move, gravity, SimTime.DeltaTime, tuning);
            WireMotionResult result = model.Step(context);
            state.Wire.RopeLength = result.RopeLength;
            return SimMath.ClampMagnitude(result.Velocity, tuning.MaxAttachedSpeed);
        }

        /// <summary>
        /// 移動後に、接続点との距離がロープ長を超えていれば内側へ戻し、外向きの速度を取り除く。
        /// 戻す移動にも衝突判定を使うので、地形にめり込まない。
        /// </summary>
        public void ApplyRopeConstraint(ref CharacterState state, Vector3 halfExtents)
        {
            Vector3 fromAnchor = state.Position - state.Wire.Anchor;
            fromAnchor.Z = 0f;
            float distance = fromAnchor.Length();
            if (distance <= state.Wire.RopeLength + RopeSlack || distance < SimMath.Epsilon)
            {
                return;
            }

            Vector3 radial = fromAnchor / distance;
            Vector3 correction = -radial * (distance - state.Wire.RopeLength);
            _motor.Move(ref state.Position, ref state.Velocity, halfExtents, correction);

            float outward = Vector3.Dot(state.Velocity, radial);
            if (outward > 0f)
            {
                state.Velocity -= radial * outward;
            }
        }

        /// <summary>
        /// 接続を解除する。速度は共通の出口速度上限で制限して引き継ぐ。
        /// RestoreAirJumpsOnRelease なら空中ジャンプの回数を回復する。
        /// その後は ReleaseCooldownTicks だけ待つ（0 なら次のティックから射出できる）。
        /// </summary>
        public static void Release(ref CharacterState state, WireReleaseReason reason, WireTuning tuning)
        {
            state.Velocity = SimMath.ClampMagnitude(state.Velocity, tuning.ExitSpeedCap);
            state.Wire.LastReleaseReason = reason;
            if (tuning.RestoreAirJumpsOnRelease)
            {
                state.AirJumpsUsed = 0;
            }
            EnterCooldown(ref state, tuning.ReleaseCooldownTicks);
        }

        private void UpdateAttached(ref CharacterState state, in InputFrame input, WireTuning tuning)
        {
            if (tuning.ButtonMode == WireButtonMode.Hold && !input.IsHeld(InputButtons.Wire))
            {
                Release(ref state, WireReleaseReason.ButtonReleased, tuning);
            }
            else if (tuning.ButtonMode == WireButtonMode.Toggle && input.WasPressed(InputButtons.Wire))
            {
                Release(ref state, WireReleaseReason.ButtonToggled, tuning);
            }
            else if (state.Wire.PhaseTicks >= tuning.MaxAttachTicks)
            {
                Release(ref state, WireReleaseReason.MaxDuration, tuning);
            }
            else if (tuning.ReleaseWhenLineBlocked && !WireTargeting.HasLineOfSight(state.Position, state.Wire.Anchor, _collision))
            {
                Release(ref state, WireReleaseReason.LineBlocked, tuning);
            }
        }

        private void Fire(ref CharacterState state, in InputFrame input, WireTuning tuning)
        {
            Vector3 origin = state.Position;
            WireAim aim = WireTargeting.ResolveAim(origin, input.Aim, state.Facing, tuning, _collision);

            SetPhase(ref state.Wire, WirePhase.Firing);
            state.Wire.HasTargetPoint = aim.HasTargetPoint;
            state.Wire.TargetPoint = aim.HasTargetPoint ? aim.TargetPoint : default;
            state.Wire.HookPosition = origin;
            state.Wire.HookDirection = aim.Direction;
            state.Wire.HookTraveled = 0f;
            state.Wire.LastReleaseReason = WireReleaseReason.None;
            state.Wire.LastMissReason = WireMissReason.None;
        }

        private void AdvanceHook(ref CharacterState state, WireTuning tuning)
        {
            ref WireState wire = ref state.Wire;
            float remaining = tuning.MaxLength - wire.HookTraveled;
            float step = tuning.FireSpeed * SimTime.DeltaTime;
            if (step > remaining)
            {
                step = remaining;
            }

            float targetDistance = float.MaxValue;
            if (wire.HasTargetPoint)
            {
                Vector3 toTarget = wire.TargetPoint - wire.HookPosition;
                toTarget.Z = 0f;
                targetDistance = toTarget.Length();
            }

            float castDistance = targetDistance < step ? targetDistance : step;
            if (castDistance > 0f && _collision.Raycast(wire.HookPosition, wire.HookDirection, castDistance, out CastHit hit))
            {
                if (hit.Attachable)
                {
                    Attach(ref state, hit.Point, tuning);
                }
                else
                {
                    // 当たった点を外れた位置として残す（外れた表示に使う）。
                    wire.HookPosition = hit.Point;
                    Miss(ref state, WireMissReason.HitNonAttachable);
                }
                return;
            }

            if (wire.HasTargetPoint && targetDistance <= step)
            {
                Attach(ref state, wire.TargetPoint, tuning);
                return;
            }

            wire.HookPosition += wire.HookDirection * step;
            wire.HookTraveled += step;
            if (wire.HookTraveled >= tuning.MaxLength - SimMath.Epsilon)
            {
                Miss(ref state, WireMissReason.OutOfRange);
            }
        }

        private static void Attach(ref CharacterState state, Vector3 anchor, WireTuning tuning)
        {
            anchor.Z = 0f;
            SetPhase(ref state.Wire, WirePhase.Attached);
            state.Wire.Anchor = anchor;
            state.Wire.HookPosition = anchor;

            Vector3 toAnchor = anchor - state.Position;
            toAnchor.Z = 0f;
            float distance = toAnchor.Length();
            float ropeLength = distance < tuning.MinRopeLength ? tuning.MinRopeLength : distance;
            state.Wire.RopeLength = ropeLength > tuning.MaxLength ? tuning.MaxLength : ropeLength;
            state.Wire.AttachSpeed = state.Velocity.Length();
        }

        private static void Miss(ref CharacterState state, WireMissReason reason)
        {
            SetPhase(ref state.Wire, WirePhase.Retracting);
            state.Wire.LastMissReason = reason;
            state.Wire.HasTargetPoint = false;
        }

        private static void EnterCooldown(ref CharacterState state, int cooldownTicks)
        {
            state.Wire.HasTargetPoint = false;
            if (cooldownTicks <= 0)
            {
                SetPhase(ref state.Wire, WirePhase.Ready);
                state.Wire.CooldownTicksLeft = 0;
                return;
            }
            SetPhase(ref state.Wire, WirePhase.Cooldown);
            state.Wire.CooldownTicksLeft = cooldownTicks;
        }

        private static void SetPhase(ref WireState wire, WirePhase phase)
        {
            wire.Phase = phase;
            wire.PhaseTicks = 0;
        }
    }
}
