using System.Numerics;
using Degunatsu.Core.Common;
using Degunatsu.Core.Physics;

namespace Degunatsu.Core.Movement
{
    /// <summary>
    /// Rigidbody を使わない移動と衝突解決。箱を移動方向にスイープし、当たった面に沿って滑らせる。
    /// スイープ（連続判定）なので、高速でも細い壁をすり抜けない。
    /// </summary>
    public sealed class KinematicMotor
    {
        // 地形との間に残す隙間。接触したままのスイープが毎回 0 距離で当たるのを防ぐ（技術的な定数で、ゲームの調整値ではない）。
        private const float SkinWidth = 0.01f;

        // 接地判定のために下方向へ調べる距離。
        private const float GroundProbeDistance = 0.05f;

        // 1回の移動で滑らせる最大の回数（角に当たったときなど）。
        private const int MaxSlideIterations = 4;

        // この値以上上を向いた面を「地面」とみなす。
        private const float GroundNormalMinY = 0.7f;

        private readonly ICollisionQuery _collision;

        public KinematicMotor(ICollisionQuery collision)
        {
            _collision = collision;
        }

        /// <summary>
        /// 地形に重なっていたら押し出す。押し出した場合 true。
        /// </summary>
        public bool Depenetrate(ref Vector3 position, Vector3 halfExtents)
        {
            if (!_collision.ComputePenetration(position, halfExtents, out Vector3 correction))
            {
                return false;
            }
            position += correction;
            return true;
        }

        /// <summary>
        /// position を delta だけ動かす。地形に当たったら面に沿って滑らせ、velocity から面に向かう成分を取り除く。
        /// </summary>
        public void Move(ref Vector3 position, ref Vector3 velocity, Vector3 halfExtents, Vector3 delta)
        {
            Vector3 remaining = delta;
            for (int i = 0; i < MaxSlideIterations; i++)
            {
                float remainingLength = remaining.Length();
                if (remainingLength < SimMath.Epsilon)
                {
                    break;
                }

                if (!_collision.SweepBox(position, halfExtents, remaining, out CastHit hit))
                {
                    position += remaining;
                    break;
                }

                Vector3 direction = remaining / remainingLength;
                float travel = hit.Distance - SkinWidth;
                if (travel > 0f)
                {
                    position += direction * travel;
                    remaining -= direction * travel;
                }

                remaining = RemoveIntoSurface(remaining, hit.Normal);
                velocity = RemoveIntoSurface(velocity, hit.Normal);
            }
        }

        /// <summary>
        /// 足元に地面があるか。
        /// </summary>
        public bool ProbeGround(Vector3 position, Vector3 halfExtents)
        {
            var down = new Vector3(0f, -(GroundProbeDistance + SkinWidth), 0f);
            return _collision.SweepBox(position, halfExtents, down, out CastHit hit) && hit.Normal.Y >= GroundNormalMinY;
        }

        private static Vector3 RemoveIntoSurface(Vector3 vector, Vector3 normal)
        {
            float into = Vector3.Dot(vector, normal);
            return into < 0f ? vector - normal * into : vector;
        }
    }
}
