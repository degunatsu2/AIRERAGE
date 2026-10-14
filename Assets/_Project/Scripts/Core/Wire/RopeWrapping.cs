using System;
using System.Numerics;
using Airerage.Core.Common;
using Airerage.Core.Physics;

namespace Airerage.Core.Wire
{
    /// <summary>
    /// ロープの巻き付き（D-33）。接続中に、今の支点からキャラクターまでのロープが地形の角に掛かったら、その角を新しい支点にする。
    /// 巻き付いたときと反対側へ戻ったら、支点を外す（ほどける）。両タイプ共通で、運動モデルは今の支点（WireState.ActivePivot）と
    /// 残りのロープの長さ（WireState.RopeLength）だけを使う。三角関数を使わず、内積と外積だけで決める（PC 間で結果をそろえるため）。
    /// </summary>
    public static class RopeWrapping
    {
        /// <summary>
        /// 移動の後に呼ぶ。from は移動の前の位置、to は移動の後の位置。
        /// 戻り値: 今の支点からキャラクターまでのロープが地形に遮られていないか（巻き付けられなかったときは false）。
        /// </summary>
        public static bool Update(ref WireState wire, Vector3 from, Vector3 to, ICollisionQuery collision)
        {
            Unwrap(ref wire, to, collision);

            Vector3 sweepStart = from;
            while (!WireTargeting.HasLineOfSight(wire.ActivePivot, to, collision))
            {
                Vector3 pivot = wire.ActivePivot;
                if (wire.Pivots.Count >= RopePivots.Capacity
                    || !TryFindCorner(pivot, sweepStart, to, wire.RopeLength, collision, out RopeCorner found, out int direction))
                {
                    return false;
                }
                Vector3 corner = found.Pivot;
                float segment = DistanceXY(pivot, corner);
                wire.Pivots.Push(corner, direction);
                wire.RopeLength -= segment;
                // 角から先のロープは、角に触れた瞬間には「前の支点 → 角」の延長線上にあった。そこから掃いた範囲で次の角を探す。
                sweepStart = pivot + (corner - pivot) / segment * DistanceXY(pivot, sweepStart);
            }
            return true;
        }

        // 巻き付いたときと反対側へ戻り、前の点からキャラクターが見えるなら、支点を外してロープを戻す。
        private static void Unwrap(ref WireState wire, Vector3 position, ICollisionQuery collision)
        {
            while (wire.Pivots.Count > 0)
            {
                int last = wire.Pivots.Count - 1;
                Vector3 pivot = wire.Pivots[last];
                Vector3 previous = last > 0 ? wire.Pivots[last - 1] : wire.Anchor;
                float side = CrossXY(pivot - previous, position - pivot);
                if (side * wire.Pivots.Direction(last) >= 0f || !WireTargeting.HasLineOfSight(previous, position, collision))
                {
                    return;
                }
                wire.RopeLength += DistanceXY(previous, pivot);
                wire.Pivots.Pop();
            }
        }

        /// <summary>
        /// 支点 pivot から伸びるロープの先が sweepStart から sweepEnd へ動いたときに、ロープが最初に掛かる地形の角を探す。
        /// 角（の頂点）は、掃いた三角形（pivot, sweepStart, sweepEnd）の内側で、ロープの長さより近いもの。
        /// ロープは頂点を越えたときに地形に遮られるので、判定には頂点を使い、支点には少しずらした点を使う。
        /// direction はロープが回った向き（1 = 反時計回り、-1 = 時計回り）。
        /// </summary>
        public static bool TryFindCorner(
            Vector3 pivot, Vector3 sweepStart, Vector3 sweepEnd, float ropeLength, ICollisionQuery collision,
            out RopeCorner corner, out int direction)
        {
            corner = default;
            Vector3 a = Flat(sweepStart - pivot);
            Vector3 b = Flat(sweepEnd - pivot);
            float sweep = CrossXY(a, b);
            float aLength = a.Length();
            direction = sweep >= 0f ? 1 : -1;
            if (MathF.Abs(sweep) < SimMath.Epsilon || aLength < SimMath.Epsilon)
            {
                return false;
            }
            Vector3 aUnit = a / aLength;
            Vector3 edge = Flat(sweepEnd - sweepStart);
            float pivotSide = CrossXY(edge, Flat(pivot - sweepStart));

            bool found = false;
            float bestCos = float.NegativeInfinity;
            foreach (RopeCorner candidate in collision.RopeCorners)
            {
                Vector3 vertex = candidate.Vertex;
                Vector3 v = Flat(vertex - pivot);
                float length = v.Length();
                if (length < 1e-3f || length >= ropeLength)
                {
                    continue;
                }
                // ロープが掃いたくさび形の中（回った向きに、始めの向きより先で、終わりの向きより手前）。
                if (CrossXY(a, v) * direction <= 0f || CrossXY(v, b) * direction < 0f)
                {
                    continue;
                }
                // ロープの先（キャラクター）より支点に近い側（三角形の内側）。
                if (CrossXY(edge, Flat(vertex - sweepStart)) * pivotSide <= 0f)
                {
                    continue;
                }
                // 回り始めの向きに最も近い角に、最初に掛かる。
                float cos = Vector3.Dot(aUnit, v / length);
                if (cos > bestCos)
                {
                    bestCos = cos;
                    corner = candidate;
                    found = true;
                }
            }
            return found;
        }

        private static float CrossXY(Vector3 a, Vector3 b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        private static float DistanceXY(Vector3 a, Vector3 b)
        {
            return Flat(b - a).Length();
        }

        private static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.X, v.Y, 0f);
        }
    }
}
