using System.Numerics;
using Degunatsu.Core.Economy;
using Degunatsu.Core.Input;
using Degunatsu.Core.Match;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Tests.Support;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Match
{
    /// <summary>
    /// アディショナルタイム（docs/12_match_plan.md 4.2、T4-8。D-25、D-45 とその細部の決定）と、終盤のコインの出現の増加。
    /// 0 番と 1 番が、拠点（x = 0）から離れた地面（x = ∓5）に立つ。
    /// </summary>
    public class OvertimeTests
    {
        private const float GroundY = 0.91f;
        private static readonly BankSpot Bank = new BankSpot(new Vector2(0f, 1.5f), isFirst: true);

        private static MatchRules Rules(int duration = 60, int overtime = 30)
        {
            return new MatchRules { CountdownTicks = 0, DurationTicks = duration, OvertimeTicks = overtime, BankMoveIntervalTicks = 100000 };
        }

        private static SimWorld World(MatchRules rules, params CoinSpawnPoint[] coins)
        {
            var world = new SimWorld(TestArenas.Ground(), null, null, rules, coins, new[] { Bank });
            world.AddCharacter(new CharacterConfig(), new Vector3(-5f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(5f, GroundY, 0f));
            return world;
        }

        private static void Coins(SimWorld world, int character, int banked, int held)
        {
            CharacterState state = world.GetState(character);
            state.Coins.Banked = banked;
            state.Coins.Held = held;
            world.SetState(character, state);
        }

        private static void MoveToBank(SimWorld world, int character)
        {
            CharacterState state = world.GetState(character);
            state.Position = new Vector3(0f, GroundY, 0f);
            state.Velocity = Vector3.Zero;
            world.SetState(character, state);
        }

        private static void Step(SimWorld world, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.Step(new PlayerInput[2]);
            }
        }

        [Test]
        public void WhenNobodyCanOvertake_TheMatchEndsAtTimeUp()
        {
            SimWorld world = World(Rules());
            Coins(world, 0, banked: 10, held: 0);
            Coins(world, 1, banked: 2, held: 7);
            Step(world, 60);

            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Finished), "逆転できる人がいないのに終わらない");
            Assert.That(world.Match.HasOvertime, Is.False);
            Assert.That(world.Match.EndTick, Is.EqualTo(60));
        }

        [Test]
        public void SomeoneWhoCanOvertake_StartsTheOvertime_ForItsLength()
        {
            SimWorld world = World(Rules(overtime: 30));
            Coins(world, 0, banked: 10, held: 0);
            Coins(world, 1, banked: 2, held: 9);
            Step(world, 60);

            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Overtime), "逆転できる人がいるのにアディショナルタイムにならない");
            Assert.That(MatchFlow.TicksLeftAt(world.Rules, world.Match, world.Tick), Is.EqualTo(30));
            Step(world, 29);
            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Overtime));
            Step(world, 1);
            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Finished), "時間が過ぎても終わらない");
            Assert.That(world.Match.Winner, Is.EqualTo(-1), "だれも拠点に着いていないのに勝者がいる");
            Assert.That(MatchResults.Rank(new[] { world.GetState(0), world.GetState(1) }, world.Match.Winner)[0], Is.EqualTo(0), "確定コインの順にならない");
        }

        [Test]
        public void AChallengerReachingTheBank_WinsAtOnce()
        {
            SimWorld world = World(Rules());
            Coins(world, 0, banked: 10, held: 0);
            Coins(world, 1, banked: 2, held: 9);
            Step(world, 60);
            MoveToBank(world, 1);
            Step(world, 1);

            Assert.That(world.Match.Winner, Is.EqualTo(1), "拠点に着いた逆転できる人が勝たない");
            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Finished));
            Assert.That(world.GetState(1).Coins.Banked, Is.EqualTo(11), "勝った人の所持コインが確定しない");
            Assert.That(MatchResults.Rank(new[] { world.GetState(0), world.GetState(1) }, world.Match.Winner)[0], Is.EqualTo(1));
        }

        [Test]
        public void TheLeaderReachingTheBank_WinsAndEnds()
        {
            SimWorld world = World(Rules());
            Coins(world, 0, banked: 10, held: 0);
            Coins(world, 1, banked: 2, held: 9);
            Step(world, 60);
            MoveToBank(world, 0);
            Step(world, 1);

            Assert.That(world.Match.Winner, Is.EqualTo(0), "首位が拠点に着いても終わらない");
            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Finished));
        }

        [Test]
        public void WhenTheChallengerCanNoLongerOvertake_TheMatchEndsAtOnce()
        {
            SimWorld world = World(Rules(overtime: 100));
            Coins(world, 0, banked: 10, held: 0);
            Coins(world, 1, banked: 2, held: 9);
            Step(world, 60);
            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Overtime), "前提");

            // 攻撃を受けて所持コインを落とした、など。
            Coins(world, 1, banked: 2, held: 3);
            Step(world, 1);
            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Finished), "逆転できる人がいなくなっても終わらない");
            Assert.That(world.Match.Winner, Is.EqualTo(-1));
        }

        [Test]
        public void ATieAtTheTop_StartsTheOvertime_AndTheFirstToTheBankWins()
        {
            SimWorld world = World(Rules());
            Coins(world, 0, banked: 10, held: 0);
            Coins(world, 1, banked: 10, held: 0);
            Step(world, 60);
            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Overtime), "同点なのにアディショナルタイムにならない（D-45）");

            MoveToBank(world, 1);
            Step(world, 1);
            Assert.That(world.Match.Winner, Is.EqualTo(1), "同点で先に拠点に着いた人が勝たない");
            // 確定コインが同じでも、勝った人がいちばん上（番号の小さい 0 番より上）。
            Assert.That(MatchResults.Rank(new[] { world.GetState(0), world.GetState(1) }, world.Match.Winner), Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void InTheOvertime_SomeoneShortAtTimeUp_CanStillWinByEarningCoins()
        {
            // 0 番が首位（10）、1 番が逆転できる（2＋9）、2 番は時間切れの時点では足りない（0＋5）。
            var coin = new CoinSpawnPoint(new Vector2(12f, 1f), 20);
            var world = new SimWorld(TestArenas.Ground(), null, null, Rules(overtime: 100), new[] { coin }, new[] { Bank });
            world.AddCharacter(new CharacterConfig(), new Vector3(-5f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(5f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(8f, GroundY, 0f));
            Coins(world, 0, banked: 10, held: 0);
            Coins(world, 1, banked: 2, held: 9);
            Coins(world, 2, banked: 0, held: 5);
            var inputs = new PlayerInput[3];
            for (int i = 0; i < 60; i++)
            {
                world.Step(inputs);
            }
            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Overtime), "前提");
            Assert.That(MatchFlow.CanWinAtTheBank(new[] { world.GetState(0), world.GetState(1), world.GetState(2) }, 2), Is.False, "前提: 2 番は足りない");

            // 2 番がアディショナルタイム中に 20 枚のコインを拾って、拠点に着く。
            CharacterState state = world.GetState(2);
            state.Position = new Vector3(12f, GroundY, 0f);
            world.SetState(2, state);
            world.Step(inputs);
            Assert.That(world.GetState(2).Coins.Held, Is.EqualTo(25), "前提: 拾った");
            state = world.GetState(2);
            state.Position = new Vector3(0f, GroundY, 0f);
            world.SetState(2, state);
            world.Step(inputs);

            Assert.That(world.Match.Winner, Is.EqualTo(2), "アディショナルタイム中に稼いで条件を満たした人が勝たない");
            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Finished));
        }

        [Test]
        public void WithoutATimeLimit_ThereIsNoOvertime()
        {
            SimWorld world = World(new MatchRules { CountdownTicks = 0, DurationTicks = 0 });
            Coins(world, 0, banked: 10, held: 0);
            Coins(world, 1, banked: 10, held: 5);
            Step(world, 500);
            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Playing));
            Assert.That(world.Match, Is.EqualTo(MatchState.None));
        }

        [Test]
        public void RollingBack_AcrossTheOvertime_GivesTheSameResult()
        {
            SimWorld world = World(Rules());
            Coins(world, 0, banked: 10, held: 0);
            Coins(world, 1, banked: 2, held: 9);
            Step(world, 55);
            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);
            Step(world, 10);
            MoveToBank(world, 1);
            Step(world, 3);
            ulong expected = world.Checksum();
            MatchState expectedMatch = world.Match;

            world.LoadFrom(snapshot);
            Assert.That(world.Match, Is.EqualTo(MatchState.None), "巻き戻しても、アディショナルタイムが残っている");
            Step(world, 10);
            MoveToBank(world, 1);
            Step(world, 3);
            Assert.That(world.Match, Is.EqualTo(expectedMatch));
            Assert.That(world.Checksum(), Is.EqualTo(expected));
        }

        [Test]
        public void InTheLateGame_CoinsComeBackFaster()
        {
            var rules = new MatchRules
            {
                CountdownTicks = 0,
                DurationTicks = 1000,
                CoinSpawnIntervalTicks = 600,
                LateGameTicks = 300,
                LateGameSpawnIntervalTicks = 200,
                BankMoveIntervalTicks = 100000,
            };
            var coin = new CoinSpawnPoint(new Vector2(5f, 1f), 1);
            SimWorld world = World(rules, coin);
            Step(world, 1);
            int pickedAt = world.Tick;
            Assert.That(world.GetState(1).Coins.Held, Is.EqualTo(1), "前提: 拾った");
            Assert.That(FieldCoinRules.EffectiveReadyTick(world.Coins.Field, 0, rules, 650), Is.EqualTo(pickedAt + 600), "終盤の前は、ふつうの間隔");
            Assert.That(FieldCoinRules.EffectiveReadyTick(world.Coins.Field, 0, rules, 750), Is.EqualTo(pickedAt + 200), "終盤は短い間隔");

            // 1 番を離してから、終盤に入るまで進める。終盤に入ったら、ふつうの間隔より早く出直す。
            CharacterState away = world.GetState(1);
            away.Position = new Vector3(-5f, GroundY, 0f);
            world.SetState(1, away);
            Step(world, 700 - world.Tick);
            Assert.That(world.Coins.Field.Coins.Count, Is.GreaterThanOrEqualTo(1), "終盤なのに出直さない");
        }

        [Test]
        public void InvalidOvertimeRules_AreRejected()
        {
            Assert.Throws<Degunatsu.Core.Common.InvalidTuningException>(() => World(new MatchRules { OvertimeTicks = -1 }));
            Assert.Throws<Degunatsu.Core.Common.InvalidTuningException>(() => World(new MatchRules { LateGameSpawnIntervalTicks = 0 }));
        }
    }
}
