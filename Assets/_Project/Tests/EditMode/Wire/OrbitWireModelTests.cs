using System.Numerics;
using Degunatsu.Core.Input;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Tests.Support;
using Degunatsu.Core.Wire;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Wire
{
    /// <summary>
    /// タイプA の試行版（円運動、D-11）のモデルと、回る向きの決め方。
    /// </summary>
    public class OrbitWireModelTests
    {
        private static CharacterConfig OrbitConfig()
        {
            return WireTestSetup.LongAttachConfig(WireModelType.Orbit);
        }

        [TestCase(1)]
        [TestCase(-1)]
        public void Attach_TurnsTowardTheFacingDirection_OnACircle(int facing)
        {
            // 真上の天井に接続する。照準は真上（左右の入力がないので向きは変わらない）。
            BoxTerrain world = new BoxTerrain().AddBox(-20f, 20f, 20f, 21f, attachable: true);
            var harness = new SimHarness(world, OrbitConfig(), new Vector3(0f, 12f, 0f), facing);

            harness.StepUntil(s => s.Wire.Phase == WirePhase.Attached, Inputs.Wire(0f, 1f), 20);
            Assert.That(harness.State.Wire.SwingDirection, Is.EqualTo(facing), "接続点が真上なら、右向きは反時計回り、左向きは時計回り");

            float rope = harness.State.Wire.RopeLength;
            for (int i = 0; i < 30; i++)
            {
                harness.Step(Inputs.Wire());
                float distance = WireTestSetup.DistanceToAnchor(harness.State);
                Assert.That(distance, Is.EqualTo(rope).Within(0.02f), $"ティック {i}: 円の上にいない");
            }
            Assert.That(harness.State.Position.X * facing, Is.GreaterThan(3f), "向いている方向へ回っていない");
        }

        [Test]
        public void KeepsRadius_AndReachesOrbitSpeed()
        {
            var config = OrbitConfig();
            var harness = new SimHarness(new BoxTerrain(), config, new Vector3(0f, 20f, 0f));
            WireTestSetup.AttachAt(harness, new Vector3(-6f, 12f, 0f), new Vector3(0f, 20f, 0f));

            for (int i = 0; i < 30; i++)
            {
                harness.Step(Inputs.Wire());
                Assert.That(WireTestSetup.DistanceToAnchor(harness.State), Is.EqualTo(10f).Within(0.02f), $"ティック {i}");
            }
            Assert.That(harness.State.Velocity.Length(), Is.EqualTo(config.Wire.Orbit.OrbitSpeed).Within(0.05f));
        }

        [Test]
        public void SwingDirection_DoesNotChangeWhenFacingChanges()
        {
            var harness = new SimHarness(new BoxTerrain(), OrbitConfig(), new Vector3(0f, 20f, 0f));
            WireTestSetup.AttachAt(harness, new Vector3(-6f, 12f, 0f), new Vector3(0f, 20f, 0f), swingDirection: 1);

            for (int i = 0; i < 40; i++)
            {
                harness.Step(Inputs.Wire(-1f, 0f)); // 左に入力して、向きを左に変える
                CharacterState s = harness.State;
                Vector3 fromAnchor = s.Position - s.Wire.Anchor;
                float angularSign = fromAnchor.X * s.Velocity.Y - fromAnchor.Y * s.Velocity.X;
                Assert.That(s.Facing, Is.EqualTo(-1));
                Assert.That(s.Wire.SwingDirection, Is.EqualTo(1));
                Assert.That(angularSign, Is.GreaterThan(0f), $"ティック {i}: 反時計回りでない");
            }
        }

        [Test]
        public void Stick_AlongTheMotionIsFaster_AgainstIsSlower()
        {
            // 接続点の真下から反時計回り（右へ）に回る。
            float along = OrbitSpeedAfter(Inputs.Wire(1f, 0f));
            float neutral = OrbitSpeedAfter(Inputs.Wire());
            float against = OrbitSpeedAfter(Inputs.Wire(-1f, 0f));

            var config = OrbitConfig();
            Assert.That(neutral, Is.EqualTo(config.Wire.Orbit.OrbitSpeed).Within(0.05f));
            Assert.That(along, Is.GreaterThan(neutral + 2f));
            Assert.That(against, Is.LessThan(neutral - 2f));
            Assert.That(along, Is.LessThanOrEqualTo(config.Wire.Orbit.OrbitSpeed * (1f + config.Wire.AttachedInputInfluence) + 0.05f));
        }

        [Test]
        public void Stick_WithZeroInfluence_DoesNotChangeSpeed()
        {
            float along = OrbitSpeedAfter(Inputs.Wire(1f, 0f), influence: 0f);
            float against = OrbitSpeedAfter(Inputs.Wire(-1f, 0f), influence: 0f);

            Assert.That(along, Is.EqualTo(against).Within(1e-3f));
        }

        [Test]
        public void SwingDirectionFor_PicksTheTangentTowardFacing()
        {
            var below = new Vector3(0f, 0f, 0f);
            var above = new Vector3(0f, 10f, 0f);

            // 接続点が上: 右向きは反時計回り（右へ）、左向きは時計回り（左へ）。
            Assert.That(WireController.SwingDirectionFor(below, above, 1), Is.EqualTo(1));
            Assert.That(WireController.SwingDirectionFor(below, above, -1), Is.EqualTo(-1));
            // 接続点が下: 右向きは時計回り（右へ）。
            Assert.That(WireController.SwingDirectionFor(above, below, 1), Is.EqualTo(-1));
            Assert.That(WireController.SwingDirectionFor(above, below, -1), Is.EqualTo(1));
        }

        [TestCase(1)]
        [TestCase(-1)]
        public void SwingDirectionFor_HorizontalRope_TurnsDownward(int facing)
        {
            var position = new Vector3(0f, 5f, 0f);

            // 接続点が右: 反時計回りが下向き。接続点が左: 時計回りが下向き。
            Assert.That(WireController.SwingDirectionFor(position, new Vector3(6f, 5f, 0f), facing), Is.EqualTo(1));
            Assert.That(WireController.SwingDirectionFor(position, new Vector3(-6f, 5f, 0f), facing), Is.EqualTo(-1));
        }

        private static float OrbitSpeedAfter(PlayerInput input, float influence = -1f)
        {
            var config = OrbitConfig();
            if (influence >= 0f)
            {
                config.Wire.AttachedInputInfluence = influence;
            }
            var harness = new SimHarness(new BoxTerrain(), config, new Vector3(0f, 20f, 0f));
            WireTestSetup.AttachAt(harness, new Vector3(0f, 10f, 0f), new Vector3(0f, 20f, 0f), swingDirection: 1);

            // 15 ティック（約 0.25 秒）で、回る方向はまだほぼ右向き。
            harness.Step(input, 15);
            return harness.State.Velocity.Length();
        }
    }
}
