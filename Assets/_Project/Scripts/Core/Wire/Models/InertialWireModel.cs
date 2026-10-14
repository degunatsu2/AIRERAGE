using System;
using System.Numerics;
using Degunatsu.Core.Common;

namespace Degunatsu.Core.Wire.Models
{
    /// <summary>
    /// タイプA: 物理・慣性重視。ロープ長の拘束と重力による振り子運動。
    /// 巻き取るとロープが縮み、角運動量の保存で振りが速くなる（上限は共通の MaxAttachedSpeed）。
    /// 遅い状態で接続した直後は、速さが下限（StartPullSpeed）に届くように接続点の方向へ強く引く。
    /// 上下の入力でロープの長さの扱いを変える（WireLengthMode。D-41、D-50。どの入力にどれを割り当てるかは調整値）:
    /// - 標準（縦の入力なし）: 長さを保ち（張力を保ったまま）、左右の入力だけで振る（HoldSwingAcceleration）。接続点へ近づいたら、
    ///   その分だけロープを巻き取ってたるませない（たるんだロープが急に張って跳ねないように）。伸ばすときも同じ。
    /// - 上（真上に近い入力）: 巻き取る（ReelSpeed。以前の標準の動き）。
    /// - 下（斜め下を含む）: 伸ばしながら、左右の入力で振る（ReelOutSpeed。最大長まで）。
    /// 長さを保つ・伸ばすときは、接続直後に遅ければ、接続点の方向へ引く代わりに、振りの方向の速さを下限まで上げる。
    /// </summary>
    public sealed class InertialWireModel : IWireMotionModel
    {
        // ロープが張っているとみなす余裕（m）。
        private const float TautTolerance = 1e-3f;

        // 下を押しているとき、斜め下（45°）の入力で左右いっぱいの力になるようにする。
        private const float Cos45 = 0.70710678f;

        public WireMotionResult Step(in WireMotionContext context)
        {
            InertialWireTuning tuning = context.Tuning.Inertial;
            float dt = context.DeltaTime;

            Vector3 velocity = context.Velocity;
            velocity.Y -= context.Gravity * dt;

            Vector3 fromAnchor = context.Position - context.Anchor;
            fromAnchor.Z = 0f;
            float distance = fromAnchor.Length();
            if (distance < SimMath.Epsilon)
            {
                return new WireMotionResult(velocity, context.RopeLength);
            }
            Vector3 radial = fromAnchor / distance;
            Vector3 tangent = SimMath.PerpendicularXY(radial);

            WireLengthMode mode = context.Stick.Y >= tuning.UpInputThreshold ? tuning.UpMode
                : context.Stick.Y <= -tuning.DownInputThreshold ? tuning.DownMode : tuning.NeutralMode;
            bool holdLength = mode != WireLengthMode.Reel;
            if (holdLength)
            {
                float swingX = Math.Clamp(context.Stick.X / Cos45, -1f, 1f);
                velocity += tangent * (swingX * tangent.X * tuning.HoldSwingAcceleration * dt);
            }
            else
            {
                float stickAlongTangent = Vector3.Dot(new Vector3(context.Stick.X, context.Stick.Y, 0f), tangent);
                velocity += tangent * (stickAlongTangent * tuning.SwingAcceleration * dt);
            }

            if (tuning.Damping > 0f)
            {
                velocity *= MathMax(0f, 1f - tuning.Damping * dt);
            }

            float radialSpeed = Vector3.Dot(velocity, radial);
            Vector3 tangential = velocity - radial * radialSpeed;
            float floor = StartSpeedFloor(context, tuning);
            float reelSpeed;
            bool pullingAtStart = false;
            if (holdLength)
            {
                // ロープの長さを保つ（伸ばすときは負の巻き取り）。接続直後に遅ければ、振りの方向の速さを下限まで上げる。
                reelSpeed = mode == WireLengthMode.ReelOut ? -tuning.ReelOutSpeed : 0f;
                // 接続点へ近づいている速さも数える（振りの速さだけを下限まで上げると、合わせた速さが下限を超えて跳ねる）。
                float inward = MathF.Min(radialSpeed, 0f);
                float tangentialFloor = floor * floor > inward * inward ? MathF.Sqrt(floor * floor - inward * inward) : 0f;
                tangential = RaiseToFloor(tangential, tangent, context.Stick.X, tangentialFloor);
                velocity = tangential + radial * radialSpeed;
            }
            else
            {
                // 接続直後で遅いときは、速さが下限に届くように巻き取りを速くする。
                reelSpeed = tuning.ReelSpeed;
                float startPull = PullInSpeed(floor, tangential.Length());
                pullingAtStart = startPull > reelSpeed;
                if (pullingAtStart)
                {
                    reelSpeed = startPull;
                }
            }

            float lengthBefore = context.RopeLength;
            // 長さを保つ・伸ばすときは張力を保つ: 接続点へ近づいた分だけロープを巻き取り、たるませない（たるんだロープが急に張ると、
            // 遠ざかる向きの速さが1ティックで消えて、跳ねたように向きが変わる。D-50 の後のプレイテストの指摘、2026-10-14）。
            float ropeBase = holdLength && distance < lengthBefore - TautTolerance
                ? MathF.Min(lengthBefore, MathMax(context.Tuning.MinRopeLength, distance))
                : lengthBefore;
            float lengthAfter;
            if (reelSpeed >= 0f)
            {
                lengthAfter = MathMax(context.Tuning.MinRopeLength, ropeBase - reelSpeed * dt);
                if (lengthAfter > ropeBase)
                {
                    // 最小長より短い状態で接続した場合は、ロープを伸ばさない。
                    lengthAfter = ropeBase;
                }
            }
            else
            {
                // 伸ばす（最大長まで。最大長より長い状態では伸ばさない）。
                lengthAfter = MathF.Max(ropeBase, MathF.Min(context.Tuning.MaxLength, ropeBase - reelSpeed * dt));
            }

            bool taut = distance >= ropeBase - TautTolerance;
            if (taut)
            {
                // ロープは伸びないので外向きの速度は消え、巻き取りの速さで内側へ引かれる（伸ばしているときは、伸ばす速さまで外へ動ける）。
                // ロープの力は接続点に向かう（中心力）ので角運動量は変わらず、半径が縮むと振りが自然に速くなる。
                // 接続直後の強い引きの間は、接続点の方向の速さをロープが決める
                // （引きが弱まるときに、勢いでロープがたるまないように）。最小長に達したら通常に戻す。
                float reelInSpeed = (ropeBase - lengthAfter) / dt;
                bool ropeControlsRadial = pullingAtStart && lengthAfter > context.Tuning.MinRopeLength;
                bool keepFasterInward = !ropeControlsRadial && radialSpeed < -reelInSpeed;
                float newRadialSpeed = keepFasterInward ? radialSpeed : -reelInSpeed;
                velocity = tangential + radial * newRadialSpeed;
            }

            return new WireMotionResult(velocity, lengthAfter);
        }

