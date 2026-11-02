using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Match
{
    /// <summary>
    /// 逆転の要素（ユーザーの要望、2026-11-02。CatchUpRules）: 確定コインの順位が低い人ほど 1 秒ごとにゲージを得る。所持コインが 100 枚以上の人は
    /// WANTED で、被弾ドロップが 2 倍になる。
    /// </summary>
    public class CatchUpTests
    {
        private const float GroundY = 0.91f;

        private static SimWorld World(int players)
        {
            var rules = new MatchRules { CountdownTicks = 0, DurationTicks = 6000, BankMoveIntervalTicks = 100000 };
            var world = new SimWorld(TestArenas.Ground(), null, null, rules);
            for (int i = 0; i < players; i++)
            {
                world.AddCharacter(new CharacterConfig(), new Vector3(-30f + i * 20f, GroundY, 0f));
            }
            return world;
        }

        private static void Banked(SimWorld world, params int[] banked)
        {
            for (int i = 0; i < banked.Length; i++)
            {
                CharacterState state = world.GetState(i);
                state.Coins.Banked = banked[i];
                world.SetState(i, state);
            }
        }

        private static List<CharacterState> States(SimWorld world)
        {
            var states = new List<CharacterState>();
            for (int i = 0; i < world.CharacterCount; i++)
            {
                states.Add(world.GetState(i));
            }
            return states;
        }

        private static void Step(SimWorld world, int ticks)
        {
            var inputs = new PlayerInput[world.CharacterCount];
            for (int i = 0; i < ticks; i++)
            {
                world.Step(inputs);
            }
        }

        [Test]
        public void Rank_IsOnePlusTheNumberOfPlayersWithMoreBankedCoins()
        {
            SimWorld world = World(4);
            Banked(world, 30, 50, 30, 0);
            List<CharacterState> states = States(world);
            Assert.That(CatchUpRules.RankOf(states, 1), Is.EqualTo(1));
            Assert.That(CatchUpRules.RankOf(states, 0), Is.EqualTo(2), "同じ確定コインは同じ順位");
            Assert.That(CatchUpRules.RankOf(states, 2), Is.EqualTo(2), "同じ確定コインは同じ順位");
            Assert.That(CatchUpRules.RankOf(states, 3), Is.EqualTo(4), "同じ順位の 2 人の次は 4 位");

            // 試合から抜けた人は数えない。
            CharacterState left = states[1];
            left.Combat.HasLeft = true;
            states[1] = left;
            Assert.That(CatchUpRules.RankOf(states, 1), Is.EqualTo(0));
            Assert.That(CatchUpRules.RankOf(states, 0), Is.EqualTo(1));
            Assert.That(CatchUpRules.RankOf(states, 3), Is.EqualTo(3));
        }

        [Test]
        public void LowerRanks_GainGaugeEverySecond_AndTheLeaderGainsNothing()
        {
            SimWorld world = World(4);
            Banked(world, 30, 20, 10, 0);
            Step(world, 59);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(world.GetState(i).Combat.Gauge, Is.Zero, $"{i + 1} 番が 1 秒より前にゲージを得た");
            }
            Step(world, 1);
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(0), "1 位がゲージを得た");
            Assert.That(world.GetState(1).Combat.Gauge, Is.EqualTo(5), "2 位は 0.5%");
            Assert.That(world.GetState(2).Combat.Gauge, Is.EqualTo(10), "3 位は 1.0%");
            Assert.That(world.GetState(3).Combat.Gauge, Is.EqualTo(20), "4 位は 2.0%");
            Step(world, 60);
            Assert.That(world.GetState(3).Combat.Gauge, Is.EqualTo(40), "次の 1 秒でまた得る");
        }

        [Test]
        public void EveryoneTiedAtTheStart_GainsNothing()
        {
            SimWorld world = World(4);
            Step(world, 600);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(world.GetState(i).Combat.Gauge, Is.Zero, "みんな 1 位なのにゲージを得た");
            }
        }

        [Test]
        public void NoGauge_DuringTheCountdown_OrWithoutAMatch()
        {
            var rules = new MatchRules { CountdownTicks = 120, DurationTicks = 6000, BankMoveIntervalTicks = 100000 };
            var world = new SimWorld(TestArenas.Ground(), null, null, rules);
            world.AddCharacter(new CharacterConfig(), new Vector3(-10f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(10f, GroundY, 0f));
            Banked(world, 10, 0);
            Step(world, 120);
            Assert.That(world.GetState(1).Combat.Gauge, Is.Zero, "カウントダウンの間にゲージを得た");
            Step(world, 60);
            Assert.That(world.GetState(1).Combat.Gauge, Is.EqualTo(5), "始まって 1 秒で得ない");
        }

        [Test]
        public void WantedPlayers_DropTwiceAsManyCoinsWhenHit()
        {
            var rules = new MatchRules();
            Assert.That(CatchUpRules.IsWanted(rules, 99), Is.False);
            Assert.That(CatchUpRules.IsWanted(rules, 100), Is.True, "100 枚から WANTED");
            Assert.That(CoinDropRules.HitDropAmount(rules, 99), Is.EqualTo(9), "WANTED でなければ、ふつうの量");
            Assert.That(CoinDropRules.HitDropAmount(rules, 100), Is.EqualTo(20), "WANTED は 2 倍（10 → 20）");
            Assert.That(CoinDropRules.HitDropAmount(rules, 400), Is.EqualTo(20), "上限も 2 倍");
            Assert.That(CoinDropRules.KoDropAmount(rules, 100), Is.EqualTo(50), "撃破ドロップは変えない");
        }
    }
}
