using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Match;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;

namespace Airerage.Core.Skills
{
    /// <summary>
    /// 技の効果とワールドに置かれた物の規則（docs/13 T5-6、docs/14。STORM RUNNER の強化と風、SPACE TRAVELER の技は SpaceRules）。SimWorld が 1 ティックの中で次の順に呼ぶ:
    /// 1. FireEffects（全員を進めた後）: 攻撃のデータの効果（AttackData.Effect）を、出してから EffectTick のティックに 1 回だけ起こす
    ///    （物を置く、投射物を撃つ、強化）。
    /// 2. StepObjects: 物を進める（寿命、ミラーボールのゲージ、セーフティボックスのチャージ、投射物の移動と命中）。投射物の命中は
    ///    hits に集めて、HitResolver がキャラクターの攻撃と一緒に当てる。
    /// 3. EndObjects（命中の後）: 寿命が来た・壊れた物を取り除く（ばらまき、ワイヤーの解除、クールタイムの開始）。
    /// 物は番号の順、キャラクターは番号の順に調べる（結果が順番で変わらない）。PASTRY CHEF の技は PastryRules。
    /// </summary>
    public static class SkillRules
    {
        // 置いた物の位置が同じとみなす距離（m。ワイヤーの接続点と比べる）。
        private const float SamePointTolerance = 0.01f;

        /// <summary>1. 技の効果を起こす。tick は進めた後のティック。</summary>
        public static void FireEffects(WorldObjects objects, CoinLedger ledger, List<CharacterState> states,
            IReadOnlyList<CharacterConfig> configs, int tick)
        {
            for (int i = 0; i < states.Count; i++)
            {
                CharacterState state = states[i];
                if (state.Action.Kind != ActionKind.Attack || state.Action.EffectDone)
                {
                    continue;
                }
                CharacterConfig config = configs[i];
                AttackData attack = config.Combat.Get(state.Action.Attack);
                // 効果中にもう一度出した突風は、溜めの後に撃つ。
                int effectTick = state.Action.Recast ? config.Skills.SpeedBoost.GustWindupTicks : attack.EffectTick;
                if (attack.Effect == SkillEffect.None || state.Action.Ticks < effectTick)
                {
                    continue;
                }
                state.Action.EffectDone = true;
                states[i] = state;
                Fire(attack.Effect, objects, ledger, states, i, config, tick);
            }
        }

        /// <summary>2. 物を進める。投射物の命中を hits に加える（hits は呼ぶ側が空にしておく）。</summary>
        public static void StepObjects(WorldObjects objects, ICollisionQuery terrain, List<CharacterState> states,
            IReadOnlyList<CharacterConfig> configs, StageBounds? bounds, int tick, List<HitRecord> hits)
        {
            // クリームとマカロンは、動かない地形だけに当たる（わたがしの足場には張り付かない・跳ねない）。
            ICollisionQuery ground = terrain is WorldCollision world ? world.Terrain : terrain;
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject item = objects[o];
                if (item.Ended)
                {
                    continue;
                }
                if (tick >= item.ExpireTick)
                {
                    item.Ended = true;
                    objects[o] = item;
                    continue;
                }
                CharacterConfig owner = configs[item.Owner];
                switch (item.Kind)
                {
                    case WorldObjectKind.MirrorBall:
                        StepMirrorBall(item, states, configs, owner.Skills.MirrorBall, tick);
                        break;
                    case WorldObjectKind.Safe:
                        StepSafe(ref item, owner.Skills.Safe, tick);
                        break;
                    case WorldObjectKind.Projectile:
                        StepProjectile(ref item, objects, terrain, states, configs, bounds, owner.Skills.Projectile, hits);
                        break;
                    case WorldObjectKind.Gust:
                        StepGust(ref item, objects, states, configs, owner.Skills.SpeedBoost);
                        break;
                    case WorldObjectKind.WindZone:
                        StepWindZone(item, objects, states, configs, owner.Skills.WindZone);
                        break;
                    case WorldObjectKind.Missile:
                        SpaceRules.StepMissile(ref item, objects, terrain, states, configs, bounds, owner.Skills.Missile, hits, tick);
                        break;
                    case WorldObjectKind.Beam:
                        SpaceRules.StepBeam(item, objects, states, configs, owner.Skills.Beam, hits, tick);
                        break;
                    case WorldObjectKind.CottonCandy:
                        PastryRules.StepCottonCandy(item, states, configs, owner.Skills.CottonCandy);
                        break;
                    case WorldObjectKind.CreamShot:
                        PastryRules.StepCreamShot(ref item, ground, bounds, owner.Skills.Cream, tick);
                        break;
                    case WorldObjectKind.Macaron:
                        PastryRules.StepMacaron(ref item, ground, states, configs, bounds, owner.Skills.TeaParty, hits, tick);
                        break;
                }
                objects[o] = item;
            }
            SpaceRules.WarpThroughPortals(objects, states, configs);
            PastryRules.TouchCream(objects, states, configs);
        }

