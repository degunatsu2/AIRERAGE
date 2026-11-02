using System;
using Airerage.Core.Input;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Rollback
{
    /// <summary>
    /// 観戦（docs/19。ユーザーの要望、2026-11-02）: ホストの RollbackSession が確定したティックの全員の入力を知らせ（TickConfirmed）、
    /// それで進めた観戦のワールド（SpectatorSession）が、本当の入力で計算したものと同じになる。抜けた人がいても同じ。
    /// </summary>
    public class SpectatorSessionTests
    {
        private static readonly RollbackSettings Settings = new RollbackSettings { InputDelayTicks = 2, MaxPredictionTicks = 8 };

        [TestCase(false, TestName = "観戦: 3 人の試合")]
        [TestCase(true, TestName = "観戦: 途中で 1 人が抜ける")]
        public void Spectator_FedWithConfirmedTicks_MatchesTheTrueResult(bool someoneLeaves)
        {
            var network = new PseudoNetwork(3, Settings, latencyFrames: 4, jitterFrames: 2, lossRate: 0.1, seed: 11);
            var spectator = new SpectatorSession(network.Reference(0));
            int expectedTick = 0;
            network.Sessions[0].TickConfirmed += (tick, inputs) =>
            {
                Assert.That(tick, Is.EqualTo(expectedTick), "確定したティックが順に 1 回ずつ知らされない");
                expectedTick++;
                spectator.AddInputs(tick, inputs);
            };

            network.RunUntil(200, maxFrames: 3000);
            if (someoneLeaves)
            {
                network.RemovePlayer(2, noticeLatencyFrames: 3);
            }
            network.RunUntil(500, maxFrames: 5000);

            int buffered = spectator.BufferedTicks;
            while (spectator.Step())
            {
            }
            Assert.That(spectator.CurrentTick, Is.GreaterThan(480), "観戦が確定したティックまで進まない");
            Assert.That(spectator.CurrentTick, Is.LessThanOrEqualTo(500));
            Assert.That(buffered, Is.EqualTo(spectator.CurrentTick), "途切れずに届いた数が違う");
            Assert.That(spectator.World.Checksum(), Is.EqualTo(network.Reference(spectator.CurrentTick).Checksum()),
                "観戦のワールドが、本当の入力で計算したものと違う");
            if (someoneLeaves)
            {
                Assert.That(spectator.World.GetState(2).Combat.HasLeft, Is.True, "観戦のワールドで、抜けた人に印がつかない");
            }
        }

        [Test]
        public void Spectator_WaitsForMissingTicks_AndRejectsConflictingInputs()
        {
            var world = new SimWorld(TestArenas.Playground());
            world.AddCharacter(new CharacterConfig(), PseudoNetwork.Spawns[0]);
            world.AddCharacter(new CharacterConfig(), PseudoNetwork.Spawns[1]);
            var spectator = new SpectatorSession(world);
            var right = new[] { PlayerInput.FromAxes(1f, 0f, InputButtons.None), default(PlayerInput) };

            spectator.AddInputs(1, right);
            Assert.That(spectator.CanStep, Is.False, "ティック 0 がないのに進める");
            Assert.That(spectator.Step(), Is.False);
            spectator.AddInputs(0, right);
            Assert.That(spectator.BufferedTicks, Is.EqualTo(2));
            Assert.That(spectator.Step() && spectator.Step(), Is.True);
            Assert.That(spectator.CurrentTick, Is.EqualTo(2));

            spectator.AddInputs(0, right);
            spectator.AddInputs(3, right);
            spectator.AddInputs(3, right);
            Assert.Throws<InvalidOperationException>(() => spectator.AddInputs(3, new[] { default(PlayerInput), default(PlayerInput) }));
            Assert.Throws<ArgumentException>(() => spectator.AddInputs(4, new[] { default(PlayerInput) }), "人数と違う入力");
        }
    }
}
