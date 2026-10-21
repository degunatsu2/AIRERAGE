using System.Numerics;
using Airerage.Core.Common;
using Airerage.Core.Objects;
using Airerage.Core.Physics;

namespace Airerage.Core.Wire
{
    /// <summary>射出の方向と、エイムアシストの対象。</summary>
    public readonly struct WireAim
    {
        public readonly Vector3 Direction;
        public readonly bool HasTargetPoint;
        public readonly Vector3 TargetPoint;

        public WireAim(Vector3 direction, bool hasTargetPoint, Vector3 targetPoint)
        {
            Direction = direction;
            HasTargetPoint = hasTargetPoint;
            TargetPoint = targetPoint;
        }
    }

    /// <summary>射出したときの結果の予測。</summary>
    public enum WireShotResult : byte
    {
        /// <summary>接続できる。</summary>
        Attach,

        /// <summary>接続できない地形に当たって外れる。</summary>
        HitNonAttachable,

        /// <summary>射程内に何もなく外れる。</summary>
        OutOfRange,
    }

    /// <summary>今の位置から射出したときの予測（照準のインジケーター用）。</summary>
    public readonly struct WireShotPreview
    {
        public readonly WireAim Aim;
        public readonly WireShotResult Result;

        /// <summary>接続点、当たった点、または射程の端。</summary>
        public readonly Vector3 EndPoint;

        public WireShotPreview(WireAim aim, WireShotResult result, Vector3 endPoint)
        {
            Aim = aim;
            Result = result;
            EndPoint = endPoint;
        }
    }

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
        /// エイムアシストの対象を探す。照準方向から AimAssistAngleDeg 以内か、照準の線がアンカーから AimAssistRadius 以内を通る、
        /// 射程内・遮るものがないアンカーポイントのうち、
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
                float along = Vector3.Dot(toAnchor, aimDirection);
                bool nearTheLine = along > 0f && (toAnchor - aimDirection * along).Length() <= tuning.AimAssistRadius;
                if (angle > tuning.AimAssistAngleDeg && !nearTheLine)
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
        /// 射出の方向を決める（射出のときと同じ計算）。照準の入力から方向を求め、エイムアシストの対象があればそこへ向ける。
        /// </summary>
        public static WireAim ResolveAim(Vector3 origin, Vector2 aimInput, int facing, WireTuning tuning, ICollisionQuery collision)
        {
            Vector3 direction = AimDirection(aimInput, facing, tuning);
            if (!TryFindAssistTarget(origin, direction, tuning, collision, out Vector3 target))
            {
                return new WireAim(direction, false, default);
            }
            Vector3 toTarget = target - origin;
            toTarget.Z = 0f;
            return new WireAim(SimMath.NormalizeOr(toTarget, direction), true, target);
        }

        /// <summary>
        /// 今の位置から射出したときの結果を予測する（表示用）。フックが飛ぶ間のキャラクターの移動は考えない。
        /// フックの判定（WireController）と同じく、照準方向へ射程まで（対象があれば対象まで）地形を調べる。
        /// </summary>
        public static WireShotPreview PredictShot(Vector3 origin, Vector2 aimInput, int facing, WireTuning tuning, ICollisionQuery collision)
        {
            WireAim aim = ResolveAim(origin, aimInput, facing, tuning, collision);
            float reach = tuning.MaxLength;
            if (aim.HasTargetPoint)
            {
                Vector3 toTarget = aim.TargetPoint - origin;
                toTarget.Z = 0f;
                reach = toTarget.Length();
            }

            if (reach > 0f && CastHook(collision, origin, aim.Direction, reach, out CastHit hit))
            {
                return new WireShotPreview(aim, hit.Attachable || tuning.AttachAnySurface ? WireShotResult.Attach : WireShotResult.HitNonAttachable,
                    hit.Point);
            }
            if (aim.HasTargetPoint)
            {
                return new WireShotPreview(aim, WireShotResult.Attach, aim.TargetPoint);
            }
            // 空中につながる強化の間は、最大射程の点につながる（キャッスルインジエアー）。
            return new WireShotPreview(aim, tuning.AttachInAir ? WireShotResult.Attach : WireShotResult.OutOfRange,
                origin + aim.Direction * tuning.MaxLength);
        }

        /// <summary>
        /// ワイヤーのフックの当たり（置かれた物を含む地形なら WorldCollision.RaycastHook。わたがしの足場とクリーム）。
        /// </summary>
        public static bool CastHook(ICollisionQuery collision, Vector3 origin, Vector3 direction, float maxDistance, out CastHit hit)
        {
            return collision is WorldCollision world
                ? world.RaycastHook(origin, direction, maxDistance, out hit)
                : collision.Raycast(origin, direction, maxDistance, out hit);
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
