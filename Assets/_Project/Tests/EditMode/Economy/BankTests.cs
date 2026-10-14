using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Economy
{
    /// <summary>拠点（バンク。docs/12_match_plan.md T4-7）: 場所と移動、予告、所持コインの確定。</summary>
    public class BankTests
    {
        private const float GroundY = 0.91f;
        private static readonly BankSpot Home = new BankSpot(new Vector2(0f, 1.5f), isFirst: true);
        private static readonly BankSpot[] FourSpots =
        {
            new BankSpot(new Vector2(20f, 8f)),
            new BankSpot(new Vector2(-20f, 8f)),
            Home,
            new BankSpot(new Vector2(0f, 20f)),
        };

        private static MatchRules Rules(int countdown = 0, int duration = 0, int interval = 60, int notice = 20, int seed = 7)
        {
            return new MatchRules
            {
                CountdownTicks = countdown,
                DurationTicks = duration,
                BankMoveIntervalTicks = interval,
                BankNoticeTicks = notice,
                Seed = seed,
            };
        }

        private static SimWorld World(MatchRules rules, BankSpot[] banks, params CoinSpawnPoint[] coins)
        {
            return new SimWorld(TestArenas.Ground(), null, null, rules, coins, banks);
        }

        private static void Step(SimWorld world, int ticks)
        {
            var inputs = new PlayerInput[world.CharacterCount];
            for (int i = 0; i < ticks; i++)
            {
                world.Step(inputs);
            }
        }

        private static void GiveCoins(SimWorld world, int character, int held)
        {
            CharacterState state = world.GetState(character);
            state.Coins.Held = held;
            world.SetState(character, state);
        }

        [Test]
        public void TheBank_StartsAtTheFirstSpot_AndMovesToADifferentSpotEveryInterval()
        {
            MatchRules rules = Rules(countdown: 30, interval: 60);
            Assert.That(BankRules.SpotAt(rules, 4, 0), Is.EqualTo(0), "カウントダウン中は最初の地点");
            Assert.That(BankRules.SpotAt(rules, 4, 30 + 59), Is.EqualTo(0), "間隔より前に移った");

            int previous = 0;
            for (int move = 1; move <= 40; move++)
            {
                int spot = BankRules.SpotAt(rules, 4, 30 + move * 60);
                Assert.That(spot, Is.InRange(0, 3));
                Assert.That(spot, Is.Not.EqualTo(previous), $"{move} 回目に同じ地点へ移った");
                Assert.That(BankRules.SpotAt(rules, 4, 30 + move * 60 + 59), Is.EqualTo(spot), "間隔の途中で移った");
                previous = spot;
            }
            Assert.That(BankRules.SpotAt(rules, 1, 30 + 600), Is.EqualTo(0), "候補が 1 つなら動かない");
            Assert.That(BankRules.SpotAt(rules, 0, 0), Is.EqualTo(-1), "候補がないのに場所がある");
        }

        [Test]
        public void TheMoves_DependOnTheSeed()
        {
            var a = new List<int>();
            var b = new List<int>();
            for (int move = 1; move <= 12; move++)
            {
                a.Add(BankRules.SpotAfterMoves(Rules(seed: 1), 4, move));
                b.Add(BankRules.SpotAfterMoves(Rules(seed: 2), 4, move));
            }
            Assert.That(a, Is.Not.EqualTo(b), "種を変えても移り方が同じ");
            Assert.That(BankRules.SpotAfterMoves(Rules(seed: 1), 4, 12), Is.EqualTo(a[11]), "同じ種で違う移り方になった");
        }

        [Test]
        public void TheNextSpot_IsAnnounced_BeforeTheMove()
        {
            MatchRules rules = Rules(countdown: 30, interval: 60, notice: 20);
            int next = BankRules.NextSpotAt(rules, 4, 30 + 39, out int until);
            Assert.That(until, Is.EqualTo(21));
            Assert.That(BankRules.IsNoticeAt(rules, 4, 30 + 39), Is.False, "早すぎる予告");
            Assert.That(BankRules.IsNoticeAt(rules, 4, 30 + 40), Is.True, "予告がない");
            Assert.That(next, Is.EqualTo(BankRules.SpotAt(rules, 4, 30 + 60)), "予告した地点に移らない");

            BankRules.NextSpotAt(rules, 4, 0, out int duringCountdown);
            Assert.That(duringCountdown, Is.EqualTo(90), "カウントダウン中の移るまでの時間");
            Assert.That(BankRules.IsNoticeAt(rules, 1, 30 + 50), Is.False, "候補が 1 つなのに予告した");
        }

        [Test]
        public void TouchingTheBank_BanksAllHeldCoins()
        {
            SimWorld world = World(Rules(), FourSpots);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            Step(world, 1);
            GiveCoins(world, 0, 7);

            Step(world, 1);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(0), "拠点に触れても確定しない");
            Assert.That(world.GetState(0).Coins.Banked, Is.EqualTo(7));
            Assert.That(world.Coins.Events[world.Coins.Events.Count - 1].Id.Kind, Is.EqualTo(CoinEventKind.Bank));
        }

        [Test]
        public void CoinsPickedUpInsideTheBank_AreBankedAtOnce()
        {
            var coin = new CoinSpawnPoint(new Vector2(0.5f, 1f), 3);
            SimWorld world = World(Rules(), FourSpots, coin);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));

            Step(world, 1);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(0), "拾ったコインがすぐに確定しない");
            Assert.That(world.GetState(0).Coins.Banked, Is.EqualTo(3));
            var states = new List<CharacterState> { world.GetState(0) };
            Assert.That(world.Coins.Totals(states).IsConserved, Is.True);
        }

        [Test]
        public void AwayFromTheBank_NothingIsBanked()
        {
            SimWorld world = World(Rules(), FourSpots);
            world.AddCharacter(new CharacterConfig(), new Vector3(5f, GroundY, 0f));
            Step(world, 1);
            GiveCoins(world, 0, 7);

            Step(world, 30);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(7), "拠点の外で確定した");
            Assert.That(world.GetState(0).Coins.Banked, Is.EqualTo(0));
        }

        [Test]
        public void AnEnemyInsideTheBank_DoesNotStopBanking()
        {
            SimWorld world = World(Rules(), FourSpots);
            world.AddCharacter(new CharacterConfig(), new Vector3(-0.8f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(0.8f, GroundY, 0f));
            Step(world, 1);
            GiveCoins(world, 0, 4);
            GiveCoins(world, 1, 9);

            Step(world, 1);
            Assert.That(world.GetState(0).Coins.Banked, Is.EqualTo(4), "敵がいると確定しない");
            Assert.That(world.GetState(1).Coins.Banked, Is.EqualTo(9), "敵がいると確定しない");
        }

        [Test]
        public void ACharacterWaitingToRespawn_DoesNotBank()
        {
            SimWorld world = World(Rules(), FourSpots);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            Step(world, 1);
            CharacterState state = world.GetState(0);
            state.Coins.Held = 5;
            state.Combat.RespawnTicksLeft = 30;
            world.SetState(0, state);

            Step(world, 10);
            Assert.That(world.GetState(0).Coins.Banked, Is.EqualTo(0), "復帰を待っている間に確定した");
        }

        [Test]
        public void NothingIsBanked_DuringTheCountdown_OrAfterTheEnd()
        {
            SimWorld world = World(Rules(countdown: 20, duration: 40), FourSpots);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            GiveCoins(world, 0, 5);
            Step(world, 20);
            Assert.That(world.GetState(0).Coins.Banked, Is.EqualTo(0), "カウントダウン中に確定した");

            Step(world, 1);
            Assert.That(world.GetState(0).Coins.Banked, Is.EqualTo(5), "始まっても確定しない");

            Step(world, 39);
            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Finished), "前提: 終わった");
            GiveCoins(world, 0, 6);
            Step(world, 10);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(6), "終わった後に確定した");
        }

        [Test]
        public void AfterTheBankMoves_TheOldSpotNoLongerBanks()
        {
            SimWorld world = World(Rules(interval: 60), FourSpots);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            Step(world, 60);
            Assert.That(BankRules.SpotAt(world.Rules, world.BankSpots.Count, world.Tick), Is.Not.EqualTo(0), "前提: 移った");
            GiveCoins(world, 0, 5);

            Step(world, 30);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(5), "移った後の元の地点で確定した");
        }

        [Test]
        public void BankSpotsGivenInADifferentOrder_GiveTheSameResult()
        {
            var reversed = new BankSpot[FourSpots.Length];
            for (int i = 0; i < FourSpots.Length; i++)
            {
                reversed[i] = FourSpots[FourSpots.Length - 1 - i];
            }
            SimWorld a = World(Rules(interval: 30), FourSpots);
            SimWorld b = World(Rules(interval: 30), reversed);
            Assert.That(a.BankSpots[0], Is.EqualTo(Home), "最初の拠点が先頭にない");
            Assert.That(b.BankSpots, Is.EqualTo(a.BankSpots), "渡した順で並びが変わった");
        }

        [Test]
        public void Touches_UsesTheClosestPointOfTheBox()
        {
            var half = new Vector3(0.5f, 1f, 0.5f);
            Assert.That(BankRules.Touches(Vector3.Zero, half, new Vector2(2.4f, 0f), 2f), Is.True, "横から触れている");
            Assert.That(BankRules.Touches(Vector3.Zero, half, new Vector2(2.6f, 0f), 2f), Is.False);
            // 角: 箱の角 (0.5, 1) から (1.9, 2.4) までは約 1.98m。
            Assert.That(BankRules.Touches(Vector3.Zero, half, new Vector2(1.9f, 2.4f), 2f), Is.True, "角で触れている");
            Assert.That(BankRules.Touches(Vector3.Zero, half, new Vector2(2f, 2.5f), 2f), Is.False, "角から離れている");
        }

        [Test]
        public void RollingBack_RestoresTheBankedCoins()
        {
            SimWorld world = World(Rules(), FourSpots, new CoinSpawnPoint(new Vector2(0.5f, 1f), 3));
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);
            Step(world, 5);
            ulong expected = world.Checksum();

            world.LoadFrom(snapshot);
            Assert.That(world.GetState(0).Coins.Banked, Is.EqualTo(0));
            Step(world, 5);
            Assert.That(world.Checksum(), Is.EqualTo(expected));
            Assert.That(world.GetState(0).Coins.Banked, Is.EqualTo(3));
        }

        [Test]
        public void InvalidRules_AreRejected()
        {
            Assert.Throws<InvalidTuningException>(() => World(new MatchRules { BankMoveIntervalTicks = 0 }, FourSpots));
            Assert.Throws<InvalidTuningException>(() => World(new MatchRules { BankRadius = 0f }, FourSpots));
            Assert.Throws<System.ArgumentException>(() => new BankSpot(new Vector2(float.NaN, 0f)));
        }
    }
}
