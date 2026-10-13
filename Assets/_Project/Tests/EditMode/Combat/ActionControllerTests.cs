using System.Numerics;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Common;
using Degunatsu.Core.Input;
using Degunatsu.Core.Rollback;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Tests.Support;
using Degunatsu.Core.Wire;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Combat
{
    /// <summary>攻撃の状態とフレームデータ（docs/11_combat_plan.md T3-1）。</summary>
    public class ActionControllerTests
    {
        private static PlayerInput Attack(float x = 0f, float y = 0f, InputButtons extra = InputButtons.None)
        {
            return PlayerInput.FromAxes(x, y, InputButtons.Attack | extra);
        }

        private static SimHarness OnGround()
        {
            var harness = new SimHarness(TestArenas.Ground());
            harness.Step(Inputs.None, 2);
            Assert.That(harness.State.Grounded, Is.True, "前提: 地面に立っている");
            return harness;
        }

        [Test]
        public void GroundAttack_GoesThroughStartupActiveRecovery_ThenFree()
        {
            SimHarness harness = OnGround();
            AttackData data = harness.Config.Combat.GroundNeutral;

            harness.Step(Attack());
            Assert.That(harness.State.Action.Kind, Is.EqualTo(ActionKind.Attack));
            Assert.That(harness.State.Action.Attack, Is.EqualTo(AttackSlot.GroundNeutral));

            int startup = 0, active = 0, recovery = 0;
            int ticks = 0;
            while (harness.State.Action.Kind == ActionKind.Attack)
            {
                switch (ActionController.CurrentPhase(harness.State, harness.Config.Combat))
                {
                    case AttackPhase.Startup: startup++; break;
                    case AttackPhase.Active: active++; break;
                    case AttackPhase.Recovery: recovery++; break;
                }
                harness.Step(Attack()); // 押し続けても、次の攻撃は出ない
                ticks++;
                Assert.That(ticks, Is.LessThanOrEqualTo(data.TotalTicks), "攻撃が終わらない");
            }

            Assert.That(startup, Is.EqualTo(data.StartupTicks));
            Assert.That(active, Is.EqualTo(data.ActiveTicks));
            Assert.That(recovery, Is.EqualTo(data.RecoveryTicks));
            Assert.That(harness.State.Action.IsFree, Is.True);

            harness.Step(Attack(), 30);
            Assert.That(harness.State.Action.IsFree, Is.True, "押し続けても、もう一度押すまで次の攻撃は出ない");
        }

        [Test]
        public void NextAttack_CanStartRightAfterRecovery()
        {
            SimHarness harness = OnGround();
            int total = harness.Config.Combat.GroundNeutral.TotalTicks;

            harness.Step(Attack());
            harness.Step(Inputs.None, total - 1);
            Assert.That(harness.State.Action.Kind, Is.EqualTo(ActionKind.Attack), "前提: まだ最後のティック");
            harness.Step(Attack());

            Assert.That(harness.State.Action.Kind, Is.EqualTo(ActionKind.Attack));
            Assert.That(harness.State.Action.Ticks, Is.EqualTo(0), "硬直が終わったティックに、次の攻撃を始められる");
        }

        [TestCase(0f, 0f, true, AttackSlot.GroundNeutral)]
        [TestCase(1f, 0f, true, AttackSlot.GroundSide)]
        [TestCase(-1f, 0f, true, AttackSlot.GroundSide)]
        [TestCase(0f, 1f, true, AttackSlot.GroundUp)]
        [TestCase(0f, -1f, true, AttackSlot.GroundDown)]
        [TestCase(0.7f, 0.7f, true, AttackSlot.GroundUp)]
        [TestCase(0.9f, 0.5f, true, AttackSlot.GroundSide)]
        [TestCase(0.1f, 0.1f, true, AttackSlot.GroundNeutral)]
        [TestCase(0f, 0f, false, AttackSlot.AirNeutral)]
        [TestCase(1f, 0f, false, AttackSlot.AirSide)]
        [TestCase(0f, 1f, false, AttackSlot.AirUp)]
        [TestCase(0f, -1f, false, AttackSlot.AirDown)]
        public void Stick_SelectsTheAttack(float x, float y, bool grounded, AttackSlot expected)
        {
            Assert.That(ActionController.SelectAttack(grounded, new Vector2(x, y), 0.2f), Is.EqualTo(expected));
        }

        [Test]
        public void SideAttack_FacesTheStick_AndKeepsFacingDuringTheAttack()
        {
            SimHarness harness = OnGround();
            Assert.That(harness.State.Facing, Is.EqualTo(1), "前提: 右を向いている");

            harness.Step(Attack(-1f, 0f));
            Assert.That(harness.State.Action.Attack, Is.EqualTo(AttackSlot.GroundSide));
            Assert.That(harness.State.Facing, Is.EqualTo(-1), "左に入れて攻撃すると左を向く");

            harness.Step(Inputs.Right, 3);
            Assert.That(harness.State.Facing, Is.EqualTo(-1), "攻撃中は向きが変わらない");
        }

        [Test]
        public void DuringAttack_MoveJumpAndWireAreIgnored()
        {
            SimHarness harness = OnGround();
            harness.Step(Inputs.Right, 30);
            Assert.That(harness.State.Velocity.X, Is.GreaterThan(7f), "前提: 走っている");

            harness.Step(Attack(1f, 0f));
            var everything = PlayerInput.FromAxes(1f, 0f, InputButtons.Attack | InputButtons.Jump | InputButtons.Wire);
            harness.Step(everything, 10);

            CharacterState state = harness.State;
            Assert.That(state.Action.Kind, Is.EqualTo(ActionKind.Attack), "前提: まだ攻撃中");
            Assert.That(state.Velocity.X, Is.EqualTo(0f).Within(1e-4f), "地上の攻撃中は走れず、止まる");
            Assert.That(state.Grounded, Is.True, "攻撃中はジャンプできない");
            Assert.That(state.Wire.Phase, Is.EqualTo(WirePhase.Ready), "攻撃中はワイヤーを撃てない");

            // 攻撃が終わっても、押し続けているジャンプやワイヤーは出ない（もう一度押すと出る）。
            harness.Step(everything, harness.Config.Combat.GroundSide.TotalTicks);
            Assert.That(harness.State.Action.IsFree, Is.True);
            Assert.That(harness.State.Grounded, Is.True);
            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Ready));
        }

        [Test]
        public void AirAttack_KeepsMomentum()
        {
            var harness = new SimHarness(TestArenas.Ground(), spawn: new Vector3(0f, 8f, 0f));
            CharacterState start = harness.State;
            start.Velocity = new Vector3(6f, 0f, 0f);
            harness.State = start;

            harness.Step(Attack());
            Assert.That(harness.State.Action.Attack, Is.EqualTo(AttackSlot.AirNeutral));
            harness.Step(PlayerInput.FromAxes(-1f, 0f, InputButtons.Attack), 9);

            // 逆向きに入れても空中の加速は効かず、空気抵抗だけで少し遅くなる。重力で落ちる。
            float drag = harness.Config.Movement.AirDrag * SimTime.DeltaTime;
            Assert.That(harness.State.Velocity.X, Is.EqualTo(6f - 10 * drag).Within(1e-3f));
            Assert.That(harness.State.Velocity.Y, Is.LessThan(-1f));
        }

        [Test]
        public void CannotAttack_WhileWireIsFiring()
        {
            SimHarness harness = OnGround();
            harness.Step(Inputs.Wire(0f, 1f));
            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Firing), "前提: フックが飛んでいる");

            harness.Step(Attack(0f, 1f, InputButtons.Wire));

            Assert.That(harness.State.Action.IsFree, Is.True);
        }

        [Test]
        public void CannotAttack_WhileWireIsAttached()
        {
            var harness = new SimHarness(TestArenas.Playground(), WireTestSetup.LongAttachConfig(WireModelType.Inertial));
            WireTestSetup.AttachAt(harness, new Vector3(0f, 5f, 0f), new Vector3(3f, 7f, 0f));
            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Attached), "前提: 接続している");

            harness.Step(Attack(0f, 0f, InputButtons.Wire));

            Assert.That(harness.State.Action.IsFree, Is.True, "ワイヤー→攻撃のキャンセルはまだない（T3-3）");
            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Attached));
        }

        [Test]
        public void Validate_RejectsAttackWithoutStartupOrActive()
        {
            var config = new CharacterConfig();
            config.Combat.AirUp.ActiveTicks = 0;

            Assert.Throws<InvalidTuningException>(() => config.Validate());
        }

        [Test]
        public void Rollback_ReproducesAttacks()
        {
            // 攻撃を含む入力で、巻き戻しても全員が本当の入力どおりの状態になる（アクション状態も保存・復元される）。
            var scripts = new PlayerInput[2][];
            for (int p = 0; p < 2; p++)
            {
                scripts[p] = new PlayerInput[800];
                for (int t = 0; t < scripts[p].Length; t++)
                {
                    int phase = (t + p * 17) % 60;
                    float x = phase < 20 ? 1f : phase < 40 ? -1f : 0f;
                    InputButtons buttons = phase % 13 == 0 ? InputButtons.Attack : phase == 45 ? InputButtons.Jump : InputButtons.None;
                    scripts[p][t] = PlayerInput.FromAxes(x, phase % 26 == 0 ? 1f : 0f, buttons);
                }
            }
            var network = new PseudoNetwork(scripts, new RollbackSettings(), 5, 2, 0.05, 3);

            network.RunUntil(600, maxFrames: 3000);

            ulong expected = network.Reference(600).Checksum();
            Assert.That(network.Worlds[0].Checksum(), Is.EqualTo(expected));
            Assert.That(network.Worlds[1].Checksum(), Is.EqualTo(expected));
            Assert.That(network.Sessions[0].RollbackCount, Is.GreaterThan(0), "前提: 巻き戻している");
        }
    }
}
