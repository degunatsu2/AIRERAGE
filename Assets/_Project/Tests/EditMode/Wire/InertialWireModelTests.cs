using System;
using System.Numerics;
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
