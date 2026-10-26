using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Common;
using Airerage.Core.Physics;

namespace Airerage.Core.Simulation
{
    /// <summary>ステージの仕掛けの種類（StageTrap。ユーザーの要望、2026-10-26）。</summary>
    public enum StageTrapKind : byte
    {
        /// <summary>
        /// 赤外線レーダーとミサイル発射装置（研究所）。だれかがレーダー（Beam の範囲）に触れると、ArmTicks の予告の後に、発射口（Muzzle）
        /// から向き（Direction）へ速さ Speed のミサイルを撃つ（レーダーに沿って Range m まで飛ぶ）。ミサイルは最初に当たった人で消える。
        /// 撃ってから飛び終わり、さらに CooldownTicks たつと、また撃てる。触れた・撃った時刻は状態（StageState）。
        /// </summary>
        Tripwire = 0,

        /// <summary>
        /// ピッチャーマシン（陸上競技場）。周期 Period ごとに、発射口（Muzzle）から LaunchVelocity でボールを打ち出す。ボールは重力で落ち、
        /// 動かない地形で跳ね、転がって止まったら BlinkTicks だけ点滅して消える。ボールの道のりは人に関係なく決まる（作るときに地形から
        /// 求める。Prepare）ので、状態を持たない。速さが MinHitSpeed 以上のボールに触れると吹き飛ぶ。
        /// </summary>
        Pitcher = 1,
    }

    /// <summary>
    /// ステージの仕掛け（赤外線とミサイル、ピッチャーマシン。ユーザーの要望、2026-10-26）。当たると、ステージの危険と同じように吹き飛び、
    /// 威力（Power）の枚数のコインを落とす（StageZones.Strike）。決まりは StageTraps。値は作るときに検査する（不正なら ArgumentException）。
    /// </summary>
    public sealed class StageTrap
    {
        /// <summary>ボールの重力（m/s²）と、跳ね返り・転がりの値【仮】。</summary>
        public const float BallGravity = 26f;
        public const float BallRestitution = 0.7f;
        public const float BallBounceFriction = 0.86f;
        public const float BallRollDeceleration = 5f;
        public const float BallStopSpeed = 0.6f;

        /// <summary>ボールが跳ねなくなる（転がり始める）上下の速さ（m/s）【仮】。</summary>
        public const float BallSettleSpeed = 2.4f;

        /// <summary>ボールが飛んでいられる長さの上限（ティック。止まらなくても、ここで点滅を始める）【仮】。</summary>
        public const int BallMaxFlightTicks = 900;

        private Vector2[] _path = new Vector2[0];

        private StageTrap(StageTrapKind kind)
        {
            Kind = kind;
        }

        public StageTrapKind Kind { get; }

        /// <summary>ミサイル・ボールの出る位置。</summary>
        public Vector2 Muzzle { get; private set; }

        /// <summary>当たったときに落とすコインの枚数（威力）と、吹き飛ばす速さ（m/s）。</summary>
        public int Power { get; private set; }
        public float Knockback { get; private set; }

        /// <summary>ミサイル・ボールの大きさ（半分。m）。</summary>
        public Vector2 HalfSize { get; private set; }

        /// <summary>赤外線レーダーの範囲。</summary>
        public Vector2 BeamMin { get; private set; }
        public Vector2 BeamMax { get; private set; }

        /// <summary>ミサイルの向き（長さ 1）・速さ（m/s）・飛ぶ距離（m）。</summary>
        public Vector2 Direction { get; private set; }
        public float Speed { get; private set; }
        public float Range { get; private set; }

        /// <summary>レーダーに触れてから撃つまで（予告。ティック）と、飛び終わってからまた撃てるまで（ティック）。</summary>
        public int ArmTicks { get; private set; }
        public int CooldownTicks { get; private set; }

        /// <summary>ミサイルが飛び終わるまでのティック数。</summary>
        public int FlightTicks => Speed > 0f ? (int)MathF.Ceiling(Range / Speed * SimTime.TicksPerSecond) : 0;

        /// <summary>ボールを打ち出す速度と周期・ずれ（ティック）。</summary>
        public Vector2 LaunchVelocity { get; private set; }
        public int Period { get; private set; }
        public int PhaseTicks { get; private set; }

        /// <summary>ボールが止まってから消えるまでの点滅の長さ（ティック）と、当たる速さの下限（m/s）。</summary>
        public int BlinkTicks { get; private set; }
        public float MinHitSpeed { get; private set; }

        /// <summary>ボールの道のり（打ち出してからのティックごとの位置。Prepare で求める）。</summary>
        public IReadOnlyList<Vector2> Path => _path;

