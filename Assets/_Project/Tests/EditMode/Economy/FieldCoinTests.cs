using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Economy
{
    /// <summary>フィールドのコイン（docs/12_match_plan.md T4-4）: 出現、拾得、消滅。すべて台帳を通る。</summary>
    public class FieldCoinTests
    {
        private const float GroundY = 0.91f;
        private static readonly CoinSpawnPoint Near = new CoinSpawnPoint(new Vector2(0f, 1f), 5);
        private static readonly CoinSpawnPoint Far = new CoinSpawnPoint(new Vector2(20f, 8f), 10);

        private static MatchRules Rules(int countdown = 0, int interval = 120)
        {
            return new MatchRules { CountdownTicks = countdown, DurationTicks = 0, CoinSpawnIntervalTicks = interval };
        }

        private static SimWorld World(MatchRules rules, params CoinSpawnPoint[] spawns)
        {
            return new SimWorld(TestArenas.Ground(), null, null, rules, spawns);
        }

        private static void Step(SimWorld world, int ticks, PlayerInput input = default)
        {
            var inputs = new PlayerInput[world.CharacterCount];
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = input;
            }
            for (int i = 0; i < ticks; i++)
            {
                world.Step(inputs);
            }
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
        public void Coins_SpawnWhenPlayStarts_AtEveryPoint()
        {
            SimWorld world = World(Rules(countdown: 30), Far, new CoinSpawnPoint(new Vector2(-20f, 8f), 5));
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));

            Step(world, 30);
            Assert.That(world.Coins.Field.Coins, Is.Empty, "カウントダウン中に出た");
            Step(world, 1);
            Assert.That(world.Coins.Field.Coins.Count, Is.EqualTo(2), "始まったときに出ない");
            Assert.That(world.Coins.Field.FieldTotal, Is.EqualTo(15));
            AssertConserved(world);
        }

        [Test]
        public void APoint_DoesNotSpawnAgain_UntilItsCoinIsPickedUp()
        {
            SimWorld world = World(Rules(interval: 60), Far);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            Step(world, 200);

            Assert.That(world.Coins.Field.Coins.Count, Is.EqualTo(1), "同じ地点に2つ出た");
            Assert.That(world.Coins.Field.Generated, Is.EqualTo(10));
        }

        [Test]
        public void TouchingACoin_PicksItUp_AndThePointSpawnsAgainLater()
        {
            SimWorld world = World(Rules(interval: 60), Near);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));

            Step(world, 1);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(5), "触れているのに拾わない");
            Assert.That(world.Coins.Field.Coins, Is.Empty);
            Step(world, 59);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(5), "間隔の前に出た");
            Step(world, 1);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(10), "次の間隔でまた出て、拾う");
            AssertConserved(world);
        }

        [Test]
        public void PickingUpACoin_GivesGauge_ForEachCoinOfItsValue()
        {
            // 1 枚につき 0.2%、価値の高いコインはその枚数分（ユーザーの要望、2026-10-15）。5 枚のコインで 1.0%。
            SimWorld world = World(Rules(interval: 60), Near);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));

            Step(world, 1);
            var combat = new CombatTuning();
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(5), "前提: 拾った");
            Assert.That(combat.GaugePerCoin, Is.EqualTo(CombatTuning.GaugePerPercent / 5), "1 枚 0.2%");
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(5 * combat.GaugePerCoin));
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(CombatTuning.GaugePerPercent), "5 枚で 1.0%");

            // 最大を超えない。
            CharacterState state = world.GetState(0);
            state.Combat.Gauge = CombatTuning.MaxGauge - 1;
            world.SetState(0, state);
            Step(world, 60);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(10), "前提: また拾った");
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(CombatTuning.MaxGauge));
        }

        [Test]
        public void TheRespawnTime_CountsFromThePickup()
        {
            // 間隔ごとではなく、拾われてから数える（ユーザーの要望、2026-10-13）。
            SimWorld world = World(Rules(interval: 60), Far);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            Step(world, 30);
            CharacterState state = world.GetState(0);
            state.Position = new Vector3(Far.Position.X, Far.Position.Y, 0f);
            world.SetState(0, state);
            Step(world, 1);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(10), "前提: ティック 31 で拾った");
            state = world.GetState(0);
            state.Position = new Vector3(0f, GroundY, 0f);
            world.SetState(0, state);

            Step(world, 59);
            Assert.That(world.Coins.Field.Coins, Is.Empty, "拾われてから 60 ティックたつ前に出た");
            Step(world, 1);
            Assert.That(world.Coins.Field.Coins.Count, Is.EqualTo(1), "拾われてから 60 ティックで出ない");
        }

        private static readonly CoinSpawnPoint[] JackpotSpots =
        {
            new CoinSpawnPoint(new Vector2(-30f, 10f), 100, CoinSpawnKind.Jackpot),
            new CoinSpawnPoint(new Vector2(30f, 10f), 100, CoinSpawnKind.Jackpot),
            new CoinSpawnPoint(new Vector2(0f, 30f), 100, CoinSpawnKind.Jackpot),
        };

        private static int CountJackpots(SimWorld world)
        {
            int count = 0;
            foreach (FieldCoin coin in world.Coins.Field.Coins)
            {
                count += coin.Value == 100 ? 1 : 0;
            }
            return count;
        }

        [Test]
        public void TheJackpot_IsAnnouncedWithOneMinuteLeft_AndAppearsAfterTheNotice_Once()
        {
            var rules = new MatchRules { CountdownTicks = 0, DurationTicks = 600, JackpotTicksBeforeEnd = 120, JackpotNoticeTicks = 30 };
            var points = new List<CoinSpawnPoint>(JackpotSpots) { Far };
            SimWorld world = World(rules, points.ToArray());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            Assert.That(FieldCoinRules.JackpotAnnounceTick(rules), Is.EqualTo(480), "予告は残り 120 ティック");
            Assert.That(FieldCoinRules.JackpotSpawnTick(rules), Is.EqualTo(510), "出るのは予告の 30 ティック後");
            int spot = FieldCoinRules.JackpotSpot(rules, world.CoinSpawns);
            Assert.That(spot, Is.GreaterThanOrEqualTo(0));
            Assert.That(world.CoinSpawns[spot].Kind, Is.EqualTo(CoinSpawnKind.Jackpot));

            Step(world, 510);
            Assert.That(CountJackpots(world), Is.EqualTo(0), "予告の間に出た（候補の地点がふつうに出た）");
            Step(world, 1);
            Assert.That(CountJackpots(world), Is.EqualTo(1), "予告の後に出ない");
            FieldCoin jackpot = default;
            foreach (FieldCoin coin in world.Coins.Field.Coins)
            {
                if (coin.Value == 100)
                {
                    jackpot = coin;
                }
            }
            Assert.That(jackpot.Position, Is.EqualTo(world.CoinSpawns[spot].Position), "予告した地点と違う");

            // 拾っても、もう出ない。
            CharacterState state = world.GetState(0);
            state.Position = new Vector3(jackpot.Position.X, jackpot.Position.Y, 0f);
            world.SetState(0, state);
            Step(world, 1);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(100));
            Step(world, 80);
            Assert.That(CountJackpots(world), Is.EqualTo(0), "100 枚のコインが2回出た");
        }

        [Test]
        public void TheJackpotSpot_DependsOnTheSeed()
        {
            var spots = new HashSet<Vector2>();
            for (int seed = 0; seed < 30; seed++)
            {
                var rules = new MatchRules { CountdownTicks = 0, DurationTicks = 20, JackpotTicksBeforeEnd = 10, JackpotNoticeTicks = 0, Seed = seed };
                SimWorld world = World(rules, JackpotSpots);
                world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
                Step(world, 11);
                Assert.That(world.Coins.Field.Coins.Count, Is.EqualTo(1), $"種 {seed} で出ない");
                spots.Add(world.Coins.Field.Coins[0].Position);

                SimWorld again = World(rules, JackpotSpots);
                again.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
                Step(again, 11);
                Assert.That(again.Coins.Field.Coins[0].Position, Is.EqualTo(world.Coins.Field.Coins[0].Position), "同じ種で場所が違う");
            }
            Assert.That(spots.Count, Is.GreaterThanOrEqualTo(2), "種を変えても、いつも同じ場所");
        }

        [Test]
        public void WithoutATimeLimit_TheJackpotNeverAppears()
        {
            SimWorld world = World(Rules(), JackpotSpots);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            Step(world, 1000);

            Assert.That(world.Coins.Field.Coins, Is.Empty);
            Assert.That(FieldCoinRules.JackpotAnnounceTick(world.Rules), Is.EqualTo(-1));
            Assert.That(FieldCoinRules.JackpotSpot(world.Rules, world.CoinSpawns), Is.EqualTo(-1));
        }

        [Test]
        public void TheClosestCharacter_PicksUpTheCoin()
        {
            SimWorld world = World(Rules(), new CoinSpawnPoint(new Vector2(0.3f, 1f), 5));
            world.AddCharacter(new CharacterConfig(), new Vector3(-0.5f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(0.4f, GroundY, 0f));
            Step(world, 1);

            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(0));
            Assert.That(world.GetState(1).Coins.Held, Is.EqualTo(5), "近い方が拾わない");
            Assert.That(world.Coins.RejectedCount, Is.EqualTo(0), "2人とも拾おうとした");
        }

        [Test]
        public void AtTheSameDistance_TheLowerNumberPicksItUp()
        {
            SimWorld world = World(Rules(), Near);
            world.AddCharacter(new CharacterConfig(), new Vector3(-0.3f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(0.3f, GroundY, 0f));
            Step(world, 1);

            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(5));
            Assert.That(world.GetState(1).Coins.Held, Is.EqualTo(0));
        }

        [Test]
        public void ACharacterWaitingToRespawn_DoesNotPickUpCoins()
        {
            SimWorld world = World(Rules(), Near);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            CharacterState state = world.GetState(0);
            state.Combat.RespawnTicksLeft = 30;
            world.SetState(0, state);
            Step(world, 1);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(0), "待っている間に拾った");

            Step(world, 30);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(5), "戻ったら拾う");
        }

        [Test]
        public void FarCoins_AreNotPickedUp()
        {
            SimWorld world = World(Rules(), Far);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            Step(world, 30);

            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(0));
            Assert.That(world.Coins.Field.Coins.Count, Is.EqualTo(1));
        }

        [Test]
        public void DroppedCoins_ExpireAtTheirTick_AndCannotBePickedUpEarly()
        {
            SimWorld world = World(Rules());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            Step(world, 1);
            var states = new List<CharacterState> { world.GetState(0) };
            world.Coins.BeginTick(world.Tick);
            world.Coins.Reward(new CoinEventId(world.Tick, CoinEventKind.DamageReward, 0), states, 0, 8);
            // 自分の足元に落とす（拾えるのは 20 ティック後、寿命は 10 ティック後）。
            world.Coins.Drop(new CoinEventId(world.Tick, CoinEventKind.HitDrop, 0), states, 0, 8, new Vector2(0f, 1f), world.Tick + 20, world.Tick + 10);
            world.SetState(0, states[0]);

            Step(world, 9);
            Assert.That(world.Coins.Field.Coins.Count, Is.EqualTo(1), "まだ拾えないのに拾った、または早く消えた");
            Step(world, 1);
            Assert.That(world.Coins.Field.Coins, Is.Empty, "寿命で消えない");
            Assert.That(world.Coins.Field.Destroyed, Is.EqualTo(8));
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(0));
            AssertConserved(world);
        }

        [Test]
        public void SpawnPointsGivenInADifferentOrder_GiveTheSameResult()
        {
            CoinSpawnPoint[] points = { Far, Near, new CoinSpawnPoint(new Vector2(-6f, 2f), 7) };
            var reversed = new[] { points[2], points[1], points[0] };
            SimWorld a = World(Rules(interval: 50), points);
            SimWorld b = World(Rules(interval: 50), reversed);
            a.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            b.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));

            for (int i = 0; i < 300; i++)
            {
                var input = PlayerInput.FromAxes(i % 100 < 50 ? -1f : 1f, 0f, i % 30 == 0 ? InputButtons.Jump : InputButtons.None);
                a.Step(new[] { input });
                b.Step(new[] { input });
                Assert.That(b.Checksum(), Is.EqualTo(a.Checksum()), $"ティック {a.Tick} で違う");
            }
            Assert.That(a.GetState(0).Coins.Held, Is.GreaterThan(0), "前提: 拾った");
        }

        [Test]
        public void RollingBack_RestoresTheCoins()
        {
            SimWorld world = World(Rules(interval: 40), Near, Far);
            world.AddCharacter(new CharacterConfig(), new Vector3(3f, GroundY, 0f));
            var snapshot = new WorldSnapshot();
            Step(world, 10);
            world.SaveTo(snapshot);
            Step(world, 100, Inputs.Left);
            ulong expected = world.Checksum();
            int held = world.GetState(0).Coins.Held;

            world.LoadFrom(snapshot);
            Step(world, 100, Inputs.Left);
            Assert.That(world.Checksum(), Is.EqualTo(expected));
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(held));
            Assert.That(held, Is.GreaterThan(0), "前提: 拾った");
        }

        [Test]
        public void InvalidSpawnPoints_AreRejected()
        {
            Assert.Throws<System.ArgumentException>(() => new CoinSpawnPoint(Vector2.Zero, 0));
            Assert.Throws<System.ArgumentException>(() => new CoinSpawnPoint(new Vector2(float.NaN, 0f), 5));
        }
    }
}
