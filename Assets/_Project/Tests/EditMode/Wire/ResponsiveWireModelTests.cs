using System.Numerics;
using Degunatsu.Core.Input;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Tests.Support;
using Degunatsu.Core.Wire;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Wire
{
    /// <summary>
    /// タイプB（操作追従重視）のモデル（docs/06_prototype_plan.md T1-5）。
    /// </summary>
    public class ResponsiveWireModelTests
    {
        [Test]
        public void FromRest_ReachesAnchorWithinExpectedTicks()
        {
            var config = WireTestSetup.LongAttachConfig(WireModelType.Responsive);
            var harness = new SimHarness(new BoxTerrain(), config, new Vector3(0f, 20f, 0f));
            WireTestSetup.AttachAt(harness, new Vector3(0f, 20f, 0f), new Vector3(8f, 20f, 0f));

            var tuning = config.Wire.Responsive;
            // 加速にかかる時間＋残りを最高速で進む時間。
            float accelerateSeconds = tuning.PullSpeed / tuning.PullAcceleration;
            float accelerateDistance = 0.5f * tuning.PullSpeed * accelerateSeconds;
            float cruiseSeconds = (8f - tuning.ArrivalRadius - accelerateDistance) / tuning.PullSpeed;
            int expectedTicks = (int)System.MathF.Ceiling((accelerateSeconds + cruiseSeconds) * 60f);

            int ticks = harness.StepUntil(
                s => WireTestSetup.DistanceToAnchor(s) <= tuning.ArrivalRadius + 0.01f, Inputs.Wire(), 120);

            Assert.That(ticks, Is.LessThanOrEqualTo(expectedTicks + 3));
        }

        [Test]
        public void Pull_SpeedStaysWithinPullAndSteerSpeedIncludingInputInfluence()
        {
            var config = WireTestSetup.LongAttachConfig(WireModelType.Responsive);
            var harness = new SimHarness(new BoxTerrain(), config, new Vector3(0f, 20f, 0f));
            WireTestSetup.AttachAt(harness, new Vector3(-10f, 15f, 0f), new Vector3(0f, 20f, 0f));

            var tuning = config.Wire.Responsive;
            // スティックを接続点の方向に入れると、牽引の速さは最大で (1 + AttachedInputInfluence) 倍になる。
            float maxPullSpeed = tuning.PullSpeed * (1f + config.Wire.AttachedInputInfluence);
            float limit = Vector2.Distance(Vector2.Zero, new Vector2(maxPullSpeed, tuning.SteerSpeed)) + 0.01f;
            for (int i = 0; i < 60; i++)
            {
                harness.Step(Inputs.Wire(0f, 1f));
                Assert.That(harness.State.Velocity.Length(), Is.LessThanOrEqualTo(limit), $"ティック {i}");
            }
        }

        [Test]
        public void AnchorUpperLeft_StickLeftOrUpIsFaster_RightOrDownIsSlower()
        {
            // ユーザーの例（2026-10-12）: 左上の接続点に引かれているとき、右・下の入力で少し遅く、左・上の入力で少し速くなる。
            float neutral = PullSpeedAfter(Inputs.Wire());
            Assert.That(PullSpeedAfter(Inputs.Wire(-1f, 0f)), Is.GreaterThan(neutral + 1f), "左");
            Assert.That(PullSpeedAfter(Inputs.Wire(0f, 1f)), Is.GreaterThan(neutral + 1f), "上");
            Assert.That(PullSpeedAfter(Inputs.Wire(1f, 0f)), Is.LessThan(neutral - 1f), "右");
            Assert.That(PullSpeedAfter(Inputs.Wire(0f, -1f)), Is.LessThan(neutral - 1f), "下");
        }

        [Test]
        public void Arrived_HangsInPlaceWithoutFalling()
        {
            var config = WireTestSetup.LongAttachConfig(WireModelType.Responsive);
            var harness = new SimHarness(new BoxTerrain(), config, new Vector3(0f, 20f, 0f));
            var anchor = new Vector3(5f, 20f, 0f);
            WireTestSetup.AttachAt(harness, new Vector3(0f, 20f, 0f), anchor);

            harness.Step(Inputs.Wire(), 90);
            Vector3 hangingAt = harness.State.Position;
            harness.Step(Inputs.Wire(), 60);

            Assert.That(WireTestSetup.DistanceToAnchor(harness.State), Is.LessThanOrEqualTo(config.Wire.Responsive.ArrivalRadius + 0.01f));
            Assert.That(Vector3.Distance(harness.State.Position, hangingAt), Is.LessThan(0.01f));
            Assert.That(harness.State.Velocity.Length(), Is.LessThan(1e-3f));
        }

        // 左上の接続点（距離 10 m）に引かれ始めてから 12 ティック後の、接続点の方向の速さ。
        private static float PullSpeedAfter(PlayerInput input)
        {
            var config = WireTestSetup.LongAttachConfig(WireModelType.Responsive);
            var harness = new SimHarness(new BoxTerrain(), config, new Vector3(0f, 20f, 0f));
            var anchor = new Vector3(-6f, 18f, 0f);
            WireTestSetup.AttachAt(harness, new Vector3(0f, 10f, 0f), anchor);

            harness.Step(input, 12);
            Vector3 toAnchor = anchor - harness.State.Position;
            return Vector3.Dot(harness.State.Velocity, Vector3.Normalize(toAnchor));
        }
    }
}
