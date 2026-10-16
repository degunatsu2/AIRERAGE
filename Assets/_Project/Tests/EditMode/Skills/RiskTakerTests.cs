using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Core.Tests.Support;
using Airerage.Core.Wire;
using NUnit.Framework;

namespace Airerage.Core.Tests.Skills
{
    /// <summary>
    /// RISK TAKER（docs/14 1.3）と、技の効果・置かれた物の共通の仕組み（docs/13 T5-6）: ミラーボール、セーフティボックス、
    /// フィーバータイム、ルーレットスマッシュ、コイントス、オールイン、長押し、コインの総量、巻き戻し。
    /// </summary>
    public class RiskTakerTests
    {
        private const float GroundY = 0.91f;
        private static readonly CharacterConfig Risk = CharacterPresets.RiskTaker();

        private static PlayerInput Press(InputButtons buttons, float x = 0f, float y = 0f)
        {
            return PlayerInput.FromAxes(x, y, buttons);
        }

        private static void Step(SimWorld world, int ticks, params PlayerInput[] inputs)
        {
            var all = new PlayerInput[world.CharacterCount];
            for (int i = 0; i < all.Length && i < inputs.Length; i++)
            {
                all[i] = inputs[i];
            }
            for (int t = 0; t < ticks; t++)
            {
                world.Step(all);
            }
        }

        private static void Edit(SimWorld world, int index, System.Func<CharacterState, CharacterState> edit)
        {
            world.SetState(index, edit(world.GetState(index)));
        }

        // 総量の保存: フィールド ＋ 所持 ＋ 確定 ＋ 預け入れ − (生成 − 破棄)。テストで所持コインを直接与えた分だけずれる。
        private static long Imbalance(SimWorld world)
        {
            var states = new List<CharacterState>();
            for (int i = 0; i < world.CharacterCount; i++)
            {
                states.Add(world.GetState(i));
            }
            CoinTotals t = world.Coins.Totals(states);
            return t.Field + t.Held + t.Banked + t.Stored - (t.Generated - t.Destroyed);
        }

        [Test]
        public void Preset_IsValid_AndDiffersFromTheStandard()
        {
            Assert.DoesNotThrow(() => CharacterPresets.RiskTaker().Validate());
            Assert.That(Risk.Combat.SuperGaugeCost, Is.EqualTo(50 * CombatTuning.GaugePerPercent));
            Assert.That(Risk.Combat.Special1.Effect, Is.EqualTo(SkillEffect.MirrorBall));
            Assert.That(Risk.Combat.Special2.Effect, Is.EqualTo(SkillEffect.Safe));
            Assert.That(Risk.Combat.Special2.HoldMaxTicks, Is.GreaterThan(0));
            Assert.That(Risk.Combat.Super.Effect, Is.EqualTo(SkillEffect.CoinBoost));
            Assert.That(Risk.Combat.AirDown.Roulette, Is.True);
            Assert.That(CharacterPresets.Standard().Combat.SuperGaugeCost, Is.EqualTo(70 * CombatTuning.GaugePerPercent));
            Assert.That(Risk.Combat.Special1CooldownTicks, Is.EqualTo(15 * SimTime.TicksPerSecond), "LIGHT のクールタイムが 15 秒でない");
            Assert.That(Risk.Combat.Ground.ChargeLevels.Select(l => l.BonusPercent), Is.EqualTo(new[] { 0, 20, 50, 100, 200, 500 }));
        }

