using System;
using System.Numerics;

namespace Airerage.Core.Simulation
{
    /// <summary>ステージのギミックの種類（StageZone。ユーザーの要望、2026-10-13）。</summary>
    public enum StageZoneKind : byte
    {
        /// <summary>ジャンプ台。触れると Direction の向きに Power の速さで打ち上げる。空中ジャンプが戻る。</summary>
        JumpPad = 0,

        /// <summary>上昇気流。中にいる間、上向きに Power（m/s²）で加速する。上向きの速さは Limit まで。</summary>
        Updraft = 1,

        /// <summary>
        /// 加速リング。外から入ったとき、通っている向き（Direction の向きか、その逆）の速さを Power 以上にする（両方向。
        /// ユーザーの要望、2026-10-13。速い人を遅くはしない）。Direction に対して真横に入ったときは効かない。
        /// </summary>
        BoostRing = 2,

        /// <summary>
        /// スピードレーン（動く歩道。昼のビル街。docs/15 6.11）。地面に立って重なっている間、Direction の向きの速さを Power 以上にする
        /// （走ったまま跳ぶと、その速さで隣のビルへ渡れる）。
        /// </summary>
        SpeedLane = 3,

        /// <summary>
        /// 発射台（屋上のダクト、噴火口、マスドライバー、間欠泉）。触れると速度をまるごと Direction の向き × Power にする（ジャンプ台と違い、
        /// 横の速さを残さないので、いつも同じ軌道で飛ぶ）。空中ジャンプが戻る。周期を決めると、その間だけ効く（間欠泉）。
        /// </summary>
        Launcher = 4,

        /// <summary>バンパー（ネオン看板、噴石）。中心へ向かって触れると、中心から外向きに Power の速さで弾き返す。空中ジャンプが戻る。</summary>
        Bumper = 5,

        /// <summary>
        /// 風（ビル風、地下鉄の通風口、下降気流）。中にいる間、Direction の向きに Power（m/s²）で加速する（その向きの速さは Limit まで）。
        /// 地面に立っている人は、横向きの成分で、Limit × GroundWindRatio の速さで流される（走る速さの目標がずれる）。
        /// 周期を決めると、その間だけ吹く。
        /// </summary>
        Gust = 6,

        /// <summary>氷（雪原）。地面に立って重なっている間、横の速さの変化を Power 倍にする（0〜1。小さいほど滑る。止まりにくく、曲がりにくい）。</summary>
        Ice = 7,

        /// <summary>低重力（宇宙ステーション）。中にいる間、重力を Power 倍（0〜1）にし、落ちる速さを Limit までにする。</summary>
        LowGravity = 8,

        /// <summary>
        /// ワープゲート（宇宙ステーション）。外から入ると、Target の位置へ移る（速度はそのまま。つないでいたワイヤーは外れる）。
        /// 2 つのゲートで互いを Target にして、行き来できるようにする。
        /// </summary>
        Warp = 9,

        /// <summary>
        /// 噴き出す危険（溶岩流、つらら。ユーザーの要望、2026-10-19）。周期ごとに、範囲の中のランダムな横の位置（幅 Width の柱。位置は
        /// 試合の種と周期の番号で決まる）が吹く間（ActiveTicks）だけ効き、触れた人を Direction の向きに吹き飛ばす（Power は威力。
        /// 吹き飛びの速さ = 8 + Power × 0.25 m/s、のけぞり 40 ティック）。同じ噴き出しには 1 回だけ当たる。WarningTicks 前から予告する（表示と音）。
        /// </summary>
        Hazard = 10,

        /// <summary>
        /// 吹雪（雪原と海。ユーザーの要望、2026-10-19）。周期ごと（Period ＋ ランダムに 0〜JitterTicks）に ActiveTicks だけ吹き、範囲の中で
        /// 風上（Direction の逆）の Limit m の中に地形のない人を Power 秒凍らせる（動けない。重力は効く。攻撃を受けると解ける）。
        /// WarningTicks 前から予告する。
        /// </summary>
        Blizzard = 11,

