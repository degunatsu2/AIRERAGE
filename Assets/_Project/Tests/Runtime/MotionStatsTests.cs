using Airerage.Core.Simulation;
using Airerage.Core.Wire;
using Airerage.Runtime.DebugTools;
using NUnit.Framework;
using NVector3 = System.Numerics.Vector3;

namespace Airerage.Runtime.Tests
{
    public sealed class MotionStatsTests
    {
        private static CharacterState State(float vx, float vy, WirePhase phase)
        {
            return new CharacterState
            {
                Velocity = new NVector3(vx, vy, 0f),
                Wire = new WireState { Phase = phase },
            };
        }

        [Test]
        public void TracksCurrentAndMaxSpeed()
        {
            var stats = new MotionStats();

            stats.Step(State(3f, 4f, WirePhase.Ready));   // 5 m/s
            stats.Step(State(6f, 8f, WirePhase.Ready));   // 10 m/s
            stats.Step(State(1f, 0f, WirePhase.Ready));   // 1 m/s

            Assert.That(stats.CurrentSpeed, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(stats.MaxSpeed, Is.EqualTo(10f).Within(1e-5f));
        }

        [Test]
        public void RecordsSpeedOnTheTickTheWireIsReleased()
        {
            var stats = new MotionStats();
            stats.Step(State(0f, 0f, WirePhase.Attached));
            Assert.That(stats.LastExitSpeed.HasValue, Is.False);

            stats.Step(State(12f, 5f, WirePhase.Ready)); // 解除したティック（13 m/s）
            stats.Step(State(2f, 0f, WirePhase.Ready));

            Assert.That(stats.LastExitSpeed.HasValue, Is.True);
            Assert.That(stats.LastExitSpeed.Value, Is.EqualTo(13f).Within(1e-5f));
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            var stats = new MotionStats();
            stats.Step(State(0f, 0f, WirePhase.Attached));
            stats.Step(State(10f, 0f, WirePhase.Ready));

            stats.Reset();

            Assert.That(stats.MaxSpeed, Is.EqualTo(0f));
            Assert.That(stats.LastExitSpeed.HasValue, Is.False);
        }
    }
}
