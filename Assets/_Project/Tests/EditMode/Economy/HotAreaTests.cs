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
    /// <summary>
    /// ホットエリア（ユーザーの要望、2026-11-02。HotAreaRules）: 30 秒ごとに 100 枚のコインの候補の地点のどれかに、ゲージを回復する
    /// フルーツと 30 枚のコインを出す。100 枚のコインは残り 1 分 10 秒に予告して残り 1 分に出し、その時刻のホットエリアは別の地点。
    /// </summary>
    public class HotAreaTests
    {
        private const float GroundY = 0.91f;

        // 地面から離れた高い所の候補の地点（コインの輪が地形に埋まらない）。
        private static readonly CoinSpawnPoint[] Spots =
        {
            new CoinSpawnPoint(new Vector2(-30f, 12f), 100, CoinSpawnKind.Jackpot),
            new CoinSpawnPoint(new Vector2(30f, 12f), 100, CoinSpawnKind.Jackpot),
            new CoinSpawnPoint(new Vector2(0f, 30f), 100, CoinSpawnKind.Jackpot),
            new CoinSpawnPoint(new Vector2(0f, 12f), 100, CoinSpawnKind.Jackpot),
        };

        private static MatchRules Rules(int seed = 0)
        {
            // 3 分の試合（ふつうの値）。カウントダウンは 0。
            return new MatchRules { CountdownTicks = 0, Seed = seed, BankMoveIntervalTicks = 100000 };
        }

        private static SimWorld World(MatchRules rules, params CoinSpawnPoint[] spawns)
        {
            var world = new SimWorld(TestArenas.Ground(), null, null, rules, spawns);
            world.AddCharacter(new CharacterConfig(), new Vector3(-60f, GroundY, 0f));
            return world;
        }

        private static void Step(SimWorld world, int ticks)
        {
            var inputs = new PlayerInput[world.CharacterCount];
            for (int i = 0; i < ticks; i++)
            {
                world.Step(inputs);
            }
        }

        private static int CountOrigin(SimWorld world, CoinEventKind origin)
        {
            int count = 0;
            foreach (FieldCoin coin in world.Coins.Field.Coins)
            {
                count += coin.Origin == origin ? 1 : 0;
            }
            return count;
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
        public void TheJackpot_IsAnnouncedAtOneMinuteTenSeconds_AndAppearsAtOneMinute()
        {
            MatchRules rules = Rules();
            int end = rules.CountdownTicks + rules.DurationTicks;
            Assert.That(FieldCoinRules.JackpotAnnounceTick(rules), Is.EqualTo(end - 70 * 60), "予告は残り 1 分 10 秒");
            Assert.That(FieldCoinRules.JackpotSpawnTick(rules), Is.EqualTo(end - 60 * 60), "出るのは残り 1 分");
        }

        [Test]
        public void HotAreas_AppearEveryThirtySeconds_FromTheStart()
        {
            MatchRules rules = Rules();
            Assert.That(HotAreaRules.RoundCount(rules), Is.EqualTo(5), "3 分の試合は 0:30・1:00・1:30・2:00・2:30 の 5 回");
            for (int round = 1; round <= 5; round++)
            {
                Assert.That(HotAreaRules.SpawnTick(rules, round), Is.EqualTo(round * 1800), $"{round} 回目の時刻");
                Assert.That(HotAreaRules.AnnounceTick(rules, round), Is.EqualTo(round * 1800 - 600), $"{round} 回目の予告は 10 秒前");
            }
            Assert.That(HotAreaRules.SpawnTick(rules, 6), Is.EqualTo(-1), "試合の終わりには出ない");
            Assert.That(HotAreaRules.SpawnTick(rules, 4), Is.EqualTo(FieldCoinRules.JackpotSpawnTick(rules)), "残り 1 分は 100 枚のコインと同時");
            Assert.That(HotAreaRules.SpawnTick(MatchRules.FreePlay(), 1), Is.EqualTo(-1), "時間の制限がなければ出ない");

            Assert.That(HotAreaRules.AnnouncedRound(rules, 1199, out _), Is.EqualTo(0), "予告の前");
            Assert.That(HotAreaRules.AnnouncedRound(rules, 1200, out int until), Is.EqualTo(1), "予告の始まり");
            Assert.That(until, Is.EqualTo(601));
            Assert.That(HotAreaRules.AnnouncedRound(rules, 1800, out until), Is.EqualTo(1), "出るステップの前まで予告");
            Assert.That(until, Is.EqualTo(1));
            Assert.That(HotAreaRules.AnnouncedRound(rules, 1801, out _), Is.EqualTo(0), "出た後は予告しない");
            Assert.That(HotAreaRules.ActiveRound(rules, 1800), Is.EqualTo(0), "出る前");
            Assert.That(HotAreaRules.ActiveRound(rules, 1801), Is.EqualTo(1), "出た");
            Assert.That(HotAreaRules.ActiveRound(rules, 3600), Is.EqualTo(1), "30 秒残る");
            Assert.That(HotAreaRules.ActiveRound(rules, 3601), Is.EqualTo(2), "次の回に入れ替わる");
        }

        [Test]
        public void AHotArea_PutsThirtyCoinsAroundTheFruit_AndTheyExpireWithIt()
        {
            MatchRules rules = Rules();
            SimWorld world = World(rules, Spots);
            Step(world, 1800);
            Assert.That(CountOrigin(world, CoinEventKind.HotArea), Is.EqualTo(0), "出る前に出た");
            Step(world, 1);
            Assert.That(CountOrigin(world, CoinEventKind.HotArea), Is.EqualTo(30), "30 枚のコインが出ない");
            int spot = HotAreaRules.Spot(rules, world.CoinSpawns, 1);
            Vector2 center = world.CoinSpawns[spot].Position;
            foreach (FieldCoin coin in world.Coins.Field.Coins)
            {
                if (coin.Origin == CoinEventKind.HotArea)
                {
                    Assert.That(coin.Value, Is.EqualTo(1), "通常のコインではない");
                    Assert.That(Vector2.Distance(coin.Position, center), Is.InRange(1.3f, 2.5f), "フルーツの周りにない");
                }
            }
            Assert.That(HotAreaRules.FruitPresent(rules, world.Match, world.Tick, out int round), Is.True, "フルーツが出ない");
            Assert.That(round, Is.EqualTo(1));
            AssertConserved(world);

            // 30 秒で消え、次の回のコインに入れ替わる（同時に出ているのは 1 回分だけ）。
            Step(world, 1800);
            Assert.That(CountOrigin(world, CoinEventKind.HotArea), Is.EqualTo(30), "次の回に入れ替わらない");
            Assert.That(HotAreaRules.ActiveRound(rules, world.Tick), Is.EqualTo(2));
            AssertConserved(world);
        }

        [Test]
        public void TheFruit_RestoresThirtyPercentGauge_Once()
        {
            // コインのゲージと分けるため、コインは出さない。
            MatchRules rules = Rules();
            rules.HotAreaCoins = 0;
            SimWorld world = World(rules, Spots);
            Step(world, 1801);
            int spot = HotAreaRules.Spot(rules, world.CoinSpawns, 1);
            Vector2 center = world.CoinSpawns[spot].Position;
            CharacterState state = world.GetState(0);
            state.Position = new Vector3(center.X, center.Y, 0f);
            state.Velocity = Vector3.Zero;
            state.Combat.Gauge = 100;
            world.SetState(0, state);
            Step(world, 1);
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(100 + 300), "ゲージが 30% 回復しない");
            Assert.That(world.Match.HotFruitTaken, Is.EqualTo(1));
            Assert.That(HotAreaRules.FruitPresent(rules, world.Match, world.Tick, out _), Is.False, "取ったのに残っている");

            // 同じ場所にいても、2 回は回復しない（巻き戻しでも同じ）。
            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);
            Step(world, 5);
            int gauge = world.GetState(0).Combat.Gauge;
            Assert.That(gauge, Is.EqualTo(100 + 300), "2 回回復した");
            world.LoadFrom(snapshot);
            Step(world, 5);
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(gauge), "巻き戻すと違う");
        }

        [Test]
        public void AtOneMinuteLeft_TheHotAreaAndTheJackpot_AreInDifferentPlaces()
        {
            var places = new HashSet<int>();
            for (int seed = 0; seed < 40; seed++)
            {
                MatchRules rules = Rules(seed);
                var spawns = new List<CoinSpawnPoint>(Spots);
                int jackpot = FieldCoinRules.JackpotSpot(rules, spawns);
                Assert.That(jackpot, Is.GreaterThanOrEqualTo(0));
                int previous = -1;
                for (int round = 1; round <= HotAreaRules.RoundCount(rules); round++)
                {
                    int spot = HotAreaRules.Spot(rules, spawns, round);
                    Assert.That(spawns[spot].Kind, Is.EqualTo(CoinSpawnKind.Jackpot), "候補の地点ではない");
                    Assert.That(spot, Is.Not.EqualTo(previous), $"種 {seed}: {round} 回目が前と同じ地点");
                    if (HotAreaRules.SpawnTick(rules, round) + rules.HotAreaLifetimeTicks > FieldCoinRules.JackpotAnnounceTick(rules))
                    {
                        Assert.That(spot, Is.Not.EqualTo(jackpot), $"種 {seed}: {round} 回目が 100 枚のコインと同じ地点");
                    }
                    previous = spot;
                    places.Add(spot);
                }
            }
            Assert.That(places.Count, Is.GreaterThanOrEqualTo(3), "種を変えても、場所が変わらない");

            // 実際に進めても、残り 1 分に 100 枚のコインとホットエリアが別々に出る。
            MatchRules match = Rules(7);
            SimWorld world = World(match, Spots);
            Step(world, FieldCoinRules.JackpotSpawnTick(match) + 1);
            Assert.That(CountOrigin(world, CoinEventKind.HotArea), Is.EqualTo(30), "ホットエリアが出ない");
            int jackpots = 0;
            foreach (FieldCoin coin in world.Coins.Field.Coins)
            {
                jackpots += coin.Value == 100 ? 1 : 0;
            }
            Assert.That(jackpots, Is.EqualTo(1), "100 枚のコインが出ない");
        }

        [Test]
        public void CoinsThatWouldBeInsideTerrain_ArePulledTowardTheFruit()
        {
            // 地面すれすれの候補の地点（下の輪は地面に埋まる）。
            var low = new CoinSpawnPoint(new Vector2(0f, 1.2f), 100, CoinSpawnKind.Jackpot);
            MatchRules rules = Rules();
            SimWorld world = World(rules, low);
            Step(world, 1801);
            Assert.That(CountOrigin(world, CoinEventKind.HotArea), Is.EqualTo(30));
            foreach (FieldCoin coin in world.Coins.Field.Coins)
            {
                // 地面（上面 y = 0）の上で、中心より下にある（地面の下へは抜けない）。
                Assert.That(coin.Position.Y, Is.InRange(0.3f - 1e-3f, 1.2f + 2.5f), $"コインが地面に埋まっている: {coin.Position}");
            }
        }

        [Test]
        public void CoinsNearTheStageEdge_ArePulledInsideTheKnockoutLines()
        {
            // 候補の地点が撃破の線の 1m 内側にあっても、輪のコインは線から BoundsInset より内側に寄せる。
            var rules = new MatchRules();
            var bounds = new StageBounds(-20f, -10f, 20f, 30f);
            var center = new Vector2(19f, 0f);
            for (int i = 0; i < rules.HotAreaCoins; i++)
            {
                Vector2 p = HotAreaRules.CoinPosition(rules, null, bounds, center, i);
                Assert.That(p.X, Is.LessThanOrEqualTo(bounds.MaxX - HotAreaRules.BoundsInset + 1e-4f), $"コイン {i} が撃破の線に近すぎる: {p}");
                Vector2 ring = center + HotAreaRules.CoinOffset(rules, i);
                if (ring.X <= bounds.MaxX - HotAreaRules.BoundsInset)
                {
                    Assert.That(p, Is.EqualTo(ring), $"範囲の中のコイン {i} まで動いた");
                }
            }
        }
    }
}