        /// <summary>ボールが止まった（点滅を始めた）、打ち出してからのティック。</summary>
        public int StopTick { get; private set; }

        /// <summary>赤外線レーダーとミサイル発射装置を作る。</summary>
        public static StageTrap Tripwire(Vector2 beamMin, Vector2 beamMax, Vector2 muzzle, Vector2 direction, float speed, float range, int power,
            float knockback, int armTicks, int cooldownTicks, Vector2 halfSize)
        {
            float length = direction.Length();
            if (!(beamMin.X < beamMax.X) || !(beamMin.Y < beamMax.Y) || !(length > 0f) || float.IsInfinity(length) || !(speed > 0f)
                || float.IsInfinity(speed) || !(range > 0f) || float.IsInfinity(range) || power <= 0 || !(knockback > 0f) || armTicks < 0
                || cooldownTicks < 0 || !(halfSize.X > 0f) || !(halfSize.Y > 0f) || !Finite(muzzle))
            {
                throw new ArgumentException($"赤外線の仕掛けの値が不正です（範囲 {beamMin}〜{beamMax}、発射口 {muzzle}、向き {direction}、速さ {speed}、"
                    + $"距離 {range}、威力 {power}、吹き飛び {knockback}、予告 {armTicks}、待ち {cooldownTicks}、大きさ {halfSize}）");
            }
            return new StageTrap(StageTrapKind.Tripwire)
            {
                BeamMin = beamMin,
                BeamMax = beamMax,
                Muzzle = muzzle,
                Direction = direction / length,
                Speed = speed,
                Range = range,
                Power = power,
                Knockback = knockback,
                ArmTicks = armTicks,
                CooldownTicks = cooldownTicks,
                HalfSize = halfSize,
            };
        }

        /// <summary>ピッチャーマシンを作る（道のりは Prepare で求める）。</summary>
        public static StageTrap Pitcher(Vector2 muzzle, Vector2 launchVelocity, int period, int phaseTicks, float radius, int power, float knockback,
            float minHitSpeed, int blinkTicks)
        {
            if (!Finite(muzzle) || !Finite(launchVelocity) || period <= 0 || phaseTicks < 0 || !(radius > 0f) || float.IsInfinity(radius)
                || power <= 0 || !(knockback > 0f) || minHitSpeed < 0f || blinkTicks < 0)
            {
                throw new ArgumentException($"ピッチャーマシンの値が不正です（発射口 {muzzle}、速度 {launchVelocity}、周期 {period}、ずれ {phaseTicks}、"
                    + $"半径 {radius}、威力 {power}、吹き飛び {knockback}、当たる速さ {minHitSpeed}、点滅 {blinkTicks}）");
            }
            return new StageTrap(StageTrapKind.Pitcher)
            {
                Muzzle = muzzle,
                LaunchVelocity = launchVelocity,
                Period = period,
                PhaseTicks = phaseTicks % period,
                HalfSize = new Vector2(radius, radius),
                Power = power,
                Knockback = knockback,
                MinHitSpeed = minHitSpeed,
                BlinkTicks = blinkTicks,
            };
        }

        private static bool Finite(Vector2 v)
        {
            return !float.IsNaN(v.X) && !float.IsNaN(v.Y) && !float.IsInfinity(v.X) && !float.IsInfinity(v.Y);
        }

