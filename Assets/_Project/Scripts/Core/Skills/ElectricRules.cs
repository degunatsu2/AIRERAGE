using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;

namespace Airerage.Core.Skills
{
    /// <summary>
    /// SOLDERER の電気の技（ユーザーの案、2026-10-21。docs/13 3.17、docs/14 1.5）。SkillRules から呼ぶ。
    /// - E インバータ（InverterTuning）: ON・OFF。ON の間はワイヤーの射程が短くなり（SpeedBoosts）、外れたワイヤーを押したままなら鞭
    ///   （WireController。当たりは StepWhips）、つながる地形につなぐと雷の波動（SpawnWaves、StepShockWave）。
    /// - R ボールライトニング（BallLightningTuning）: 照準の方向へゆっくり進み、跳ね返り、止まって罠になる球（StepBall）。
    /// 雷の波動と球は、触れた相手（持ち主以外）を感電させる（Stun。その場にとどまり、動けない）。
    /// </summary>
    public static class ElectricRules
    {
        // 雷の波動が始まる点を、地形の箱の縁とみなす距離（m）。
        private const float SurfaceTolerance = 0.08f;

        /// <summary>E: インバータを切り替える。</summary>
        public static void ToggleInverter(List<CharacterState> states, int index)
        {
            CharacterState state = states[index];
            state.Combat.InverterOn = !state.Combat.InverterOn;
            if (!state.Combat.InverterOn && state.Wire.Whip)
            {
                // 切ったら鞭もやめる（回収する）。
                state.Wire.Whip = false;
                state.Wire.PhaseTicks = 0;
            }
            states[index] = state;
        }

        /// <summary>R: ボールライトニングを用意する（IntervalTicks ごとに、そのときの照準の方向へ放つ。放つまでは持ち主についていく）。</summary>
        public static void FireBallLightning(WorldObjects objects, in CharacterState state, int index, CharacterConfig config, int tick)
        {
            BallLightningTuning tuning = config.Skills.BallLightning;
            for (int k = 0; k < tuning.Count; k++)
            {
                int launch = tick + k * tuning.IntervalTicks;
                objects.Add(new WorldObject
                {
                    Kind = WorldObjectKind.BallLightning, Owner = index, Slot = state.Action.Attack,
                    Position = new Vector2(state.Position.X, state.Position.Y), SpawnTick = launch, ExpireTick = int.MaxValue,
                });
            }
        }

        /// <summary>ボールライトニングが止まって罠になっているか。</summary>
        public static bool IsResting(in WorldObject ball)
        {
            return ball.Stored == 2;
        }

        /// <summary>
        /// ボールライトニング: 放つまでは持ち主についていく。放ったら、そのときの照準（CombatState.SkillAim）の方向へ進み、地形で跳ね返る。
        /// TravelDistance 進むと止まって TrapTicks の間残る。触れた相手を感電させる。
        /// </summary>
        public static void StepBall(ref WorldObject ball, ICollisionQuery terrain, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            StageBounds? bounds, int tick)
        {
            CharacterConfig ownerConfig = configs[ball.Owner];
            BallLightningTuning tuning = ownerConfig.Skills.BallLightning;
            if (ball.Stored == 0)
            {
                CharacterState owner = states[ball.Owner];
                Vector2 direction = SkillRules.AimDirection(owner.Combat.SkillAim, owner.Facing, ownerConfig.Wire.AimDeadzone);
                ball.Position = new Vector2(owner.Position.X, owner.Position.Y) + direction * tuning.LaunchOffset;
                if (tick < ball.SpawnTick)
                {
                    return;
                }
                ball.Velocity = direction * tuning.Speed;
                ball.Stored = 1;
            }
            if (ball.Stored == 1)
            {
                Move(ref ball, terrain, tuning, tick);
                if (bounds.HasValue && !bounds.Value.Contains(new Vector3(ball.Position.X, ball.Position.Y, 0f)))
                {
                    ball.Ended = true;
                    return;
                }
            }
            for (int v = 0; v < states.Count; v++)
            {
                if (v == ball.Owner || (ball.HitMask & (1u << v)) != 0)
                {
                    continue;
                }
                CharacterState target = states[v];
                Vector3 half = configs[v].Movement.HalfExtents;
                if (MathF.Abs(target.Position.X - ball.Position.X) < half.X + tuning.Radius
                    && MathF.Abs(target.Position.Y - ball.Position.Y) < half.Y + tuning.Radius
                    && Stun(ref target, configs[v], ownerConfig.Skills.Inverter))
                {
                    states[v] = target;
                    ball.HitMask |= 1u << v;
                }
            }
        }

