using System.Numerics;
using Degunatsu.Core.Common;
using Degunatsu.Core.Physics;

namespace Degunatsu.Core.Wire
{
    /// <summary>
    /// ワイヤーの照準（D-08）とエイムアシスト。
    /// </summary>
    public static class WireTargeting
    {
        // 照準線と接続点の間に地形があるかを調べるとき、接続点の手前に残す余裕（m）。
        private const float LineOfSightTolerance = 0.05f;

        /// <summary>
        /// 照準方向。スティックの入力があればその方向、なければ「向いている方向から NeutralAimAngleDeg 上向き」。
        /// </summary>
        public static Vector3 AimDirection(Vector2 stick, int facing, WireTuning tuning)
        {
            if (stick.Length() >= tuning.AimDeadzone)
            {
                return Vector3.Normalize(new Vector3(stick.X, stick.Y, 0f));
            }
            Vector3 neutral = SimMath.DirectionFromAngleDeg(tuning.NeutralAimAngleDeg);
            return new Vector3(neutral.X * (facing < 0 ? -1f : 1f), neutral.Y, 0f);
        }

        /// <summary>
        /// エイムアシストの対象を探す。照準方向から AimAssistAngleDeg 以内・射程内・遮るものがないアンカーポイントのうち、
        /// 照準方向との角度が最も小さいもの（同じなら近いもの）を選ぶ。
        /// </summary>
        public static bool TryFindAssistTarget(
            Vector3 origin, Vector3 aimDirection, WireTuning tuning, ICollisionQuery collision, out Vector3 target)
        {
            target = default;
            bool found = false;
            float bestAngle = float.MaxValue;
            float bestDistance = float.MaxValue;

            var anchors = collision.AnchorPoints;
            for (int i = 0; i < anchors.Count; i++)
            {
                Vector3 toAnchor = anchors[i] - origin;
                toAnchor.Z = 0f;
                float distance = toAnchor.Length();
                if (distance < SimMath.Epsilon || distance > tuning.MaxLength)
                {
                    continue;
                }

                float angle = SimMath.AngleBetweenDeg(aimDirection, toAnchor / distance);
                if (angle > tuning.AimAssistAngleDeg)
                {
                    continue;
                }

                bool better = angle < bestAngle - SimMath.Epsilon
                    || (angle <= bestAngle + SimMath.Epsilon && distance < bestDistance);
                if (!better || !HasLineOfSight(origin, anchors[i], collision))
                {
                    continue;
                }

                found = true;
                bestAngle = angle;
                bestDistance = distance;
                target = anchors[i];
            }
            return found;
        }

        /// <summary>
        /// from から to までの間に地形がないか。to が地形の表面にある場合も、その面自体は遮蔽とみなさない。
        /// </summary>
        public static bool HasLineOfSight(Vector3 from, Vector3 to, ICollisionQuery collision)
        {
            Vector3 delta = to - from;
            delta.Z = 0f;
            float distance = delta.Length();
            float checkDistance = distance - LineOfSightTolerance;
            if (checkDistance <= 0f)
            {
                return true;
            }
            return !collision.Raycast(from, delta / distance, checkDistance, out _);
        }
    }
}