        /// <summary>
        /// 3. 寿命が来た・壊れた物を取り除く。tick は進めた後のティック。spots と playing は、撃ち終わったビームの拠点での確定に使う
        /// （spots が null なら確定しない）。最後に、キャラクターごとのワープポータルの数を数え直す。
        /// </summary>
        public static void EndObjects(WorldObjects objects, CoinLedger ledger, List<CharacterState> states,
            IReadOnlyList<CharacterConfig> configs, MatchRules rules, int tick, IReadOnlyList<BankSpot> spots = null, bool playing = false)
        {
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject item = objects[o];
                CharacterConfig owner = configs[item.Owner];
                bool broken = item.Kind == WorldObjectKind.Safe && item.Damage >= owner.Skills.Safe.Durability;
                if (!item.Ended && !broken)
                {
                    continue;
                }
                switch (item.Kind)
                {
                    case WorldObjectKind.MirrorBall:
                        DetachFrom(item, states, configs);
                        break;
                    case WorldObjectKind.Safe:
                        CoinDropRules.Spill(ledger, item.Id, item.Stored, SafeCoins(item), item.Position, rules, tick);
                        ReleaseCooldown(states, item.Owner, item.Slot, owner.Combat);
                        break;
                    case WorldObjectKind.Beam:
                        SpaceRules.BankByBeam(item, ledger, states, configs, spots, rules, tick, playing);
                        break;
                }
                objects.RemoveAt(o);
                o--;
            }
            SpaceRules.CountPortals(objects, states);
        }

        /// <summary>セーフティボックスの今の枚数（チャージの小数は切り捨て）。</summary>
        public static int SafeCoins(in WorldObject safe)
        {
            return (int)(safe.ChargeMilli / 1000);
        }

        /// <summary>セーフティボックスが上限に着いたか（もうチャージしない）。</summary>
        public static bool IsSafeFull(in WorldObject safe, SafeTuning tuning)
        {
            return safe.ChargeMilli >= tuning.MaxCoins * 1000L;
        }

        /// <summary>次のチャージまでの割合（0〜1。置いた直後とチャージした直後が 0）。表示用。</summary>
        public static float SafeChargeProgress(in WorldObject safe, SafeTuning tuning, int tick)
        {
            int age = Math.Max(0, tick - safe.SpawnTick);
            return (float)(age % tuning.ChargeIntervalTicks) / tuning.ChargeIntervalTicks;
        }

        /// <summary>ワイヤーが、物 item（アンカーになる物）につながっているか。</summary>
        public static bool IsAttachedTo(in CharacterState state, in WorldObject item)
        {
            return state.Wire.Phase == WirePhase.Attached && SamePoint(state.Wire.Anchor, item.Position);
        }

        private static bool SamePoint(Vector3 point, Vector2 position)
        {
            return MathF.Abs(point.X - position.X) <= SamePointTolerance && MathF.Abs(point.Y - position.Y) <= SamePointTolerance;
        }

        private static void Fire(SkillEffect effect, WorldObjects objects, CoinLedger ledger, List<CharacterState> states, int index,
            CharacterConfig config, int tick)
        {
            CharacterState state = states[index];
            var position = new Vector2(state.Position.X, state.Position.Y);
            switch (effect)
            {
                case SkillEffect.MirrorBall:
                {
                    MirrorBallTuning ball = config.Skills.MirrorBall;
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.MirrorBall, Owner = index, Slot = state.Action.Attack,
                        Position = position + new Vector2(0f, ball.OffsetY), SpawnTick = tick, ExpireTick = tick + ball.DurationTicks,
                    });
                    break;
                }
                case SkillEffect.Safe:
                {
                    // 長押しした長さの分だけ手持ちのコインを預ける（上限まで）。預けなくても置く。
                    SafeTuning safe = config.Skills.Safe;
                    int amount = Math.Min(Math.Min(state.Coins.Held, safe.DepositFor(state.Action.Charge)), safe.MaxCoins);
                    int stored = amount > 0 && ledger.Deposit(new CoinEventId(tick, CoinEventKind.Deposit, index), states, index, amount) ? amount : 0;
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.Safe, Owner = index, Slot = state.Action.Attack, Position = position,
                        SpawnTick = tick, ExpireTick = tick + safe.DurationTicks, ChargeMilli = stored * 1000L, Stored = stored,
                    });
                    // クールタイムは、セーフティボックスが壊れるか時間が来てから数える。
                    CharacterState owner = states[index];
                    SetCooldown(ref owner.Combat, state.Action.Attack, CombatState.CooldownWhileActive);
                    states[index] = owner;
                    break;
                }
                case SkillEffect.CoinBoost:
                    state.Combat.CoinBoostTicksLeft = config.Skills.CoinBoost.DurationTicks;
                    states[index] = state;
                    break;
                case SkillEffect.SpeedBoost:
                {
                    SpeedBoostTuning boost = config.Skills.SpeedBoost;
                    if (state.Action.Recast)
                    {
                        // 効果中にもう一度出した: 強化を解いて、溜めの後に照準の方向へ突風を撃ち、反動で逆へ下がる（ユーザーの要望、
                        // 2026-10-16）。クールタイムは出したときから数えている（StartAttack）。
                        Vector2 direction = AimDirection(state.Action.Aim, state.Facing, config.Wire.AimDeadzone);
                        state.Combat.SpeedBoostTicksLeft = 0;
                        if (MathF.Abs(direction.X) > 0.1f)
                        {
                            state.Facing = direction.X > 0f ? 1 : -1;
                        }
                        state.Velocity = new Vector3(-direction.X * boost.GustRecoilSpeed, -direction.Y * boost.GustRecoilSpeed, 0f);
                        if (state.Velocity.Y > 0f)
                        {
                            state.Grounded = false;
                        }
                        objects.Add(new WorldObject
                        {
                            Kind = WorldObjectKind.Gust, Owner = index, Slot = state.Action.Attack,
                            Position = position + direction * 0.6f,
                            Velocity = direction * boost.GustSpeed,
                            SpawnTick = tick, ExpireTick = tick + boost.GustTicks,
                        });
                    }
                    else
                    {
                        // クールタイムは、強化が終わってから数える（ActionController が時間切れで始める）。
                        state.Combat.SpeedBoostTicksLeft = boost.DurationTicks;
                        SetCooldown(ref state.Combat, state.Action.Attack, CombatState.CooldownWhileActive);
                    }
                    states[index] = state;
                    break;
                }
                case SkillEffect.WindZone:
                {
                    WindZoneTuning wind = config.Skills.WindZone;
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.WindZone, Owner = index, Slot = state.Action.Attack, Position = position,
                        Velocity = AimDirection(state.Action.Aim, state.Facing, config.Wire.AimDeadzone),
                        SpawnTick = tick, ExpireTick = tick + wind.DurationTicks,
                    });
                    break;
                }
                case SkillEffect.SkyBoost:
                    state.Combat.SkyBoostTicksLeft = config.Skills.SkyBoost.DurationTicks;
                    states[index] = state;
                    break;
                case SkillEffect.Portal:
                    SpaceRules.FirePortal(objects, states, index, config, tick);
                    break;
                case SkillEffect.Missile:
                    SpaceRules.FireMissile(objects, state, index, config, tick);
                    break;
                case SkillEffect.CosmicRay:
                    SpaceRules.FireCosmicRay(objects, index, config, tick);
                    break;
                case SkillEffect.SlowFall:
                    // ゆっくり落ちる（別の移動をするまで。CharacterSimulator が終わりを決める）。
                    state.Combat.SlowFallSerial = state.Combat.AttackSerial + 1;
                    states[index] = state;
                    break;
                case SkillEffect.CottonCandy:
                    PastryRules.FireCottonCandy(objects, state, index, config, tick);
                    break;
                case SkillEffect.Cream:
                    PastryRules.FireCream(objects, state, index, config, tick);
                    break;
                case SkillEffect.TeaParty:
                    PastryRules.FireTeaParty(objects, state, index, config, tick);
                    break;
                case SkillEffect.Projectile:
                {
                    // 威力の上乗せは、撃ったときの手持ちのコインで決める（コイントス。AttackPower の上乗せ）。
                    ProjectileTuning shot = config.Skills.Projectile;
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.Projectile, Owner = index, Slot = state.Action.Attack,
                        Position = position + new Vector2(shot.OffsetX * state.Facing, shot.OffsetY),
                        Velocity = new Vector2(shot.VelocityX * state.Facing, shot.VelocityY),
                        SpawnTick = tick, ExpireTick = tick + shot.LifetimeTicks,
                        BonusPercent = shot.HeldCoinBonusPercent(state.Coins.Held),
                    });
                    break;
                }
            }
        }

        // ミラーボール: つないでいる人（敵も含む）のゲージが、GaugeIntervalTicks ごとにたまる（C-14）。
        private static void StepMirrorBall(in WorldObject ball, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            MirrorBallTuning tuning, int tick)
        {
            int age = tick - ball.SpawnTick;
            if (age <= 0 || age % tuning.GaugeIntervalTicks != 0)
            {
                return;
            }
            for (int i = 0; i < states.Count; i++)
            {
                CharacterState state = states[i];
                if (IsAttachedTo(state, ball))
                {
                    CombatTuning.AddGauge(ref state.Combat, tuning.GaugePerInterval, configs[i].Skills.CoinBoost);
                    states[i] = state;
                }
            }
        }

        // セーフティボックス: ChargeIntervalTicks ごとに ChargeCoins 枚を足し、ChargeRatePercent % にする（上限 MaxCoins）。
        // 上限に着いたら、もうチャージしない（ユーザーの要望、2026-10-16）。
        // 1/1000 枚単位の整数で計算する（PC が違っても同じ。小数まで記録し、ばらまくときに切り捨てる）。
        private static void StepSafe(ref WorldObject safe, SafeTuning tuning, int tick)
        {
            int age = tick - safe.SpawnTick;
            if (age <= 0 || age % tuning.ChargeIntervalTicks != 0 || IsSafeFull(safe, tuning))
            {
                return;
            }
            long charged = (safe.ChargeMilli + tuning.ChargeCoins * 1000L) * tuning.ChargeRatePercent / 100;
            safe.ChargeMilli = Math.Min(charged, tuning.MaxCoins * 1000L);
        }

        // 投射物: 重力で曲がりながら飛ぶ。地形・ステージの外で消える。撃った本人以外のキャラクターか、壊せる物に触れたら当てて消える。
        private static void StepProjectile(ref WorldObject shot, WorldObjects objects, ICollisionQuery terrain, List<CharacterState> states,
            IReadOnlyList<CharacterConfig> configs, StageBounds? bounds, ProjectileTuning tuning, List<HitRecord> hits)
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
                    shot.Position = new Vector2(wall.Point.X, wall.Point.Y);
                    shot.Ended = true;
                    return;
                }
            }
            shot.Position += delta;
            if (bounds.HasValue && !bounds.Value.Contains(new Vector3(shot.Position.X, shot.Position.Y, 0f)))
            {
                shot.Ended = true;
                return;
            }

            for (int v = 0; v < states.Count; v++)
            {
                CharacterState target = states[v];
                if (v == shot.Owner || (shot.HitMask & (1u << v)) != 0 || target.Combat.InvincibleTicksLeft > 0 || target.Combat.IsWaitingToRespawn)
                {
                    continue;
                }
                Vector3 half = configs[v].Movement.HalfExtents;
                if (MathF.Abs(target.Position.X - shot.Position.X) < half.X + tuning.Radius
                    && MathF.Abs(target.Position.Y - shot.Position.Y) < half.Y + tuning.Radius)
                {
                    int facing = shot.Velocity.X > 0f ? 1 : shot.Velocity.X < 0f ? -1 : states[shot.Owner].Facing;
                    hits.Add(new HitRecord(shot.Owner, v, shot.Slot, 0, tuning.Hit, facing,
                        AttackPower.OfProjectile(shot.BonusPercent, PowerSource.HeldCoins), fromObject: true));
                    shot.HitMask |= 1u << v;
                    shot.Ended = true;
                    return;
                }
            }

            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject other = objects[o];
                if (other.Kind != WorldObjectKind.Safe || other.Ended)
                {
                    continue;
                }
                SafeTuning safe = configs[other.Owner].Skills.Safe;
                if (MathF.Abs(other.Position.X - shot.Position.X) < safe.HalfWidth + tuning.Radius
                    && MathF.Abs(other.Position.Y - shot.Position.Y) < safe.HalfHeight + tuning.Radius)
                {
                    other.Damage += Math.Max(1, AttackPower.Damage(tuning.Hit.Damage, 1f, shot.BonusPercent));
                    objects[o] = other;
                    shot.Ended = true;
                    return;
                }
            }
        }

        /// <summary>照準の方向 aim（長さが deadzone より小さければ、向いている方向 facing の水平）を長さ 1 にしたもの。</summary>
        public static Vector2 AimDirection(Vector2 aim, int facing, float deadzone)
        {
            float length = aim.Length();
            return length >= deadzone && length > SimMath.Epsilon ? aim / length : new Vector2(facing >= 0 ? 1f : -1f, 0f);
        }

        /// <summary>風の帯 zone の中に、点 point（キャラクターなら中心。margin はその大きさの分の余裕）があるか。</summary>
        public static bool InWindZone(in WorldObject zone, WindZoneTuning tuning, Vector2 point, float margin)
        {
            Vector2 rel = point - zone.Position;
            float along = Vector2.Dot(rel, zone.Velocity);
            float across = MathF.Abs(rel.X * zone.Velocity.Y - rel.Y * zone.Velocity.X);
            return along >= -margin && along <= tuning.Length + margin && across <= tuning.HalfWidth + margin;
        }

        // 突風: まっすぐ進み（地形は通り抜ける）、触れたキャラクター（持ち主を除く）と投射物を、進む向きへ押す（それぞれ 1 回）。
        private static void StepGust(ref WorldObject gust, WorldObjects objects, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            SpeedBoostTuning tuning)
        {
            gust.Position += gust.Velocity * SimTime.DeltaTime;
            float speed = gust.Velocity.Length();
            if (speed <= SimMath.Epsilon)
            {
                return;
            }
            Vector2 direction = gust.Velocity / speed;
            const float thickness = 0.6f;
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState target = states[v];
                if (v == gust.Owner || (gust.HitMask & (1u << v)) != 0 || target.Combat.IsWaitingToRespawn)
                {
                    continue;
                }
                Vector3 half = configs[v].Movement.HalfExtents;
                Vector2 rel = new Vector2(target.Position.X, target.Position.Y) - gust.Position;
                float along = Vector2.Dot(rel, direction);
                float across = MathF.Abs(rel.X * direction.Y - rel.Y * direction.X);
                if (MathF.Abs(along) > thickness + half.X || across > tuning.GustHalfWidth + half.Y)
                {
                    continue;
                }
                target.Velocity.X += direction.X * tuning.GustPush;
                target.Velocity.Y += direction.Y * tuning.GustPush;
                if (target.Grounded && direction.Y >= 0f)
                {
                    // 地上の相手は少し浮かせる（地面の摩擦ですぐに止まらないように）【仮】。
                    target.Velocity.Y = MathF.Max(target.Velocity.Y, 3f);
                    target.Grounded = false;
                }
                states[v] = target;
                gust.HitMask |= 1u << v;
            }
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject other = objects[o];
                if (other.Kind != WorldObjectKind.Projectile || other.Ended || other.LastPushId == gust.Id)
                {
                    continue;
                }
                Vector2 rel = other.Position - gust.Position;
                float along = Vector2.Dot(rel, direction);
                float across = MathF.Abs(rel.X * direction.Y - rel.Y * direction.X);
                if (MathF.Abs(along) <= thickness + 0.5f && across <= tuning.GustHalfWidth + 0.5f)
                {
                    other.Velocity += direction * tuning.GustPush;
                    other.LastPushId = gust.Id;
                    objects[o] = other;
                }
            }
        }

        // 風の帯: 中にいるキャラクター（持ち主も）と投射物を、向きへ加速する（向きの速さの上限まで）。止まっている（ヒットストップ・
        // 復帰を待つ）キャラクターには効かない。
        private static void StepWindZone(in WorldObject zone, WorldObjects objects, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            WindZoneTuning tuning)
        {
            float add = tuning.Acceleration * SimTime.DeltaTime;
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState target = states[v];
                if (target.Combat.IsWaitingToRespawn || target.Combat.HitstopTicksLeft > 0)
                {
                    continue;
                }
                Vector3 half = configs[v].Movement.HalfExtents;
                if (!InWindZone(zone, tuning, new Vector2(target.Position.X, target.Position.Y), MathF.Min(half.X, half.Y)))
                {
                    continue;
                }
                var velocity = new Vector2(target.Velocity.X, target.Velocity.Y);
                velocity = Blow(velocity, zone.Velocity, add, tuning.SpeedLimit);
                target.Velocity.X = velocity.X;
                target.Velocity.Y = velocity.Y;
                if (target.Velocity.Y > 0f)
                {
                    target.Grounded = false;
                }
                states[v] = target;
            }
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject other = objects[o];
                if (other.Kind == WorldObjectKind.Projectile && !other.Ended && InWindZone(zone, tuning, other.Position, 0.3f))
                {
                    other.Velocity = Blow(other.Velocity, zone.Velocity, add, tuning.SpeedLimit);
                    objects[o] = other;
                }
            }
        }

        // 速度 velocity の、向き direction の成分を add だけ上げる（上限 limit。直交する成分は残す）。
        private static Vector2 Blow(Vector2 velocity, Vector2 direction, float add, float limit)
        {
            float along = Vector2.Dot(velocity, direction);
            if (along >= limit)
            {
                return velocity;
            }
            return velocity + direction * MathF.Min(add, limit - along);
        }

        // 消えたミラーボールにつないでいた人のワイヤーを外し、向かって飛んでいたフックは狙いをやめさせる（射程まで飛んで外れる）。
        private static void DetachFrom(in WorldObject ball, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs)
        {
            for (int i = 0; i < states.Count; i++)
            {
                CharacterState state = states[i];
                if (IsAttachedTo(state, ball))
                {
                    WireController.Release(ref state, WireReleaseReason.AnchorGone, configs[i].Wire);
                    states[i] = state;
                }
                else if (state.Wire.Phase == WirePhase.Firing && state.Wire.HasTargetPoint && SamePoint(state.Wire.TargetPoint, ball.Position))
                {
                    state.Wire.HasTargetPoint = false;
                    states[i] = state;
                }
            }
        }

        // 効果が終わったら、止めていたクールタイムを数え始める。
        private static void ReleaseCooldown(List<CharacterState> states, int owner, AttackSlot slot, CombatTuning combat)
        {
            CharacterState state = states[owner];
            if (CooldownOf(state.Combat, slot) == CombatState.CooldownWhileActive)
            {
                SetCooldown(ref state.Combat, slot, combat.CooldownTicksOf(slot));
                states[owner] = state;
            }
        }

        private static int CooldownOf(in CombatState combat, AttackSlot slot)
        {
            return slot == AttackSlot.Special1 ? combat.Special1CooldownTicksLeft
                : slot == AttackSlot.Special2 ? combat.Special2CooldownTicksLeft : 0;
        }

        private static void SetCooldown(ref CombatState combat, AttackSlot slot, int ticks)
        {
            if (slot == AttackSlot.Special1)
            {
                combat.Special1CooldownTicksLeft = ticks;
            }
            else if (slot == AttackSlot.Special2)
            {
                combat.Special2CooldownTicksLeft = ticks;
            }
        }
    }
}
