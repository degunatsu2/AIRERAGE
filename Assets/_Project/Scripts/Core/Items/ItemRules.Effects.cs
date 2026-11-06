using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Match;
using Airerage.Core.Movement;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Core.Wire;

namespace Airerage.Core.Items
{
    /// <summary>
    /// 追加のアイテム（ユーザーが選んだ 25 個。2026-11-05。docs/20_item_plan.md）の規則。パッシブは調整値（ConfigFor）と、命中・ゲージ・
    /// コイン・撃破の場所から呼ぶ関数で表す。アクティブは状態（ItemBag の反射・バリア・磁石・印）と、置く物・飛ばす物（WorldObject）で表す。
    /// </summary>
    public static partial class ItemRules
    {
        // ---------------------------------------------------------------- パッシブ（調整値）

        // 戦闘の値（ワイヤーキャンセルのクールタイム、ジャストガード、受ける威力・吹き飛び、ゲージ、コイン、復帰、撃破ドロップ、お守り、靴底）。
        private static void ApplyPassiveCombat(in ItemBag bag, CombatTuning combat, ItemTuning tuning)
        {
            int steel = bag.CountOf(ItemId.SteelWire);
            if (steel > 0)
            {
                combat.WireCancelCooldownTicks = Scale(combat.WireCancelCooldownTicks, Pow(1f - tuning.SteelWirePercent / 100f, steel), 0);
            }
            int bells = bag.CountOf(ItemId.GoldBell);
            if (bells > 0)
            {
                combat.JustGuardTicks = Scale(combat.JustGuardTicks, Pow(tuning.BellJustGuardScale, bells), 1);
            }
            int iron = bag.CountOf(ItemId.IronBoots);
            int feather = bag.CountOf(ItemId.FeatherHat);
            int charms = bag.CountOf(ItemId.AdversityCharm);
            int phoenix = bag.CountOf(ItemId.PhoenixFeather);
            combat.ItemDamageTakenPercent += charms * tuning.CharmTakenPercent;
            combat.ItemKnockbackTakenPercent += iron * tuning.IronBootsKnockbackPercent + feather * tuning.FeatherKnockbackPercent
                + charms * tuning.CharmTakenPercent;
            combat.ItemGaugePercent += bag.CountOf(ItemId.SpareBattery) * tuning.BatteryGaugePercent;
            combat.ItemCoinPickupScale *= Pow(tuning.MagnetPickupScale, bag.CountOf(ItemId.GoldMagnet));
            combat.ItemBankBonusPercent += bag.CountOf(ItemId.VaultKey) * tuning.VaultBonusPercent;
            combat.ItemRespawnReductionTicks += phoenix * tuning.PhoenixRespawnTicks;
            combat.ItemKoDropPercent = (int)MathF.Round(combat.ItemKoDropPercent * Pow(tuning.PhoenixKoDropPercent / 100f, phoenix));
            combat.ItemKoDropAll |= charms > 0;
            combat.ItemCharmDamagePercent += charms * tuning.CharmDamagePercent;
            combat.ItemCharmGaugePercent += charms * tuning.CharmGaugePercent;
            combat.ItemCharmCoinPercent += charms * tuning.CharmCoinPercent;
            combat.ItemFreezesWalls |= bag.CountOf(ItemId.SpikeSoles) > 0;
        }

        // 動きの値（スカイブーツの空中ジャンプ、鉄の長靴と羽毛の帽子の重力、スパイクの靴底の張り付き）。変えなければ movement のまま。
        private static MovementTuning PassiveMovement(in ItemBag bag, MovementTuning movement, ItemTuning tuning)
        {
            int sky = bag.CountOf(ItemId.SkyBoots);
            int iron = bag.CountOf(ItemId.IronBoots);
            int feather = bag.CountOf(ItemId.FeatherHat);
            int spikes = bag.CountOf(ItemId.SpikeSoles);
            if (sky == 0 && iron == 0 && feather == 0 && spikes == 0)
            {
                return movement;
            }
            MovementTuning copy = movement.Clone();
            copy.AirJumps += sky * tuning.SkyBootsAirJumps;
            copy.Gravity *= Pow(1f + tuning.IronBootsGravityPercent / 100f, iron) * Pow(1f + tuning.FeatherGravityPercent / 100f, feather);
            copy.WallClingMaxTicks += spikes * tuning.SpikeClingTicks;
            return copy;
        }

        // ワイヤーの値（ロングフックの射程）。変えなければ wire のまま。
        private static WireTuning PassiveWire(in ItemBag bag, WireTuning wire, ItemTuning tuning)
        {
            int hooks = bag.CountOf(ItemId.LongHook);
            if (hooks == 0)
            {
                return wire;
            }
            WireTuning copy = wire.Clone();
            copy.MaxLength *= Pow(1f + tuning.LongHookPercent / 100f, hooks);
            return copy;
        }

        // ---------------------------------------------------------------- パッシブ（命中・ゲージ・コイン・撃破から呼ぶ）

