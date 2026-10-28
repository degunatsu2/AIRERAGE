using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Movement;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;

namespace Airerage.Core.Skills
{
    /// <summary>THIEF の技が使う、ステージの情報（SimWorld が 1 つ作る。ギミック・仕掛け・ルール・拠点の候補）。</summary>
    public sealed class ThiefContext
    {
        public ThiefContext(IReadOnlyList<StageZone> zones, IReadOnlyList<StageTrap> traps, MatchRules rules, IReadOnlyList<BankSpot> bankSpots,
            List<CharacterState> clones)
        {
            Zones = zones ?? new StageZone[0];
            Traps = traps ?? new StageTrap[0];
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            BankSpots = bankSpots ?? new BankSpot[0];
            Clones = clones ?? new List<CharacterState>();
        }

        /// <summary>キャラクターごとの分身の状態（SimWorld のもの。持ち主の CombatState.CloneTicksLeft が 0 より大きい間だけ意味がある）。</summary>
        public List<CharacterState> Clones { get; }

        /// <summary>ステージのギミック（並べた順。奪うときと、置いた写しの元に使う）。</summary>
        public IReadOnlyList<StageZone> Zones { get; }

        /// <summary>ステージの仕掛け（並べた順）。</summary>
        public IReadOnlyList<StageTrap> Traps { get; }

        public MatchRules Rules { get; }

        public IReadOnlyList<BankSpot> BankSpots { get; }

        /// <summary>
        /// 進めた後のティック tick に、キャラクターが触れていた拠点の地点の番号（BankRules.Step と同じく、進める前のティックの場所。
        /// 拠点がなければ -1）。
        /// </summary>
        public int BankSpotAt(int tick)
        {
            return BankSpots.Count > 0 ? BankRules.SpotAt(Rules, BankSpots.Count, tick - 1) : -1;
        }
    }

    /// <summary>
    /// THIEF の技（ユーザーの案、2026-10-28。docs/13 3.23、docs/14 1.6。値は ThiefTuning）。SimWorld と SkillRules から呼ぶ。
    /// - 煙球（地上技）: FireSmokeBall で真下へ投げ、StepObjects で落とす。地形か持ち主以外のキャラクターに触れたら、ごく狭い範囲に攻撃を
    ///   当て（投射物の命中）、煙（Smoke。見た目だけ）を出す。
    /// - タレントスナッチ（Q）: 弾（SnatchShot）がキャラクターに当たれば、その人の Q をコピーする（ConfigFor が、THIEF の Q をその技にした
    ///   調整値を作る。相手はそのまま使える）。奪えるステージのギミック・仕掛けに当たれば、それを奪い、もう一度の Q（ThiefTuning.PlaceAttack）
    ///   で照準の方向の目の前に置く（PlacedZone・PlacedTrap。2 つまで）。置いたギミックは SimWorld がステージのギミックに加えて効かせ
    ///   （RefreshZones）、置いた仕掛けはここで動かす。コピーした技を使うか置き終えるか、弾が外れて消えたら、クールタイムを数える（AfterStep）。
    /// - ミスディレクション（E）: 透明になり、分身（SimWorld の分身の状態）を残す。分身は移動とワイヤーだけをする簡単な動き（CloneInput）で、
    ///   ほかの人の攻撃の判定に触れると煙になって消える（PopClones）。透明と分身は時間・R 以外の行動・もう一度の E で解ける（AfterStep）。
    /// - バンクハイスト（R）: 拠点の近くでだけ出せる（CombatState.NearBank。ActionController.CanUse）。詠唱している間は動けず（CharacterSimulator）、
    ///   もう一度 R か拠点が移ったら、詠唱した長さのコインを得る（StepHeist。台帳の CoinEventKind.Heist）。のけぞると、得ずに止まる。
    /// </summary>
    public static class ThiefRules
    {
        // 弾・煙球が一度に進む距離（m。速い弾が、薄い物を通り抜けないように分けて調べる）。
        private const float SweepStep = 0.4f;

        // 分身の動きの乱数を、ほかの選択と分けるための数。
        private const int CloneSalt = 9100;

        /// <summary>THIEF（Q がタレントスナッチ）か。</summary>
        public static bool IsThief(CharacterConfig config)
        {
            return config != null && config.Combat.Special1.Effect == SkillEffect.TalentSnatch;
        }

        /// <summary>
        /// キャラクターの今の調整値（THIEF がコピーした Q・奪ったギミックを持っていれば、Q をその技・置く技にした調整値。それ以外は config
        /// そのもの）。baseConfigs は全員のもとの調整値（コピーした技の出どころ）。タレントスナッチを撃っている間（StealFromSerial の攻撃）は、
        /// もとの Q のまま。
        /// </summary>
        public static CharacterConfig ConfigFor(in CharacterState state, CharacterConfig config, IReadOnlyList<CharacterConfig> baseConfigs)
        {
            CombatState c = state.Combat;
            if (c.StealKind == StealKind.None || !IsThief(config))
            {
                return config;
            }
            if (state.Action.Kind == ActionKind.Attack && state.Action.Attack == AttackSlot.Special1 && c.AttackSerial == c.StealFromSerial)
            {
                return config;
            }
            AttackData special;
            if (c.StealKind == StealKind.Skill)
            {
                if (c.StealSource < 0 || c.StealSource >= baseConfigs.Count)
                {
                    return config;
                }
                special = baseConfigs[c.StealSource].Combat.Special1;
            }
            else
            {
                special = config.Skills.Thief.PlaceAttack;
            }
            // 使い終えるまでクールタイムは数えない（使い終えたら AfterStep がもとのクールタイムを始める）。
            return new CharacterConfig
            {
                Movement = config.Movement,
                Wire = config.Wire,
                Combat = config.Combat.WithSpecial1(special, 0),
                Skills = config.Skills,
            };
        }

        /// <summary>THIEF の技の効果を起こす（SkillRules.FireEffects から。tick は進めた後のティック）。</summary>
        public static void Fire(SkillEffect effect, WorldObjects objects, List<CharacterState> states, int index, CharacterConfig config, int tick,
            ThiefContext context)
        {
            CharacterState state = states[index];
            ThiefTuning tuning = config.Skills.Thief;
            var position = new Vector2(state.Position.X, state.Position.Y);
            switch (effect)
            {
                case SkillEffect.SmokeBall:
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.SmokeBall, Owner = index, Slot = state.Action.Attack, Position = position,
                        Velocity = new Vector2(0f, -tuning.SmokeBallSpeed), SpawnTick = tick, ExpireTick = tick + tuning.SmokeBallLifetimeTicks,
                    });
                    break;

