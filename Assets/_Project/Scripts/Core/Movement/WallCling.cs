using System;
using System.Numerics;
using Airerage.Core.Input;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;

namespace Airerage.Core.Movement
{
    /// <summary>
    /// 壁への張り付きと壁ジャンプ（ユーザーの要望、2026-10-19）。
    /// - 張り付く: 空中で、何もしていない（攻撃・ガード・のけぞりでない）、ワイヤーの接続中でない、凍っていないときに、横から地形に
    ///   ぶつかる（横の速さが止められる）と、その壁に張り付く。上へ速く動いている間（MovementTuning.WallClingMaxRiseSpeed より速い）は
    ///   張り付かない（ジャンプ台などで昇っている途中）。壁の上の端が頭より下なら張り付かない（縁を越えて上がれる）。
    /// - 張り付いている間: WallSlideSpeed で少しずつずり落ちる。合わせて WallClingMaxTicks（4 秒）たつと離れ、着地するか壁ジャンプ
    ///   するまで、もう張り付かない（WallClingTicks は張り付いていた長さで、着地と壁ジャンプで 0 に戻る。ユーザーの要望、2026-10-20）。
    /// - 壁ジャンプ: ジャンプを押すと、壁から離れる向きの斜め上へ跳ぶ（WallJumpSpeedX / Y）。回数の制限はなく、空中ジャンプの回数と
    ///   攻撃→攻撃のキャンセル（D-17）を戻す。
    /// - やめる: 着地、壁と反対へ入れる、攻撃・技・ガード、ワイヤーの接続、凍る、のけぞる、壁がなくなる。
    /// 状態は CharacterState.WallSide / WallClingTicks。表示と音のために WallJumpCount を数える。
    /// </summary>
    public static class WallCling
    {
        /// <summary>張り付きを続けるか（やめる条件に当たらないか）。</summary>
        public static bool Continues(in CharacterState state, in InputFrame frame, MovementTuning tuning, KinematicMotor motor, Vector3 halfExtents)
        {
            if (state.WallSide == 0 || state.Grounded || !state.Action.IsFree || state.Combat.IsFrozen
                || state.Wire.Phase == WirePhase.Attached || state.Wire.Phase == WirePhase.Firing)
            {
                return false;
            }
            if (frame.Move.X * state.WallSide <= -tuning.MoveDeadzone)
            {
                // 壁と反対へ入れた: 離れる。
                return false;
            }
            return motor.ProbeWall(state.Position, halfExtents, state.WallSide);
        }

        /// <summary>張り付いている間の速度（ジャンプを押していれば壁ジャンプ）。</summary>
        public static void Hold(ref CharacterState state, in InputFrame frame, MovementTuning tuning)
        {
            if (frame.WasPressed(InputButtons.Jump) || state.JumpBufferTicksLeft > 0)
            {
                Jump(ref state, tuning);
                return;
            }
            state.WallClingTicks++;
            state.Velocity = new Vector3(0f, -tuning.WallSlideSpeed, 0f);
            state.JumpBufferTicksLeft = 0;
            state.CoyoteTicksLeft = 0;
            if (state.WallClingTicks >= tuning.WallClingMaxTicks)
            {
                // 時間切れ: 離れる（WallClingTicks は残し、着地か壁ジャンプまで張り付かない）。
                state.WallSide = 0;
            }
        }

        /// <summary>張り付いていられる残りの割合（0〜1。張り付いていなければ 0）。表示用。</summary>
        public static float Remaining(in CharacterState state, MovementTuning tuning)
        {
            if (state.WallSide == 0 || tuning.WallClingMaxTicks <= 0)
            {
                return 0f;
            }
            return Math.Max(0f, 1f - state.WallClingTicks / (float)tuning.WallClingMaxTicks);
        }

        /// <summary>壁ジャンプ（壁から離れる向きの斜め上へ）。空中ジャンプと攻撃→攻撃のキャンセルを戻す。</summary>
        public static void Jump(ref CharacterState state, MovementTuning tuning)
        {
            int away = -state.WallSide;
            state.Velocity = new Vector3(away * tuning.WallJumpSpeedX, tuning.WallJumpSpeedY, 0f);
            state.Facing = away;
            state.AirJumpsUsed = 0;
            state.Combat.AttackCancelUsed = false;
            state.WallSide = 0;
            state.WallClingTicks = 0;
            state.WallJumpCount++;
            state.Grounded = false;
            state.JumpBufferTicksLeft = 0;
            state.CoyoteTicksLeft = 0;
        }

        /// <summary>頭の高さを調べる薄い箱の、体の高さに対する位置（中心からの割合）と半分の高さ。</summary>
        private const float HeadProbeOffset = 0.6f;
        private const float HeadProbeHalfHeight = 0.1f;

        // 頭の高さまで壁があるか（壁の上の端が頭より下なら張り付かず、そのまま縁を越えて上がれるようにする）。
        private static bool WallAtHead(Vector3 position, Vector3 halfExtents, int side, KinematicMotor motor)
        {
            var head = new Vector3(position.X, position.Y + halfExtents.Y * HeadProbeOffset, position.Z);
            return motor.ProbeWall(head, new Vector3(halfExtents.X, HeadProbeHalfHeight, halfExtents.Z), side);
        }

        /// <summary>
        /// 移動の後に、横から壁にぶつかっていたら張り付く（頭の高さまで壁がないときは張り付かない。縁をよじ登れるように）。velocityBeforeMove は移動の前の速度（ぶつかると横の成分が消える）。
        /// </summary>
        public static bool TryStart(ref CharacterState state, Vector3 velocityBeforeMove, MovementTuning tuning, KinematicMotor motor,
            Vector3 halfExtents)
        {
            if (state.WallSide != 0 || state.Grounded || !state.Action.IsFree || state.Combat.IsFrozen
                || state.Wire.Phase == WirePhase.Attached || velocityBeforeMove.Y > tuning.WallClingMaxRiseSpeed
                || state.WallClingTicks >= tuning.WallClingMaxTicks)
            {
                return false;
            }
            float before = velocityBeforeMove.X;
            if (MathF.Abs(before) < 0.5f || MathF.Abs(state.Velocity.X) >= MathF.Abs(before) * 0.5f)
            {
                return false;
            }
            int side = before > 0f ? 1 : -1;
            if (!motor.ProbeWall(state.Position, halfExtents, side) || !WallAtHead(state.Position, halfExtents, side, motor))
            {
                return false;
            }
            state.WallSide = side;
            state.Velocity = Vector3.Zero;
            state.Facing = -side;
            return true;
        }
    }
}
