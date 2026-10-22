using System;
using System.Numerics;

namespace Airerage.Core.Combat
{
    /// <summary>
    /// 判定の箱の当たり（傾いた箱 AttackHit.AngleDeg と、軸平行のキャラクターの箱。分離軸で調べる）。表示（判定の線）にも使う。
    /// </summary>
    public static class HitShapes
    {
        /// <summary>中心 center・半分の大きさ half・傾き angle（ラジアン）の箱と、中心 target・半分の大きさ targetHalf の軸平行の箱が重なるか。</summary>
        public static bool Overlaps(Vector2 center, Vector2 half, float angle, Vector2 target, Vector2 targetHalf)
        {
            Vector2 d = target - center;
            if (angle == 0f)
            {
                return MathF.Abs(d.X) < half.X + targetHalf.X && MathF.Abs(d.Y) < half.Y + targetHalf.Y;
            }
            float c = MathF.Cos(angle);
            float s = MathF.Sin(angle);
            float ac = MathF.Abs(c);
            float asin = MathF.Abs(s);
            // 世界の X・Y の軸。
            if (MathF.Abs(d.X) >= ac * half.X + asin * half.Y + targetHalf.X || MathF.Abs(d.Y) >= asin * half.X + ac * half.Y + targetHalf.Y)
            {
                return false;
            }
            // 傾いた箱の軸。
            float du = d.X * c + d.Y * s;
            float dv = -d.X * s + d.Y * c;
            return MathF.Abs(du) < half.X + ac * targetHalf.X + asin * targetHalf.Y
                && MathF.Abs(dv) < half.Y + asin * targetHalf.X + ac * targetHalf.Y;
        }

        /// <summary>点 point が、傾いた箱（radius だけ広げた）の中にあるか。</summary>
        public static bool ContainsPoint(Vector2 center, Vector2 half, float angle, Vector2 point, float radius)
        {
            Vector2 d = point - center;
            float c = MathF.Cos(angle);
            float s = MathF.Sin(angle);
            float du = d.X * c + d.Y * s;
            float dv = -d.X * s + d.Y * c;
            return MathF.Abs(du) <= half.X + radius && MathF.Abs(dv) <= half.Y + radius;
        }

        /// <summary>傾いた箱を囲む軸平行の箱の半分の大きさ。</summary>
        public static Vector2 Bounds(Vector2 half, float angle)
        {
            if (angle == 0f)
            {
                return half;
            }
            float ac = MathF.Abs(MathF.Cos(angle));
            float asin = MathF.Abs(MathF.Sin(angle));
            return new Vector2(ac * half.X + asin * half.Y, asin * half.X + ac * half.Y);
        }

        /// <summary>傾いた箱の 4 つの角（表示用。左回り）。</summary>
        public static Vector2 Corner(Vector2 center, Vector2 half, float angle, int index)
        {
            float x = index == 0 || index == 3 ? half.X : -half.X;
            float y = index < 2 ? half.Y : -half.Y;
            float c = MathF.Cos(angle);
            float s = MathF.Sin(angle);
            return center + new Vector2(x * c - y * s, x * s + y * c);
        }
    }
}
