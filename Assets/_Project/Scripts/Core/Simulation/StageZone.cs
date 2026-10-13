using System;
using System.Numerics;

namespace Degunatsu.Core.Simulation
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
    }

    /// <summary>
    /// ステージのギミック（ジャンプ台、上昇気流、加速リング）。動かない軸平行の長方形の範囲で、キャラクターの当たりの箱が
    /// 重なると効く。ギミック自体は状態を持たない（巻き戻しても同じ結果になる）。効き方は StageZones.Apply。
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

        /// <summary>上昇気流の上向きの速さの上限（m/s）。ほかの種類では使わない。</summary>
        public readonly float Limit;

        /// <param name="direction">向き。長さ 1 にして持つ。上昇気流では無視して上向きにする。</param>
        public StageZone(StageZoneKind kind, float minX, float minY, float maxX, float maxY, Vector2 direction, float power, float limit = 0f)
        {
            if (!(minX < maxX) || !(minY < maxY))
            {
                throw new ArgumentException($"ギミックの範囲の最小が最大以上です（x {minX}〜{maxX}、y {minY}〜{maxY}）");
            }
            if (!(power > 0f) || float.IsInfinity(power))
            {
                throw new ArgumentException($"ギミックの強さは正の有限な値にします（{power}）");
            }
            if (kind == StageZoneKind.Updraft)
            {
                if (!(limit > 0f) || float.IsInfinity(limit))
                {
                    throw new ArgumentException($"上昇気流の速さの上限は正の有限な値にします（{limit}）");
                }
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
            return c != 0 ? c : Limit.CompareTo(other.Limit);
        }

        public bool Equals(StageZone other)
        {
            return Kind == other.Kind && MinX.Equals(other.MinX) && MinY.Equals(other.MinY) && MaxX.Equals(other.MaxX)
                && MaxY.Equals(other.MaxY) && Direction.Equals(other.Direction) && Power.Equals(other.Power) && Limit.Equals(other.Limit);
        }

        public override bool Equals(object obj)
        {
            return obj is StageZone other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Kind, MinX, MinY, MaxX, MaxY, Direction, Power, Limit);
        }

        public override string ToString()
        {
            return $"{Kind} x {MinX}〜{MaxX}, y {MinY}〜{MaxY}, dir {Direction}, power {Power}";
        }
    }
}