        /// <summary>
        /// 水（海。ユーザーの要望、2026-10-26）。範囲の上の辺が水面。中心が水面より下に入ると、重力の代わりに浮力で水面に浮き、横は泳ぐ速さ
        /// まで遅くなる（水面に浮いている間はジャンプできる）。陸に着くかワイヤーをつなぐまでに、合わせて Power 秒水の中にいると溺れ、
        /// 沈んで撃破される。水の中では下向きの速さを一定の割合で落とし、水面に Limit（m/s）で入ると、ちょうど範囲の下の辺まで沈む
        /// （下の辺を撃破の線に合わせると、それより速く入った人は撃破される）。StageZones.ApplyWaterForces と UpdateWater。
        /// </summary>
        Water = 12,
    }

    /// <summary>ギミックの周期（ティック）。Period が 0 ならいつも効く。</summary>
    public readonly struct StageZoneTiming : IEquatable<StageZoneTiming>
    {
        /// <summary>周期（ティック。0 ならいつも）。</summary>
        public readonly int Period;

        /// <summary>1 周期のうち効いているティック数。</summary>
        public readonly int ActiveTicks;

        /// <summary>周期のずれ（ティック）。</summary>
        public readonly int PhaseTicks;

        /// <summary>周期ごとにランダムに足す長さの上限（ティック。0 なら決まった周期）。</summary>
        public readonly int JitterTicks;

        /// <summary>効き始める何ティック前から予告するか（表示と音だけ。シミュレーションには関係しない）。</summary>
        public readonly int WarningTicks;

        public StageZoneTiming(int period, int activeTicks, int phaseTicks = 0, int jitterTicks = 0, int warningTicks = 0)
        {
            Period = period;
            ActiveTicks = activeTicks;
            PhaseTicks = phaseTicks;
            JitterTicks = jitterTicks;
            WarningTicks = warningTicks;
        }

        public bool Equals(StageZoneTiming other)
        {
            return Period == other.Period && ActiveTicks == other.ActiveTicks && PhaseTicks == other.PhaseTicks && JitterTicks == other.JitterTicks
                && WarningTicks == other.WarningTicks;
        }

        public override bool Equals(object obj)
        {
            return obj is StageZoneTiming other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Period, ActiveTicks, PhaseTicks, JitterTicks, WarningTicks);
        }
    }

    /// <summary>
    /// ステージのギミック（ジャンプ台、上昇気流、加速リング、ステージ固有のギミック）。動かない軸平行の長方形の範囲で、キャラクターの
    /// 当たりの箱が重なると効く。ギミック自体は状態を持たない（周期・動き・ランダムな位置は、試合のティック数と試合の種だけで決まる。
    /// 巻き戻しても同じ結果になる）。効き方は StageZones.Apply。
    /// 動き（Travel、TravelPeriod）を決めると、範囲が Travel だけ行って戻る（TravelPeriod ティックで 1 往復。At で今の範囲を求める）。
    /// </summary>
    public readonly struct StageZone : IEquatable<StageZone>
    {
        public readonly StageZoneKind Kind;
        public readonly float MinX;
        public readonly float MinY;
        public readonly float MaxX;
        public readonly float MaxY;

        /// <summary>向き（長さ 1。上昇気流では使わない。加速リングでは、この向きとその逆の両方に効く）。</summary>
        public readonly Vector2 Direction;

        /// <summary>
        /// ジャンプ台・加速リングでは速さ（m/s）、上昇気流では加速度（m/s²）、危険・吹雪では威力（当たる・凍ると、この枚数のコインを
        /// 落とす。ユーザーの要望、2026-10-21）、水では浮いていられる長さ（秒）。
        /// </summary>
        public readonly float Power;

        /// <summary>
        /// 上昇気流・風では速さの上限（m/s）、低重力では落ちる速さの上限（m/s）、水では下の辺まで沈む速さ（m/s）、吹雪では影になる距離（m。風上のこの距離の中に地形があれば
        /// 凍らない。0 なら StageZones.BlizzardShelterDistance）、危険では吹き飛ばす速さ（m/s。0 なら StageZones.HazardKnockbackSpeed で
        /// 威力から決める）。ほかの種類では使わない。
        /// </summary>
        public readonly float Limit;

        /// <summary>周期（風・発射台・危険・吹雪）。ほかの種類では 0。</summary>
        public readonly StageZoneTiming Timing;

        /// <summary>ワープゲートの出口（ワールドの位置）。ほかの種類では 0。</summary>
        public readonly Vector2 Target;

        /// <summary>危険の柱の幅（m）。ほかの種類では 0。</summary>
        public readonly float Width;

        /// <summary>動く量（1 往復の行きの分。m）と、1 往復のティック数（0 なら動かない）。</summary>
        public readonly Vector2 Travel;
        public readonly int TravelPeriod;

        public int Period => Timing.Period;
        public int ActiveTicks => Timing.ActiveTicks;
        public int PhaseTicks => Timing.PhaseTicks;

        /// <param name="direction">向き。長さ 1 にして持つ。上昇気流では無視して上向きにする。</param>
        public StageZone(StageZoneKind kind, float minX, float minY, float maxX, float maxY, Vector2 direction, float power, float limit = 0f)
            : this(kind, minX, minY, maxX, maxY, direction, power, limit, default, Vector2.Zero)
        {
        }

        /// <param name="period">周期（ティック。0 ならいつも）。</param>
        /// <param name="activeTicks">1 周期のうち効いているティック数。</param>
        /// <param name="phaseTicks">周期のずれ（ティック）。</param>
        /// <param name="target">ワープゲートの出口。</param>
        public StageZone(StageZoneKind kind, float minX, float minY, float maxX, float maxY, Vector2 direction, float power, float limit,
            int period, int activeTicks, int phaseTicks, Vector2 target)
            : this(kind, minX, minY, maxX, maxY, direction, power, limit, new StageZoneTiming(period, activeTicks, phaseTicks), target)
        {
        }

        /// <param name="timing">周期（風・発射台・危険・吹雪だけ）。</param>
        /// <param name="target">ワープゲートの出口。</param>
        /// <param name="width">危険の柱の幅。</param>
        /// <param name="travel">動く量（行きの分）。</param>
        /// <param name="travelPeriod">1 往復のティック数（0 なら動かない）。</param>
        public StageZone(StageZoneKind kind, float minX, float minY, float maxX, float maxY, Vector2 direction, float power, float limit,
            StageZoneTiming timing, Vector2 target, float width = 0f, Vector2 travel = default, int travelPeriod = 0)
        {
            if (!(minX < maxX) || !(minY < maxY))
            {
                throw new ArgumentException($"ギミックの範囲の最小が最大以上です（x {minX}〜{maxX}、y {minY}〜{maxY}）");
            }
            if (!(power > 0f) || float.IsInfinity(power))
            {
                throw new ArgumentException($"ギミックの強さは正の有限な値にします（{power}）");
            }
            if ((kind == StageZoneKind.Updraft || kind == StageZoneKind.Gust || kind == StageZoneKind.LowGravity || kind == StageZoneKind.Water)
                && (!(limit > 0f) || float.IsInfinity(limit)))
            {
                throw new ArgumentException($"{kind} の速さの上限は正の有限な値にします（{limit}）");
            }
            if ((kind == StageZoneKind.Blizzard || kind == StageZoneKind.Hazard) && (limit < 0f || float.IsNaN(limit) || float.IsInfinity(limit)))
            {
                throw new ArgumentException($"{kind} の {(kind == StageZoneKind.Blizzard ? "影になる距離" : "吹き飛ばす速さ")}は 0 以上の有限な値にします（{limit}）");
            }
            if ((kind == StageZoneKind.Ice || kind == StageZoneKind.LowGravity) && power > 1f)
            {
                throw new ArgumentException($"{kind} の強さは 0 より大きく 1 以下にします（{power}）");
            }
            timing = ValidateTiming(kind, timing);
            if (kind == StageZoneKind.Warp)
            {
                if (float.IsNaN(target.X) || float.IsNaN(target.Y) || float.IsInfinity(target.X) || float.IsInfinity(target.Y)
                    || (target.X >= minX && target.X <= maxX && target.Y >= minY && target.Y <= maxY))
                {
                    throw new ArgumentException($"ワープゲートの出口が不正か、ゲートの中です（{target}）");
                }
            }
            else
            {
                target = Vector2.Zero;
            }
            if (kind == StageZoneKind.Hazard)
            {
                if (!(width > 0f) || width > maxX - minX)
                {
                    throw new ArgumentException($"危険の柱の幅は 0 より大きく、範囲の幅以下にします（{width}）");
                }
            }
            else
            {
                width = 0f;
            }
            if (travelPeriod < 0 || (travelPeriod > 0 && (float.IsNaN(travel.X) || float.IsNaN(travel.Y) || float.IsInfinity(travel.X)
                || float.IsInfinity(travel.Y))))
            {
                throw new ArgumentException($"ギミックの動きが不正です（{travel}、{travelPeriod} ティック）");
            }
            if (travelPeriod == 0)
            {
                travel = Vector2.Zero;
            }
            else if (kind == StageZoneKind.Hazard || kind == StageZoneKind.Blizzard || timing.JitterTicks > 0)
            {
                // ランダムな値は範囲から決まる Salt で変える。動くと Salt が変わるので、ランダムを使うギミックは動かさない。
                throw new ArgumentException($"{kind} のランダムな周期・位置を使うギミックは動かせません");
            }
            if (kind == StageZoneKind.Updraft || !UsesDirection(kind))
            {
                direction = Vector2.UnitY;
            }
            else
            {
                float length = direction.Length();
                if (!(length > 0f) || float.IsInfinity(length))
                {
                    throw new ArgumentException($"ギミックの向きが 0 か不正です（{direction}）");
                }
                direction /= length;
            }
            Kind = kind;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
            Direction = direction;
            Power = power;
            Limit = limit;
            Timing = timing;
            Target = target;
            Width = width;
            Travel = travel;
            TravelPeriod = travelPeriod;
        }

        // 検査を済ませた値から、範囲だけを動かしたものを作る（At）。
        private StageZone(in StageZone source, float dx, float dy)
        {
            Kind = source.Kind;
            MinX = source.MinX + dx;
            MinY = source.MinY + dy;
            MaxX = source.MaxX + dx;
            MaxY = source.MaxY + dy;
            Direction = source.Direction;
            Power = source.Power;
            Limit = source.Limit;
            Timing = source.Timing;
            Target = source.Target;
            Width = source.Width;
            Travel = Vector2.Zero;
            TravelPeriod = 0;
        }

        private static StageZoneTiming ValidateTiming(StageZoneKind kind, StageZoneTiming timing)
        {
            bool periodic = kind == StageZoneKind.Gust || kind == StageZoneKind.Launcher || kind == StageZoneKind.Hazard
                || kind == StageZoneKind.Blizzard;
            if (!periodic)
            {
                return default;
            }
            bool required = kind == StageZoneKind.Hazard || kind == StageZoneKind.Blizzard;
            if (timing.Period < 0 || (required && timing.Period == 0)
                || (timing.Period > 0 && (timing.ActiveTicks <= 0 || timing.ActiveTicks > timing.Period))
                || timing.JitterTicks < 0 || timing.WarningTicks < 0 || timing.PhaseTicks < 0
                || (timing.Period > 0 && timing.WarningTicks > timing.Period - timing.ActiveTicks))
            {
                throw new ArgumentException($"{kind} の周期が不正です（周期 {timing.Period}、効く長さ {timing.ActiveTicks}、ずれ {timing.PhaseTicks}、"
                    + $"ばらつき {timing.JitterTicks}、予告 {timing.WarningTicks}）");
            }
            if (timing.Period == 0)
            {
                return default;
            }
            int phase = timing.JitterTicks > 0 ? timing.PhaseTicks : timing.PhaseTicks % timing.Period;
            return new StageZoneTiming(timing.Period, timing.ActiveTicks, phase, timing.JitterTicks, timing.WarningTicks);
        }

        // 向きを使う種類か（バンパー・氷・低重力・ワープは向きを使わない）。
        private static bool UsesDirection(StageZoneKind kind)
        {
            return kind != StageZoneKind.Bumper && kind != StageZoneKind.Ice && kind != StageZoneKind.LowGravity && kind != StageZoneKind.Warp
                && kind != StageZoneKind.Water;
        }

        /// <summary>範囲の中心。</summary>
        public Vector2 Center => new Vector2((MinX + MaxX) * 0.5f, (MinY + MaxY) * 0.5f);

        /// <summary>動くか。</summary>
        public bool Moves => TravelPeriod > 0;

        /// <summary>試合のティック tick の範囲（動かなければそのまま）。行きは Travel だけ進み、帰りは戻る。</summary>
        public StageZone At(int tick)
        {
            if (TravelPeriod <= 0)
            {
                return this;
            }
            float t = OffsetFraction(tick);
            return new StageZone(this, Travel.X * t, Travel.Y * t);
        }

        /// <summary>試合のティック tick の、動く量に対する進み（0〜1。行きで 0→1、帰りで 1→0）。</summary>
        public float OffsetFraction(int tick)
        {
            if (TravelPeriod <= 0)
            {
                return 0f;
            }
            int phase = (int)(((long)tick % TravelPeriod + TravelPeriod) % TravelPeriod);
            int half = TravelPeriod / 2;
            if (half <= 0)
            {
                return 0f;
            }
            return phase < half ? (float)phase / half : (float)(TravelPeriod - phase) / (TravelPeriod - half);
        }

        /// <summary>試合のティック tick に効いているか（周期のあるギミックだけが止まる）。seed は試合の種（ばらつきのある周期で使う）。</summary>
        public bool IsActive(int tick, int seed = 0)
        {
            if (Timing.Period <= 0)
            {
                return true;
            }
            Cycle(tick, seed, out int index, out int start, out _);
            return index >= 0 && tick - start < Timing.ActiveTicks;
        }

        /// <summary>
        /// tick を含む周期の番号と始まり（まだ最初の周期が始まっていなければ番号 -1）と、次の周期の始まり。
        /// ばらつきのない周期は (tick + PhaseTicks) が Period で割り切れるティックに始まる。ばらつきのある周期は、試合の始まりから
        /// PhaseTicks ＋ (Period ＋ ばらつき) ごとに始まる（ばらつきは試合の種と周期の番号で決まる）。
        /// </summary>
        public void Cycle(int tick, int seed, out int index, out int start, out int next)
        {
            int period = Timing.Period;
            if (period <= 0)
            {
                index = 0;
                start = int.MinValue;
                next = int.MaxValue;
                return;
            }
            if (Timing.JitterTicks <= 0)
            {
                long shifted = (long)tick + Timing.PhaseTicks;
                long cycle = shifted >= 0 ? shifted / period : -((-shifted + period - 1) / period);
                index = (int)cycle;
                start = (int)(cycle * period - Timing.PhaseTicks);
                next = start + period;
                return;
            }
            // ばらつきのある周期: 最初から順に足していく（試合の長さなら回数は少ない）。
            index = -1;
            start = int.MinValue;
            long at = Timing.PhaseTicks;
            for (int k = 0; ; k++)
            {
                long begin = at + period + Jitter(seed, k);
                if (begin > tick || begin > int.MaxValue / 2)
                {
                    next = (int)Math.Min(begin, int.MaxValue);
                    return;
                }
                index = k;
                start = (int)begin;
                at = begin;
            }
        }

        // 周期 k のばらつき（0〜JitterTicks）。
        private int Jitter(int seed, int k)
        {
            return (int)(StageRandom.Hash(seed, Salt, k) % (uint)(Timing.JitterTicks + 1));
        }

        /// <summary>周期ごとのランダムな値を、ギミックごとに変える数（範囲から決まる。シーンで見つかった順によらない）。</summary>
        public int Salt => StageRandom.Mix(StageRandom.Bits(MinX), StageRandom.Bits(MinY), StageRandom.Bits(MaxX)) ^ (int)Kind;

        /// <summary>危険の周期 cycle の柱の範囲（横の位置は試合の種と周期の番号で決まる）。</summary>
        public void HazardColumn(int cycle, int seed, out float minX, out float maxX)
        {
            float span = MaxX - MinX - Width;
            float fraction = (StageRandom.Hash(seed, Salt, cycle) & 0xFFFFFF) / 16777216f;
            minX = MinX + span * fraction;
            maxX = minX + Width;
        }

        /// <summary>中心 position、半分の大きさ halfExtents の箱が範囲と重なるか（辺が触れているだけでも重なるとみなす）。</summary>
        public bool Overlaps(Vector3 position, Vector2 halfExtents)
        {
            return position.X + halfExtents.X >= MinX && position.X - halfExtents.X <= MaxX
                && position.Y + halfExtents.Y >= MinY && position.Y - halfExtents.Y <= MaxY;
        }

        /// <summary>
        /// 中心が from から to へ動いた箱が、途中で範囲と重なったか（速く動いて 1 ティックで通り抜けたときも見つける）。
        /// 範囲を箱の大きさだけ広げ、線分との交わりを調べる。
        /// </summary>
        public bool Swept(Vector3 from, Vector3 to, Vector2 halfExtents)
        {
            float t0 = 0f;
            float t1 = 1f;
            return Clip(from.X, to.X - from.X, MinX - halfExtents.X, MaxX + halfExtents.X, ref t0, ref t1)
                && Clip(from.Y, to.Y - from.Y, MinY - halfExtents.Y, MaxY + halfExtents.Y, ref t0, ref t1);
        }

        private static bool Clip(float start, float delta, float min, float max, ref float t0, ref float t1)
        {
            if (delta == 0f)
            {
                return start >= min && start <= max;
            }
            float a = (min - start) / delta;
            float b = (max - start) / delta;
            if (a > b)
            {
                float swap = a;
                a = b;
                b = swap;
            }
            t0 = MathF.Max(t0, a);
            t1 = MathF.Min(t1, b);
            return t0 <= t1;
        }

        /// <summary>並べ替えの順（シーンから集めた順によらず、同じ順で効かせるため）。</summary>
        public int CompareTo(StageZone other)
        {
            int c = Kind.CompareTo(other.Kind);
            if (c == 0)
            {
                c = MinX.CompareTo(other.MinX);
            }
            if (c == 0)
            {
                c = MinY.CompareTo(other.MinY);
            }
            if (c == 0)
            {
                c = MaxX.CompareTo(other.MaxX);
            }
            if (c == 0)
            {
                c = MaxY.CompareTo(other.MaxY);
            }
            if (c == 0)
            {
                c = Direction.X.CompareTo(other.Direction.X);
            }
            if (c == 0)
            {
                c = Direction.Y.CompareTo(other.Direction.Y);
            }
            if (c == 0)
            {
                c = Power.CompareTo(other.Power);
            }
            if (c == 0)
            {
                c = Limit.CompareTo(other.Limit);
            }
            if (c == 0)
            {
                c = Timing.Period.CompareTo(other.Timing.Period);
            }
            if (c == 0)
            {
                c = Timing.ActiveTicks.CompareTo(other.Timing.ActiveTicks);
            }
            if (c == 0)
            {
                c = Timing.PhaseTicks.CompareTo(other.Timing.PhaseTicks);
            }
            if (c == 0)
            {
                c = Timing.JitterTicks.CompareTo(other.Timing.JitterTicks);
            }
            if (c == 0)
            {
                c = Timing.WarningTicks.CompareTo(other.Timing.WarningTicks);
            }
            if (c == 0)
            {
                c = Target.X.CompareTo(other.Target.X);
            }
            if (c == 0)
            {
                c = Target.Y.CompareTo(other.Target.Y);
            }
            if (c == 0)
            {
                c = Width.CompareTo(other.Width);
            }
            if (c == 0)
            {
                c = Travel.X.CompareTo(other.Travel.X);
            }
            if (c == 0)
            {
                c = Travel.Y.CompareTo(other.Travel.Y);
            }
            return c != 0 ? c : TravelPeriod.CompareTo(other.TravelPeriod);
        }

        public bool Equals(StageZone other)
        {
            return Kind == other.Kind && MinX.Equals(other.MinX) && MinY.Equals(other.MinY) && MaxX.Equals(other.MaxX)
                && MaxY.Equals(other.MaxY) && Direction.Equals(other.Direction) && Power.Equals(other.Power) && Limit.Equals(other.Limit)
                && Timing.Equals(other.Timing) && Target.Equals(other.Target) && Width.Equals(other.Width) && Travel.Equals(other.Travel)
                && TravelPeriod == other.TravelPeriod;
        }

        public override bool Equals(object obj)
        {
            return obj is StageZone other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(HashCode.Combine(Kind, MinX, MinY, MaxX, MaxY, Direction, Power, Limit), Timing, Target, Width, Travel, TravelPeriod);
        }

        public override string ToString()
        {
            return $"{Kind} x {MinX}〜{MaxX}, y {MinY}〜{MaxY}, dir {Direction}, power {Power}";
        }
    }

    /// <summary>ステージのギミックのランダムな値（試合の種と番号だけで決まる。整数の計算なので、どの PC でも同じ）。</summary>
    public static class StageRandom
    {
        public static uint Hash(int seed, int salt, int index)
        {
            uint x = (uint)seed * 0x9E3779B1u ^ (uint)salt * 0x85EBCA77u ^ (uint)index * 0xC2B2AE3Du;
            return Finalize(x + 0x27D4EB2Fu);
        }

        public static int Mix(int a, int b, int c)
        {
            return (int)Finalize((uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du);
        }

        /// <summary>float のビット列（同じ値ならどの PC でも同じ）。</summary>
        public static int Bits(float value)
        {
            return BitConverter.SingleToInt32Bits(value);
        }

        private static uint Finalize(uint x)
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }
    }
}
