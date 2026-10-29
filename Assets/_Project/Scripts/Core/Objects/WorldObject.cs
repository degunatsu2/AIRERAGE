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

        /// <summary>ワープポータル（SPACE TRAVELER の Q）。同じ持ち主のポータル 2 つがつながる。壊せない。</summary>
        Portal = 6,

        /// <summary>スペースミサイル（SPACE TRAVELER の E）。まっすぐ飛び、触れたら爆発する。</summary>
        Missile = 7,

        /// <summary>コズミックレイのビーム（SPACE TRAVELER の R）。Position から End までの線分。壁を貫通する。</summary>
        Beam = 8,

        /// <summary>
        /// わたがしの足場（PASTRY CHEF の Q）。Position を中心に、向き Velocity（長さ 1。足場の表の向き）に垂直な横長の長方形
        /// （End は半分の長さと半分の厚さ）。触れた人を弾き飛ばす。ワイヤーはつながらない（弾く）。
        /// </summary>
        CottonCandy = 9,

        /// <summary>飛んでいるクリーム（PASTRY CHEF の E）。重力で落ち、地形に当たるとその場に張り付く（CreamPatch に変わる）。Variant は味。</summary>
        CreamShot = 10,

        /// <summary>
        /// 地形に張り付いたクリーム（PASTRY CHEF の E）。Position を中心、End を半分の大きさにした軸平行の箱。Velocity は張り付いた面の
        /// 法線。Variant は味（CreamFlavor）。
        /// </summary>
        CreamPatch = 11,

        /// <summary>マカロン（PASTRY CHEF の R）。SpawnTick に投げ上げ、地形で跳ね返る。止まるかステージの外に出たら点滅して消える。Variant は色。</summary>
        Macaron = 12,

        /// <summary>
        /// デジタルスケール（PASTRY CHEF の空中下の撃破技）。真下へ自由落下し、落ちていた時間でグラム数・威力・吹き飛びが増える。地形・相手に
        /// 当たるか、ステージの外に出たら消える。
        /// </summary>
        Scale = 13,

        /// <summary>
        /// 雷の波動（SOLDERER のインバータでワイヤーをつないだとき）。つないだ地形の箱の縁を、Position から左右の両回りに進み、反対側で
        /// 出会って消える。Velocity は箱の左下、End は右上。触れた人を感電させる。
        /// </summary>
        ShockWave = 14,

        /// <summary>
        /// ボールライトニング（SOLDERER の R）。SpawnTick に照準の方向へ放ち、ゆっくり進んで壁で跳ね返る。決まった距離を進むと止まり、
        /// しばらく残る罠になる。触れた人を感電させる。Stored は 0: 放つ前、1: 進んでいる、2: 止まった。ChargeMilli は進んだ距離（mm）。
        /// </summary>
        BallLightning = 15,

        /// <summary>煙球（THIEF の地上技）。真下へ落ち、地形か持ち主以外のキャラクターに触れると、ごく狭い範囲を攻撃して煙（Smoke）を出す。</summary>
        SmokeBall = 16,

        /// <summary>
        /// 煙（THIEF の煙球と、分身が消えたとき）。Position を中心、End.X を半径にした円。中の物を見えなくする（見た目だけ。シミュレーション
        /// には効かない）。Variant は 0: 煙球の煙、1: 分身が消えた煙。
        /// </summary>
        Smoke = 17,

        /// <summary>
        /// タレントスナッチの弾（THIEF の Q）。照準の方向へまっすぐ速く飛ぶ。ChargeMilli は飛んだ距離（mm）。キャラクター・奪えるギミック・
        /// 仕掛けに当たると、それを奪う。地形・ステージの外・届く距離の終わりで外れて消える。
        /// </summary>
        SnatchShot = 18,

        /// <summary>
        /// 置いたギミック（THIEF が奪ったステージのギミックの写し）。Stored は元のギミックの番号（SimWorld.Zones）、Position は中心、End は
        /// 半分の大きさ、Variant は 1 なら向きを左右に反転、SpawnTick は置いたティック（周期のあるギミックは、ここで 1 回効き始める）。
        /// ChargeMilli が 1 なら、空中に置いたベルトコンベアで、ベルトの見た目どおりの足場を持つ（WorldCollision.FootingBox。PlacementRules）。
        /// SimWorld が、ステージのギミックに加えて効かせる。
        /// </summary>
        PlacedZone = 19,

        /// <summary>
        /// 置いた仕掛け（THIEF が奪った赤外線とミサイル・ピッチャーマシンの写し）。Stored は元の仕掛けの番号（SimWorld.Traps）、Position は
        /// 発射口、Variant は 1 なら向きを左右に反転、SpawnTick は置いたティック。赤外線は ChargeMilli がレーダーに触れたティック（0 は待って
        /// いる）、HitMask が 1 ならミサイルがもう当たった。置いた瞬間に 1 回撃つ（ThiefRules）。
        /// </summary>
        PlacedTrap = 20,

        /// <summary>
        /// 置いたピッチャーマシンのボール（PlacedTrap が打ち出す）。重力で落ち、地形で跳ね、止まったら点滅して消える。Stored は 1 なら
        /// 転がっている、ChargeMilli は止まったティック（0 は動いている）、Variant は元の仕掛けの番号の下位（表示用）。
        /// </summary>
        TrapBall = 21,
    }

    /// <summary>クリームの味（PASTRY CHEF の E。WorldObject.Variant）。</summary>
    public enum CreamFlavor : byte
    {
        /// <summary>オレンジ: 触れている間は速くなる。壁ジャンプが高くなり、張り付いた所にワイヤーがつながる。</summary>
        Orange = 1,

        /// <summary>抹茶: 触れている間は遅くなる。壁と天井では少しの間張り付く。張り付いた所は壁に張り付けず、ワイヤーもつながらない。</summary>
        Matcha = 2,
    }

    /// <summary>マカロンの色（PASTRY CHEF の R。WorldObject.Variant。色で威力が違う）。</summary>
    public enum MacaronColor : byte
    {
        Green = 0,
        Yellow = 1,
        Rainbow = 2,
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

        /// <summary>今いるワープポータルの番号（投射物。外から入ったときだけ移る。0 はなし）。</summary>
        public int InPortalId;

        /// <summary>もう一方の端（ビームの終わりの点。m）。</summary>
        public Vector2 End;

        /// <summary>
        /// 最近当たった攻撃（壊せる物。攻撃した人と攻撃の番号と判定の番号を 1 つの値にしたもの。0 はなし）。同じ攻撃の同じ判定は
        /// 1 回だけダメージを与える（4 つまで覚える）。
        /// </summary>
        public int HitKey0;
        public int HitKey1;
        public int HitKey2;
        public int HitKey3;

        /// <summary>種類の中の違い（クリームの味 CreamFlavor、マカロンの色 MacaronColor）。</summary>
        public byte Variant;

        /// <summary>このティックで壊れた・寿命が来た（取り除く前の印）。</summary>
        public bool Ended;

        public bool Equals(WorldObject other)
        {
            return Id == other.Id && Kind == other.Kind && Owner == other.Owner && Slot == other.Slot
                && Position.Equals(other.Position) && Velocity.Equals(other.Velocity)
                && SpawnTick == other.SpawnTick && ExpireTick == other.ExpireTick && Damage == other.Damage
                && ChargeMilli == other.ChargeMilli && Stored == other.Stored && HitMask == other.HitMask && BonusPercent == other.BonusPercent
                && LastPushId == other.LastPushId && InPortalId == other.InPortalId && End.Equals(other.End)
                && HitKey0 == other.HitKey0 && HitKey1 == other.HitKey1 && HitKey2 == other.HitKey2 && HitKey3 == other.HitKey3
                && Variant == other.Variant && Ended == other.Ended;
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