        /// <summary>
        /// ボールの道のりを、動かない地形 terrain から求める（ピッチャーマシンだけ。SimWorld が作るときに呼ぶ。同じ地形なら、どの PC でも
        /// 同じ道のりになる）。重力で落ち、地形で跳ね返り（BallRestitution。面に沿う向きは BallBounceFriction 倍）、跳ねなくなったら
        /// 転がって（BallRollDeceleration）止まる。止まるか、飛んでいられる長さを超えたら、その場で BlinkTicks だけ点滅する。
        /// ステージの外（下）へ落ちたら、そこで終わる（点滅しない）。
        /// </summary>
        public void Prepare(ICollisionQuery terrain)
        {
            if (Kind != StageTrapKind.Pitcher || terrain == null)
            {
                return;
            }
            var path = new List<Vector2>();
            Vector2 position = Muzzle;
            Vector2 velocity = LaunchVelocity;
            var half = new Vector3(HalfSize.X, HalfSize.Y, 0.5f);
            float dt = SimTime.DeltaTime;
            bool rolling = false;
            bool fell = false;
            path.Add(position);
            for (int t = 1; t <= BallMaxFlightTicks; t++)
            {
                if (rolling)
                {
                    // 転がる: 横だけ動き、遅くなる。下に地面がなければ、また落ちる。
                    float speed = MathF.Abs(velocity.X);
                    speed = MathF.Max(0f, speed - BallRollDeceleration * dt);
                    velocity = new Vector2(MathF.Sign(velocity.X) * speed, 0f);
                    if (!terrain.SweepBox(new Vector3(position, 0f), half, new Vector3(0f, -0.05f, 0f), out _))
                    {
                        rolling = false;
                    }
                }
                if (!rolling)
                {
                    velocity.Y -= BallGravity * dt;
                }
                Vector2 delta = velocity * dt;
                if (delta.LengthSquared() > 0f && terrain.SweepBox(new Vector3(position, 0f), half, new Vector3(delta, 0f), out CastHit hit))
                {
                    float length = delta.Length();
                    float along = MathF.Max(0f, hit.Distance - 0.002f);
                    position += delta / length * along;
                    var normal = new Vector2(hit.Normal.X, hit.Normal.Y);
                    float into = Vector2.Dot(velocity, normal);
                    if (into < 0f)
                    {
                        Vector2 tangent = velocity - normal * into;
                        velocity = tangent * BallBounceFriction - normal * into * BallRestitution;
                    }
                    if (normal.Y > 0.7f && MathF.Abs(velocity.Y) < BallSettleSpeed)
                    {
                        velocity.Y = 0f;
                        rolling = true;
                    }
                }
                else
                {
                    position += delta;
                }
                path.Add(position);
                if (position.Y < -80f || MathF.Abs(position.X) > 160f)
                {
                    fell = true;
                    break;
                }
                if (rolling && MathF.Abs(velocity.X) < BallStopSpeed)
                {
                    break;
                }
            }
            StopTick = path.Count - 1;
            if (!fell)
            {
                for (int i = 0; i < BlinkTicks; i++)
                {
                    path.Add(position);
                }
            }
            _path = path.ToArray();
        }

        /// <summary>
        /// ピッチャーマシンのボールが、ティック tick にあるか（あれば、打ち出してからのティック age と位置と速度）。点滅している間は
        /// blinking。道のりを求めていなければ、ない。
        /// </summary>
        public bool TryGetBall(int tick, out int age, out Vector2 position, out Vector2 velocity, out bool blinking)
        {
            age = 0;
            position = Muzzle;
            velocity = Vector2.Zero;
            blinking = false;
            if (Kind != StageTrapKind.Pitcher || _path.Length == 0)
            {
                return false;
            }
            long shifted = (long)tick + PhaseTicks;
            if (shifted < 0)
            {
                return false;
            }
            age = (int)(shifted % Period);
            if (age >= _path.Length)
            {
                return false;
            }
            position = _path[age];
            velocity = age > 0 ? (_path[age] - _path[age - 1]) * SimTime.TicksPerSecond : LaunchVelocity;
            blinking = age >= StopTick;
            return true;
        }

        /// <summary>並べ替えの順（シーンから集めた順によらず、同じ番号をつけるため）。</summary>
        public int CompareTo(StageTrap other)
        {
            int c = Kind.CompareTo(other.Kind);
            if (c == 0)
            {
                c = Muzzle.X.CompareTo(other.Muzzle.X);
            }
            if (c == 0)
            {
                c = Muzzle.Y.CompareTo(other.Muzzle.Y);
            }
            if (c == 0)
            {
                c = BeamMin.X.CompareTo(other.BeamMin.X);
            }
            if (c == 0)
            {
                c = BeamMin.Y.CompareTo(other.BeamMin.Y);
            }
            if (c == 0)
            {
                c = PhaseTicks.CompareTo(other.PhaseTicks);
            }
            if (c == 0)
            {
                c = LaunchVelocity.X.CompareTo(other.LaunchVelocity.X);
            }
            return c != 0 ? c : LaunchVelocity.Y.CompareTo(other.LaunchVelocity.Y);
        }

        /// <summary>同じ仕掛けか（値がすべて同じ。表示が自分の番号を探すのに使う）。</summary>
        public bool SameAs(StageTrap other)
        {
            return other != null && CompareTo(other) == 0 && BeamMax.Equals(other.BeamMax) && Direction.Equals(other.Direction)
                && Speed.Equals(other.Speed) && Range.Equals(other.Range) && Power == other.Power && Knockback.Equals(other.Knockback)
                && ArmTicks == other.ArmTicks && CooldownTicks == other.CooldownTicks && HalfSize.Equals(other.HalfSize) && Period == other.Period
                && BlinkTicks == other.BlinkTicks && MinHitSpeed.Equals(other.MinHitSpeed);
        }

        public override string ToString()
        {
            return Kind == StageTrapKind.Tripwire ? $"Tripwire {BeamMin}〜{BeamMax} from {Muzzle} dir {Direction}" : $"Pitcher at {Muzzle} v {LaunchVelocity}";
        }
    }
}
