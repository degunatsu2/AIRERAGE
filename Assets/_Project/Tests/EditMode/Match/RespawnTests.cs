using System.Numerics;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Input;
using Degunatsu.Core.Match;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Tests.Support;
using Degunatsu.Core.Wire;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Match
{
    /// <summary>撃破された後の復帰待ちと、復帰した後の無敵（docs/12_match_plan.md T4-2）。</summary>
    public class RespawnTests
    {
        private const float GroundY = 0.91f;
        private static readonly PlayerInput Attack = new PlayerInput(0, 0, InputButtons.Attack);
        private static readonly StageBounds Bounds = new StageBounds(-20f, -10f, 20f, 30f);
        private static readonly MatchRules Rules = new MatchRules();

        // 幅 8m（x = -4〜4）の浮いた足場。右へ歩くと落ちる。
        private static SimWorld FallingWorld(Vector3 spawn, MatchRules rules = null)
        {
            var world = new SimWorld(new BoxTerrain().AddBox(-4f, -1f, 4f, 0f), Bounds, null, rules ?? MatchRules.FreePlay());
            world.AddCharacter(new CharacterConfig(), spawn);
            return world;
        }

        private static void WalkOff(SimWorld world)
        {
            for (int i = 0; i < 300 && world.GetState(0).Combat.Falls == 0; i++)
            {
                world.Step(new[] { Inputs.Right });
            }
            Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(1), "前提: ステージの外に出た");
        }

        // 0 が 1 に地上技を出す（1 は 1.6m 先）。1 の状態は adjust で変える。
        private static SimWorld AttackWorld(System.Func<CharacterState, CharacterState> adjust)
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(1.6f, GroundY, 0f), -1);
            world.Step(new PlayerInput[2]);
            world.SetState(1, adjust(world.GetState(1)));
            return world;
        }

        private static int HitsWhileAttacking(SimWorld world)
        {
            int hits = 0;
            world.Step(new[] { Attack, default });
            for (int i = 0; i < 40; i++)
            {
                world.Step(new PlayerInput[2]);
                hits += world.LastHits.Count;
            }
            return hits;
        }

        [Test]
        public void KnockedOut_WaitsAtTheSpawn_ThenComesBack()
        {
            var spawn = new Vector3(3f, GroundY, 0f);
            SimWorld world = FallingWorld(spawn);
            WalkOff(world);

            CharacterState waiting = world.GetState(0);
            Assert.That(waiting.Combat.RespawnTicksLeft, Is.EqualTo(Rules.RespawnDelayTicks));
            Assert.That(waiting.Combat.InvincibleTicksLeft, Is.EqualTo(Rules.RespawnDelayTicks + Rules.InvincibleTicks));
            Assert.That(waiting.Position, Is.EqualTo(spawn), "出現位置へ移っていない");

            for (int i = 0; i < Rules.RespawnDelayTicks; i++)
            {
                world.Step(new[] { Inputs.Right });
                Assert.That(world.GetState(0).Position, Is.EqualTo(spawn), $"待っている間に動いた（{i} ティック目）");
            }
            Assert.That(world.GetState(0).Combat.IsWaitingToRespawn, Is.False, "待ち終わらない");
            Assert.That(world.GetState(0).Combat.InvincibleTicksLeft, Is.EqualTo(Rules.InvincibleTicks), "復帰した後の無敵");

            world.Step(new[] { Inputs.Right });
            Assert.That(world.GetState(0).Position.X, Is.GreaterThan(spawn.X), "復帰した後に動けない");
            Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(1));
        }

        [Test]
        public void WaitingCharacter_IsNotKnockedOutAgain()
        {
            // 出現位置がステージの外でも、待っている間は撃破にしない（待ち終わってから撃破になる）。
            var world = new SimWorld(TestArenas.Ground(), Bounds, null, MatchRules.FreePlay());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, 50f, 0f));
            world.Step(new PlayerInput[1]);
            Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(1), "前提: 外にいるので撃破");

            for (int i = 0; i < Rules.RespawnDelayTicks - 1; i++)
            {
                world.Step(new PlayerInput[1]);
            }
            Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(1), "待っている間に、もう一度撃破になった");
            world.Step(new PlayerInput[1]);
            Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(2), "待ち終わったら撃破になる");
        }

        [Test]
        public void WaitingCharacter_CannotBeHit()
        {
            SimWorld world = AttackWorld(s =>
            {
                s.Combat.RespawnTicksLeft = 100;
                s.Combat.InvincibleTicksLeft = 100 + Rules.InvincibleTicks;
                return s;
            });
            Assert.That(HitsWhileAttacking(world), Is.EqualTo(0), "復帰を待っている相手に当たった");
        }

        [Test]
        public void AfterRespawn_IsInvincible_ThenCanBeHit()
        {
            SimWorld world = AttackWorld(s =>
            {
                s.Combat.InvincibleTicksLeft = 60;
                return s;
            });
            Assert.That(HitsWhileAttacking(world), Is.EqualTo(0), "無敵の相手に当たった");
            Assert.That(world.GetState(1).Combat.InvincibleTicksLeft, Is.EqualTo(60 - 41), "無敵が数えられていない");

            for (int i = 0; i < 60; i++)
            {
                world.Step(new PlayerInput[2]);
            }
            // 位置を戻して、もう一度当てる。
            CharacterState attacker = world.GetState(0);
            attacker.Position = new Vector3(world.GetState(1).Position.X - 1.6f, GroundY, 0f);
            attacker.Facing = 1;
            world.SetState(0, attacker);
            Assert.That(HitsWhileAttacking(world), Is.GreaterThan(0), "無敵が終わっても当たらない");
        }

        [Test]
        public void AttackingEndsTheInvincibility()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            world.Step(new PlayerInput[1]);
            CharacterState state = world.GetState(0);
            state.Combat.InvincibleTicksLeft = Rules.InvincibleTicks;
            world.SetState(0, state);

            world.Step(new[] { Attack });

            Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Attack), "前提: 攻撃した");
            Assert.That(world.GetState(0).Combat.InvincibleTicksLeft, Is.EqualTo(0), "攻撃しても無敵が終わらない");
        }

        [Test]
        public void ButtonHeldWhileWaiting_IsNotAPress_AfterComingBack()
        {
            SimWorld world = FallingWorld(new Vector3(3f, GroundY, 0f));
            WalkOff(world);

            PlayerInput holdWire = Inputs.Wire(0f, 1f);
            for (int i = 0; i < Rules.RespawnDelayTicks + 5; i++)
            {
                world.Step(new[] { holdWire });
            }
            Assert.That(world.GetState(0).Wire.Phase, Is.EqualTo(WirePhase.Ready), "押し続けていたワイヤーが、戻った直後に出た");

            world.Step(new PlayerInput[1]);
            world.Step(new[] { holdWire });
            Assert.That(world.GetState(0).Wire.Phase, Is.EqualTo(WirePhase.Firing), "押し直すと出る");
        }

        [Test]
        public void NoDelay_ComesBackAtOnce_WithInvincibility()
        {
            var rules = MatchRules.FreePlay();
            rules.RespawnDelayTicks = 0;
            SimWorld world = FallingWorld(new Vector3(3f, GroundY, 0f), rules);
            WalkOff(world);

            Assert.That(world.GetState(0).Combat.IsWaitingToRespawn, Is.False);
            Assert.That(world.GetState(0).Combat.InvincibleTicksLeft, Is.EqualTo(rules.InvincibleTicks));
        }
    }
}
