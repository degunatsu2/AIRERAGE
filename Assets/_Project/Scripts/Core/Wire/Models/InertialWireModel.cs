using System.Numerics;
using Degunatsu.Core.Common;

namespace Degunatsu.Core.Wire.Models
{
    /// <summary>
    /// タイプA: 物理・慣性重視。ロープ長の拘束と重力による振り子運動。
    /// 巻き取るとロープが縮み、角運動量の保存で振りが速くなる（上限は共通の MaxAttachedSpeed）。
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

            float lengthBefore = context.RopeLength;
            float lengthAfter = MathMax(context.Tuning.MinRopeLength, lengthBefore - tuning.ReelSpeed * dt);
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
                float radialSpeed = Vector3.Dot(velocity, radial);
                Vector3 tangential = velocity - radial * radialSpeed;
                float reelInSpeed = (lengthBefore - lengthAfter) / dt;
                float newRadialSpeed = radialSpeed < -reelInSpeed ? radialSpeed : -reelInSpeed;
                velocity = tangential + radial * newRadialSpeed;
            }

            return new WireMotionResult(velocity, lengthAfter);
        }

        private static float MathMax(float a, float b)
        {
            return a > b ? a : b;
        }
    }
}
