using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using NUnit.Framework;

namespace Airerage.Core.Tests.Combat
{
    /// <summary>のけぞり中に床・壁・天井へ勢いよくぶつかったら跳ね返る（ユーザーの要望、2026-10-13）。</summary>
    public class BounceTests
    {
        private static readonly CombatTuning Combat = new CombatTuning();

        // 床（上面 y = 0）、右の壁（x = 5〜6）、天井（下面 y = 10）の部屋。
        private static BoxTerrain Room()
        {
            return new BoxTerrain()
                .AddBox(-20f, -1f, 20f, 0f)
                .AddBox(5f, -1f, 6f, 11f)
                .AddBox(-20f, 10f, 20f, 11f);
        }

        // position にいるキャラクターを、速度 velocity でのけぞらせて（hitstun でなければ Free のまま）1ティックずつ進める。
        private static SimWorld Launch(Vector3 position, Vector3 velocity, bool hitstun = true)
        {
            var world = new SimWorld(Room());
            world.AddCharacter(new CharacterConfig(), position, 1);
            CharacterState state = world.GetState(0);
            state.Velocity = velocity;
            if (hitstun)
            {
                state.Action = new ActionState { Kind = ActionKind.Hitstun, Duration = 60 };
            }
            world.SetState(0, state);
            return world;
        }

        private static void StepUntilBounceOr(SimWorld world, int maxTicks)
        {
            for (int i = 0; i < maxTicks && world.GetState(0).Combat.BounceCount == 0; i++)
            {
                world.Step(new PlayerInput[1]);
            }
        }

        [Test]
        public void FastFallInHitstun_BouncesOffTheFloor()
        {
            SimWorld world = Launch(new Vector3(0f, 3f, 0f), new Vector3(0f, -25f, 0f));
            StepUntilBounceOr(world, 30);

            CharacterState state = world.GetState(0);
            Assert.That(state.Combat.BounceCount, Is.EqualTo(1), "床で跳ね返らない");
            Assert.That(state.Velocity.Y, Is.GreaterThan(0f), "上へ跳ね返っていない");
            Assert.That(state.Grounded, Is.False);
        }

        [Test]
        public void FastKnockbackInHitstun_BouncesOffTheWall()
        {
            SimWorld world = Launch(new Vector3(3f, 5f, 0f), new Vector3(20f, 0f, 0f));
            StepUntilBounceOr(world, 30);

            CharacterState state = world.GetState(0);
            Assert.That(state.Combat.BounceCount, Is.EqualTo(1), "壁で跳ね返らない");
            Assert.That(state.Velocity.X, Is.EqualTo(-20f * Combat.BounceRestitution).Within(0.5f), "反対向きに倍率どおり返らない");
        }

        [Test]
        public void FastLaunchInHitstun_BouncesOffTheCeiling()
        {
            SimWorld world = Launch(new Vector3(0f, 7f, 0f), new Vector3(0f, 25f, 0f));
            StepUntilBounceOr(world, 30);

            Assert.That(world.GetState(0).Combat.BounceCount, Is.EqualTo(1), "天井で跳ね返らない");
            Assert.That(world.GetState(0).Velocity.Y, Is.LessThan(0f));
        }

        [Test]
        public void SlowContact_DoesNotBounce()
        {
            SimWorld world = Launch(new Vector3(4f, 5f, 0f), new Vector3(Combat.BounceMinSpeed * 0.5f, 0f, 0f));
            for (int i = 0; i < 30; i++)
            {
                world.Step(new PlayerInput[1]);
            }

            Assert.That(world.GetState(0).Combat.BounceCount, Is.EqualTo(0));
        }

        [Test]
        public void OutsideHitstun_ThereIsNoBounce()
        {
            SimWorld world = Launch(new Vector3(0f, 3f, 0f), new Vector3(0f, -25f, 0f), hitstun: false);
            for (int i = 0; i < 30; i++)
            {
                world.Step(new PlayerInput[1]);
            }

            Assert.That(world.GetState(0).Combat.BounceCount, Is.EqualTo(0));
            Assert.That(world.GetState(0).Grounded, Is.True, "ふつうに着地する");
        }
    }
}
