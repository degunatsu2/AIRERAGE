using System.Numerics;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Input;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Tests.Support;
using Degunatsu.Core.Wire;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Combat
{
    /// <summary>ガードとジャストガード（docs/11_combat_plan.md T3-5。D-21、D-39）。</summary>
    public class GuardTests
    {
        private const float GroundY = 0.91f;
        private static readonly CombatTuning Combat = new CombatTuning();
        private static readonly PlayerInput Guard = new PlayerInput(0, 0, InputButtons.Guard);
        private static readonly PlayerInput Attack = new PlayerInput(0, 0, InputButtons.Attack);

        private static SimWorld OneCharacter(float y = GroundY)
        {
            var world = new SimWorld(y > 50f ? new BoxTerrain() : TestArenas.Ground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, y, 0f), 1);
            world.Step(new PlayerInput[1]);
            return world;
        }

        // 0 が右向きで攻撃し、1（左向き）が受ける。guardLead: 1 がガードを押してから、0 が攻撃を押すまでのティック数
        // （負なら、0 が攻撃を押してから -guardLead ティック後に 1 がガードを押す）。当たるかガードされるまで進める。
        private static SimWorld AttackIntoGuard(int guardLead)
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(1.6f, GroundY, 0f), -1);
            world.Step(new PlayerInput[2]);
            int attackTick = guardLead > 0 ? guardLead : 0;
            int guardTick = guardLead > 0 ? 0 : -guardLead;
            for (int t = 0; t < 60; t++)
            {
                PlayerInput a = t == attackTick ? Attack : default;
                PlayerInput g = t == guardTick ? Guard : default;
                world.Step(new[] { a, g });
                if (world.LastHits.Count > 0)
                {
                    return world;
                }
            }
            Assert.Fail("攻撃が届かなかった");
            return world;
        }

        [Test]
        public void Guard_LastsGuardTicks_ThenTheCooldownStarts()
        {
            SimWorld world = OneCharacter();
            world.Step(new[] { Guard });
            Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Guard));

            for (int i = 1; i < Combat.GuardTicks; i++)
            {
                world.Step(new PlayerInput[1]);
                Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Guard), $"ティック {i}");
            }
            world.Step(new PlayerInput[1]);

            Assert.That(world.GetState(0).Action.IsFree, Is.True, "ガードが終わらない");
            Assert.That(world.GetState(0).Combat.GuardCooldownTicksLeft, Is.EqualTo(Combat.GuardCooldownTicks));
        }

        [Test]
        public void Guard_CannotBeUsedDuringTheCooldown()
        {
            SimWorld world = OneCharacter();
            world.Step(new[] { Guard });
            for (int i = 0; i < Combat.GuardTicks + 10; i++)
            {
                world.Step(new PlayerInput[1]);
            }
            world.Step(new[] { Guard });
            Assert.That(world.GetState(0).Action.IsFree, Is.True, "クールタイム中にガードできた");

            for (int i = 0; i < Combat.GuardCooldownTicks; i++)
            {
                world.Step(new PlayerInput[1]);
            }
            world.Step(new[] { Guard });
            Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Guard), "クールタイムの後にガードできない");
        }

        [Test]
        public void Guard_WorksInTheAir()
        {
            SimWorld world = OneCharacter(100f);
            world.Step(new[] { Guard });
            Assert.That(world.GetState(0).Grounded, Is.False);
            Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Guard));
        }

        [Test]
        public void JustGuard_NoKnockback_ResetsTheCooldown_AndStunsTheAttacker()
        {
            // 地上技の1段目は攻撃を始めてから 5 ティック目に出る。その少し前にガードを押す（ジャストガードの受付 6 ティック以内）。
            SimWorld world = AttackIntoGuard(-3);

            Assert.That(world.LastHits[0].Result, Is.EqualTo(HitResult.JustGuarded));
            CharacterState defender = world.GetState(1);
            CharacterState attacker = world.GetState(0);
            Assert.That(defender.Action.IsFree, Is.True, "ジャストガードの後もガードの硬直がある");
            Assert.That(defender.Velocity.X, Is.EqualTo(0f), "ジャストガードで吹き飛んだ");
            Assert.That(defender.Combat.GuardCooldownTicksLeft, Is.EqualTo(0));
            Assert.That(defender.Combat.JustGuardCount, Is.EqualTo(1));
            Assert.That(defender.Combat.HasLastAttacker, Is.False, "ジャストガードでダメージを受けた");
            Assert.That(defender.Combat.ComboCount, Is.EqualTo(0));
            Assert.That(attacker.Action.Kind, Is.EqualTo(ActionKind.Hitstun), "攻撃した側に隙ができない");
            Assert.That(attacker.Action.Duration, Is.EqualTo(Combat.ParriedStunTicks));
        }

        [Test]
        public void JustGuard_LetsTheDefenderGuardAgainRightAway()
        {
            SimWorld world = AttackIntoGuard(-3);
            while (world.GetState(1).Combat.HitstopTicksLeft > 0)
            {
                world.Step(new PlayerInput[2]);
            }

            world.Step(new[] { default, Guard });

            Assert.That(world.GetState(1).Action.Kind, Is.EqualTo(ActionKind.Guard), "ジャストガードの後にすぐガードできない（D-39）");
        }

        [Test]
        public void NormalGuard_PushesBack_WithoutHitstunOrCombo_AndBlocksTheCancel()
        {
            // 攻撃を押す 4 ティック前にガードする（当たるときはガードを始めて 9 ティック目。ジャストガードではない）。
            SimWorld world = AttackIntoGuard(4);

            HitRecord hit = world.LastHits[0];
            Assert.That(hit.Result, Is.EqualTo(HitResult.Guarded));
            CharacterState defender = world.GetState(1);
            Assert.That(defender.Action.Kind, Is.EqualTo(ActionKind.Guard), "ガードのまま");
            Assert.That(defender.Velocity.X, Is.EqualTo(hit.Knockback.X * Combat.GuardKnockbackScale).Within(1e-4f), "ノックバック");
            Assert.That(defender.Combat.ComboCount, Is.EqualTo(0), "ガードしたのにコンボになった");
            Assert.That(defender.Combat.GuardedCount, Is.EqualTo(1));
            Assert.That(defender.Combat.HasLastAttacker, Is.True, "削りとして最後にダメージを与えた人を記録する【仮】");
            Assert.That(defender.Action.Duration, Is.GreaterThanOrEqualTo(defender.Action.Ticks + Combat.GuardStunTicks), "ガード硬直");

            CharacterState attacker = world.GetState(0);
            Assert.That(attacker.Action.Connected, Is.False, "ガードされたのに当たった扱い");
            Assert.That(attacker.Action.HitMask, Is.Not.EqualTo(0u), "同じ判定がもう一度当たる");
        }

        [Test]
        public void NormalGuard_IsExtendedByTheGuardStun()
        {
            // ガードが終わる直前に受けても、ガード硬直（GuardStunTicks）の間はガードのまま（その間の攻撃も防げる）。
            int lead = Combat.GuardTicks - Combat.Ground.FirstHitTick - 4;
            SimWorld world = AttackIntoGuard(lead);
            Assert.That(world.LastHits[0].Result, Is.EqualTo(HitResult.Guarded), "前提: ふつうのガード");
            int guardTicksAtHit = world.GetState(1).Action.Ticks;
            Assert.That(guardTicksAtHit + Combat.GuardStunTicks, Is.GreaterThan(Combat.GuardTicks), "前提: 硬直がなければ先に終わる");

            while (world.GetState(1).Combat.HitstopTicksLeft > 0)
            {
                world.Step(new PlayerInput[2]);
            }
            int guarding = 0;
            while (world.GetState(1).Action.Kind == ActionKind.Guard && guarding < 100)
            {
                world.Step(new PlayerInput[2]);
                guarding++;
            }

            Assert.That(guarding, Is.EqualTo(Combat.GuardStunTicks + 1), "ガード硬直の間、ガードが続かない");
            Assert.That(world.GetState(1).Combat.GuardCooldownTicksLeft, Is.EqualTo(Combat.GuardCooldownTicks));
        }

        [Test]
        public void Guard_WhileAttached_ReleasesTheWire()
        {
            var harness = new SimHarness(TestArenas.Playground(), WireTestSetup.LongAttachConfig(WireModelType.Inertial));
            WireTestSetup.AttachAt(harness, new Vector3(0f, 5f, 0f), new Vector3(3f, 7f, 0f), new Vector3(0f, 6f, 0f));
            harness.Step(Inputs.Wire(), 3);
            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Attached), "前提: 接続している");

            harness.Step(new PlayerInput(0, 0, InputButtons.Wire | InputButtons.Guard));

            Assert.That(harness.State.Action.Kind, Is.EqualTo(ActionKind.Guard));
            Assert.That(harness.State.Wire.Phase, Is.Not.EqualTo(WirePhase.Attached));
            Assert.That(harness.State.Wire.LastReleaseReason, Is.EqualTo(WireReleaseReason.Guard));
        }

        [Test]
        public void Guard_IsNotPossible_DuringAnAttack_OrWhileTheHookIsFlying()
        {
            SimWorld attacking = OneCharacter();
            attacking.Step(new[] { Attack });
            attacking.Step(new[] { Guard });
            Assert.That(attacking.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Attack), "攻撃中にガードできた");

            var harness = new SimHarness(TestArenas.Playground(), WireTestSetup.LongAttachConfig(WireModelType.Inertial));
            harness.Step(Inputs.Wire(0f, 1f));
            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Firing), "前提: フックが飛んでいる");
            harness.Step(new PlayerInput(0, 127, InputButtons.Wire | InputButtons.Guard));
            Assert.That(harness.State.Action.IsFree, Is.True, "射出中にガードできた");
        }

        [Test]
        public void GuardAndAttackOnTheSameTick_Guards()
        {
            SimWorld world = OneCharacter();
            world.Step(new[] { new PlayerInput(0, 0, InputButtons.Guard | InputButtons.Attack) });
            Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Guard));
            Assert.That(world.GetState(0).Combat.AttackBufferTicksLeft, Is.EqualTo(0), "ガードの後に攻撃が出る");
        }
    }
}
