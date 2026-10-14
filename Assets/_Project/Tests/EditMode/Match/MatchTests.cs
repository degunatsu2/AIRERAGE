using System;
using System.Numerics;
using Airerage.Core.Common;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Match
{
    /// <summary>試合の進行（カウントダウン、制限時間、終了）と順位（docs/12_match_plan.md T4-1）。</summary>
    public class MatchTests
    {
        private const float GroundY = 0.91f;

        private static MatchRules Short(int countdown, int duration)
        {
            return new MatchRules { CountdownTicks = countdown, DurationTicks = duration };
        }

        private static SimWorld World(MatchRules rules)
        {
            var world = new SimWorld(TestArenas.Ground(), null, null, rules);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            return world;
        }

        [Test]
        public void Phase_FollowsTheTick()
        {
            MatchRules rules = Short(180, 600);
            Assert.That(rules.PhaseAt(0), Is.EqualTo(MatchPhase.Countdown));
            Assert.That(rules.PhaseAt(179), Is.EqualTo(MatchPhase.Countdown));
            Assert.That(rules.PhaseAt(180), Is.EqualTo(MatchPhase.Playing));
            Assert.That(rules.PhaseAt(779), Is.EqualTo(MatchPhase.Playing));
            Assert.That(rules.PhaseAt(780), Is.EqualTo(MatchPhase.Finished));
            Assert.That(rules.TicksLeftAt(0), Is.EqualTo(180), "始まるまで");
            Assert.That(rules.TicksLeftAt(180), Is.EqualTo(600), "終わるまで");
            Assert.That(rules.TicksLeftAt(779), Is.EqualTo(1));
            Assert.That(rules.TicksLeftAt(900), Is.EqualTo(0));
        }

        [Test]
        public void FreePlay_NeverEnds()
        {
            MatchRules rules = MatchRules.FreePlay();
            Assert.That(rules.PhaseAt(0), Is.EqualTo(MatchPhase.Playing));
            Assert.That(rules.PhaseAt(10000000), Is.EqualTo(MatchPhase.Playing));
            Assert.That(rules.TicksLeftAt(100), Is.EqualTo(-1));
            Assert.That(new SimWorld(TestArenas.Ground()).Rules.HasTimeLimit, Is.False, "ルールを渡さなければ時間の制限はない");
        }

        [Test]
        public void Countdown_IgnoresInput()
        {
            SimWorld world = World(Short(60, 600));
            for (int i = 0; i < 60; i++)
            {
                world.Step(new[] { Inputs.Right });
            }
            Assert.That(world.GetState(0).Position.X, Is.EqualTo(0f), "カウントダウン中に動いた");
            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Playing));

            world.Step(new[] { Inputs.Right });
            Assert.That(world.GetState(0).Position.X, Is.GreaterThan(0f), "始まっても動けない");
        }

        [Test]
        public void ButtonHeldThroughTheCountdown_IsAPress_AtTheStart()
        {
            // カウントダウン中に押していたボタンは、始まったティックに押したとみなす（「GO!」と同時に動ける）。
            SimWorld world = World(Short(30, 600));
            for (int i = 0; i < 31; i++)
            {
                world.Step(new[] { Inputs.Jump });
            }
            Assert.That(world.GetState(0).Velocity.Y, Is.GreaterThan(0f), "始まったティックにジャンプしない");
        }

        [Test]
        public void Finished_StopsEveryone()
        {
            SimWorld world = World(Short(0, 30));
            CharacterState state = world.GetState(0);
            state.Velocity = new Vector3(5f, 8f, 0f);
            world.SetState(0, state);
            for (int i = 0; i < 30; i++)
            {
                world.Step(new[] { Inputs.Right });
            }
            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Finished));
            CharacterState atEnd = world.GetState(0);

            for (int i = 0; i < 60; i++)
            {
                world.Step(new[] { Inputs.RightJump });
            }
            Assert.That(world.GetState(0), Is.EqualTo(atEnd), "終わった後に状態が変わった");
            Assert.That(world.Tick, Is.EqualTo(90), "ティック数は進む");
            Assert.That(world.LastHits, Is.Empty);
        }

        [Test]
        public void RollingBackAcrossTheEnd_GivesTheSameResult()
        {
            SimWorld world = World(Short(20, 100));
            var snapshot = new WorldSnapshot();
            for (int i = 0; i < 90; i++)
            {
                world.Step(new[] { Inputs.Right });
            }
            world.SaveTo(snapshot);
            for (int i = 0; i < 60; i++)
            {
                world.Step(new[] { i % 20 == 0 ? Inputs.Jump : Inputs.Left });
            }
            ulong expected = world.Checksum();

            world.LoadFrom(snapshot);
            for (int i = 0; i < 60; i++)
            {
                world.Step(new[] { i % 20 == 0 ? Inputs.Jump : Inputs.Left });
            }
            Assert.That(world.Checksum(), Is.EqualTo(expected));
            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Finished));
        }

        [Test]
        public void Rank_ByKnockouts_ThenFalls_ThenNumber()
        {
            var states = new CharacterState[4];
            states[0].Combat.Knockouts = 1;
            states[0].Combat.Falls = 2;
            states[1].Combat.Knockouts = 3;
            states[1].Combat.Falls = 5;
            states[2].Combat.Knockouts = 1;
            states[2].Combat.Falls = 1;
            states[3].Combat.Knockouts = 1;
            states[3].Combat.Falls = 2;

            Assert.That(MatchResults.Rank(states), Is.EqualTo(new[] { 1, 2, 0, 3 }));
        }

        [Test]
        public void Rank_ByBankedCoins_First()
        {
            // スコアは確定コインだけ（D-25）。所持コインは数えない。
            var states = new CharacterState[3];
            states[0].Combat.Knockouts = 5;
            states[0].Coins.Held = 50;
            states[1].Coins.Banked = 12;
            states[2].Coins.Banked = 30;

            Assert.That(MatchResults.Rank(states), Is.EqualTo(new[] { 2, 1, 0 }));
        }

        [Test]
        public void InvalidRules_AreRejected()
        {
            Assert.Throws<InvalidTuningException>(() => new SimWorld(TestArenas.Ground(), null, null, new MatchRules { DurationTicks = -1 }));
            Assert.Throws<InvalidTuningException>(() => new SimWorld(TestArenas.Ground(), null, null, new MatchRules { RespawnDelayTicks = -5 }));
        }
    }
}
