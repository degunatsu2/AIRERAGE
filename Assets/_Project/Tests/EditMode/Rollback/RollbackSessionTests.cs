using System;
using Airerage.Core.Input;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Rollback
{
    /// <summary>
    /// ロールバックのセッション（docs/10_online_plan.md T2-2、T2-3）。4人のセッションを疑似ネットワークでつなぎ、
    /// どんな通信の条件でも、全員が本当の入力だけで計算した結果と同じ状態になることを確かめる。
    /// </summary>
    public class RollbackSessionTests
    {
        private const int TargetTick = 600;

        // 遅延・揺らぎ（フレーム）、欠落率、入力遅延、予測の上限
        [TestCase(0, 0, 0.0, 2, 8, TestName = "遅延なし")]
        [TestCase(2, 0, 0.0, 2, 8, TestName = "遅延が入力遅延と同じ")]
        [TestCase(6, 0, 0.0, 2, 8, TestName = "遅延が入力遅延より大きい")]
        [TestCase(4, 3, 0.1, 2, 8, TestName = "揺らぎと欠落10%")]
        [TestCase(5, 2, 0.05, 0, 8, TestName = "入力遅延なし")]
        [TestCase(12, 4, 0.2, 2, 8, TestName = "予測の上限を超える遅延と欠落20%")]
        [TestCase(3, 0, 0.0, 2, 0, TestName = "予測なし（ロックステップ）")]
        public void AllPlayersReachTheSameStateAsTheTrueInputs(int latency, int jitter, double loss, int delay, int maxPrediction)
        {
            var settings = new RollbackSettings { InputDelayTicks = delay, MaxPredictionTicks = maxPrediction };
            var network = new PseudoNetwork(4, settings, latency, jitter, loss, seed: 7);

            network.RunUntil(TargetTick, maxFrames: 5000);

            ulong expected = network.Reference(TargetTick).Checksum();
            for (int p = 0; p < network.Sessions.Length; p++)
            {
                Assert.That(network.Worlds[p].Tick, Is.EqualTo(TargetTick));
                Assert.That(network.Worlds[p].Checksum(), Is.EqualTo(expected), $"プレイヤー {p} の最後の状態が違う");
            }
            Assert.That(network.ChecksumMismatches, Is.EqualTo(0), "確定した状態が途中でずれた");
            Assert.That(network.ComparedTicks, Is.GreaterThan(TargetTick / 2), "前提: 途中の確定した状態を比べている");

            bool noLateInputs = latency + jitter <= delay && loss == 0.0;
            if (noLateInputs || maxPrediction == 0)
            {
                foreach (RollbackSession session in network.Sessions)
                {
                    Assert.That(session.RollbackCount, Is.EqualTo(0), "入力が間に合っているのに巻き戻した");
                }
            }
            if (latency > delay && maxPrediction > 0)
            {
                Assert.That(network.Sessions[0].RollbackCount, Is.GreaterThan(0), "前提: 予測が外れて巻き戻している");
            }
            if (latency > maxPrediction + delay)
            {
                Assert.That(network.StalledFrames, Is.GreaterThan(0), "前提: 予測の上限で待っている");
            }
            foreach (RollbackSession session in network.Sessions)
            {
                Assert.That(session.LongestRollback, Is.LessThanOrEqualTo(maxPrediction + 1), "予測の上限より長く巻き戻した");
            }
        }

        [Test]
        public void RemoteHoldingTheSameInput_RollsBackOnlyWhenItChanges()
        {
            // 相手が右を押し続けているとき、予測（直前の入力を続ける）が当たるので、巻き戻すのは最初に届いたときだけ。
            var world = TwoPlayerWorld();
            var session = new RollbackSession(world, 0, new RollbackSettings { InputDelayTicks = 0, MaxPredictionTicks = 8 });
            const int latency = 4;

            for (int frame = 0; frame < 120; frame++)
            {
                int arrived = frame - latency;
                if (arrived >= 0)
                {
                    session.AddRemoteInput(1, arrived, Inputs.Right);
                }
                session.AdvanceFrame(Inputs.None);
            }

            Assert.That(session.RollbackCount, Is.EqualTo(1));
        }

        [Test]
        public void SameInputTwice_IsIgnored_ButADifferentInputForTheSameTickThrows()
        {
            var session = new RollbackSession(TwoPlayerWorld(), 0, new RollbackSettings());
            int tick = session.LocalInputTick;

            session.AddRemoteInput(1, tick, Inputs.Jump);
            session.AddRemoteInput(1, tick, Inputs.Jump);

            Assert.Throws<InvalidOperationException>(() => session.AddRemoteInput(1, tick, Inputs.Left));
            Assert.Throws<ArgumentException>(() => session.AddRemoteInput(0, tick, Inputs.Left));
        }

        [Test]
        public void Settings_NoDelayAndNoPrediction_IsRejected()
        {
            var settings = new RollbackSettings { InputDelayTicks = 0, MaxPredictionTicks = 0 };

            Assert.Throws<Airerage.Core.Common.InvalidTuningException>(() => new RollbackSession(TwoPlayerWorld(), 0, settings));
        }

        [Test]
        public void AdvanceBeyondPredictionLimit_Throws()
        {
            var session = new RollbackSession(TwoPlayerWorld(), 0, new RollbackSettings { InputDelayTicks = 2, MaxPredictionTicks = 3 });

            while (session.CanAdvance)
            {
                session.AdvanceFrame(Inputs.None);
            }

            // 入力遅延の2ティック＋予測の上限3ティックまでは進める。
            Assert.That(session.CurrentTick, Is.EqualTo(5));
            Assert.Throws<InvalidOperationException>(() => session.AdvanceFrame(Inputs.None));
        }

        [Test]
        public void RemoteInputsAheadOfTheLocalWorld_AreKeptUntilSimulated()
        {
            // 遅れて始めた側（T2-7 で見つかった）: 始める前に、相手の先のティックまでの入力がまとめて届く。
            // 確定ティックが今のティックより先にあっても、まだ計算していないティックの入力を捨ててはいけない。
            SimWorld world = TwoPlayerWorld();
            var session = new RollbackSession(world, 1, new RollbackSettings { InputDelayTicks = 2, MaxPredictionTicks = 8 });
            for (int tick = 2; tick < 10; tick++)
            {
                session.AddRemoteInput(0, tick, Inputs.Right);
            }

            SimWorld reference = TwoPlayerWorld();
            for (int frame = 0; frame < 8; frame++)
            {
                session.AdvanceFrame(Inputs.None);
                reference.Step(new[] { frame >= 2 ? Inputs.Right : Inputs.None, Inputs.None });
            }

            Assert.That(world.Checksum(), Is.EqualTo(reference.Checksum()));
            Assert.That(session.RollbackCount, Is.EqualTo(0), "入力が先に届いているので、予測も巻き戻しもいらない");
        }

        private static SimWorld TwoPlayerWorld()
        {
            var world = new SimWorld(TestArenas.Playground());
            world.AddCharacter(new CharacterConfig(), PseudoNetwork.Spawns[0]);
            world.AddCharacter(new CharacterConfig(), PseudoNetwork.Spawns[1]);
            return world;
        }
    }
}
