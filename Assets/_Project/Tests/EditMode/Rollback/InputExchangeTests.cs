using Degunatsu.Core.Input;
using Degunatsu.Core.Rollback;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Tests.Support;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Rollback
{
    /// <summary>入力の送受信の規則（冗長送信と確認応答。docs/10_online_plan.md T2-4）。</summary>
    public class InputExchangeTests
    {
        private static readonly RollbackSettings Settings = new RollbackSettings { InputDelayTicks = 2, MaxPredictionTicks = 8 };

        [Test]
        public void UnackedInputsAreResent_UntilTheRemoteAcknowledgesThem()
        {
            InputExchange a = Create(0);
            InputExchange b = Create(1);
            for (int i = 0; i < 3; i++)
            {
                a.AdvanceFrame(Inputs.Right);
            }

            // 確認されるまでは、毎回すべての入力を送る（ティック 2, 3, 4）。
            InputMessage first = a.BuildMessage(1);
            InputMessage second = a.BuildMessage(1);
            Assert.That(first.StartTick, Is.EqualTo(2));
            Assert.That(first.Inputs.Count, Is.EqualTo(3));
            Assert.That(second.Inputs.Count, Is.EqualTo(3));

            // b が受け取り、確認応答を返すと、a はそれより後の入力だけを送る。
            b.Receive(first);
            Assert.That(b.Session.ContiguousTick(0), Is.EqualTo(4));
            a.Receive(b.BuildMessage(0));
            a.AdvanceFrame(Inputs.Left);

            InputMessage third = a.BuildMessage(1);
            Assert.That(third.StartTick, Is.EqualTo(5));
            Assert.That(third.Inputs, Is.EqualTo(new[] { Inputs.Left }));
            Assert.That(a.UnackedCount, Is.EqualTo(1), "確認された入力は捨てる");
        }

        [Test]
        public void LostOrReorderedMessages_AreRecovered()
        {
            InputExchange a = Create(0);
            InputExchange b = Create(1);

            a.AdvanceFrame(Inputs.Jump);
            InputMessage lost = a.BuildMessage(1); // 届かなかった
            a.AdvanceFrame(Inputs.Right);
            InputMessage newer = a.BuildMessage(1);

            b.Receive(newer);
            b.Receive(lost); // 遅れて届いた古いメッセージ（同じ入力の再送なので無視される）
            b.Receive(newer); // 重複

            Assert.That(b.Session.ContiguousTick(0), Is.EqualTo(3));
            Assert.That(b.Session.TryGetInput(0, 2, out PlayerInput atTwo) && atTwo.Equals(Inputs.Jump), Is.True);
            Assert.That(b.Session.TryGetInput(0, 3, out PlayerInput atThree) && atThree.Equals(Inputs.Right), Is.True);
        }

        [Test]
        public void MessageSize_IsLimited_WhenTheRemoteNeverAcknowledges()
        {
            // 予測の上限を大きくして、確認応答のないまま長く進める。
            var settings = new RollbackSettings { InputDelayTicks = 2, MaxPredictionTicks = 400 };
            InputExchange a = Create(0, settings);
            for (int i = 0; i < 300; i++)
            {
                a.AdvanceFrame(Inputs.Right);
            }

            InputMessage message = a.BuildMessage(1);

            Assert.That(message.Inputs.Count, Is.EqualTo(InputMessage.MaxInputs));
            Assert.That(message.StartTick, Is.EqualTo(2), "古い入力から送る（受け手が順に確定できるように）");
        }

        [Test]
        public void PseudoNetwork_KeepsTheResendQueueSmall()
        {
            var network = new PseudoNetwork(4, Settings, latencyFrames: 4, jitterFrames: 3, lossRate: 0.1, seed: 11);

            network.RunUntil(300, maxFrames: 3000);

            foreach (InputExchange exchange in network.Exchanges)
            {
                Assert.That(exchange.UnackedCount, Is.LessThan(40), "確認された入力が捨てられていない");
            }
            Assert.That(network.SentBytes / (double)network.SentMessages, Is.LessThan(16 + 6 * 20), "1つのメッセージが大きすぎる");
        }

        private static InputExchange Create(int localPlayer, RollbackSettings settings = null)
        {
            var world = new SimWorld(TestArenas.Playground());
            world.AddCharacter(new CharacterConfig(), PseudoNetwork.Spawns[0]);
            world.AddCharacter(new CharacterConfig(), PseudoNetwork.Spawns[1]);
            settings = settings ?? Settings;
            return new InputExchange(new RollbackSession(world, localPlayer, settings), settings);
        }
    }
}
