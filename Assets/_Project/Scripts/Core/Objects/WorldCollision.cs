using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;

namespace Airerage.Core.Objects
{
    /// <summary>
    /// 動かない地形（ICollisionQuery）に、ワールドに置かれた物のアンカーポイント（ミラーボール。docs/14）を加えた当たりの調べ方。
    /// 地形の調べ方はそのまま渡し、アンカーポイントだけ「地形のアンカー → 置かれた物（番号の順）」の順に並べる。
    /// SimWorld が各ティックの初めに Refresh で物の一覧から作り直す（状態から決まるので、巻き戻しても全員で同じ）。
    /// ワイヤーのフック（RaycastHook）だけは、置かれた物も調べる: わたがしの足場（PASTRY CHEF の Q）に当たると弾かれ、地形に張り付いた
    /// クリーム（E）の上では、オレンジならつながり、抹茶ならつながらない（ロープの遮りと巻き付きは、地形だけを調べる）。
    /// ステージの動く足場と扉（StagePlatform。ユーザーの要望、2026-10-26）は、ティックごとに SetPlatforms でその位置の箱を作り、地形と
    /// 同じように調べる（当たった物は CastHit.Body。足場の番号 + 1）。ロープの巻き付きの角は、動かない地形だけ。
    /// </summary>
    public sealed class WorldCollision : ICollisionQuery
    {
        private readonly ICollisionQuery _terrain;
        private readonly List<Vector3> _anchors = new List<Vector3>();
        private readonly List<float> _anchorRadii = new List<float>();
        private readonly List<WorldObject> _hookObjects = new List<WorldObject>();
        private readonly BoxTerrain _platforms = new BoxTerrain();
        private bool _hasPlatforms;

        // クリームの上の当たった点を、クリームの箱の中とみなす余裕（m）。
        private const float PatchTolerance = 0.05f;

        public WorldCollision(ICollisionQuery terrain)
        {
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            Refresh(null);
        }

        /// <summary>動かない地形。</summary>
        public ICollisionQuery Terrain => _terrain;

        public IReadOnlyList<Vector3> AnchorPoints => _anchors;

        public IReadOnlyList<float> AnchorRadii => _anchorRadii;

        public IReadOnlyList<RopeCorner> RopeCorners => _terrain.RopeCorners;

        /// <summary>アンカーポイントを、地形のものと、objects のうちアンカーになる物から作り直す。</summary>
        public void Refresh(WorldObjects objects)
        {
            _anchors.Clear();
            _anchors.AddRange(_terrain.AnchorPoints);
            _anchorRadii.Clear();
            _anchorRadii.AddRange(_terrain.AnchorRadii);
            _hookObjects.Clear();
            if (objects == null)
            {
                return;
            }
            foreach (WorldObject item in objects.Items)
            {
                if (IsAnchor(item))
                {
                    _anchors.Add(new Vector3(item.Position.X, item.Position.Y, 0f));
                    _anchorRadii.Add(0f);
                }
                if (!item.Ended && (item.Kind == WorldObjectKind.CottonCandy || item.Kind == WorldObjectKind.CreamPatch))
                {
                    _hookObjects.Add(item);
                }
            }
        }

        /// <summary>動く足場と扉の当たりを、ティック tick の位置で作り直す（ない足場は入れない）。</summary>
        public void SetPlatforms(IReadOnlyList<StagePlatform> platforms, StageState stage, int tick)
        {
            _platforms.Clear();
            _hasPlatforms = false;
            if (platforms == null)
            {
                return;
            }
            for (int i = 0; i < platforms.Count; i++)
            {
                if (!StagePlatforms.TryGetCenter(platforms, stage, i, tick, false, out Vector2 center))
                {
                    continue;
                }
                platforms[i].BoundsAt(center, out Vector2 min, out Vector2 max);
                _platforms.AddBox(min.X, min.Y, max.X, max.Y, platforms[i].Attachable, i + 1);
                _hasPlatforms = true;
            }
        }

