using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Physics;

namespace Airerage.Core.Objects
{
    /// <summary>
    /// 動かない地形（ICollisionQuery）に、ワールドに置かれた物のアンカーポイント（ミラーボール。docs/14）を加えた当たりの調べ方。
    /// 地形の調べ方はそのまま渡し、アンカーポイントだけ「地形のアンカー → 置かれた物（番号の順）」の順に並べる。
    /// SimWorld が各ティックの初めに Refresh で物の一覧から作り直す（状態から決まるので、巻き戻しても全員で同じ）。
    /// </summary>
    public sealed class WorldCollision : ICollisionQuery
    {
        private readonly ICollisionQuery _terrain;
        private readonly List<Vector3> _anchors = new List<Vector3>();

        public WorldCollision(ICollisionQuery terrain)
        {
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            Refresh(null);
        }

        /// <summary>動かない地形。</summary>
        public ICollisionQuery Terrain => _terrain;

        public IReadOnlyList<Vector3> AnchorPoints => _anchors;

        public IReadOnlyList<RopeCorner> RopeCorners => _terrain.RopeCorners;

        /// <summary>アンカーポイントを、地形のものと、objects のうちアンカーになる物から作り直す。</summary>
        public void Refresh(WorldObjects objects)
        {
            _anchors.Clear();
            _anchors.AddRange(_terrain.AnchorPoints);
            if (objects == null)
            {
                return;
            }
            foreach (WorldObject item in objects.Items)
            {
                if (IsAnchor(item))
                {
                    _anchors.Add(new Vector3(item.Position.X, item.Position.Y, 0f));
                }
            }
        }

        /// <summary>ワイヤーのアンカーポイントになる物か（今はミラーボールだけ）。</summary>
        public static bool IsAnchor(in WorldObject item)
        {
            return item.Kind == WorldObjectKind.MirrorBall && !item.Ended;
        }

        public bool SweepBox(Vector3 center, Vector3 halfExtents, Vector3 delta, out CastHit hit)
        {
            return _terrain.SweepBox(center, halfExtents, delta, out hit);
        }

        public bool ComputePenetration(Vector3 center, Vector3 halfExtents, out Vector3 correction)
        {
            return _terrain.ComputePenetration(center, halfExtents, out correction);
        }

        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out CastHit hit)
        {
            return _terrain.Raycast(origin, direction, maxDistance, out hit);
        }
    }
}
