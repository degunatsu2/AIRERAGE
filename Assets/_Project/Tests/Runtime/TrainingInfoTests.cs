using Airerage.Core.Combat;
using Airerage.Core.Skills;
using Airerage.Runtime.DebugTools;
using NUnit.Framework;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// トレーニングの表示の発生・持続・後隙（DebugHud.FramesOf。ユーザーの要望、2026-10-30）。技の性能表（docs/17）と同じ数え方:
    /// 発生は出したフレームを 1 とした最初の判定、持続は判定が出ているフレームの数、後隙は最後の判定が消えてから終わりまで。
    /// </summary>
    public sealed class TrainingInfoTests
    {
        [Test]
        public void FramesOf_CountsStartupActiveAndRecovery()
        {
            var attack = new AttackData
            {
                TotalTicks = 20,
                Hits = new[]
                {
                    new AttackHit { StartTick = 4, ActiveTicks = 3 },
                    new AttackHit { StartTick = 10, ActiveTicks = 2 },
                },
            };
            DebugHud.FramesOf(attack, 0, out int startup, out int active, out int recovery);
            Assert.That(startup, Is.EqualTo(5));
            Assert.That(active, Is.EqualTo(5), "2 つの判定の出ているフレームの合計");
            Assert.That(recovery, Is.EqualTo(8), "最後の判定が消えて（12）から終わり（20）まで");
        }

        [Test]
        public void FramesOf_CountsOverlappingHitsOnce()
        {
            // 前と後ろに同時に出る判定は、1 回と数える。
            var attack = new AttackData
            {
                TotalTicks = 15,
                Hits = new[]
                {
                    new AttackHit { StartTick = 3, ActiveTicks = 4 },
                    new AttackHit { StartTick = 3, ActiveTicks = 4 },
                },
            };
            DebugHud.FramesOf(attack, 0, out int startup, out int active, out int recovery);
            Assert.That(startup, Is.EqualTo(4));
            Assert.That(active, Is.EqualTo(4));
            Assert.That(recovery, Is.EqualTo(8));
        }

        [Test]
        public void FramesOf_ProjectileOnlySkill_ShowsWhenItFires()
        {
            var attack = new AttackData { TotalTicks = 30, Hits = new AttackHit[0], Effect = SkillEffect.Missile, EffectTick = 9 };
            DebugHud.FramesOf(attack, 0, out int startup, out int active, out int recovery);
            Assert.That(startup, Is.EqualTo(10), "物が出るフレーム");
            Assert.That(active, Is.EqualTo(-1));
            Assert.That(recovery, Is.EqualTo(-1));
        }

        [Test]
        public void Labels_AreTheSixRequestedItems()
        {
            Assert.That(DebugHud.Labels, Is.EqualTo(new[] { "ダメージ", "吹っ飛ばし力", "速度", "発生", "持続", "後隙" }));
        }
    }
}
