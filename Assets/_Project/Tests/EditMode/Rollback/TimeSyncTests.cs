using Degunatsu.Core.Rollback;
using Degunatsu.Core.Tests.Support;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Rollback
{
    /// <summary>時刻の同期（docs/10_online_plan.md T2-6）。</summary>
    public class TimeSyncTests
    {
        [Test]
        public void Estimate_CancelsTheLatency()
        {
            // 自分は相手より常に10ティック先、片道の遅れは5ティック。
            // 自分から見た差: 自分のティック − 届いたメッセージの送り手のティック = 10 + 5 = 15
            // 相手から見た差: 相手のティック − 相手が知っている自分の最新のティック = -10 + 5 = -5
            var settings = new RollbackSettings();
            var sync = new TimeSync(2, 0, 0, settings);
            for (int i = 0; i < 20; i++)
            {
                sync.OnMessage(1, 115 + i, 100 + i, -5);
            }

            Assert.That(sync.TicksAhead, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(sync.RecommendWait(200), Is.EqualTo(settings.MaxPredictionTicks), "待つのは予測の上限まで");
            Assert.That(sync.RecommendWait(201), Is.EqualTo(0), "次の判断は間隔をあけてから");
        }

        [TestCase(3, 0, 0.0, 10, TestName = "10%速い、遅延3")]
        [TestCase(6, 2, 0.05, 100, TestName = "1%速い、遅延6、揺らぎ、欠落5%")]
        public void FasterPeer_WaitsInsteadOfHittingThePredictionLimit(int latency, int jitter, double loss, int extraStepEvery)
        {
            PseudoNetwork without = Run(latency, jitter, loss, extraStepEvery, useTimeSync: false);
            PseudoNetwork with = Run(latency, jitter, loss, extraStepEvery, useTimeSync: true);

            Assert.That(with.TimeSyncWaitFrames, Is.GreaterThan(0), "前提: 時刻の同期で待っている");
            Assert.That(with.StalledFramesOf(0), Is.LessThan(without.StalledFramesOf(0) / 3 + 1), "予測の上限で止まる回数が減っていない");
            Assert.That(with.AverageTickSpread, Is.LessThan(without.AverageTickSpread), "進み具合の差が縮んでいない");
        }

        [Test]
        public void SameSpeed_DoesNotWait()
        {
            PseudoNetwork network = Run(4, 3, 0.1, extraStepEvery: 0, useTimeSync: true);

            Assert.That(network.TimeSyncWaitFrames, Is.LessThan(network.Frame / 100), "同じ速さなのに待っている");
        }

        private static PseudoNetwork Run(int latency, int jitter, double loss, int extraStepEvery, bool useTimeSync)
        {
            var network = new PseudoNetwork(4, new RollbackSettings(), latency, jitter, loss, seed: 3)
            {
                FastPeer = 0,
                ExtraStepEvery = extraStepEvery,
                UseTimeSync = useTimeSync,
            };
            network.RunUntil(1200, maxFrames: 6000);
            ulong expected = network.Reference(1200).Checksum();
            foreach (Degunatsu.Core.Simulation.SimWorld world in network.Worlds)
            {
                Assert.That(world.Checksum(), Is.EqualTo(expected), "全員が同じ状態にならない");
            }
            Assert.That(network.ChecksumMismatches, Is.EqualTo(0));
            return network;
        }
    }
}
