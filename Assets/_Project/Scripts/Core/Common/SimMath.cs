using System;
using System.Numerics;

namespace Degunatsu.Core.Common
{
    /// <summary>
    /// シミュレーションで使う数学の補助関数。ゲームプレイは XY 平面（Z = 0）で行う。
    /// </summary>
    public static class SimMath
    {
        public const float Epsilon = 1e-6f;

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            float diff = target - current;
            if (MathF.Abs(diff) <= maxDelta)
            {
                return target;
            }
            return current + MathF.Sign(diff) * maxDelta;
        }

        public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDelta)
        {
            Vector3 diff = target - current;
            float distance = diff.Length();
            if (distance <= maxDelta || distance < Epsilon)
            {
                return target;
            }
            return current + diff / distance * maxDelta;
        }

        public static Vector3 ClampMagnitude(Vector3 value, float maxLength)
        {
            float lengthSquared = value.LengthSquared();
            if (lengthSquared <= maxLength * maxLength)
            {
                return value;
            }
            return value / MathF.Sqrt(lengthSquared) * maxLength;
        }

        /// <summary>長さが 0 に近い場合は fallback を返す正規化。</summary>
        public static Vector3 NormalizeOr(Vector3 value, Vector3 fallback)
        {
            float length = value.Length();
            return length < Epsilon ? fallback : value / length;
        }

        /// <summary>XY 平面上で 90°反時計回りに回転したベクトル。</summary>
        public static Vector3 PerpendicularXY(Vector3 value)
        {
            return new Vector3(-value.Y, value.X, 0f);
        }

        /// <summary>2つの方向ベクトルのなす角（度）。どちらも正規化されている前提。</summary>
        public static float AngleBetweenDeg(Vector3 fromNormalized, Vector3 toNormalized)
        {
            float dot = Math.Clamp(Vector3.Dot(fromNormalized, toNormalized), -1f, 1f);
            return MathF.Acos(dot) * (180f / MathF.PI);
        }

        public static Vector3 DirectionFromAngleDeg(float angleDeg)
        {
            float radians = angleDeg * (MathF.PI / 180f);
            return new Vector3(MathF.Cos(radians), MathF.Sin(radians), 0f);
        }
    }
}
