using System;
using System.Numerics;
using Degunatsu.Core.Common;
using Degunatsu.Core.Input;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Tests.Support;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Simulation
{
    public class SimWorldTests
    {
        [Test]
        public void Step_EmptyWorld_AdvancesTickWithoutErrors()
        {
            var sim = new SimWorld(new BoxTerrain());
            sim.AddCharacter(new CharacterConfig(), new Vector3(0f, 10f, 0f));

            var inputs = new[] { new PlayerInput(0, 0, InputButtons.None) };
            for (int i = 0; i < 100; i++)
            {
                sim.Step(inputs);
            }

            Assert.That(sim.Tick, Is.EqualTo(100));
            Assert.That(sim.GetState(0).Position.Y, Is.LessThan(10f), "重力で落下しているはず");
        }

        [Test]
        public void Step_InputCountMismatch_Throws()
        {
            var sim = new SimWorld(new BoxTerrain());
            sim.AddCharacter(new CharacterConfig(), Vector3.Zero);
            sim.AddCharacter(new CharacterConfig(), Vector3.Zero);

            Assert.Throws<ArgumentException>(() => sim.Step(new[] { new PlayerInput(0, 0, InputButtons.None) }));
        }

        [Test]
        public void Step_TwoCharacters_UseTheirOwnInputs()
        {
            var world = new BoxTerrain().AddBox(-50f, -1f, 50f, 0f);
            var sim = new SimWorld(world);
            sim.AddCharacter(new CharacterConfig(), new Vector3(-5f, 0.91f, 0f));
            sim.AddCharacter(new CharacterConfig(), new Vector3(5f, 0.91f, 0f));

            var inputs = new[] { Inputs.Left, Inputs.Right };
            for (int i = 0; i < 30; i++)
            {
                sim.Step(inputs);
            }

            Assert.That(sim.GetState(0).Position.X, Is.LessThan(-5f));
            Assert.That(sim.GetState(1).Position.X, Is.GreaterThan(5f));
        }

        [Test]
        public void AddCharacter_InvalidTuning_ThrowsWithFieldName()
        {
            var sim = new SimWorld(new BoxTerrain());
            var config = new CharacterConfig();
            config.Movement.RunSpeed = -1f;
            config.Wire.MinRopeLength = 20f;

            var exception = Assert.Throws<InvalidTuningException>(() => sim.AddCharacter(config, Vector3.Zero));
            StringAssert.Contains("RunSpeed", exception.Message);
            StringAssert.Contains("MinRopeLength", exception.Message);
        }
    }

    public class PlayerInputTests
    {
        [Test]
        public void FromAxes_QuantizesAndClamps()
        {
            var input = PlayerInput.FromAxes(2f, -0.5f, InputButtons.Jump);

            Assert.That(input.MoveX, Is.EqualTo((sbyte)127));
            Assert.That(input.MoveY, Is.EqualTo((sbyte)-64));
            Assert.That(input.IsHeld(InputButtons.Jump), Is.True);
            Assert.That(input.IsHeld(InputButtons.Wire), Is.False);
        }

        [Test]
        public void Move_DiagonalIsNormalizedToUnitLength()
        {
            var input = PlayerInput.FromAxes(1f, 1f, InputButtons.None);

            Assert.That(input.Move.Length(), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void InputFrame_DetectsPressAndRelease()
        {
            var frame = new InputFrame(new PlayerInput(0, 0, InputButtons.Wire), InputButtons.Jump);

            Assert.That(frame.WasPressed(InputButtons.Wire), Is.True);
            Assert.That(frame.WasPressed(InputButtons.Jump), Is.False);
            Assert.That(frame.Released, Is.EqualTo(InputButtons.Jump));
        }
    }
}
