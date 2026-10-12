using System;
using System.Collections.Generic;
using System.Numerics;

namespace Degunatsu.Core.Physics
{
    /// <summary>
    /// 軸平行な矩形（XY 平面上。Z 方向には無限に続く）とアンカーポイントの集まりでできた地形。
    /// スイープは Minkowski 和を使った厳密な計算で、結果が決定的になる。
    /// Unity のシーンでは、軸平行な BoxCollider から作る（Degunatsu.Runtime の SceneTerrainBuilder）。
    /// Unity の物理エンジン（PhysX）のクエリは、形状を約2mm膨らませて扱うため使わない（docs/03_architecture.md 4.2）。
    /// </summary>
    public sealed class BoxTerrain : ICollisionQuery
    {
        public readonly struct Box
        {
            public readonly Vector2 Min;
            public readonly Vector2 Max;
            public readonly bool Attachable;

            public Box(Vector2 min, Vector2 max, bool attachable)
            {
                Min = min;
                Max = max;
                Attachable = attachable;
            }
        }

        private readonly List<Box> _boxes = new List<Box>();
        private readonly List<Vector3> _anchors = new List<Vector3>();

        public IReadOnlyList<Box> Boxes => _boxes;
        public IReadOnlyList<Vector3> AnchorPoints => _anchors;

        /// <summary>矩形を追加する。最小値が最大値より小さくない（NaN を含む）場合は ArgumentException。</summary>
        public BoxTerrain AddBox(float minX, float minY, float maxX, float maxY, bool attachable = false)
        {
            if (!(minX < maxX) || !(minY < maxY))
            {
                throw new ArgumentException(
                    $"矩形の最小値は最大値より小さくしてください（min=({minX}, {minY}) max=({maxX}, {maxY})）");
            }
            _boxes.Add(new Box(new Vector2(minX, minY), new Vector2(maxX, maxY), attachable));
            return this;
        }

        public BoxTerrain AddAnchor(float x, float y)
        {
            _anchors.Add(new Vector3(x, y, 0f));
            return this;
        }

        /// <summary>箱が地形に重なっているか（tolerance より深い重なりのみ）。</summary>
        public bool IsOverlapping(Vector3 center, Vector3 halfExtents, float tolerance = 1e-4f)
        {
            foreach (Box box in _boxes)
            {
                if (OverlapDepth(box, center, halfExtents, out _, out _) > tolerance)
                {
                    return true;
                }
            }
            return false;
        }

        public bool SweepBox(Vector3 center, Vector3 halfExtents, Vector3 delta, out CastHit hit)
        {
            hit = default;
            bool found = false;
            float bestT = float.MaxValue;
            var origin = new Vector2(center.X, center.Y);
            var direction = new Vector2(delta.X, delta.Y);
            var half = new Vector2(halfExtents.X, halfExtents.Y);

            foreach (Box box in _boxes)
            {
                if (!SegmentVsBox(origin, direction, box.Min - half, box.Max + half, out float t, out Vector2 normal))
                {
                    continue;
                }
                if (t < bestT)
                {
                    bestT = t;
                    found = true;
                    hit = new CastHit
                    {
                        Distance = t * direction.Length(),
                        Point = new Vector3(origin + direction * t, 0f),
                        Normal = new Vector3(normal, 0f),
                        Attachable = box.Attachable,
                    };
                }
            }
            return found;
        }

        public bool ComputePenetration(Vector3 center, Vector3 halfExtents, out Vector3 correction)
        {
            correction = Vector3.Zero;
            Vector3 current = center;
            bool any = false;
            foreach (Box box in _boxes)
            {
                if (OverlapDepth(box, current, halfExtents, out Vector2 axis, out float depth) <= 0f)
                {
                    continue;
                }
                Vector3 push = new Vector3(axis * depth, 0f);
                current += push;
                correction += push;
                any = true;
            }
            return any;
        }

        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out CastHit hit)
        {
            return SweepBox(origin, Vector3.Zero, direction * maxDistance, out hit);
        }

        /// <summary>
        /// 線分 origin + direction * t（0 ≦ t ≦ 1）と矩形の最初の交差。開始時に内部にある場合は対象外。
        /// </summary>
        private static bool SegmentVsBox(Vector2 origin, Vector2 direction, Vector2 min, Vector2 max, out float t, out Vector2 normal)
        {
            t = 0f;
            normal = Vector2.Zero;
            float tEnter = float.NegativeInfinity;
            float tExit = float.PositiveInfinity;
            Vector2 enterNormal = Vector2.Zero;

            for (int axis = 0; axis < 2; axis++)
            {
                float o = axis == 0 ? origin.X : origin.Y;
                float d = axis == 0 ? direction.X : direction.Y;
                float lo = axis == 0 ? min.X : min.Y;
                float hi = axis == 0 ? max.X : max.Y;

                if (MathF.Abs(d) < 1e-9f)
                {
                    if (o <= lo || o >= hi)
                    {
                        return false;
                    }
                    continue;
                }

                float t1 = (lo - o) / d;
                float t2 = (hi - o) / d;
                float near = MathF.Min(t1, t2);
                float far = MathF.Max(t1, t2);
                if (near > tEnter)
                {
                    tEnter = near;
                    enterNormal = axis == 0 ? new Vector2(-MathF.Sign(d), 0f) : new Vector2(0f, -MathF.Sign(d));
                }
                if (far < tExit)
                {
                    tExit = far;
                }
            }

            if (tEnter > tExit || tExit <= 0f || tEnter > 1f || tEnter < 0f)
            {
                return false;
            }
            t = tEnter;
            normal = enterNormal;
            return true;
        }

        /// <summary>重なりの深さ（最も浅く抜けられる軸）。重なっていなければ 0 以下。</summary>
        private static float OverlapDepth(Box box, Vector3 center, Vector3 halfExtents, out Vector2 pushAxis, out float depth)
        {
            float overlapLeft = center.X + halfExtents.X - box.Min.X;
            float overlapRight = box.Max.X - (center.X - halfExtents.X);
            float overlapBottom = center.Y + halfExtents.Y - box.Min.Y;
            float overlapTop = box.Max.Y - (center.Y - halfExtents.Y);

            float minX = MathF.Min(overlapLeft, overlapRight);
            float minY = MathF.Min(overlapBottom, overlapTop);
            if (minX <= 0f || minY <= 0f)
            {
                pushAxis = Vector2.Zero;
                depth = 0f;
                return 0f;
            }

            if (minX < minY)
            {
                pushAxis = overlapLeft < overlapRight ? new Vector2(-1f, 0f) : new Vector2(1f, 0f);
                depth = minX;
            }
            else
            {
                pushAxis = overlapBottom < overlapTop ? new Vector2(0f, -1f) : new Vector2(0f, 1f);
                depth = minY;
            }
            return depth;
        }
    }
}
