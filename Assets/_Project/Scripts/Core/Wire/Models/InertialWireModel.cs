using System;
using System.Numerics;
using Degunatsu.Core.Common;

namespace Degunatsu.Core.Wire.Models
{
    /// <summary>
    /// タイプA: 物理・慣性重視。ロープ長の拘束と重力による振り子運動。
    /// 巻き取るとロープが縮み、角運動量の保存で振りが速くなる（上限は共通の MaxAttachedSpeed）。
    /// 遅い状態で接続した直後は、速さが下限（StartPullSpeed）に届くように接続点の方向へ強く引く。
    /// 上下の入力で巻き取りを変える（D-41）: 下を押し続けるとロープの長さを保ち（接続直後の引きもしない）、上なら素早く縮める。
    /// </summary>
    public sealed class InertialWireModel : IWireMotionModel
    {
        // ロープが張っているとみなす余裕（m）。
        private const float TautTolerance = 1e-3f;

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

            float stickAlongTangent = Vector3.Dot(new Vector3(context.Stick.X, context.Stick.Y, 0f), tangent);
            velocity += tangent * (stickAlongTangent * tuning.SwingAcceleration * dt);

            if (tuning.Damping > 0f)
            {
                velocity *= MathMax(0f, 1f - tuning.Damping * dt);
            }

            // 接続直後で遅いときは、速さが下限に届くように巻き取りを速くする。
            float radialSpeed = Vector3.Dot(velocity, radial);
            Vector3 tangential = velocity - radial * radialSpeed;
            bool holdLength = context.Stick.Y <= -tuning.ReelInputThreshold;
            float reelSpeed = holdLength ? 0f
                : context.Stick.Y >= tuning.ReelInputThreshold ? tuning.FastReelSpeed : tuning.ReelSpeed;
            float startPull = holdLength ? 0f : StartPullInSpeed(context, tuning, tangential.Length());
            bool pullingAtStart = startPull > reelSpeed;
            if (pullingAtStart)
            {
                reelSpeed = startPull;
            }

            float lengthBefore = context.RopeLength;
            float lengthAfter = MathMax(context.Tuning.MinRopeLength, lengthBefore - reelSpeed * dt);
            if (lengthAfter > lengthBefore)
            {
                // 最小長より短い状態で接続した場合は、ロープを伸ばさない。
                lengthAfter = lengthBefore;
            }

            bool taut = distance >= lengthBefore - TautTolerance;
            if (taut)
            {
                // ロープは伸びないので外向きの速度は消え、巻き取りの速さで内側へ引かれる。
                // ロープの力は接続点に向かう（中心力）ので角運動量は変わらず、半径が縮むと振りが自然に速くなる。
                // 接続直後の強い引きの間は、接続点の方向の速さをロープが決める
                // （引きが弱まるときに、勢いでロープがたるまないように）。最小長に達したら通常に戻す。
                float reelInSpeed = (lengthBefore - lengthAfter) / dt;
                bool ropeControlsRadial = pullingAtStart && lengthAfter > context.Tuning.MinRopeLength;
                bool keepFasterInward = !ropeControlsRadial && radialSpeed < -reelInSpeed;
                float newRadialSpeed = keepFasterInward ? radialSpeed : -reelInSpeed;
                velocity = tangential + radial * newRadialSpeed;
            }

            return new WireMotionResult(velocity, lengthAfter);
        }

        /// <summary>
        /// 接続直後の速さの下限に届くのに必要な、接続点の方向の速さ（不要なら 0）。
        /// 接続した瞬間の速さが StartPullSpeed より遅いときだけ使う。下限は StartPullTicks の間 StartPullSpeed で、
        /// その後 StartPullFadeTicks で ReelSpeed まで直線的に下がる（通常の巻き取りへ切れ目なく戻る）。
        /// </summary>
        private static float StartPullInSpeed(in WireMotionContext context, InertialWireTuning tuning, float tangentialSpeed)
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
            if (tangentialSpeed >= floor)
            {
                return 0f;
            }
            return MathF.Sqrt(floor * floor - tangentialSpeed * tangentialSpeed);
        }

        private static float MathMax(float a, float b)
        {
            return a > b ? a : b;
        }
    }
}
