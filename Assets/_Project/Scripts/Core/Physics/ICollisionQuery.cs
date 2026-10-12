using System.Collections.Generic;
using System.Numerics;

namespace Degunatsu.Core.Physics
{
    /// <summary>
    /// 地形への衝突クエリの結果。
    /// </summary>
    public struct CastHit
    {
        /// <summary>移動方向に沿った、当たるまでの距離。</summary>
        public float Distance;

        /// <summary>当たった地点（Raycast のみ意味を持つ）。</summary>
        public Vector3 Point;

        /// <summary>当たった面の法線（XY 平面上の単位ベクトル）。</summary>
        public Vector3 Normal;

        /// <summary>当たった地形にワイヤーを接続できるか。</summary>
        public bool Attachable;
    }

    /// <summary>
    /// ロープが巻き付ける地形の角（D-33）。Vertex は地形の角そのもの、Pivot はロープの支点を置く点
    /// （角から外側へ少しずらした点。角を通る線が箱の面に触れて遮られたと判定しないように）。
    /// </summary>
    public readonly struct RopeCorner
    {
        public readonly Vector3 Vertex;
        public readonly Vector3 Pivot;

        public RopeCorner(Vector3 vertex, Vector3 pivot)
        {
            Vertex = vertex;
            Pivot = pivot;
        }
    }

    /// <summary>
    /// シミュレーションが地形を調べるためのインターフェース。
    /// 実装は BoxTerrain（軸平行の矩形の集まり）。Unity のシーンからは SceneTerrainBuilder で作る（D-32）。
    /// 地形は静的（動かない）で、ゲームプレイは XY 平面上で行う。
    /// </summary>
    public interface ICollisionQuery
    {
        /// <summary>
        /// 軸平行の箱（中心 center、半サイズ halfExtents）を delta だけ動かしたとき、最初に当たる地形を返す。
        /// 開始時点で重なっている地形は対象外（先に ComputePenetration で解消する）。
        /// </summary>
        bool SweepBox(Vector3 center, Vector3 halfExtents, Vector3 delta, out CastHit hit);

        /// <summary>
        /// 箱が地形に重なっている場合、重なりを解消する移動量を返す。
        /// </summary>
        bool ComputePenetration(Vector3 center, Vector3 halfExtents, out Vector3 correction);

        /// <summary>
        /// origin から direction（正規化済み）の方向へ maxDistance までの線分で、最初に当たる地形を返す。
        /// </summary>
        bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out CastHit hit);

        /// <summary>
        /// ワイヤーのエイムアシストの対象になるアンカーポイント。
        /// </summary>
        IReadOnlyList<Vector3> AnchorPoints { get; }

        /// <summary>
        /// ロープが巻き付ける地形の角（ほかの地形に埋まっていない角。D-33）。
        /// </summary>
        IReadOnlyList<RopeCorner> RopeCorners { get; }
    }
}
