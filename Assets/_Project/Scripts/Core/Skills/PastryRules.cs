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
    /// PASTRY CHEF の技（ユーザーの案、2026-10-21。docs/13 3.15、docs/14 1.4）。SkillRules から呼ぶ。
    /// - Q フラッフィベイス（CottonCandyTuning）: わたがしの足場。触れた人を弾き飛ばす。
    /// - E スイートトラップ（CreamTuning）: クリームを扇形に飛ばし、地形に張り付かせる。触れた人の速さを変える（TouchCream）。
    /// - R ティーパーティ（TeaPartyTuning）: マカロンを次々と投げ上げる。跳ね返り、相手に当たる。
    /// - 空中下（撃破技）デジタルスケール（ScaleTuning）: 真下へ自由落下し、落ちていた時間で強くなる（2026-10-21）。
    /// - 空中上 ボウル（AttackData.BlocksProjectilesAbove）: 上から来た投射物を防ぐ（TryBlockFromAbove。2026-10-21）。
    /// 置く位置・飛ばす向きは、表示（長押しの間の予告）でも同じ計算を使う（CandyPlacement、CreamLaunch）。
    /// </summary>
    public static class PastryRules
    {
        // クリームに触れたとみなす余裕（m）。
        private const float CreamContactMargin = 0.06f;

        // 地形にぶつかったマカロンを面から離す距離（m）。
        private const float MacaronSkin = 0.01f;

        /// <summary>
        /// わたがしの足場を置く中心と向き（表の向き。長さ 1）。charge はためた長さ（ティック）: 短ければ足元に上向き、長ければ
        /// 照準 aim の方向を向けて目の前に。
        /// </summary>
        public static void CandyPlacement(in CharacterState state, CharacterConfig config, int charge, Vector2 aim, out Vector2 center,
            out Vector2 normal)
        {
            CottonCandyTuning tuning = config.Skills.CottonCandy;
            var position = new Vector2(state.Position.X, state.Position.Y);
            if (charge < tuning.ShortPressTicks)
            {
                normal = Vector2.UnitY;
                center = position - new Vector2(0f, config.Movement.HalfExtents.Y + tuning.FeetOffset);
                return;
            }
            normal = SkillRules.AimDirection(aim, state.Facing, config.Wire.AimDeadzone);
            center = position + normal * tuning.PlaceDistance;
        }

        /// <summary>k 番目（0 から）のクリームを飛ばす位置と速度（照準 aim の方向を中心に扇形。真ん中ほど密）。</summary>
        public static void CreamLaunch(in CharacterState state, CharacterConfig config, Vector2 aim, int k, out Vector2 origin,
            out Vector2 velocity)
        {
            CreamTuning tuning = config.Skills.Cream;
            Vector2 direction = SkillRules.AimDirection(aim, state.Facing, config.Wire.AimDeadzone);
            // 扇の中の位置（-1〜1）を 2 乗で寄せて、狙った方向ほど密にする。
            float x = tuning.ShotCount > 1 ? 2f * k / (tuning.ShotCount - 1) - 1f : 0f;
            float spread = tuning.SpreadDeg * 0.5f * x * Math.Abs(x);
            float angle = MathF.Atan2(direction.Y, direction.X) + spread * MathF.PI / 180f;
            var shot = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            origin = new Vector2(state.Position.X, state.Position.Y) + direction * tuning.LaunchOffset;
            velocity = shot * tuning.Speed;
        }

        /// <summary>選んだクリームの味（ためている間・出した後。選んでいなければオレンジ）。</summary>
        public static CreamFlavor FlavorOf(in CharacterState state)
        {
            return state.Combat.HoldVariant == (byte)CreamFlavor.Matcha ? CreamFlavor.Matcha : CreamFlavor.Orange;
        }

        /// <summary>Q: わたがしの足場を置く。</summary>
        public static void FireCottonCandy(WorldObjects objects, in CharacterState state, int index, CharacterConfig config, int tick)
        {
            CottonCandyTuning tuning = config.Skills.CottonCandy;
            CandyPlacement(state, config, state.Action.Charge, state.Action.Aim, out Vector2 center, out Vector2 normal);
            objects.Add(new WorldObject
            {
                Kind = WorldObjectKind.CottonCandy, Owner = index, Slot = state.Action.Attack, Position = center, Velocity = normal,
                End = new Vector2(tuning.HalfLength, tuning.HalfThickness), SpawnTick = tick, ExpireTick = tick + tuning.DurationTicks,
            });
        }

        /// <summary>E: クリームを扇形に飛ばす。</summary>
        public static void FireCream(WorldObjects objects, in CharacterState state, int index, CharacterConfig config, int tick)
        {
            CreamTuning tuning = config.Skills.Cream;
            byte flavor = (byte)FlavorOf(state);
            for (int k = 0; k < tuning.ShotCount; k++)
            {
                CreamLaunch(state, config, state.Action.Aim, k, out Vector2 origin, out Vector2 velocity);
                objects.Add(new WorldObject
                {
                    Kind = WorldObjectKind.CreamShot, Owner = index, Slot = state.Action.Attack, Position = origin, Velocity = velocity,
                    SpawnTick = tick, ExpireTick = tick + tuning.ShotLifetimeTicks, Variant = flavor,
                });
            }
        }

        /// <summary>
        /// R: マカロンを IntervalTicks ごとに投げ上げる（投げるまでは持ち主の頭上についていく）。角度・速さ・色は、ティックと持ち主と
        /// 順番から決まる（PC が違っても同じ）。
        /// </summary>
        public static void FireTeaParty(WorldObjects objects, in CharacterState state, int index, CharacterConfig config, int tick)
        {
            TeaPartyTuning tuning = config.Skills.TeaParty;
            for (int k = 0; k < tuning.Count; k++)
            {
                float angle = (StableHash.Range(tick, index, k * 3, 1001) / 500f - 1f) * tuning.MaxAngleDeg * MathF.PI / 180f;
                float speed = tuning.MinSpeed + (tuning.MaxSpeed - tuning.MinSpeed) * StableHash.Range(tick, index, k * 3 + 1, 1001) / 1000f;
                int roll = StableHash.Range(tick, index, k * 3 + 2, 100);
                MacaronColor color = roll < tuning.GreenPercent ? MacaronColor.Green
                    : roll < tuning.GreenPercent + tuning.YellowPercent ? MacaronColor.Yellow : MacaronColor.Rainbow;
                int launch = tick + k * tuning.IntervalTicks;
                objects.Add(new WorldObject
                {
                    Kind = WorldObjectKind.Macaron, Owner = index, Slot = state.Action.Attack,
                    Position = HeldMacaronPosition(state, tuning),
                    Velocity = new Vector2(MathF.Sin(angle) * speed, MathF.Cos(angle) * speed),
                    SpawnTick = launch, ExpireTick = launch + tuning.MaxLifetimeTicks, Variant = (byte)color,
                    BonusPercent = tuning.BonusPercentOf(color),
                });
            }
        }

        /// <summary>マカロンが投げられたか（SpawnTick より前は、持ち主の頭上で待っている）。</summary>
        public static bool IsLaunched(in WorldObject macaron, int tick)
        {
            return tick >= macaron.SpawnTick;
        }

        /// <summary>マカロンが消える前の点滅中か（点滅の間は当たらない）。</summary>
        public static bool IsBlinking(in WorldObject macaron, TeaPartyTuning tuning, int tick)
        {
            return macaron.ExpireTick - tick <= tuning.BlinkTicks;
        }

        private static Vector2 HeldMacaronPosition(in CharacterState owner, TeaPartyTuning tuning)
        {
            return new Vector2(owner.Position.X, owner.Position.Y + tuning.OffsetY);
        }

        /// <summary>わたがしの足場: 触れた人（持ち主も）を、足場の中心から外向き（横長の形に合わせた向き）に弾き飛ばす。</summary>
        public static void StepCottonCandy(in WorldObject candy, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            CottonCandyTuning tuning)
        {
            Vector2 normal = candy.Velocity;
            var tangent = new Vector2(normal.Y, -normal.X);
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState target = states[v];
                if (target.Combat.IsUntouchable || target.Combat.HitstopTicksLeft > 0)
                {
                    continue;
                }
                Vector3 half = configs[v].Movement.HalfExtents;
                var rel = new Vector2(target.Position.X - candy.Position.X, target.Position.Y - candy.Position.Y);
                float u = Vector2.Dot(rel, tangent);
                float w = Vector2.Dot(rel, normal);
                // キャラクターの箱の、足場の向きへの広がり。
                float reachU = candy.End.X + MathF.Abs(tangent.X) * half.X + MathF.Abs(tangent.Y) * half.Y;
                float reachW = candy.End.Y + MathF.Abs(normal.X) * half.X + MathF.Abs(normal.Y) * half.Y;
                if (MathF.Abs(u) > reachU || MathF.Abs(w) > reachW)
                {
                    continue;
                }
                float du = u / reachU;
                float dw = w / reachW;
                Vector2 away = tangent * du + normal * dw;
                float length = away.Length();
                away = length > 1e-4f ? away / length : normal;
                if (StageZones.BumpAlong(ref target, away, tuning.BouncePower))
                {
                    states[v] = target;
                }
            }
        }

        /// <summary>飛んでいるクリーム: 重力で落ち、地形に当たったら、その面に張り付く（その場で CreamPatch に変わる）。</summary>
        public static void StepCreamShot(ref WorldObject shot, ICollisionQuery terrain, StageBounds? bounds, CreamTuning tuning, int tick)
        {
            shot.Velocity.Y -= tuning.Gravity * SimTime.DeltaTime;
            Vector2 delta = shot.Velocity * SimTime.DeltaTime;
            float distance = delta.Length();
            if (distance > SimMath.Epsilon)
            {
                var origin = new Vector3(shot.Position.X, shot.Position.Y, 0f);
                var direction = new Vector3(delta.X / distance, delta.Y / distance, 0f);
                if (terrain.Raycast(origin, direction, distance, out CastHit wall))
                {
                    StickCream(ref shot, new Vector2(wall.Point.X, wall.Point.Y), new Vector2(wall.Normal.X, wall.Normal.Y), tuning, tick);
                    return;
                }
            }
            shot.Position += delta;
            if (bounds.HasValue && !bounds.Value.Contains(new Vector3(shot.Position.X, shot.Position.Y, 0f)))
            {
                shot.Ended = true;
            }
        }

        // 面（点 point、法線 normal。地形は軸平行の箱なので、法線は軸の向き）に張り付いたクリームの箱にする。
        private static void StickCream(ref WorldObject shot, Vector2 point, Vector2 normal, CreamTuning tuning, int tick)
        {
            bool side = MathF.Abs(normal.X) > MathF.Abs(normal.Y);
            normal = side ? new Vector2(normal.X > 0f ? 1f : -1f, 0f) : new Vector2(0f, normal.Y > 0f ? 1f : -1f);
            float halfThickness = tuning.PatchThickness * 0.5f;
            shot.Kind = WorldObjectKind.CreamPatch;
            shot.Position = point + normal * halfThickness;
            shot.End = side ? new Vector2(halfThickness, tuning.PatchHalfLength) : new Vector2(tuning.PatchHalfLength, halfThickness);
            shot.Velocity = normal;
            shot.SpawnTick = tick;
            shot.ExpireTick = tick + tuning.PatchTicks;
        }

        /// <summary>張り付いたクリームの面が、壁（横向き）か。</summary>
        public static bool IsWallPatch(in WorldObject patch)
        {
            return MathF.Abs(patch.Velocity.X) > 0.5f;
        }

        /// <summary>張り付いたクリームの面が、天井（下向き）か。</summary>
        public static bool IsCeilingPatch(in WorldObject patch)
        {
            return patch.Velocity.Y < -0.5f;
        }

        /// <summary>キャラクター（中心 position、半分の大きさ half）が、張り付いたクリームに触れているか。</summary>
        public static bool Touches(in WorldObject patch, Vector3 position, Vector3 half)
        {
            return MathF.Abs(position.X - patch.Position.X) <= patch.End.X + half.X + CreamContactMargin
                && MathF.Abs(position.Y - patch.Position.Y) <= patch.End.Y + half.Y + CreamContactMargin;
        }

        /// <summary>
        /// 張り付いたクリームに触れている人の効果を決め直す（物を進めた後に毎ティック。CombatState の Cream で始まる値）。
        /// 同じ味のクリームにいくつ触れても、効果は 1 つ分。オレンジと抹茶の両方に触れていれば、速さは足し合わせる。
        /// </summary>
        public static void TouchCream(WorldObjects objects, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs)
        {
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState state = states[v];
                CombatState before = state.Combat;
                int orangeSpeed = 0;
                int matchaSpeed = 0;
                int wallJump = 0;
                bool noCling = false;
                bool stickTouch = false;
                if (!state.Combat.IsUntouchable)
                {
                    Vector3 half = configs[v].Movement.HalfExtents;
                    for (int o = 0; o < objects.Count; o++)
                    {
                        WorldObject patch = objects[o];
                        if (patch.Kind != WorldObjectKind.CreamPatch || patch.Ended || !Touches(patch, state.Position, half))
                        {
                            continue;
                        }
                        CreamTuning tuning = configs[patch.Owner].Skills.Cream;
                        bool wall = IsWallPatch(patch);
                        if (patch.Variant == (byte)CreamFlavor.Orange)
                        {
                            orangeSpeed = tuning.OrangeSpeedPercent;
                            if (wall)
                            {
                                wallJump = tuning.OrangeWallJumpPercent;
                            }
                            continue;
                        }
                        // 抹茶: 置いた本人も含めて全員に効く（ユーザーの要望、2026-10-21）。張り付いた所はだれも壁に張り付けない。
                        noCling |= wall;
                        matchaSpeed = tuning.MatchaSpeedPercent;
                        if ((wall || IsCeilingPatch(patch)) && tuning.MatchaStickTicks > 0)
                        {
                            stickTouch = true;
                            if (!state.Combat.CreamStickSpent && state.Combat.CreamStickTicksLeft == 0 && state.Action.Kind != ActionKind.Hitstun)
                            {
                                Stick(ref state, configs[v], tuning.MatchaStickTicks);
                            }
                        }
                    }
                }
                state.Combat.CreamSpeedPercent = orangeSpeed + matchaSpeed;
                state.Combat.CreamWallJumpPercent = wallJump;
                state.Combat.CreamNoCling = noCling;
                if (!stickTouch && state.Combat.CreamStickTicksLeft == 0)
                {
                    state.Combat.CreamStickSpent = false;
                }
                if (!state.Combat.Equals(before))
                {
                    states[v] = state;
                }
            }
        }

        // 抹茶のクリームに張り付く（壁の張り付きとワイヤーの接続はやめる）。
        private static void Stick(ref CharacterState state, CharacterConfig config, int ticks)
        {
            state.Combat.CreamStickTicksLeft = ticks;
            state.Combat.CreamStickSpent = true;
            state.WallSide = 0;
            state.Velocity = Vector3.Zero;
            if (state.Wire.Phase == WirePhase.Attached || state.Wire.Phase == WirePhase.Firing)
            {
                WireController.Release(ref state, WireReleaseReason.Hit, config.Wire);
            }
        }

        /// <summary>
        /// マカロン: 投げるまでは持ち主の頭上で待つ。投げた後は重力で落ち、地形で跳ね返る（床で跳ねたときに遅ければ止まる）。止まるか
        /// ステージの外に出たら点滅を始める。点滅していなければ、持ち主以外に相手ごとに 1 回だけ当たる（hits に加える）。
        /// </summary>
        public static void StepMacaron(ref WorldObject macaron, ICollisionQuery terrain, List<CharacterState> states,
            IReadOnlyList<CharacterConfig> configs, StageBounds? bounds, TeaPartyTuning tuning, List<HitRecord> hits, int tick)
        {
            if (!IsLaunched(macaron, tick))
            {
                macaron.Position = HeldMacaronPosition(states[macaron.Owner], tuning);
                return;
            }
            if (IsBlinking(macaron, tuning, tick))
            {
                return;
            }
            macaron.Velocity.Y -= tuning.Gravity * SimTime.DeltaTime;
            Vector2 delta = macaron.Velocity * SimTime.DeltaTime;
            var half = new Vector3(tuning.Radius, tuning.Radius, tuning.Radius);
            var center = new Vector3(macaron.Position.X, macaron.Position.Y, 0f);
            if (delta.Length() > SimMath.Epsilon && terrain.SweepBox(center, half, new Vector3(delta.X, delta.Y, 0f), out CastHit wall))
            {
                float length = delta.Length();
                float travel = MathF.Max(0f, wall.Distance - MacaronSkin);
                macaron.Position += delta / length * travel;
                var normal = new Vector2(wall.Normal.X, wall.Normal.Y);
                float into = Vector2.Dot(macaron.Velocity, normal);
                if (into < 0f)
                {
                    Vector2 tangentVelocity = macaron.Velocity - normal * into;
                    macaron.Velocity = tangentVelocity * tuning.Friction - normal * (into * tuning.Restitution);
                }
                if (normal.Y > 0.5f && macaron.Velocity.Length() < tuning.StopSpeed)
                {
                    StartBlink(ref macaron, tuning, tick);
                    return;
                }
            }
            else
            {
                macaron.Position += delta;
            }
            if (bounds.HasValue && !bounds.Value.Contains(new Vector3(macaron.Position.X, macaron.Position.Y, 0f)))
            {
                StartBlink(ref macaron, tuning, tick);
                return;
            }

            for (int v = 0; v < states.Count; v++)
            {
                CharacterState target = states[v];
                if (v == macaron.Owner || (macaron.HitMask & (1u << v)) != 0 || target.Combat.IsUntouchable)
                {
                    continue;
                }
                Vector3 size = configs[v].Movement.HalfExtents;
                if (MathF.Abs(target.Position.X - macaron.Position.X) < size.X + tuning.Radius
                    && MathF.Abs(target.Position.Y - macaron.Position.Y) < size.Y + tuning.Radius)
                {
                    int facing = macaron.Velocity.X > 0f ? 1 : macaron.Velocity.X < 0f ? -1 : (target.Position.X >= macaron.Position.X ? 1 : -1);
                    // 色の上乗せは、威力と吹き飛びの両方にかける（威力に合わせて吹き飛ぶ）。
                    var power = new AttackPower(1f, macaron.BonusPercent, macaron.BonusPercent,
                        jackpot: macaron.Variant == (byte)MacaronColor.Rainbow);
                    hits.Add(new HitRecord(macaron.Owner, v, macaron.Slot, 0, tuning.Hit, facing, power, fromObject: true));
                    macaron.HitMask |= 1u << v;
                }
            }
        }

        // 点滅を始める（その場で止まり、BlinkTicks で消える）。
        private static void StartBlink(ref WorldObject macaron, TeaPartyTuning tuning, int tick)
        {
            macaron.Velocity = Vector2.Zero;
            macaron.ExpireTick = Math.Min(macaron.ExpireTick, tick + tuning.BlinkTicks);
        }
        /// <summary>空中下: デジタルスケールを足もとから落とす。</summary>
        public static void FireScale(WorldObjects objects, in CharacterState state, int index, CharacterConfig config, int tick)
        {
            ScaleTuning tuning = config.Skills.Scale;
            objects.Add(new WorldObject
            {
                Kind = WorldObjectKind.Scale, Owner = index, Slot = state.Action.Attack,
                Position = new Vector2(state.Position.X, state.Position.Y + tuning.OffsetY), SpawnTick = tick, ExpireTick = int.MaxValue,
            });
        }

        /// <summary>デジタルスケールのグラム数（表示にも使う）。</summary>
        public static int ScaleGrams(in WorldObject scale, ScaleTuning tuning, int tick)
        {
            return tuning.GramsAt(tick - scale.SpawnTick);
        }

        /// <summary>
        /// デジタルスケール: 真下へ自由落下する。持ち主以外の相手に触れたら当てて（落ちていた時間で威力と吹き飛びが増え、FatalGrams 以上なら
        /// FATAL）消える。地形に当たるか、ステージの外に出ても消える。
        /// </summary>
        public static void StepScale(ref WorldObject scale, ICollisionQuery terrain, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            StageBounds? bounds, ScaleTuning tuning, List<HitRecord> hits, int tick)
        {
            scale.Velocity.X = 0f;
            scale.Velocity.Y = MathF.Max(scale.Velocity.Y - tuning.Gravity * SimTime.DeltaTime, -tuning.MaxFallSpeed);
            Vector2 delta = scale.Velocity * SimTime.DeltaTime;
            var half = new Vector3(tuning.HalfWidth, tuning.HalfHeight, tuning.HalfWidth);
            var center = new Vector3(scale.Position.X, scale.Position.Y, 0f);
            if (terrain.SweepBox(center, half, new Vector3(delta.X, delta.Y, 0f), out CastHit ground))
            {
                float length = delta.Length();
                if (length > SimMath.Epsilon)
                {
                    scale.Position += delta / length * MathF.Max(0f, ground.Distance - 0.01f);
                }
                scale.Ended = true;
                return;
            }
            scale.Position += delta;
            if (bounds.HasValue && !bounds.Value.Contains(new Vector3(scale.Position.X, scale.Position.Y, 0f)))
            {
                scale.Ended = true;
                return;
            }
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState target = states[v];
                if (v == scale.Owner || target.Combat.IsUntouchable)
                {
                    continue;
                }
                Vector3 size = configs[v].Movement.HalfExtents;
                if (MathF.Abs(target.Position.X - scale.Position.X) < size.X + tuning.HalfWidth
                    && MathF.Abs(target.Position.Y - scale.Position.Y) < size.Y + tuning.HalfHeight)
                {
                    int grams = ScaleGrams(scale, tuning, tick);
                    int bonus = tuning.BonusPercentAt(grams);
                    var power = new AttackPower(1f, bonus, bonus, fatal: grams >= tuning.FatalGrams, sources: PowerSource.FallTime);
                    int facing = target.Position.X >= scale.Position.X ? 1 : -1;
                    hits.Add(new HitRecord(scale.Owner, v, scale.Slot, 0, tuning.Hit, facing, power, fromObject: true));
                    scale.Ended = true;
                    return;
                }
            }
        }

        /// <summary>防げる投射物か（上から来たものを、PASTRY CHEF のボウルが防ぐ）。</summary>
        public static bool IsBlockable(WorldObjectKind kind)
        {
            return kind == WorldObjectKind.Projectile || kind == WorldObjectKind.Missile || kind == WorldObjectKind.Macaron
                || kind == WorldObjectKind.CreamShot || kind == WorldObjectKind.Scale || kind == WorldObjectKind.BallLightning
                || kind == WorldObjectKind.Shell || kind == WorldObjectKind.Mine;
        }

        /// <summary>
        /// 上から来た投射物 item（下へ動いている）を、ボウルの攻撃（AttackData.BlocksProjectilesAbove）の判定の箱で防ぐ人がいれば、防いで
        /// （その人の ShieldBlockCount を増やす）true。持ち主の投射物は防がない。
        /// </summary>
        public static bool TryBlockFromAbove(in WorldObject item, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, float radius)
        {
            if (item.Velocity.Y >= 0f)
            {
                return false;
            }
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState state = states[v];
                if (v == item.Owner || state.Action.Kind != ActionKind.Attack || state.Combat.IsWaitingToRespawn)
                {
                    continue;
                }
                AttackData attack = configs[v].Combat.Get(state.Action.Attack);
                if (!attack.BlocksProjectilesAbove || item.Position.Y < state.Position.Y)
                {
                    continue;
                }
                foreach (AttackHit hit in attack.Hits)
                {
                    if (!hit.IsActiveAt(state.Action.Ticks))
                    {
                        continue;
                    }
                    HitResolver.Hitbox(state, hit, out Vector2 center, out Vector2 half);
                    if (MathF.Abs(item.Position.X - center.X) < half.X + radius && MathF.Abs(item.Position.Y - center.Y) < half.Y + radius)
                    {
                        state.Combat.ShieldBlockCount++;
                        states[v] = state;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
