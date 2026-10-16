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
    /// 技の効果とワールドに置かれた物の規則（docs/13 T5-6、docs/14）。SimWorld が 1 ティックの中で次の順に呼ぶ:
    /// 1. FireEffects（全員を進めた後）: 攻撃のデータの効果（AttackData.Effect）を、出してから EffectTick のティックに 1 回だけ起こす
    ///    （物を置く、投射物を撃つ、強化）。
    /// 2. StepObjects: 物を進める（寿命、ミラーボールのゲージ、セーフティボックスのチャージ、投射物の移動と命中）。投射物の命中は
    ///    hits に集めて、HitResolver がキャラクターの攻撃と一緒に当てる。
    /// 3. EndObjects（命中の後）: 寿命が来た・壊れた物を取り除く（ばらまき、ワイヤーの解除、クールタイムの開始）。
    /// 物は番号の順、キャラクターは番号の順に調べる（結果が順番で変わらない）。
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
                if (attack.Effect == SkillEffect.None || state.Action.Ticks < attack.EffectTick)
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
                        StepMirrorBall(item, states, owner.Skills.MirrorBall, tick);
                        break;
                    case WorldObjectKind.Safe:
                        StepSafe(ref item, owner.Skills.Safe, tick);
                        break;
                    case WorldObjectKind.Projectile:
                        StepProjectile(ref item, objects, terrain, states, configs, bounds, owner.Skills.Projectile, hits);
                        break;
                }
                objects[o] = item;
            }
        }

        /// <summary>3. 寿命が来た・壊れた物を取り除く。tick は進めた後のティック。</summary>
        public static void EndObjects(WorldObjects objects, CoinLedger ledger, List<CharacterState> states,
            IReadOnlyList<CharacterConfig> configs, MatchRules rules, int tick)
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
                }
                objects.RemoveAt(o);
                o--;
            }
        }

        /// <summary>セーフティボックスの今の枚数（チャージの小数は切り捨て）。</summary>
        public static int SafeCoins(in WorldObject safe)
        {
            return (int)(safe.ChargeMilli / 1000);
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
                case SkillEffect.Projectile:
                {
                    ProjectileTuning shot = config.Skills.Projectile;
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.Projectile, Owner = index, Slot = state.Action.Attack,
                        Position = position + new Vector2(shot.OffsetX * state.Facing, shot.OffsetY),
                        Velocity = new Vector2(shot.VelocityX * state.Facing, shot.VelocityY),
                        SpawnTick = tick, ExpireTick = tick + shot.LifetimeTicks,
                    });
                    break;
                }
            }
        }

        // ミラーボール: つないでいる人（敵も含む）のゲージが、GaugeIntervalTicks ごとにたまる（C-14）。
        private static void StepMirrorBall(in WorldObject ball, List<CharacterState> states, MirrorBallTuning tuning, int tick)
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
                    CombatTuning.AddGauge(ref state.Combat, tuning.GaugePerInterval);
                    states[i] = state;
                }
            }
        }

        // セーフティボックス: ChargeIntervalTicks ごとに ChargeCoins 枚を足し、ChargeRatePercent % にする（上限 MaxCoins）。
        // 1/1000 枚単位の整数で計算する（PC が違っても同じ。小数まで記録し、ばらまくときに切り捨てる）。
        private static void StepSafe(ref WorldObject safe, SafeTuning tuning, int tick)
        {
            int age = tick - safe.SpawnTick;
            if (age <= 0 || age % tuning.ChargeIntervalTicks != 0)
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
                    hits.Add(new HitRecord(shot.Owner, v, shot.Slot, 0, tuning.Hit, facing, 1f, fromObject: true));
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
                    other.Damage += Math.Max(1, tuning.Hit.Damage);
                    objects[o] = other;
                    shot.Ended = true;
                    return;
                }
            }
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
