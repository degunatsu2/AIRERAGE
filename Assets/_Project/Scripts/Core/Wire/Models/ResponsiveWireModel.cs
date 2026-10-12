using System.Numerics;
using Degunatsu.Core.Common;

namespace Degunatsu.Core.Wire.Models
{
    /// <summary>
    /// タイプB: 操作追従重視。接続点へ向かう目標速度に素早く収束する。スティックで進路を横にずらせる。
    /// 到着したらその場で静止してぶら下がる。
    /// </summary>
    public sealed class ResponsiveWireModel : IWireMotionModel
    {
        public WireMotionResult Step(in WireMotionContext context)
        {
            ResponsiveWireTuning tuning = context.Tuning.Responsive;
            float dt = context.DeltaTime;

            Vector3 toAnchor = context.Anchor - context.Position;
            toAnchor.Z = 0f;
            float distance = toAnchor.Length();
            float ropeLength = distance < context.RopeLength ? distance : context.RopeLength;

            if (distance <= tuning.ArrivalRadius || distance < SimMath.Epsilon)
            {
                Vector3 settled = SimMath.MoveTowards(context.Velocity, Vector3.Zero, tuning.PullAcceleration * dt);
                return new WireMotionResult(settled, ropeLength);
            }

            Vector3 pullDirection = toAnchor / distance;
            Vector3 lateral = SimMath.PerpendicularXY(pullDirection);
            float steer = Vector3.Dot(new Vector3(context.Stick.X, context.Stick.Y, 0f), lateral);
            Vector3 targetVelocity = pullDirection * tuning.PullSpeed + lateral * (steer * tuning.SteerSpeed);

            Vector3 velocity = context.Velocity;
            velocity.Y -= context.Gravity * tuning.GravityScale * dt;
            velocity = SimMath.MoveTowards(velocity, targetVelocity, tuning.PullAcceleration * dt);

            return new WireMotionResult(velocity, ropeLength);
        }
    }
}
