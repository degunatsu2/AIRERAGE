using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Movement;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using Airerage.Core.Wire;
using NUnit.Framework;

namespace Airerage.Core.Tests.Combat
{
    /// <summary>キャンセル規則（docs/11_combat_plan.md T3-4。D-17〜D-20、D-42）。</summary>
    public class CancelTests
    {
        private const float GroundY = 0.91f;
        private static readonly CombatTuning Combat = new CombatTuning();
        private static readonly MovementTuning Movement = new MovementTuning();

        private static PlayerInput Press(InputButtons buttons, float x = 0f, float y = 0f)
        {
            return PlayerInput.FromAxes(x, y, buttons);
        }

        // 地形のない空中に1人だけ置く（しばらく落ち続ける）。
        private static SimWorld AirWorld()
        {
            var world = new SimWorld(new BoxTerrain());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, 100f, 0f), 1);
            return world;
        }

        private static void Step(SimWorld world, int ticks, PlayerInput input = default)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.Step(new[] { input });
            }
        }

        // 今の攻撃が相手に当たったことにする（命中の仕組みは HitResolverTests で確かめる）。
        private static void MarkHit(SimWorld world)
        {
            CharacterState state = world.GetState(0);
            state.Action.HitMask = HitResolver.HitBit(0, 1);
            state.Action.Connected = true;
            world.SetState(0, state);
        }

        [Test]
        public void AttackToAttack_IsNotPossible_WithoutAHit()
        {
            SimWorld world = AirWorld();
            world.Step(new[] { Press(InputButtons.Attack) });
            Step(world, 6);

            world.Step(new[] { Press(InputButtons.Attack, 1f) });

            CharacterState state = world.GetState(0);
            Assert.That(state.Action.Attack, Is.EqualTo(AttackSlot.AirNeutral), "空振りなのにキャンセルした");
            Assert.That(state.Action.Ticks, Is.EqualTo(7));
        }

        [Test]
        public void AttackToAttack_OnHit_OnlyOncePerChain()
        {
            SimWorld world = AirWorld();
            world.Step(new[] { Press(InputButtons.Attack) });
            Step(world, 6);
            MarkHit(world);

            world.Step(new[] { Press(InputButtons.Attack, 1f) });
            CharacterState first = world.GetState(0);
            Assert.That(first.Action.Attack, Is.EqualTo(AttackSlot.AirSide), "当たったのにキャンセルできない");
            Assert.That(first.Action.Ticks, Is.EqualTo(0));
            Assert.That(first.Combat.AttackCancelUsed, Is.True);

            Step(world, 8);
            MarkHit(world);
            world.Step(new[] { Press(InputButtons.Attack, 0f, 1f) });
            CharacterState second = world.GetState(0);
            Assert.That(second.Action.Attack, Is.EqualTo(AttackSlot.AirSide), "2回目のキャンセルができてしまった");
            Assert.That(second.Action.Ticks, Is.EqualTo(9));
        }

        [Test]
        public void AttackCancel_FacesTheNewDirection()
        {
            SimWorld world = AirWorld();
            world.Step(new[] { Press(InputButtons.Attack) });
            Step(world, 6);
            MarkHit(world);

            world.Step(new[] { Press(InputButtons.Attack, -1f) });

            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.AirSide));
            Assert.That(world.GetState(0).Facing, Is.EqualTo(-1), "横の攻撃は入れた向きを向く");
        }

        [Test]
        public void GroundAttackToAirAttack_OnHit_DoesNotUseTheAttackCancel()
        {
            // 地上技が当たったら、空中に出た後に空中の攻撃でキャンセルできる。これは攻撃→攻撃の1回に数えない（D-42）。
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(1.0f, GroundY, 0f), -1);
            world.Step(new PlayerInput[2]);
            world.Step(new[] { Press(InputButtons.Attack), default });
            for (int t = 0; t < 40 && !world.GetState(0).Action.Connected; t++)
            {
                world.Step(new PlayerInput[2]);
            }
            Assert.That(world.GetState(0).Action.Connected, Is.True, "前提: 地上技が当たった");

            // 1段目が当たった瞬間（地上にいる間）に押しても（先行入力）、空中に出てからキャンセルする。
            world.Step(new[] { Press(InputButtons.Attack, 0f, 1f), default });
            for (int t = 0; t < 40 && world.GetState(0).Action.Attack == AttackSlot.Ground; t++)
            {
                world.Step(new PlayerInput[2]);
            }

            CharacterState state = world.GetState(0);
            Assert.That(state.Grounded, Is.False);
            Assert.That(state.Action.Attack, Is.EqualTo(AttackSlot.AirUp), "地上技→空中の攻撃にキャンセルしない");
            Assert.That(state.Combat.AttackCancelUsed, Is.False, "地上技→空中の攻撃を回数に数えた");
        }

        [Test]
        public void GroundAttack_CannotCancelIntoAnotherGroundAttack()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f), 1);
            world.Step(new PlayerInput[1]);
            world.Step(new[] { Press(InputButtons.Attack) });
            Step(world, 6);
            MarkHit(world);

            world.Step(new[] { Press(InputButtons.Attack) });

            CharacterState state = world.GetState(0);
            Assert.That(state.Grounded, Is.True, "前提: まだ地上（1段目）");
            Assert.That(state.Action.Attack, Is.EqualTo(AttackSlot.Ground));
            Assert.That(state.Action.Ticks, Is.EqualTo(7), "地上技→地上技にキャンセルした");
        }

        [Test]
        public void JumpCancel_OnHit_UsesAnAirJump_AndKeepsTheChain()
        {
            SimWorld world = AirWorld();
            world.Step(new[] { Press(InputButtons.Attack) });
            Step(world, 6);
            MarkHit(world);
            world.Step(new[] { Press(InputButtons.Attack, 1f) }); // 攻撃→攻撃を使う
            Step(world, 8);
            MarkHit(world);

            world.Step(new[] { Press(InputButtons.Jump) });

            CharacterState state = world.GetState(0);
            Assert.That(state.Action.IsFree, Is.True, "ジャンプでキャンセルしない");
            Assert.That(state.AirJumpsUsed, Is.EqualTo(1));
            Assert.That(state.Velocity.Y, Is.EqualTo(Movement.JumpSpeed).Within(1f), "ジャンプしていない");
            Assert.That(state.Combat.AttackCancelUsed, Is.True, "ジャンプで連携が戻った");
        }

        [Test]
        public void JumpCancel_NeedsAHit_AndAnAirJumpLeft()
        {
            SimWorld whiff = AirWorld();
            whiff.Step(new[] { Press(InputButtons.Attack) });
            Step(whiff, 6);
            whiff.Step(new[] { Press(InputButtons.Jump) });
            Assert.That(whiff.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Attack), "空振りなのにジャンプでキャンセルした");

            SimWorld noJumps = AirWorld();
            CharacterState state = noJumps.GetState(0);
            state.AirJumpsUsed = Movement.AirJumps;
            noJumps.SetState(0, state);
            noJumps.Step(new[] { Press(InputButtons.Attack) });
            Step(noJumps, 6);
            MarkHit(noJumps);
            noJumps.Step(new[] { Press(InputButtons.Jump) });
            Assert.That(noJumps.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Attack), "空中ジャンプが残っていないのにキャンセルした");
        }

        [Test]
        public void WireCancel_FiresTheWire_AndStartsTheCooldown()
        {
            // 攻撃→ワイヤーは当たっていなくてもできる。持続の開始（WireCancelFromTick）から（D-20）。使うとクールタイム（P-1）。
            SimWorld world = AirWorld();
            world.Step(new[] { Press(InputButtons.Attack) });
            int from = Combat.AirNeutral.WireCancelFromTick;
            Step(world, from - 2);
            world.Step(new[] { Press(InputButtons.Wire, 0f, 1f) });
            Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Attack), "発生中にワイヤーでキャンセルした");

            world.Step(new PlayerInput[1]);
            world.Step(new[] { Press(InputButtons.Wire, 0f, 1f) });

            CharacterState state = world.GetState(0);
            Assert.That(state.Action.IsFree, Is.True, "ワイヤーでキャンセルしない");
            Assert.That(state.Wire.Phase, Is.EqualTo(WirePhase.Firing), "ワイヤーを射出していない");
            Assert.That(state.Combat.WireCancelCooldownTicksLeft, Is.EqualTo(Combat.WireCancelCooldownTicks));
        }

        [Test]
        public void WireCancel_IsNotPossible_DuringTheCooldown_ButTheWireStillFiresWhenFree()
        {
            SimWorld world = AirWorld();
            CharacterState start = world.GetState(0);
            start.Combat.WireCancelCooldownTicksLeft = 100;
            world.SetState(0, start);

            world.Step(new[] { Press(InputButtons.Attack) });
            Step(world, Combat.AirNeutral.WireCancelFromTick + 1);
            world.Step(new[] { Press(InputButtons.Wire, 0f, 1f) });
            Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Attack), "クールタイム中にワイヤーでキャンセルした");

            Step(world, Combat.AirNeutral.TotalTicks);
            Assert.That(world.GetState(0).Action.IsFree, Is.True, "前提: 攻撃が終わった");
            world.Step(new[] { Press(InputButtons.Wire, 0f, 1f) });
            Assert.That(world.GetState(0).Wire.Phase, Is.EqualTo(WirePhase.Firing), "クールタイムは通常の射出に影響しない");
        }

        [Test]
        public void Chain_IsResetByAttachingTheWire()
        {
            var harness = new SimHarness(TestArenas.Playground(), WireTestSetup.LongAttachConfig(WireModelType.Inertial));
            CharacterState state = harness.State;
            state.Combat.AttackCancelUsed = true;
            harness.State = state;

            WireTestSetup.AttachAt(harness, new Vector3(0f, 5f, 0f), new Vector3(3f, 7f, 0f));
            harness.Step(Inputs.Wire());

            Assert.That(harness.State.Combat.AttackCancelUsed, Is.False);
        }

        [Test]
        public void Chain_IsResetWhenTheAttackEndsWithoutACancel()
        {
            SimWorld world = AirWorld();
            CharacterState state = world.GetState(0);
            state.Combat.AttackCancelUsed = true;
            world.SetState(0, state);

            world.Step(new[] { Press(InputButtons.Attack) });
            Step(world, Combat.AirNeutral.TotalTicks - 2);
            Assert.That(world.GetState(0).Combat.AttackCancelUsed, Is.True, "前提: 攻撃の間は残る");
            Step(world, 2);

            Assert.That(world.GetState(0).Action.IsFree, Is.True);
            Assert.That(world.GetState(0).Combat.AttackCancelUsed, Is.False);
        }

        [Test]
        public void AttackPressedDuringHitstop_IsUsedAfterTheHitstop()
        {
            // 空中で密着した相手にニュートラルを当て、ヒットストップの間に上＋攻撃を1ティックだけ押して離す。
            var world = new SimWorld(new BoxTerrain());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, 100f, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(0.6f, 100f, 0f), -1);
            world.Step(new[] { Press(InputButtons.Attack), default });
            for (int t = 0; t < 20 && world.GetState(0).Combat.HitstopTicksLeft == 0; t++)
            {
                world.Step(new PlayerInput[2]);
            }
            Assert.That(world.GetState(0).Combat.HitstopTicksLeft, Is.GreaterThan(1), "前提: 当ててヒットストップ中");

            world.Step(new[] { Press(InputButtons.Attack, 0f, 1f), default });
            while (world.GetState(0).Combat.HitstopTicksLeft > 0)
            {
                world.Step(new PlayerInput[2]);
            }
            world.Step(new PlayerInput[2]);

            CharacterState state = world.GetState(0);
            Assert.That(state.Action.Attack, Is.EqualTo(AttackSlot.AirUp), "ヒットストップ中に押した攻撃が出ない");
            Assert.That(state.Action.Ticks, Is.EqualTo(0));
            Assert.That(state.Combat.HitstopPresses, Is.EqualTo(InputButtons.None));
        }

        [Test]
        public void JumpPressedDuringHitstop_CancelsAfterTheHitstop()
        {
            var world = new SimWorld(new BoxTerrain());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, 100f, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(0.6f, 100f, 0f), -1);
            world.Step(new[] { Press(InputButtons.Attack), default });
            for (int t = 0; t < 20 && world.GetState(0).Combat.HitstopTicksLeft == 0; t++)
            {
                world.Step(new PlayerInput[2]);
            }

            world.Step(new[] { Press(InputButtons.Jump), default });
            while (world.GetState(0).Combat.HitstopTicksLeft > 0)
            {
                world.Step(new PlayerInput[2]);
            }
            world.Step(new PlayerInput[2]);

            CharacterState state = world.GetState(0);
            Assert.That(state.Action.IsFree, Is.True, "ヒットストップ中に押したジャンプでキャンセルしない");
            Assert.That(state.AirJumpsUsed, Is.EqualTo(1));
        }

        [Test]
        public void AttackBuffer_StartsTheAttackWhenTheRecoveryEnds()
        {
            // 攻撃の終わる少し前に押すと（当たっていないのでキャンセルはできない）、終わったティックに次の攻撃が出る。
            SimWorld world = AirWorld();
            world.Step(new[] { Press(InputButtons.Attack) });
            int total = Combat.AirNeutral.TotalTicks;
            Step(world, total - 4);
            world.Step(new[] { Press(InputButtons.Attack, 0f, 1f) });
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.AirNeutral), "前提: まだ前の攻撃");

            Step(world, 3);

            Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Attack));
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.AirUp), "先行入力の攻撃が出ない");
        }
    }
}
