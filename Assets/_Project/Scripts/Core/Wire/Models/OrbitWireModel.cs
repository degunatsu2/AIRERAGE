using System.Numerics;
using Degunatsu.Core.Common;

namespace Degunatsu.Core.Wire.Models
{
    /// <summary>
    /// タイプA の試行版（D-11）: 接続点を中心に、ロープ長を半径とする円を描いて回る。
    /// 回る向きは接続した瞬間に向いていた方向で決まる（WireState.SwingDirection）。重力は受けず、一定の速さで回る。
    /// スティックを回る方向に入れると少し速く、逆に入れると少し遅くなる（WireTuning.AttachedInputInfluence）。
    /// 地形に押されて円の内側に入ったら、ロープ長をその距離まで縮める（半径が小さくなる）。
    /// </summary>
    public sealed class OrbitWireModel : IWireMotionModel
    {
        public WireMotionResult Step(in WireMotionContext context)
        {
            OrbitWireTuning tuning = context.Tuning.Orbit;

            Vector3 fromAnchor = context.Position - context.Anchor;
            fromAnchor.Z = 0f;
            float distance = fromAnchor.Length();
            float ropeLength = distance < context.RopeLength ? distance : context.RopeLength;
            if (distance < SimMath.Epsilon)
            {
                return new WireMotionResult(Vector3.Zero, ropeLength);
            }

            // 反時計回りの接線を、回る向きに合わせる。
            Vector3 tangent = SimMath.PerpendicularXY(fromAnchor / distance);
            if (context.SwingDirection < 0)
            {
                tangent = -tangent;
            }

            // 半径方向の速度は持たない（円の上を回る）。接線方向の速さを目標の速さに近づける。
            float speed = Vector3.Dot(context.Velocity, tangent);
            float targetSpeed = tuning.OrbitSpeed * context.InputSpeedScale(tangent);
            speed = SimMath.MoveTowards(speed, targetSpeed, tuning.OrbitAcceleration * context.DeltaTime);

            return new WireMotionResult(tangent * speed, ropeLength);
        }
    }
}