        [Test]
        public void MirrorBall_IsAnAnchor_GivesGaugeToWhoeverIsAttached_AndReleasesTheWireWhenItVanishes()
        {
            var world = new SimWorld(new BoxTerrain());
            world.AddCharacter(CharacterPresets.RiskTaker(), new Vector3(0f, 50f, 0f));
            CharacterConfig other = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            world.AddCharacter(other, new Vector3(-3f, 45f, 0f), -1);
            int anchorsBefore = world.Collision.AnchorPoints.Count;

            Step(world, 1, Press(InputButtons.Special1));
            Step(world, Risk.Combat.Special1.EffectTick);
            Assert.That(world.Objects.Count, Is.EqualTo(1), "ミラーボールを置かない");
            WorldObject ball = world.Objects[0];
            Assert.That(ball.Kind, Is.EqualTo(WorldObjectKind.MirrorBall));
            Assert.That(ball.Position.Y, Is.EqualTo(world.GetState(0).Position.Y + Risk.Skills.MirrorBall.OffsetY).Within(0.6f), "自分の少し上に置かない");
            Assert.That(world.GetState(0).Combat.Special1CooldownTicksLeft, Is.GreaterThan(0));

            // 次のティックから、エイムアシストの対象（アンカーポイント）になる。
            Step(world, 1);
            Assert.That(world.Collision.AnchorPoints.Count, Is.EqualTo(anchorsBefore + 1));

            // ほかの人（敵）がつなぐと、1 秒に 5% たまる（120 ティックで 10%）。
            var anchor = new Vector3(ball.Position.X, ball.Position.Y, 0f);
            Edit(world, 1, s =>
            {
                s.Position = anchor + new Vector3(-3f, -3f, 0f);
                s.Velocity = Vector3.Zero;
                s.PreviousButtons = InputButtons.Wire;
                s.Wire = new WireState { Phase = WirePhase.Attached, Anchor = anchor, RopeLength = Vector3.Distance(s.Position, anchor) };
                return s;
            });
            Step(world, 120, default, Press(InputButtons.Wire));
            Assert.That(world.GetState(1).Wire.Phase, Is.EqualTo(WirePhase.Attached), "前提: つないだまま");
            Assert.That(world.GetState(1).Combat.Gauge, Is.EqualTo(10 * CombatTuning.GaugePerPercent), "1 秒に 5% たまらない");
            Assert.That(world.GetState(0).Combat.Gauge, Is.Zero, "つないでいない人にたまった");

            // 5 秒で消え、つないでいたワイヤーは外れる。
            Step(world, Risk.Skills.MirrorBall.DurationTicks, default, Press(InputButtons.Wire));
            Assert.That(world.Objects.Count, Is.Zero, "ミラーボールが消えない");
            Assert.That(world.GetState(1).Wire.Phase, Is.Not.EqualTo(WirePhase.Attached), "消えたのにつないだまま");
            Assert.That(world.GetState(1).Wire.LastReleaseReason, Is.EqualTo(WireReleaseReason.AnchorGone));
            Step(world, 1);
            Assert.That(world.Collision.AnchorPoints.Count, Is.EqualTo(anchorsBefore));
        }

        [Test]
        public void Safe_HoldingDepositsCoins_ChargesWithInterest_AndSpillsWhenBroken()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.RiskTaker(), new Vector3(0f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(-6f, GroundY, 0f));
            Step(world, 1);
            Edit(world, 0, s => { s.Coins.Held = 100; return s; });
            long imbalance = Imbalance(world);

            // E を押し続けている間はためる（動ける。技は出ない）。
            Step(world, 120, Press(InputButtons.Special2));
            Assert.That(world.GetState(0).Combat.Holding, Is.True, "ためていない");
            Assert.That(world.GetState(0).Action.IsFree, Is.True, "ためている間に技が出た");
            int held = world.GetState(0).Combat.HoldTicks;
            Assert.That(world.Objects.Count, Is.Zero);

            // 離すと、ためた長さの分（1 秒に 30 枚）を預けて置く。
            Step(world, 1);
            SafeTuning tuning = Risk.Skills.Safe;
            int deposit = tuning.DepositFor(held);
            Assert.That(deposit, Is.EqualTo(held / 2), "1 秒に 30 枚");
            Assert.That(world.Objects.Count, Is.EqualTo(1), "離しても置かない");
            WorldObject safe = world.Objects[0];
            Assert.That(safe.Kind, Is.EqualTo(WorldObjectKind.Safe));
            Assert.That(safe.Stored, Is.EqualTo(deposit));
            Assert.That(SkillRules.SafeCoins(safe), Is.EqualTo(deposit));
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(100 - deposit));
            Assert.That(world.Coins.Field.Stored, Is.EqualTo(deposit));
            Assert.That(world.GetState(0).Combat.Special2CooldownTicksLeft, Is.EqualTo(CombatState.CooldownWhileActive), "置いている間のクールタイムを止めていない");
            Assert.That(Imbalance(world), Is.EqualTo(imbalance), "預けるときにコインの総量が変わった");

