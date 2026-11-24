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
        /// 動かない地形で跳ね（天井と壁ではよく跳ね返る）、転がって止まったら BlinkTicks だけ点滅して消える。ボールの道のりは人に関係なく
        /// 決まる（作るときに地形から求める。Prepare）ので、状態を持たない。触れた人はバンパーのように弾き返され、速さが MinHitSpeed 以上の
        /// ボールなら吹き飛ぶ（StageTraps）。
        /// </summary>
        Pitcher = 1,

        /// <summary>
        /// 鬼火の輪（恐怖の館。ユーザーの要望、2026-11-21）。決まった地点（Waypoints）の間を、試合の種で決まる順にランダムに飛び回る
        /// （地点で HoverTicks だけ漂い、次の地点へ速さ Speed で飛ぶ。WispAt。道のりはティック数と種だけで決まる）。だれかが輪（HalfSize）に
        /// 触れると、その人に Reward 枚のコインを渡して消え、RespawnMinTicks〜RespawnMaxTicks の後（種で決まる）に、その時の道のりの所に
        /// また現れる。触れたティックと人は状態（StageState）。StageTraps.StepWispRings。
        /// </summary>
        WispRing = 2,

        /// <summary>
        /// ドーナツフライヤー（キッチン。ユーザーの要望、2026-11-24）。周期 Period ごとに、揚げたドーナツを 1 つ出す。ドーナツは出口
        /// （Muzzle）から決まった着地点（Waypoints）へ順に弾みながら進み（重力 DonutGravity の放物線。どの跳ねも、高いほうの点より
        /// HopHeight 上まで上がる）、最後の点（ステージの外）で消える。道のりはティック数だけで決まる（状態を持たない。TryGetDonut）。
        /// 触れた人はバンパーのように弾き返され（StageTraps.DonutBumpPower）、ドーナツの真ん中はアンカー（ワイヤーがつながり、一緒に動く。
        /// StageTraps.AddAnchors・CarryWires）。
        /// </summary>
        DonutFryer = 3,
    }

    /// <summary>
    /// ステージの仕掛け（赤外線とミサイル、ピッチャーマシン。ユーザーの要望、2026-10-26。鬼火の輪は 2026-11-21）。当たると、ステージの危険と同じように吹き飛び、
    /// 威力（Power）の枚数のコインを落とす（StageZones.Strike）。決まりは StageTraps。値は作るときに検査する（不正なら ArgumentException）。
    /// </summary>
    public sealed class StageTrap
    {
        /// <summary>ボールの重力（m/s²）と、床での跳ね返り・転がりの値【仮】。</summary>
        public const float BallGravity = 26f;
        public const float BallRestitution = 0.7f;
        public const float BallBounceFriction = 0.86f;

        /// <summary>天井と壁での跳ね返り（面に垂直な速さの割合）と、面に沿う速さの割合【仮】（ユーザーの要望、2026-10-26: 大きく跳ね返る）。</summary>
        public const float BallWallRestitution = 0.95f;
        public const float BallWallFriction = 1f;
        public const float BallRollDeceleration = 5f;
        public const float BallStopSpeed = 0.6f;

        /// <summary>ボールが跳ねなくなる（転がり始める）上下の速さ（m/s）【仮】。</summary>
        public const float BallSettleSpeed = 2.4f;

        /// <summary>ボールが飛んでいられる長さの上限（ティック。止まらなくても、ここで点滅を始める。点滅の間も動き続ける）【仮】。</summary>
        public const int BallMaxFlightTicks = 900;

        /// <summary>ドーナツの重力（m/s²。キャラクターと同じ）。</summary>
        public const float DonutGravity = 30f;

        /// <summary>1 つのドーナツフライヤーから同時に出ていられるドーナツの数の上限（ワイヤーの接続先の番号を分けるため）。</summary>
        public const int MaxDonuts = 8;

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

        /// <summary>鬼火の輪の地点・地点で漂う長さ（ティック）・渡すコインの枚数・また現れるまでの長さ（ティック）。速さは Speed、輪の大きさは HalfSize。</summary>
        public IReadOnlyList<Vector2> Waypoints => _waypoints;
        public int HoverTicks { get; private set; }
        public int Reward { get; private set; }
        public int RespawnMinTicks { get; private set; }
        public int RespawnMaxTicks { get; private set; }

        /// <summary>鬼火の輪の道のりの乱数の種の混ぜ物。</summary>
        private const int WispSalt = 0x5717;

        private Vector2[] _waypoints = new Vector2[0];

        /// <summary>ボールの道のり（打ち出してからのティックごとの位置。Prepare で求める）。</summary>
        public IReadOnlyList<Vector2> Path => _path;

        /// <summary>ボールが止まった（点滅を始めた）、打ち出してからのティック。</summary>
        public int StopTick { get; private set; }

        /// <summary>ドーナツの跳ねの高さ（m。跳ねるたびに、前と次の着地点の高いほうからこれだけ上がる）。</summary>
        public float HopHeight { get; private set; }

        /// <summary>同時に出ていられるドーナツの数（周期と道のりの長さで決まる。番号 0〜DonutSlots-1 を順に使い回す）。</summary>
        public int DonutSlots { get; private set; }

        /// <summary>ドーナツが出てから、それぞれの着地点に着くティック（表示の弾む形に使う）。</summary>
        public IReadOnlyList<int> LandingTicks => _landingTicks;

        private int[] _landingTicks = new int[0];

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

        /// <summary>鬼火の輪を作る（地点は 2 つ以上。最初の地点を Muzzle にする〈並べ替えの順に使う〉）。</summary>
        public static StageTrap WispRing(IReadOnlyList<Vector2> waypoints, float speed, int hoverTicks, Vector2 halfSize, int reward, int respawnMinTicks,
            int respawnMaxTicks)
        {
            if (waypoints == null || waypoints.Count < 2 || !(speed > 0f) || float.IsInfinity(speed) || hoverTicks < 0 || !(halfSize.X > 0f)
                || !(halfSize.Y > 0f) || reward <= 0 || respawnMinTicks <= 0 || respawnMaxTicks < respawnMinTicks)
            {
                throw new ArgumentException($"鬼火の輪の値が不正です（地点 {waypoints?.Count ?? 0}、速さ {speed}、漂う {hoverTicks}、大きさ {halfSize}、"
                    + $"コイン {reward}、また現れる {respawnMinTicks}〜{respawnMaxTicks}）");
            }
            var copy = new Vector2[waypoints.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                if (!Finite(waypoints[i]))
                {
                    throw new ArgumentException($"鬼火の輪の {i} 番目の地点が不正です（{waypoints[i]}）");
                }
                copy[i] = waypoints[i];
            }
            return new StageTrap(StageTrapKind.WispRing)
            {
                _waypoints = copy,
                Muzzle = copy[0],
                Speed = speed,
                HoverTicks = hoverTicks,
                HalfSize = halfSize,
                Reward = reward,
                RespawnMinTicks = respawnMinTicks,
                RespawnMaxTicks = respawnMaxTicks,
            };
        }

        /// <summary>
        /// ドーナツフライヤーを作る。muzzle は出口、landings は着地点（1 つ以上。最後はステージの外で消える所）。周期 period ごとに
        /// 1 つ出す（ずれ phaseTicks。試合が始まる前に出たことになるドーナツはない）。radius はドーナツの半径、hopHeight は跳ねの高さ。
        /// 道のりは作るときに求める（重力 DonutGravity の放物線をつなぐ）。
        /// </summary>
        public static StageTrap DonutFryer(Vector2 muzzle, IReadOnlyList<Vector2> landings, int period, int phaseTicks, float radius, float hopHeight)
        {
            if (!Finite(muzzle) || landings == null || landings.Count < 1 || period <= 0 || phaseTicks < 0 || !(radius > 0f) || float.IsInfinity(radius)
                || !(hopHeight > 0f) || float.IsInfinity(hopHeight))
            {
                throw new ArgumentException($"ドーナツフライヤーの値が不正です（出口 {muzzle}、着地点 {landings?.Count ?? 0}、周期 {period}、ずれ {phaseTicks}、"
                    + $"半径 {radius}、跳ね {hopHeight}）");
            }
            var copy = new Vector2[landings.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                if (!Finite(landings[i]))
                {
                    throw new ArgumentException($"ドーナツフライヤーの {i} 番目の着地点が不正です（{landings[i]}）");
                }
                copy[i] = landings[i];
            }
            var trap = new StageTrap(StageTrapKind.DonutFryer)
            {
                _waypoints = copy,
                Muzzle = muzzle,
                Period = period,
                PhaseTicks = phaseTicks % period,
                HalfSize = new Vector2(radius, radius),
                HopHeight = hopHeight,
            };
            trap.BuildDonutPath();
            trap.DonutSlots = trap._path.Length / period + 2;
            if (trap.DonutSlots > MaxDonuts)
            {
                throw new ArgumentException($"ドーナツフライヤーの周期 {period} が短すぎます（道のり {trap._path.Length} ティックの間に {MaxDonuts} 個より多く出る）");
            }
            return trap;
        }

        // ドーナツの道のり（出てからのティックごとの位置）: 出口から着地点へ順に、高いほうの点より HopHeight 上まで上がる放物線でつなぐ。
        private void BuildDonutPath()
        {
            var path = new List<Vector2>();
            var landingTicks = new int[_waypoints.Length];
            float dt = SimTime.DeltaTime;
            float g = DonutGravity;
            float time = 0f;
            float segmentStart = 0f;
            Vector2 from = Muzzle;
            for (int i = 0; i < _waypoints.Length; i++)
            {
                Vector2 to = _waypoints[i];
                float apex = MathF.Max(from.Y, to.Y) + HopHeight;
                float up = MathF.Sqrt(2f * (apex - from.Y) / g);
                float down = MathF.Sqrt(2f * (apex - to.Y) / g);
                float duration = up + down;
                float vx = (to.X - from.X) / duration;
                float vy = g * up;
                float end = segmentStart + duration;
                while (time < end)
                {
                    float t = time - segmentStart;
                    path.Add(new Vector2(from.X + vx * t, from.Y + vy * t - 0.5f * g * t * t));
                    time = path.Count * dt;
                }
                landingTicks[i] = path.Count;
                segmentStart = end;
                from = to;
            }
            path.Add(from);
            _path = path.ToArray();
            _landingTicks = landingTicks;
        }

        /// <summary>
        /// ドーナツフライヤーの、番号 slot のドーナツのティック tick の様子（出てからのティック age・位置・速度）。その番号のドーナツが
        /// なければ false。番号は出た順に 0〜DonutSlots-1 を使い回す（同じ番号のドーナツは同時に 2 つ出ていない）。
        /// </summary>
        public bool TryGetDonut(int tick, int slot, out int age, out Vector2 position, out Vector2 velocity)
        {
            age = 0;
            position = Muzzle;
            velocity = Vector2.Zero;
            if (Kind != StageTrapKind.DonutFryer || _path.Length == 0 || slot < 0 || slot >= DonutSlots)
            {
                return false;
            }
            long shifted = (long)tick + PhaseTicks;
            if (shifted < 0)
            {
                return false;
            }
            long latest = shifted / Period;
            long n = latest - (((latest - slot) % DonutSlots) + DonutSlots) % DonutSlots;
            // 1 つ目のドーナツは、ティック Period - PhaseTicks に出る（0 番目は出さない）。
            if (n < 1)
            {
                return false;
            }
            long elapsed = shifted - n * Period;
            if (elapsed >= _path.Length || tick - elapsed < 0)
            {
                return false;
            }
            age = (int)elapsed;
            position = _path[age];
            velocity = age > 0 ? (_path[age] - _path[age - 1]) * SimTime.TicksPerSecond : (_path.Length > 1 ? (_path[1] - _path[0]) * SimTime.TicksPerSecond : Vector2.Zero);
            return true;
        }

        /// <summary>
        /// 鬼火の輪の、ティック tick の中心（試合の種 seed で決まる道のり）。最初の地点は種で決まり、各地点で HoverTicks だけ漂ってから、
        /// 今の地点と違う地点（種と何本目の道のりかで決まる）へ、ゆっくり動き出して止まるように飛ぶ（平均の速さ Speed）。
        /// </summary>
        public Vector2 WispAt(int tick, int seed)
        {
            int n = _waypoints.Length;
            if (n == 0)
            {
                return Muzzle;
            }
            int current = (int)(StageRandom.Hash(seed, WispSalt, 0) % (uint)n);
            int t = Math.Max(0, tick);
            for (int leg = 0; ; leg++)
            {
                int next = (current + 1 + (int)(StageRandom.Hash(seed, WispSalt, leg + 1) % (uint)(n - 1))) % n;
                Vector2 from = _waypoints[current];
                if (t < HoverTicks)
                {
                    return from;
                }
                t -= HoverTicks;
                Vector2 to = _waypoints[next];
                int travel = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(from, to) / Speed * SimTime.TicksPerSecond));
                if (t < travel)
                {
                    float f = (float)t / travel;
                    f = f * f * (3f - 2f * f);
                    return from + (to - from) * f;
                }
                t -= travel;
                current = next;
            }
        }

        /// <summary>ティック taken にくぐられた鬼火の輪が、また現れるティック（RespawnMinTicks〜RespawnMaxTicks の後。種で決まる）。</summary>
        public int RespawnTickAfter(int taken, int seed)
        {
            int span = RespawnMaxTicks - RespawnMinTicks + 1;
            return taken + RespawnMinTicks + (int)(StageRandom.Hash(seed, WispSalt + 1, taken) % (uint)span);
        }

        private static bool Finite(Vector2 v)
        {
            return !float.IsNaN(v.X) && !float.IsNaN(v.Y) && !float.IsInfinity(v.X) && !float.IsInfinity(v.Y);
        }

        /// <summary>
        /// ボールの道のりを、動かない地形 terrain から求める（ピッチャーマシンだけ。SimWorld が作るときに呼ぶ。同じ地形なら、どの PC でも
        /// 同じ道のりになる）。重力で落ち、床で跳ね返り（BallRestitution。面に沿う向きは BallBounceFriction 倍）、天井と壁ではよく跳ね返り
        /// （BallWallRestitution・BallWallFriction）、跳ねなくなったら転がって（BallRollDeceleration）止まる。止まったら、その場で
        /// BlinkTicks だけ点滅する。飛んでいられる長さを超えたら、動き続けながら BlinkTicks だけ点滅する。
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
            bool rolling = false;
            bool fell = false;
            bool stopped = false;
            path.Add(position);
            for (int t = 1; t <= BallMaxFlightTicks; t++)
            {
                StepBall(terrain, half, ref position, ref velocity, ref rolling);
                path.Add(position);
                if (position.Y < -80f || MathF.Abs(position.X) > 160f)
                {
                    fell = true;
                    break;
                }
                if (rolling && MathF.Abs(velocity.X) < BallStopSpeed)
                {
                    stopped = true;
                    break;
                }
            }
            StopTick = path.Count - 1;
            for (int i = 0; i < BlinkTicks && !fell; i++)
            {
                // 止まったボールはその場で、止まらなかったボールは動き続けながら点滅する。
                if (!stopped)
                {
                    StepBall(terrain, half, ref position, ref velocity, ref rolling);
                    fell = position.Y < -80f || MathF.Abs(position.X) > 160f;
                }
                path.Add(position);
            }
            _path = path.ToArray();
        }

        /// <summary>
        /// ボールを 1 ティック進める（転がっている間は横だけ動いて遅くなり、下に地面がなければまた落ちる）。THIEF が置いたピッチャーマシンの
        /// ボールも、これで動かす（ThiefRules）。
        /// </summary>
        internal static void StepBall(ICollisionQuery terrain, Vector3 half, ref Vector2 position, ref Vector2 velocity, ref bool rolling)
        {
            float dt = SimTime.DeltaTime;
            if (rolling)
            {
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
                bool floor = normal.Y > 0.7f;
                if (into < 0f)
                {
                    Vector2 tangent = velocity - normal * into;
                    velocity = floor
                        ? tangent * BallBounceFriction - normal * into * BallRestitution
                        : tangent * BallWallFriction - normal * into * BallWallRestitution;
                }
                if (floor && MathF.Abs(velocity.Y) < BallSettleSpeed)
                {
                    velocity.Y = 0f;
                    rolling = true;
                }
            }
            else
            {
                position += delta;
            }
        }

        /// <summary>
        /// ピッチャーマシンのボールが、ティック tick にあるか（あれば、打ち出してからのティック age と位置と速度）。点滅している間は
        /// blinking。道のりを求めていなければ、ない。試合の始まりより前に打ち出したことになるボールもない。
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
            // 試合が始まる前に打ち出したことになるボールは、ない（最初のボールは Period - PhaseTicks のティックに打ち出す）。
            if (age >= _path.Length || tick - age < 0)
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
                && BlinkTicks == other.BlinkTicks && MinHitSpeed.Equals(other.MinHitSpeed) && SameWaypoints(other) && HoverTicks == other.HoverTicks
                && Reward == other.Reward && RespawnMinTicks == other.RespawnMinTicks && RespawnMaxTicks == other.RespawnMaxTicks
                && HopHeight.Equals(other.HopHeight);
        }

        private bool SameWaypoints(StageTrap other)
        {
            if (_waypoints.Length != other._waypoints.Length)
            {
                return false;
            }
            for (int i = 0; i < _waypoints.Length; i++)
            {
                if (!_waypoints[i].Equals(other._waypoints[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public override string ToString()
        {
            return Kind == StageTrapKind.Tripwire ? $"Tripwire {BeamMin}〜{BeamMax} from {Muzzle} dir {Direction}"
                : Kind == StageTrapKind.WispRing ? $"WispRing {_waypoints.Length} points from {Muzzle}"
                : Kind == StageTrapKind.DonutFryer ? $"DonutFryer {_waypoints.Length} landings from {Muzzle} every {Period}"
                : $"Pitcher at {Muzzle} v {LaunchVelocity}";
        }
    }
}
