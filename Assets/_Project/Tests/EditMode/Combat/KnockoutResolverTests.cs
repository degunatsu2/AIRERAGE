using System;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Physics;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using Airerage.Core.Wire;
using NUnit.Framework;

namespace Airerage.Core.Tests.Combat
{
    /// <summary>ステージの外と撃破（docs/11_combat_plan.md T3-3）。</summary>
    public class KnockoutResolverTests
    {
        private const float GroundY = 0.91f;
        private static readonly PlayerInput Attack = new PlayerInput(0, 0, InputButtons.Attack);
        private static readonly StageBounds Bounds = new StageBounds(-20f, -10f, 20f, 30f);

        // 幅 8m（x = -4〜4）の浮いた足場。
        private static BoxTerrain SmallPlatform()
        {
            return new BoxTerrain().AddBox(-4f, -1f, 4f, 0f);
        }

        // 出現位置に戻ったか（撃破の数以外は出現したときの状態）。
        private static void AssertRespawned(CharacterState state, Vector3 spawn)
        {
            Assert.That(state.Position, Is.EqualTo(new Vector3(spawn.X, spawn.Y, 0f)), "出現位置に戻っていない");
            Assert.That(state.Velocity, Is.EqualTo(Vector3.Zero));
            Assert.That(state.Action.IsFree, Is.True);
            Assert.That(state.Wire.Phase, Is.EqualTo(WirePhase.Ready));
            Assert.That(state.Combat.HasLastAttacker, Is.False, "最後にダメージを与えた人が残っている");
            Assert.That(state.Combat.ComboCount, Is.EqualTo(0));
        }

        [Test]
        public void WalkingOffTheStage_CountsAFall_WithoutCredit_AndRespawns()
        {
            var world = new SimWorld(SmallPlatform(), Bounds);
            var spawn = new Vector3(3f, GroundY, 0f);
            world.AddCharacter(new CharacterConfig(), spawn);

            int ticks = 0;
            while (world.GetState(0).Combat.Falls == 0 && ticks < 300)
            {
                world.Step(new[] { Inputs.Right });
                ticks++;
            }

            CharacterState state = world.GetState(0);
            Assert.That(state.Combat.Falls, Is.EqualTo(1), "300 ティック以内にステージの外に出なかった");
            Assert.That(state.Combat.Knockouts, Is.EqualTo(0), "だれにも攻撃されていなければ、手柄はつかない");
            AssertRespawned(state, spawn);
        }

        [Test]
        public void KnockedOffTheStage_CreditsTheAttacker()
        {
            // 足場の右端の近くにいる相手に地上技を当て、足場の外へ飛ばす。
            var world = new SimWorld(SmallPlatform(), Bounds);
            world.AddCharacter(new CharacterConfig(), new Vector3(1.4f, GroundY, 0f), 1);
            var victimSpawn = new Vector3(3f, GroundY, 0f);
            world.AddCharacter(new CharacterConfig(), victimSpawn, -1);
            world.Step(new PlayerInput[2]);
            world.Step(new[] { Attack, default });

            int ticks = 0;
            while (world.GetState(1).Combat.Falls == 0 && ticks < 300)
            {
                world.Step(new PlayerInput[2]);
                ticks++;
            }

            CharacterState victim = world.GetState(1);
            Assert.That(victim.Combat.Falls, Is.EqualTo(1), "300 ティック以内にステージの外に出なかった");
            Assert.That(world.GetState(0).Combat.Knockouts, Is.EqualTo(1), "当てた人の撃破になる");
            Assert.That(victim.Combat.Knockouts, Is.EqualTo(0));
            AssertRespawned(victim, victimSpawn);
        }

        [Test]
        public void Credit_HasNoTimeLimit()
        {
            // 当ててから 20 秒たっても、最後にダメージを与えた人の撃破になる（D-40）。
            var world = new SimWorld(TestArenas.Ground(), Bounds);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(1.6f, GroundY, 0f), -1);
            world.Step(new PlayerInput[2]);
            world.Step(new[] { Attack, default });
            for (int i = 0; i < 1200; i++)
            {
                world.Step(new PlayerInput[2]);
            }
            CharacterState victim = world.GetState(1);
            Assert.That(victim.Combat.HasLastAttacker, Is.True, "前提: 当たっている");
            Assert.That(world.GetState(1).Combat.Falls, Is.EqualTo(0), "前提: まだ外に出ていない");

            victim.Position = new Vector3(0f, -15f, 0f);
            world.SetState(1, victim);
            world.Step(new PlayerInput[2]);

            Assert.That(world.GetState(1).Combat.Falls, Is.EqualTo(1));
            Assert.That(world.GetState(0).Combat.Knockouts, Is.EqualTo(1));
        }