        // 進む（地形で跳ね返る）。進んだ距離が TravelDistance に着いたら止まる。
        private static void Move(ref WorldObject ball, ICollisionQuery terrain, BallLightningTuning tuning, int tick)
        {
            float step = tuning.Speed * SimTime.DeltaTime;
            float left = tuning.TravelDistance - ball.ChargeMilli / 1000f;
            if (step >= left)
            {
                step = Math.Max(0f, left);
            }
            float speed = ball.Velocity.Length();
            if (speed > SimMath.Epsilon && step > 0f)
            {
                Vector2 direction = ball.Velocity / speed;
                var origin = new Vector3(ball.Position.X, ball.Position.Y, 0f);
                if (terrain.Raycast(origin, new Vector3(direction.X, direction.Y, 0f), step + tuning.Radius * 0.5f, out CastHit wall))
                {
                    // 壁の手前まで進み、跳ね返る。
                    float travel = Math.Max(0f, wall.Distance - tuning.Radius * 0.5f);
                    ball.Position += direction * travel;
                    var normal = new Vector2(wall.Normal.X, wall.Normal.Y);
                    ball.Velocity -= 2f * Vector2.Dot(ball.Velocity, normal) * normal;
                    // 距離は、進んだ時間の分を数える（角で跳ね返り続けても、いつかは止まる）。
                    ball.ChargeMilli += (long)(step * 1000f);
                }
                else
                {
                    ball.Position += direction * step;
                    ball.ChargeMilli += (long)(step * 1000f);
                }
            }
            if (ball.ChargeMilli >= (long)(tuning.TravelDistance * 1000f) - 1)
            {
                ball.Velocity = Vector2.Zero;
                ball.Stored = 2;
                ball.ExpireTick = tick + tuning.TrapTicks;
            }
        }

        /// <summary>
        /// target を感電させる（その場にとどまり、動けない）。干渉されない（復帰の後）・感電しない間（StunGuardTicks）なら何もしない。
        /// 感電させたら true。
        /// </summary>
        public static bool Stun(ref CharacterState target, CharacterConfig targetConfig, InverterTuning tuning)
        {
            if (target.Combat.IsUntouchable || target.Combat.StunGuardTicks > 0 || tuning.StunTicks <= 0)
            {
                return false;
            }
            target.Combat.StunTicksLeft = tuning.StunTicks;
            target.Combat.StunGuardTicks = tuning.StunTicks + tuning.StunGuardTicks;
            target.Combat.StunCount++;
            target.Combat.Holding = false;
            target.Combat.HoldTicks = 0;
            if (target.Action.Kind == ActionKind.Attack || target.Action.Kind == ActionKind.Guard)
            {
                target.Action = default;
            }
            target.Velocity = Vector3.Zero;
            target.WallSide = 0;
            WireController.Interrupt(ref target, targetConfig.Wire);
            return true;
        }

        /// <summary>
        /// インバータの間にワイヤーをつないだ人がいれば（つないだティックだけ）、つないだ地形の箱の縁を走る雷の波動を出す（地形が箱で
        /// なければ出さない）。
        /// </summary>
        public static void SpawnWaves(WorldObjects objects, ICollisionQuery terrain, List<CharacterState> states, int tick)
        {
            if (!(terrain is BoxTerrain boxes))
            {
                return;
            }
            for (int i = 0; i < states.Count; i++)
            {
                CharacterState state = states[i];
                if (!state.Combat.InverterOn || state.Wire.Phase != WirePhase.Attached || state.Wire.PhaseTicks != 0)
                {
                    continue;
                }
                var point = new Vector2(state.Wire.Anchor.X, state.Wire.Anchor.Y);
                if (!TryFindBox(boxes, point, out Vector2 min, out Vector2 max))
                {
                    continue;
                }
                objects.Add(new WorldObject
                {
                    Kind = WorldObjectKind.ShockWave, Owner = i, Slot = AttackSlot.Special2, Position = ClosestOnEdge(point, min, max),
                    Velocity = min, End = max, SpawnTick = tick, ExpireTick = int.MaxValue,
                });
            }
        }

