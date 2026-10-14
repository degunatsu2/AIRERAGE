using System.Numerics;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using Airerage.Core.Wire;
using NUnit.Framework;

namespace Airerage.Core.Tests.Wire
{
    /// <summary>ロープの巻き付き（D-33）。</summary>
    public class RopeWrappingTests
    {
        private static readonly Vector3 Anchor = new Vector3(0f, 20f, 0f);

        // 接続点の右下（約5m）に細い柱。接続点から 8m 下で右へ（反時計回りに）回ると、ロープが最初に柱の左下の角 (3, 16) に掛かる。
        private static BoxTerrain WorldWithPost()
        {
            return new BoxTerrain().AddBox(3f, 16f, 3.5f, 16.5f);
        }

        private static readonly Vector3 FirstCorner = new Vector3(3f - BoxTerrain.RopeCornerOffset, 16f - BoxTerrain.RopeCornerOffset, 0f);

        // 重力も巻き取りもない設定（ロープの長さの合計が変わらないことを確かめやすい）。
        private static CharacterConfig FreeSwingConfig(WireModelType type = WireModelType.Inertial)
        {
            var config = WireTestSetup.LongAttachConfig(type);
            config.Movement.Gravity = 0f;
            config.Wire.Inertial.ReelSpeed = 0f;
            config.Wire.Inertial.SwingAcceleration = 0f;
            config.Wire.Inertial.StartPullSpeed = 0f;
            return config;
        }

        [Test]
        public void BoxTerrain_RopeCorners_ExcludeCornersHiddenByOtherBoxes()
        {
            var single = new BoxTerrain().AddBox(0f, 0f, 1f, 1f);
            Assert.That(single.RopeCorners.Count, Is.EqualTo(4));
            Assert.That(single.RopeCorners, Does.Contain(new RopeCorner(
                new Vector3(1f, 1f, 0f), new Vector3(1f + BoxTerrain.RopeCornerOffset, 1f + BoxTerrain.RopeCornerOffset, 0f))));

            // 地面の上に立つ壁: 壁の下の2つの角は地面に埋まる。地面の上の2つの角のうち、壁に重なるものはない。
            var wallOnGround = new BoxTerrain().AddBox(-10f, -1f, 10f, 0f).AddBox(0f, 0f, 1f, 5f);
            Assert.That(wallOnGround.RopeCorners.Count, Is.EqualTo(6));
        }

        [Test]
        public void SwingingPastAPost_WrapsAroundItsCorner_AndKeepsTheTotalLength()
        {
            var config = FreeSwingConfig();
            BoxTerrain world = WorldWithPost();
            var harness = new SimHarness(world, config, new Vector3(0f, 12f, 0f));
            WireTestSetup.AttachAt(harness, new Vector3(0f, 12f, 0f), Anchor, new Vector3(10f, 0f, 0f));

            harness.StepUntil(s => s.Wire.Pivots.Count == 1, Inputs.Wire(), 120);
            WireState wire = harness.State.Wire;
            Vector3 pivot = wire.Pivots[0];

            Assert.That(pivot, Is.EqualTo(FirstCorner), "最初に掛かる角でない");
            Assert.That(Vector3.Distance(Anchor, pivot) + wire.RopeLength, Is.EqualTo(8f).Within(0.05f), "ロープの長さの合計が変わった");
            Assert.That(wire.ActivePivot, Is.EqualTo(pivot));

            // 巻き付いた後は、角を中心に回り、地形にめり込まない。
            for (int i = 0; i < 20; i++)
            {
                harness.Step(Inputs.Wire());
                Assert.That(world.IsOverlapping(harness.State.Position, harness.HalfExtents), Is.False, $"ティック {i}");
                float distance = Vector3.Distance(harness.State.Position, harness.State.Wire.ActivePivot);
                Assert.That(distance, Is.LessThanOrEqualTo(harness.State.Wire.RopeLength + 1e-3f), $"ティック {i}");
            }
        }

