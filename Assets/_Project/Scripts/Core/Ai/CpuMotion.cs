using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;

namespace Airerage.Core.Ai
{
    /// <summary>
    /// CPU の動きで覚えていること（目標・ワイヤーをつないだ時刻・行き詰まりの判定など）。値の型なので、THIEF の分身のように
    /// シミュレーションの中で動かすものは、状態と一緒に保存して巻き戻せる（SimWorld の分身の頭）。
    /// </summary>
    public struct CpuMotionMemory : IEquatable<CpuMotionMemory>
    {
        public CpuGoalKind Goal;
        public Vector2 Target;
        public int TargetCharacter;
        public int NextDecisionTick;
        public InputButtons LastButtons;
        public int WireAttachedTick;
        public int WireReadyTick;
        public Vector3 StuckFrom;
        public int StuckSinceTick;
        public int WanderUntilTick;

        /// <summary>何も覚えていない状態（攻める相手・ワイヤーをつないだ時刻・歩き回る終わりはなし）。</summary>
        public static CpuMotionMemory Initial => new CpuMotionMemory { TargetCharacter = -1, WireAttachedTick = -1, WanderUntilTick = -1 };

        /// <summary>position で、ティック tick に動き始める（行き詰まりはそこから数える）。</summary>
        public static CpuMotionMemory StartingAt(Vector3 position, int tick)
        {
            CpuMotionMemory memory = Initial;
            memory.StuckFrom = position;
            memory.StuckSinceTick = tick;
            memory.NextDecisionTick = tick;
            return memory;
        }

        public bool Equals(CpuMotionMemory other)
        {
            return Goal == other.Goal && Target.Equals(other.Target) && TargetCharacter == other.TargetCharacter
                && NextDecisionTick == other.NextDecisionTick && LastButtons == other.LastButtons && WireAttachedTick == other.WireAttachedTick
                && WireReadyTick == other.WireReadyTick && StuckFrom.Equals(other.StuckFrom) && StuckSinceTick == other.StuckSinceTick
                && WanderUntilTick == other.WanderUntilTick;
        }

