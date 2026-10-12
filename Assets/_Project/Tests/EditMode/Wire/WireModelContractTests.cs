using System.Numerics;
using Degunatsu.Core.Common;
using Degunatsu.Core.Input;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Tests.Support;
using Degunatsu.Core.Wire;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Wire
{
    /// <summary>
    /// すべてのタイプが守るべき共通の規則（docs/06_prototype_plan.md T1-6）。
    /// 同じテストをすべてのモデルに適用し、タイプによる不公平や抜け穴がないことを確かめる。
    /// </summary>
    [TestFixture(WireModelType.Inertial)]
    [TestFixture(WireModelType.Responsive)]
    public class WireModelContractTests
    {
        private readonly WireModelType _modelType;

        public WireModelContractTests(WireModelType modelType)
        {
            _modelType = modelType;
        }

        [Test]
        public void WhileAttached_SpeedNeverExceedsSharedCap_AndRopeLengthHolds()
        {
            var config = WireTestSetup.LongAttachConfig(_modelType);
            var harness = new SimHarness(new BoxTerrain(), config, new Vector3(0f, 20f, 0f));
            WireTestSetup.AttachAt(harness, new Vector3(-9f, 14f, 0f), new Vector3(0f, 20f, 0f), new Vector3(0f, -15f, 0f));

            for (int i = 0; i < 300; i++)
            {
                harness.Step(Inputs.Wire(i % 90 < 45 ? 1f : -1f, 0.5f));
                Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Attached));
                Assert.That(harness.State.Velocity.Length(), Is.LessThanOrEqualTo(config.Wire.MaxAttachedSpeed + 1e-4f), $"ティック {i}");
                Assert.That(WireTestSetup.DistanceToAnchor(harness.State), Is.LessThanOrEqualTo(harness.State.Wire.RopeLength + 1e-3f), $"ティック {i}");
            }
        }

        [Test]
        public void WhileAttached_IsPullingIsAlwaysTrue()
        {
            // D-18: ワイヤー→攻撃のキャンセル判定に使う「牽引中」は、モデルに関係なく接続中フェーズと一致する。
            var config = WireTestSetup.LongAttachConfig(_modelType);
            var harness = new SimHarness(TestArenas.Playground(), config, new Vector3(-6f, 0.91f, 0f));

            harness.StepUntil(s => s.Wire.Phase == WirePhase.Attached, Inputs.Wire(0.3f, 1f), 30);
            for (int i = 0; i < 120; i++)
            {
                harness.Step(Inputs.Wire(1f, 0f));
                Assert.That(harness.State.Wire.IsPulling, Is.EqualTo(harness.State.Wire.Phase == WirePhase.Attached));
            }
            harness.Step(Inputs.None);
            Assert.That(harness.State.Wire.IsPulling, Is.False);
        }

        [Test]
        public void SwingingThroughArena_NeverPenetratesTerrain()
        {
            var config = WireTestSetup.LongAttachConfig(_modelType);
            BoxTerrain world = TestArenas.Playground();
            var harness = new SimHarness(world, config, new Vector3(-6f, 0.91f, 0f));

            PlayerInput[] pattern =
            {
                Inputs.Wire(0.4f, 1f), Inputs.Wire(1f, 0.2f), Inputs.Right, Inputs.Wire(1f, 1f), Inputs.RightJump,
                Inputs.Wire(-1f, 1f), Inputs.Left, Inputs.Wire(-0.2f, 1f), Inputs.None, Inputs.Wire(1f, -0.5f),
            };
            int attachedTicks = 0;
            float traveled = 0f;
            for (int i = 0; i < 1200; i++)
            {
                Vector3 before = harness.State.Position;
                harness.Step(pattern[(i / 40) % pattern.Length]);
                traveled += Vector3.Distance(before, harness.State.Position);
                if (harness.State.Wire.Phase == WirePhase.Attached)
                {
                    attachedTicks++;
                }
                Assert.That(world.IsOverlapping(harness.State.Position, harness.HalfExtents), Is.False,
                    $"ティック {i} で地形にめり込みました: {harness.State}");
            }
            Assert.That(attachedTicks, Is.GreaterThan(200), "前提: ワイヤーで十分に移動している");
            Assert.That(traveled, Is.GreaterThan(100f), "前提: アリーナを十分に動き回っている");
        }

        [Test]
        public void OnRelease_ExitSpeedIsLimitedBySharedCap()
        {
            var config = WireTestSetup.LongAttachConfig(_modelType);
            var harness = new SimHarness(new BoxTerrain(), config, new Vector3(0f, 20f, 0f));
            // モデルによって接続中の最高速は違う（タイプBは上限より遅い）ので、解除直前の速度を直接与える。
            WireTestSetup.AttachAt(harness, new Vector3(-9f, 14f, 0f), new Vector3(0f, 20f, 0f), new Vector3(15f, -20f, 0f));
            Assert.That(harness.State.Velocity.Length(), Is.GreaterThan(config.Wire.ExitSpeedCap), "前提: 解除前は出口速度上限より速い");

            harness.Step(Inputs.Stick(1f, 0f));

            // 解除のティックでは、上限で制限した後に1ティック分の重力と空中制御が加わる。
            var movement = config.Movement;
            float allowance = (movement.Gravity + movement.AirAcceleration) * SimTime.DeltaTime;
            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Ready));
            Assert.That(harness.State.Velocity.Length(), Is.LessThanOrEqualTo(config.Wire.ExitSpeedCap + allowance));
        }
    }
}
