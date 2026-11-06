using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Items;
using Airerage.Core.Match;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Items
{
    /// <summary>
    /// 追加のアイテム（ユーザーが選んだ 25 個。2026-11-05。docs/20_item_plan.md）と、落ちているアイテムの寿命・広がり・拾う範囲、
    /// ランダムセレクト、THIEF のスナッチとマガジンマガジンのクールタイム。
    /// </summary>
    public class MoreItemTests
    {
        private static readonly float GroundY = new CharacterConfig().Movement.HalfHeight + 0.01f;

        private static SimWorld World(MatchRules rules, params Vector3[] at)
        {
            var world = new SimWorld(TestArenas.Ground(), new StageBounds(-60f, -30f, 60f, 60f), rules: rules);
            foreach (Vector3 position in at)
            {
                world.AddCharacter(new CharacterConfig(), position);
            }
            return world;
        }

        private static MatchRules PreItem()
        {
            MatchRules rules = MatchRules.FreePlay();
            rules.ItemMode = ItemMode.PreItem;
            return rules;
        }

        private static PlayerInput Keys(InputButtons buttons)
        {
            return PlayerInput.FromAxes(0f, 0f, buttons);
        }

        private static List<HitRecord> Step(SimWorld world, int ticks, params PlayerInput[] inputs)
        {
            var hits = new List<HitRecord>();
            var all = new PlayerInput[world.CharacterCount];
            for (int i = 0; i < all.Length && i < inputs.Length; i++)
            {
                all[i] = inputs[i];
            }
            for (int t = 0; t < ticks; t++)
            {
                world.Step(all);
                hits.AddRange(world.LastHits);
            }
            return hits;
        }

        private static List<HitRecord> Use(SimWorld world, int slot, Vector2 aim)
        {
            InputButtons key = ItemRules.SlotButton(slot);
            List<HitRecord> hits = Step(world, 1, Keys(key).WithAim(aim));
            hits.AddRange(Step(world, 1, Keys(InputButtons.None).WithAim(aim)));
            return hits;
        }

        private static ItemBag Bag(params ItemId[] items)
        {
            return ItemRules.StartingBag(items, items.Length);
        }

        private static WorldObject[] ObjectsOf(SimWorld world, WorldObjectKind kind)
        {
            return world.Objects.Items.Where(o => o.Kind == kind).ToArray();
        }

        // ---------------------------------------------------------------- 一覧

        [Test]
        public void Catalog_ListsPassivesBeforeActives()
        {
            Assert.That(ItemCatalog.Count, Is.EqualTo(40));
            Assert.That(ItemCatalog.PassiveCount, Is.EqualTo(22));
            for (int i = 0; i < ItemCatalog.Count; i++)
            {
                ItemId id = ItemCatalog.At(i);
                Assert.That(ItemCatalog.IsActive(id), Is.EqualTo(i >= ItemCatalog.PassiveCount), $"{id} の並びがパッシブ → アクティブでない");
                Assert.That(ItemCatalog.IsActive(id) ? new ItemTuning().CooldownOf(id) : 0, ItemCatalog.IsActive(id) ? Is.GreaterThan(0) : Is.EqualTo(0));
            }
            Assert.That(new ItemTuning().Validate().Errors, Is.Empty);
        }

        // ---------------------------------------------------------------- パッシブ

        [Test]
        public void NewPassives_ChangeTheValues()
        {
            var tuning = new ItemTuning();
            var config = new CharacterConfig();
            CharacterConfig sky = ItemRules.ConfigFor(Bag(ItemId.SkyBoots), config, tuning);
            Assert.That(sky.Movement.AirJumps, Is.EqualTo(config.Movement.AirJumps + 1), "スカイブーツで空中ジャンプが増えない");

            CharacterConfig iron = ItemRules.ConfigFor(Bag(ItemId.IronBoots), config, tuning);
            Assert.That(iron.Movement.Gravity, Is.EqualTo(config.Movement.Gravity * 1.3f).Within(1e-4f), "鉄の長靴で重力が +30% にならない");
            Assert.That(iron.Movement.RunSpeed, Is.EqualTo(config.Movement.RunSpeed), "鉄の長靴で移動が遅くなった（ユーザーの指示でなし）");
            Assert.That(iron.Combat.ItemKnockbackTakenPercent, Is.EqualTo(-20));

            CharacterConfig hat = ItemRules.ConfigFor(Bag(ItemId.FeatherHat), config, tuning);
            Assert.That(hat.Movement.Gravity, Is.EqualTo(config.Movement.Gravity * 0.7f).Within(1e-4f));
            Assert.That(hat.Combat.ItemKnockbackTakenPercent, Is.EqualTo(20));

            Assert.That(ItemRules.ConfigFor(Bag(ItemId.SteelWire), config, tuning).Combat.WireCancelCooldownTicks,
                Is.EqualTo(config.Combat.WireCancelCooldownTicks / 2), "鋼のワイヤーで −50% にならない");
            Assert.That(ItemRules.ConfigFor(Bag(ItemId.LongHook), config, tuning).Wire.MaxLength,
                Is.EqualTo(config.Wire.MaxLength * 1.25f).Within(1e-4f));
            Assert.That(ItemRules.ConfigFor(Bag(ItemId.GoldBell), config, tuning).Combat.JustGuardTicks,
                Is.EqualTo((int)System.MathF.Round(config.Combat.JustGuardTicks * 1.5f)));
            Assert.That(ItemRules.ConfigFor(Bag(ItemId.SpareBattery), config, tuning).Combat.ItemGaugePercent, Is.EqualTo(25));
            Assert.That(ItemRules.ConfigFor(Bag(ItemId.VaultKey), config, tuning).Combat.ItemBankBonusPercent, Is.EqualTo(10));
            Assert.That(ItemRules.ConfigFor(Bag(ItemId.GoldMagnet), config, tuning).Combat.ItemCoinPickupScale, Is.EqualTo(2f));

            CharacterConfig phoenix = ItemRules.ConfigFor(Bag(ItemId.PhoenixFeather), config, tuning);
            Assert.That(phoenix.Combat.ItemRespawnReductionTicks, Is.EqualTo(60));
            Assert.That(ItemRules.KoDropAmount(40, 80, phoenix.Combat), Is.EqualTo(20), "不死鳥の羽根で撃破ドロップが半分にならない");

            CharacterConfig charm = ItemRules.ConfigFor(Bag(ItemId.AdversityCharm), config, tuning);
            Assert.That(ItemRules.KoDropAmount(40, 80, charm.Combat), Is.EqualTo(80), "逆境のお守りで撃破ドロップが全額にならない");
            Assert.That(charm.Combat.ItemDamageTakenPercent, Is.EqualTo(50));
            Assert.That(charm.Combat.ItemKnockbackTakenPercent, Is.EqualTo(50));

            CharacterConfig spikes = ItemRules.ConfigFor(Bag(ItemId.SpikeSoles), config, tuning);
            Assert.That(spikes.Movement.WallClingMaxTicks, Is.EqualTo(config.Movement.WallClingMaxTicks + 120));
            Assert.That(spikes.Combat.ItemFreezesWalls, Is.True);

            Assert.That(ItemRules.PassiveKey(Bag(ItemId.SkyBoots, ItemId.FeatherHat)), Is.EqualTo(ItemRules.PassiveKey(Bag(ItemId.FeatherHat, ItemId.SkyBoots))),
                "持つ順で調整値の鍵が変わる");
        }

        [Test]
        public void TakenModifiers_AndCharm_AdjustTheHit()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f), new Vector3(1f, GroundY, 0f));
            world.SetItems(1, new[] { ItemId.AdversityCharm });
            Step(world, 1);
            var configs = new[] { world.GetConfig(0), world.GetConfig(1) };
            var states = new List<CharacterState> { world.GetState(0), world.GetState(1) };
            var hit = new HitRecord(0, 1, AttackSlot.Ground, 0, new AttackHit { Damage = 10, KnockbackX = 10f }, 1, AttackPower.Normal);
            HitRecord adjusted = ItemRules.AdjustHit(states, configs, hit);
            Assert.That(adjusted.Damage, Is.EqualTo(15), "逆境のお守りで受ける威力が 1.5 倍にならない");
            Assert.That(adjusted.Knockback.X, Is.EqualTo(15f).Within(1e-3f), "受ける吹き飛びが 1.5 倍にならない");
        }

        // ---------------------------------------------------------------- アクティブ

        [Test]
        public void BubbleBarrier_BlocksOneHit_AndReflectorSendsAShotBack()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f), new Vector3(6f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.EnergyShot, ItemId.EnergyShot });
            world.SetItems(1, new[] { ItemId.BubbleBarrier });
            Use(world, 0, new Vector2(0f, 0f));
            Step(world, 1, default, Keys(InputButtons.Item1));
            Step(world, 1, default, Keys(InputButtons.None));
            Assert.That(world.GetState(1).Items.BarrierTicks, Is.GreaterThan(0), "前提: バリアを張った");
            List<HitRecord> hits = Use(world, 0, new Vector2(1f, 0f));
            hits.AddRange(Step(world, 30));
            Assert.That(hits.Any(h => h.Victim == 1), Is.False, "バリアがあるのに当たった");
            Assert.That(world.GetState(1).Items.BarrierPopCount, Is.EqualTo(1), "バリアが割れない");

            SimWorld reflect = World(PreItem(), new Vector3(0f, GroundY, 0f), new Vector3(6f, GroundY, 0f));
            reflect.SetItems(0, new[] { ItemId.EnergyShot });
            reflect.SetItems(1, new[] { ItemId.Reflector });
            CharacterState defender = reflect.GetState(1);
            defender.Facing = -1;
            reflect.SetState(1, defender);
            Step(reflect, 1, default, Keys(InputButtons.Item1));
            Step(reflect, 1, default, Keys(InputButtons.None));
            List<HitRecord> back = Use(reflect, 0, new Vector2(1f, 0f));
            back.AddRange(Step(reflect, 40));
            Assert.That(reflect.GetState(1).Items.ReflectCount, Is.EqualTo(1), "反射板で跳ね返さない");
            Assert.That(back.Any(h => h.Victim == 1), Is.False, "跳ね返したのに当たった");
            Assert.That(back.Any(h => h.Victim == 0 && h.Attacker == 1), Is.True, "跳ね返した光弾が撃った人に当たらない");
        }

        [Test]
        public void Hourglass_SlowsNearbyOpponents()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f), new Vector3(3f, GroundY, 0f), new Vector3(20f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.Hourglass });
            Use(world, 0, new Vector2(1f, 0f));
            Step(world, new ItemTuning().HourglassCastTicks + 2);
            Assert.That(world.GetState(1).Items.SlowTicks, Is.GreaterThan(0), "近くの相手が遅くならない");
            Assert.That(SpeedBoosts.PercentOf(world.GetState(1), world.GetConfig(1)), Is.EqualTo(-50));
            Assert.That(world.GetState(2).Items.SlowTicks, Is.EqualTo(0), "遠くの相手まで遅くなった");
        }

        [Test]
        public void ReturnRemote_MarksThenTeleports_AndStartsTheCooldown()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.ReturnRemote });
            Use(world, 0, new Vector2(1f, 0f));
            Assert.That(world.GetState(0).Items.MarkTicks, Is.GreaterThan(0), "印を置かない");
            CharacterState moved = world.GetState(0);
            moved.Position = new Vector3(10f, GroundY, 0f);
            world.SetState(0, moved);
            Use(world, 0, new Vector2(1f, 0f));
            Assert.That(world.GetState(0).Position.X, Is.EqualTo(0f).Within(0.5f), "印へ戻らない");
            Assert.That(world.GetState(0).Items.TeleportCount, Is.EqualTo(1));
            Assert.That(world.GetState(0).Items.GetCooldown(0), Is.GreaterThan(new ItemTuning().RemoteCooldownTicks - 5), "クールタイムが始まらない");
        }

        [Test]
        public void GrappleBomb_SticksToTheGround_AndExplodes()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f), new Vector3(3f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.GrappleBomb });
            List<HitRecord> hits = Use(world, 0, Vector2.Normalize(new Vector2(1f, -1f)));
            hits.AddRange(Step(world, 20));
            WorldObject[] bombs = ObjectsOf(world, WorldObjectKind.ItemBomb);
            Assert.That(bombs.Length, Is.EqualTo(1));
            Assert.That(bombs[0].Stored, Is.Not.EqualTo(0), "地形か相手に刺さらない");
            hits.AddRange(Step(world, new ItemTuning().BombFuseTicks + 5));
            Assert.That(hits.Any(h => h.Victim == 1 && h.Data.Damage == 15), Is.True, "爆発が近くの相手に当たらない");
            Assert.That(ObjectsOf(world, WorldObjectKind.ItemBomb), Is.Empty, "爆発しても消えない");
        }

        [Test]
        public void ChainHook_PullsTheOpponentTowardTheUser()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f), new Vector3(8f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.ChainHook });
            List<HitRecord> hits = Use(world, 0, new Vector2(1f, 0f));
            hits.AddRange(Step(world, 15));
            Assert.That(hits.Any(h => h.Victim == 1 && h.Data.Damage == 3), Is.True, "鉤が当たらない");
            Step(world, 20);
            Assert.That(world.GetState(1).Position.X, Is.LessThan(4f), "相手が引き寄せられない");
        }

        [Test]
        public void Trampoline_LaunchesAnyone_AndPlatformIsSolid()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.PocketTrampoline, ItemId.FoldingPlatform });
            Use(world, 0, new Vector2(1f, 0f));
            Step(world, 3);
            Assert.That(world.GetState(0).Velocity.Y, Is.GreaterThan(10f), "トランポリンで跳ね上がらない");

            SimWorld platform = World(PreItem(), new Vector3(0f, GroundY, 0f));
            platform.SetItems(0, new[] { ItemId.FoldingPlatform });
            Use(platform, 0, new Vector2(0f, 1f));
            WorldObject[] boards = ObjectsOf(platform, WorldObjectKind.ItemPlatform);
            Assert.That(boards.Length, Is.EqualTo(1), "足場を出さない");
            CharacterState above = platform.GetState(0);
            above.Position = new Vector3(boards[0].Position.X, boards[0].Position.Y + boards[0].End.Y + GroundY + 0.5f, 0f);
            above.Velocity = Vector3.Zero;
            platform.SetState(0, above);
            Step(platform, 40);
            Assert.That(platform.GetState(0).Grounded, Is.True, "足場の上に立てない");
            Assert.That(platform.GetState(0).Position.Y, Is.GreaterThan(boards[0].Position.Y), "足場をすり抜けた");
        }

        [Test]
        public void ThunderCloud_StrikesThreeTimes()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f), new Vector3(4f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.BottledThunder });
            List<HitRecord> hits = Use(world, 0, Vector2.Normalize(new Vector2(4f, 3f)));
            hits.AddRange(Step(world, new ItemTuning().CloudTicks + 5));
            Assert.That(hits.Count(h => h.Victim == 1 && h.Data.StunTicks > 0), Is.EqualTo(3), "雷が 3 回落ちない");
        }

        // 身代わり人形（ユーザーの指定、2026-11-06）: 照準の方向の目の前に置き、攻撃を受けるまではその場にとどまる。何も受けずに 2 秒たつと
        // 当たらない爆発で消える。
        [Test]
        public void Decoy_IsPlacedTowardTheAim_StaysPut_AndVanishesAfterTwoSeconds()
        {
            var tuning = new ItemTuning();
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY + 2f, 0f), new Vector3(6f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.DecoyDoll });
            Use(world, 0, new Vector2(-1f, 0f));
            WorldObject[] dolls = ObjectsOf(world, WorldObjectKind.Decoy);
            Assert.That(dolls.Length, Is.EqualTo(1), "人形を置かない");
            Assert.That(dolls[0].Position.X, Is.LessThan(-1f), "照準の方向（左）の目の前に置かない");
            Vector2 placed = dolls[0].Position;
            Step(world, 30);
            Assert.That(ObjectsOf(world, WorldObjectKind.Decoy)[0].Position, Is.EqualTo(placed), "攻撃を受ける前に動いた（重力で落ちた）");
            List<HitRecord> hits = Step(world, tuning.DecoyTicks);
            Assert.That(ObjectsOf(world, WorldObjectKind.Decoy).Any(d => !d.Ended), Is.False, "2 秒で消えない");
            Assert.That(hits.Any(h => h.Attacker == 0), Is.False, "記録が 0 なのに爆発が当たった");
        }

        // 攻撃を受けると威力を記録して吹き飛び、重力で落ちる。3 回で、記録した威力の合計に応じた範囲と威力の爆発を起こす。
        [Test]
        public void Decoy_RecordsDamage_FliesWhenHit_AndExplodesAfterThreeHits()
        {
            var tuning = new ItemTuning();
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY + 2f, 0f), new Vector3(5f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.DecoyDoll });
            Use(world, 0, new Vector2(1f, 0f));
            WorldObject doll = ObjectsOf(world, WorldObjectKind.Decoy)[0];
            var strike = new AttackHit { Damage = 10, KnockbackX = 4f, KnockbackY = 6f };
            Assert.That(ItemRules.HitDecoys(world.Objects, 101, doll.Position, Vector2.One, strike, -1, AttackPower.Normal), Is.True);
            Step(world, 6);
            WorldObject flying = ObjectsOf(world, WorldObjectKind.Decoy)[0];
            Assert.That(flying.ChargeMilli, Is.EqualTo(10), "威力を記録しない");
            Assert.That(flying.Position.X, Is.LessThan(doll.Position.X), "吹き飛ばない");
            Step(world, 40);
            Assert.That(ObjectsOf(world, WorldObjectKind.Decoy)[0].Position.Y, Is.LessThan(doll.Position.Y), "重力で落ちない");

            WorldObject now = ObjectsOf(world, WorldObjectKind.Decoy)[0];
            CharacterState enemy = world.GetState(1);
            enemy.Position = new Vector3(now.Position.X + 2f, now.Position.Y, 0f);
            world.SetState(1, enemy);
            var still = new AttackHit { Damage = 10 };
            ItemRules.HitDecoys(world.Objects, 102, now.Position, Vector2.One, still, 1, AttackPower.Normal);
            ItemRules.HitDecoys(world.Objects, 103, now.Position, Vector2.One, still, 1, AttackPower.Normal);
            List<HitRecord> hits = Step(world, 2);
            Assert.That(ObjectsOf(world, WorldObjectKind.Decoy).Any(d => !d.Ended), Is.False, "3 回で爆発しない");
            HitRecord blast = hits.FirstOrDefault(h => h.Attacker == 0 && h.Victim == 1);
            Assert.That(blast.Data, Is.Not.Null, "爆発が近くの相手に当たらない");
            Assert.That(blast.Data.Damage, Is.EqualTo(ItemRules.DecoyBlastDamage(30, tuning)), "爆発の威力が記録した威力の合計に応じない");
            Assert.That(ItemRules.DecoyBlastRadius(30, tuning), Is.GreaterThan(ItemRules.DecoyBlastRadius(5, tuning)), "記録が多いほど範囲が広くならない");
        }

        // 持ち主の攻撃と投射物も受けて記録する（ユーザーの指定、2026-11-06）。投射物は人形に当たって消える。
        [Test]
        public void Decoy_TakesTheOwnersOwnAttacksAndProjectiles()
        {
            var tuning = new ItemTuning();
            SimWorld shot = World(PreItem(), new Vector3(0f, GroundY, 0f), new Vector3(30f, GroundY, 0f));
            shot.SetItems(0, new[] { ItemId.DecoyDoll, ItemId.EnergyShot });
            Use(shot, 0, new Vector2(1f, 0f));
            Assert.That(ObjectsOf(shot, WorldObjectKind.Decoy).Length, Is.EqualTo(1), "前提: 人形を置いた");
            Use(shot, 1, new Vector2(1f, 0f));
            Step(shot, 20);
            WorldObject doll = ObjectsOf(shot, WorldObjectKind.Decoy)[0];
            Assert.That(doll.Damage, Is.EqualTo(1), "自分の投射物が人形に当たらない");
            Assert.That(doll.ChargeMilli, Is.EqualTo(tuning.ShotHit.Damage), "自分の投射物の威力を記録しない");
            Assert.That(ObjectsOf(shot, WorldObjectKind.ItemShot).Any(s => !s.Ended), Is.False, "投射物が人形に当たっても消えない");

            SimWorld melee = World(PreItem(), new Vector3(0f, GroundY, 0f), new Vector3(30f, GroundY, 0f));
            melee.SetItems(0, new[] { ItemId.DecoyDoll });
            Use(melee, 0, new Vector2(1f, 0f));
            Step(melee, 1, Keys(InputButtons.Attack));
            Step(melee, 40);
            WorldObject struck = ObjectsOf(melee, WorldObjectKind.Decoy)[0];
            Assert.That(struck.Damage, Is.GreaterThanOrEqualTo(1), "自分の近接攻撃が人形に当たらない");
            Assert.That(struck.ChargeMilli, Is.GreaterThan(0), "自分の近接攻撃の威力を記録しない");
        }

        [Test]
        public void BankSeal_BanksHalf_AndBigMagnetPullsCoins()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.BankSeal, ItemId.BigMagnet });
            world.Coins.Spawn(new CoinEventId(0, CoinEventKind.Spawn, 0), new Vector2(0f, GroundY), 40, 0, int.MaxValue);
            Step(world, 2);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(40), "前提: コインを拾った");
            Use(world, 0, new Vector2(1f, 0f));
            Step(world, new ItemTuning().SealCastTicks + 2);
            Assert.That(world.GetState(0).Coins.Banked, Is.EqualTo(20), "所持の半分を確定しない");
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(20));

            world.Coins.Spawn(new CoinEventId(world.Tick, CoinEventKind.Spawn, 1), new Vector2(6f, GroundY + 1f), 5, 0, int.MaxValue);
            Use(world, 1, new Vector2(1f, 0f));
            Step(world, 60);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(25), "大きな磁石でコインを吸い寄せない");
        }

        [Test]
        public void PocketRocket_DashesTowardTheAim()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY + 3f, 0f));
            world.SetItems(0, new[] { ItemId.PocketRocket });
            Use(world, 0, new Vector2(1f, 0f));
            Step(world, 10);
            Assert.That(world.GetState(0).Position.X, Is.GreaterThan(4f), "照準の方向へ速く動かない");
        }

        // アイテムのキャンセル（ユーザーの要望、2026-11-06）: 使っている最中にワイヤーでキャンセルでき（クールタイムが始まる）、
        // 当たった攻撃はキャンセルしてアイテムを使える（当たっていない攻撃の間は使えない）。
        [Test]
        public void Items_CanBeWireCancelled_AndCancelAHitAttack()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY + 3f, 0f));
            world.SetItems(0, new[] { ItemId.PocketRocket });
            Use(world, 0, new Vector2(1f, 0f));
            Assert.That(world.GetState(0).Items.Acting, Is.True, "前提: ロケットで動いている");
            Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(new Vector2(0f, 1f)));
            CharacterState state = world.GetState(0);
            Assert.That(state.Items.Acting, Is.False, "ワイヤーでキャンセルできない");
            Assert.That(state.Combat.WireCancelCooldownTicksLeft, Is.GreaterThan(0), "ワイヤーキャンセルのクールタイムが始まらない");
            Assert.That(state.Wire.Phase, Is.Not.EqualTo(Airerage.Core.Wire.WirePhase.Ready), "キャンセルしたティックにワイヤーを撃たない");

            var attacking = new CharacterState { Action = new ActionState { Kind = ActionKind.Attack, Attack = AttackSlot.Ground } };
            Assert.That(ItemRules.CanActivate(attacking), Is.False, "当たっていない攻撃の間に使えてしまう");
            attacking.Action.Connected = true;
            attacking.Combat.HitstopTicksLeft = 6;
            Assert.That(ItemRules.CanActivate(attacking), Is.True, "当たった攻撃をキャンセルして使えない");
        }

        [Test]
        public void SpikeSoles_FreezeTheWall_AndOpponentsWhoTouchIt()
        {
            var objects = new WorldObjects();
            var tuning = new ItemTuning();
            var config = new CharacterConfig();
            var states = new List<CharacterState>
            {
                new CharacterState { Position = new Vector3(0f, 5f, 0f), WallSide = 1 },
                new CharacterState { Position = new Vector3(20f, 5f, 0f) },
            };
            var configs = new[] { ItemRules.ConfigFor(Bag(ItemId.SpikeSoles), config, tuning), config };
            ItemRules.StepWorld(states, configs, tuning, objects, null, null, MatchRules.FreePlay(), null, 10, true, new List<HitRecord>());
            Assert.That(objects.Items.Count(o => o.Kind == WorldObjectKind.FrozenWall), Is.EqualTo(1), "壁を凍らせない");
            states[1] = new CharacterState { Position = new Vector3(config.Movement.HalfWidth * 2f, 5f, 0f) };
            ItemRules.StepWorld(states, configs, tuning, objects, null, null, MatchRules.FreePlay(), null, 11, true, new List<HitRecord>());
            Assert.That(states[1].Combat.IsFrozen, Is.True, "凍った壁に触れた相手が凍らない");
        }

        // ---------------------------------------------------------------- 落ちているアイテム

        [Test]
        public void DroppedItems_VanishThirtySecondsAfterLanding_AndSpreadApart()
        {
            SimWorld world = World(PreItem(), new Vector3(-20f, GroundY, 0f));
            world.Objects.Add(new WorldObject { Kind = WorldObjectKind.DroppedItem, Owner = 0, Position = new Vector2(0f, 2f), ExpireTick = int.MaxValue, Variant = (byte)ItemId.SkyBoots });
            world.Objects.Add(new WorldObject { Kind = WorldObjectKind.DroppedItem, Owner = 0, Position = new Vector2(0.1f, 2f), ExpireTick = int.MaxValue, Variant = (byte)ItemId.GoldBell });
            Step(world, 90);
            WorldObject[] items = ObjectsOf(world, WorldObjectKind.DroppedItem);
            Assert.That(items.Length, Is.EqualTo(2));
            Assert.That(System.MathF.Abs(items[0].Position.X - items[1].Position.X), Is.GreaterThanOrEqualTo(new ItemTuning().DropHalfSize * 2f - 0.05f),
                "重なったアイテムが広がらない");
            Assert.That(items[0].ExpireTick, Is.LessThan(int.MaxValue), "着地しても消えるまでを数えない");
            Step(world, new ItemTuning().DropLifetimeTicks);
            Assert.That(ObjectsOf(world, WorldObjectKind.DroppedItem), Is.Empty, "30 秒たっても消えない");
        }

        [Test]
        public void RandomStartItems_AreTheSameForTheSameSeed()
        {
            ItemId[] a = ItemRules.ResolveStartItems(new[] { ItemId.Random }, 3, 1234, 2);
            ItemId[] b = ItemRules.ResolveStartItems(new[] { ItemId.Random }, 3, 1234, 2);
            Assert.That(a, Is.EqualTo(b), "同じ種で違うアイテムになった");
            Assert.That(a.Length, Is.EqualTo(3));
            Assert.That(a.Distinct().Count(), Is.EqualTo(3), "同じアイテムが重なった");
            Assert.That(a.All(ItemCatalog.IsValid), Is.True);
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.Random });
            Assert.That(world.GetState(0).Items.IsEmpty, Is.False, "ランダムセレクトで何も持たない");
        }

        // ---------------------------------------------------------------- THIEF とマガジンマガジン

        [Test]
        public void StolenSkill_KeepsCountingTheOriginalCooldownInTheBackground()
        {
            var c = new CombatState
            {
                StealKind = StealKind.Skill, StealSavedMax = 2, StealSavedSpent = 2, StealSavedCooldownTicks = 10,
            };
            for (int t = 0; t < 10; t++)
            {
                ThiefRules.CountSavedCooldown(ref c);
            }
            Assert.That(c.StealSavedSpent, Is.EqualTo(1), "裏でクールタイムを数えない");
            for (int t = 0; t < 30; t++)
            {
                ThiefRules.CountSavedCooldown(ref c);
            }
            Assert.That(c.StealSavedSpent, Is.EqualTo(1), "スタック数 − 1 に届いても止まらない");
        }
    }
}