        public override bool Equals(object obj)
        {
            return obj is CpuMotionMemory other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Goal, Target, TargetCharacter, NextDecisionTick, WireAttachedTick, StuckSinceTick, WanderUntilTick);
        }
    }

    /// <summary>
    /// CPU の動き（docs/16 T7-1）: 目標を選び（コイン・拠点・相手。落ちそうなら戻る、行き詰まったら歩き回る）、目標へ走る・跳ぶ・空中ジャンプ・
    /// ワイヤー（つながる所を WireTargeting.PredictShot で確かめて撃ち、目標の方へ振れたら離す）。覚えていることは CpuMotionMemory に持つので、
    /// CPU（CpuPlayer）と、シミュレーションの中で動く THIEF の分身（EASY の動き。ThiefRules）で同じ考え方を使う。
    /// 乱数は key（CPU ごとの値）とティックから作るので、同じ入力の並びなら何度でも同じ動きになる。
    /// </summary>
    public sealed class CpuMotion
    {
        // 目標に着いたとみなす距離（m）。
        private const float ArriveDistance = 0.8f;

        // 撃破の範囲の端から、この距離より外側に出たら戻る（m）。
        private const float EdgeMargin = 5f;

        // ワイヤーにつないでいる最大のティック（振れ切らないうちでも離す）。
        private const int WireHoldMaxTicks = 80;

        // この間（ティック）に 1m も動けなければ、行き詰まったとみなして歩き回る。
        private const int StuckTicks = 120;
        private const int WanderTicks = 90;

        // ワイヤーを離してから、次に撃つまでの間（ティック。戻るときは待たない。撃ちすぎてせわしなく見えないように）。
        private const int WireGapTicks = 15;

        // ワイヤーを試す方向（目標の方向から、上へ回す角度。度）。
        private static readonly float[] WireLifts = { 25f, 45f, 65f, 80f, 0f };

        private readonly CpuTuning _tuning;
        private readonly List<Vector2> _candidates = new List<Vector2>();

        public CpuMotion(CpuTuning tuning)
        {
            _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        }

        public CpuTuning Tuning => _tuning;

        /// <summary>
        /// 目標を選ぶ。落ちそうなら戻る。行き詰まっていたら歩き回る。それ以外は、コイン・拠点・相手のうち点数の高いもの。
        /// self は動かすものの状態、selfIndex は狙わない番号（CPU 自身か、分身の持ち主）。
        /// </summary>
        public void DecideGoal(SimWorld world, in CharacterState self, int selfIndex, int key, int tick, ref CpuMotionMemory m)
        {
            Vector2 position = Flat(self.Position);
            if (Vector3.Distance(self.Position, m.StuckFrom) > 1f)
            {
                m.StuckFrom = self.Position;
                m.StuckSinceTick = tick;
            }
            else if (tick - m.StuckSinceTick > StuckTicks && m.Goal != CpuGoalKind.Idle)
            {
                m.WanderUntilTick = tick + WanderTicks;
                m.StuckSinceTick = tick;
            }

            m.TargetCharacter = -1;
            if (world.Bounds.HasValue && (InDanger(world.Bounds.Value, position) || OverVoid(world, self)))
            {
                m.Goal = CpuGoalKind.Recover;
                m.Target = SafePoint(world, position);
                return;
            }
            if (tick < m.WanderUntilTick)
            {
                m.Goal = CpuGoalKind.Idle;
                m.Target = WanderPoint(world, position, key, tick);
                return;
            }

            float best = 0f;
            m.Goal = CpuGoalKind.Idle;
            m.Target = position;

            // 拠点: 持っているコインが多い（アディショナルタイムなら少しでも）。
            IReadOnlyList<BankSpot> banks = world.BankSpots;
            bool overtime = world.Phase == MatchPhase.Overtime;
            if (banks.Count > 0 && (self.Coins.Held >= _tuning.BankAtHeldCoins || (overtime && self.Coins.Held > 0)))
            {
                int spot = BankRules.SpotAt(world.Rules, banks.Count, tick);
                if (spot >= 0)
                {
                    float score = 3f * Jitter(key, tick, 1);
                    if (score > best)
                    {
                        best = score;
                        m.Goal = CpuGoalKind.Bank;
                        m.Target = banks[spot].Position;
                    }
                }
            }

            // コイン: 価値 ÷ 距離。
            IReadOnlyList<FieldCoin> coins = world.Coins.Field.Coins;
            for (int i = 0; i < coins.Count; i++)
            {
                FieldCoin coin = coins[i];
                if (coin.PickableFromTick > tick)
                {
                    continue;
                }
                float distance = Vector2.Distance(position, coin.Position);
                float score = 4f * MathF.Sqrt(coin.Value) / (distance + 6f) * Jitter(key, tick, 10 + i);
                if (score > best)
                {
                    best = score;
                    m.Goal = CpuGoalKind.Coin;
                    m.Target = coin.Position;
                }
            }

            // 相手: 攻める欲しさ ÷ 距離。コインを多く持っている相手ほど狙う。
            for (int other = 0; other < world.CharacterCount; other++)
            {
                if (other == selfIndex)
                {
                    continue;
                }
                CharacterState opponent = world.GetState(other);
                if (opponent.Combat.IsUntouchable || IsHidden(opponent))
                {
                    continue;
                }
                float distance = Vector2.Distance(position, Flat(opponent.Position));
                float score = _tuning.Aggression * 6f / (distance + 6f) * (1f + opponent.Coins.Held / 40f) * Jitter(key, tick, 100 + other);
                if (score > best)
                {
                    best = score;
                    m.Goal = CpuGoalKind.Fight;
                    m.Target = Flat(opponent.Position);
                    m.TargetCharacter = other;
                }
            }
        }

        /// <summary>攻めている相手を追う（目的地を相手の今の位置に。相手が触れない・見えないなら、次のティックで目標を選び直す）。</summary>
        public static void FollowTarget(SimWorld world, int tick, ref CpuMotionMemory m)
        {
            if (m.Goal == CpuGoalKind.Fight && m.TargetCharacter >= 0)
            {
                CharacterState opponent = world.GetState(m.TargetCharacter);
                if (opponent.Combat.IsUntouchable || IsHidden(opponent))
                {
                    m.NextDecisionTick = tick;
                }
                m.Target = Flat(opponent.Position);
            }
        }

        /// <summary>目標へ動く: 走る・跳ぶ・空中ジャンプ・ワイヤー。ワイヤーを撃ったら true。</summary>
        public bool Steer(SimWorld world, in CharacterState self, CharacterConfig config, int key, int tick, ref CpuMotionMemory m, ref float moveX,
            ref float moveY, ref InputButtons buttons, ref Vector2 aim)
        {
            bool fired = false;
            Vector2 position = Flat(self.Position);
            Vector2 toTarget = m.Target - position;
            bool fighting = m.Goal == CpuGoalKind.Fight;
            // 攻める相手とは、少し間をあける（重なると判定が外れやすい）。
            float stopX = fighting ? 1.2f : ArriveDistance;
            if (MathF.Abs(toTarget.X) > stopX)
            {
                moveX = MathF.Sign(toTarget.X);
            }

            bool blocked = moveX != 0f && Blocked(world.Collision, self, config, moveX);
            bool wantUp = toTarget.Y > 1.2f || blocked;
            WireState wire = self.Wire;

            // 跳ぶ: 地上で上へ行きたい・前がふさがっている。空中で落ち始めたら空中ジャンプ。
            bool jumpHeldLastTick = (m.LastButtons & InputButtons.Jump) != 0;
            if (!jumpHeldLastTick && wantUp && wire.Phase != WirePhase.Attached)
            {
                bool canAirJump = self.AirJumpsUsed < config.Movement.AirJumps && self.Velocity.Y < 2f;
                if (self.Grounded || self.WallSide != 0 || canAirJump)
                {
                    buttons |= InputButtons.Jump;
                }
            }

            switch (wire.Phase)
            {
                case WirePhase.Ready:
                    bool needWire = m.Goal == CpuGoalKind.Recover
                        || (toTarget.Y > 3f && !self.Grounded)
                        || (toTarget.Y > 4f)
                        || (MathF.Abs(toTarget.X) > 9f && !self.Grounded);
                    bool wireHeldLastTick = (m.LastButtons & InputButtons.Wire) != 0;
                    bool waited = tick >= m.WireReadyTick || m.Goal == CpuGoalKind.Recover;
                    if (needWire && waited && !wireHeldLastTick && TryAimWire(world, self, config.Wire, key, tick, m.Target, out Vector2 direction))
                    {
                        buttons |= InputButtons.Wire;
                        aim = direction;
                        fired = true;
                    }
                    m.WireAttachedTick = -1;
                    break;

                case WirePhase.Firing:
                    buttons |= InputButtons.Wire;
                    break;

                case WirePhase.Attached:
                    if (m.WireAttachedTick < 0)
                    {
                        m.WireAttachedTick = tick;
                    }
                    if (!ShouldRelease(self, toTarget, tick, m))
                    {
                        buttons |= InputButtons.Wire;
                        // 目標が上なら巻き取る（D-50: 上で巻き取る）。
                        moveY = toTarget.Y > 1f ? 1f : 0f;
                    }
                    else
                    {
                        m.WireReadyTick = tick + WireGapTicks;
                    }
                    break;
            }
            return fired;
        }

        // ワイヤーを撃つ方向を選ぶ: 上にあるアンカーと、目標の方向から上へ回した方向のうち、つながって目標に近づくもの。
        private bool TryAimWire(SimWorld world, in CharacterState self, WireTuning tuning, int key, int tick, Vector2 target, out Vector2 direction)
        {
            direction = Vector2.Zero;
            Vector2 position = Flat(self.Position);
            Vector2 toTarget = target - position;
            float side = toTarget.X != 0f ? MathF.Sign(toTarget.X) : self.Facing;
            _candidates.Clear();
            foreach (Vector3 anchor in world.Collision.AnchorPoints)
            {
                Vector2 point = Flat(anchor);
                if (point.Y > position.Y + 0.5f && Vector2.Distance(point, position) < tuning.MaxLength * 0.95f)
                {
                    _candidates.Add(point - position);
                }
            }
            float baseAngle = MathF.Atan2(MathF.Max(toTarget.Y, 0f), side * MathF.Max(MathF.Abs(toTarget.X), 0.01f));
            foreach (float lift in WireLifts)
            {
                float angle = side > 0f ? baseAngle + lift * MathF.PI / 180f : baseAngle - lift * MathF.PI / 180f;
                if (lift >= 80f)
                {
                    angle = MathF.PI / 2f;
                }
                _candidates.Add(new Vector2(MathF.Cos(angle), MathF.Sin(angle)));
            }

            float best = float.MaxValue;
            var origin = self.Position;
            foreach (Vector2 candidate in _candidates)
            {
                if (candidate.LengthSquared() < 1e-6f)
                {
                    continue;
                }
                Vector2 unit = Vector2.Normalize(candidate);
                WireShotPreview preview = WireTargeting.PredictShot(origin, unit, self.Facing, tuning, world.Collision);
                if (preview.Result != WireShotResult.Attach || preview.EndPoint.Y < origin.Y - 0.5f)
                {
                    continue;
                }
                Vector2 end = Flat(preview.EndPoint);
                // 目標に近く、高い所ほどよい（振って届くように）。
                float score = Vector2.Distance(end, target) - 0.3f * (end.Y - position.Y);
                if (score < best)
                {
                    best = score;
                    direction = unit;
                }
            }
            if (best == float.MaxValue)
            {
                return false;
            }
            // 狙いのずれ（強さ）。
            float error = (Random(key, tick, 300) * 2f - 1f) * _tuning.AimErrorDegrees * MathF.PI / 180f;
            float cos = MathF.Cos(error);
            float sin = MathF.Sin(error);
            direction = new Vector2(direction.X * cos - direction.Y * sin, direction.X * sin + direction.Y * cos);
            return true;
        }

        // つないだワイヤーを離すか: 長くつなぎすぎた、目標に着いた、アンカーの高さまで上がった、目標の方へ振れて上がっている。
        private static bool ShouldRelease(in CharacterState self, Vector2 toTarget, int tick, in CpuMotionMemory m)
        {
            if (tick - m.WireAttachedTick > WireHoldMaxTicks || toTarget.Length() < 2f)
            {
                return true;
            }
            Vector3 anchor = self.Wire.Anchor;
            if (self.Position.Y > anchor.Y - 1.2f)
            {
                return true;
            }
            float side = MathF.Sign(toTarget.X);
            bool pastAnchor = MathF.Sign(self.Position.X - anchor.X) == side && MathF.Abs(self.Position.X - anchor.X) > 1.5f;
            bool swingingToward = MathF.Sign(self.Velocity.X) == side && self.Velocity.Y > 1f;
            return m.Goal != CpuGoalKind.Recover && pastAnchor && swingingToward;
        }

        // 進む方向の目の前（体の高さ）が地形でふさがっているか。
        private static bool Blocked(ICollisionQuery collision, in CharacterState self, CharacterConfig config, float side)
        {
            var origin = new Vector3(self.Position.X, self.Position.Y - config.Movement.HalfHeight * 0.4f, 0f);
            float reach = config.Movement.HalfWidth + 0.9f;
            return collision.Raycast(origin, new Vector3(side, 0f, 0f), reach, out _);
        }

        private static bool InDanger(StageBounds bounds, Vector2 position)
        {
            return position.Y < bounds.MinY + EdgeMargin || position.X < bounds.MinX + EdgeMargin || position.X > bounds.MaxX - EdgeMargin;
        }

        /// <summary>空中で、真下に何もない（このまま落ちるとステージの外へ出る）。</summary>
        internal static bool OverVoid(SimWorld world, in CharacterState self)
        {
            if (self.Grounded || self.Wire.Phase == WirePhase.Attached)
            {
                return false;
            }
            float depth = self.Position.Y - world.Bounds.Value.MinY;
            return depth > 0f && !world.Collision.Raycast(self.Position, new Vector3(0f, -1f, 0f), depth, out _);
        }

        // 戻る先: ステージの内側で、今より高い所（アンカーがあれば、中央に近いもの）。
        private static Vector2 SafePoint(SimWorld world, Vector2 position)
        {
            StageBounds bounds = world.Bounds.Value;
            float centerX = (bounds.MinX + bounds.MaxX) * 0.5f;
            Vector2 best = new Vector2(centerX, Math.Max(position.Y + 6f, bounds.MinY + EdgeMargin + 6f));
            float bestScore = float.MaxValue;
            foreach (Vector3 anchor in world.Collision.AnchorPoints)
            {
                if (anchor.Y < position.Y)
                {
                    continue;
                }
                float score = MathF.Abs(anchor.X - centerX) + Vector2.Distance(Flat(anchor), position) * 0.5f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = Flat(anchor);
                }
            }
            return best;
        }

        // 行き詰まったときの行き先（ステージの内側の、ランダムな少し高い所）。
        private static Vector2 WanderPoint(SimWorld world, Vector2 position, int key, int tick)
        {
            float x = position.X + (Random(key, tick, 700) * 2f - 1f) * 10f;
            float y = position.Y + 4f;
            if (world.Bounds.HasValue)
            {
                StageBounds bounds = world.Bounds.Value;
                x = Math.Clamp(x, bounds.MinX + EdgeMargin + 2f, bounds.MaxX - EdgeMargin - 2f);
            }
            return new Vector2(x, y);
        }

        /// <summary>透明の相手（THIEF の E）は見えないので、ねらわない。</summary>
        internal static bool IsHidden(in CharacterState opponent)
        {
            return opponent.Combat.InvisibleTicksLeft > 0;
        }

        /// <summary>0〜1 の乱数（key・目的・ティックから決まる）。</summary>
        internal static float Random(int key, int tick, int purpose)
        {
            return StableHash.Range(key, purpose, tick, 10000) / 10000f;
        }

        // 点数のゆらぎ（0.85〜1.15 倍。CPU どうしが同じ動きにならないように）。
        private float Jitter(int key, int tick, int purpose)
        {
            return 0.85f + 0.3f * Random(key, tick / Math.Max(1, _tuning.DecideEveryTicks), purpose);
        }

        internal static Vector2 Flat(Vector3 value)
        {
            return new Vector2(value.X, value.Y);
        }
    }
}
