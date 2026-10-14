using System.Collections.Generic;
using Airerage.Core.Input;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;
using NVector3 = System.Numerics.Vector3;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// シーンのコライダーから作った地形で、Core のテストと同じ動きになることを確かめる（シーン → BoxTerrain → SimWorld の通し）。
    /// </summary>
    public sealed class SimulationParityTests
    {
        // コライダーの位置と大きさから矩形を計算し直すときの浮動小数点の誤差だけを許す。
        private const float PositionTolerance = 1e-4f;

        private BoxTerrain _reference;
        private ColliderWorld _world;
        private BoxTerrain _built;

        [SetUp]
        public void SetUp()
        {
            _reference = TestArenas.Playground();
            _world = ColliderWorld.From(_reference);
            var errors = new List<string>();
            _built = _world.Build(errors);
            Assert.That(errors, Is.Empty);
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
        }

        [Test]
        public void RunIntoThinWallAndJump_SameTrajectory()
        {
            var script = new List<PlayerInput>();
            Repeat(script, Inputs.Right, 30);
            Repeat(script, Inputs.RightJump, 5);
            Repeat(script, Inputs.Right, 80);
            Repeat(script, Inputs.None, 30);
            AssertSameTrajectory(new NVector3(4f, 0.91f, 0f), script);
        }

        [Test]
        public void WireToCeiling_SwingAndRelease_SameTrajectory()
        {
            var script = new List<PlayerInput>();
            Repeat(script, Inputs.Right, 20);
            Repeat(script, Inputs.Wire(0.5f, 1f), 90);
            Repeat(script, Inputs.Right, 60);
            AssertSameTrajectory(new NVector3(-6f, 0.91f, 0f), script);
        }

        [Test]
        public void WireWithAimAssistToAnchor_SameTrajectory()
        {
            var script = new List<PlayerInput>();
            Repeat(script, Inputs.Wire(0.4f, 1f), 100);
            Repeat(script, Inputs.None, 60);
            AssertSameTrajectory(new NVector3(0f, 0.91f, 0f), script);
        }

        private void AssertSameTrajectory(NVector3 spawn, IReadOnlyList<PlayerInput> script)
        {
            var reference = new SimWorld(_reference);
            var scene = new SimWorld(_built);
            reference.AddCharacter(new CharacterConfig(), spawn);
            scene.AddCharacter(new CharacterConfig(), spawn);

            var inputs = new PlayerInput[1];
            for (int tick = 0; tick < script.Count; tick++)
            {
                inputs[0] = script[tick];
                reference.Step(inputs);
                scene.Step(inputs);

                CharacterState expected = reference.GetState(0);
                CharacterState actual = scene.GetState(0);
                float error = NVector3.Distance(expected.Position, actual.Position);
                if (error > PositionTolerance || expected.Wire.Phase != actual.Wire.Phase || expected.Grounded != actual.Grounded)
                {
                    Assert.Fail($"{tick} ティック目で動きがずれた（位置の差 {error}）\n期待: {expected}\n実際（シーンの地形）: {actual}");
                }
            }
        }

        private static void Repeat(List<PlayerInput> script, PlayerInput input, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                script.Add(input);
            }
        }
    }
}
