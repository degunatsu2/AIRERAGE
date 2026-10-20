using System;
using System.Numerics;

namespace Airerage.Core.Economy
{
    /// <summary>
    /// キャラクター1人のコイン（docs/01_game_design.md 3.1）。増減は CoinLedger だけが行う。
    /// スコアは確定コインだけ（D-25）。
    /// </summary>
    public struct CoinPurse : IEquatable<CoinPurse>
    {
        /// <summary>所持コイン（未確定。被弾や撃破で落とす）。</summary>
        public int Held;

        /// <summary>確定コイン（拠点で確定したもの。失わない）。</summary>
        public int Banked;

        public bool Equals(CoinPurse other)
        {
            return Held == other.Held && Banked == other.Banked;
        }

        public override bool Equals(object obj)
        {
            return obj is CoinPurse other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Held, Banked);
        }

        public override string ToString()
        {
            return $"held={Held} banked={Banked}";
        }
    }

    /// <summary>フィールドにある、拾えるコイン1つ（定期出現か、ドロップ）。</summary>
    public readonly struct FieldCoin : IEquatable<FieldCoin>
    {
        /// <summary>通し番号（試合の中で一意。出た順に大きくなる）。</summary>
        public readonly int Id;

        public readonly Vector2 Position;

        /// <summary>枚数（1 つのコインが何枚分か）。</summary>
        public readonly int Value;

        /// <summary>このティックから拾える（ドロップの直後は拾えない）。</summary>
        public readonly int PickableFromTick;

        /// <summary>このティックで消える（寿命）。int.MaxValue なら消えない。</summary>
        public readonly int ExpireTick;

        /// <summary>出した出現地点の番号（ドロップなら -1）。同じ地点からは、前のコインが拾われるまで出さない。</summary>
        public readonly int Source;

        /// <summary>どうやってフィールドに出たか（Spawn、HitDrop、KoDrop）。表示が、撃破ドロップのかたまりを示すのに使う。</summary>
        public readonly CoinEventKind Origin;

        public FieldCoin(int id, Vector2 position, int value, int pickableFromTick, int expireTick, int source = -1,
            CoinEventKind origin = CoinEventKind.Spawn)
        {
            Id = id;
            Position = position;
            Value = value;
            PickableFromTick = pickableFromTick;
            ExpireTick = expireTick;
            Source = source;
            Origin = origin;
        }

        public bool Equals(FieldCoin other)
        {
            return Id == other.Id && Position.Equals(other.Position) && Value == other.Value
                && PickableFromTick == other.PickableFromTick && ExpireTick == other.ExpireTick && Source == other.Source
                && Origin == other.Origin;
        }

        public override bool Equals(object obj)
        {
            return obj is FieldCoin other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Position, Value, PickableFromTick, ExpireTick, Source, Origin);
        }

        public override string ToString()
        {
            return $"#{Id} {Value} at {Position}";
        }
    }

    /// <summary>コインの取引の種類（docs/01_game_design.md 3.2）。</summary>
    public enum CoinEventKind : byte
    {
        /// <summary>出現（生成。フィールド＋）。</summary>
        Spawn = 0,

        /// <summary>拾得（移動。フィールド → 所持）。</summary>
        Pickup = 1,

        /// <summary>被弾ドロップ（移動。所持 → フィールド）。</summary>
        HitDrop = 2,

        /// <summary>撃破ドロップ（移動。所持 → フィールド）。</summary>
        KoDrop = 3,

        /// <summary>確定（移動。所持 → 確定）。</summary>
        Bank = 4,

        /// <summary>ダメージ報酬（生成。→ 攻撃者の所持）。</summary>
        DamageReward = 5,

        /// <summary>撃破報酬（生成。→ 攻撃者の所持）。</summary>
        KoReward = 6,

        /// <summary>消滅（破棄。フィールド −）。</summary>
        Expire = 7,

        /// <summary>預け入れ（移動。所持 → 置かれた物。セーフティボックス。docs/14）。</summary>
        Deposit = 8,

        /// <summary>ばらまき（置かれた物 → フィールド。預けた分は移動、増えた分は生成。セーフティボックスが壊れた・時間切れ）。</summary>
        SafeSpill = 9,

        /// <summary>効率のボーナス（生成。→ 所持。フィーバータイム）。</summary>
        CoinBonus = 10,

        /// <summary>コズミックレイでの確定（移動。所持 → 確定。ビームが拠点に触れていたとき。SPACE TRAVELER）。</summary>
        RayBank = 11,

        /// <summary>ステージの危険（溶岩流・つらら）に当たったドロップ（移動。所持 → フィールド。威力に応じた量）。</summary>
        HazardDrop = 12,
    }

    /// <summary>
    /// 取引のイベント ID。ティック、種類、対象（コインの番号やキャラクターの番号など、取引を出す側が決める）、同じ対象の中の連番で
    /// 決まる。同じティックに同じ ID の取引が来たら、CoinLedger が拒否する（二重の加算を防ぐ）。
    /// </summary>
    public readonly struct CoinEventId : IEquatable<CoinEventId>
    {
        public readonly int Tick;
        public readonly CoinEventKind Kind;
        public readonly int Subject;
        public readonly int Sequence;

        public CoinEventId(int tick, CoinEventKind kind, int subject, int sequence = 0)
        {
            Tick = tick;
            Kind = kind;
            Subject = subject;
            Sequence = sequence;
        }

        public bool Equals(CoinEventId other)
        {
            return Tick == other.Tick && Kind == other.Kind && Subject == other.Subject && Sequence == other.Sequence;
        }

        public override bool Equals(object obj)
        {
            return obj is CoinEventId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Tick, Kind, Subject, Sequence);
        }

        public override string ToString()
        {
            return $"{Kind}@{Tick}:{Subject}.{Sequence}";
        }
    }

    /// <summary>行われた取引1つ（表示、音、記録用。状態ではないので、巻き戻しでは戻らない）。</summary>
    public readonly struct CoinEvent
    {
        public readonly CoinEventId Id;

        /// <summary>関係するキャラクターの番号（なければ -1）。</summary>
        public readonly int Character;

        /// <summary>関係するフィールドのコインの番号（なければ -1）。</summary>
        public readonly int CoinId;

        public readonly int Amount;
        public readonly Vector2 Position;

        public CoinEvent(CoinEventId id, int character, int coinId, int amount, Vector2 position)
        {
            Id = id;
            Character = character;
            CoinId = coinId;
            Amount = amount;
            Position = position;
        }

        public override string ToString()
        {
            return $"{Id} char={Character} coin={CoinId} amount={Amount}";
        }
    }

    /// <summary>コインの出現地点の種類。</summary>
    public enum CoinSpawnKind : byte
    {
        /// <summary>ふつうの地点。コインが拾われてから MatchRules.CoinSpawnIntervalTicks たつと、また出る。</summary>
        Regular = 0,

        /// <summary>
        /// 100 枚のコインの候補の地点（ユーザーの要望、2026-10-13）。残り 1 分のときに、候補の中から1つを選んで1回だけ出す
        /// （選び方は試合の種で決まる）。ふつうには出さない。
        /// </summary>
        Jackpot = 1,
    }

    /// <summary>コインの出現地点（ステージに置く。docs/12 T4-4）。基本は空中に置き、取りにくい場所ほど高額にする。</summary>
    public readonly struct CoinSpawnPoint : IEquatable<CoinSpawnPoint>
    {
        public readonly Vector2 Position;

        /// <summary>出すコインの枚数。</summary>
        public readonly int Value;

        public readonly CoinSpawnKind Kind;

        public CoinSpawnPoint(Vector2 position, int value, CoinSpawnKind kind = CoinSpawnKind.Regular)
        {
            if (value <= 0)
            {
                throw new ArgumentException($"コインの出現地点の枚数は 1 以上にします（{value}）");
            }
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || float.IsInfinity(position.X) || float.IsInfinity(position.Y))
            {
                throw new ArgumentException($"コインの出現地点の位置が不正です（{position}）");
            }
            Position = position;
            Value = value;
            Kind = kind;
        }

        /// <summary>並べ替えの順（シーンから集めた順によらず、同じ番号になるように）。</summary>
        public int CompareTo(CoinSpawnPoint other)
        {
            int c = Kind.CompareTo(other.Kind);
            if (c == 0)
            {
                c = Position.X.CompareTo(other.Position.X);
            }
            if (c == 0)
            {
                c = Position.Y.CompareTo(other.Position.Y);
            }
            return c != 0 ? c : Value.CompareTo(other.Value);
        }

        public bool Equals(CoinSpawnPoint other)
        {
            return Position.Equals(other.Position) && Value == other.Value && Kind == other.Kind;
        }

        public override bool Equals(object obj)
        {
            return obj is CoinSpawnPoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Position, Value, Kind);
        }

        public override string ToString()
        {
            return $"{Kind} {Value} at {Position}";
        }
    }
}
