using System;
using System.Numerics;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using Airerage.Core.Wire;
using NUnit.Framework;

namespace Airerage.Core.Tests.Wire
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

        // 速度 velocity で飛んでいる間に、照準 aim の向きの壁や天井へフックを刺し、接続した瞬間の速度と、その後 ticks ティックの位置を返す。
        private static (Vector3 Before, Vector3 After, CharacterState Attached, Vector3 Later) HookWhileFlying(
            BoxTerrain world, Vector3 velocity, float aimX, float aimY, float turnSpeed, int ticks = 30)
        {
            var config = WireTestSetup.LongAttachConfig(WireModelType.Inertial);
            config.Wire.AttachTurnSpeed = turnSpeed;
            config.Wire.Inertial.StartPullSpeed = 0f;
            var harness = new SimHarness(world, config, new Vector3(0f, 6f, 0f));
            var state = harness.State;
            state.Grounded = false;
            state.Velocity = velocity;
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
            }, Inputs.Wire(aimX, aimY), 30);
            CharacterState attached = harness.State;
            harness.Step(Inputs.Wire(), ticks);
            return (before, attached.Velocity, attached, harness.State.Position);
        }

        [Test]
        public void HookingBehindTheMotion_TurnsTowardTheHook_KeepingTheSpeed()
        {
            // 右へ速く飛んでいる間に、左上の天井へフックを刺す。慣性に負けて右（フックと逆）へ振られず、速さを保ったまま
            // フックの側（左下へ、接続点の下をくぐる振り）へ向きを変える（ユーザーの指摘、2026-10-14）。
            var world = new BoxTerrain().AddBox(-40f, 12f, 40f, 13f, attachable: true);
            var (before, after, attached, later) = HookWhileFlying(world, new Vector3(16f, 0f, 0f), -1f, 1f, 1f);

            Vector3 radial = Vector3.Normalize(attached.Position - attached.Wire.Anchor);
            Assert.That(attached.Wire.Anchor.X, Is.LessThan(attached.Position.X), "前提: フックは左にある");
            Assert.That(after.X, Is.LessThan(0f), $"フックの側へ向かない: 前 {before}、後 {after}");
            // 接続したティックの重力の分だけ違う。
            Assert.That(after.Length(), Is.EqualTo(before.Length()).Within(1f), $"速さが保たれない: 前 {before}、後 {after}");
            Assert.That(MathF.Abs(Vector3.Dot(after, radial)), Is.LessThan(0.5f), "振りの向き（ロープに垂直）になっていない（たるむ・跳ねる）");
            Assert.That(later.X, Is.LessThan(attached.Position.X), "その後もフックの側へ振れていない");
        }

        [Test]
        public void HookingBehindTheMotion_WithTheTurnOff_IsCarriedAwayByTheMomentum()
        {
            // 向きを変えない設定（AttachTurnSpeed 0）では、遠ざかる向きの速さをロープが止め、残りの勢いでフックと逆の側へ振られる（以前の動き）。
            var world = new BoxTerrain().AddBox(-40f, 12f, 40f, 13f, attachable: true);
            var (before, after, _, _) = HookWhileFlying(world, new Vector3(16f, 0f, 0f), -1f, 1f, 0f);

            Assert.That(after.X, Is.GreaterThanOrEqualTo(0f), $"前提が違う: 前 {before}、後 {after}");
            Assert.That(after.Length(), Is.LessThan(before.Length() * 0.8f));
        }

        [Test]
        public void HookingNearlyStraightUp_WhileRunning_StillSwingsForward()
        {
            // 右へ動きながら（落ちながら）真上に近いところへ刺したとき（逆の側ではない）は、今までどおり前（右）へ振れる。
            var world = new BoxTerrain().AddBox(-40f, 12f, 40f, 13f, attachable: true);
            var (before, after, _, _) = HookWhileFlying(world, new Vector3(12f, 0f, 0f), -0.15f, 1f, 1f);

            Assert.That(after.X, Is.GreaterThan(0f), $"前へ振れない: 前 {before}、後 {after}");
        }

        [Test]
        public void HookingSideways_AgainstTheMotion_SwingsDownUnderTheAnchor()
        {
            // 右へ飛んでいる間に、真横（左）の壁へ刺す（ロープがほぼ水平）。上ではなく下へ向きを変え、接続点の下をくぐって振れる。
            var world = new BoxTerrain().AddBox(-12f, -30f, -11f, 40f, attachable: true);
            var (before, after, _, _) = HookWhileFlying(world, new Vector3(14f, 0f, 0f), -1f, 0f, 1f);

            Assert.That(after.Y, Is.LessThan(-before.Length() * 0.8f), $"下へ向かない: 前 {before}、後 {after}");
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
            state.PreviousButtons = Airerage.Core.Input.InputButtons.Wire;
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
            state.PreviousButtons = Airerage.Core.Input.InputButtons.Wire;
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
