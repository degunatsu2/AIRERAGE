using System;
using System.Numerics;
using Degunatsu.Core.Input;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Tests.Support;
using Degunatsu.Core.Wire;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Wire
{
    /// <summary>
    /// タイプA（物理・慣性重視）のモデル（docs/06_prototype_plan.md T1-4）。
    /// </summary>
    public class InertialWireModelTests
    {
        private static readonly Vector3 Anchor = new Vector3(0f, 20f, 0f);

        [Test]
        public void Swing_DistanceNeverExceedsRopeLength()
        {
            var config = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            config.Wire.Inertial.ReelSpeed = 0f;
            var harness = new SimHarness(new BoxTerrain(), config);
            WireTestSetup.AttachAt(harness, Anchor + new Vector3(4f, -3f, 0f), Anchor);
            float rope = harness.State.Wire.RopeLength;

            for (int i = 0; i < 600; i++)
            {
                harness.Step(Inputs.Wire(i % 120 < 60 ? 1f : -1f, 0f));
                Assert.That(WireTestSetup.DistanceToAnchor(harness.State), Is.LessThanOrEqualTo(rope + 1e-3f), $"ティック {i}");
            }
        }

        [Test]
        public void SmallSwing_PeriodMatchesPendulumTheory()
        {
            var config = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            config.Wire.Inertial.ReelSpeed = 0f;
            config.Wire.Inertial.SwingAcceleration = 0f;
            config.Wire.Inertial.Damping = 0f;
            config.Wire.Inertial.StartPullSpeed = 0f; // 振り子の理論と比べるため、接続直後の引きを使わない
            var harness = new SimHarness(new BoxTerrain(), config);

            float length = 4f;
            float amplitudeRad = 10f * MathF.PI / 180f;
            var start = Anchor + new Vector3(MathF.Sin(amplitudeRad), -MathF.Cos(amplitudeRad), 0f) * length;
            WireTestSetup.AttachAt(harness, start, Anchor);

            // x が 0 を横切る時刻（線形補間）を記録する。
            var crossings = new System.Collections.Generic.List<float>();
            float previousX = harness.State.Position.X;
            for (int tick = 1; tick <= 600 && crossings.Count < 3; tick++)
            {
                harness.Step(Inputs.Wire());
                float x = harness.State.Position.X;
                if (MathF.Sign(x) != MathF.Sign(previousX))
                {
                    float fraction = previousX / (previousX - x);
                    crossings.Add(tick - 1 + fraction);
                }
                previousX = x;
            }

            Assert.That(crossings.Count, Is.EqualTo(3), "振り子が3回中心を通過していません");
            float periodSeconds = (crossings[2] - crossings[0]) / 60f;
            float expected = 2f * MathF.PI * MathF.Sqrt(length / config.Movement.Gravity);
            Assert.That(periodSeconds, Is.EqualTo(expected).Within(expected * 0.03f));
        }

        [Test]
        public void Reel_ShortensRopeToMinimum()
        {
            var config = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            var harness = new SimHarness(new BoxTerrain(), config);
            WireTestSetup.AttachAt(harness, Anchor + new Vector3(0f, -8f, 0f), Anchor);

            float seconds = (8f - config.Wire.MinRopeLength) / config.Wire.Inertial.ReelSpeed;
            harness.Step(Inputs.Wire(), (int)MathF.Ceiling(seconds * 60f) + 5);

            Assert.That(harness.State.Wire.RopeLength, Is.EqualTo(config.Wire.MinRopeLength));
            Assert.That(WireTestSetup.DistanceToAnchor(harness.State), Is.LessThanOrEqualTo(config.Wire.MinRopeLength + 1e-3f));
        }

        [Test]
        public void Reel_ConservesAngularMomentum()
        {
            var config = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            config.Movement.Gravity = 0f;
            config.Wire.Inertial.SwingAcceleration = 0f;
            var harness = new SimHarness(new BoxTerrain(), config);
            // 半径 8m の円周上を接線方向に 5 m/s で動いている状態から、半径 4m まで巻き取る。
            WireTestSetup.AttachAt(harness, Anchor + new Vector3(0f, -8f, 0f), Anchor, new Vector3(5f, 0f, 0f));

            harness.StepUntil(s => s.Wire.RopeLength <= 4f, Inputs.Wire(), 120);

            Vector3 radial = Vector3.Normalize(harness.State.Position - Anchor);
            Vector3 velocity = harness.State.Velocity;
            float tangentialSpeed = (velocity - radial * Vector3.Dot(velocity, radial)).Length();
            Assert.That(tangentialSpeed, Is.EqualTo(10f).Within(1f), "半径が半分になれば接線速度は約2倍");
        }

        [Test]
        public void FromRest_StartsAtStartPullSpeed()
        {
            float boosted = SpeedAfterAttachFromRest(startPullSpeed: 14f);
            float plain = SpeedAfterAttachFromRest(startPullSpeed: 0f);

            Assert.That(boosted, Is.EqualTo(14f).Within(0.5f), "止まっている状態から接続した直後の速さ");
            Assert.That(plain, Is.LessThan(boosted - 4f), "前提: 下限がなければ遅い（巻き取りの速さ程度）");
        }

        [Test]
        public void FromRest_HoldsStartPullSpeed_WithoutSlackRope()
        {
            // 地面に立ったまま、右上の接続点に接続する。引きの間はロープがたるまず、速さが下限を下回らない。
            var config = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            var harness = new SimHarness(TestArenas.Ground(), config);
            WireTestSetup.AttachAt(harness, new Vector3(0f, 0.91f, 0f), new Vector3(6f, 6.91f, 0f));

            int ticks = config.Wire.Inertial.StartPullTicks + config.Wire.Inertial.StartPullFadeTicks;
            for (int i = 0; i < ticks; i++)
            {
                harness.Step(Inputs.Wire());
                Assert.That(WireTestSetup.DistanceToAnchor(harness.State), Is.EqualTo(harness.State.Wire.RopeLength).Within(1e-3f),
                    $"ティック {i}: ロープがたるんだ");
                if (i < config.Wire.Inertial.StartPullTicks)
                {
                    Assert.That(harness.State.Velocity.Length(), Is.GreaterThan(config.Wire.Inertial.StartPullSpeed - 0.5f), $"ティック {i}");
                }
            }
        }

        [Test]
        public void FastAttach_IsNotChangedByStartPull()
        {
            // 下限より速く振っている状態で接続したときは、下限があってもなくても同じ動きになる。
            var withPull = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            var withoutPull = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            withoutPull.Wire.Inertial.StartPullSpeed = 0f;
            var a = new SimHarness(new BoxTerrain(), withPull);
            var b = new SimHarness(new BoxTerrain(), withoutPull);
            Vector3 start = Anchor + new Vector3(0f, -8f, 0f);
            var velocity = new Vector3(withPull.Wire.Inertial.StartPullSpeed + 2f, 0f, 0f);
            WireTestSetup.AttachAt(a, start, Anchor, velocity);
            WireTestSetup.AttachAt(b, start, Anchor, velocity);

            for (int i = 0; i < 60; i++)
            {
                a.Step(Inputs.Wire());
                b.Step(Inputs.Wire());
                Assert.That(a.State, Is.EqualTo(b.State), $"ティック {i}");
            }
        }

        [Test]
        public void StartPull_EndsAfterStartPullTicks()
        {
            var config = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            var harness = new SimHarness(new BoxTerrain(), config);
            WireTestSetup.AttachAt(harness, Anchor + new Vector3(0f, -11f, 0f), Anchor);

            harness.Step(Inputs.Wire(), config.Wire.Inertial.StartPullTicks + config.Wire.Inertial.StartPullFadeTicks + 1);
            float before = harness.State.Wire.RopeLength;
            harness.Step(Inputs.Wire());

            Assert.That(before - harness.State.Wire.RopeLength, Is.EqualTo(config.Wire.Inertial.ReelSpeed / 60f).Within(1e-4f),
                "下限の期間が終わったら、巻き取りの速さに戻る");
        }

        [Test]
        public void HoldingDown_KeepsTheRopeLength_WhileSwinging()
        {
            // 下（斜め下を含む）を押し続けると巻き取らない。左右で振ることはできる（D-41）。
            var config = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            var harness = new SimHarness(new BoxTerrain(), config);
            WireTestSetup.AttachAt(harness, Anchor + new Vector3(6f, -8f, 0f), Anchor);
            float rope = harness.State.Wire.RopeLength;

            float minX = harness.State.Position.X;
            for (int i = 0; i < 300; i++)
            {
                harness.Step(Inputs.Wire(i % 120 < 60 ? -1f : 1f, -1f));
                Assert.That(harness.State.Wire.RopeLength, Is.EqualTo(rope), $"ティック {i}: ロープが縮んだ");
                Assert.That(WireTestSetup.DistanceToAnchor(harness.State), Is.LessThanOrEqualTo(rope + 1e-3f), $"ティック {i}");
                minX = MathF.Min(minX, harness.State.Position.X);
            }
            Assert.That(minX, Is.LessThan(0f), "振れていない（接続点の左側まで行かない）");
        }

        [Test]
        public void HoldingDown_FromRest_StartsAtStartPullSpeed_AlongTheSwing()
        {
            // 止まった状態で接続しても、振りの方向（ロープに垂直）に下限の速さが出る。ロープは縮まない。
            var config = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            var harness = new SimHarness(new BoxTerrain(), config);
            WireTestSetup.AttachAt(harness, Anchor + new Vector3(6f, -8f, 0f), Anchor);
            float rope = harness.State.Wire.RopeLength;

            harness.Step(Inputs.Wire(0f, -1f));

            Vector3 radial = Vector3.Normalize(harness.State.Position - Anchor);
            float radialSpeed = Vector3.Dot(harness.State.Velocity, radial);
            Assert.That(harness.State.Velocity.Length(), Is.EqualTo(config.Wire.Inertial.StartPullSpeed).Within(0.5f));
            Assert.That(MathF.Abs(radialSpeed), Is.LessThan(0.5f), "接続点の方向に引いている");
            Assert.That(harness.State.Velocity.X, Is.LessThan(0f), "振り下ろす向き（接続点の真下の方）に動く");
            Assert.That(harness.State.Wire.RopeLength, Is.EqualTo(rope));
        }

        [Test]
        public void HoldingDown_DiagonalInput_SwingsHarderThanTheNormalSwing()
        {
            // 下を押しているときは、斜め下でも左右いっぱいの力（HoldSwingAcceleration）で振れる。
            float held = SwingSpeedAfterPumping(Inputs.Wire(1f, -1f));
            float normal = SwingSpeedAfterPumping(Inputs.Wire(1f, 0f), reelSpeed: 0f);

            Assert.That(held, Is.GreaterThan(normal + 1f), $"下: {held:0.00} m/s、ふつう: {normal:0.00} m/s");
        }

        [Test]
        public void HoldingUp_ReelsFast()
        {
            var config = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            var harness = new SimHarness(new BoxTerrain(), config);
            WireTestSetup.AttachAt(harness, Anchor + new Vector3(0f, -11f, 0f), Anchor);
            float rope = harness.State.Wire.RopeLength;

            harness.Step(Inputs.Wire(0f, 1f), 3);

            Assert.That(config.Wire.Inertial.FastReelSpeed, Is.GreaterThan(config.Wire.Inertial.StartPullSpeed), "前提: 接続直後の引きより速い");
            Assert.That(rope - harness.State.Wire.RopeLength, Is.EqualTo(3 * config.Wire.Inertial.FastReelSpeed / 60f).Within(1e-4f));
        }

        [Test]
        public void DiagonalUp_IsTheNormalSwing()
        {
            // 斜め上は「上」に数えない（振っている間に斜め上を押し続けることが多いため）。何も押さないときと同じ速さで巻き取る。
            var config = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            var harness = new SimHarness(new BoxTerrain(), config);
            Vector3 start = Anchor + new Vector3(0f, -11f, 0f);
            WireTestSetup.AttachAt(harness, start, Anchor, new Vector3(config.Wire.Inertial.StartPullSpeed + 1f, 0f, 0f));
            float rope = harness.State.Wire.RopeLength;

            harness.Step(Inputs.Wire(1f, 1f));

            Assert.That(rope - harness.State.Wire.RopeLength, Is.EqualTo(config.Wire.Inertial.ReelSpeed / 60f).Within(1e-4f));
        }

        // 接続点の真下で止まった状態から、入力 input で 20 ティック振ったときの速さ（接続直後の下限は使わない）。
        private static float SwingSpeedAfterPumping(PlayerInput input, float? reelSpeed = null)
        {
            var config = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            config.Wire.Inertial.StartPullSpeed = 0f;
            if (reelSpeed.HasValue)
            {
                config.Wire.Inertial.ReelSpeed = reelSpeed.Value;
            }
            var harness = new SimHarness(new BoxTerrain(), config);
            WireTestSetup.AttachAt(harness, Anchor + new Vector3(0f, -8f, 0f), Anchor);
            harness.Step(input, 20);
            return harness.State.Velocity.Length();
        }

        // 接続点の右下（止まった状態）で接続し、1ティック後の速さ。
        private static float SpeedAfterAttachFromRest(float startPullSpeed)
        {
            var config = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            config.Wire.Inertial.StartPullSpeed = startPullSpeed;
            var harness = new SimHarness(new BoxTerrain(), config);
            WireTestSetup.AttachAt(harness, Anchor + new Vector3(6f, -8f, 0f), Anchor);

            harness.Step(Inputs.Wire());
            return harness.State.Velocity.Length();
        }
    }
}
