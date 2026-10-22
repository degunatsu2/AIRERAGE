using System;
using System.Collections.Generic;
using Airerage.Core.Input;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Rollback
{
    /// <summary>
    /// 試合中に抜けた人を外して続ける（docs/15 T6-10）: 抜けたティックからその人の入力を「抜けた」で確定し、残りの人が同じ結果になる。
    /// </summary>
    public class PlayerLeaveTests
    {
        private static readonly RollbackSettings Settings = new RollbackSettings { InputDelayTicks = 2, MaxPredictionTicks = 8 };

        [Test]
        public void OneOfThreeLeaves_TheOthersContinue_AndStayInSync()
        {
            var network = new PseudoNetwork(3, Settings, latencyFrames: 3, jitterFrames: 2, lossRate: 0.1, seed: 5);
            network.RunUntil(150, maxFrames: 2000);

            int fromTick = network.RemovePlayer(2, noticeLatencyFrames: 4);
            network.RunUntil(400, maxFrames: 4000);

            Assert.That(fromTick, Is.GreaterThan(140), "抜けたティックが、抜けた人の送った入力の次になっていない");
            Assert.That(network.ChecksumMismatches, Is.EqualTo(0), "残りの人の確定した状態がそろわない");
            Assert.That(network.ComparedTicks, Is.GreaterThan(300));
            ulong reference = network.Reference(400).Checksum();
            for (int p = 0; p < 2; p++)
            {
                Assert.That(network.Worlds[p].Checksum(), Is.EqualTo(reference), $"プレイヤー {p} の状態が、本当の入力だけで計算したものと違う");
                Assert.That(network.Worlds[p].GetState(2).Combat.HasLeft, Is.True, "抜けた人に印がつかない");
                Assert.That(network.Sessions[p].HasLeft(2), Is.True);
                Assert.That(network.Exchanges[p].UnackedCount, Is.LessThan(40), "抜けた人の確認を待ち続けている");
            }
        }

        [Test]
        public void LastRemoteLeaves_TheSessionKeepsGoingAlone()
        {
            var network = new PseudoNetwork(2, Settings, latencyFrames: 2, jitterFrames: 1, lossRate: 0.05, seed: 9);
            network.RunUntil(60, maxFrames: 1000);

            network.RemovePlayer(1, noticeLatencyFrames: 2);
            network.RunUntil(200, maxFrames: 1000);

            Assert.That(network.Sessions[0].CurrentTick, Is.EqualTo(200));
            Assert.That(network.Exchanges[0].UnackedCount, Is.EqualTo(0), "相手がいないのに入力を送るために残している");
            Assert.That(network.Worlds[0].Checksum(), Is.EqualTo(network.Reference(200).Checksum()));
        }

        [Test]
        public void RemovePlayer_NeedsEveryInputBeforeTheLeavingTick()
        {
            RollbackSession session = Create(0, out _);
            session.AddRemoteInput(1, 2, Inputs.Right);

            Assert.Throws<InvalidOperationException>(() => session.RemovePlayer(1, 5), "ティック 3・4 の入力がないのに抜けたことにした");
            Assert.Throws<ArgumentOutOfRangeException>(() => session.RemovePlayer(0, 3), "自分を外した");
        }

        [Test]
        public void RemovePlayer_RollsBackPredictedTicks_AndUsesTheDepartedInput()
        {
            RollbackSession session = Create(0, out SimWorld world);
            for (int i = 0; i < 6; i++)
            {
                session.AdvanceFrame(Inputs.Right);
            }
            int rollbacks = session.RollbackCount;
            session.AddRemoteInput(1, 2, Inputs.Right);
            session.RemovePlayer(1, 3);
            session.AdvanceFrame(Inputs.Right);

            Assert.That(session.RollbackCount, Is.EqualTo(rollbacks + 1), "予測で計算したティックを計算し直していない");
            Assert.That(session.TryGetInput(1, 3, out _), Is.False);
            Assert.That(world.GetState(1).Combat.HasLeft, Is.True);
            Assert.That(session.ConfirmedTick, Is.GreaterThanOrEqualTo(session.LocalInputTick - 1), "抜けた人の入力を待っている");
            Assert.That(session.CanAdvance, Is.True);

            // 遅れて届いた、抜けた人の入力は使わない。
            Assert.DoesNotThrow(() => session.AddRemoteInput(1, 4, Inputs.Left));
            Assert.That(session.TryGetInput(1, 4, out _), Is.False);
        }

        [Test]
        public void Exchange_IgnoresMessagesFromTheDeparted_AndDoesNotBuildForThem()
        {
            InputExchange a = CreateExchange(0);
            InputExchange b = CreateExchange(1);
            b.AdvanceFrame(Inputs.Jump);
            InputMessage late = b.BuildMessage(0);

            a.RemovePlayer(1, 3, 2, new List<PlayerInput> { Inputs.Jump });

            Assert.DoesNotThrow(() => a.Receive(late));
            Assert.That(a.HasLeft(1), Is.True);
            Assert.Throws<InvalidOperationException>(() => a.BuildMessage(1));
        }

        [Test]
        public void DepartedCharacter_StaysPut_TouchesNothing_AndKeepsItsCoins()
        {
            var world = new SimWorld(TestArenas.Playground());
            world.AddCharacter(new CharacterConfig(), PseudoNetwork.Spawns[0]);
            world.AddCharacter(new CharacterConfig(), PseudoNetwork.Spawns[1]);
            CharacterState state = world.GetState(1);
            state.Coins.Held = 12;
            state.Coins.Banked = 40;
            state.Combat.Knockouts = 2;
            world.SetState(1, state);

            var inputs = new[] { default(PlayerInput), PlayerInput.Departed };
            world.Step(inputs);
            CharacterState departed = world.GetState(1);
            for (int i = 0; i < 60; i++)
            {
                world.Step(inputs);
            }
            CharacterState later = world.GetState(1);

            Assert.That(departed.Combat.HasLeft, Is.True);
            Assert.That(departed.Combat.IsUntouchable && departed.Combat.IsWaitingToRespawn, Is.True, "抜けた人が見えたり触れたりする");
            Assert.That(later.Position, Is.EqualTo(departed.Position), "抜けた人が動く");
            Assert.That(later.Coins.Held, Is.EqualTo(12));
            Assert.That(later.Coins.Banked, Is.EqualTo(40), "確定したコインが結果に残らない");
            Assert.That(later.Combat.Knockouts, Is.EqualTo(2));
        }

        private static RollbackSession Create(int localPlayer, out SimWorld world)
        {
            world = new SimWorld(TestArenas.Playground());
            world.AddCharacter(new CharacterConfig(), PseudoNetwork.Spawns[0]);
            world.AddCharacter(new CharacterConfig(), PseudoNetwork.Spawns[1]);
            return new RollbackSession(world, localPlayer, Settings);
        }

        private static InputExchange CreateExchange(int localPlayer)
        {
            RollbackSession session = Create(localPlayer, out _);
            return new InputExchange(session, Settings);
        }
    }
}
