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
        public void FallAndAirSpeeds_StopAt40()
        {
            var harness = new SimHarness(new BoxTerrain(), spawn: new Vector3(0f, 500f, 0f));
            CharacterState state = harness.State;
            state.Velocity = new Vector3(55f, 0f, 0f);
            harness.State = state;
            harness.Step(Inputs.Right, 150);
            Assert.That(harness.State.Velocity.Y, Is.EqualTo(-40f).Within(1e-3f), "落ちる速さが 40 m/s で止まらない");
            Assert.That(harness.State.Velocity.X, Is.EqualTo(40f).Within(1e-3f), "空中の横の速さが 40 m/s で止まらない");
        }
    }
}