        [Test]
        public void KeepsSwinging_WindsAroundThePost()
        {
            // 回り続けると、柱の角に次々と巻き付き、ロープは短くなっていく（支点の数の上限まで）。
            var config = FreeSwingConfig();
            BoxTerrain world = WorldWithPost();
            var harness = new SimHarness(world, config, new Vector3(0f, 12f, 0f));
            WireTestSetup.AttachAt(harness, new Vector3(0f, 12f, 0f), Anchor, new Vector3(10f, 0f, 0f));

            harness.StepUntil(s => s.Wire.Pivots.Count >= 3, Inputs.Wire(), 240);

            WireState wire = harness.State.Wire;
            float total = wire.RopeLength + Vector3.Distance(Anchor, wire.Pivots[0]);
            for (int i = 1; i < wire.Pivots.Count; i++)
            {
                Assert.That(IsCornerPivot(world, wire.Pivots[i]), Is.True, $"支点 {i} が地形の角にない");
                total += Vector3.Distance(wire.Pivots[i - 1], wire.Pivots[i]);
            }
            Assert.That(total, Is.EqualTo(8f).Within(0.1f), "ロープの長さの合計が変わった");
        }

        [Test]
        public void SwingingBack_Unwraps()
        {
            var config = FreeSwingConfig();
            var harness = new SimHarness(WorldWithPost(), config, new Vector3(0f, 12f, 0f));
            WireTestSetup.AttachAt(harness, new Vector3(0f, 12f, 0f), Anchor, new Vector3(10f, 0f, 0f));
            harness.StepUntil(s => s.Wire.Pivots.Count == 1, Inputs.Wire(), 120);
            harness.Step(Inputs.Wire(), 3);

            // 向きを逆にして戻ると、角から外れて元の長さに戻る。
            CharacterState state = harness.State;
            state.Velocity = -state.Velocity;
            harness.State = state;
            harness.StepUntil(s => s.Wire.Pivots.Count == 0, Inputs.Wire(), 120);

            Assert.That(harness.State.Wire.RopeLength, Is.EqualTo(8f).Within(0.05f));
            Assert.That(harness.State.Wire.ActivePivot, Is.EqualTo(Anchor));
        }

        [TestCase(WireModelType.Inertial)]
        [TestCase(WireModelType.Responsive)]
        public void WrappingDisabled_RopePassesThroughTheTerrain(WireModelType type)
        {
            var config = FreeSwingConfig(type);
            config.Wire.RopeWrapping = false;
            var harness = new SimHarness(WorldWithPost(), config, new Vector3(0f, 12f, 0f));
            WireTestSetup.AttachAt(harness, new Vector3(0f, 12f, 0f), Anchor, new Vector3(10f, 0f, 0f));

            harness.Step(Inputs.Wire(), 60);

            Assert.That(harness.State.Wire.Pivots.Count, Is.EqualTo(0));
            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Attached));
        }

        [Test]
        public void Release_ClearsThePivots()
        {
            var harness = new SimHarness(WorldWithPost(), FreeSwingConfig(), new Vector3(0f, 12f, 0f));
            WireTestSetup.AttachAt(harness, new Vector3(0f, 12f, 0f), Anchor, new Vector3(10f, 0f, 0f));
            harness.StepUntil(s => s.Wire.Pivots.Count == 1, Inputs.Wire(), 120);

            harness.Step(Inputs.None);

            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Ready));
            Assert.That(harness.State.Wire.Pivots.Count, Is.EqualTo(0));
        }

        private static bool IsCornerPivot(BoxTerrain world, Vector3 point)
        {
            foreach (RopeCorner corner in world.RopeCorners)
            {
                if (corner.Pivot.Equals(point))
                {
                    return true;
                }
            }
            return false;
        }

        [Test]
        public void Pivots_PushAndPop_KeepDirections()
        {
            var pivots = new RopePivots();
            pivots.Push(new Vector3(1f, 0f, 0f), 1);
            pivots.Push(new Vector3(2f, 0f, 0f), -1);

            Assert.That(pivots.Count, Is.EqualTo(2));
            Assert.That(pivots.Direction(0), Is.EqualTo(1));
            Assert.That(pivots.Direction(1), Is.EqualTo(-1));

            pivots.Pop();
            Assert.That(pivots.Count, Is.EqualTo(1));
            Assert.That(pivots.Equals(new RopePivots { Count = 1, P0 = new Vector3(1f, 0f, 0f) }), Is.True, "外した支点が残っている");
        }
    }
}
