using System;
using System.Numerics;
using Airerage.Core.Common;
using Airerage.Core.Physics;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Movement
{
    public class MovementTests
    {
        // 地面（上面 y = 0）に立ったときの中心の高さ。半分の高さ 0.9 ＋ 地形との隙間。
        private const float StandingY = 0.91f;

        [Test]
        public void Spawn_AboveGround_FallsAndLands()
        {
            var harness = new SimHarness(TestArenas.Ground(), spawn: new Vector3(0f, 3f, 0f));

            harness.Step(Inputs.None, 120);

            Assert.That(harness.State.Grounded, Is.True);
            Assert.That(harness.State.Position.Y, Is.EqualTo(StandingY).Within(0.02f));
            Assert.That(harness.State.Velocity.Y, Is.EqualTo(0f));
        }

        [Test]
        public void Run_OnGround_ReachesRunSpeedAndNotBeyond()
        {
            var harness = new SimHarness(TestArenas.Ground());

            harness.Step(Inputs.Right, 60);

            Assert.That(harness.State.Velocity.X, Is.EqualTo(harness.Config.Movement.RunSpeed));
            Assert.That(harness.State.Facing, Is.EqualTo(1));
        }

        [Test]
        public void Jump_ApexHeightMatchesTuning()
        {
            var harness = new SimHarness(TestArenas.Ground());
            harness.Step(Inputs.None, 5);
            float startY = harness.State.Position.Y;

            harness.Step(Inputs.Jump);
            float apex = startY;
            for (int i = 0; i < 60; i++)
            {
                harness.Step(Inputs.None);
                apex = MathF.Max(apex, harness.State.Position.Y);
            }

            var tuning = harness.Config.Movement;
            float expected = tuning.JumpSpeed * tuning.JumpSpeed / (2f * tuning.Gravity);
            Assert.That(apex - startY, Is.EqualTo(expected).Within(expected * 0.05f));
        }

        [Test]
        public void HighSpeed_IntoThinWall_DoesNotTunnel()
        {
            var world = TestArenas.Ground().AddBox(5f, 0f, 5.2f, 20f);
            var harness = new SimHarness(world, spawn: new Vector3(0f, 8f, 0f));
            var state = harness.State;
            state.Velocity = new Vector3(22f, 0f, 0f);
            harness.State = state;

            for (int i = 0; i < 90; i++)
            {
                harness.Step(Inputs.Right);
                Assert.That(harness.State.Position.X + harness.HalfExtents.X, Is.LessThanOrEqualTo(5f),
                    $"ティック {i} で壁を越えました: {harness.State}");
            }
        }

        [Test]
        public void Jump_IntoCeiling_StopsUpwardVelocity()
        {
            var world = TestArenas.Ground().AddBox(-5f, 3f, 5f, 4f);
            var harness = new SimHarness(world);
            harness.Step(Inputs.None, 5);

            harness.Step(Inputs.Jump);
            float maxY = 0f;
            bool stoppedByCeiling = false;
            for (int i = 0; i < 30; i++)
            {
                harness.Step(Inputs.None);
                maxY = MathF.Max(maxY, harness.State.Position.Y);
                if (harness.State.Position.Y + harness.HalfExtents.Y > 2.95f && harness.State.Velocity.Y <= 0f)
                {
                    stoppedByCeiling = true;
                }
            }

            Assert.That(maxY + harness.HalfExtents.Y, Is.LessThanOrEqualTo(3f));
            Assert.That(stoppedByCeiling, Is.True, "天井に当たった時点で上向きの速度が消えていません");
        }

        [Test]
        public void Air_FastHorizontalMomentum_IsKeptWhileHoldingSameDirection()
        {
            var harness = new SimHarness(new BoxTerrain(), spawn: new Vector3(0f, 50f, 0f));
            var state = harness.State;
            state.Velocity = new Vector3(15f, 0f, 0f);
            harness.State = state;

            harness.Step(Inputs.Right, 10);

            Assert.That(harness.State.Velocity.X, Is.EqualTo(15f));
        }

        [Test]
        public void Air_OppositeInput_DeceleratesWithAirAcceleration()
        {
            var harness = new SimHarness(new BoxTerrain(), spawn: new Vector3(0f, 50f, 0f));
            var state = harness.State;
            state.Velocity = new Vector3(15f, 0f, 0f);
            harness.State = state;

            harness.Step(Inputs.Left, 6);

            float expected = 15f - harness.Config.Movement.AirAcceleration * SimTime.DeltaTime * 6;
            Assert.That(harness.State.Velocity.X, Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void CoyoteTime_AllowsGroundJumpShortlyAfterLeavingLedge()
        {
            var world = new BoxTerrain().AddBox(-20f, -1f, 0f, 0f);
            var harness = new SimHarness(world, spawn: new Vector3(-2f, StandingY, 0f));

            harness.StepUntil(s => !s.Grounded, Inputs.Right, 120);
            harness.Step(Inputs.Right, 2);
            harness.Step(Inputs.RightJump);

            Assert.That(harness.State.Velocity.Y, Is.GreaterThan(0f));
            Assert.That(harness.State.AirJumpsUsed, Is.EqualTo(0), "空中ジャンプを消費せずに跳べるはず");
        }

        [Test]
        public void JumpBuffer_PressedJustBeforeLanding_JumpsOnLanding()
        {
            var config = new Airerage.Core.Simulation.CharacterConfig();
            config.Movement.AirJumps = 0;
            var harness = new SimHarness(TestArenas.Ground(), config, new Vector3(0f, 1.2f, 0f));
            var state = harness.State;
            state.Velocity = new Vector3(0f, -6f, 0f);
            harness.State = state;

            harness.Step(Inputs.Jump);
            Assert.That(harness.State.Grounded, Is.False, "前提: まだ着地していない");

            int ticks = harness.StepUntil(s => s.Velocity.Y > 0f, Inputs.Jump, 6);
            Assert.That(ticks, Is.LessThanOrEqualTo(6));
        }

        [Test]
        public void AirJump_CanBeUsedOnlyConfiguredTimes()
        {
            var harness = new SimHarness(new BoxTerrain(), spawn: new Vector3(0f, 50f, 0f));
            harness.Step(Inputs.None, 3);

            harness.Step(Inputs.Jump);
            Assert.That(harness.State.Velocity.Y, Is.GreaterThan(0f));
            Assert.That(harness.State.AirJumpsUsed, Is.EqualTo(1));

            harness.Step(Inputs.None, 20);
            float before = harness.State.Velocity.Y;
            harness.Step(Inputs.Jump);
            Assert.That(harness.State.Velocity.Y, Is.LessThan(before), "空中ジャンプは1回まで");
        }
    }
}