        [Test]
        public void Credit_GoesToTheLastAttacker()
        {
            var world = new SimWorld(TestArenas.Ground(), Bounds);
            for (int i = 0; i < 3; i++)
            {
                world.AddCharacter(new CharacterConfig(), new Vector3(i * 3f, GroundY, 0f));
            }
            CharacterState victim = world.GetState(2);
            victim.Combat.HasLastAttacker = true;
            victim.Combat.LastAttacker = 1; // 0 が先に当て、その後に 1 が当てた
            victim.Position = new Vector3(25f, 5f, 0f); // 右の辺から出る
            world.SetState(2, victim);

            world.Step(new PlayerInput[3]);

            Assert.That(world.GetState(0).Combat.Knockouts, Is.EqualTo(0));
            Assert.That(world.GetState(1).Combat.Knockouts, Is.EqualTo(1));
            Assert.That(world.GetState(2).Combat.Falls, Is.EqualTo(1));
        }

        [Test]
        public void TwoCharactersOutOnTheSameTick_BothAreCounted()
        {
            var world = new SimWorld(TestArenas.Ground(), Bounds);
            world.AddCharacter(new CharacterConfig(), new Vector3(-2f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(2f, GroundY, 0f));
            for (int i = 0; i < 2; i++)
            {
                CharacterState state = world.GetState(i);
                state.Combat.HasLastAttacker = true;
                state.Combat.LastAttacker = (byte)(1 - i);
                state.Combat.Knockouts = 3;
                state.Position = new Vector3(0f, 40f, 0f); // 上の辺から出る
                world.SetState(i, state);
            }

            world.Step(new PlayerInput[2]);

            for (int i = 0; i < 2; i++)
            {
                Assert.That(world.GetState(i).Combat.Knockouts, Is.EqualTo(4), $"キャラクター {i}: 撃破した数を引き継いで +1");
                Assert.That(world.GetState(i).Combat.Falls, Is.EqualTo(1), $"キャラクター {i}");
            }
        }

        [Test]
        public void Respawn_DoesNotFireTheWire_WhileTheButtonIsHeld()
        {
            var world = new SimWorld(TestArenas.Ground(), Bounds);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            world.Step(new[] { Inputs.Wire() });
            CharacterState state = world.GetState(0);
            state.Position = new Vector3(0f, -15f, 0f);
            world.SetState(0, state);

            world.Step(new[] { Inputs.Wire() }); // ここで撃破されて戻る
            world.Step(new[] { Inputs.Wire() }); // 押し続けている

            Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(1), "前提: 撃破された");
            Assert.That(world.GetState(0).Wire.Phase, Is.EqualTo(WirePhase.Ready), "押し続けているだけで射出した");
        }

        [Test]
        public void WithoutBounds_ThereIsNoKnockout()
        {
            var world = new SimWorld(SmallPlatform());
            world.AddCharacter(new CharacterConfig(), new Vector3(3f, GroundY, 0f));
            for (int i = 0; i < 300; i++)
            {
                world.Step(new[] { Inputs.Right });
            }

            Assert.That(world.GetState(0).Position.Y, Is.LessThan(-50f), "前提: 落ち続けている");
            Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(0));
        }

        [Test]
        public void Knockout_IsReproducedAfterRollback()
        {
            var world = new SimWorld(SmallPlatform(), Bounds);
            world.AddCharacter(new CharacterConfig(), new Vector3(1.4f, GroundY, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(3f, GroundY, 0f), -1);
            world.Step(new PlayerInput[2]);
            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);

            ulong first = RunAttackAndFall(world);
            world.LoadFrom(snapshot);
            ulong second = RunAttackAndFall(world);

            Assert.That(world.GetState(1).Combat.Falls, Is.EqualTo(1), "前提: 撃破が起きる");
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void StageBounds_RejectsAnEmptyRange_AndTheEdgeIsOutside()
        {
            Assert.Throws<ArgumentException>(() => new StageBounds(5f, 0f, 5f, 10f));
            Assert.Throws<ArgumentException>(() => new StageBounds(0f, 10f, 5f, 0f));
            Assert.That(Bounds.Contains(new Vector3(0f, 0f, 0f)), Is.True);
            Assert.That(Bounds.Contains(new Vector3(20f, 0f, 0f)), Is.False);
            Assert.That(Bounds.Contains(new Vector3(0f, -10f, 0f)), Is.False);
        }

        private static ulong RunAttackAndFall(SimWorld world)
        {
            world.Step(new[] { Attack, default });
            for (int i = 0; i < 200; i++)
            {
                world.Step(new PlayerInput[2]);
            }
            return world.Checksum();
        }
    }
}