            // 5 秒ごとに 2 枚足して 1.1 倍。
            Step(world, tuning.ChargeIntervalTicks);
            safe = world.Objects[0];
            Assert.That(safe.ChargeMilli, Is.EqualTo((deposit * 1000L + 2000L) * 110 / 100));

            // ほかの人が攻撃して壊すと（30 ダメージ）、その場にばらまき、クールタイムが始まる。
            int coins = SkillRules.SafeCoins(safe);
            for (int attack = 0; attack < 12 && world.Objects.Count > 0; attack++)
            {
                Edit(world, 1, s =>
                {
                    s.Position = new Vector3(safe.Position.X, safe.Position.Y + 1.5f, 0f);
                    s.Velocity = Vector3.Zero;
                    s.Grounded = false;
                    s.Action = default;
                    return s;
                });
                Step(world, 1, default, Press(InputButtons.Attack));
                Step(world, 20);
            }
            Assert.That(world.Objects.Count, Is.Zero, "攻撃しても壊れない");
            List<FieldCoin> spilled = world.Coins.Field.Coins.Where(c => c.Origin == CoinEventKind.SafeSpill).ToList();
            Assert.That(spilled.Sum(c => c.Value), Is.EqualTo(coins), "チャージした枚数をばらまかない");
            Assert.That(world.Coins.Field.Stored, Is.Zero);
            Assert.That(world.GetState(0).Combat.Special2CooldownTicksLeft, Is.InRange(1, Risk.Combat.Special2CooldownTicks), "壊れてからクールタイムが始まらない");
            Assert.That(Imbalance(world), Is.EqualTo(imbalance), "ばらまいてコインの総量が合わない");
        }

        [Test]
        public void Safe_ChargeIsCappedAt300()
        {
            var objects = new WorldObjects();
            var states = new List<CharacterState> { CharacterState.Spawn(Vector3.Zero, 1) };
            var configs = new List<CharacterConfig> { CharacterPresets.RiskTaker() };
            SafeTuning tuning = configs[0].Skills.Safe;
            objects.Add(new WorldObject { Kind = WorldObjectKind.Safe, ChargeMilli = 290_000, SpawnTick = 0, ExpireTick = int.MaxValue });

            SkillRules.StepObjects(objects, new BoxTerrain(), states, configs, null, tuning.ChargeIntervalTicks, new List<HitRecord>());

            Assert.That(objects[0].ChargeMilli, Is.EqualTo(tuning.MaxCoins * 1000L));
            Assert.That(tuning.MaxCoins, Is.EqualTo(300));
            Assert.That(tuning.GoldCoins, Is.EqualTo(150));
        }

        [Test]
        public void Safe_SpillsWhenItsTimeRunsOut()
        {
            CharacterConfig config = CharacterPresets.RiskTaker();
            config.Skills.Safe.DurationTicks = 90;
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(config, new Vector3(0f, GroundY, 0f));
            Step(world, 1);
            Edit(world, 0, s => { s.Coins.Held = 50; return s; });
            long imbalance = Imbalance(world);
            Step(world, 40, Press(InputButtons.Special2));
            Step(world, 1);
            Assert.That(world.Objects.Count, Is.EqualTo(1), "前提: 置いた");

            Step(world, 90);
            Assert.That(world.Objects.Count, Is.Zero, "時間が来ても消えない");
            Assert.That(world.Coins.Field.Coins.Where(c => c.Origin == CoinEventKind.SafeSpill).Sum(c => c.Value), Is.GreaterThan(0));
            Assert.That(Imbalance(world), Is.EqualTo(imbalance));
            Assert.That(world.GetState(0).Combat.Special2CooldownTicksLeft, Is.GreaterThan(0));
        }

        [Test]
        public void Hold_IsCancelledByAHit_AndNothingIsPlaced()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.RiskTaker(), new Vector3(0f, GroundY, 0f), -1);
            world.AddCharacter(new CharacterConfig(), new Vector3(-1.6f, GroundY, 0f));
            Step(world, 1);
            Step(world, 2, Press(InputButtons.Special2));
            Assert.That(world.GetState(0).Combat.Holding, Is.True, "前提: ためている");

            Step(world, 1, Press(InputButtons.Special2), Press(InputButtons.Attack));
            for (int t = 0; t < 20 && world.GetState(0).Action.Kind != ActionKind.Hitstun; t++)
            {
                Step(world, 1, Press(InputButtons.Special2));
            }
            Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Hitstun), "前提: 攻撃を受けた");
            Step(world, 1, Press(InputButtons.Special2));
            Assert.That(world.GetState(0).Combat.Holding, Is.False, "攻撃を受けてもためたまま");
            Step(world, 60);
            Assert.That(world.Objects.Count, Is.Zero, "やめたのに置いた");
        }

        [Test]
        public void Fever_BoostsPickedUpCoins_AndKeepsTheFraction()
        {
            var rules = new MatchRules { CountdownTicks = 0, DurationTicks = 0, CoinSpawnIntervalTicks = 30 };
            var spawns = new[]
            {
                new CoinSpawnPoint(new Vector2(20f, 1f), 5),
                new CoinSpawnPoint(new Vector2(-20f, 1f), 3),
            };
            var world = new SimWorld(TestArenas.Ground(), null, null, rules, spawns);
            world.AddCharacter(CharacterPresets.RiskTaker(), new Vector3(0f, GroundY, 0f));
            Step(world, 1);
            Edit(world, 0, s => { s.Combat.Gauge = CombatTuning.MaxGauge; return s; });
            long imbalance = Imbalance(world);

            Step(world, 1, Press(InputButtons.Super));
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Super));
            Assert.That(world.GetState(0).Combat.CoinBoostTicksLeft, Is.EqualTo(Risk.Skills.CoinBoost.DurationTicks), "効率が上がらない");
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(CombatTuning.MaxGauge - Risk.Combat.SuperGaugeCost), "50% を使わない");

            // 5 枚 × 20% = 1 枚のボーナス。
            Edit(world, 0, s => { s.Position = new Vector3(20f, GroundY, 0f); return s; });
            Step(world, 1);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(5 + 1), "ボーナスが入らない");
            // 3 枚 × 20% = 0.6 枚は、ためておく。
            Edit(world, 0, s => { s.Position = new Vector3(-20f, GroundY, 0f); return s; });
            Step(world, 1);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(9));
            Assert.That(world.GetState(0).Combat.CoinBonusMilli, Is.EqualTo(600));
            Assert.That(Imbalance(world), Is.EqualTo(imbalance), "ボーナスで総量が合わない（生成に数えていない）");

            // 効率が切れたら、もう足さない。
            Step(world, Risk.Skills.CoinBoost.DurationTicks);
            int before = world.GetState(0).Coins.Held;
            Edit(world, 0, s => { s.Position = new Vector3(20f, GroundY, 0f); return s; });
            Step(world, 1);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(before + 5));
        }

        [Test]
        public void RouletteSmash_ScalesTheKnockback_AndCountsAJackpot()
        {
            int seed = Enumerable.Range(1, 500).First(s => HitResolver.RouletteRoll(s, 0, 1) == AttackData.RouletteScales.Length - 1);
            var rules = new MatchRules { CountdownTicks = 0, DurationTicks = 0, Seed = seed };
            var world = new SimWorld(new BoxTerrain(), null, null, rules);
            world.AddCharacter(CharacterPresets.RiskTaker(), new Vector3(0f, 50f, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(1f, 48f, 0f));

            HitRecord? hit = null;
            Step(world, 1, Press(InputButtons.Attack, 0f, -1f));
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.AirDown), "前提: 撃破技");
            for (int t = 0; t < 40 && hit == null; t++)
            {
                Edit(world, 1, s => { s.Position = world.GetState(0).Position + new Vector3(1f, -2f, 0f); s.Velocity = Vector3.Zero; return s; });
                Step(world, 1);
                if (world.LastHits.Count > 0)
                {
                    hit = world.LastHits[0];
                }
            }
            Assert.That(hit.HasValue, Is.True, "撃破技が当たらない");
            // 速さの倍率の代わりに、ルーレットの目がそのまま倍率になる（ユーザーの要望、2026-10-16）。
            float expected = AttackData.RouletteScales[AttackData.RouletteScales.Length - 1];
            Assert.That(hit.Value.KnockbackScale, Is.EqualTo(expected), "ルーレットの目がそのまま倍率になっていない");
            Assert.That(hit.Value.Damage, Is.EqualTo(AttackPower.Damage(Risk.Combat.AirDown.Hits[0].Damage, expected, 0)));
            Assert.That(hit.Value.Fatal, Is.False, "ルーレットの技が速さで FATAL になった");
            Assert.That(hit.Value.Jackpot, Is.True);
            Assert.That(world.GetState(1).Combat.JackpotHitCount, Is.EqualTo(1), "JACKPOT を数えない");
        }

        [Test]
        public void CoinToss_HitsTheEnemyAbove_WithoutStoppingTheThrower_AndNeverTheThrower()
        {
            var world = new SimWorld(new BoxTerrain());
            world.AddCharacter(CharacterPresets.RiskTaker(), new Vector3(0f, 50f, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(0.3f, 53f, 0f));

            Step(world, 1, Press(InputButtons.Attack, 0f, 1f));
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.AirUp), "前提: 空中・上");
            bool hit = false;
            for (int t = 0; t < 40 && !hit; t++)
            {
                Edit(world, 1, s => { s.Position = new Vector3(0.3f, world.GetState(0).Position.Y + 3f, 0f); s.Velocity = Vector3.Zero; return s; });
                Step(world, 1);
                hit = world.LastHits.Any(h => h.FromObject && h.Victim == 1);
                Assert.That(world.LastHits.Any(h => h.Victim == 0), Is.False, "投げた本人に当たった");
            }
            Assert.That(hit, Is.True, "投げたコインが当たらない");
            Assert.That(world.GetState(1).Action.Kind, Is.EqualTo(ActionKind.Hitstun));
            Assert.That(world.GetState(0).Combat.HitstopTicksLeft, Is.Zero, "投げた人が止まった");
            Assert.That(world.Objects.Count, Is.Zero, "当たったのに消えない");
        }

        [Test]
        public void CoinToss_VanishesOnTheGround()
        {
            CharacterConfig config = CharacterPresets.RiskTaker();
            config.Skills.Projectile.VelocityY = -10f;
            config.Skills.Projectile.LifetimeTicks = 200;
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(config, new Vector3(0f, 6f, 0f));
            Step(world, 1, Press(InputButtons.Attack, 0f, 1f));
            Step(world, config.Combat.AirUp.EffectTick + 1);
            Assert.That(world.Objects.Count, Is.EqualTo(1), "前提: 投げた");
            Step(world, 60);
            Assert.That(world.Objects.Count, Is.Zero, "地面に当たっても消えない");
        }

        [Test]
        public void AllIn_LaunchesOnlyOnAHit()
        {
            CharacterConfig config = CharacterPresets.RiskTaker();
            var whiff = new SimWorld(TestArenas.Ground());
            whiff.AddCharacter(config, new Vector3(0f, GroundY, 0f));
            Step(whiff, 1);
            Step(whiff, 1, Press(InputButtons.Attack));
            float whiffRise = 0f;
            for (int t = 0; t < 50; t++)
            {
                Step(whiff, 1);
                whiffRise = System.Math.Max(whiffRise, whiff.GetState(0).Velocity.Y);
            }
            Assert.That(whiffRise, Is.LessThanOrEqualTo(0f), "外したのに飛び出した");

            // 当たると（ヒットストップの後、踏み込みが終わったところで）斜め上へ飛び出す。
            var hitWorld = new SimWorld(TestArenas.Ground());
            hitWorld.AddCharacter(config, new Vector3(0f, GroundY, 0f));
            hitWorld.AddCharacter(new CharacterConfig(), new Vector3(2f, GroundY, 0f), -1);
            Step(hitWorld, 1);
            Step(hitWorld, 1, Press(InputButtons.Attack));
            float hitRise = 0f;
            for (int t = 0; t < 50; t++)
            {
                Step(hitWorld, 1);
                hitRise = System.Math.Max(hitRise, hitWorld.GetState(0).Velocity.Y);
            }
            Assert.That(hitWorld.GetState(1).Combat.HasLastAttacker, Is.True, "前提: 当たった");
            Assert.That(hitRise, Is.GreaterThan(5f), "当たったのに飛び出さない");
        }

        [TestCase(0, 0)]
        [TestCase(30, 20)]
        [TestCase(50, 50)]
        [TestCase(100, 100)]
        [TestCase(130, 200)]
        [TestCase(400, 500)]
        public void AllIn_HoldingRaisesThePower_AndTheTopLevelIsAJackpot(int holdTicks, int bonus)
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.RiskTaker(), new Vector3(0f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(2f, GroundY, 0f), -1);
            Step(world, 1);
            Step(world, 1 + holdTicks, Press(InputButtons.Attack));
            Assert.That(world.GetState(0).Combat.Holding, Is.True, "前提: ためている");
            Assert.That(world.GetState(0).Combat.HoldTicks, Is.EqualTo(System.Math.Min(holdTicks, Risk.Combat.Ground.HoldMaxTicks)));

            HitRecord? hit = null;
            for (int t = 0; t < 30 && hit == null; t++)
            {
                Step(world, 1);
                if (world.LastHits.Count > 0)
                {
                    hit = world.LastHits[0];
                }
            }
            Assert.That(hit.HasValue, Is.True, "離しても当たらない");
            Assert.That(hit.Value.Attack, Is.EqualTo(AttackSlot.Ground));
            Assert.That(hit.Value.BonusPercent, Is.EqualTo(bonus), "ためた長さの上乗せでない");
            Assert.That(hit.Value.Damage, Is.EqualTo(AttackPower.Damage(Risk.Combat.Ground.Hits[0].Damage, 1f, bonus)));
            Assert.That(hit.Value.KnockbackScale, Is.EqualTo(1f), "ためても吹き飛びは変えない");
            bool top = bonus == 500;
            Assert.That(hit.Value.Jackpot, Is.EqualTo(top), "虹（最後の段階）だけが JACKPOT");
            Assert.That(world.GetState(1).Combat.JackpotHitCount, Is.EqualTo(top ? 1 : 0));
        }

        [Test]
        public void AllIn_HoldingKeepsTheCharacterInPlace_ButItCanTurn()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.RiskTaker(), new Vector3(0f, GroundY, 0f));
            Step(world, 1);
            Step(world, 1, Press(InputButtons.Attack));
            Step(world, 40, Press(InputButtons.Attack, -1f));
            Step(world, 5, Press(InputButtons.Attack | InputButtons.Jump, -1f));
            CharacterState holding = world.GetState(0);
            Assert.That(holding.Combat.Holding, Is.True, "前提: ためている");
            Assert.That(holding.Position.X, Is.EqualTo(0f).Within(1e-3f), "ためている間に動いた");
            Assert.That(holding.Grounded, Is.True, "ためている間にジャンプした");
            Assert.That(holding.Facing, Is.EqualTo(-1), "ためている間に向きを変えられない");

            Step(world, 1);
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Ground), "離しても出ない");
            Assert.That(world.GetState(0).Facing, Is.EqualTo(-1), "離したときの向きで出ない");
            Assert.That(world.GetState(0).Action.Charge, Is.EqualTo(45));
        }

        [Test]
        public void CoinToss_PowerGrowsWithTheHeldCoins_UpTo300()
        {
            ProjectileTuning shot = Risk.Skills.Projectile;
            Assert.That(shot.HeldCoinBonusPercent(2), Is.EqualTo(0));
            Assert.That(shot.HeldCoinBonusPercent(3), Is.EqualTo(1), "3 枚で 1%");
            Assert.That(shot.HeldCoinBonusPercent(150), Is.EqualTo(50));
            Assert.That(shot.HeldCoinBonusPercent(300), Is.EqualTo(100), "300 枚で +100%");
            Assert.That(shot.HeldCoinBonusPercent(900), Is.EqualTo(100), "+100% より上がった");
            Assert.That(new ProjectileTuning().HeldCoinBonusPercent(300), Is.Zero, "ほかの投射物まで上がる");

            var world = new SimWorld(new BoxTerrain());
            world.AddCharacter(CharacterPresets.RiskTaker(), new Vector3(0f, 50f, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(0.3f, 53f, 0f));
            Edit(world, 0, s => { s.Coins.Held = 150; return s; });
            Step(world, 1, Press(InputButtons.Attack, 0f, 1f));
            HitRecord? hit = null;
            for (int t = 0; t < 40 && hit == null; t++)
            {
                Edit(world, 1, s => { s.Position = new Vector3(0.3f, world.GetState(0).Position.Y + 3f, 0f); s.Velocity = Vector3.Zero; return s; });
                Step(world, 1);
                if (world.LastHits.Count > 0)
                {
                    hit = world.LastHits[0];
                }
            }
            Assert.That(hit.HasValue, Is.True, "前提: 当たった");
            Assert.That(hit.Value.BonusPercent, Is.EqualTo(50), "手持ちのコインで上がらない");
            Assert.That(hit.Value.Damage, Is.EqualTo(AttackPower.Damage(shot.Hit.Damage, 1f, 50)));
        }

        [Test]
        public void Safe_HittingItStopsTheAttackerLikeAHit_AndAFullSafeStopsCharging()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.RiskTaker(), new Vector3(0f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(-6f, GroundY, 0f));
            Step(world, 1);
            Step(world, 10, Press(InputButtons.Special2));
            Step(world, 1);
            Assert.That(world.Objects.Count, Is.EqualTo(1), "前提: 置いた");
            WorldObject safe = world.Objects[0];

            Edit(world, 1, s =>
            {
                s.Position = new Vector3(safe.Position.X, safe.Position.Y + 1.5f, 0f);
                s.Velocity = Vector3.Zero;
                s.Grounded = false;
                return s;
            });
            Step(world, 1, default, Press(InputButtons.Attack));
            for (int t = 0; t < 20 && world.Objects[0].Damage == 0; t++)
            {
                Step(world, 1);
            }
            Assert.That(world.Objects[0].Damage, Is.GreaterThan(0), "前提: 当たった");
            Assert.That(world.GetState(1).Combat.HitstopTicksLeft, Is.EqualTo(new CharacterConfig().Combat.AirNeutral.Hits[0].HitstopTicks),
                "金庫に当てても止まらない");

            // 上限に着いた金庫は、もうチャージしない。
            SafeTuning tuning = Risk.Skills.Safe;
            var objects = new WorldObjects();
            objects.Add(new WorldObject { Kind = WorldObjectKind.Safe, ChargeMilli = tuning.MaxCoins * 1000L, ExpireTick = int.MaxValue });
            Assert.That(SkillRules.IsSafeFull(objects[0], tuning), Is.True);
            SkillRules.StepObjects(objects, new BoxTerrain(), new List<CharacterState> { CharacterState.Spawn(Vector3.Zero, 1) },
                new List<CharacterConfig> { CharacterPresets.RiskTaker() }, null, tuning.ChargeIntervalTicks, new List<HitRecord>());
            Assert.That(objects[0].ChargeMilli, Is.EqualTo(tuning.MaxCoins * 1000L));
            Assert.That(SkillRules.SafeChargeProgress(objects[0], tuning, tuning.ChargeIntervalTicks / 2), Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Fever_AlsoBoostsTheGauge_AndKeepsTheFraction()
        {
            CoinBoostTuning boost = Risk.Skills.CoinBoost;
            var combat = new CombatState { CoinBoostTicksLeft = 10 };
            CombatTuning.AddGauge(ref combat, 80, boost);
            Assert.That(combat.Gauge, Is.EqualTo(96), "8% が 9.6% にならない");
            CombatTuning.AddGauge(ref combat, 2, boost);
            Assert.That(combat.Gauge, Is.EqualTo(98));
            Assert.That(combat.GaugeBonusRemainder, Is.EqualTo(40), "端数をためない");
            CombatTuning.AddGauge(ref combat, 4, boost);
            Assert.That(combat.Gauge, Is.EqualTo(103), "たまった端数を足さない");
            Assert.That(combat.GaugeBonusRemainder, Is.EqualTo(20));

            combat.CoinBoostTicksLeft = 0;
            CombatTuning.AddGauge(ref combat, 80, boost);
            Assert.That(combat.Gauge, Is.EqualTo(183), "切れた後も増える");
        }

        [Test]
        public void Snapshot_RestoresTheObjects()
        {
            var world = new SimWorld(new BoxTerrain());
            world.AddCharacter(CharacterPresets.RiskTaker(), new Vector3(0f, 50f, 0f));
            Step(world, 1, Press(InputButtons.Special1));
            Step(world, 10);
            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);
            ulong checksum = world.Checksum();
            Assert.That(world.Objects.Count, Is.EqualTo(1), "前提: 置いた");

            Step(world, Risk.Skills.MirrorBall.DurationTicks);
            Assert.That(world.Objects.Count, Is.Zero, "前提: 消えた");
            world.LoadFrom(snapshot);
            Assert.That(world.Objects.Count, Is.EqualTo(1), "巻き戻しで物が戻らない");
            Assert.That(world.Checksum(), Is.EqualTo(checksum));
            Assert.That(snapshot.Checksum(), Is.EqualTo(checksum));
        }

        [Test]
        public void Rollback_ReproducesTheSkillsAndTheObjects()
        {
            // 2 人の RISK TAKER がミラーボール・金庫（長押し）・フィーバー・コイントス・攻撃を出し合う入力で、遅延と欠落のある疑似ネット
            // ワークで巻き戻しても、全員が本当の入力どおりの状態になる。
            var scripts = new PlayerInput[2][];
            for (int p = 0; p < 2; p++)
            {
                float toward = p == 0 ? 1f : -1f;
                scripts[p] = new PlayerInput[900];
                for (int t = 0; t < scripts[p].Length; t++)
                {
                    int phase = (t + p * 13) % 120;
                    InputButtons buttons = phase == 3 ? InputButtons.Special1
                        : phase >= 20 && phase < 50 ? InputButtons.Special2
                        : phase == 60 ? InputButtons.Super
                        : phase == 75 || phase == 95 ? InputButtons.Attack
                        : phase == 85 ? InputButtons.Jump
                        : InputButtons.None;
                    float y = phase == 75 ? 1f : 0f;
                    scripts[p][t] = PlayerInput.FromAxes(phase < 70 ? toward * 0.5f : 0f, y, buttons);
                }
            }
            void Setup(SimWorld world)
            {
                for (int i = 0; i < world.CharacterCount; i++)
                {
                    world.SetConfig(i, CharacterPresets.RiskTaker());
                    CharacterState state = world.GetState(i);
                    state.Coins.Held = 200;
                    state.Combat.Gauge = CombatTuning.MaxGauge;
                    world.SetState(i, state);
                }
            }
            var network = new PseudoNetwork(scripts, new RollbackSettings(), 5, 2, 0.05, 23, Setup);

            network.RunUntil(700, maxFrames: 4000);

            SimWorld reference = network.Reference(700);
            Assert.That(network.Worlds[0].Checksum(), Is.EqualTo(reference.Checksum()));
            Assert.That(network.Worlds[1].Checksum(), Is.EqualTo(reference.Checksum()));
            Assert.That(network.Sessions[0].RollbackCount, Is.GreaterThan(0), "前提: 巻き戻している");
            Assert.That(network.Reference(130).Objects.Count, Is.GreaterThan(0), "前提: 物を置いている");
        }
    }
}
