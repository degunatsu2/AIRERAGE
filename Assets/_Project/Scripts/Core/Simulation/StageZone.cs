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
        /// 発射台（屋上のダクト、溶岩の噴き上げ、マスドライバー）。触れると速度をまるごと Direction の向き × Power にする（ジャンプ台と違い、
        /// 横の速さを残さないので、いつも同じ軌道で飛ぶ）。空中ジャンプが戻る。
        /// </summary>
        Launcher = 4,

        /// <summary>バンパー（ネオン看板、噴石）。中心へ向かって触れると、中心から外向きに Power の速さで弾き返す。空中ジャンプが戻る。</summary>
        Bumper = 5,

        /// <summary>
        /// 風（ビル風、地下鉄の通風口、間欠泉、波）。中にいる間、Direction の向きに Power（m/s²）で加速する（その向きの速さは Limit まで）。
        /// Period が 0 より大きければ、Period ティックごとに ActiveTicks ティックだけ吹く（PhaseTicks だけずらす。試合のティック数で決まる）。
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
    }

    /// <summary>
    /// ステージのギミック（ジャンプ台、上昇気流、加速リング、ステージ固有のギミック）。動かない軸平行の長方形の範囲で、キャラクターの
    /// 当たりの箱が重なると効く。ギミック自体は状態を持たない（周期のある風は試合のティック数で決まる。巻き戻しても同じ結果になる）。
    /// 効き方は StageZones.Apply。
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

        /// <summary>ジャンプ台・加速リングでは速さ（m/s）、上昇気流では加速度（m/s²）。</summary>
        public readonly float Power;

        /// <summary>上昇気流・風では速さの上限（m/s）、低重力では落ちる速さの上限（m/s）。ほかの種類では使わない。</summary>
        public readonly float Limit;

        /// <summary>風の周期（ティック。0 ならいつも吹く）。ほかの種類では 0。</summary>
        public readonly int Period;

        /// <summary>風が 1 周期のうち吹いているティック数（Period が 0 なら使わない）。</summary>
        public readonly int ActiveTicks;

        /// <summary>風の周期のずれ（ティック。0〜Period - 1）。</summary>
        public readonly int PhaseTicks;

        /// <summary>ワープゲートの出口（ワールドの位置）。ほかの種類では 0。</summary>
        public readonly Vector2 Target;

        /// <param name="direction">向き。長さ 1 にして持つ。上昇気流では無視して上向きにする。</param>
        public StageZone(StageZoneKind kind, float minX, float minY, float maxX, float maxY, Vector2 direction, float power, float limit = 0f)
            : this(kind, minX, minY, maxX, maxY, direction, power, limit, 0, 0, 0, Vector2.Zero)
        {
        }

        /// <param name="period">風の周期（ティック。0 ならいつも）。</param>
        /// <param name="activeTicks">風が 1 周期のうち吹いているティック数。</param>
        /// <param name="phaseTicks">風の周期のずれ（ティック）。</param>
        /// <param name="target">ワープゲートの出口。</param>
        public StageZone(StageZoneKind kind, float minX, float minY, float maxX, float maxY, Vector2 direction, float power, float limit,
            int period, int activeTicks, int phaseTicks, Vector2 target)
        {
            if (!(minX < maxX) || !(minY < maxY))
            {
                throw new ArgumentException($"ギミックの範囲の最小が最大以上です（x {minX}〜{maxX}、y {minY}〜{maxY}）");
            }
            if (!(power > 0f) || float.IsInfinity(power))
            {
                throw new ArgumentException($"ギミックの強さは正の有限な値にします（{power}）");
            }
            if ((kind == StageZoneKind.Updraft || kind == StageZoneKind.Gust || kind == StageZoneKind.LowGravity)
                && (!(limit > 0f) || float.IsInfinity(limit)))
            {
                throw new ArgumentException($"{kind} の速さの上限は正の有限な値にします（{limit}）");
            }
            if ((kind == StageZoneKind.Ice || kind == StageZoneKind.LowGravity) && power > 1f)
            {
                throw new ArgumentException($"{kind} の強さは 0 より大きく 1 以下にします（{power}）");
            }
            if (kind != StageZoneKind.Gust)
            {
                period = 0;
                activeTicks = 0;
                phaseTicks = 0;
            }
            else if (period < 0 || (period > 0 && (activeTicks <= 0 || activeTicks > period)))
            {
                throw new ArgumentException($"風の周期が不正です（周期 {period}、吹く長さ {activeTicks}）");
            }
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
            Period = period;
            ActiveTicks = activeTicks;
            PhaseTicks = period > 0 ? ((phaseTicks % period) + period) % period : 0;
            Target = target;
        }

        // 向きを使う種類か（バンパー・氷・低重力・ワープは向きを使わない）。
        private static bool UsesDirection(StageZoneKind kind)
        {
            return kind != StageZoneKind.Bumper && kind != StageZoneKind.Ice && kind != StageZoneKind.LowGravity && kind != StageZoneKind.Warp;
        }

        /// <summary>範囲の中心。</summary>
        public Vector2 Center => new Vector2((MinX + MaxX) * 0.5f, (MinY + MaxY) * 0.5f);

        /// <summary>試合のティック tick に効いているか（周期のある風だけが止まる）。</summary>
        public bool IsActive(int tick)
        {
            if (Period <= 0)
            {
                return true;
            }
            int phase = (int)(((long)tick + PhaseTicks) % Period);
            return phase < ActiveTicks;
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
                c = Period.CompareTo(other.Period);
            }
            if (c == 0)
            {
                c = ActiveTicks.CompareTo(other.ActiveTicks);
            }
            if (c == 0)
            {
                c = PhaseTicks.CompareTo(other.PhaseTicks);
            }
            if (c == 0)
            {
                c = Target.X.CompareTo(other.Target.X);
            }
            return c != 0 ? c : Target.Y.CompareTo(other.Target.Y);
        }

        public bool Equals(StageZone other)
        {
            return Kind == other.Kind && MinX.Equals(other.MinX) && MinY.Equals(other.MinY) && MaxX.Equals(other.MaxX)
                && MaxY.Equals(other.MaxY) && Direction.Equals(other.Direction) && Power.Equals(other.Power) && Limit.Equals(other.Limit)
                && Period == other.Period && ActiveTicks == other.ActiveTicks && PhaseTicks == other.PhaseTicks && Target.Equals(other.Target);
        }

        public override bool Equals(object obj)
        {
            return obj is StageZone other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(HashCode.Combine(Kind, MinX, MinY, MaxX, MaxY, Direction, Power, Limit), Period, ActiveTicks, PhaseTicks, Target);
        }

        public override string ToString()
        {
            return $"{Kind} x {MinX}〜{MaxX}, y {MinY}〜{MaxY}, dir {Direction}, power {Power}";
        }
    }
}