        // 点 point が縁にある地形の箱。
        private static bool TryFindBox(BoxTerrain terrain, Vector2 point, out Vector2 min, out Vector2 max)
        {
            foreach (BoxTerrain.Box box in terrain.Boxes)
            {
                if (point.X >= box.Min.X - SurfaceTolerance && point.X <= box.Max.X + SurfaceTolerance
                    && point.Y >= box.Min.Y - SurfaceTolerance && point.Y <= box.Max.Y + SurfaceTolerance)
                {
                    min = box.Min;
                    max = box.Max;
                    return true;
                }
            }
            min = max = default;
            return false;
        }

        /// <summary>雷の波動の、始まりから今までに進んだ距離（m）。</summary>
        public static float WaveTravel(in WorldObject wave, InverterTuning tuning, int tick)
        {
            return Math.Max(0, tick - wave.SpawnTick) * SimTime.DeltaTime * tuning.WaveSpeed;
        }

        /// <summary>雷の波動の箱の縁の長さ（m）。</summary>
        public static float Perimeter(in WorldObject wave)
        {
            return 2f * ((wave.End.X - wave.Velocity.X) + (wave.End.Y - wave.Velocity.Y));
        }

        /// <summary>雷の波動の、始まりの点から縁に沿って distance（m。負なら逆回り）進んだ点。</summary>
        public static Vector2 PointAlong(in WorldObject wave, float distance)
        {
            Vector2 min = wave.Velocity;
            Vector2 max = wave.End;
            float perimeter = Perimeter(wave);
            if (perimeter <= SimMath.Epsilon)
            {
                return wave.Position;
            }
            float s = (EdgeParameter(wave.Position, min, max) + distance) % perimeter;
            if (s < 0f)
            {
                s += perimeter;
            }
            return EdgePoint(s, min, max);
        }

        /// <summary>
        /// 雷の波動: 箱の縁を両回りに進み、縁の半分を進んだら消える。進む先の 2 つの点の近くにいる相手（持ち主以外）を感電させる
        /// （同じ相手には 1 回）。
        /// </summary>
        public static void StepShockWave(ref WorldObject wave, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, int tick)
        {
            InverterTuning tuning = configs[wave.Owner].Skills.Inverter;
            float travel = WaveTravel(wave, tuning, tick);
            if (travel >= Perimeter(wave) * 0.5f)
            {
                wave.Ended = true;
                return;
            }
            Vector2 forward = PointAlong(wave, travel);
            Vector2 backward = PointAlong(wave, -travel);
            for (int v = 0; v < states.Count; v++)
            {
                if (v == wave.Owner || (wave.HitMask & (1u << v)) != 0)
                {
                    continue;
                }
                CharacterState target = states[v];
                Vector3 half = configs[v].Movement.HalfExtents;
                if ((Near(target.Position, half, forward, tuning.WaveRadius) || Near(target.Position, half, backward, tuning.WaveRadius))
                    && Stun(ref target, configs[v], tuning))
                {
                    states[v] = target;
                    wave.HitMask |= 1u << v;
                }
            }
        }

        private static bool Near(Vector3 position, Vector3 half, Vector2 point, float radius)
        {
            return MathF.Abs(position.X - point.X) <= half.X + radius && MathF.Abs(position.Y - point.Y) <= half.Y + radius;
        }

        // 箱の縁の、左下の角から左回り（下の辺 → 右の辺 → 上の辺 → 左の辺）に測った位置。
        private static float EdgeParameter(Vector2 p, Vector2 min, Vector2 max)
        {
            float w = max.X - min.X;
            float h = max.Y - min.Y;
            float toBottom = MathF.Abs(p.Y - min.Y);
            float toRight = MathF.Abs(p.X - max.X);
            float toTop = MathF.Abs(p.Y - max.Y);
            float toLeft = MathF.Abs(p.X - min.X);
            float nearest = MathF.Min(MathF.Min(toBottom, toRight), MathF.Min(toTop, toLeft));
            if (nearest == toBottom)
            {
                return Clamp(p.X - min.X, 0f, w);
            }
            if (nearest == toRight)
            {
                return w + Clamp(p.Y - min.Y, 0f, h);
            }
            if (nearest == toTop)
            {
                return w + h + Clamp(max.X - p.X, 0f, w);
            }
            return 2f * w + h + Clamp(max.Y - p.Y, 0f, h);
        }

