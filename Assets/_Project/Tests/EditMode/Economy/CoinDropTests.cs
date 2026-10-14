using System.Collections.Generic;
using System.Numerics;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Economy;
using Degunatsu.Core.Input;
using Degunatsu.Core.Match;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Tests.Support;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Economy
{
    /// <summary>コインのドロップ（docs/12_match_plan.md T4-6）: 被弾ドロップ、撃破ドロップ、分け方、拾える時刻と寿命。</summary>
    public class CoinDropTests
    {
        private const float GroundY = 0.91f;
        private static readonly PlayerInput Attack = new PlayerInput(0, 0, InputButtons.Attack);
        private static readonly PlayerInput Guard = new PlayerInput(0, 0, InputButtons.Guard);
        private static readonly StageBounds Bounds = new StageBounds(-20f, -10f, 20f, 30f);

        // 0 が 1（1.6m 先）に地上技を出す。1 は最初のティックに、足元のコイン（held 枚）を拾う（総量が保存されるように）。
        private static SimWorld AttackWorld(int held, MatchRules rules = null)
        {
            var coin = new CoinSpawnPoint(new Vector2(1.6f, 1f), held);
            var world = new SimWorld(TestArenas.Ground(), Bounds, null, rules ?? MatchRules.FreePlay(), new[] { coin });
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(1.6f, GroundY, 0f), -1);
            world.Step(new PlayerInput[2]);
            Assert.That(world.GetState(1).Coins.Held, Is.EqualTo(held), "前提: 足元のコインを拾った");
            return world;
        }

        // 攻撃を出して、当たるまで進める。当たったティックを返す。
        private static int StepUntilHit(SimWorld world, PlayerInput victimInput = default)
        {
            world.Step(new[] { Attack, victimInput });
            for (int i = 0; i < 40; i++)
            {
                if (world.LastHits.Count > 0)
                {
                    return world.Tick;
                }
                world.Step(new[] { default, victimInput });
            }
            Assert.Fail("前提: 攻撃が当たらない");
            return -1;
        }

        private static List<FieldCoin> Drops(SimWorld world)
        {
            var drops = new List<FieldCoin>();
            foreach (FieldCoin coin in world.Coins.Field.Coins)
            {
                if (coin.Source < 0)
                {
                    drops.Add(coin);
                }
            }
            return drops;
        }

        private static int Total(List<FieldCoin> coins)
        {
            int total = 0;
            foreach (FieldCoin coin in coins)
            {
                total += coin.Value;
            }
            return total;
        }

        private static void AssertConserved(SimWorld world)
        {
            var states = new List<CharacterState>();
            for (int i = 0; i < world.CharacterCount; i++)
            {
                states.Add(world.GetState(i));
            }
            CoinTotals totals = world.Coins.Totals(states);
            Assert.That(totals.IsConserved, Is.True, totals.ToString());
        }

        [Test]
        public void TheFirstHitOfACombo_DropsTenPercent_AboveTheVictim()
        {
            SimWorld world = AttackWorld(50);
            int hitTick = StepUntilHit(world);

            Assert.That(world.GetState(1).Coins.Held, Is.EqualTo(45), "所持の 10% を落とさない");
            List<FieldCoin> drops = Drops(world);
            Assert.That(Total(drops), Is.EqualTo(5));
            Assert.That(drops.ConvertAll(c => c.Value), Is.EqualTo(new[] { 3, 1, 1 }), "20・3・1 枚に分けていない");
            Vector3 victim = world.GetState(1).Position;
            foreach (FieldCoin coin in drops)
            {
                Assert.That(coin.PickableFromTick, Is.EqualTo(hitTick + 60), "拾えるまでの時間");
                Assert.That(coin.ExpireTick, Is.EqualTo(hitTick + 600), "寿命");
                Assert.That(coin.Origin, Is.EqualTo(CoinEventKind.HitDrop));
                Assert.That(coin.Position.Y, Is.GreaterThan(GroundY), "当たった人の上に並んでいない");
                // 戦っている2人がすぐに拾わないように、少し遠くに置く（2.8m 以上。当たった人は吹き飛び始めているので少し余裕を見る）。
                Assert.That(Vector2.Distance(coin.Position, new Vector2(victim.X, victim.Y)), Is.GreaterThan(2f), "当たった人に近すぎる");
            }
            AssertConserved(world);
        }

        [Test]
        public void TheRestOfTheCombo_DoesNotDropMore()
        {
            SimWorld world = AttackWorld(50);
            StepUntilHit(world);
            int hits = 0;
            // 地上技の二段目まで、と、その後しばらく（拾えない時間が過ぎても、戦っていた2人の近くに落ちていない）。
            for (int i = 0; i < 100; i++)
            {
                world.Step(new PlayerInput[2]);
                hits += world.LastHits.Count;
            }
            Assert.That(hits, Is.GreaterThan(0), "前提: 地上技の二段目が当たった");
            Assert.That(world.GetState(1).Coins.Held, Is.EqualTo(45), "同じコンボで、もう一度落とした（か、戦っていた人が拾った）");
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(0), "攻撃した人が、すぐ近くに落ちたコインを拾った");
        }

        [Test]
        public void AGuardedHit_DropsNothing()
        {
            SimWorld world = AttackWorld(50);
            // 先にガードを構えておく（ジャストガードの受付が過ぎるまで）。
            for (int i = 0; i < 20; i++)
            {
                world.Step(new[] { default, Guard });
            }
            StepUntilHit(world, Guard);
            Assert.That(world.LastHits[0].Result, Is.Not.EqualTo(HitResult.Hit), "前提: ガードした");
            Assert.That(world.GetState(1).Coins.Held, Is.EqualTo(50), "ガードしたのに落とした");
            Assert.That(Drops(world), Is.Empty);
        }

        [Test]
        public void DroppedCoins_CannotBePickedUpAtOnce_AndExpire()
        {
            SimWorld world = AttackWorld(50);
            int hitTick = StepUntilHit(world);
            FieldCoin first = Drops(world)[0];

            // 落とした直後に、攻撃した人をコインに重ねる。
            CharacterState attacker = world.GetState(0);
            attacker.Position = new Vector3(first.Position.X, first.Position.Y, 0f);
            attacker.Velocity = Vector3.Zero;
            world.SetState(0, attacker);
            world.Step(new PlayerInput[2]);
            Assert.That(world.Coins.Field.IndexOf(first.Id), Is.GreaterThanOrEqualTo(0), "拾えない時間に拾った");

            while (world.Tick < hitTick + 600)
            {
                world.Step(new PlayerInput[2]);
            }
            Assert.That(Drops(world).TrueForAll(c => c.Id == first.Id || c.ExpireTick > world.Tick), Is.True);
            Assert.That(Drops(world).Exists(c => c.Id != first.Id), Is.False, "寿命が過ぎても消えない");
            AssertConserved(world);
        }

        [Test]
        public void BeingKnockedOut_DropsHalf_InAClusterAtACoinSpawnPoint()
        {
            // 幅 8m の浮いた足場から右へ歩いて落ちる（自滅。D-44 で撃破と同じドロップ）。
            var coin = new CoinSpawnPoint(new Vector2(0f, 1f), 9);
            var world = new SimWorld(new BoxTerrain().AddBox(-4f, -1f, 4f, 0f), Bounds, null, MatchRules.FreePlay(), new[] { coin });
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            world.Step(new PlayerInput[1]);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(9), "前提: コインを拾った");

            for (int i = 0; i < 400 && world.GetState(0).Combat.Falls == 0; i++)
            {
                world.Step(new[] { Inputs.Right });
            }
            Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(1), "前提: ステージの外に出た");
            Assert.That(world.LastKnockouts.Count, Is.EqualTo(1));
            Assert.That(world.LastKnockouts[0].Attacker, Is.EqualTo(-1), "自滅に手柄がついた");

            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(4), "所持の半分（切り上げ）を落とさない");
            List<FieldCoin> drops = Drops(world);
            Assert.That(Total(drops), Is.EqualTo(5));
            // 出現地点は1つなので、そこに固めて置く。
            Vector2 origin = CoinDropRules.KoDropOrigin(world.LastKnockouts[0], Bounds, world.CoinSpawns, world.Rules, world.Tick);
            Assert.That(origin, Is.EqualTo(coin.Position), "コインの出現地点に置いていない");
            foreach (FieldCoin dropped in drops)
            {
                Assert.That(dropped.Origin, Is.EqualTo(CoinEventKind.KoDrop));
                Assert.That(Vector2.Distance(dropped.Position, origin), Is.LessThanOrEqualTo(1.7f), "固まっていない");
                Assert.That(Bounds.Contains(new Vector3(dropped.Position.X, dropped.Position.Y, 0f)), Is.True, "ステージの外に落とした");
            }
            AssertConserved(world);
        }

        [Test]
        public void TheKnockoutDropSpot_IsPickedFromTheCoinSpawnPoints_ByTheSeed()
        {
            var spots = new[]
            {
                new CoinSpawnPoint(new Vector2(-10f, 5f), 1),
                new CoinSpawnPoint(new Vector2(0f, 12f), 3),
                new CoinSpawnPoint(new Vector2(10f, 5f), 20),
                new CoinSpawnPoint(new Vector2(0f, 25f), 100, CoinSpawnKind.Jackpot),
            };
            var ko = new KnockoutRecord(1, -1, new Vector2(0f, -30f));
            var chosen = new HashSet<Vector2>();
            for (int seed = 0; seed < 40; seed++)
            {
                var rules = new MatchRules { Seed = seed };
                Vector2 a = CoinDropRules.KoDropOrigin(ko, Bounds, spots, rules, 500);
                Assert.That(CoinDropRules.KoDropOrigin(ko, Bounds, spots, rules, 500), Is.EqualTo(a), "同じ種で場所が違う");
                Assert.That(System.Array.Exists(spots, s => s.Position == a), Is.True, "出現地点のどれでもない");
                chosen.Add(a);
            }
            Assert.That(chosen.Count, Is.GreaterThanOrEqualTo(3), "種を変えても場所が変わらない");

            // 出現地点がなければ、ステージの外に出た位置を範囲の内側に入れる。
            Vector2 fallback = CoinDropRules.KoDropOrigin(ko, Bounds, new CoinSpawnPoint[0], new MatchRules(), 500);
            Assert.That(fallback.Y, Is.EqualTo(Bounds.MinY + new MatchRules().KoDropInset));
        }

        [Test]
        public void DropAmounts_HaveAMinimumAndACap()
        {
            var rules = new MatchRules();
            Assert.That(CoinDropRules.HitDropAmount(rules, 0), Is.EqualTo(0));
            Assert.That(CoinDropRules.HitDropAmount(rules, 1), Is.EqualTo(1), "少なくとも 1 枚");
            Assert.That(CoinDropRules.HitDropAmount(rules, 9), Is.EqualTo(1));
            Assert.That(CoinDropRules.HitDropAmount(rules, 57), Is.EqualTo(5));
            Assert.That(CoinDropRules.HitDropAmount(rules, 1000), Is.EqualTo(10), "上限");
            Assert.That(CoinDropRules.KoDropAmount(rules, 0), Is.EqualTo(0));
            Assert.That(CoinDropRules.KoDropAmount(rules, 1), Is.EqualTo(1), "切り上げ");
            Assert.That(CoinDropRules.KoDropAmount(rules, 3), Is.EqualTo(2));
            Assert.That(CoinDropRules.KoDropAmount(rules, 50), Is.EqualTo(25));
        }


        [Test]
        public void DropsAreSplitIntoTwentyThreeAndOne_UpToTheMaximum()
        {
            var values = new List<int>();
            int remaining = 47;
            for (int piece = 0; remaining > 0; piece++)
            {
                int value = CoinDropRules.PieceValue(remaining, piece);
                values.Add(value);
                remaining -= value;
            }
            Assert.That(values, Is.EqualTo(new[] { 20, 20, 3, 3, 1 }));

            // 上限の数を超えるときは、最後の1つに残りをまとめる。
            values.Clear();
            remaining = 20 * 20;
            for (int piece = 0; remaining > 0; piece++)
            {
                int value = CoinDropRules.PieceValue(remaining, piece);
                values.Add(value);
                remaining -= value;
            }
            Assert.That(values.Count, Is.EqualTo(CoinDropRules.MaxPieces));
            Assert.That(values[values.Count - 1], Is.EqualTo(400 - 20 * (CoinDropRules.MaxPieces - 1)));
        }

        [Test]
        public void RollingBack_ThroughADrop_GivesTheSameResult()
        {
            SimWorld world = AttackWorld(50);
            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);
            StepUntilHit(world);
            for (int i = 0; i < 20; i++)
            {
                world.Step(new PlayerInput[2]);
            }
            ulong expected = world.Checksum();
            int tick = world.Tick;

            world.LoadFrom(snapshot);
            StepUntilHit(world);
            while (world.Tick < tick)
            {
                world.Step(new PlayerInput[2]);
            }
            Assert.That(world.Checksum(), Is.EqualTo(expected));
        }

        [Test]
        public void InvalidDropRules_AreRejected()
        {
            Assert.Throws<Degunatsu.Core.Common.InvalidTuningException>(() => new SimWorld(TestArenas.Ground(), null, null, new MatchRules { HitDropPercent = 150 }));
            Assert.Throws<Degunatsu.Core.Common.InvalidTuningException>(() => new SimWorld(TestArenas.Ground(), null, null, new MatchRules { DropLifetimeTicks = 0 }));
        }
    }
}
