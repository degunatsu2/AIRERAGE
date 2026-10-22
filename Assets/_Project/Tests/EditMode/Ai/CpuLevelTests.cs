using System.Numerics;
using Airerage.Core.Ai;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Ai
{
    /// <summary>
    /// CPU の強さ 4 段階（D-71。ユーザーの決定、2026-10-22: EASY 1・NORMAL 3・HARD 6・HELL 10 点くらい）: 1 対 1 で戦わせると、
    /// 強い段階ほどはっきり多く当てる。HELL は当たった攻撃をキャンセルしてつなぎ、ジャストガードを狙う。
    /// </summary>
    public class CpuLevelTests
    {
        private const int Seeds = 4;

        [TestCase(CpuLevel.Easy, CpuLevel.Normal)]
        [TestCase(CpuLevel.Normal, CpuLevel.Hard)]
        [TestCase(CpuLevel.Hard, CpuLevel.Hell)]
        [TestCase(CpuLevel.Easy, CpuLevel.Hell)]
        public void StrongerLevel_LandsClearlyMoreHits(CpuLevel weaker, CpuLevel stronger)
        {
            int weakHits = 0;
            int strongHits = 0;
            for (int seed = 0; seed < Seeds; seed++)
            {
                Stats[] stats = Duel(weaker, stronger, seed);
                weakHits += stats[0].Hits;
                strongHits += stats[1].Hits;
            }
            TestContext.WriteLine($"{weaker} {weakHits} : {stronger} {strongHits}（当てた数。40 秒 × {Seeds} 回）");
            Assert.That(strongHits, Is.GreaterThan(weakHits * 1.3f), $"{stronger} が {weaker} よりはっきり強くない");
        }

        [Test]
        public void Hell_CancelsOnHit_AndJustGuards()
        {
            int cancels = 0;
            int justGuards = 0;
            int hardCancels = 0;
            for (int seed = 0; seed < Seeds; seed++)
            {
                Stats[] stats = Duel(CpuLevel.Hard, CpuLevel.Hell, seed);
                hardCancels += stats[0].Cancels;
                cancels += stats[1].Cancels;
                justGuards += stats[1].JustGuards;
            }
            TestContext.WriteLine($"HELL: キャンセル {cancels}、ジャストガード {justGuards}（HARD のキャンセル {hardCancels}）");
            Assert.That(cancels, Is.GreaterThan(hardCancels), "HELL が当たった攻撃をつながない");
            Assert.That(justGuards, Is.GreaterThan(0), "HELL がジャストガードしない");
        }

        private struct Stats
        {
            public int Hits;
            public int Cancels;
            public int JustGuards;
        }

        // 遊び場で 1 対 1 を 40 秒（seed で出現位置と乱数を変える）。
        private static Stats[] Duel(CpuLevel first, CpuLevel second, int seed)
        {
            var world = new SimWorld(TestArenas.Playground(), new StageBounds(-29f, -10f, 29f, 30f), null, MatchRules.FreePlay());
            float offset = (seed % 3) * 4f;
            world.AddCharacter(new CharacterConfig(), new Vector3(-10f + offset, 0.91f, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(10f - offset, 0.91f, 0f), -1);
            var cpus = new[] { new CpuPlayer(0, CpuTuning.For(first), salt: seed * 2), new CpuPlayer(1, CpuTuning.For(second), salt: seed * 2 + 1) };
            var stats = new Stats[2];
            var inputs = new PlayerInput[2];
            var cancelUsed = new bool[2];
            for (int t = 0; t < 2400; t++)
            {
                inputs[0] = cpus[0].Think(world);
                inputs[1] = cpus[1].Think(world);
                world.Step(inputs);
                foreach (HitRecord hit in world.LastHits)
                {
                    if (hit.Result == HitResult.Hit)
                    {
                        stats[hit.Attacker].Hits++;
                    }
                }
                for (int i = 0; i < 2; i++)
                {
                    bool used = world.GetState(i).Combat.AttackCancelUsed;
                    stats[i].Cancels += used && !cancelUsed[i] ? 1 : 0;
                    cancelUsed[i] = used;
                }
            }
            for (int i = 0; i < 2; i++)
            {
                stats[i].JustGuards = world.GetState(i).Combat.JustGuardCount;
            }
            return stats;
        }
    }
}
