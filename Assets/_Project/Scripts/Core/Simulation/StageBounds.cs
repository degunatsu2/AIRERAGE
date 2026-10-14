using System;
using System.Numerics;

namespace Airerage.Core.Simulation
{
    /// <summary>
    /// ステージの範囲（docs/11_combat_plan.md T3-3）。キャラクターの中心がこの長方形の外に出たら撃破になる（D-23）。
    /// 上下左右のどの辺から出ても同じ。
    /// </summary>
    public readonly struct StageBounds : IEquatable<StageBounds>
    {
        public readonly float MinX;
        public readonly float MinY;
        public readonly float MaxX;
        public readonly float MaxY;

        public StageBounds(float minX, float minY, float maxX, float maxY)
        {
            if (!(minX < maxX) || !(minY < maxY))
            {
                throw new ArgumentException($"ステージの範囲の最小が最大以上です（x {minX}〜{maxX}、y {minY}〜{maxY}）");
            }
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        /// <summary>position が範囲の内側にあるか（辺の上は外）。</summary>
        public bool Contains(Vector3 position)
        {
            return position.X > MinX && position.X < MaxX && position.Y > MinY && position.Y < MaxY;
        }

        public bool Equals(StageBounds other)
        {
            return MinX.Equals(other.MinX) && MinY.Equals(other.MinY) && MaxX.Equals(other.MaxX) && MaxY.Equals(other.MaxY);
        }

        public override bool Equals(object obj)
        {
            return obj is StageBounds other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(MinX, MinY, MaxX, MaxY);
        }

        public override string ToString()
        {
            return $"x {MinX}〜{MaxX}, y {MinY}〜{MaxY}";
        }
    }
}
