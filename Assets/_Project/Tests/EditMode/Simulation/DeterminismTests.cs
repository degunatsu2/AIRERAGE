using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Input;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using Airerage.Core.Wire;
using NUnit.Framework;

namespace Airerage.Core.Tests.Simulation
{
    /// <summary>
    /// 同じ入力列からは同じ状態が得られること（ネットワークの予測・入力の記録と再生の前提）。
    /// </summary>
    public class DeterminismTests
    {
        [TestCase(WireModelType.Inertial)]
        [TestCase(WireModelType.Responsive)]
        public void SameInputSequence_ProducesIdenticalStates(WireModelType modelType)
        {
            List<CharacterState> first = Run(modelType);
            List<CharacterState> second = Run(modelType);

            Assert.That(first.Exists(s => s.Wire.Phase == WirePhase.Attached), Is.True, "前提: 入力列の中でワイヤーが接続している");
            Assert.That(second.Count, Is.EqualTo(first.Count));
            for (int i = 0; i < first.Count; i++)
            {
                Assert.That(second[i], Is.EqualTo(first[i]), $"ティック {i} で状態が一致しません");
            }
        }

        private static List<CharacterState> Run(WireModelType modelType)
        {
            BoxTerrain world = TestArenas.Playground();
            var config = new CharacterConfig();
            config.Wire.ModelType = modelType;
            var harness = new SimHarness(world, config, new Vector3(-6f, 0.91f, 0f));

            var states = new List<CharacterState>();
            foreach (PlayerInput input in ScriptedInputs())
            {
                harness.Step(input);
                states.Add(harness.State);
            }
            return states;
        }

        private static IEnumerable<PlayerInput> ScriptedInputs()
        {
            for (int i = 0; i < 20; i++) yield return Inputs.Right;
            yield return Inputs.RightJump;
            for (int i = 0; i < 10; i++) yield return Inputs.Right;
            for (int i = 0; i < 90; i++) yield return Inputs.Wire(0.5f, 1f);
            for (int i = 0; i < 30; i++) yield return Inputs.Left;
            for (int i = 0; i < 60; i++) yield return Inputs.Wire(-0.7f, 0.7f);
            for (int i = 0; i < 60; i++) yield return Inputs.None;
        }
    }
}
