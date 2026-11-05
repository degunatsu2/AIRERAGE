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
    /// アイテム（ユーザーの要望、2026-11-05。docs/20_item_plan.md）: プリアイテム、パッシブの効果、拾う・捨てる、アイテムドロップ、
    /// アクティブアイテム（エネルギーショット・櫓落とし・イヤーボーン・イカロスウイング・砂岩ボール）、巻き戻し。
    /// </summary>
    public class ItemTests
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

        private static MatchRules PreItem(int count = 3)
        {
            MatchRules rules = MatchRules.FreePlay();
            rules.ItemMode = ItemMode.PreItem;
            rules.StartItemCount = count;
            return rules;
        }

        private static PlayerInput Keys(InputButtons buttons, float x = 0f, float y = 0f)
        {
            return PlayerInput.FromAxes(x, y, buttons);
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

        // 枠 slot のキーを押して離す（アクティブアイテムを使う）。照準 aim つき。
        private static List<HitRecord> Use(SimWorld world, int slot, Vector2 aim)
        {
            InputButtons key = ItemRules.SlotButton(slot);
            var hits = Step(world, 1, Keys(key).WithAim(aim));
            hits.AddRange(Step(world, 1, Keys(InputButtons.None).WithAim(aim)));
            return hits;
        }

        private static WorldObject[] ObjectsOf(SimWorld world, WorldObjectKind kind)
        {
            return world.Objects.Items.Where(o => o.Kind == kind).ToArray();
        }

        // ---------------------------------------------------------------- プリアイテム

        [Test]
        public void PreItem_PutsTheChosenItemsInTheSlotsInOrder_UpToTheCount()
        {
            SimWorld world = World(PreItem(2), new Vector3(0f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.EnergyShot, ItemId.None, ItemId.QuickBooster, ItemId.EarBone });
            ItemBag bag = world.GetState(0).Items;
            Assert.That(bag.Get(0), Is.EqualTo(ItemId.EnergyShot));
            Assert.That(bag.Get(1), Is.EqualTo(ItemId.QuickBooster), "空きを飛ばして詰めない");
            Assert.That(bag.Get(2), Is.EqualTo(ItemId.None), "初期アイテム数より多く入れた");

            SimWorld plain = World(MatchRules.FreePlay(), new Vector3(0f, GroundY, 0f));
            plain.SetItems(0, new[] { ItemId.EnergyShot });
            Assert.That(plain.GetState(0).Items.IsEmpty, Is.True, "プリアイテムでないのに入れた");
        }

        [Test]
        public void Items_AreKeptAfterAKnockout()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.QuickBooster, ItemId.EnergyShot });
            CharacterState state = world.GetState(0);
            state.Position = new Vector3(0f, -50f, 0f);
            world.SetState(0, state);
            Step(world, 2);
            Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(1), "前提: 撃破された");
            Assert.That(world.GetState(0).Items.Get(0), Is.EqualTo(ItemId.QuickBooster), "撃破でアイテムがなくなった");
            Assert.That(world.GetState(0).Items.Get(1), Is.EqualTo(ItemId.EnergyShot));
        }

        // ---------------------------------------------------------------- パッシブ

        [Test]
        public void Boosters_ChangeTheCooldownsAndTheGaugeCost()
        {
            var tuning = new ItemTuning();
            var config = new CharacterConfig();
            CharacterConfig quick = ItemRules.ConfigFor(Bag(ItemId.QuickBooster), config, tuning);
            Assert.That(quick.Combat.Special1CooldownTicks, Is.EqualTo((int)System.MathF.Round(config.Combat.Special1CooldownTicks * 0.8f)), "Q が 20% 減らない");
            Assert.That(quick.Combat.Special2CooldownTicks, Is.EqualTo(config.Combat.Special2CooldownTicks));

            CharacterConfig energy = ItemRules.ConfigFor(Bag(ItemId.EnergyBooster), config, tuning);
            Assert.That(energy.Combat.Special2CooldownTicks, Is.EqualTo((int)System.MathF.Round(config.Combat.Special2CooldownTicks * 0.8f)), "E が 20% 減らない");

            CharacterConfig ultimate = ItemRules.ConfigFor(Bag(ItemId.UltimateBooster), config, tuning);
            Assert.That(ultimate.Combat.SuperGaugeCost, Is.EqualTo((int)System.MathF.Round(config.Combat.SuperGaugeCost * 0.8f)), "R のゲージが 20% 減らない");

            CharacterConfig quickEx = ItemRules.ConfigFor(Bag(ItemId.QuickBoosterEx), config, tuning);
            Assert.That(quickEx.Combat.Special1CooldownTicks, Is.EqualTo((int)System.MathF.Round(config.Combat.Special1CooldownTicks * 0.6f)), "Q が 40% 減らない");
            Assert.That(quickEx.Combat.Special2CooldownTicks, Is.EqualTo((int)System.MathF.Round(config.Combat.Special2CooldownTicks * 1.2f)), "E が 20% 増えない");

            CharacterConfig energyEx = ItemRules.ConfigFor(Bag(ItemId.EnergyBoosterEx), config, tuning);
            Assert.That(energyEx.Combat.Special2CooldownTicks, Is.EqualTo((int)System.MathF.Round(config.Combat.Special2CooldownTicks * 0.6f)), "E が 40% 減らない");
            Assert.That(energyEx.Combat.Special1CooldownTicks, Is.EqualTo((int)System.MathF.Round(config.Combat.Special1CooldownTicks * 1.2f)), "Q が 20% 増えない");

            CharacterConfig ultimateEx = ItemRules.ConfigFor(Bag(ItemId.UltimateBoosterEx), config, tuning);
            Assert.That(ultimateEx.Combat.SuperGaugeCost, Is.EqualTo((int)System.MathF.Round(config.Combat.SuperGaugeCost * 0.6f)), "R のゲージが 40% 減らない");
            Assert.That(ultimateEx.Combat.Special1CooldownTicks, Is.EqualTo((int)System.MathF.Round(config.Combat.Special1CooldownTicks * 1.1f)), "Q が 10% 増えない");
            Assert.That(ultimateEx.Combat.Special2CooldownTicks, Is.EqualTo((int)System.MathF.Round(config.Combat.Special2CooldownTicks * 1.1f)), "E が 10% 増えない");

            CharacterConfig shield = ItemRules.ConfigFor(Bag(ItemId.PhotonShield), config, tuning);
            Assert.That(shield.Combat.GuardCooldownTicks, Is.EqualTo((int)System.MathF.Round(config.Combat.GuardCooldownTicks * 0.8f)), "ガードが 20% 減らない");

            Assert.That(config.Combat.Special1CooldownTicks, Is.EqualTo(new CharacterConfig().Combat.Special1CooldownTicks), "もとの調整値を書き換えた");
            Assert.That(ItemRules.ConfigFor(Bag(ItemId.EnergyShot), config, tuning), Is.SameAs(config), "パッシブがないのに写しを作った");
        }

        [Test]
        public void Magazine_AddsAChargeToProjectileSkills_Banana_GrowsPlacements_Diamond_StrengthensSkills()
        {
            var tuning = new ItemTuning();
            CharacterConfig juggernaut = CharacterPresets.Juggernaut();
            CharacterConfig loaded = ItemRules.ConfigFor(Bag(ItemId.MagazineMagazine), juggernaut, tuning);
            Assert.That(loaded.Combat.Special1MaxCharges, Is.EqualTo(juggernaut.Combat.Special1MaxCharges + 1), "フラック（投射物）のスタックが増えない");
            CharacterConfig storm = CharacterPresets.StormRunner();
            Assert.That(ItemRules.ConfigFor(Bag(ItemId.MagazineMagazine), storm, tuning).Combat.Special1MaxCharges,
                Is.EqualTo(storm.Combat.Special1MaxCharges), "投射物でない技のスタックが増えた");

            CharacterConfig risk = CharacterPresets.RiskTaker();
            CharacterConfig grown = ItemRules.ConfigFor(Bag(ItemId.GreenBanana), risk, tuning);
            Assert.That(grown.Skills.MirrorBall.Radius, Is.EqualTo(risk.Skills.MirrorBall.Radius * 1.5f).Within(1e-4f), "ミラーボールが 1.5 倍にならない");
            Assert.That(risk.Skills.MirrorBall.Radius, Is.EqualTo(CharacterPresets.RiskTaker().Skills.MirrorBall.Radius), "もとの調整値を書き換えた");

            CharacterConfig heavy = ItemRules.ConfigFor(Bag(ItemId.DiamondDumbbell), new CharacterConfig(), tuning);
            Assert.That(heavy.Combat.SkillDamageBonusPercent, Is.EqualTo(50), "技の威力が 1.5 倍にならない");
        }

        [Test]
        public void Diamond_MakesTheSkillHitsOnePointFiveTimesStronger()
        {
            CharacterConfig config = ItemRules.ConfigFor(Bag(ItemId.DiamondDumbbell), new CharacterConfig(), new ItemTuning());
            var state = CharacterState.Spawn(Vector3.Zero, 1);
            state.Action = new ActionState { Kind = ActionKind.Attack, Attack = AttackSlot.Special1 };
            AttackPower power = AttackPower.Of(state, 0, config, 0);
            Assert.That(power.DamageScale, Is.EqualTo(1.5f).Within(1e-4f));
            state.Action = new ActionState { Kind = ActionKind.Attack, Attack = AttackSlot.AirSide };
            Assert.That(AttackPower.Of(state, 0, config, 0).DamageScale, Is.EqualTo(1f).Within(1e-4f), "通常の攻撃も強くなった");
        }

        [Test]
        public void PassiveItems_ChangeTheConfigUsedInTheWorld()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f));
            int baseCooldown = world.GetConfig(0).Combat.Special1CooldownTicks;
            world.SetItems(0, new[] { ItemId.QuickBooster });
            Assert.That(world.GetConfig(0).Combat.Special1CooldownTicks, Is.LessThan(baseCooldown), "持っているパッシブが効かない");
            Assert.That(world.GetBaseConfig(0).Combat.Special1CooldownTicks, Is.EqualTo(baseCooldown));
        }

        private static ItemBag Bag(params ItemId[] items)
        {
            return ItemRules.StartingBag(items, 3);
        }

        // ---------------------------------------------------------------- 拾う・捨てる

        [Test]
        public void ADroppedItem_FallsAndRests_AndAnEmptySlotKeyPicksItUp()
        {
            SimWorld world = World(MatchRules.FreePlay(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            world.Objects.Add(new WorldObject
            {
                Kind = WorldObjectKind.DroppedItem, Position = new Vector2(0.3f, 6f), ExpireTick = int.MaxValue, Variant = (byte)ItemId.PhotonShield,
            });
            Step(world, 60);
            WorldObject dropped = ObjectsOf(world, WorldObjectKind.DroppedItem).Single();
            Assert.That(dropped.Stored, Is.EqualTo(1), "足場に止まらない");
            Assert.That(dropped.Position.Y, Is.EqualTo(new ItemTuning().DropHalfSize).Within(0.05f), "地面の上にない");

            Step(world, 1, Keys(InputButtons.Item2));
            Step(world, 1);
            Assert.That(world.GetState(0).Items.Get(1), Is.EqualTo(ItemId.PhotonShield), "押した枠に拾わない");
            Assert.That(world.GetState(0).Items.PickupCount, Is.EqualTo(1));
            Assert.That(ObjectsOf(world, WorldObjectKind.DroppedItem), Is.Empty, "拾ったのにフィールドに残っている");
        }

        [Test]
        public void HoldingASlotKey_AndPressingDrop_DiscardsTheItem_WhichCanBePickedUpAgainLater()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.EnergyShot });
            Step(world, 2);
            Step(world, 1, Keys(InputButtons.Item1));
            Step(world, 1, Keys(InputButtons.Item1 | InputButtons.ItemDrop));
            Step(world, 1, Keys(InputButtons.None));
            Assert.That(world.GetState(0).Items.Get(0), Is.EqualTo(ItemId.None), "捨てない");
            Assert.That(ObjectsOf(world, WorldObjectKind.ItemShot), Is.Empty, "捨てるつもりで使った");
            Assert.That(ObjectsOf(world, WorldObjectKind.DroppedItem).Single().Variant, Is.EqualTo((byte)ItemId.EnergyShot));

            // すぐには拾い直せない（捨てた人だけ）。
            Step(world, 1, Keys(InputButtons.Item1));
            Step(world, 1);
            Assert.That(world.GetState(0).Items.Get(0), Is.EqualTo(ItemId.None), "捨ててすぐに拾い直した");
            Step(world, 60);
            Step(world, 1, Keys(InputButtons.Item1));
            Step(world, 1);
            Assert.That(world.GetState(0).Items.Get(0), Is.EqualTo(ItemId.EnergyShot), "しばらくしても拾い直せない");
        }

        [Test]
        public void PressingAnOccupiedSlot_DoesNotPickUp()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.QuickBooster });
            Step(world, 2);
            world.Objects.Add(new WorldObject
            {
                Kind = WorldObjectKind.DroppedItem, Position = new Vector2(0f, 0.5f), ExpireTick = int.MaxValue, Variant = (byte)ItemId.PhotonShield,
            });
            Step(world, 1, Keys(InputButtons.Item1));
            Step(world, 1);
            Assert.That(world.GetState(0).Items.Get(0), Is.EqualTo(ItemId.QuickBooster), "入っている枠のアイテムを入れ替えた");
            Assert.That(ObjectsOf(world, WorldObjectKind.DroppedItem), Has.Length.EqualTo(1));
        }

        // ---------------------------------------------------------------- アイテムドロップ

        [Test]
        public void ItemDrop_DropsItemsFromThePool_AtTheChosenRate()
        {
            MatchRules rules = MatchRules.FreePlay();
            rules.ItemMode = ItemMode.ItemDrop;
            rules.ItemDropRate = 1;
            rules.ItemDropPool = (long)(ItemCatalog.Bit(ItemId.EarBone) | ItemCatalog.Bit(ItemId.PhotonShield));
            var spawns = new[] { new CoinSpawnPoint(new Vector2(-5f, 8f), 1), new CoinSpawnPoint(new Vector2(5f, 8f), 1) };
            var world = new SimWorld(TestArenas.Ground(), new StageBounds(-60f, -30f, 60f, 60f), rules: rules, coinSpawns: spawns);
            world.AddCharacter(new CharacterConfig(), new Vector3(30f, GroundY, 0f));
            int interval = world.ItemTuning.DropIntervalOf(1);
            Step(world, interval - 1);
            Assert.That(ObjectsOf(world, WorldObjectKind.DroppedItem), Is.Empty, "早く落ちてきた");
            Step(world, 1);
            Assert.That(ObjectsOf(world, WorldObjectKind.DroppedItem), Has.Length.EqualTo(1), "間隔で落ちてこない");
            Step(world, interval * 7);
            WorldObject[] dropped = ObjectsOf(world, WorldObjectKind.DroppedItem);
            Assert.That(dropped, Has.Length.EqualTo(8), "30 秒に 8 個ほど（中）にならない");
            Assert.That(dropped.All(d => d.Variant == (byte)ItemId.EarBone || d.Variant == (byte)ItemId.PhotonShield), Is.True,
                "選んでいないアイテムが落ちてきた");
            Assert.That(world.ItemTuning.DropIntervalOf(0) * 4, Is.EqualTo(1800), "少が 30 秒に 4 個でない");
            Assert.That(world.ItemTuning.DropIntervalOf(2) * 12, Is.EqualTo(1800), "多が 30 秒に 12 個でない");

            var plain = new SimWorld(TestArenas.Ground(), new StageBounds(-60f, -30f, 60f, 60f), rules: MatchRules.FreePlay(), coinSpawns: spawns);
            plain.AddCharacter(new CharacterConfig(), new Vector3(30f, GroundY, 0f));
            Step(plain, interval * 3);
            Assert.That(ObjectsOf(plain, WorldObjectKind.DroppedItem), Is.Empty, "デフォルトのモードで落ちてきた");
        }

        // ---------------------------------------------------------------- アクティブ

        [Test]
        public void EnergyShot_FlysTowardTheAim_HitsFor10_AndStartsTheCooldown()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f), new Vector3(8f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.EnergyShot });
            Step(world, 2);
            Step(world, 1, Keys(InputButtons.Item1).WithAim(Vector2.UnitX));
            Assert.That(ObjectsOf(world, WorldObjectKind.ItemShot), Is.Empty, "押した瞬間に撃った（離して撃つ）");
            List<HitRecord> hits = Step(world, 1, Keys(InputButtons.None).WithAim(Vector2.UnitX));
            Assert.That(ObjectsOf(world, WorldObjectKind.ItemShot), Has.Length.EqualTo(1), "離しても撃たない");
            Assert.That(world.GetState(0).Items.Cooldown0, Is.EqualTo(world.ItemTuning.ShotCooldownTicks - 0).Within(1), "クールタイムに入らない");
            hits.AddRange(Step(world, 30));
            HitRecord hit = hits.Single(h => h.Victim == 1);
            Assert.That(hit.Damage, Is.EqualTo(10), "威力が 10 でない");
            Assert.That(ObjectsOf(world, WorldObjectKind.ItemShot), Is.Empty, "当たっても消えない");

            Use(world, 0, Vector2.UnitX);
            Assert.That(ObjectsOf(world, WorldObjectKind.ItemShot), Is.Empty, "クールタイム中に撃てた");
        }

        [Test]
        public void Magazine_GivesTheEnergyShotTwoCharges()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.EnergyShot, ItemId.MagazineMagazine });
            Step(world, 2);
            Use(world, 0, Vector2.UnitX);
            Use(world, 0, Vector2.UnitX);
            Assert.That(ObjectsOf(world, WorldObjectKind.ItemShot), Has.Length.EqualTo(2), "続けて 2 回撃てない");
            Use(world, 0, Vector2.UnitX);
            Assert.That(ObjectsOf(world, WorldObjectKind.ItemShot), Has.Length.EqualTo(2), "3 回撃てた");
        }

        [Test]
        public void Yaguraotoshi_SlashesThreeTimes_Casts_Dashes_AndTheFlashIsFatal()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY + 0.5f, 0f), new Vector3(1.6f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.Yaguraotoshi });
            Step(world, 2);
            CharacterState before = world.GetState(0);
            List<HitRecord> hits = Use(world, 0, Vector2.UnitX);
            ItemTuning tuning = world.ItemTuning;
            Assert.That(world.GetState(0).Items.Act, Is.EqualTo((byte)ItemId.Yaguraotoshi), "使い始めない");
            hits.AddRange(Step(world, tuning.SwordSlashesTicks));
            Assert.That(world.GetState(0).Position.Y, Is.EqualTo(before.Position.Y).Within(0.05f), "振りの間に落ちた");
            Assert.That(hits.Count(h => h.Victim == 1 && h.Damage == 5), Is.EqualTo(3), "3 回の振りが当たらない");

            // 詠唱の間は棒のゲージを出す（詠唱の段階）。
            Assert.That(ItemRules.TryGetCast(world.GetState(0), tuning, out int total, out _), Is.True, "詠唱しない");
            Assert.That(total, Is.EqualTo(tuning.SwordCastTicks));
            float x = world.GetState(0).Position.X;
            // ダミーを一閃の通り道に置き直す。
            CharacterState dummy = world.GetState(1);
            dummy.Position = new Vector3(4f, world.GetState(0).Position.Y, 0f);
            dummy.Action = default;
            dummy.Velocity = Vector3.Zero;
            world.SetState(1, dummy);
            hits.AddRange(Step(world, tuning.SwordCastTicks + tuning.SwordDashTicks + 2));
            Assert.That(world.GetState(0).Position.X, Is.GreaterThan(x + tuning.SwordDashDistance - 0.5f), "照準の方向へ突進しない");
            HitRecord flash = hits.Single(h => h.Victim == 1 && h.Damage == 30);
            Assert.That(flash.Fatal, Is.True, "一閃が FATAL でない");
            Assert.That(world.GetState(0).Items.Acting, Is.False, "終わらない");
            Assert.That(world.GetState(0).Items.FlashCount, Is.EqualTo(1));
        }

        [Test]
        public void EarBone_PushesOthersOutOfTheCircle_WithoutDamage()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f), new Vector3(2f, GroundY, 0f), new Vector3(20f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.EarBone });
            Step(world, 2);
            List<HitRecord> hits = Use(world, 0, Vector2.UnitX);
            hits.AddRange(Step(world, world.ItemTuning.EarCastTicks + 1));
            Assert.That(world.GetState(0).Items.BurstCount, Is.EqualTo(1), "風を起こさない");
            Step(world, 15);
            Assert.That(world.GetState(1).Position.X, Is.GreaterThan(world.ItemTuning.EarRadius), "円の外へ押し出さない");
            Assert.That(world.GetState(2).Position.X, Is.EqualTo(20f).Within(0.01f), "円の外の人まで押した");
            Assert.That(hits, Is.Empty, "攻撃になっている");
        }

        [Test]
        public void IcarusWing_FliesFreelyAtTheRunSpeed_ThenEnds()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.IcarusWing });
            Step(world, 2);
            Use(world, 0, Vector2.UnitX);
            ItemTuning tuning = world.ItemTuning;
            Step(world, tuning.WingCastTicks - 1, Keys(InputButtons.None, 0f, 1f));
            float y = world.GetState(0).Position.Y;
            Step(world, 30, Keys(InputButtons.None, 0f, 1f));
            float risen = world.GetState(0).Position.Y - y;
            Assert.That(risen, Is.EqualTo(new CharacterConfig().Movement.RunSpeed * 0.5f).Within(0.6f), "上へ地上の速さで飛ばない");
            Step(world, 10, Keys(InputButtons.None));
            Assert.That(world.GetState(0).Items.Acting, Is.True, "1 秒たたずに終わった");
            float hover = world.GetState(0).Position.Y;
            Step(world, 5, Keys(InputButtons.None));
            Assert.That(world.GetState(0).Position.Y, Is.EqualTo(hover).Within(0.1f), "飛んでいる間に落ちた");
            int guard = 0;
            while (world.GetState(0).Items.Acting && guard++ < tuning.WingFlightTicks)
            {
                Step(world, 1);
            }
            Assert.That(world.GetState(0).Items.Acting, Is.False, "終わらない");
            Step(world, 3);
            Assert.That(world.GetState(0).Velocity.Y, Is.LessThan(0f), "終わっても落ちない");
        }

        [Test]
        public void SandstoneBall_Falls_IsStruckByAttacks_HitsPeople_AndStartsTheCooldownWhenItVanishes()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f), new Vector3(9f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.SandstoneBall });
            Step(world, 2);
            Use(world, 0, Vector2.UnitX);
            WorldObject ball = ObjectsOf(world, WorldObjectKind.SandBall).Single();
            Assert.That(world.GetState(0).Items.Cooldown0, Is.EqualTo(CombatState.CooldownWhileActive), "ボールがあるのにクールタイムを数えている");
            Step(world, 40);
            ball = ObjectsOf(world, WorldObjectKind.SandBall).Single();
            Assert.That(ball.Position.Y, Is.EqualTo(ball.End.X).Within(0.1f), "地面まで落ちない");

            // 攻撃で打つと、その吹き飛びのまま飛び、動いているボールに当たった人は攻撃を受ける。
            var objects = new WorldObjects();
            objects.CopyFrom(world.Objects);
            Assert.That(ItemRules.StrikeBalls(objects, 0, 1234, ball.Position, new Vector2(0.5f, 0.5f), new Vector2(20f, 4f), world.Tick,
                world.ItemTuning), Is.True, "攻撃の判定で打てない");
            WorldObject struck = objects.Items.Single(o => o.Kind == WorldObjectKind.SandBall);
            Assert.That(struck.Velocity.X, Is.EqualTo(20f).Within(1e-3f), "攻撃の吹き飛びで飛ばない");
            int index = world.Objects.IndexOf(ball.Id);
            world.Objects[index] = struck;
            List<HitRecord> hits = Step(world, 30);
            HitRecord hit = hits.Single(h => h.Victim == 1);
            Assert.That(hit.Damage, Is.GreaterThan(3), "速さに応じた威力でない");
            Assert.That(hit.Attacker, Is.EqualTo(0), "打った人の手柄にならない");

            Step(world, world.ItemTuning.BallLifetimeTicks);
            Assert.That(ObjectsOf(world, WorldObjectKind.SandBall), Is.Empty, "5 秒で消えない");
            Assert.That(world.GetState(0).Items.Cooldown0, Is.InRange(1, world.ItemTuning.BallCooldownTicks), "消えてもクールタイムが始まらない");
        }

        [Test]
        public void Items_RollBackExactly()
        {
            SimWorld world = World(PreItem(), new Vector3(0f, GroundY, 0f), new Vector3(3f, GroundY, 0f));
            world.SetItems(0, new[] { ItemId.Yaguraotoshi, ItemId.SandstoneBall, ItemId.QuickBooster });
            Step(world, 2);
            Use(world, 1, Vector2.UnitX);
            Use(world, 0, Vector2.UnitX);
            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);
            Step(world, 40);
            ulong expected = world.Checksum();
            world.LoadFrom(snapshot);
            Step(world, 40);
            Assert.That(world.Checksum(), Is.EqualTo(expected), "巻き戻して同じ入力で進めても同じにならない");
        }
    }
}