        private static Vector2 EdgePoint(float s, Vector2 min, Vector2 max)
        {
            float w = max.X - min.X;
            float h = max.Y - min.Y;
            if (s < w)
            {
                return new Vector2(min.X + s, min.Y);
            }
            s -= w;
            if (s < h)
            {
                return new Vector2(max.X, min.Y + s);
            }
            s -= h;
            if (s < w)
            {
                return new Vector2(max.X - s, max.Y);
            }
            s -= w;
            return new Vector2(min.X, max.Y - Math.Min(s, h));
        }

        private static Vector2 ClosestOnEdge(Vector2 p, Vector2 min, Vector2 max)
        {
            return EdgePoint(EdgeParameter(p, min, max), min, max);
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : value > max ? max : value;
        }

        /// <summary>
        /// 鞭（インバータの間に外れて、押したままのワイヤー）: 持ち主から鞭の先までの線に触れた相手（持ち主以外）に、鞭の判定を当てる
        /// （hits に加える。同じ相手には WhipRehitTicks ごと）。鞭の先の位置も、移動の後の位置に合わせる。
        /// </summary>
        public static void StepWhips(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, List<HitRecord> hits)
        {
            for (int a = 0; a < states.Count; a++)
            {
                CharacterState owner = states[a];
                if (!owner.Wire.Whip || owner.Wire.Phase != WirePhase.Retracting || owner.Combat.IsUntouchable)
                {
                    continue;
                }
                CharacterConfig effective = SpeedBoosts.EffectiveConfig(owner, configs[a]);
                owner.Wire.HookPosition = owner.Position + owner.Wire.HookDirection * effective.Wire.MaxLength;
                states[a] = owner;
                InverterTuning tuning = configs[a].Skills.Inverter;
                var from = new Vector2(owner.Position.X, owner.Position.Y);
                var to = new Vector2(owner.Wire.HookPosition.X, owner.Wire.HookPosition.Y);
                for (int v = 0; v < states.Count; v++)
                {
                    CharacterState target = states[v];
                    if (v == a || target.Combat.IsUntouchable || target.Combat.WhipGuardTicks > 0)
                    {
                        continue;
                    }
                    Vector3 half = configs[v].Movement.HalfExtents;
                    if (!SegmentTouchesBox(from, to, new Vector2(target.Position.X, target.Position.Y),
                            new Vector2(half.X + tuning.WhipHalfThickness, half.Y + tuning.WhipHalfThickness)))
                    {
                        continue;
                    }
                    target.Combat.WhipGuardTicks = tuning.WhipRehitTicks;
                    states[v] = target;
                    int facing = to.X >= from.X ? 1 : -1;
                    hits.Add(new HitRecord(a, v, AttackSlot.Special2, 0, tuning.WhipHit, facing, AttackPower.Normal, fromObject: true));
                }
            }
        }

        /// <summary>線分 from–to が、中心 center・半分の大きさ half の軸平行の箱に触れるか。</summary>
        public static bool SegmentTouchesBox(Vector2 from, Vector2 to, Vector2 center, Vector2 half)
        {
            Vector2 d = to - from;
            float enter = 0f;
            float exit = 1f;
            return Slab(from.X - center.X, d.X, half.X, ref enter, ref exit) && Slab(from.Y - center.Y, d.Y, half.Y, ref enter, ref exit);
        }

        private static bool Slab(float o, float d, float half, ref float enter, ref float exit)
        {
            if (MathF.Abs(d) < 1e-6f)
            {
                return MathF.Abs(o) <= half;
            }
            float t1 = (-half - o) / d;
            float t2 = (half - o) / d;
            enter = MathF.Max(enter, MathF.Min(t1, t2));
            exit = MathF.Min(exit, MathF.Max(t1, t2));
            return enter <= exit;
        }
    }
}