        /// <summary>
        /// 接続直後の速さの下限（m/s。使わないときは 0）。接続した瞬間の速さが StartPullSpeed より遅いときだけ使う。
        /// 下限は StartPullTicks の間 StartPullSpeed で、その後 StartPullFadeTicks で ReelSpeed まで直線的に下がる
        /// （通常の巻き取りへ切れ目なく戻る）。
        /// </summary>
        private static float StartSpeedFloor(in WireMotionContext context, InertialWireTuning tuning)
        {
            int ticks = context.AttachedTicks;
            if (tuning.StartPullSpeed <= 0f || context.AttachSpeed >= tuning.StartPullSpeed
                || ticks >= tuning.StartPullTicks + tuning.StartPullFadeTicks)
            {
                return 0f;
            }
            float floor = tuning.StartPullSpeed;
            if (ticks >= tuning.StartPullTicks)
            {
                float fade = (float)(ticks - tuning.StartPullTicks) / tuning.StartPullFadeTicks;
                floor += (tuning.ReelSpeed - tuning.StartPullSpeed) * fade;
            }
            return floor;
        }

        /// <summary>速さが下限 floor に届くのに必要な、接続点の方向の速さ（不要なら 0）。</summary>
        private static float PullInSpeed(float floor, float tangentialSpeed)
        {
            if (tangentialSpeed >= floor)
            {
                return 0f;
            }
            return MathF.Sqrt(floor * floor - tangentialSpeed * tangentialSpeed);
        }

        /// <summary>
        /// 振りの方向の速度 tangential を、速さが floor に届くまで上げる。向きは今の振りの向き。止まっていれば、
        /// 左右の入力の向き（なければ上げない）。
        /// </summary>
        private static Vector3 RaiseToFloor(Vector3 tangential, Vector3 tangent, float stickX, float floor)
        {
            float speed = tangential.Length();
            if (speed >= floor)
            {
                return tangential;
            }
            if (speed > SimMath.Epsilon)
            {
                return tangential * (floor / speed);
            }
            float side = stickX * tangent.X;
            if (MathF.Abs(side) <= SimMath.Epsilon)
            {
                return tangential;
            }
            return tangent * (side > 0f ? floor : -floor);
        }

        private static float MathMax(float a, float b)
        {
            return a > b ? a : b;
        }
    }
}
