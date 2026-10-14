using System;
using System.Numerics;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Tests.Support;
using Degunatsu.Core.Wire;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Wire
{
    /// <summary>
    /// ワイヤーの共通制御（docs/06_prototype_plan.md T1-3）。
    /// </summary>
    public class WireControllerTests
    {
        private const float StandingY = 0.91f;

        private static BoxTerrain GroundWithCeiling(bool attachable)
        {
            return TestArenas.Ground().AddBox(-20f, 8f, 20f, 9f, attachable);
        }

        [Test]
        public void Fire_AtAttachableSurface_AttachesAtHitPoint()
        {
            var harness = new SimHarness(GroundWithCeiling(attachable: true));

            harness.StepUntil(s => s.Wire.Phase == WirePhase.Attached, Inputs.Wire(0f, 1f), 20);

            Assert.That(harness.State.Wire.Anchor.X, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(harness.State.Wire.Anchor.Y, Is.EqualTo(8f).Within(1e-3f));
            Assert.That(harness.State.Wire.IsPulling, Is.True);
        }

        [Test]
        public void Fire_NoTargetInRange_RetractsThenCoolsDownThenReady()
        {
            var harness = new SimHarness(TestArenas.Ground());
            var tuning = harness.Config.Wire;

            int firingTicks = harness.StepUntil(s => s.Wire.Phase == WirePhase.Retracting, Inputs.Wire(0f, 1f), 30);
            Assert.That(harness.State.Wire.LastMissReason, Is.EqualTo(WireMissReason.OutOfRange));
            int expectedFiringTicks = (int)Math.Ceiling(tuning.MaxLength / (tuning.FireSpeed / 60f));
            Assert.That(firingTicks, Is.EqualTo(expectedFiringTicks).Within(1));

            int retractTicks = harness.StepUntil(s => s.Wire.Phase == WirePhase.Cooldown, Inputs.None, 30);
            Assert.That(retractTicks, Is.EqualTo(tuning.MissRetractTicks));

            int cooldownTicks = harness.StepUntil(s => s.Wire.Phase == WirePhase.Ready, Inputs.None, 60);
            Assert.That(cooldownTicks, Is.EqualTo(tuning.CooldownTicks));
        }

        [Test]
        public void Fire_DuringCooldown_IsIgnored()
        {
            var harness = new SimHarness(TestArenas.Ground());
            harness.StepUntil(s => s.Wire.Phase == WirePhase.Cooldown, Inputs.Wire(0f, 1f), 60);

            harness.Step(Inputs.None);
            harness.Step(Inputs.Wire(0f, 1f));

            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Cooldown));
        }

        [Test]
        public void Fire_HitsNonAttachableSurface_Misses()
        {
            var harness = new SimHarness(GroundWithCeiling(attachable: false));

            harness.StepUntil(s => s.Wire.Phase == WirePhase.Retracting, Inputs.Wire(0f, 1f), 20);

            Assert.That(harness.State.Wire.LastMissReason, Is.EqualTo(WireMissReason.HitNonAttachable));
        }

        [Test]
        public void ReleaseButton_WhileFiring_Misses()
        {
            var harness = new SimHarness(GroundWithCeiling(attachable: true));

            harness.Step(Inputs.Wire(0f, 1f));
            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Firing), "前提: 射出中");
            harness.Step(Inputs.None);

            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Retracting));
            Assert.That(harness.State.Wire.LastMissReason, Is.EqualTo(WireMissReason.ButtonReleased));
        }

        [Test]
        public void AimAssist_PicksAnchorClosestToAimWithinAngle()
        {
            // どちらもアシストの角度の内側にある2つのアンカーポイントのうち、照準に近い方を選ぶ。
            float assist = new CharacterConfig().Wire.AimAssistAngleDeg;
            float height = 5f;
            var world = TestArenas.Ground()
                .AddAnchor(TanDeg(assist * 0.8f) * height, StandingY + height)
                .AddAnchor(TanDeg(assist * 0.3f) * height, StandingY + height)
                .AddAnchor(TanDeg(-20f) * height, StandingY + height);
            var harness = new SimHarness(world);

            harness.Step(Inputs.Wire(0f, 1f));

            Assert.That(harness.State.Wire.HasTargetPoint, Is.True);
            Assert.That(harness.State.Wire.TargetPoint.X, Is.EqualTo(TanDeg(assist * 0.3f) * height).Within(1e-4f));

            harness.StepUntil(s => s.Wire.Phase == WirePhase.Attached, Inputs.Wire(0f, 1f), 20);
            Assert.That(harness.State.Wire.Anchor.X, Is.EqualTo(TanDeg(assist * 0.3f) * height).Within(1e-4f));
        }

        [Test]
        public void AimAssist_IgnoresAnchorOutsideAngle()
        {
            float assist = new CharacterConfig().Wire.AimAssistAngleDeg;
            var world = TestArenas.Ground().AddAnchor(TanDeg(assist * 1.3f) * 5f, StandingY + 5f);
            var harness = new SimHarness(world);

            harness.Step(Inputs.Wire(0f, 1f));

            Assert.That(harness.State.Wire.HasTargetPoint, Is.False);
        }

        [Test]
        public void AimAssist_IgnoresAnchorBeyondRange()
        {
            var world = TestArenas.Ground().AddAnchor(0f, StandingY + 15f);
            var harness = new SimHarness(world);

            harness.Step(Inputs.Wire(0f, 1f));

            Assert.That(harness.State.Wire.HasTargetPoint, Is.False);
        }

        [Test]
        public void AimAssist_IgnoresBlockedAnchor_AndRawAimHitsBlocker()
        {
            var world = TestArenas.Ground()
                .AddBox(-1f, 3f, 1f, 3.5f)
                .AddAnchor(0f, 6f);
            var harness = new SimHarness(world);

            harness.Step(Inputs.Wire(0f, 1f));
            Assert.That(harness.State.Wire.HasTargetPoint, Is.False);

            harness.StepUntil(s => s.Wire.Phase == WirePhase.Retracting, Inputs.Wire(0f, 1f), 20);
            Assert.That(harness.State.Wire.LastMissReason, Is.EqualTo(WireMissReason.HitNonAttachable));
        }

        [Test]
        public void AimDirection_NoStick_UsesFacingAndNeutralAngle()
        {
            var tuning = new WireTuning();

            Vector3 left = WireTargeting.AimDirection(Vector2.Zero, -1, tuning);
            Vector3 right = WireTargeting.AimDirection(Vector2.Zero, 1, tuning);

            float c = MathF.Cos(tuning.NeutralAimAngleDeg * MathF.PI / 180f);
            float s = MathF.Sin(tuning.NeutralAimAngleDeg * MathF.PI / 180f);
            Assert.That(left.X, Is.EqualTo(-c).Within(1e-5f));
            Assert.That(left.Y, Is.EqualTo(s).Within(1e-5f));
            Assert.That(right.X, Is.EqualTo(c).Within(1e-5f));
        }

        [Test]
        public void ReleaseButton_WhileAttached_ReturnsToReadyWithReason()
        {
            var harness = new SimHarness(GroundWithCeiling(attachable: true));
            harness.StepUntil(s => s.Wire.Phase == WirePhase.Attached, Inputs.Wire(0f, 1f), 20);

            harness.Step(Inputs.None);

            // 解除後のクールダウンは既定で 0（ユーザーの指示）
            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Ready));
            Assert.That(harness.State.Wire.LastReleaseReason, Is.EqualTo(WireReleaseReason.ButtonReleased));
        }

        [Test]
        public void ReleaseThenPressAgain_FiresOnTheNextTick()
        {
            var harness = new SimHarness(GroundWithCeiling(attachable: true));
            harness.StepUntil(s => s.Wire.Phase == WirePhase.Attached, Inputs.Wire(0f, 1f), 20);
            harness.Step(Inputs.None);

            harness.Step(Inputs.Wire(0f, 1f));

            Assert.That(harness.State.Wire.Phase, Is.Not.EqualTo(WirePhase.Ready).And.Not.EqualTo(WirePhase.Cooldown),
                "解除の次のティックに押せば射出できる");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Release_AfterUsingUpAirJumps_RestoresThemWhenEnabled(bool restore)
        {
            var config = new CharacterConfig();
            config.Wire.RestoreAirJumpsOnRelease = restore;
            var harness = new SimHarness(GroundWithCeiling(attachable: true), config);
            int airJumps = config.Movement.AirJumps;

            // ジャンプして、空中ジャンプを使い切る。
            harness.Step(Inputs.Jump);
            for (int i = 0; i < airJumps; i++)
            {
                harness.Step(Inputs.None, 5);
                harness.Step(Inputs.Jump);
            }
            Assert.That(harness.State.AirJumpsUsed, Is.EqualTo(airJumps), "前提: 空中ジャンプを使い切った");

            // 接続して、すぐに解除する。
            harness.StepUntil(s => s.Wire.Phase == WirePhase.Attached, Inputs.Wire(0f, 1f), 20);
            harness.Step(Inputs.None);
            Assert.That(harness.State.Grounded, Is.False, "前提: 空中で解除した");
            Assert.That(harness.State.AirJumpsUsed, Is.EqualTo(restore ? 0 : airJumps));

            // 回復したなら、もう一度空中ジャンプできる（回復しなければ、空中ジャンプは起きない）。
            harness.Step(Inputs.Jump);
            Assert.That(harness.State.AirJumpsUsed, Is.EqualTo(airJumps));
            if (restore)
            {
                Assert.That(harness.State.Velocity.Y, Is.EqualTo(config.Movement.JumpSpeed).Within(1e-4f));
            }
        }

        [Test]
        public void Miss_DoesNotRestoreAirJumps()
        {
            var harness = new SimHarness(TestArenas.Ground());
            harness.Step(Inputs.Jump);
            harness.Step(Inputs.None, 5);
            harness.Step(Inputs.Jump);
            int used = harness.State.AirJumpsUsed;
            Assert.That(used, Is.GreaterThan(0), "前提: 空中ジャンプを使った");

            // 何もない方向に撃って外す（接続しない）。
            harness.StepUntil(s => s.Wire.Phase == WirePhase.Retracting, Inputs.Wire(0f, 1f), 30);

            Assert.That(harness.State.Grounded, Is.False);
            Assert.That(harness.State.AirJumpsUsed, Is.EqualTo(used));
        }

        [Test]
        public void ReleaseCooldownTicks_WhenSet_WaitsBeforeReady()
        {
            var config = new CharacterConfig();
            config.Wire.ReleaseCooldownTicks = 10;
            var harness = new SimHarness(GroundWithCeiling(attachable: true), config);
            harness.StepUntil(s => s.Wire.Phase == WirePhase.Attached, Inputs.Wire(0f, 1f), 20);

            harness.Step(Inputs.None);
            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Cooldown));

            int ticks = harness.StepUntil(s => s.Wire.Phase == WirePhase.Ready, Inputs.None, 30);
            Assert.That(ticks, Is.EqualTo(10));
        }

        [Test]
        public void Release_ClampsSpeedToExitCapAndKeepsDirection()
        {
            var tuning = new WireTuning();
            var state = new CharacterState
            {
                Velocity = new Vector3(30f, 40f, 0f),
                Wire = new WireState { Phase = WirePhase.Attached },
            };

            WireController.Release(ref state, WireReleaseReason.ButtonReleased, tuning);

            Assert.That(state.Velocity.Length(), Is.EqualTo(tuning.ExitSpeedCap).Within(1e-4f));
            Assert.That(state.Velocity.X / state.Velocity.Y, Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(state.Wire.Phase, Is.EqualTo(WirePhase.Ready));
        }

        [TestCase(0f)]
        [TestCase(1f)]
        public void Attach_MovingAway_KeepsOnlyTheRedirectedShare_OfTheSpeed(float redirect)
        {
            // 右へ速く飛んでいる間に、左上の壁へフックを刺す（接続点から遠ざかる向きに動いている）。
            // 標準（AttachRedirect 0）は、遠ざかる向きの速さをロープが止める。1 なら速さを保ったまま、振りの向きに曲がる。
            // どちらも、接続点の方へ跳ね返らない（以前の「反転」は廃止した）。
            var world = new BoxTerrain().AddBox(-7f, -20f, -6f, 30f, attachable: true);
            var config = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            config.Wire.AttachRedirect = redirect;
            config.Wire.Inertial.StartPullSpeed = 0f;
            var harness = new SimHarness(world, config, new Vector3(0f, 5f, 0f));
            var state = harness.State;
            state.Grounded = false;
            state.Velocity = new Vector3(12f, 4f, 0f);
            harness.State = state;

            Vector3 before = harness.State.Velocity;
            harness.StepUntil(s =>
            {
                if (s.Wire.Phase != WirePhase.Attached)
                {
                    before = s.Velocity;
                    return false;
                }
                return true;
            }, Inputs.Wire(-1f, 0.6f), 30);

            Vector3 after = harness.State.Velocity;
            Vector3 radial = Vector3.Normalize(harness.State.Position - harness.State.Wire.Anchor);
            Assert.That(Vector3.Dot(after, radial), Is.GreaterThan(-1f), $"接続点の方へ跳ね返った: 前 {before}、後 {after}");
            if (redirect > 0f)
            {
                Assert.That(after.Length(), Is.EqualTo(before.Length()).Within(1f), $"速さが保たれない: 前 {before}、後 {after}");
                Assert.That(MathF.Abs(Vector3.Dot(after, radial)), Is.LessThan(1f), "振りの向き（ロープに垂直）になっていない");
            }
            else
            {
                Assert.That(after.Length(), Is.LessThan(before.Length() * 0.6f), $"遠ざかる向きの速さが残る: 前 {before}、後 {after}");
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LineBlocked_WhileAttached_ReleasesOnlyWhenEnabled(bool releaseWhenBlocked)
        {
            var world = GroundWithCeiling(attachable: true).AddBox(2f, 4f, 3f, 6f);
            var config = new CharacterConfig();
            config.Wire.ReleaseWhenLineBlocked = releaseWhenBlocked;
            var harness = new SimHarness(world, config, spawn: new Vector3(5f, 3f, 0f));
            var state = harness.State;
            state.Wire = new WireState { Phase = WirePhase.Attached, Anchor = new Vector3(0f, 8f, 0f), RopeLength = 7.1f };
            state.PreviousButtons = Degunatsu.Core.Input.InputButtons.Wire;
            harness.State = state;

            harness.Step(Inputs.Wire());

            if (releaseWhenBlocked)
            {
                Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Ready));
                Assert.That(harness.State.Wire.LastReleaseReason, Is.EqualTo(WireReleaseReason.LineBlocked));
            }
            else
            {
                // ロープは地形を通り抜け、接続を保つ。ロープの長さの拘束は続く。
                Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Attached));
                Assert.That(WireTestSetup.DistanceToAnchor(harness.State), Is.LessThanOrEqualTo(harness.State.Wire.RopeLength + 1e-3f));
            }
        }

        [Test]
        public void Fire_HitsNonAttachableSurface_KeepsTheHitPointAsHookPosition()
        {
            var harness = new SimHarness(GroundWithCeiling(attachable: false));

            harness.StepUntil(s => s.Wire.Phase == WirePhase.Retracting, Inputs.Wire(0f, 1f), 20);

            Assert.That(harness.State.Wire.HookPosition.Y, Is.EqualTo(8f).Within(1e-3f), "外れた位置が天井の面にない");
        }

        [Test]
        public void HangingBesideWall_RopeAlongWallFace_IsNotBlocked()
        {
            // 左の壁（内側の面 x = -30）と天井（下面 y = 10）の角に接続し、壁に押し付けながらぶら下がる。
            // ロープが壁面とほぼ平行になっても、壁に遮られたと誤判定しない（docs/06_prototype_plan.md T1-7）。
            var harness = new SimHarness(TestArenas.Playground(), spawn: new Vector3(-29.55f, 5f, 0f), facing: -1);
            var state = harness.State;
            state.Wire = new WireState { Phase = WirePhase.Attached, Anchor = new Vector3(-30f, 10f, 0f), RopeLength = 5f };
            state.PreviousButtons = Degunatsu.Core.Input.InputButtons.Wire;
            harness.State = state;

            for (int tick = 0; tick < 60; tick++)
            {
                harness.Step(Inputs.Wire(-1f, 0f));
                Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Attached),
                    $"{tick} ティック目に外れた（理由 {harness.State.Wire.LastReleaseReason}）: {harness.State}");
            }
        }

        [Test]
        public void MaxAttachTicks_ForcesRelease()
        {
            var config = new CharacterConfig();
            config.Wire.MaxAttachTicks = 30;
            var harness = new SimHarness(GroundWithCeiling(attachable: true), config);
            harness.StepUntil(s => s.Wire.Phase == WirePhase.Attached, Inputs.Wire(0f, 1f), 20);

            int ticks = harness.StepUntil(s => s.Wire.Phase != WirePhase.Attached, Inputs.Wire(0f, 1f), 60);

            Assert.That(ticks, Is.EqualTo(30));
            Assert.That(harness.State.Wire.LastReleaseReason, Is.EqualTo(WireReleaseReason.MaxDuration));
        }

        [Test]
        public void ToggleMode_StaysAttachedAfterButtonUp_AndSecondPressReleases()
        {
            var config = new CharacterConfig();
            config.Wire.ButtonMode = WireButtonMode.Toggle;
            var harness = new SimHarness(GroundWithCeiling(attachable: true), config);

            harness.Step(Inputs.Wire(0f, 1f));
            harness.StepUntil(s => s.Wire.Phase == WirePhase.Attached, Inputs.Stick(0f, 1f), 20);
            harness.Step(Inputs.None, 5);
            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Attached), "トグル式では離しても接続を保つ");

            harness.Step(Inputs.Wire());

            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Ready));
            Assert.That(harness.State.Wire.LastReleaseReason, Is.EqualTo(WireReleaseReason.ButtonToggled));
        }

        private static float TanDeg(float degrees)
        {
            return MathF.Tan(degrees * MathF.PI / 180f);
        }
    }
}
