using System.Numerics;
using Airerage.Core.Movement;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Movement
{
    /// <summary>
    /// 速さの上限と着地の勢い（ユーザーの要望、2026-11-03。実験的に試している【仮】）: 地上・空中・落下の最高速度は 40 m/s。着地の瞬間に
    /// ジャンプすれば横の勢いを保ったまま空中に戻れ、地面にいると急速に走る速さへ落ちる。
    /// </summary>
    public class SpeedCapTests
    {
        // 地面（上面 y = 0）の少し上から、横に速く落ちてくる状態にする。
        private static SimHarness Falling(float vx)
        {
            var harness = new SimHarness(TestArenas.Ground(), spawn: new Vector3(-40f, 1.3f, 0f));
            CharacterState state = harness.State;
            state.Velocity = new Vector3(vx, -6f, 0f);
            harness.State = state;
            return harness;
        }

        [Test]
        public void JumpingRightAtTheLanding_KeepsTheSpeed()
        {
            SimHarness harness = Falling(30f);
            harness.StepUntil(s => s.Grounded, Inputs.Right, 30);
            Assert.That(harness.State.Grounded, Is.True, "前提: 着地した");
            harness.Step(Inputs.RightJump);
            Assert.That(harness.State.Grounded, Is.False, "前提: 跳んだ");
            Assert.That(harness.State.Velocity.X, Is.EqualTo(30f).Within(1e-3f), "着地の直後に跳んだのに勢いが落ちた");
        }

        [Test]
        public void StayingOnTheGround_LosesTheSpeedQuickly()
        {
            SimHarness harness = Falling(30f);
            harness.StepUntil(s => s.Grounded, Inputs.Right, 30);
            MovementTuning tuning = harness.Config.Movement;
            harness.Step(Inputs.Right, tuning.LandingGraceTicks);
            Assert.That(harness.State.Velocity.X, Is.EqualTo(30f).Within(1e-3f), "着地の直後に勢いが落ちた");
            harness.Step(Inputs.Right, 30);
            Assert.That(harness.State.Velocity.X, Is.EqualTo(tuning.RunSpeed).Within(1e-3f), "地面にいても勢いが落ちない");
        }

        [Test]
        public void AtTheTopSpeed_DoesNotPassThroughAThinWall()
        {
            // 上限の 40 m/s（1 ティックに 0.67m）でも、厚さ 0.2m の壁をすり抜けない（KinematicMotor の連続判定）。
            var world = TestArenas.Ground().AddBox(5f, 0f, 5.2f, 20f);
            var harness = new SimHarness(world, spawn: new Vector3(0f, 8f, 0f));
            CharacterState state = harness.State;
            state.Velocity = new Vector3(harness.Config.Movement.MaxAirSpeed, 0f, 0f);
            harness.State = state;
            for (int i = 0; i < 90; i++)
            {
                harness.Step(Inputs.Right);
                Assert.That(harness.State.Position.X + harness.HalfExtents.X, Is.LessThanOrEqualTo(5f), $"ティック {i} で壁を越えた: {harness.State}");
            }
        }

        [Test]
        public void SpeedRamp_KeepsTheOldGainBelowTheOldCap_AndReachesTheTopInAboutFourSeconds()
        {
            // 2026-11-03 の要望: 前の上限（22 m/s）までは今までどおり、その先は曲線的に緩やかに、4 秒ほどで 40 m/s に届く。
            Assert.That(SpeedRamp.Limit(10f, 15f, 22f, 40f, 0.3f), Is.EqualTo(15f), "前の上限より下の伸びが変わった");
            Assert.That(SpeedRamp.Limit(30f, 25f, 22f, 40f, 0.3f), Is.EqualTo(25f), "減速が変わった");
            Assert.That(SpeedRamp.Limit(40f, 45f, 22f, 40f, 0.3f), Is.EqualTo(40f), "上限を超えた");
            const float gravity = 30f;
            float scale = SpeedRamp.ScaleFor(22f, 40f, gravity, 4f);
            float speed = 0f;
            float lastGain = float.MaxValue;
            int ticks = 0;
            while (speed < 40f - 1e-3f && ticks < 1000)
            {
                float next = SpeedRamp.Limit(speed, speed + gravity / 60f, 22f, 40f, scale);
                if (speed >= 22f)
                {
                    Assert.That(next - speed, Is.LessThanOrEqualTo(lastGain + 1e-6f), "上限に近いほど伸びが小さくならない");
                    lastGain = next - speed;
                }
                speed = next;
                ticks++;
            }
            Assert.That(ticks / 60f, Is.EqualTo(4f).Within(0.1f), "重力の加速で 4 秒ほどで上限に届かない");
        }

        [Test]
        public void Falling_KeepsTheOldSpeedUpTo20_ThenReaches40InAboutFourSeconds()
        {
            var harness = new SimHarness(new BoxTerrain(), spawn: new Vector3(0f, 2000f, 0f));
            harness.Step(Inputs.None, 40);
            Assert.That(harness.State.Velocity.Y, Is.EqualTo(-20f).Within(0.05f), "前の上限（20 m/s）までの落ち方が変わった");
            int ticks = 40 + harness.StepUntil(s => s.Velocity.Y <= -39.99f, Inputs.None, 600);
            Assert.That(ticks / 60f, Is.EqualTo(4f).Within(0.15f), "止まった状態から 4 秒ほどで最大の落下速度に届かない");
        }

        [Test]
        public void FallAndAirSpeeds_StopAt40()
        {
            var harness = new SimHarness(new BoxTerrain(), spawn: new Vector3(0f, 500f, 0f));
            CharacterState state = harness.State;
            state.Velocity = new Vector3(55f, 0f, 0f);
            harness.State = state;
            harness.Step(Inputs.Right, 300);
            Assert.That(harness.State.Velocity.Y, Is.EqualTo(-40f).Within(1e-3f), "落ちる速さが 40 m/s で止まらない");
            Assert.That(harness.State.Velocity.X, Is.EqualTo(40f).Within(1e-3f), "空中の横の速さが 40 m/s で止まらない");
        }
    }
}