        /// <summary>
        /// キャラクター index が、確定コインの順位で最下位か（ほかに順位の上の人がいて、自分より下の人がいない。チーム対戦はチームの順位。
        /// 逆境のお守り）。
        /// </summary>
        public static bool IsLast(IReadOnlyList<CharacterState> states, int index)
        {
            int mine = CatchUpRules.RankOf(states, index);
            if (mine <= 1)
            {
                return false;
            }
            for (int i = 0; i < states.Count; i++)
            {
                if (i != index && !states[i].Combat.HasLeft && CatchUpRules.RankOf(states, i) > mine)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 命中 hit の強さに、アイテムの上乗せを足す（受けた側の受ける威力・吹き飛び、当てた側の逆境のお守り。AttackPower の上乗せ。D-64）。
        /// </summary>
        public static HitRecord AdjustHit(IReadOnlyList<CharacterState> states, IReadOnlyList<CharacterConfig> configs, HitRecord hit)
        {
            CombatTuning victim = configs[hit.Victim].Combat;
            CombatTuning attacker = configs[hit.Attacker].Combat;
            int damage = victim.ItemDamageTakenPercent;
            int knockback = victim.ItemKnockbackTakenPercent;
            if (attacker.ItemCharmDamagePercent != 0 && IsLast(states, hit.Attacker))
            {
                damage += attacker.ItemCharmDamagePercent;
            }
            return damage == 0 && knockback == 0 ? hit : hit.WithPower(hit.Power.WithBonus(damage, knockback, PowerSource.Item));
        }

        /// <summary>シャボンのバリアがあれば、命中を 1 回なくして割る（なくしたら true）。</summary>
        public static bool TryAbsorb(List<CharacterState> states, int victim)
        {
            CharacterState state = states[victim];
            if (state.Items.BarrierTicks <= 0)
            {
                return false;
            }
            state.Items.BarrierTicks = 0;
            state.Items.BarrierPopCount++;
            states[victim] = state;
            return true;
        }

        /// <summary>攻撃で得るゲージ amount に、アイテムの上乗せ（予備電池、最下位の間の逆境のお守り）を足した量。</summary>
        public static int AttackGauge(IReadOnlyList<CharacterState> states, CombatTuning combat, int attacker, int amount)
        {
            int percent = combat.ItemGaugePercent;
            if (combat.ItemCharmGaugePercent != 0 && IsLast(states, attacker))
            {
                percent += combat.ItemCharmGaugePercent;
            }
            return percent == 0 || amount <= 0 ? amount : amount * (100 + percent) / 100;
        }

        /// <summary>戦闘の報酬 amount に、最下位の間の逆境のお守りの上乗せを足した量。</summary>
        public static int RewardAmount(IReadOnlyList<CharacterState> states, CombatTuning combat, int attacker, int amount)
        {
            return combat.ItemCharmCoinPercent != 0 && amount > 0 && IsLast(states, attacker)
                ? amount * (100 + combat.ItemCharmCoinPercent) / 100
                : amount;
        }

        /// <summary>撃破ドロップの枚数（ふつうは amount。逆境のお守りなら所持の全部、不死鳥の羽根で半分ずつ。所持 held を超えない）。</summary>
        public static int KoDropAmount(int amount, int held, CombatTuning combat)
        {
            int baseAmount = combat.ItemKoDropAll ? held : amount;
            int scaled = combat.ItemKoDropPercent == 100 ? baseAmount : (baseAmount * combat.ItemKoDropPercent + 99) / 100;
            return Math.Max(0, Math.Min(held, scaled));
        }

        // ---------------------------------------------------------------- アクティブ（状態）

        // 状態の残りを 1 ティック数える（帰還のリモコンの印が消えたら、クールタイムを数え始める）。
        private static void CountStatuses(ref ItemBag bag, ItemTuning tuning)
        {
            if (bag.ReflectTicks > 0)
            {
                bag.ReflectTicks--;
            }
            if (bag.BarrierTicks > 0)
            {
                bag.BarrierTicks--;
            }
            if (bag.SlowTicks > 0)
            {
                bag.SlowTicks--;
                if (bag.SlowTicks == 0)
                {
                    bag.SlowPercent = 0;
                }
            }
            if (bag.MagnetTicks > 0)
            {
                bag.MagnetTicks--;
            }
            if (bag.MarkTicks > 0)
            {
                bag.MarkTicks--;
                if (bag.MarkTicks == 0)
                {
                    ReleaseRemote(ref bag, tuning);
                }
            }
        }

        // 帰還のリモコンの印を消し、待っていたクールタイムを数え始める。
        private static void ReleaseRemote(ref ItemBag bag, ItemTuning tuning)
        {
            bag.MarkTicks = 0;
            for (int slot = 0; slot < ItemCatalog.BagSlots; slot++)
            {
                if (bag.Get(slot) == ItemId.ReturnRemote && bag.GetCooldown(slot) == CombatState.CooldownWhileActive)
                {
                    bag.SetCooldown(slot, tuning.RemoteCooldownTicks);
                }
            }
        }

        /// <summary>帰還のリモコンの 2 回目（印へ瞬間移動）を使えるか。</summary>
        public static bool CanRecall(in ItemBag bag, ItemId id)
        {
            return id == ItemId.ReturnRemote && bag.MarkTicks > 0;
        }

        // 印へ瞬間移動する（ワイヤーは外し、勢いはなくす）。クールタイムを数え始める。
        private static void Recall(ref CharacterState state, ref ItemBag bag, int slot, CharacterConfig config, ItemTuning tuning)
        {
            WireController.Interrupt(ref state, config.Wire);
            state.Position = new Vector3(bag.Mark.X, bag.Mark.Y, 0f);
            state.Velocity = Vector3.Zero;
            state.Grounded = false;
            bag.MarkTicks = 0;
            bag.TeleportCount++;
            bag.UseCount++;
            bag.LastUsed = (byte)ItemId.ReturnRemote;
            if (bag.GetCooldown(slot) == CombatState.CooldownWhileActive)
            {
                bag.SetCooldown(slot, tuning.RemoteCooldownTicks);
            }
        }

        // 追加のアクティブアイテムを使う（置く物・飛ばす物を出すか、状態を付ける）。
        private static void ActivateMore(ref CharacterState state, ref ItemBag bag, int slot, ItemId id, int index, CharacterConfig config,
            WorldObjects objects, ICollisionQuery collision, ItemTuning tuning, int tick, Vector2 aim, Vector2 center)
        {
            float size = SizeScale(bag, tuning);
            Vector3 body = config.Movement.HalfExtents;
            switch (id)
            {
                case ItemId.GrappleBomb:
                    Spend(ref bag, slot, tuning.BombCooldownTicks, false);
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.ItemBomb, Owner = index, Position = Reach(collision, center, aim, 0.8f, tuning.BombRadius),
                        Velocity = aim * tuning.BombSpeed, SpawnTick = tick, ExpireTick = int.MaxValue, Variant = (byte)id,
                        ChargeMilli = tick + tuning.BombFlightTicks,
                    });
                    break;
                case ItemId.ChainHook:
                    Spend(ref bag, slot, tuning.HookCooldownTicks, false);
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.ChainHook, Owner = index, Position = Reach(collision, center, aim, 0.8f, tuning.HookRadius),
                        Velocity = aim * tuning.HookSpeed, SpawnTick = tick, ExpireTick = int.MaxValue, Variant = (byte)id,
                    });
                    break;
                case ItemId.FoldingPlatform:
                {
                    Spend(ref bag, slot, tuning.PlatformCooldownTicks, false);
                    var half = new Vector2(tuning.PlatformHalfWidth, tuning.PlatformHalfHeight) * size;
                    Vector2 position = center + aim * tuning.PlatformDistance;
                    if (MathF.Abs(position.X - center.X) < half.X + body.X && MathF.Abs(position.Y - center.Y) < half.Y + body.Y)
                    {
                        // 自分の体に重なるなら、足もとの下に出す（足場に埋まらないように）。
                        position = new Vector2(position.X, center.Y - body.Y - half.Y - 0.05f);
                    }
                    position = PlacementRules.FreePosition(collision, position, half, center);
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.ItemPlatform, Owner = index, Position = position, End = half, SpawnTick = tick,
                        ExpireTick = tick + tuning.PlatformTicks, Variant = (byte)id,
                    });
                    break;
                }
                case ItemId.PocketTrampoline:
                {
                    Spend(ref bag, slot, tuning.TrampolineCooldownTicks, false);
                    var half = new Vector2(tuning.TrampolineHalfWidth, tuning.TrampolineHalfHeight) * size;
                    var position = new Vector2(center.X, center.Y - body.Y + half.Y);
                    position = PlacementRules.FreePosition(collision, position, half, center);
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.Trampoline, Owner = index, Position = position, End = half, SpawnTick = tick,
                        ExpireTick = tick + tuning.TrampolineTicks, Variant = (byte)id,
                    });
                    break;
                }
                case ItemId.DecoyDoll:
                {
                    // 照準の方向の目の前に置く（ユーザーの指定、2026-11-06）。消えるのは爆発したとき（StepDecoy）なので、ExpireTick では消さない。
                    Spend(ref bag, slot, tuning.DecoyCooldownTicks, false);
                    var half = new Vector2(body.X, body.Y) * tuning.DecoySizeScale * size;
                    Vector2 position = PlacementRules.FreePosition(collision, center + aim * tuning.DecoyDistance, half, center);
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.Decoy, Owner = index, Position = position, End = half, SpawnTick = tick,
                        ExpireTick = int.MaxValue, Variant = (byte)id, Stored = aim.X < 0f ? -1 : 1,
                        // 攻撃で飛ばされる割合（%）。技の投射物からも当たるので、ItemTuning を持たない所でも使えるように人形に持たせる。
                        BonusPercent = (int)MathF.Round(tuning.DecoyKnockScale * 100f),
                    });
                    break;
                }
                case ItemId.BottledThunder:
                {
                    Spend(ref bag, slot, tuning.CloudCooldownTicks, false);
                    Vector2 position = Reach(collision, center, aim, tuning.CloudDistance, 0.8f);
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.ThunderCloud, Owner = index, Position = position,
                        End = new Vector2(tuning.CloudHalfWidth * size, tuning.CloudDepth), SpawnTick = tick,
                        ExpireTick = tick + tuning.CloudTicks + 1, Variant = (byte)id,
                    });
                    break;
                }
                case ItemId.Reflector:
                    Spend(ref bag, slot, tuning.ReflectorCooldownTicks, false);
                    bag.ReflectTicks = tuning.ReflectorTicks;
                    break;
                case ItemId.BubbleBarrier:
                    Spend(ref bag, slot, tuning.BarrierCooldownTicks, false);
                    bag.BarrierTicks = tuning.BarrierTicks;
                    break;
                case ItemId.BigMagnet:
                    Spend(ref bag, slot, tuning.BigMagnetCooldownTicks, false);
                    bag.MagnetTicks = tuning.BigMagnetTicks;
                    break;
                case ItemId.ReturnRemote:
                    // 1 回目: その場に印を置く（2 回目で印へ瞬間移動。クールタイムは、移動するか印が消えてから数える）。
                    bag.SetSpent(slot, 1);
                    bag.SetCooldown(slot, CombatState.CooldownWhileActive);
                    bag.Mark = center;
                    bag.MarkTicks = tuning.RemoteMarkTicks;
                    break;
            }
        }

        // 砂時計: 自分を中心とした円の中の相手（味方は除く）を、しばらく遅くする。
        private static void Slow(List<CharacterState> states, int index, ItemTuning tuning)
        {
            var center = new Vector2(states[index].Position.X, states[index].Position.Y);
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState other = states[v];
                if (v == index || other.Combat.IsUntouchable || other.Combat.IsAway || TeamRules.Allies(states, index, v))
                {
                    continue;
                }
                if (Vector2.DistanceSquared(new Vector2(other.Position.X, other.Position.Y), center) > tuning.HourglassRadius * tuning.HourglassRadius)
                {
                    continue;
                }
                other.Items.SlowTicks = tuning.HourglassSlowTicks;
                other.Items.SlowPercent = tuning.HourglassSlowPercent;
                states[v] = other;
            }
        }

        // 確定の印鑑: 所持コインの決まった割合を、その場で確定する。
        private static void Seal(List<CharacterState> states, int index, ref ItemBag bag, CoinLedger ledger, ItemTuning tuning, int tick)
        {
            int amount = states[index].Coins.Held * tuning.SealPercent / 100;
            if (ledger == null || amount <= 0)
            {
                return;
            }
            if (ledger.Bank(new CoinEventId(tick, CoinEventKind.ItemBank, index, 1), states, index, amount))
            {
                bag.SealCount++;
                bag.SealCoins = amount;
            }
        }

        // 状態の効果を進める（反射板で投射物を跳ね返す、大きな磁石でコインを吸い寄せる、スパイクの靴底で壁を凍らせる）。
        private static void StepStatuses(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, ItemTuning tuning, WorldObjects objects,
            CoinLedger ledger, int tick)
        {
            for (int i = 0; i < states.Count; i++)
            {
                CharacterState state = states[i];
                if (state.Combat.IsWaitingToRespawn || state.Combat.HasLeft || state.Combat.IsAway)
                {
                    continue;
                }
                bool changed = false;
                if (state.Items.ReflectTicks > 0)
                {
                    int reflected = Reflect(objects, states, i, tuning);
                    if (reflected > 0)
                    {
                        state.Items.ReflectCount += reflected;
                        changed = true;
                    }
                }
                if (state.Items.MagnetTicks > 0 && ledger != null)
                {
                    PullCoins(ledger.Field, new Vector2(state.Position.X, state.Position.Y), tuning);
                }
                if (configs[i].Combat.ItemFreezesWalls && state.WallSide != 0)
                {
                    FreezeWall(objects, i, state, configs[i], tuning, tick);
                }
                if (changed)
                {
                    states[i] = state;
                }
            }
        }

        /// <summary>反射板で跳ね返せる物（飛んでくる投射物）か【仮】。</summary>
        public static bool IsReflectable(in WorldObject item)
        {
            switch (item.Kind)
            {
                case WorldObjectKind.Projectile:
                case WorldObjectKind.Missile:
                case WorldObjectKind.CreamShot:
                case WorldObjectKind.Macaron:
                case WorldObjectKind.Shell:
                case WorldObjectKind.ItemShot:
                case WorldObjectKind.SmokeBall:
                case WorldObjectKind.BallLightning:
                case WorldObjectKind.ChainHook:
                    return true;
                case WorldObjectKind.ItemBomb:
                    return item.Stored == 0;
                case WorldObjectKind.Mine:
                    return item.Variant == 0;
                default:
                    return false;
            }
        }

        // キャラクター index の正面に飛んでくる相手の投射物を、向きを逆にして跳ね返す（跳ね返した物は index の物になる）。跳ね返した数。
        private static int Reflect(WorldObjects objects, List<CharacterState> states, int index, ItemTuning tuning)
        {
            CharacterState me = states[index];
            int facing = me.Facing < 0 ? -1 : 1;
            var center = new Vector2(me.Position.X + facing * tuning.ReflectorReach, me.Position.Y);
            int count = 0;
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject item = objects[o];
                if (item.Ended || !IsReflectable(item) || item.Owner == index || (item.Owner >= 0 && item.Owner < states.Count
                        && TeamRules.Allies(states, item.Owner, index)))
                {
                    continue;
                }
                if (MathF.Abs(item.Position.X - center.X) > tuning.ReflectorHalfWidth || MathF.Abs(item.Position.Y - center.Y) > tuning.ReflectorHalfHeight)
                {
                    continue;
                }
                var toMe = new Vector2(me.Position.X - item.Position.X, me.Position.Y - item.Position.Y);
                if (Vector2.Dot(item.Velocity, toMe) <= 0f)
                {
                    continue;
                }
                item.Velocity = -item.Velocity;
                item.Owner = index;
                item.HitMask = 0;
                if (item.Kind == WorldObjectKind.ItemShot || item.Kind == WorldObjectKind.ChainHook)
                {
                    // 届く距離を数え直す。
                    item.ChargeMilli = 0;
                }
                objects[o] = item;
                count++;
            }
            return count;
        }

        // 大きな磁石: 半径の中のフィールドのコインを、center へ吸い寄せる。
        private static void PullCoins(CoinField field, Vector2 center, ItemTuning tuning)
        {
            float step = tuning.BigMagnetPullSpeed * SimTime.DeltaTime;
            for (int c = 0; c < field.Coins.Count; c++)
            {
                FieldCoin coin = field.Coins[c];
                Vector2 toCenter = center - coin.Position;
                float distance = toCenter.Length();
                if (distance > tuning.BigMagnetRadius || distance < 1e-4f)
                {
                    continue;
                }
                field.MoveCoin(c, distance <= step ? center : coin.Position + toCenter / distance * step);
            }
        }

        // スパイクの靴底: 張り付いている壁のうち、まだ凍っていない所を凍らせる（自分のまわりの決まった高さ）。
        private static void FreezeWall(WorldObjects objects, int index, in CharacterState state, CharacterConfig config, ItemTuning tuning, int tick)
        {
            int side = state.WallSide;
            float face = state.Position.X + side * config.Movement.HalfWidth;
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject wall = objects[o];
                if (wall.Kind == WorldObjectKind.FrozenWall && !wall.Ended && wall.Owner == index && MathF.Abs(wall.Position.X - face) < 0.3f
                    && MathF.Abs(wall.Position.Y - state.Position.Y) < wall.End.Y - 0.4f)
                {
                    return;
                }
            }
            objects.Add(new WorldObject
            {
                Kind = WorldObjectKind.FrozenWall, Owner = index, Position = new Vector2(face, state.Position.Y),
                End = new Vector2(tuning.SpikeWallHalfWidth, tuning.SpikeWallHalfHeight), SpawnTick = tick, ExpireTick = tick + tuning.SpikeWallTicks,
                Variant = (byte)ItemId.SpikeSoles, Stored = side,
            });
        }

        // ---------------------------------------------------------------- アクティブ（置く物・飛ばす物）

        // 追加のアイテムの物を 1 ティック進める（扱った物なら true）。
        private static bool StepMore(ref WorldObject item, WorldObjects objects, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            ICollisionQuery collision, StageBounds? bounds, ItemTuning tuning, int tick, List<HitRecord> hits)
        {
            switch (item.Kind)
            {
                case WorldObjectKind.ItemBomb:
                    StepBomb(ref item, objects, states, configs, collision, bounds, tuning, tick, hits);
                    return true;
                case WorldObjectKind.ChainHook:
                    StepHook(ref item, states, configs, collision, bounds, tuning, hits);
                    return true;
                case WorldObjectKind.ThunderCloud:
                    StepCloud(ref item, states, configs, tuning, tick, hits);
                    return true;
                case WorldObjectKind.Trampoline:
                    StepTrampoline(ref item, states, configs, tuning);
                    return true;
                case WorldObjectKind.FrozenWall:
                    StepFrozenWall(ref item, states, configs, tuning);
                    return true;
                case WorldObjectKind.Decoy:
                    StepDecoy(ref item, states, configs, collision, bounds, tuning, tick, hits);
                    return true;
                default:
                    return false;
            }
        }

        // 身代わり人形: 置いた所にとどまり、一度でも攻撃を受けたら吹き飛んで重力で落ちる。決まった回数の攻撃を受けるか時間が来たら、
        // 記録した威力の合計に応じた爆発を起こして消える（ユーザーの指定、2026-11-06）。
        private static void StepDecoy(ref WorldObject decoy, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            ICollisionQuery collision, StageBounds? bounds, ItemTuning tuning, int tick, List<HitRecord> hits)
        {
            if (decoy.Damage >= tuning.DecoyHits || tick - decoy.SpawnTick >= tuning.DecoyTicks)
            {
                ExplodeDecoy(decoy, states, configs, tuning, hits);
                decoy.Ended = true;
                return;
            }
            if (decoy.Damage == 0)
            {
                return;
            }
            float dt = SimTime.DeltaTime;
            Vector2 velocity = decoy.Velocity;
            velocity.Y = Math.Max(velocity.Y - tuning.DecoyGravity * dt, -tuning.DecoyMaxFallSpeed);
            var half = new Vector3(decoy.End.X, decoy.End.Y, 0.5f);
            if (collision != null)
            {
                // 横と縦を別々に動かす（壁で横を止め、床・天井で縦を止める）。
                float dx = velocity.X * dt;
                if (dx != 0f && collision.SweepBox(new Vector3(decoy.Position.X, decoy.Position.Y, 0f), half, new Vector3(dx, 0f, 0f), out CastHit wall))
                {
                    dx = Math.Sign(dx) * Math.Max(0f, wall.Distance - 0.01f);
                    velocity.X = 0f;
                }
                decoy.Position = new Vector2(decoy.Position.X + dx, decoy.Position.Y);
                float dy = velocity.Y * dt;
                if (dy != 0f && collision.SweepBox(new Vector3(decoy.Position.X, decoy.Position.Y, 0f), half, new Vector3(0f, dy, 0f), out CastHit floor))
                {
                    dy = Math.Sign(dy) * Math.Max(0f, floor.Distance - 0.01f);
                    bool landed = velocity.Y < 0f;
                    velocity.Y = 0f;
                    if (landed)
                    {
                        // 床の上では止まっていく。
                        float slow = tuning.DecoyGroundFriction * dt;
                        velocity.X = Math.Abs(velocity.X) <= slow ? 0f : velocity.X - Math.Sign(velocity.X) * slow;
                    }
                }
                decoy.Position = new Vector2(decoy.Position.X, decoy.Position.Y + dy);
            }
            else
            {
                decoy.Position += velocity * dt;
            }
            decoy.Velocity = velocity;
            if (bounds.HasValue && !bounds.Value.Contains(new Vector3(decoy.Position.X, decoy.Position.Y, 0f)))
            {
                // ステージの外へ飛んだ: 爆発せずに消える。
                decoy.Ended = true;
            }
        }

        /// <summary>身代わり人形が記録した威力の合計 recorded での、爆発の半径（m）。</summary>
        public static float DecoyBlastRadius(long recorded, ItemTuning tuning)
        {
            return Math.Min(tuning.DecoyBlastRadiusMax, tuning.DecoyBlastRadiusBase + tuning.DecoyBlastRadiusPerDamage * Math.Max(0L, recorded));
        }

        /// <summary>身代わり人形が記録した威力の合計 recorded での、爆発の威力（0 なら当たらない）。</summary>
        public static int DecoyBlastDamage(long recorded, ItemTuning tuning)
        {
            long damage = Math.Max(0L, recorded) * tuning.DecoyBlastDamagePercent / 100;
            return (int)Math.Min(tuning.DecoyBlastDamageMax, damage);
        }

        private static void ExplodeDecoy(in WorldObject decoy, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, ItemTuning tuning,
            List<HitRecord> hits)
        {
            int damage = DecoyBlastDamage(decoy.ChargeMilli, tuning);
            if (damage <= 0 || hits == null)
            {
                return;
            }
            float radius = DecoyBlastRadius(decoy.ChargeMilli, tuning);
            float scale = Math.Min(tuning.DecoyBlastKnockbackScaleMax, 0.6f + decoy.ChargeMilli / Math.Max(1f, tuning.DecoyBlastKnockbackDivisor));
            AttackHit source = tuning.DecoyBlastHit;
            var blast = new AttackHit
            {
                Damage = damage, Gauge = source.Gauge, HitstopTicks = source.HitstopTicks, HitstunTicks = source.HitstunTicks,
                KnockbackX = source.KnockbackX * scale, KnockbackY = source.KnockbackY * scale,
            };
            for (int v = 0; v < states.Count; v++)
            {
                if (!IsTarget(states, decoy.Owner, v))
                {
                    continue;
                }
                Vector3 body = configs[v].Movement.HalfExtents;
                float nx = Math.Max(Math.Abs(states[v].Position.X - decoy.Position.X) - body.X, 0f);
                float ny = Math.Max(Math.Abs(states[v].Position.Y - decoy.Position.Y) - body.Y, 0f);
                if (nx * nx + ny * ny > radius * radius)
                {
                    continue;
                }
                int facing = states[v].Position.X >= decoy.Position.X ? 1 : -1;
                hits.Add(new HitRecord(decoy.Owner, v, AttackSlot.Ground, 0, blast, facing, AttackPower.Normal, fromObject: true));
            }
        }

        // 相手（自分・味方・触れない人は除く）か。
        private static bool IsTarget(List<CharacterState> states, int owner, int v)
        {
            return v != owner && !states[v].Combat.IsUntouchable && !states[v].Combat.IsAway && (owner < 0 || owner >= states.Count
                || !TeamRules.Allies(states, owner, v));
        }

        private static bool Overlaps(Vector2 center, Vector2 half, in CharacterState state, CharacterConfig config)
        {
            Vector3 body = config.Movement.HalfExtents;
            return MathF.Abs(state.Position.X - center.X) < half.X + body.X && MathF.Abs(state.Position.Y - center.Y) < half.Y + body.Y;
        }

        // グラップリングボム: 山なりに飛び、地形か相手に刺さり、時間が来たら爆発する。
        private static void StepBomb(ref WorldObject bomb, WorldObjects objects, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            ICollisionQuery collision, StageBounds? bounds, ItemTuning tuning, int tick, List<HitRecord> hits)
        {
            float dt = SimTime.DeltaTime;
            if (bomb.Stored == 0)
            {
                bomb.Velocity = new Vector2(bomb.Velocity.X, bomb.Velocity.Y - tuning.BombGravity * dt);
                Vector2 delta = bomb.Velocity * dt;
                var half = new Vector3(tuning.BombRadius, tuning.BombRadius, 0.5f);
                if (collision != null && delta.LengthSquared() > 0f
                    && collision.SweepBox(new Vector3(bomb.Position.X, bomb.Position.Y, 0f), half, new Vector3(delta.X, delta.Y, 0f), out CastHit hit))
                {
                    // 地形に刺さった。
                    bomb.Position += Vector2.Normalize(delta) * Math.Max(0f, hit.Distance - 0.01f);
                    bomb.Velocity = Vector2.Zero;
                    bomb.Stored = 1;
                    bomb.ChargeMilli = tick + tuning.BombFuseTicks;
                }
                else
                {
                    bomb.Position += delta;
                    if (TouchesDecoy(objects, bomb.Position, tuning.BombRadius))
                    {
                        // 身代わり人形（だれの人形でも）に触れた: その場に刺さって、時間が来たら爆発する（ユーザーの指定、2026-11-06）。
                        bomb.Velocity = Vector2.Zero;
                        bomb.Stored = 1;
                        bomb.ChargeMilli = tick + tuning.BombFuseTicks;
                    }
                    for (int v = 0; v < states.Count && bomb.Stored == 0; v++)
                    {
                        if (IsTarget(states, bomb.Owner, v)
                            && Overlaps(bomb.Position, new Vector2(tuning.BombRadius, tuning.BombRadius), states[v], configs[v]))
                        {
                            // 相手に刺さった（その人と一緒に動く）。
                            bomb.Stored = 2;
                            bomb.LastPushId = v;
                            bomb.End = bomb.Position - new Vector2(states[v].Position.X, states[v].Position.Y);
                            bomb.Velocity = Vector2.Zero;
                            bomb.ChargeMilli = tick + tuning.BombFuseTicks;
                            break;
                        }
                    }
                }
            }
            else if (bomb.Stored == 2)
            {
                int victim = bomb.LastPushId;
                if (victim < 0 || victim >= states.Count || states[victim].Combat.IsWaitingToRespawn)
                {
                    // 刺さった人が撃破された: 爆発せずに消える。
                    bomb.Ended = true;
                    return;
                }
                bomb.Position = new Vector2(states[victim].Position.X, states[victim].Position.Y) + bomb.End;
            }
            if (bounds.HasValue && !bounds.Value.Contains(new Vector3(bomb.Position.X, bomb.Position.Y, 0f)))
            {
                bomb.Ended = true;
                return;
            }
            if (tick >= bomb.ChargeMilli)
            {
                Explode(bomb, objects, states, configs, tuning, hits);
                bomb.Ended = true;
            }
        }

        // 爆発: 半径の中の相手に当てる（中心から外向きに吹き飛ばす）。
        private static void Explode(in WorldObject bomb, WorldObjects objects, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            ItemTuning tuning, List<HitRecord> hits)
        {
            // 範囲の身代わり人形（だれの人形でも）にも当たる（ユーザーの指定、2026-11-06）。
            HitDecoys(objects, bomb, tuning.BombBlastRadius, tuning.BombHit, 0, AttackPower.Normal);
            for (int v = 0; v < states.Count; v++)
            {
                if (!IsTarget(states, bomb.Owner, v))
                {
                    continue;
                }
                Vector3 body = configs[v].Movement.HalfExtents;
                float nx = Math.Max(Math.Abs(states[v].Position.X - bomb.Position.X) - body.X, 0f);
                float ny = Math.Max(Math.Abs(states[v].Position.Y - bomb.Position.Y) - body.Y, 0f);
                if (nx * nx + ny * ny > tuning.BombBlastRadius * tuning.BombBlastRadius)
                {
                    continue;
                }
                int facing = states[v].Position.X >= bomb.Position.X ? 1 : -1;
                hits.Add(new HitRecord(bomb.Owner, v, AttackSlot.Ground, 0, tuning.BombHit, facing, AttackPower.Normal, fromObject: true));
            }
        }

        // チェーンフック: まっすぐ飛び、最初に当たった相手を持ち主の前へ引き寄せる。地形・届く距離・ステージの外で消える。
        private static void StepHook(ref WorldObject hook, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            ICollisionQuery collision, StageBounds? bounds, ItemTuning tuning, List<HitRecord> hits)
        {
            Vector2 delta = hook.Velocity * SimTime.DeltaTime;
            var half = new Vector3(tuning.HookRadius * 0.5f, tuning.HookRadius * 0.5f, 0.5f);
            if (collision != null && collision.SweepBox(new Vector3(hook.Position.X, hook.Position.Y, 0f), half, new Vector3(delta.X, delta.Y, 0f),
                    out CastHit hit))
            {
                hook.Position += Vector2.Normalize(delta) * hit.Distance;
                hook.Ended = true;
                return;
            }
            hook.Position += delta;
            hook.ChargeMilli += (long)MathF.Round(delta.Length() * 1000f);
            if (hook.ChargeMilli >= (long)(tuning.HookRange * 1000f)
                || (bounds.HasValue && !bounds.Value.Contains(new Vector3(hook.Position.X, hook.Position.Y, 0f))))
            {
                hook.Ended = true;
                return;
            }
            if (hook.Owner < 0 || hook.Owner >= states.Count)
            {
                return;
            }
            CharacterState owner = states[hook.Owner];
            for (int v = 0; v < states.Count; v++)
            {
                if (!IsTarget(states, hook.Owner, v) || !Overlaps(hook.Position, new Vector2(tuning.HookRadius, tuning.HookRadius), states[v], configs[v]))
                {
                    continue;
                }
                // 持ち主の、相手のいる側のすぐ前へ、HookPullSeconds で着くように飛ばす（重力の分だけ上向きに足す）。
                CharacterState victim = states[v];
                float side = victim.Position.X >= owner.Position.X ? 1f : -1f;
                float gap = configs[hook.Owner].Movement.HalfWidth + configs[v].Movement.HalfWidth + 0.2f;
                var target = new Vector2(owner.Position.X + side * gap, owner.Position.Y);
                Vector2 velocity = (target - new Vector2(victim.Position.X, victim.Position.Y)) / tuning.HookPullSeconds;
                velocity.Y += configs[v].Movement.Gravity * tuning.HookPullSeconds * 0.5f;
                AttackHit source = tuning.HookHit;
                var pull = new AttackHit
                {
                    Damage = source.Damage, Gauge = source.Gauge, HitstopTicks = source.HitstopTicks, HitstunTicks = source.HitstunTicks,
                    KnockbackX = MathF.Abs(velocity.X), KnockbackY = velocity.Y,
                };
                hits.Add(new HitRecord(hook.Owner, v, AttackSlot.Ground, 0, pull, velocity.X >= 0f ? 1 : -1, AttackPower.Normal, fromObject: true));
                hook.Stored = 1;
                hook.Ended = true;
                return;
            }
        }

        // 瓶詰めの雷雲: 1 秒ごとに、雲の下の相手に雷を落とす（感電）。
        private static void StepCloud(ref WorldObject cloud, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, ItemTuning tuning,
            int tick, List<HitRecord> hits)
        {
            int interval = Math.Max(1, tuning.CloudTicks / Math.Max(1, tuning.CloudStrikes));
            int elapsed = tick - cloud.SpawnTick;
            if (elapsed <= 0 || elapsed % interval != 0 || cloud.Damage >= tuning.CloudStrikes)
            {
                return;
            }
            cloud.Damage++;
            var center = new Vector2(cloud.Position.X, cloud.Position.Y - cloud.End.Y * 0.5f);
            var half = new Vector2(cloud.End.X, cloud.End.Y * 0.5f);
            for (int v = 0; v < states.Count; v++)
            {
                if (IsTarget(states, cloud.Owner, v) && Overlaps(center, half, states[v], configs[v]))
                {
                    int facing = states[v].Position.X >= cloud.Position.X ? 1 : -1;
                    hits.Add(new HitRecord(cloud.Owner, v, AttackSlot.Ground, 0, tuning.CloudHit, facing, AttackPower.Normal, fromObject: true));
                }
            }
        }

        // 携帯トランポリン: 乗った人（だれでも。落ちているか止まっている人）を跳ね上げる。
        private static void StepTrampoline(ref WorldObject pad, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, ItemTuning tuning)
        {
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState state = states[v];
                if (state.Combat.IsWaitingToRespawn || state.Combat.HasLeft || state.Combat.IsAway || state.Velocity.Y > 0f
                    || !Overlaps(pad.Position, pad.End, state, configs[v]))
                {
                    continue;
                }
                state.Velocity = new Vector3(state.Velocity.X, tuning.TrampolineLaunchSpeed, 0f);
                state.Grounded = false;
                state.JumpPadCount++;
                states[v] = state;
                pad.Damage++;
            }
        }

        // 凍らせた壁: 触れた相手（持ち主と味方は除く）を 1 回だけ凍らせる。
        private static void StepFrozenWall(ref WorldObject wall, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, ItemTuning tuning)
        {
            for (int v = 0; v < states.Count && v < 32; v++)
            {
                if ((wall.HitMask & (1u << v)) != 0 || !IsTarget(states, wall.Owner, v) || states[v].Combat.IsFrozen
                    || !Overlaps(wall.Position, wall.End + new Vector2(0.05f, 0f), states[v], configs[v]))
                {
                    continue;
                }
                CharacterState state = states[v];
                WireController.Interrupt(ref state, configs[v].Wire);
                if (state.Action.Kind != ActionKind.Hitstun)
                {
                    state.Action = default;
                }
                state.Combat.Holding = false;
                state.Combat.HoldTicks = 0;
                state.Combat.FrozenTicksLeft = tuning.SpikeFreezeTicks;
                state.Combat.FreezeCount++;
                state.WallSide = 0;
                state.Velocity.X = 0f;
                states[v] = state;
                wall.HitMask |= 1u << v;
            }
        }

        // 足場に止まっている落ちたアイテムどうしが重なったら、少しずつ押しのけて広げる（番号の順。全員で同じ結果）。
        private static void SpreadDropped(WorldObjects objects, ICollisionQuery collision, ItemTuning tuning)
        {
            float size = tuning.DropHalfSize * 2f;
            float step = tuning.DropSpreadSpeed * SimTime.DeltaTime;
            var half = new Vector3(tuning.DropHalfSize, tuning.DropHalfSize, 0.5f);
            for (int a = 0; a < objects.Count; a++)
            {
                WorldObject first = objects[a];
                if (first.Kind != WorldObjectKind.DroppedItem || first.Ended || first.Stored != Resting)
                {
                    continue;
                }
                for (int b = a + 1; b < objects.Count; b++)
                {
                    WorldObject second = objects[b];
                    if (second.Kind != WorldObjectKind.DroppedItem || second.Ended || second.Stored != Resting)
                    {
                        continue;
                    }
                    float dx = second.Position.X - first.Position.X;
                    if (MathF.Abs(dx) >= size || MathF.Abs(second.Position.Y - first.Position.Y) >= tuning.DropHalfSize)
                    {
                        continue;
                    }
                    float side = dx >= 0f ? 1f : -1f;
                    Vector2 p = first.Position;
                    MoveAxis(collision, ref p, half, -side * step, true);
                    first.Position = p;
                    p = second.Position;
                    MoveAxis(collision, ref p, half, side * step, true);
                    second.Position = p;
                    objects[b] = second;
                }
                objects[a] = first;
            }
        }

        // ---------------------------------------------------------------- 身代わり人形（CPU と攻撃）

        /// <summary>キャラクター owner の身代わり人形の位置（なければ false）。CPU は人形のほうを狙う。</summary>
        public static bool TryGetDecoy(WorldObjects objects, int owner, out Vector2 position)
        {
            if (objects != null)
            {
                for (int o = objects.Count - 1; o >= 0; o--)
                {
                    WorldObject item = objects[o];
                    if (item.Kind == WorldObjectKind.Decoy && !item.Ended && item.Owner == owner)
                    {
                        position = item.Position;
                        return true;
                    }
                }
            }
            position = default;
            return false;
        }

        /// <summary>
        /// 身代わり人形に当てる、投射物・爆発の攻撃の鍵（同じ投射物は 1 回だけ当たる）。近接攻撃の鍵（HitResolver.ObjectHitKey。正の数）と
        /// 重ならないように、物の番号の負の数にする。
        /// </summary>
        public static int ProjectileDecoyKey(in WorldObject projectile)
        {
            return -projectile.Id;
        }

        /// <summary>点 point（半径 radius）が身代わり人形に触れているか（投射物が人形に当たって止まる・爆発するか）。</summary>
        public static bool TouchesDecoy(WorldObjects objects, Vector2 point, float radius)
        {
            for (int i = 0; objects != null && i < objects.Count; i++)
            {
                WorldObject decoy = objects[i];
                if (decoy.Kind == WorldObjectKind.Decoy && !decoy.Ended
                    && MathF.Abs(decoy.Position.X - point.X) < radius + decoy.End.X && MathF.Abs(decoy.Position.Y - point.Y) < radius + decoy.End.Y)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 攻撃 hit（強さ power、向き facing。0 なら範囲の中心から離れる向き）の範囲（中心 center、半分の大きさ half）に重なった身代わり人形に、
        /// 攻撃を 1 回当てる（同じ攻撃の同じ判定は 1 回。key）。人形は持ち主・味方・相手を問わず、近接攻撃・投射物・爆発のどれも受ける
        /// （ユーザーの指定、2026-11-06）。威力を記録し（ChargeMilli）、吹き飛びの割合（BonusPercent）で飛ぶ（それからは重力で落ちる）。
        /// 決まった回数で爆発する（StepDecoy）。当てたら true。
        /// </summary>
        public static bool HitDecoys(WorldObjects objects, int key, Vector2 center, Vector2 half, AttackHit hit, int facing, AttackPower power)
        {
            if (objects == null || hit == null)
            {
                return false;
            }
            bool struck = false;
            for (int i = 0; i < objects.Count; i++)
            {
                WorldObject decoy = objects[i];
                if (decoy.Kind != WorldObjectKind.Decoy || decoy.Ended || decoy.HasHitKey(key))
                {
                    continue;
                }
                if (MathF.Abs(decoy.Position.X - center.X) >= half.X + decoy.End.X || MathF.Abs(decoy.Position.Y - center.Y) >= half.Y + decoy.End.Y)
                {
                    continue;
                }
                int side = facing != 0 ? facing : decoy.Position.X >= center.X ? 1 : -1;
                decoy.AddHitKey(key);
                decoy.Damage++;
                decoy.ChargeMilli += Math.Max(0, AttackPower.Damage(hit.Damage, power.Multiplier, power.BonusPercent));
                decoy.Velocity = new Vector2(hit.KnockbackX * side, hit.KnockbackY) * power.KnockbackScale * (decoy.BonusPercent / 100f);
                objects[i] = decoy;
                struck = true;
            }
            return struck;
        }

        /// <summary>投射物 projectile（半径 radius）の攻撃 hit を、触れた身代わり人形に当てる（HitDecoys）。当てたら true（投射物は止まる）。</summary>
        public static bool HitDecoys(WorldObjects objects, in WorldObject projectile, float radius, AttackHit hit, int facing, AttackPower power)
        {
            return HitDecoys(objects, ProjectileDecoyKey(projectile), projectile.Position, new Vector2(radius, radius), hit, facing, power);
        }
    }
}