                case SkillEffect.TalentSnatch:
                {
                    Vector2 direction = SkillRules.AimDirection(state.Action.Aim, state.Facing, config.Wire.AimDeadzone);
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.SnatchShot, Owner = index, Slot = state.Action.Attack,
                        Position = position + direction * tuning.SnatchLaunchOffset, Velocity = direction * tuning.SnatchSpeed,
                        SpawnTick = tick, ExpireTick = int.MaxValue,
                    });
                    // 当たるか外れるかが決まるまで、クールタイムは数えない。
                    state.Combat.Special1CooldownTicksLeft = CombatState.CooldownWhileActive;
                    states[index] = state;
                    break;
                }

                case SkillEffect.PlaceGimmick:
                    Place(objects, state, index, config, tick, context);
                    break;

                case SkillEffect.Misdirection:
                    // 分身は、今の位置と慣性で残す（突進は次のティックから。AttackMotion）。
                    state.Combat.InvisibleTicksLeft = tuning.MisdirectionTicks;
                    state.Combat.CloneTicksLeft = tuning.MisdirectionTicks;
                    states[index] = state;
                    if (context != null)
                    {
                        while (context.Clones.Count <= index)
                        {
                            context.Clones.Add(default);
                        }
                        context.Clones[index] = SpawnClone(state);
                    }
                    break;

                case SkillEffect.BankHeist:
                    state.Combat.HeistTicks = 1;
                    state.Combat.HeistSpot = context != null ? context.BankSpotAt(tick) : -1;
                    state.Velocity = Vector3.Zero;
                    states[index] = state;
                    break;
            }
        }

        /// <summary>THIEF の効果か（SkillRules.Fire がここへ回す）。</summary>
        public static bool Handles(SkillEffect effect)
        {
            return effect == SkillEffect.SmokeBall || effect == SkillEffect.TalentSnatch || effect == SkillEffect.PlaceGimmick
                || effect == SkillEffect.Misdirection || effect == SkillEffect.BankHeist;
        }

        // ---------------------------------------------------------------- 物（煙球・弾・置いた仕掛け・ボール）

        /// <summary>
        /// THIEF の物を進める（SkillRules.StepObjects の後に呼ぶ。投射物の命中は hits に加える）。terrain は地形（置かれた物を含む）。
        /// </summary>
        public static void StepObjects(WorldObjects objects, ICollisionQuery terrain, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            StageBounds? bounds, int tick, List<HitRecord> hits, ThiefContext context, StageState stage)
        {
            ICollisionQuery ground = terrain is WorldCollision world ? world.Terrain : terrain;
            int count = objects.Count;
            for (int o = 0; o < count; o++)
            {
                WorldObject item = objects[o];
                if (item.Ended)
                {
                    continue;
                }
                switch (item.Kind)
                {
                    case WorldObjectKind.SmokeBall:
                        StepSmokeBall(ref item, objects, terrain, states, configs, bounds, hits, tick);
                        break;
                    case WorldObjectKind.SnatchShot:
                        StepSnatchShot(ref item, terrain, states, configs, bounds, tick, context, stage);
                        break;
                    case WorldObjectKind.PlacedTrap:
                        StepPlacedTrap(ref item, objects, states, configs, tick, context);
                        break;
                    case WorldObjectKind.TrapBall:
                        StepTrapBall(ref item, ground, states, configs, bounds, tick, context);
                        break;
                    default:
                        continue;
                }
                objects[o] = item;
            }
        }

        // 煙球: 真下へ落ちる。地形か持ち主以外のキャラクターに触れたら、そこで弾けて、ごく狭い範囲の相手に攻撃を当て、煙を出す。
        private static void StepSmokeBall(ref WorldObject ball, WorldObjects objects, ICollisionQuery terrain, List<CharacterState> states,
            IReadOnlyList<CharacterConfig> configs, StageBounds? bounds, List<HitRecord> hits, int tick)
        {
            ThiefTuning tuning = configs[ball.Owner].Skills.Thief;
            if (!Sweep(ref ball, terrain, states, configs, tuning.SmokeBallRadius, out bool reached, out _))
            {
                if (bounds.HasValue && !bounds.Value.Contains(new Vector3(ball.Position.X, ball.Position.Y, 0f)))
                {
                    ball.Ended = true;
                }
                return;
            }
            ball.Ended = true;
            if (!reached)
            {
                return;
            }
            // 弾けた所のごく狭い範囲（持ち主以外）。
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState target = states[v];
                if (v == ball.Owner || target.Combat.IsUntouchable)
                {
                    continue;
                }
                Vector3 half = configs[v].Movement.HalfExtents;
                if (MathF.Abs(target.Position.X - ball.Position.X) < half.X + tuning.ExplosionRadius
                    && MathF.Abs(target.Position.Y - ball.Position.Y) < half.Y + tuning.ExplosionRadius)
                {
                    int facing = target.Position.X >= ball.Position.X ? 1 : -1;
                    hits.Add(new HitRecord(ball.Owner, v, ball.Slot, 0, tuning.SmokeHit, facing, AttackPower.Normal, fromObject: true));
                }
            }
            AddSmoke(objects, ball.Owner, ball.Slot, ball.Position, tuning.SmokeRadius, tuning.SmokeTicks, tick, 0);
        }

        private static void AddSmoke(WorldObjects objects, int owner, AttackSlot slot, Vector2 position, float radius, int ticks, int tick, byte variant)
        {
            objects.Add(new WorldObject
            {
                Kind = WorldObjectKind.Smoke, Owner = owner, Slot = slot, Position = position, End = new Vector2(radius, 0f),
                SpawnTick = tick, ExpireTick = tick + ticks, Variant = variant,
            });
        }

        // 物を速度のまま進める（SweepStep ずつ）。地形か持ち主以外のキャラクターに触れたら、そこで止めて true（reached は触れたか、victim は
        // 触れたキャラクター（地形なら -1））。ステージの外は呼ぶ側が調べる。
        private static bool Sweep(ref WorldObject item, ICollisionQuery terrain, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            float radius, out bool reached, out int victim)
        {
            reached = false;
            victim = -1;
            Vector2 delta = item.Velocity * SimTime.DeltaTime;
            float distance = delta.Length();
            if (distance <= SimMath.Epsilon)
            {
                return false;
            }
            Vector2 direction = delta / distance;
            int steps = Math.Max(1, (int)MathF.Ceiling(distance / SweepStep));
            float step = distance / steps;
            for (int s = 0; s < steps; s++)
            {
                var origin = new Vector3(item.Position.X, item.Position.Y, 0f);
                if (terrain.Raycast(origin, new Vector3(direction.X, direction.Y, 0f), step, out CastHit wall))
                {
                    item.Position = new Vector2(wall.Point.X, wall.Point.Y);
                    reached = true;
                    return true;
                }
                item.Position += direction * step;
                victim = TouchingCharacter(item, states, configs, radius);
                if (victim >= 0)
                {
                    reached = true;
                    return true;
                }
            }
            return false;
        }

        // 物 item に触れている、持ち主以外のいちばん番号の小さいキャラクター（いなければ -1）。
        private static int TouchingCharacter(in WorldObject item, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, float radius)
        {
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState target = states[v];
                if (v == item.Owner || target.Combat.IsUntouchable)
                {
                    continue;
                }
                Vector3 half = configs[v].Movement.HalfExtents;
                if (MathF.Abs(target.Position.X - item.Position.X) < half.X + radius && MathF.Abs(target.Position.Y - item.Position.Y) < half.Y + radius)
                {
                    return v;
                }
            }
            return -1;
        }

        // タレントスナッチの弾: まっすぐ進み、キャラクター・奪えるギミック・仕掛けに当たれば奪う。地形・ステージの外・届く距離の終わりで外れる。
        private static void StepSnatchShot(ref WorldObject shot, ICollisionQuery terrain, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            StageBounds? bounds, int tick, ThiefContext context, StageState stage)
        {
            ThiefTuning tuning = configs[shot.Owner].Skills.Thief;
            Vector2 delta = shot.Velocity * SimTime.DeltaTime;
            float distance = delta.Length();
            Vector2 direction = distance > SimMath.Epsilon ? delta / distance : Vector2.UnitX;
            int steps = Math.Max(1, (int)MathF.Ceiling(distance / SweepStep));
            float step = distance / steps;
            for (int s = 0; s < steps; s++)
            {
                var origin = new Vector3(shot.Position.X, shot.Position.Y, 0f);
                if (terrain.Raycast(origin, new Vector3(direction.X, direction.Y, 0f), step, out CastHit wall))
                {
                    shot.Position = new Vector2(wall.Point.X, wall.Point.Y);
                    Miss(ref shot, states);
                    return;
                }
                shot.Position += direction * step;
                shot.ChargeMilli += (long)MathF.Round(step * 1000f);
                int victim = TouchingCharacter(shot, states, configs, tuning.SnatchRadius);
                if (victim >= 0)
                {
                    // ほかの THIEF の Q（タレントスナッチ）はコピーしない（外れたのと同じ）。
                    if (IsThief(configs[victim]))
                    {
                        Miss(ref shot, states);
                    }
                    else
                    {
                        Steal(ref shot, states, StealKind.Skill, victim, Math.Max(1, configs[victim].Combat.Special1MaxCharges));
                    }
                    return;
                }
                if (context != null)
                {
                    int zone = StealableZoneAt(context.Zones, shot.Position, tuning.SnatchRadius, tick, context.Rules.Seed);
                    if (zone >= 0)
                    {
                        Steal(ref shot, states, StealKind.Zone, zone, 1);
                        return;
                    }
                    int trap = TrapAt(context.Traps, stage, shot.Position, tuning, tick);
                    if (trap >= 0)
                    {
                        Steal(ref shot, states, StealKind.Trap, trap, 1);
                        return;
                    }
                }
                if (shot.ChargeMilli >= (long)(tuning.SnatchRange * 1000f)
                    || (bounds.HasValue && !bounds.Value.Contains(new Vector3(shot.Position.X, shot.Position.Y, 0f))))
                {
                    Miss(ref shot, states);
                    return;
                }
            }
        }

        // 弾が外れた: 消して、クールタイムを数え始める。
        private static void Miss(ref WorldObject shot, List<CharacterState> states)
        {
            shot.Ended = true;
            CharacterState owner = states[shot.Owner];
            if (owner.Combat.Special1CooldownTicksLeft == CombatState.CooldownWhileActive && owner.Combat.StealKind == StealKind.None)
            {
                // クールタイムは、持ち主のもとの Q のもの（ThiefRules の外から渡さないので、ここでは決めず、AfterStep が数え始める）。
                owner.Combat.Special1CooldownTicksLeft = MissedCooldownMarker;
                states[shot.Owner] = owner;
            }
        }

        /// <summary>弾が外れたことを AfterStep に知らせる、クールタイムの印（AfterStep がもとのクールタイムにする）。</summary>
        private const int MissedCooldownMarker = -2;

        // 弾が当たって奪った: 消して、持ち主に奪ったものを持たせ、Q をすぐ使えるようにする（タレントスナッチを撃っている途中なら、その攻撃が
        // 終わるまでは、もとの Q のまま）。
        private static void Steal(ref WorldObject shot, List<CharacterState> states, StealKind kind, int source, int uses)
        {
            shot.Ended = true;
            shot.Stored = 1;
            CharacterState owner = states[shot.Owner];
            if (owner.Combat.IsWaitingToRespawn)
            {
                return;
            }
            owner.Combat.StealKind = kind;
            owner.Combat.StealSource = source;
            owner.Combat.StealUses = uses;
            owner.Combat.StealSerial = 0;
            owner.Combat.StealFromSerial = owner.Combat.AttackSerial;
            owner.Combat.Special1CooldownTicksLeft = 0;
            owner.Combat.StealCount++;
            states[shot.Owner] = owner;
        }

        /// <summary>奪えるステージのギミックの種類か（広い所を覆う物と、動く足場・扉は奪えない。ユーザーの回答、2026-10-28）。</summary>
        public static bool IsStealable(in StageZone zone)
        {
            switch (zone.Kind)
            {
                case StageZoneKind.JumpPad:
                case StageZoneKind.Updraft:
                case StageZoneKind.BoostRing:
                case StageZoneKind.SpeedLane:
                case StageZoneKind.Launcher:
                case StageZoneKind.Bumper:
                case StageZoneKind.Hazard:
                    return true;
                case StageZoneKind.Gust:
                    // 周期のある風（つむじ風）だけ。いつも吹いているビル風・下降気流は奪えない。
                    return zone.Period > 0;
                default:
                    return false;
            }
        }

        // 点 point（半径 radius）が触れている、奪えるギミックの番号（なければ -1）。危険は、噴き出している柱だけ。
        private static int StealableZoneAt(IReadOnlyList<StageZone> zones, Vector2 point, float radius, int tick, int seed)
        {
            var position = new Vector3(point.X, point.Y, 0f);
            var half = new Vector2(radius, radius);
            for (int i = 0; i < zones.Count; i++)
            {
                StageZone zone = zones[i].At(tick);
                if (!IsStealable(zone) || !zone.Overlaps(position, half))
                {
                    continue;
                }
                if (zone.Kind == StageZoneKind.Hazard)
                {
                    if (!StageZones.HazardColumnAt(zone, tick, seed, out float minX, out float maxX) || point.X + radius < minX || point.X - radius > maxX)
                    {
                        continue;
                    }
                }
                return i;
            }
            return -1;
        }

        // 点 point が触れている仕掛けの番号（なければ -1）: 発射装置・ピッチャーマシンの本体か、赤外線のレーダー（撃った後の待ちの間は消えて
        // いるので、本体だけ）。
        private static int TrapAt(IReadOnlyList<StageTrap> traps, StageState stage, Vector2 point, ThiefTuning tuning, int tick)
        {
            for (int i = 0; i < traps.Count; i++)
            {
                StageTrap trap = traps[i];
                if (Vector2.Distance(point, trap.Muzzle) <= tuning.TrapBodyRadius)
                {
                    return i;
                }
                bool reloading = stage != null && StageTraps.TryGetReload(trap, stage, i, tick, out _, out _);
                if (trap.Kind == StageTrapKind.Tripwire && !reloading && point.X >= trap.BeamMin.X - tuning.SnatchRadius
                    && point.X <= trap.BeamMax.X + tuning.SnatchRadius && point.Y >= trap.BeamMin.Y - tuning.SnatchRadius
                    && point.Y <= trap.BeamMax.Y + tuning.SnatchRadius)
                {
                    return i;
                }
            }
            return -1;
        }

        // ---------------------------------------------------------------- 置く

        // 奪ったギミック・仕掛けを、照準の方向の目の前に置く（持ち主の置いた物が上限なら、いちばん古いものを消す）。
        private static void Place(WorldObjects objects, in CharacterState state, int index, CharacterConfig config, int tick, ThiefContext context)
        {
            CombatState c = state.Combat;
            if (context == null || (c.StealKind != StealKind.Zone && c.StealKind != StealKind.Trap))
            {
                return;
            }
            ThiefTuning tuning = config.Skills.Thief;
            Vector2 direction = SkillRules.AimDirection(state.Action.Aim, state.Facing, config.Wire.AimDeadzone);
            var center = new Vector2(state.Position.X, state.Position.Y);
            Vector2 body = new Vector2(config.Movement.HalfWidth, config.Movement.HalfHeight);
            WorldObject placed;
            if (c.StealKind == StealKind.Zone)
            {
                if (c.StealSource < 0 || c.StealSource >= context.Zones.Count)
                {
                    return;
                }
                StageZone source = context.Zones[c.StealSource];
                Vector2 half = PlacedHalfSize(source, tuning);
                // 体の縁から PlaceGap 先に、置く物の手前の縁がくるように（照準の方向に沿って）。
                float reach = MathF.Abs(direction.X) * (body.X + half.X) + MathF.Abs(direction.Y) * (body.Y + half.Y) + tuning.PlaceGap;
                placed = new WorldObject
                {
                    Kind = WorldObjectKind.PlacedZone, Owner = index, Slot = AttackSlot.Special1, Position = center + direction * reach,
                    End = half, Stored = c.StealSource, Variant = Mirrors(source.Direction, direction) ? (byte)1 : (byte)0,
                    SpawnTick = tick, ExpireTick = int.MaxValue,
                };
            }
            else
            {
                if (c.StealSource < 0 || c.StealSource >= context.Traps.Count)
                {
                    return;
                }
                StageTrap source = context.Traps[c.StealSource];
                Vector2 aimOf = source.Kind == StageTrapKind.Tripwire ? source.Direction : source.LaunchVelocity;
                placed = new WorldObject
                {
                    Kind = WorldObjectKind.PlacedTrap, Owner = index, Slot = AttackSlot.Special1,
                    Position = center + direction * tuning.TrapPlaceDistance, Stored = c.StealSource,
                    Variant = Mirrors(aimOf, direction) ? (byte)1 : (byte)0, SpawnTick = tick, ExpireTick = int.MaxValue,
                    // 置いた瞬間に 1 回撃つ（赤外線は、予告を済ませた形にする。0 は待っている印なので避ける）。
                    ChargeMilli = source.Kind == StageTrapKind.Tripwire ? (tick - source.ArmTicks != 0 ? tick - source.ArmTicks : -1) : 0,
                };
            }
            RemoveOldestPlaced(objects, index, tuning.MaxPlaced - 1);
            objects.Add(placed);
        }

        // 持ち主 owner の置いた物（ギミック・仕掛け）が keep を超えていれば、古いものから消す。
        private static void RemoveOldestPlaced(WorldObjects objects, int owner, int keep)
        {
            int count = 0;
            for (int o = objects.Count - 1; o >= 0; o--)
            {
                WorldObject item = objects[o];
                if (item.Owner != owner || item.Ended || (item.Kind != WorldObjectKind.PlacedZone && item.Kind != WorldObjectKind.PlacedTrap))
                {
                    continue;
                }
                count++;
                if (count > keep)
                {
                    item.Ended = true;
                    objects[o] = item;
                }
            }
        }

        // 元の向き source を、置く向き aim の側へ左右に反転するか（横の向きを持ち、照準と逆を向いているとき）。
        private static bool Mirrors(Vector2 source, Vector2 aim)
        {
            return MathF.Abs(source.X) > 0.01f && MathF.Abs(aim.X) > 0.01f && MathF.Sign(source.X) != MathF.Sign(aim.X);
        }

        /// <summary>置いたギミックの半分の大きさ（大きいものは縮める。危険は柱 1 本の幅）。</summary>
        public static Vector2 PlacedHalfSize(in StageZone source, ThiefTuning tuning)
        {
            float width = source.MaxX - source.MinX;
            float height = source.MaxY - source.MinY;
            if (source.Kind == StageZoneKind.Hazard)
            {
                return new Vector2(source.Width * 0.5f, MathF.Min(height, tuning.PlaceMaxHazardHeight) * 0.5f);
            }
            return new Vector2(MathF.Min(width, tuning.PlaceMaxSize) * 0.5f, MathF.Min(height, tuning.PlaceMaxSize) * 0.5f);
        }

        /// <summary>
        /// 置いたギミック item（PlacedZone）の、ステージのギミックとしての値。元のギミック source の種類・強さ・周期で、置いた位置と大きさ、
        /// 左右の反転にする。周期のあるものは、置いたティックに効き始める（ばらつきはなくす）。動かない。
        /// </summary>
        public static StageZone PlacedZone(in StageZone source, in WorldObject item)
        {
            Vector2 half = item.End;
            Vector2 direction = item.Variant == 1 ? new Vector2(-source.Direction.X, source.Direction.Y) : source.Direction;
            StageZoneTiming timing = source.Timing;
            if (timing.Period > 0)
            {
                int phase = ((timing.Period - item.SpawnTick % timing.Period) % timing.Period + timing.Period) % timing.Period;
                timing = new StageZoneTiming(timing.Period, timing.ActiveTicks, phase, 0, Math.Min(timing.WarningTicks, timing.Period - timing.ActiveTicks));
            }
            float width = source.Kind == StageZoneKind.Hazard ? half.X * 2f : 0f;
            return new StageZone(source.Kind, item.Position.X - half.X, item.Position.Y - half.Y, item.Position.X + half.X, item.Position.Y + half.Y,
                direction, source.Power, source.Limit, timing, source.Target, width);
        }

        /// <summary>
        /// 効かせるギミックの一覧 active を、ステージのギミック stageZones と、置いたギミック（objects の PlacedZone。番号の順）にする。
        /// SimWorld が毎ティックの初めに呼ぶ。
        /// </summary>
        public static void RefreshZones(List<StageZone> active, IReadOnlyList<StageZone> stageZones, WorldObjects objects)
        {
            int keep = stageZones.Count;
            if (active.Count > keep)
            {
                active.RemoveRange(keep, active.Count - keep);
            }
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject item = objects[o];
                if (item.Kind == WorldObjectKind.PlacedZone && !item.Ended && item.Stored >= 0 && item.Stored < keep)
                {
                    active.Add(PlacedZone(stageZones[item.Stored], item));
                }
            }
        }

        /// <summary>置いた仕掛けの、向き（長さ 1。赤外線のミサイル）か打ち出す速度（ピッチャーマシン）。左右の反転を当てる。</summary>
        public static Vector2 PlacedTrapAim(StageTrap source, in WorldObject item)
        {
            Vector2 aim = source.Kind == StageTrapKind.Tripwire ? source.Direction : source.LaunchVelocity;
            return item.Variant == 1 ? new Vector2(-aim.X, aim.Y) : aim;
        }

        /// <summary>置いた赤外線のレーダーの範囲（元の発射口からの位置を、置いた発射口へ移して、左右の反転を当てる）。</summary>
        public static void PlacedBeam(StageTrap source, in WorldObject item, out Vector2 min, out Vector2 max)
        {
            Vector2 a = source.BeamMin - source.Muzzle;
            Vector2 b = source.BeamMax - source.Muzzle;
            if (item.Variant == 1)
            {
                a.X = -a.X;
                b.X = -b.X;
            }
            min = item.Position + Vector2.Min(a, b);
            max = item.Position + Vector2.Max(a, b);
        }

        /// <summary>置いた赤外線のミサイルが、ティック tick に飛んでいるか（位置）、予告の間か。</summary>
        public static bool TryGetPlacedMissile(StageTrap source, in WorldObject item, int tick, out bool arming, out Vector2 position)
        {
            arming = false;
            position = item.Position;
            int tripped = (int)item.ChargeMilli;
            if (source.Kind != StageTrapKind.Tripwire || item.ChargeMilli == 0)
            {
                return false;
            }
            int age = tick - (tripped + source.ArmTicks);
            if (age < 0)
            {
                arming = true;
                return true;
            }
            if (item.HitMask != 0 || age > source.FlightTicks)
            {
                return false;
            }
            position = PlacedMissileAt(source, item, age);
            return true;
        }

        private static Vector2 PlacedMissileAt(StageTrap source, in WorldObject item, int age)
        {
            float distance = MathF.Min(source.Range, source.Speed * age * SimTime.DeltaTime);
            return item.Position + PlacedTrapAim(source, item) * distance;
        }

        // 置いた仕掛け: 赤外線は、待っている間にレーダーに触れられたら予告の後に撃ち、ミサイルは最初に当たった人で消える（置いた瞬間に 1 回
        // 撃つ）。ピッチャーマシンは、置いたティックから周期ごとにボールを打ち出す（置いた人にも当たる。ステージの仕掛けと同じ）。
        private static void StepPlacedTrap(ref WorldObject item, WorldObjects objects, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            int tick, ThiefContext context)
        {
            if (context == null || item.Stored < 0 || item.Stored >= context.Traps.Count)
            {
                item.Ended = true;
                return;
            }
            StageTrap source = context.Traps[item.Stored];
            if (source.Kind == StageTrapKind.Pitcher)
            {
                if ((tick - item.SpawnTick) % source.Period == 0)
                {
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.TrapBall, Owner = item.Owner, Slot = item.Slot, Position = item.Position,
                        Velocity = PlacedTrapAim(source, item), SpawnTick = tick,
                        ExpireTick = tick + StageTrap.BallMaxFlightTicks + source.BlinkTicks, Variant = (byte)item.Stored,
                    });
                }
                return;
            }
            int tripped = (int)item.ChargeMilli;
            if (item.ChargeMilli != 0 && tick >= tripped + source.ArmTicks + source.FlightTicks + source.CooldownTicks)
            {
                item.ChargeMilli = 0;
                item.HitMask = 0;
                tripped = 0;
            }
            if (item.ChargeMilli == 0)
            {
                PlacedBeam(source, item, out Vector2 min, out Vector2 max);
                for (int c = 0; c < states.Count; c++)
                {
                    if (StageZones.CanBeStruck(states[c]) && Overlaps(states[c].Position, Half(configs[c]), min, max))
                    {
                        item.ChargeMilli = tick;
                        return;
                    }
                }
                return;
            }
            int age = tick - (tripped + source.ArmTicks);
            if (age <= 0 || age > source.FlightTicks || item.HitMask != 0)
            {
                return;
            }
            Vector2 from = PlacedMissileAt(source, item, age - 1);
            Vector2 to = PlacedMissileAt(source, item, age);
            int victim = -1;
            float best = float.MaxValue;
            for (int c = 0; c < states.Count; c++)
            {
                CharacterState state = states[c];
                if (!StageZones.CanBeStruck(state))
                {
                    continue;
                }
                Vector2 half = Half(configs[c]) + source.HalfSize;
                var p = new Vector2(state.Position.X, state.Position.Y);
                if (StageTraps.SweptBox(from, to, p - half, p + half, out float along) && along < best)
                {
                    best = along;
                    victim = c;
                }
            }
            if (victim < 0)
            {
                return;
            }
            Vector2 aim = PlacedTrapAim(source, item);
            CharacterState hit = states[victim];
            var velocity = new Vector3(aim.X * source.Knockback, aim.Y * source.Knockback + source.Knockback * StageTraps.MissileLiftRatio, 0f);
            StageZones.Strike(ref hit, velocity, source.Power, StageTraps.StrikeImmunityTicks, configs[victim].Wire);
            states[victim] = hit;
            item.HitMask = 1;
        }

        // 置いたピッチャーマシンのボール: 重力で落ち、地形で跳ね、止まったら点滅して消える（StageTrap と同じ動き）。触れた人は弾き返し、
        // 速いボールなら吹き飛ばす（StageTraps と同じ）。
        private static void StepTrapBall(ref WorldObject ball, ICollisionQuery ground, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            StageBounds? bounds, int tick, ThiefContext context)
        {
            int index = ball.Variant;
            if (context == null || index >= context.Traps.Count)
            {
                ball.Ended = true;
                return;
            }
            StageTrap source = context.Traps[index];
            if (ball.ChargeMilli == 0)
            {
                bool rolling = ball.Stored == 1;
                var half = new Vector3(source.HalfSize.X, source.HalfSize.Y, 0.5f);
                StageTrap.StepBall(ground, half, ref ball.Position, ref ball.Velocity, ref rolling);
                ball.Stored = rolling ? 1 : 0;
                if ((rolling && MathF.Abs(ball.Velocity.X) < StageTrap.BallStopSpeed) || tick - ball.SpawnTick >= StageTrap.BallMaxFlightTicks)
                {
                    ball.ChargeMilli = tick;
                    ball.ExpireTick = tick + source.BlinkTicks;
                }
                if (bounds.HasValue && !bounds.Value.Contains(new Vector3(ball.Position.X, ball.Position.Y, 0f)))
                {
                    ball.Ended = true;
                    return;
                }
            }
            if (ball.ChargeMilli != 0)
            {
                // 点滅している間は当たらない。
                return;
            }
            StageTraps.StrikeWithBall(source, ball.Position, ball.Velocity, states, configs);
        }

        /// <summary>置いたピッチャーマシンのボールが点滅しているか（止まって消えるまで）。</summary>
        public static bool IsBlinking(in WorldObject ball)
        {
            return ball.Kind == WorldObjectKind.TrapBall && ball.ChargeMilli != 0;
        }

        private static Vector2 Half(CharacterConfig config)
        {
            return new Vector2(config.Movement.HalfWidth, config.Movement.HalfHeight);
        }

        private static bool Overlaps(Vector3 position, Vector2 half, Vector2 min, Vector2 max)
        {
            return position.X + half.X >= min.X && position.X - half.X <= max.X && position.Y + half.Y >= min.Y && position.Y - half.Y <= max.Y;
        }

        // ---------------------------------------------------------------- 分身（ミスディレクション）

        /// <summary>
        /// 分身を進める（SimWorld が全員を進めた後に呼ぶ。分身は E を出したティックに Fire が作る）。分身は移動とワイヤーだけ（CloneInput）。
        /// ステージの外に出たら消える。
        /// </summary>
        public static void StepClones(List<CharacterState> clones, List<CharacterState> states, CharacterSimulator simulator,
            IReadOnlyList<CharacterConfig> configs, ICollisionQuery collision, StageBounds? bounds, int tick, int seed)
        {
            for (int i = 0; i < states.Count && i < clones.Count; i++)
            {
                CharacterState owner = states[i];
                if (owner.Combat.CloneTicksLeft <= 0)
                {
                    continue;
                }
                CharacterConfig config = configs[i];
                CharacterState clone = clones[i];
                PlayerInput input = CloneInput(clone, config, collision, bounds, i, tick, seed);
                simulator.Step(ref clone, input, config, tick, seed);
                clones[i] = clone;
                if (bounds.HasValue && !bounds.Value.Contains(clone.Position))
                {
                    owner.Combat.CloneTicksLeft = 0;
                    states[i] = owner;
                }
            }
        }

        // 分身: 持ち主の今の位置・慣性・向き・接地（攻撃・ワイヤー・技の状態は持たない）。
        private static CharacterState SpawnClone(in CharacterState owner)
        {
            CharacterState clone = CharacterState.Spawn(owner.Position, owner.Facing);
            clone.Velocity = owner.Velocity;
            clone.Grounded = owner.Grounded;
            clone.AirJumpsUsed = owner.AirJumpsUsed;
            clone.PreviousButtons = InputButtons.None;
            return clone;
        }

        /// <summary>
        /// 分身の入力（EASY の CPU のような、移動とワイヤーだけの簡単な動き。分身の状態とティックだけから決まる）: 向いている方向へ進み、
        /// ときどき向きを変え、ステージの端の近くでは内側を向く。前がふさがっていたり足場が途切れたりしたら跳び、落ちていたら空中ジャンプ、
        /// それも使い切っていたら斜め上へワイヤーを撃って、少しつないでから離す。
        /// </summary>
        public static PlayerInput CloneInput(in CharacterState clone, CharacterConfig config, ICollisionQuery collision, StageBounds? bounds, int owner,
            int tick, int seed)
        {
            ThiefTuning tuning = config.Skills.Thief;
            MovementTuning movement = config.Movement;
            float direction = clone.Facing >= 0 ? 1f : -1f;
            if (tick % tuning.CloneTurnIntervalTicks == 0
                && StableHash.Range(seed, CloneSalt + owner, tick / tuning.CloneTurnIntervalTicks, 100) < tuning.CloneTurnPercent)
            {
                direction = -direction;
            }
            if (bounds.HasValue)
            {
                StageBounds b = bounds.Value;
                if (clone.Position.X < b.MinX + 8f)
                {
                    direction = 1f;
                }
                else if (clone.Position.X > b.MaxX - 8f)
                {
                    direction = -1f;
                }
            }
            InputButtons buttons = InputButtons.None;
            Vector2 aim = Vector2.Zero;
            bool jumpHeld = (clone.PreviousButtons & InputButtons.Jump) != 0;
            bool wireHeld = (clone.PreviousButtons & InputButtons.Wire) != 0;
            var origin = clone.Position;
            var ahead = new Vector3(direction, 0f, 0f);
            bool blocked = collision.Raycast(origin, ahead, movement.HalfWidth + 0.8f, out _);
            bool ledge = clone.Grounded
                && !collision.Raycast(origin + ahead * (movement.HalfWidth + 1.2f), -Vector3.UnitY, movement.HalfHeight + 1.5f, out _);
            bool wantJump = clone.Grounded ? blocked || ledge
                : clone.Velocity.Y < -6f && clone.AirJumpsUsed < movement.AirJumps;
            if (wantJump && !jumpHeld && clone.Wire.Phase != WirePhase.Attached)
            {
                buttons |= InputButtons.Jump;
            }
            switch (clone.Wire.Phase)
            {
                case WirePhase.Ready:
                    if (!clone.Grounded && !wireHeld && clone.Velocity.Y < -8f && clone.AirJumpsUsed >= movement.AirJumps)
                    {
                        Vector2 up = Vector2.Normalize(new Vector2(direction * 0.55f, 0.85f));
                        WireShotPreview preview = WireTargeting.PredictShot(origin, up, clone.Facing, config.Wire, collision);
                        if (preview.Result == WireShotResult.Attach)
                        {
                            buttons |= InputButtons.Wire;
                            aim = up;
                        }
                    }
                    break;
                case WirePhase.Firing:
                    buttons |= InputButtons.Wire;
                    break;
                case WirePhase.Attached:
                    if (clone.Wire.PhaseTicks < tuning.CloneWireHoldTicks && clone.Position.Y < clone.Wire.Anchor.Y - 1.2f)
                    {
                        buttons |= InputButtons.Wire;
                    }
                    break;
            }
            PlayerInput input = PlayerInput.FromAxes(direction, 0f, buttons);
            return aim != Vector2.Zero ? input.WithAim(aim) : input;
        }

        /// <summary>
        /// 分身に、ほかの人の攻撃の判定か、ステージの危険が当たったら、煙になって消す（透明はそのまま。ユーザーの回答、2026-10-28）。
        /// SimWorld が攻撃の命中の後に呼ぶ。
        /// </summary>
        public static void PopClones(List<CharacterState> clones, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            WorldObjects objects, int tick)
        {
            for (int i = 0; i < states.Count && i < clones.Count; i++)
            {
                CharacterState owner = states[i];
                if (owner.Combat.CloneTicksLeft <= 0)
                {
                    continue;
                }
                CharacterState clone = clones[i];
                bool popped = clone.Action.Kind == ActionKind.Hitstun || TouchedByAttack(clone, configs[i], states, configs, i);
                if (!popped)
                {
                    continue;
                }
                owner.Combat.CloneTicksLeft = 0;
                owner.Combat.ClonePopCount++;
                states[i] = owner;
                ThiefTuning tuning = configs[i].Skills.Thief;
                AddSmoke(objects, i, AttackSlot.Special2, new Vector2(clone.Position.X, clone.Position.Y), tuning.PuffRadius, tuning.PuffTicks, tick, 1);
            }
        }

        // 分身 clone（持ち主 owner の調整値 config）に、持ち主以外の人の、今出ている攻撃の判定が触れているか。
        private static bool TouchedByAttack(in CharacterState clone, CharacterConfig config, List<CharacterState> states,
            IReadOnlyList<CharacterConfig> configs, int owner)
        {
            var center = new Vector2(clone.Position.X, clone.Position.Y);
            var half = new Vector2(config.Movement.HalfWidth, config.Movement.HalfHeight);
            for (int a = 0; a < states.Count; a++)
            {
                CharacterState attacker = states[a];
                if (a == owner || attacker.Action.Kind != ActionKind.Attack || attacker.Combat.IsUntouchable)
                {
                    continue;
                }
                AttackData attack = configs[a].Combat.Get(attacker.Action.Attack);
                foreach (AttackHit hit in attack.Hits)
                {
                    if (!hit.IsActiveAt(attacker.Action.Ticks) || !attack.Uses(hit.Charge, attacker.Action.Charge))
                    {
                        continue;
                    }
                    HitResolver.Hitbox(attacker, hit, out Vector2 hitCenter, out Vector2 hitHalf);
                    if (HitShapes.Overlaps(hitCenter, hitHalf, HitResolver.HitAngle(attacker, hit), center, half))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- 詠唱（バンクハイスト）

        /// <summary>
        /// 詠唱を進める（SimWorld が拠点の確定の後に呼ぶ。pressed はこのティックに押したボタン）。もう一度 R を押したか、拠点が移ったら、
        /// 詠唱した長さのコインを得る（試合中だけ）。のけぞったら（CharacterSimulator が止める）得ない。
        /// </summary>
        public static void StepHeist(CoinLedger ledger, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, ThiefContext context,
            int tick, bool playing, IReadOnlyList<InputButtons> pressed)
        {
            for (int i = 0; i < states.Count; i++)
            {
                CharacterState state = states[i];
                if (state.Combat.HeistTicks <= 0)
                {
                    continue;
                }
                if (state.Combat.IsWaitingToRespawn || state.Action.Kind == ActionKind.Hitstun)
                {
                    state.Combat.HeistTicks = 0;
                    states[i] = state;
                    continue;
                }
                bool again = state.Combat.HeistTicks > 1 && pressed != null && i < pressed.Count && (pressed[i] & InputButtons.Super) != 0;
                bool moved = context != null && context.BankSpotAt(tick) != state.Combat.HeistSpot;
                if (!again && !moved)
                {
                    state.Combat.HeistTicks++;
                    states[i] = state;
                    continue;
                }
                int coins = configs[i].Skills.Thief.HeistCoins(state.Combat.HeistTicks - 1);
                state.Combat.HeistTicks = 0;
                state.Combat.HeistPayoutCount++;
                state.Combat.HeistLastPayout = playing ? coins : 0;
                states[i] = state;
                if (playing && coins > 0)
                {
                    ledger.Reward(new CoinEventId(tick, CoinEventKind.Heist, i), states, i, coins);
                }
            }
        }

        // ---------------------------------------------------------------- ティックの終わり

        /// <summary>
        /// ティックの終わりの片付け（SimWorld が呼ぶ。configs は今の調整値、baseConfigs はもとの調整値、pressed はこのティックに押したボタン）:
        /// - コピーした技・奪ったギミック: 使い始めたら回数を減らし、使い終えたら（攻撃が終わり、連続技も終わったら）、もとの Q のクールタイムを
        ///   始める。弾が外れたときも、もとの Q のクールタイムを始める。
        /// - 透明と分身: 残りを減らし、R 以外の行動（攻撃・技・ガード・ワイヤー）・のけぞり・撃破・もう一度の E で解く。
        /// - 拠点の近くか（R を出せるか）を決める。
        /// </summary>
        public static void AfterStep(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, IReadOnlyList<CharacterConfig> baseConfigs,
            IReadOnlyList<InputButtons> pressed, ThiefContext context, int tick, bool playing)
        {
            for (int i = 0; i < states.Count; i++)
            {
                CharacterConfig baseConfig = baseConfigs[i];
                if (!IsThief(baseConfig))
                {
                    continue;
                }
                CharacterState state = states[i];
                UpdateSteal(ref state, baseConfig);
                UpdateInvisibility(ref state, baseConfig, pressed != null && i < pressed.Count ? pressed[i] : InputButtons.None);
                state.Combat.NearBank = playing && context != null && NearBank(state, context, baseConfig, tick);
                states[i] = state;
            }
        }

        private static void UpdateSteal(ref CharacterState state, CharacterConfig baseConfig)
        {
            ref CombatState c = ref state.Combat;
            if (c.Special1CooldownTicksLeft == MissedCooldownMarker)
            {
                c.Special1CooldownTicksLeft = baseConfig.Combat.Special1CooldownTicks;
            }
            if (c.StealKind == StealKind.None)
            {
                return;
            }
            bool inSpecial = state.Action.Kind == ActionKind.Attack && state.Action.Attack == AttackSlot.Special1 && c.AttackSerial != c.StealFromSerial;
            if (inSpecial && c.AttackSerial != c.StealSerial)
            {
                // コピーした技（置く技）を使い始めた（連続技の続きは数えない）。
                c.StealSerial = c.AttackSerial;
                if (c.ChainCount <= 1)
                {
                    c.StealUses--;
                }
            }
            bool busy = inSpecial || c.ChainCount > 0 || (c.Holding && c.HoldSlot == AttackSlot.Special1);
            if (c.StealUses > 0 || busy)
            {
                return;
            }
            c.StealKind = StealKind.None;
            c.StealSource = 0;
            c.StealSerial = 0;
            c.StealFromSerial = 0;
            c.ChainTicksLeft = 0;
            c.Special1Spent = 0;
            c.Special1CooldownTicksLeft = baseConfig.Combat.Special1CooldownTicks;
        }

        private static void UpdateInvisibility(ref CharacterState state, CharacterConfig baseConfig, InputButtons pressed)
        {
            ref CombatState c = ref state.Combat;
            if (c.InvisibleTicksLeft <= 0 && c.CloneTicksLeft <= 0)
            {
                return;
            }
            int full = baseConfig.Skills.Thief.MisdirectionTicks;
            bool started = c.InvisibleTicksLeft == full;
            bool acted = state.Action.Kind == ActionKind.Guard || state.Action.Kind == ActionKind.Hitstun
                || (state.Action.Kind == ActionKind.Attack && state.Action.Attack != AttackSlot.Special2 && state.Action.Attack != AttackSlot.Super)
                || (c.Holding && c.HoldSlot != AttackSlot.Super)
                || state.Wire.Phase == WirePhase.Firing || state.Wire.Phase == WirePhase.Attached
                || c.IsWaitingToRespawn || c.HasLeft;
            bool again = !started && (pressed & InputButtons.Special2) != 0;
            if (acted || again)
            {
                c.InvisibleTicksLeft = 0;
                c.CloneTicksLeft = 0;
                return;
            }
            if (c.InvisibleTicksLeft > 0)
            {
                c.InvisibleTicksLeft--;
            }
            if (c.CloneTicksLeft > 0)
            {
                c.CloneTicksLeft--;
            }
            if (c.InvisibleTicksLeft == 0)
            {
                // 透明が終わったら、分身も消える（どちらも 3 秒）。
                c.CloneTicksLeft = 0;
            }
        }

        /// <summary>キャラクター state（調整値 config）が、進めた後のティック tick の拠点の近く（ThiefTuning.HeistRange）にいるか。</summary>
        public static bool NearBank(in CharacterState state, ThiefContext context, CharacterConfig config, int tick)
        {
            int spot = context.BankSpotAt(tick);
            if (spot < 0 || state.Combat.IsWaitingToRespawn)
            {
                return false;
            }
            Vector2 bank = context.BankSpots[spot].Position;
            return Vector2.Distance(new Vector2(state.Position.X, state.Position.Y), bank) <= config.Skills.Thief.HeistRange;
        }
    }
}
