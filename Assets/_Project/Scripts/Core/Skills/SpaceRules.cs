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
    /// SPACE TRAVELER の技の規則（docs/14 1.1、C-4〜C-9、D-62、D-63）。SkillRules から呼ぶ。
    /// - ワープポータル: 置く（持ち主ごとに上限。増えたらいちばん古いものが消える）、移る（WarpThroughPortals）。
    /// - スペースミサイル: 照準の方向へまっすぐ飛び、触れたら爆発する（爆発の範囲の相手に当たり、壊せる物は壊す）。
    /// - コズミックレイ: 自分のポータル 2 つの間のビーム。一定の間隔で触れている相手に当たり、撃ち終わったときに拠点に触れていれば
    ///   手持ちのコインを全部確定する。
    /// 物は番号の順、キャラクターは番号の順に調べる（結果が順番で変わらない）。
    /// </summary>
    public static class SpaceRules
    {
        /// <summary>ワープポータルを置く。長押ししてから離していれば（Charge が EnterHoldTicks 以上）、置いたポータルにそのまま入る。</summary>
        public static void FirePortal(WorldObjects objects, List<CharacterState> states, int index, CharacterConfig config, int tick)
        {
            PortalTuning tuning = config.Skills.Portal;
            CharacterState state = states[index];
            int count = 0;
            int oldest = -1;
            for (int o = 0; o < objects.Count; o++)
            {
                if (IsLivePortalOf(objects[o], index))
                {
                    count++;
                    oldest = oldest < 0 ? o : oldest;
                }
            }
            if (count >= tuning.MaxPerOwner && oldest >= 0)
            {
                WorldObject removed = objects[oldest];
                removed.Ended = true;
                objects[oldest] = removed;
            }
            int id = objects.Add(new WorldObject
            {
                Kind = WorldObjectKind.Portal, Owner = index, Slot = state.Action.Attack,
                Position = new Vector2(state.Position.X, state.Position.Y),
                SpawnTick = tick, ExpireTick = tick + tuning.DurationTicks,
            });
            // 置いたときにいるポータルには入らない（いったん出てから入り直すと移る。ユーザーの要望、2026-10-16）。
            state.Combat.InPortalId = id;
            if (state.Action.Charge >= tuning.EnterHoldTicks && FindPartner(objects, index, id, out WorldObject partner))
            {
                Warp(ref state, partner, tuning, config.Wire);
            }
            states[index] = state;
        }

        /// <summary>スペースミサイルを、攻撃を始めたときの照準の方向へ撃つ。</summary>
        public static void FireMissile(WorldObjects objects, CharacterState state, int index, CharacterConfig config, int tick)
        {
            MissileTuning tuning = config.Skills.Missile;
            Vector2 direction = SkillRules.AimDirection(state.Action.Aim, state.Facing, config.Wire.AimDeadzone);
            objects.Add(new WorldObject
            {
                Kind = WorldObjectKind.Missile, Owner = index, Slot = state.Action.Attack,
                Position = new Vector2(state.Position.X, state.Position.Y + tuning.OffsetY) + direction * 0.6f,
                Velocity = direction * tuning.Speed,
                SpawnTick = tick, ExpireTick = tick + tuning.LifetimeTicks,
            });
        }

        /// <summary>自分のワープポータル 2 つ（古い方から新しい方）の間にビームを撃つ（2 つなければ何もしない）。</summary>
        public static void FireCosmicRay(WorldObjects objects, int index, CharacterConfig config, int tick)
        {
            int first = -1;
            int last = -1;
            for (int o = 0; o < objects.Count; o++)
            {
                if (IsLivePortalOf(objects[o], index))
                {
                    first = first < 0 ? o : first;
                    last = o;
                }
            }
            if (first < 0 || last == first)
            {
                return;
            }
            BeamTuning beam = config.Skills.Beam;
            objects.Add(new WorldObject
            {
                Kind = WorldObjectKind.Beam, Owner = index, Slot = AttackSlot.Super,
                Position = objects[first].Position, End = objects[last].Position,
                SpawnTick = tick, ExpireTick = tick + beam.DurationTicks + 1,
            });
        }

        /// <summary>ミサイルを進める。地形・ステージの外・キャラクター（撃った本人を除く）・壊せる物に触れたら爆発する。</summary>
        public static void StepMissile(ref WorldObject missile, WorldObjects objects, ICollisionQuery terrain, List<CharacterState> states,
            IReadOnlyList<CharacterConfig> configs, StageBounds? bounds, MissileTuning tuning, List<HitRecord> hits, int tick)
        {
            Vector2 delta = missile.Velocity * SimTime.DeltaTime;
            float distance = delta.Length();
            if (distance > SimMath.Epsilon)
            {
                var origin = new Vector3(missile.Position.X, missile.Position.Y, 0f);
                var direction = new Vector3(delta.X / distance, delta.Y / distance, 0f);
                if (terrain.Raycast(origin, direction, distance, out CastHit wall))
                {
                    missile.Position = new Vector2(wall.Point.X, wall.Point.Y);
                    Explode(ref missile, objects, states, configs, tuning, hits, tick);
                    return;
                }
            }
            missile.Position += delta;
            // ステージの範囲（撃破の範囲）の外に出たら爆発する（画面の端の代わり。D-63）。
            if (bounds.HasValue && !bounds.Value.Contains(new Vector3(missile.Position.X, missile.Position.Y, 0f)))
            {
                Explode(ref missile, objects, states, configs, tuning, hits, tick);
                return;
            }
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState target = states[v];
                if (v == missile.Owner || !CanBeHit(target))
                {
                    continue;
                }
                Vector3 half = configs[v].Movement.HalfExtents;
                if (MathF.Abs(target.Position.X - missile.Position.X) < half.X + tuning.Radius
                    && MathF.Abs(target.Position.Y - missile.Position.Y) < half.Y + tuning.Radius)
                {
                    Explode(ref missile, objects, states, configs, tuning, hits, tick);
                    return;
                }
            }
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject other = objects[o];
                if (other.Kind == WorldObjectKind.Safe && !other.Ended && Overlaps(other, configs, missile.Position, tuning.Radius))
                {
                    Explode(ref missile, objects, states, configs, tuning, hits, tick);
                    return;
                }
            }
        }

        /// <summary>
        /// ビームを進める。IntervalTicks ごとに、ビームに触れている相手（撃った本人を除く）と壊せる物に当たる（Hits 回まで）。
        /// </summary>
        public static void StepBeam(in WorldObject beam, WorldObjects objects, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            BeamTuning tuning, List<HitRecord> hits, int tick)
        {
            int age = tick - beam.SpawnTick;
            if (age <= 0 || age % tuning.IntervalTicks != 0 || age / tuning.IntervalTicks > tuning.Hits)
            {
                return;
            }
            Vector2 mid = (beam.Position + beam.End) * 0.5f;
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState target = states[v];
                if (v == beam.Owner || !CanBeHit(target))
                {
                    continue;
                }
                Vector3 half = configs[v].Movement.HalfExtents;
                var point = new Vector2(target.Position.X, target.Position.Y);
                if (DistanceToSegment(point, beam.Position, beam.End) <= tuning.HalfWidth + MathF.Max(half.X, half.Y))
                {
                    int facing = point.X >= mid.X ? 1 : -1;
                    hits.Add(new HitRecord(beam.Owner, v, AttackSlot.Super, 0, tuning.Hit, facing, AttackPower.Normal, fromObject: true));
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
                if (DistanceToSegment(other.Position, beam.Position, beam.End) <= tuning.HalfWidth + MathF.Max(safe.HalfWidth, safe.HalfHeight))
                {
                    other.Damage += Math.Max(1, tuning.Hit.Damage);
                    objects[o] = other;
                }
            }
        }

        /// <summary>
        /// 撃ち終わったビームの線が拠点に触れていたら、撃った人の手持ちのコインを全部確定する（C-9。試合中だけ。台帳の RayBank）。
        /// </summary>
        public static void BankByBeam(in WorldObject beam, CoinLedger ledger, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            IReadOnlyList<BankSpot> spots, MatchRules rules, int tick, bool playing)
        {
            BeamTuning tuning = configs[beam.Owner].Skills.Beam;
            if (!playing || spots == null || spots.Count == 0 || tick - beam.SpawnTick < tuning.DurationTicks)
            {
                return;
            }
            CharacterState owner = states[beam.Owner];
            if (owner.Coins.Held <= 0 || owner.Combat.IsWaitingToRespawn)
            {
                return;
            }
            Vector2 bank = spots[BankRules.SpotAt(rules, spots.Count, tick - 1)].Position;
            if (DistanceToSegment(bank, beam.Position, beam.End) <= rules.BankRadius + tuning.HalfWidth)
            {
                ledger.Bank(new CoinEventId(tick, CoinEventKind.RayBank, beam.Owner), states, beam.Owner, owner.Coins.Held);
            }
        }

        /// <summary>
        /// ワープポータルで移す（全員と、投射物・ミサイル）。外から入ったときだけ、同じ持ち主のもう 1 つのポータルへ移る（速度はそのまま）。
        /// 置いたとき・出たときにいたポータルは、いったん出てから入り直すまで移らない。移った直後の WarpCooldownTicks は移らない。
        /// </summary>
        public static void WarpThroughPortals(WorldObjects objects, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs)
        {
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState state = states[v];
                if (state.Combat.IsWaitingToRespawn)
                {
                    continue;
                }
                int inside = FindPortalAt(objects, configs, new Vector2(state.Position.X, state.Position.Y), 0f);
                if (inside < 0)
                {
                    if (state.Combat.InPortalId != 0)
                    {
                        state.Combat.InPortalId = 0;
                        states[v] = state;
                    }
                    continue;
                }
                WorldObject portal = objects[inside];
                if (portal.Id == state.Combat.InPortalId)
                {
                    continue;
                }
                state.Combat.InPortalId = portal.Id;
                if (state.Combat.WarpCooldownTicks <= 0 && FindPartner(objects, portal.Owner, portal.Id, out WorldObject partner))
                {
                    Warp(ref state, partner, configs[portal.Owner].Skills.Portal, configs[v].Wire);
                }
                states[v] = state;
            }

            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject shot = objects[o];
                if ((shot.Kind != WorldObjectKind.Projectile && shot.Kind != WorldObjectKind.Missile) || shot.Ended)
                {
                    continue;
                }
                int inside = FindPortalAt(objects, configs, shot.Position, 0f);
                if (inside < 0)
                {
                    shot.InPortalId = 0;
                }
                else if (objects[inside].Id != shot.InPortalId)
                {
                    WorldObject portal = objects[inside];
                    shot.InPortalId = portal.Id;
                    if (FindPartner(objects, portal.Owner, portal.Id, out WorldObject partner))
                    {
                        // 入ったときの向きと速さのまま、もう一方から出る。
                        shot.Position = partner.Position;
                        shot.InPortalId = partner.Id;
                    }
                }
                objects[o] = shot;
            }
        }

        /// <summary>キャラクターごとに、置いているワープポータルの数を数える（コズミックレイの条件。ティックの終わりに呼ぶ）。</summary>
        public static void CountPortals(WorldObjects objects, List<CharacterState> states)
        {
            for (int i = 0; i < states.Count; i++)
            {
                int count = 0;
                for (int o = 0; o < objects.Count; o++)
                {
                    count += IsLivePortalOf(objects[o], i) ? 1 : 0;
                }
                CharacterState state = states[i];
                if (state.Combat.PortalCount != count)
                {
                    state.Combat.PortalCount = count;
                    states[i] = state;
                }
            }
        }

        /// <summary>点 point から線分 a–b までの距離（m）。</summary>
        public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared();
            float t = lengthSquared > SimMath.Epsilon ? Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f) : 0f;
            return Vector2.Distance(point, a + ab * t);
        }

        private static bool IsLivePortalOf(in WorldObject item, int owner)
        {
            return item.Kind == WorldObjectKind.Portal && item.Owner == owner && !item.Ended;
        }

        // 持ち主 owner の、番号 exceptId 以外のポータル（いちばん古いもの）。
        private static bool FindPartner(WorldObjects objects, int owner, int exceptId, out WorldObject partner)
        {
            for (int o = 0; o < objects.Count; o++)
            {
                if (IsLivePortalOf(objects[o], owner) && objects[o].Id != exceptId)
                {
                    partner = objects[o];
                    return true;
                }
            }
            partner = default;
            return false;
        }

        // 点 point が中にあるポータル（いちばん古いもの。なければ -1）。
        private static int FindPortalAt(WorldObjects objects, IReadOnlyList<CharacterConfig> configs, Vector2 point, float margin)
        {
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject item = objects[o];
                if (item.Kind != WorldObjectKind.Portal || item.Ended)
                {
                    continue;
                }
                float radius = configs[item.Owner].Skills.Portal.Radius + margin;
                if (Vector2.DistanceSquared(point, item.Position) <= radius * radius)
                {
                    return o;
                }
            }
            return -1;
        }

        // ポータル partner へ移す（速度はそのまま）。つないでいたワイヤーは外れる。
        private static void Warp(ref CharacterState state, in WorldObject partner, PortalTuning tuning, WireTuning wire)
        {
            state.Position = new Vector3(partner.Position.X, partner.Position.Y, 0f);
            state.Combat.InPortalId = partner.Id;
            state.Combat.WarpCooldownTicks = tuning.WarpCooldownTicks;
            state.Combat.WarpCount++;
            state.Grounded = false;
            if (state.Wire.Phase == WirePhase.Attached)
            {
                WireController.Release(ref state, WireReleaseReason.Warped, wire);
            }
        }

        private static bool CanBeHit(in CharacterState state)
        {
            return state.Combat.InvincibleTicksLeft <= 0 && !state.Combat.IsWaitingToRespawn;
        }

        private static bool Overlaps(in WorldObject safe, IReadOnlyList<CharacterConfig> configs, Vector2 point, float radius)
        {
            SafeTuning tuning = configs[safe.Owner].Skills.Safe;
            return MathF.Abs(safe.Position.X - point.X) < tuning.HalfWidth + radius && MathF.Abs(safe.Position.Y - point.Y) < tuning.HalfHeight + radius;
        }

        // 爆発: 範囲の相手（撃った本人を除く）に当たり、範囲の壊せる物を壊す。威力は飛んでいた時間で上がる。
        private static void Explode(ref WorldObject missile, WorldObjects objects, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            MissileTuning tuning, List<HitRecord> hits, int tick)
        {
            missile.Ended = true;
            AttackPower power = AttackPower.OfProjectile(tuning.BonusPercentAt(tick - missile.SpawnTick), PowerSource.FlightTime);
            float reach = MathF.Max(tuning.ExplosionRadius, tuning.Radius);
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState target = states[v];
                if (v == missile.Owner || !CanBeHit(target))
                {
                    continue;
                }
                Vector3 half = configs[v].Movement.HalfExtents;
                if (BankRules.Touches(target.Position, half, missile.Position, reach))
                {
                    int facing = target.Position.X >= missile.Position.X ? 1 : -1;
                    hits.Add(new HitRecord(missile.Owner, v, missile.Slot, 0, tuning.Hit, facing, power, fromObject: true));
                }
            }
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject other = objects[o];
                if (other.Kind == WorldObjectKind.Safe && !other.Ended && Overlaps(other, configs, missile.Position, reach))
                {
                    // 壊せる物は壊す（docs/14 1.1）。
                    other.Damage = Math.Max(other.Damage, configs[other.Owner].Skills.Safe.Durability);
                    objects[o] = other;
                }
            }
        }
    }
}
