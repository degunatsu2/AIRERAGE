using System;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Movement
{
    /// <summary>壁への張り付きと壁ジャンプ（WallCling。ユーザーの要望、2026-10-19）。</summary>
    public class WallClingTests
    {
        // 床（上面 y = 0）と、左右の高い壁（x = -1〜0 と 5〜6。間は 5m）。
        private static BoxTerrain Walls()
        {
            return new BoxTerrain()
                .AddBox(-40f, -1f, 40f, 0f)
                .AddBox(-1f, 0f, 0f, 30f)
                .AddBox(5f, 0f, 6f, 30f);
        }

        // 空中の (3, 10) から、右の壁へ横に飛ばす。
        private static SimHarness FlyingRight(float vx = 8f, float vy = 0f)
        {
            var harness = new SimHarness(Walls(), spawn: new Vector3(3f, 10f, 0f));
            CharacterState state = harness.State;
            state.Velocity = new Vector3(vx, vy, 0f);
            harness.State = state;
            return harness;
        }

        [Test]
        public void HittingAWall_FromTheSide_InTheAir_SticksToIt()
        {
            SimHarness harness = FlyingRight();
            harness.StepUntil(s => s.WallSide != 0, Inputs.None, 30);

            CharacterState state = harness.State;
            Assert.That(state.WallSide, Is.EqualTo(1), "右の壁に張り付かない");
            Assert.That(state.Velocity, Is.EqualTo(Vector3.Zero));
            Assert.That(state.Facing, Is.EqualTo(-1), "壁から離れる向きを向かない");
            float y = state.Position.Y;
            harness.Step(Inputs.None, 30);
            Assert.That(harness.State.WallSide, Is.EqualTo(1), "張り付いたままでいない");
            Assert.That(harness.State.Position.Y, Is.EqualTo(y).Within(1e-4f), "止まっていない");
        }

        [Test]
        public void Jumping_WhileStuck_JumpsUpAndAway_AndResetsAirJumpsAndTheAttackCancel()
        {
            SimHarness harness = FlyingRight();
            harness.StepUntil(s => s.WallSide != 0, Inputs.None, 30);
            CharacterState state = harness.State;
            state.AirJumpsUsed = harness.Config.Movement.AirJumps;
            state.Combat.AttackCancelUsed = true;
            harness.State = state;

            harness.Step(Inputs.Jump);

            state = harness.State;
            Assert.That(state.WallSide, Is.EqualTo(0));
            Assert.That(state.WallJumpCount, Is.EqualTo(1));
            Assert.That(state.Velocity.X, Is.LessThan(-8f), "壁から離れる向きへ跳ばない");
            Assert.That(state.Velocity.Y, Is.GreaterThan(10f), "上へ跳ばない");
            Assert.That(state.AirJumpsUsed, Is.EqualTo(0), "空中ジャンプが戻らない");
            Assert.That(state.Combat.AttackCancelUsed, Is.False, "攻撃のキャンセルが戻らない");
        }

        [Test]
        public void WallJumps_HaveNoLimit()
        {
            SimHarness harness = FlyingRight();
            bool wasStuck = false;
            for (int i = 0; i < 220; i++)
            {
                // 張り付いたら次のティックにジャンプを押す（離してから押し直す）。
                bool stuck = harness.State.WallSide != 0;
                harness.Step(stuck && wasStuck ? Inputs.Jump : Inputs.None);
                wasStuck = stuck && !wasStuck;
            }

            // 1 回の壁ジャンプで約 3m 昇る（壁の高さは 30m）。
            Assert.That(harness.State.WallJumpCount, Is.GreaterThanOrEqualTo(6), "壁と壁の間を何度も跳べない");
            Assert.That(harness.State.Position.Y, Is.GreaterThan(20f), "壁ジャンプで昇れない");
        }

        [Test]
        public void NoCling_InHitstun_OnTheWire_OrWhileRisingFast()
        {
            SimHarness hit = FlyingRight();
            CharacterState state = hit.State;
            state.Action = new ActionState { Kind = ActionKind.Hitstun, Duration = 60 };
            hit.State = state;
            hit.Step(Inputs.None, 20);
            Assert.That(hit.State.WallSide, Is.EqualTo(0), "のけぞり中に張り付いた");

            SimHarness rising = FlyingRight(8f, 14f);
            rising.Step(Inputs.None, 3);
            Assert.That(rising.State.WallSide, Is.EqualTo(0), "上へ速く昇っている途中で張り付いた");
        }

        [Test]
        public void PushingAway_LetsGo_AndHoldingTooLong_SlidesDown()
        {
            SimHarness harness = FlyingRight();
            harness.StepUntil(s => s.WallSide != 0, Inputs.None, 30);
            float y = harness.State.Position.Y;
            harness.Step(Inputs.None, harness.Config.Movement.WallClingHoldTicks + 30);
            Assert.That(harness.State.Position.Y, Is.LessThan(y - 0.5f), "張り付いたままずり落ちない");

            harness.Step(Inputs.Left, 1);
            Assert.That(harness.State.WallSide, Is.EqualTo(0), "壁と反対へ入れても離れない");
        }

        [Test]
        public void Attacking_FromTheWall_LetsGo()
        {
            SimHarness harness = FlyingRight();
            harness.StepUntil(s => s.WallSide != 0, Inputs.None, 30);
            harness.Step(new PlayerInput(0, 0, InputButtons.Attack));

            Assert.That(harness.State.Action.Kind, Is.EqualTo(ActionKind.Attack), "壁から攻撃を出せない");
            Assert.That(harness.State.WallSide, Is.EqualTo(0));
        }

        [Test]
        public void NearTheTopOfAWall_DoesNotStick_AndClimbsOverTheEdge()
        {
            // 低い壁（上の端は y = 3）。体の下の端がもう少しで上の端に届く高さで、横からぶつかりながら昇る。
            var terrain = new BoxTerrain().AddBox(-40f, -1f, 40f, 0f).AddBox(2f, 0f, 6f, 3f);
            var harness = new SimHarness(terrain, spawn: new Vector3(1.5f, 3.7f, 0f));
            CharacterState state = harness.State;
            state.Velocity = new Vector3(8f, 5f, 0f);
            harness.State = state;

            bool clung = false;
            for (int i = 0; i < 60 && !harness.State.Grounded; i++)
            {
                harness.Step(Inputs.Right);
                clung |= harness.State.WallSide != 0;
            }
            Assert.That(clung, Is.False, "壁の上の端のすぐ下で張り付いた（縁を越えられない）");
            Assert.That(harness.State.Grounded, Is.True);
            Assert.That(harness.State.Position.X, Is.GreaterThan(2f), "壁の上に上がれない");
            Assert.That(harness.State.Position.Y, Is.GreaterThan(3f));
        }

        [Test]
        public void StandingNextToAWall_DoesNotStick()
        {
            var harness = new SimHarness(Walls(), spawn: new Vector3(4f, 0.91f, 0f));
            harness.Step(Inputs.Right, 30);
            Assert.That(harness.State.WallSide, Is.EqualTo(0), "地面に立ったまま壁に張り付いた");
        }
    }
}
