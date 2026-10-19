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
    /// <summary>
    /// ステージのギミック（ジャンプ台、上昇気流、加速リング。StageZone / StageZones。ユーザーの要望、2026-10-13）。ステージ固有のギミック
    /// （スピードレーン、発射台、バンパー、風、氷、低重力、ワープゲート。docs/15 6.11）。
    /// </summary>
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
        public void SpeedLane_CarriesAStandingCharacterAlong_AndTheSpeedLastsAfterJumpingOff()
        {
            SimWorld world = World(new StageZone(StageZoneKind.SpeedLane, -5f, 0f, 5f, 0.3f, Vector2.UnitX, 16f));
            Step(world, 1);
            Assert.That(world.GetState(0).Velocity.X, Is.EqualTo(16f).Within(1e-4f), "レーンの向きの速さにならない");

            // 走ったまま跳ぶと、その速さで飛んでいく（同じ向きに入れている間）。
            Step(world, 1, PlayerInput.FromAxes(1f, 0f, InputButtons.Jump));
            for (int i = 0; i < 10; i++)
            {
                Step(world, 1, PlayerInput.FromAxes(1f, 0f, InputButtons.None));
            }
            CharacterState state = world.GetState(0);
            Assert.That(state.Grounded, Is.False, "前提: 跳んだ");
            Assert.That(state.Velocity.X, Is.GreaterThan(14f), "跳んだ後に速さを保たない（跳んだティックに地面の加速の分だけ減る）");
        }

        [Test]
        public void SpeedLane_DoesNothing_InTheAir()
        {
            SimWorld world = World(new StageZone(StageZoneKind.SpeedLane, -5f, 0f, 5f, 3f, Vector2.UnitX, 16f));
            SetState(world, new Vector3(0f, 2f, 0f), Vector3.Zero);
            Step(world, 1);

            Assert.That(world.GetState(0).Velocity.X, Is.EqualTo(0f), "空中でもレーンが効いた");
        }

        [Test]
        public void Launcher_SetsTheWholeVelocity_WhateverTheSpeedBefore()
        {
            var direction = Vector2.Normalize(new Vector2(1f, 1f));
            SimWorld world = World(new StageZone(StageZoneKind.Launcher, -1f, 0f, 1f, 0.3f, direction, 20f));
            SetState(world, new Vector3(0f, Movement.HalfHeight, 0f), new Vector3(-10f, 0f, 0f));
            CharacterState state = world.GetState(0);
            state.AirJumpsUsed = Movement.AirJumps;
            world.SetState(0, state);
            Step(world, 1);

            state = world.GetState(0);
            Assert.That(state.Velocity.X, Is.EqualTo(direction.X * 20f).Within(1e-4f), "横の速さを残した");
            Assert.That(state.Velocity.Y, Is.EqualTo(direction.Y * 20f).Within(1e-4f));
            Assert.That(state.Grounded, Is.False);
            Assert.That(state.AirJumpsUsed, Is.EqualTo(0), "空中ジャンプが戻らない");
            Assert.That(state.JumpPadCount, Is.EqualTo(1));
            Step(world, 5);
            Assert.That(world.GetState(0).JumpPadCount, Is.EqualTo(1), "飛び出した後に、もう一度打ち出した");
        }

        [Test]
        public void Launcher_AlsoFires_ACharacterArrivingFastFromASpeedLane()
        {
            // スキージャンプ（雪原と海）: スピードレーンで走ってきた速さのまま、発射台の向きに打ち出す。
            var direction = Vector2.Normalize(new Vector2(1f, 0.45f));
            SimWorld world = World(
                new StageZone(StageZoneKind.SpeedLane, -10f, 0f, -1f, 0.3f, Vector2.UnitX, 18f),
                new StageZone(StageZoneKind.Launcher, -1f, 0f, 1f, 0.3f, direction, 22f));
            SetState(world, new Vector3(-8f, Movement.HalfHeight, 0f), Vector3.Zero);
            for (int i = 0; i < 60 && world.GetState(0).JumpPadCount == 0; i++)
            {
                Step(world, 1, PlayerInput.FromAxes(1f, 0f, InputButtons.None));
            }

            CharacterState state = world.GetState(0);
            Assert.That(state.JumpPadCount, Is.EqualTo(1), "速く走ってきた人を打ち出さない");
            Assert.That(state.Velocity.Y, Is.EqualTo(direction.Y * 22f).Within(1e-3f));
        }

        [Test]
        public void Bumper_BouncesUp_WhenFallingOntoIt()
        {
            SimWorld world = World(new StageZone(StageZoneKind.Bumper, -2f, 4f, 2f, 5f, Vector2.Zero, 18f));
            SetState(world, new Vector3(0f, 7f, 0f), new Vector3(0f, -10f, 0f));
            for (int i = 0; i < 60 && world.GetState(0).JumpPadCount == 0; i++)
            {
                Step(world, 1);
            }

            CharacterState state = world.GetState(0);
            Assert.That(state.JumpPadCount, Is.EqualTo(1), "前提: 当たった");
            Assert.That(state.Velocity.Y, Is.GreaterThan(15f), "上へ弾かない");
            Assert.That(state.Velocity.X, Is.EqualTo(0f).Within(1e-4f), "真上から落ちたのに横へ弾いた");
        }

        [Test]
        public void Bumper_BouncesSideways_WhenHitFromTheSide()
        {
            SimWorld world = World(new StageZone(StageZoneKind.Bumper, -1f, 3f, 1f, 7f, Vector2.Zero, 18f));
            SetState(world, new Vector3(-4f, 5f, 0f), new Vector3(15f, 0f, 0f));
            for (int i = 0; i < 30 && world.GetState(0).JumpPadCount == 0; i++)
            {
                Step(world, 1);
            }

            CharacterState state = world.GetState(0);
            Assert.That(state.JumpPadCount, Is.EqualTo(1), "前提: 当たった");
            Assert.That(state.Velocity.X, Is.LessThan(-10f), "横から当たったのに、来た向きへ弾き返さない");
        }

        [Test]
        public void Gust_BlowsOnlyWhileItIsActive()
        {
            var gust = new StageZone(StageZoneKind.Gust, -10f, 5f, 10f, 30f, Vector2.UnitX, 30f, 10f, 120, 60, 0, Vector2.Zero);
            Assert.That(gust.IsActive(0) && gust.IsActive(59), Is.True);
            Assert.That(gust.IsActive(60) || gust.IsActive(119), Is.False);
            Assert.That(gust.IsActive(120), Is.True, "周期で繰り返さない");

            SimWorld blowing = World(gust);
            SetState(blowing, new Vector3(0f, 20f, 0f), Vector3.Zero);
            Step(blowing, 30);
            Assert.That(blowing.GetState(0).Velocity.X, Is.GreaterThan(5f), "吹いている間に流されない");

            var shifted = new StageZone(StageZoneKind.Gust, -10f, 5f, 10f, 30f, Vector2.UnitX, 30f, 10f, 120, 60, 60, Vector2.Zero);
            SimWorld calm = World(shifted);
            SetState(calm, new Vector3(0f, 20f, 0f), Vector3.Zero);
            Step(calm, 30);
            Assert.That(calm.GetState(0).Velocity.X, Is.EqualTo(0f), "止んでいる間に流された");
        }

        [Test]
        public void Gust_StopsAtTheLimit()
        {
            SimWorld world = World(new StageZone(StageZoneKind.Gust, -40f, 5f, 40f, 60f, Vector2.UnitX, 60f, 9f));
            SetState(world, new Vector3(0f, 50f, 0f), Vector3.Zero);
            Step(world, 30);

            Assert.That(world.GetState(0).Velocity.X, Is.LessThanOrEqualTo(9f + 1e-4f), "上限より速くなった");
            Assert.That(world.GetState(0).Velocity.X, Is.GreaterThan(8f));
        }

        [Test]
        public void Ice_KeepsACharacterSliding()
        {
            var ice = new StageZone(StageZoneKind.Ice, -40f, 0f, 40f, 0.3f, Vector2.Zero, 0.1f);
            SimWorld icy = World(ice);
            SimWorld normal = World();
            SetState(icy, new Vector3(0f, Movement.HalfHeight, 0f), new Vector3(8f, 0f, 0f));
            SetState(normal, new Vector3(0f, Movement.HalfHeight, 0f), new Vector3(8f, 0f, 0f));
            Step(icy, 1);
            Step(normal, 1);
            Assert.That(icy.GetState(0).Grounded && normal.GetState(0).Grounded, Is.True, "前提: 立っている");
            Step(icy, 20);
            Step(normal, 20);

            Assert.That(normal.GetState(0).Velocity.X, Is.EqualTo(0f), "前提: ふつうの床では止まる");
            Assert.That(icy.GetState(0).Velocity.X, Is.GreaterThan(4f), "氷の上で滑らない");
        }

        [Test]
        public void LowGravity_LetsACharacterJumpHigher_AndFallSlowly()
        {
            var low = new StageZone(StageZoneKind.LowGravity, -40f, 0f, 40f, 60f, Vector2.Zero, 0.4f, 6f);
            SimWorld world = World(low);
            Step(world, 1);
            Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.Jump));
            float highest = 0f;
            float fastestFall = 0f;
            for (int i = 0; i < 180; i++)
            {
                Step(world, 1);
                highest = MathF.Max(highest, world.GetState(0).Position.Y - Movement.HalfHeight);
                fastestFall = MathF.Min(fastestFall, world.GetState(0).Velocity.Y);
            }

            // ふつうのジャンプは 2.4m。重力 0.4 倍なら 6m ほど。
            Assert.That(highest, Is.GreaterThan(5f), "高く跳べない");
            Assert.That(fastestFall, Is.GreaterThanOrEqualTo(-6f - 1e-4f), "落ちる速さが上限を超えた");
        }

        [Test]
        public void Warp_MovesToTheTarget_KeepingTheVelocity()
        {
            var gate = new StageZone(StageZoneKind.Warp, 3f, 0f, 4f, 3f, Vector2.Zero, 1f, 0f, 0, 0, 0, new Vector2(-20f, 5f));
            SimWorld world = World(gate);
            for (int i = 0; i < 120 && world.GetState(0).Combat.WarpCount == 0; i++)
            {
                Step(world, 1, PlayerInput.FromAxes(1f, 0f, InputButtons.None));
            }

            CharacterState state = world.GetState(0);
            Assert.That(state.Combat.WarpCount, Is.EqualTo(1), "ゲートで移らない");
            Assert.That(state.Position.X, Is.EqualTo(-20f));
            Assert.That(state.Position.Y, Is.EqualTo(5f));
            Assert.That(state.Velocity.X, Is.GreaterThan(5f), "速度を保たない");
            Assert.That(state.Grounded, Is.False);
        }

        [Test]
        public void Warp_DoesNotMove_ACharacterAlreadyInsideTheGate_OrJustWarped()
        {
            var gate = new StageZone(StageZoneKind.Warp, -1f, 0f, 1f, 3f, Vector2.Zero, 1f, 0f, 0, 0, 0, new Vector2(-20f, 5f));
            SimWorld inside = World(gate);
            Step(inside, 10, PlayerInput.FromAxes(1f, 0f, InputButtons.None));
            Assert.That(inside.GetState(0).Combat.WarpCount, Is.EqualTo(0), "中から出ただけで移った");

            SimWorld cooling = World(new StageZone(StageZoneKind.Warp, 3f, 0f, 4f, 3f, Vector2.Zero, 1f, 0f, 0, 0, 0, new Vector2(-20f, 5f)));
            CharacterState state = cooling.GetState(0);
            state.Combat.WarpCooldownTicks = 200;
            cooling.SetState(0, state);
            Step(cooling, 60, PlayerInput.FromAxes(1f, 0f, InputButtons.None));
            Assert.That(cooling.GetState(0).Combat.WarpCount, Is.EqualTo(0), "移った直後に、もう一度移った");
        }

        [Test]
        public void StageGimmicks_RollBackToTheSameResult()
        {
            StageZone[] zones =
            {
                new StageZone(StageZoneKind.SpeedLane, -6f, 0f, -2f, 0.3f, Vector2.UnitX, 14f),
                new StageZone(StageZoneKind.Launcher, 2f, 0f, 3f, 0.3f, new Vector2(-0.4f, 1f), 22f),
                new StageZone(StageZoneKind.Bumper, -3f, 8f, -1f, 9f, Vector2.Zero, 16f),
                new StageZone(StageZoneKind.Gust, -10f, 4f, 10f, 20f, new Vector2(-1f, 0.2f), 25f, 8f, 90, 40, 13, Vector2.Zero),
                new StageZone(StageZoneKind.Ice, 4f, 0f, 12f, 0.3f, Vector2.Zero, 0.2f),
                new StageZone(StageZoneKind.LowGravity, -12f, 10f, 12f, 30f, Vector2.Zero, 0.5f, 7f),
                new StageZone(StageZoneKind.Warp, 13f, 0f, 14f, 4f, Vector2.Zero, 1f, 0f, 0, 0, 0, new Vector2(-8f, 6f)),
            };
            var reversed = (StageZone[])zones.Clone();
            Array.Reverse(reversed);
            SimWorld a = World(zones);
            SimWorld b = World(reversed);
            var snapshot = new WorldSnapshot();
            var checksums = new List<ulong>();
            for (int i = 0; i < 400; i++)
            {
                var input = PlayerInput.FromAxes(i % 150 < 100 ? 1f : -1f, 0f, i % 50 == 25 ? InputButtons.Jump : InputButtons.None);
                if (i == 100)
                {
                    a.SaveTo(snapshot);
                }
                a.Step(new[] { input });
                b.Step(new[] { input });
                Assert.That(b.Checksum(), Is.EqualTo(a.Checksum()), $"並べた順でティック {a.Tick} の結果が違う");
                if (i >= 100)
                {
                    checksums.Add(a.Checksum());
                }
            }

            a.LoadFrom(snapshot);
            for (int i = 100; i < 400; i++)
            {
                var input = PlayerInput.FromAxes(i % 150 < 100 ? 1f : -1f, 0f, i % 50 == 25 ? InputButtons.Jump : InputButtons.None);
                a.Step(new[] { input });
                Assert.That(a.Checksum(), Is.EqualTo(checksums[i - 100]), $"巻き戻した後のティック {a.Tick} の結果が違う");
            }
        }

        [Test]
        public void InvalidStageGimmicks_AreRejected()
        {
            Assert.Throws<ArgumentException>(() => new StageZone(StageZoneKind.Ice, 0f, 0f, 1f, 1f, Vector2.Zero, 1.5f), "氷の強さが 1 より大きい");
            Assert.Throws<ArgumentException>(() => new StageZone(StageZoneKind.LowGravity, 0f, 0f, 1f, 1f, Vector2.Zero, 0.5f, 0f), "低重力の上限 0");
            Assert.Throws<ArgumentException>(() => new StageZone(StageZoneKind.Gust, 0f, 0f, 1f, 1f, Vector2.UnitX, 10f, 5f, 60, 0, 0, Vector2.Zero),
                "風が吹く長さ 0");
            Assert.Throws<ArgumentException>(() => new StageZone(StageZoneKind.Gust, 0f, 0f, 1f, 1f, Vector2.UnitX, 10f, 5f, 60, 61, 0, Vector2.Zero),
                "風が吹く長さが周期より長い");
            Assert.Throws<ArgumentException>(() => new StageZone(StageZoneKind.Warp, 0f, 0f, 2f, 2f, Vector2.Zero, 1f, 0f, 0, 0, 0, new Vector2(1f, 1f)),
                "出口がゲートの中");
            Assert.Throws<ArgumentException>(() => new StageZone(StageZoneKind.SpeedLane, 0f, 0f, 2f, 2f, Vector2.Zero, 10f), "レーンの向き 0");
            var bumper = new StageZone(StageZoneKind.Bumper, 0f, 0f, 2f, 2f, Vector2.Zero, 10f);
            Assert.That(bumper.Direction, Is.EqualTo(Vector2.UnitY), "向きを使わない種類の向きを揃えない");
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