        /// <summary>
        /// ワイヤーのフックの当たり: 地形と、わたがしの足場（つながらない）。地形の当たった点にクリームが張り付いていれば、その味で
        /// つながるかを決める（後から張り付いたものが優先）。
        /// </summary>
        public bool RaycastHook(Vector3 origin, Vector3 direction, float maxDistance, out CastHit hit)
        {
            bool found = Raycast(origin, direction, maxDistance, out hit);
            if (found)
            {
                foreach (WorldObject item in _hookObjects)
                {
                    if (item.Kind == WorldObjectKind.CreamPatch
                        && MathF.Abs(hit.Point.X - item.Position.X) <= item.End.X + PatchTolerance
                        && MathF.Abs(hit.Point.Y - item.Position.Y) <= item.End.Y + PatchTolerance)
                    {
                        hit.Attachable = item.Variant == (byte)CreamFlavor.Orange;
                    }
                }
            }
            foreach (WorldObject item in _hookObjects)
            {
                if (item.Kind == WorldObjectKind.CottonCandy
                    && RaycastCandy(item, origin, direction, found ? hit.Distance : maxDistance, out CastHit candy))
                {
                    hit = candy;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// わたがしの足場 candy（向き Velocity の横長の長方形）に、origin から direction へ maxDistance までの線が外から当たるか。
        /// </summary>
        public static bool RaycastCandy(in WorldObject candy, Vector3 origin, Vector3 direction, float maxDistance, out CastHit hit)
        {
            hit = default;
            Vector2 normal = candy.Velocity;
            var tangent = new Vector2(normal.Y, -normal.X);
            var rel = new Vector2(origin.X - candy.Position.X, origin.Y - candy.Position.Y);
            var dir = new Vector2(direction.X, direction.Y);
            float enter = 0f;
            float exit = maxDistance;
            int axis = -1;
            float side = 0f;
            if (!Slab(Vector2.Dot(rel, tangent), Vector2.Dot(dir, tangent), candy.End.X, 0, ref enter, ref exit, ref axis, ref side)
                || !Slab(Vector2.Dot(rel, normal), Vector2.Dot(dir, normal), candy.End.Y, 1, ref enter, ref exit, ref axis, ref side))
            {
                return false;
            }
            if (axis < 0)
            {
                // 線の始まりが足場の中にある。
                return false;
            }
            Vector2 n = axis == 0 ? tangent * side : normal * side;
            hit.Distance = enter;
            hit.Point = origin + direction * enter;
            hit.Normal = new Vector3(n.X, n.Y, 0f);
            hit.Attachable = false;
            return true;
        }

        // 線と、軸 index の幅 ±half の帯（o は始まりの位置、d は向きの成分）。入る距離が延びたら、その軸と面の向きを覚える。
        private static bool Slab(float o, float d, float half, int index, ref float enter, ref float exit, ref int axis, ref float side)
        {
            if (MathF.Abs(d) < 1e-6f)
            {
                return MathF.Abs(o) <= half;
            }
            float t1 = (-half - o) / d;
            float t2 = (half - o) / d;
            float near = MathF.Min(t1, t2);
            if (near > enter)
            {
                enter = near;
                axis = index;
                side = d > 0f ? -1f : 1f;
            }
            exit = MathF.Min(exit, MathF.Max(t1, t2));
            return enter <= exit;
        }

        /// <summary>ワイヤーのアンカーポイントになる物か（今はミラーボールだけ）。</summary>
        public static bool IsAnchor(in WorldObject item)
        {
            return item.Kind == WorldObjectKind.MirrorBall && !item.Ended;
        }

        public bool SweepBox(Vector3 center, Vector3 halfExtents, Vector3 delta, out CastHit hit)
        {
            bool found = _terrain.SweepBox(center, halfExtents, delta, out hit);
            if (_hasPlatforms && _platforms.SweepBox(center, halfExtents, delta, out CastHit platform) && (!found || platform.Distance < hit.Distance))
            {
                hit = platform;
                found = true;
            }
            return found;
        }

        public bool ComputePenetration(Vector3 center, Vector3 halfExtents, out Vector3 correction)
        {
            bool any = _terrain.ComputePenetration(center, halfExtents, out correction);
            if (_hasPlatforms && _platforms.ComputePenetration(center + correction, halfExtents, out Vector3 more))
            {
                correction += more;
                any = true;
            }
            return any;
        }

        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out CastHit hit)
        {
            bool found = _terrain.Raycast(origin, direction, maxDistance, out hit);
            if (_hasPlatforms && _platforms.Raycast(origin, direction, maxDistance, out CastHit platform) && (!found || platform.Distance < hit.Distance))
            {
                hit = platform;
                found = true;
            }
            return found;
        }
    }
}
