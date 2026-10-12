using System;
using System.Numerics;

namespace Degunatsu.Core.Wire
{
    /// <summary>
    /// ロープが地形の角に巻き付いてできた支点（D-33）。接続点に近い方から順に並ぶ。
    /// 状態を値だけで完結させる（コピー・比較・巻き戻しのため）ので、配列ではなく決まった数のフィールドで持つ。
    /// </summary>
    public struct RopePivots : IEquatable<RopePivots>
    {
        /// <summary>持てる支点の最大の数。</summary>
        public const int Capacity = 4;

        /// <summary>支点の数。</summary>
        public byte Count;

        public Vector3 P0;
        public Vector3 P1;
        public Vector3 P2;
        public Vector3 P3;

        /// <summary>支点ごとの巻き付いた向き。i ビット目が 1 なら時計回り、0 なら反時計回り。</summary>
        public byte Clockwise;

        public Vector3 this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return P0;
                    case 1: return P1;
                    case 2: return P2;
                    case 3: return P3;
                    default: throw new ArgumentOutOfRangeException(nameof(index), index, $"支点の番号は 0〜{Capacity - 1}");
                }
            }
        }

        /// <summary>支点 index で巻き付いた向き（1 = 反時計回り、-1 = 時計回り）。</summary>
        public int Direction(int index)
        {
            return ((Clockwise >> index) & 1) != 0 ? -1 : 1;
        }

        /// <summary>支点を加える。いっぱいなら InvalidOperationException。</summary>
        public void Push(Vector3 point, int direction)
        {
            if (Count >= Capacity)
            {
                throw new InvalidOperationException($"支点は {Capacity} 個まで");
            }
            switch (Count)
            {
                case 0: P0 = point; break;
                case 1: P1 = point; break;
                case 2: P2 = point; break;
                default: P3 = point; break;
            }
            byte bit = (byte)(1 << Count);
            Clockwise = direction < 0 ? (byte)(Clockwise | bit) : (byte)(Clockwise & ~bit);
            Count++;
        }

        /// <summary>最後の支点を外す。</summary>
        public void Pop()
        {
            if (Count == 0)
            {
                throw new InvalidOperationException("支点がありません");
            }
            Count--;
            switch (Count)
            {
                case 0: P0 = default; break;
                case 1: P1 = default; break;
                case 2: P2 = default; break;
                default: P3 = default; break;
            }
            Clockwise = (byte)(Clockwise & ~(1 << Count));
        }

        public bool Equals(RopePivots other)
        {
            return Count == other.Count && P0.Equals(other.P0) && P1.Equals(other.P1) && P2.Equals(other.P2)
                && P3.Equals(other.P3) && Clockwise == other.Clockwise;
        }

        public override bool Equals(object obj)
        {
            return obj is RopePivots other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Count, P0, P1, P2, P3, Clockwise);
        }
    }
}
