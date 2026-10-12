using System;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Tests.Support;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Rollback
{
    /// <summary>ワールドの全状態の保存と復元（docs/10_online_plan.md T2-1）。</summary>
    public class WorldSnapshotTests
    {
        [Test]
        public void LoadAfterStepping_RestoresTickAndAllStates()
        {
            SimWorld world = TwoPlayers();
            var inputs = new[] { Inputs.Right, Inputs.Wire(0.5f, 1f) };
            world.Step(inputs);
            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);
            ulong saved = world.Checksum();
            CharacterState first = world.GetState(0);

            for (int i = 0; i < 30; i++)
            {
                world.Step(inputs);
            }
            Assert.That(world.Checksum(), Is.Not.EqualTo(saved), "前提: 状態が変わった");

            world.LoadFrom(snapshot);

            Assert.That(world.Tick, Is.EqualTo(1));
            Assert.That(world.GetState(0), Is.EqualTo(first));
            Assert.That(world.Checksum(), Is.EqualTo(saved));
            Assert.That(snapshot.Checksum(), Is.EqualTo(saved));
        }

        [Test]
        public void ReplayingFromSnapshot_GivesTheSameResult()
        {
            SimWorld world = TwoPlayers();
            var inputs = new[] { Inputs.RightJump, Inputs.Wire(-0.3f, 1f) };
            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);
            for (int i = 0; i < 60; i++)
            {
                world.Step(inputs);
            }
            ulong first = world.Checksum();

            world.LoadFrom(snapshot);
            for (int i = 0; i < 60; i++)
            {
                world.Step(inputs);
            }

            Assert.That(world.Checksum(), Is.EqualTo(first));
        }

        [Test]
        public void Checksum_DependsOnTheTick()
        {
            SimWorld world = TwoPlayers();
            ulong before = world.Checksum();
            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);

            world.Step(new[] { Inputs.None, Inputs.None });
            world.LoadFrom(snapshot);

            Assert.That(world.Checksum(), Is.EqualTo(before));
        }

        [Test]
        public void LoadFrom_DifferentCharacterCount_Throws()
        {
            var snapshot = new WorldSnapshot();
            TwoPlayers().SaveTo(snapshot);
            var single = new SimWorld(TestArenas.Ground());
            single.AddCharacter(new CharacterConfig(), PseudoNetwork.Spawns[0]);

            Assert.Throws<ArgumentException>(() => single.LoadFrom(snapshot));
        }

        private static SimWorld TwoPlayers()
        {
            var world = new SimWorld(TestArenas.Playground());
            world.AddCharacter(new CharacterConfig(), PseudoNetwork.Spawns[0]);
            world.AddCharacter(new CharacterConfig(), PseudoNetwork.Spawns[1]);
            return world;
        }
    }
}
