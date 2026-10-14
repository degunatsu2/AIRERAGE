using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Movement;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using NUnit.Framework;

namespace Airerage.Core.Tests.Simulation
{
    /// <summary>ステージのギミック（ジャンプ台、上昇気流、加速リング。StageZone / StageZones。ユーザーの要望、2026-10-13）。</summary>
    public class StageZoneTests
    {
        private static readonly MovementTuning Movement = new MovementTuning();

        // 床（上面 y = 0）。立つと中心は y = HalfHeight。
        private static BoxTerrain Floor()
        {
            return new BoxTerrain().AddBox(-40f, -1f, 40f, 0f);
        }

        private static StageZone Pad(float power = 24f)
        {
            return new StageZone(StageZoneKind.JumpPad, -1f, 0f, 1f, 0.3f, Vector2.UnitY, power);
        }

        private static SimWorld World(params StageZone[] zones)
        {
            var world = new SimWorld(Floor(), null, zones);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, Movement.HalfHeight, 0f), 1);
            return world;
        }

        private static void Step(SimWorld world, int ticks, PlayerInput input = default)
        {
            var inputs = new PlayerInput[world.CharacterCount];
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = input;
            }
            for (int i = 0; i < ticks; i++)
            {
                world.Step(inputs);
            }
        }

        private static void SetState(SimWorld world, Vector3 position, Vector3 velocity)
        {
            CharacterState state = world.GetState(0);
            state.Position = position;
            state.Velocity = velocity;
            world.SetState(0, state);
        }

        [Test]
        public void JumpPad_LaunchesAStandingCharacterHigh()
        {
            SimWorld world = World(Pad());
            Step(world, 1);

            CharacterState state = world.GetState(0);
            Assert.That(state.Velocity.Y, Is.EqualTo(24f).Within(1e-4f), "ジャンプ台の速さで打ち上がらない");
            Assert.That(state.Grounded, Is.False);
            Assert.That(state.JumpPadCount, Is.EqualTo(1));

            float highest = state.Position.Y;
            for (int i = 0; i < 90; i++)
            {
                Step(world, 1);
                highest = MathF.Max(highest, world.GetState(0).Position.Y);
            }
            // 高さ v² / 2g = 9.6m（ふつうのジャンプは 2.4m）。
            Assert.That(highest - Movement.HalfHeight, Is.GreaterThan(9f), "高く上がらない");
        }

        [Test]
        public void JumpPad_DoesNotLaunchAgain_WhileFlyingOut()
        {
            SimWorld world = World(Pad());
            Step(world, 10);

            Assert.That(world.GetState(0).JumpPadCount, Is.EqualTo(1), "打ち上がった後に、もう一度打ち上げた");
        }

        [Test]
        public void JumpPad_LaunchesAgain_AfterLanding()
        {
            SimWorld world = World(Pad());
            Step(world, 150);

            Assert.That(world.GetState(0).JumpPadCount, Is.GreaterThanOrEqualTo(2), "落ちてきたらまた打ち上がる");
        }

        [Test]
        public void JumpPad_RestoresTheAirJump()
        {
            SimWorld world = World(Pad());
            SetState(world, new Vector3(0f, 3f, 0f), new Vector3(0f, -5f, 0f));
            CharacterState state = world.GetState(0);
            state.AirJumpsUsed = Movement.AirJumps;
            world.SetState(0, state);

            for (int i = 0; i < 60 && world.GetState(0).JumpPadCount == 0; i++)
            {
                Step(world, 1);
            }

            Assert.That(world.GetState(0).JumpPadCount, Is.EqualTo(1));
            Assert.That(world.GetState(0).AirJumpsUsed, Is.EqualTo(0), "空中ジャンプが戻らない");
        }

        [Test]
        public void JumpPad_AlsoLaunchesACharacterInHitstun()
        {
            SimWorld world = World(Pad());
            CharacterState state = world.GetState(0);
            state.Action = new ActionState { Kind = ActionKind.Hitstun, Duration = 60 };
            world.SetState(0, state);
            Step(world, 1);

            Assert.That(world.GetState(0).Velocity.Y, Is.EqualTo(24f).Within(1e-4f), "のけぞり中も打ち上がる");
        }

        [Test]
        public void JumpPad_DoesNothing_DuringHitstop()
        {
            SimWorld world = World(Pad());
            CharacterState state = world.GetState(0);
            state.Combat.HitstopTicksLeft = 5;
            world.SetState(0, state);
            Step(world, 5);

            Assert.That(world.GetState(0).JumpPadCount, Is.EqualTo(0), "ヒットストップで止まっている間は効かない");
            Step(world, 1);
            Assert.That(world.GetState(0).JumpPadCount, Is.EqualTo(1), "止まりが解けたら効く");
        }

        [Test]
        public void DiagonalPad_KeepsTheSidewaysSpeed_AndSetsTheSpeedAlongItsDirection()
        {
            var direction = Vector2.Normalize(new Vector2(1f, 1f));
            SimWorld world = World(new StageZone(StageZoneKind.JumpPad, -1f, 0f, 1f, 0.3f, new Vector2(1f, 1f), 20f));
            Step(world, 1);

            Vector3 v = world.GetState(0).Velocity;
            Assert.That(v.X * direction.X + v.Y * direction.Y, Is.EqualTo(20f).Within(1e-3f), "向きの速さが Power にならない");
            Assert.That(v.X, Is.GreaterThan(5f), "斜め上に飛ばない");
        }

        [Test]
        public void Updraft_LiftsAStandingCharacter_UpToTheLimit()
        {
            SimWorld world = World(new StageZone(StageZoneKind.Updraft, -2f, 0f, 2f, 20f, Vector2.Zero, 50f, 10f));
            float fastest = 0f;
            for (int i = 0; i < 60; i++)
            {
                Step(world, 1);
                fastest = MathF.Max(fastest, world.GetState(0).Velocity.Y);
            }

            Assert.That(world.GetState(0).Position.Y, Is.GreaterThan(Movement.HalfHeight + 3f), "上昇気流で浮かない");
            Assert.That(fastest, Is.LessThanOrEqualTo(10f + 1e-4f), "上向きの速さが上限を超えた");
        }

        [Test]
        public void Updraft_SlowsAFall()
        {
            SimWorld plain = World();
            SimWorld lifted = World(new StageZone(StageZoneKind.Updraft, -2f, 0f, 2f, 20f, Vector2.Zero, 50f, 10f));
            SetState(plain, new Vector3(0f, 15f, 0f), new Vector3(0f, -20f, 0f));
            SetState(lifted, new Vector3(0f, 15f, 0f), new Vector3(0f, -20f, 0f));
            Step(plain, 20);
            Step(lifted, 20);

            Assert.That(lifted.GetState(0).Velocity.Y, Is.GreaterThan(plain.GetState(0).Velocity.Y + 5f), "落ちる速さが緩まない");
        }

        [Test]
        public void BoostRing_SpeedsUpACharacterPassingThrough_Once()
        {
            SimWorld world = World(new StageZone(StageZoneKind.BoostRing, 2f, 3f, 3f, 7f, Vector2.UnitX, 20f));
            SetState(world, new Vector3(0f, 5f, 0f), new Vector3(8f, 6f, 0f));
            for (int i = 0; i < 30 && world.GetState(0).BoostRingCount == 0; i++)
            {
                Step(world, 1);
            }

            Assert.That(world.GetState(0).BoostRingCount, Is.EqualTo(1), "加速リングに入っても加速しない");
            Assert.That(world.GetState(0).Velocity.X, Is.EqualTo(20f).Within(1e-3f), "向きの速さが Power にならない");
            Step(world, 3);
            Assert.That(world.GetState(0).BoostRingCount, Is.EqualTo(1), "中にいる間にもう一度加速した");
        }

        [Test]
        public void BoostRing_CatchesACharacterThatPassesThroughInOneTick()
        {
            SimWorld world = World(new StageZone(StageZoneKind.BoostRing, 2f, 3f, 3f, 7f, Vector2.UnitX, 20f));
            // 1 ティックで 5m 進む（x = 0 → 5）。始まりと終わりのどちらでもリングに重ならない。
            SetState(world, new Vector3(0f, 5f, 0f), new Vector3(300f, 0f, 0f));
            Step(world, 1);

            Assert.That(world.GetState(0).BoostRingCount, Is.EqualTo(1), "通り抜けたのに見つからない");
            Assert.That(world.GetState(0).Velocity.X, Is.GreaterThan(290f), "もっと速いキャラクターを遅くした");
        }

        [Test]
        public void BoostRing_DoesNotSlowAFasterCharacter()
        {
            SimWorld world = World(new StageZone(StageZoneKind.BoostRing, 2f, 3f, 3f, 7f, Vector2.UnitX, 20f));
            SetState(world, new Vector3(0f, 5f, 0f), new Vector3(30f, 3f, 0f));
            Step(world, 6);

            Assert.That(world.GetState(0).BoostRingCount, Is.EqualTo(1));
            Assert.That(world.GetState(0).Velocity.X, Is.EqualTo(30f).Within(0.5f));
        }

        [Test]
        public void BoostRing_WorksBothWays()
        {
            // 左向きに通り抜ける（リングの向きは右）。左向きに加速する（ユーザーの要望、2026-10-13）。
            SimWorld world = World(new StageZone(StageZoneKind.BoostRing, 2f, 3f, 3f, 7f, Vector2.UnitX, 20f));
            SetState(world, new Vector3(5f, 5f, 0f), new Vector3(-10f, 3f, 0f));
            for (int i = 0; i < 30 && world.GetState(0).BoostRingCount == 0; i++)
            {
                Step(world, 1);
            }

            Assert.That(world.GetState(0).BoostRingCount, Is.EqualTo(1), "逆向きに通っても効かない");
            Assert.That(world.GetState(0).Velocity.X, Is.EqualTo(-20f).Within(1e-3f), "通っている向きに加速しない");
        }

        [Test]
        public void BoostRing_DoesNothing_WhenEnteredSideways()
        {
            // 右向きのリングに、真上から真下へ入る（横の速さ 0）。どちらへ加速するか決まらないので効かない。
            SimWorld world = World(new StageZone(StageZoneKind.BoostRing, -1f, 3f, 1f, 5f, Vector2.UnitX, 20f));
            SetState(world, new Vector3(0f, 8f, 0f), new Vector3(0f, -5f, 0f));
            Step(world, 40);

            Assert.That(world.GetState(0).Position.Y, Is.LessThan(3f), "前提: リングを通り抜けた");
            Assert.That(world.GetState(0).BoostRingCount, Is.EqualTo(0));
            Assert.That(world.GetState(0).Velocity.X, Is.EqualTo(0f));
        }

        [Test]
        public void ZonesGivenInADifferentOrder_GiveTheSameResult()
        {
            StageZone[] zones =
            {
                Pad(),
                new StageZone(StageZoneKind.Updraft, -1f, 0f, 3f, 12f, Vector2.Zero, 40f, 8f),
                new StageZone(StageZoneKind.BoostRing, 4f, 2f, 5f, 9f, new Vector2(1f, 0.5f), 18f),
                new StageZone(StageZoneKind.JumpPad, 6f, 0f, 9f, 0.3f, new Vector2(-0.3f, 1f), 20f),
            };
            var reversed = (StageZone[])zones.Clone();
            Array.Reverse(reversed);
            SimWorld a = World(zones);
            SimWorld b = World(reversed);

            Assert.That(b.Zones, Is.EqualTo(a.Zones), "決まった順に並べていない");
            for (int i = 0; i < 240; i++)
            {
                var input = PlayerInput.FromAxes(i % 120 < 60 ? 1f : -0.5f, 0f, i % 40 == 20 ? InputButtons.Jump : InputButtons.None);
                a.Step(new[] { input });
                b.Step(new[] { input });
                Assert.That(b.Checksum(), Is.EqualTo(a.Checksum()), $"ティック {a.Tick} で結果が違う");
            }
            Assert.That(a.GetState(0).JumpPadCount, Is.GreaterThan(0), "前提: ギミックが効いた");
        }

        [Test]
        public void RollingBack_GivesTheSameResult()
        {
            SimWorld world = World(Pad(), new StageZone(StageZoneKind.BoostRing, -3f, 4f, -2f, 8f, Vector2.UnitY, 15f));
            var snapshot = new WorldSnapshot();
            Step(world, 20);
            world.SaveTo(snapshot);
            Step(world, 100);
            ulong expected = world.Checksum();

            world.LoadFrom(snapshot);
            Step(world, 100);

            Assert.That(world.Checksum(), Is.EqualTo(expected));
        }

        [Test]
        public void InvalidZones_AreRejected()
        {
            Assert.Throws<ArgumentException>(() => new StageZone(StageZoneKind.JumpPad, 1f, 0f, 1f, 1f, Vector2.UnitY, 10f), "幅 0");
            Assert.Throws<ArgumentException>(() => new StageZone(StageZoneKind.JumpPad, 0f, 0f, 1f, 1f, Vector2.Zero, 10f), "向き 0");
            Assert.Throws<ArgumentException>(() => new StageZone(StageZoneKind.BoostRing, 0f, 0f, 1f, 1f, Vector2.UnitX, 0f), "強さ 0");
            Assert.Throws<ArgumentException>(() => new StageZone(StageZoneKind.Updraft, 0f, 0f, 1f, 1f, Vector2.Zero, 10f, 0f), "上限 0");
            Assert.Throws<ArgumentException>(() => new StageZone(StageZoneKind.JumpPad, 0f, 0f, float.NaN, 1f, Vector2.UnitY, 10f), "NaN");
        }

        [Test]
        public void Direction_IsNormalized()
        {
            var zone = new StageZone(StageZoneKind.JumpPad, 0f, 0f, 1f, 1f, new Vector2(0f, 3f), 10f);
            Assert.That(zone.Direction, Is.EqualTo(Vector2.UnitY));
        }

        [Test]
        public void Respawn_KeepsTheGimmickCounts()
        {
            var world = new SimWorld(Floor(), new StageBounds(-10f, -10f, 10f, 30f), new List<StageZone> { Pad() });
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, Movement.HalfHeight, 0f), 1);
            Step(world, 1);
            SetState(world, new Vector3(0f, -20f, 0f), Vector3.Zero);
            Step(world, 1);

            Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(1), "前提: 撃破された");
            Assert.That(world.GetState(0).JumpPadCount, Is.GreaterThanOrEqualTo(1), "撃破で数が 0 に戻った（表示が増えたことを見逃す）");
        }
    }
}
