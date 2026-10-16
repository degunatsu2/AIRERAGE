using System;
using System.Numerics;
using Airerage.Core.Combat;

namespace Airerage.Core.Objects
{
    /// <summary>ワールドに置く物と投射物の種類（docs/13 T5-6、docs/14 4 章）。</summary>
    public enum WorldObjectKind : byte
    {
        /// <summary>ミラーボール（RISK TAKER の Q）。アンカーポイントとして使え、つないだ人のゲージがたまる。</summary>
        MirrorBall = 1,

        /// <summary>セーフティボックス（RISK TAKER の E）。壊せる金庫。コインがチャージされ、壊すとばらまく。</summary>
        Safe = 2,

        /// <summary>投射物（RISK TAKER のコイントスなど）。攻撃の判定を持って飛び、当たるか地形に触れると消える。</summary>
        Projectile = 3,

        /// <summary>
        /// 突風（STORM RUNNER のウインドクロークを解いたときに撃つ風）。まっすぐ進み、触れたキャラクターと投射物を進む向きへ押す
        /// （攻撃の判定はない）。持ち主は押さない。地形は通り抜ける。
        /// </summary>
        Gust = 4,

        /// <summary>
        /// 風の帯（STORM RUNNER のスカイロケット）。置いた点から向き（Velocity。長さ 1）へ伸びる長方形で、中の全員と投射物を
        /// その向きへ加速する。動かない。
        /// </summary>
        WindZone = 5,
    }

    /// <summary>
    /// ワールドに置く物・投射物 1 つの状態（値だけで完結させ、巻き戻しと比較ができるようにする）。種類ごとに使う値が違う。
    /// 位置と速度は XY 平面（m、m/s）。
    /// </summary>
    public struct WorldObject : IEquatable<WorldObject>
    {
        /// <summary>通し番号（試合の中で一意。置いた順に大きくなる）。</summary>
        public int Id;

        public WorldObjectKind Kind;

        /// <summary>置いた・撃ったキャラクターの番号。</summary>
        public int Owner;

        /// <summary>置いた技の枠（効果が終わったときにクールタイムを始めるのに使う）。</summary>
        public AttackSlot Slot;

        public Vector2 Position;
        public Vector2 Velocity;

        /// <summary>置いたティック。</summary>
        public int SpawnTick;

        /// <summary>このティックで消える（int.MaxValue なら寿命で消えない）。</summary>
        public int ExpireTick;

        /// <summary>受けたダメージの合計（壊せる物。耐久値以上で壊れる）。</summary>
        public int Damage;

        /// <summary>チャージされたコイン（1/1000 枚単位。セーフティボックス。小数まで記録する）。</summary>
        public long ChargeMilli;

        /// <summary>預けられたコイン（台帳の Stored にある枚数。セーフティボックス）。</summary>
        public int Stored;

        /// <summary>当てたキャラクター（投射物。ビット v）。同じ相手には 1 回だけ当たる。</summary>
        public uint HitMask;

        /// <summary>威力の上乗せ（%。投射物。撃ったときに決める。AttackPower の上乗せ）。</summary>
        public int BonusPercent;

        /// <summary>最後にこの物を押した風の番号（投射物。同じ風に 2 回押されないように。0 はなし）。</summary>
        public int LastPushId;

        /// <summary>
        /// 最近当たった攻撃（壊せる物。攻撃した人と攻撃の番号と判定の番号を 1 つの値にしたもの。0 はなし）。同じ攻撃の同じ判定は
        /// 1 回だけダメージを与える（4 つまで覚える）。
        /// </summary>
        public int HitKey0;
        public int HitKey1;
        public int HitKey2;
        public int HitKey3;

        /// <summary>このティックで壊れた・寿命が来た（取り除く前の印）。</summary>
        public bool Ended;

        public bool Equals(WorldObject other)
        {
            return Id == other.Id && Kind == other.Kind && Owner == other.Owner && Slot == other.Slot
                && Position.Equals(other.Position) && Velocity.Equals(other.Velocity)
                && SpawnTick == other.SpawnTick && ExpireTick == other.ExpireTick && Damage == other.Damage
                && ChargeMilli == other.ChargeMilli && Stored == other.Stored && HitMask == other.HitMask && BonusPercent == other.BonusPercent
                && LastPushId == other.LastPushId
                && HitKey0 == other.HitKey0 && HitKey1 == other.HitKey1 && HitKey2 == other.HitKey2 && HitKey3 == other.HitKey3
                && Ended == other.Ended;
        }

        public override bool Equals(object obj)
        {
            return obj is WorldObject other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Kind, Owner, Position, SpawnTick, ChargeMilli, Damage);
        }

        /// <summary>key の攻撃をもう受けたか。</summary>
        public bool HasHitKey(int key)
        {
            return key != 0 && (HitKey0 == key || HitKey1 == key || HitKey2 == key || HitKey3 == key);
        }

        /// <summary>key の攻撃を受けたことを覚える（古いものから押し出す）。</summary>
        public void AddHitKey(int key)
        {
            HitKey3 = HitKey2;
            HitKey2 = HitKey1;
            HitKey1 = HitKey0;
            HitKey0 = key;
        }

        public override string ToString()
        {
            return $"#{Id} {Kind} P{Owner} at {Position} dmg={Damage} charge={ChargeMilli / 1000}";
        }
    }
}
