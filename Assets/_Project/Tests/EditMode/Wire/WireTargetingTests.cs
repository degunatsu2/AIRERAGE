using System.Numerics;
using Airerage.Core.Input;
using Airerage.Core.Physics;
using Airerage.Core.Tests.Support;
using Airerage.Core.Wire;
using NUnit.Framework;

namespace Airerage.Core.Tests.Wire
{
    /// <summary>
    /// 照準の方向（マウスでの照準を含む）と、射出結果の予測（照準のインジケーター用）。
    /// </summary>
    public class WireTargetingTests
    {
        private const float StandingY = 0.91f;
        private static readonly Vector3 Origin = new Vector3(0f, StandingY, 0f);
        private static readonly Vector2 Up = new Vector2(0f, 1f);

        private static BoxTerrain GroundWithCeiling(bool attachable)
        {
            return TestArenas.Ground().AddBox(-20f, 8f, 20f, 9f, attachable);
        }

        [Test]
        public void Fire_WithAimInput_UsesAimInsteadOfMoveStick()
        {
            var harness = new SimHarness(GroundWithCeiling(attachable: true));
            // 移動は右、照準は真上（マウスでの照準）
            PlayerInput input = PlayerInput.FromAxes(1f, 0f, InputButtons.Wire).WithAim(Up);

            harness.Step(input);

            Assert.That(harness.State.Wire.HookDirection.X, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(harness.State.Wire.HookDirection.Y, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void PredictShot_AttachableSurface_PredictsAttachPoint()
        {
            var tuning = new WireTuning();

            WireShotPreview preview = WireTargeting.PredictShot(Origin, Up, 1, tuning, GroundWithCeiling(attachable: true));

            Assert.That(preview.Result, Is.EqualTo(WireShotResult.Attach));
            Assert.That(preview.EndPoint.X, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(preview.EndPoint.Y, Is.EqualTo(8f).Within(1e-3f));
        }

        [Test]
        public void PredictShot_NonAttachableSurface_PredictsMissAtHitPoint()
        {
            var tuning = new WireTuning();

            WireShotPreview preview = WireTargeting.PredictShot(Origin, Up, 1, tuning, GroundWithCeiling(attachable: false));

            Assert.That(preview.Result, Is.EqualTo(WireShotResult.HitNonAttachable));
            Assert.That(preview.EndPoint.Y, Is.EqualTo(8f).Within(1e-3f));
        }

        [Test]
        public void PredictShot_NothingInRange_EndsAtMaxLength()
        {
            var tuning = new WireTuning();

            WireShotPreview preview = WireTargeting.PredictShot(Origin, Up, 1, tuning, TestArenas.Ground());

            Assert.That(preview.Result, Is.EqualTo(WireShotResult.OutOfRange));
            Assert.That(preview.EndPoint.Y, Is.EqualTo(StandingY + tuning.MaxLength).Within(1e-3f));
        }

        [Test]
        public void PredictShot_AimAssist_PredictsAnchor()
        {
            var tuning = new WireTuning();
            BoxTerrain terrain = TestArenas.Ground().AddAnchor(0.5f, 6f); // 真上から約 5.6° ずれた位置

            WireShotPreview preview = WireTargeting.PredictShot(Origin, Up, 1, tuning, terrain);

            Assert.That(preview.Result, Is.EqualTo(WireShotResult.Attach));
            Assert.That(preview.Aim.HasTargetPoint, Is.True);
            Assert.That(Vector3.Distance(preview.EndPoint, new Vector3(0.5f, 6f, 0f)), Is.LessThan(1e-4f));
        }

        [Test]
        public void PredictShot_MatchesActualAttachPoint()
        {
            BoxTerrain terrain = TestArenas.Playground();
            var start = new Vector3(-6f, StandingY, 0f);
            var aim = new Vector2(0.3f, 1f);
            PlayerInput input = PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(aim);
            var harness = new SimHarness(terrain, spawn: start);
            // 出現した直後に接地させてから、同じ位置で予測と射出を比べる
            harness.Step(Inputs.None);
            Vector3 origin = harness.State.Position;
            var frame = new InputFrame(input, InputButtons.None);

            WireShotPreview preview = WireTargeting.PredictShot(origin, frame.Aim, harness.State.Facing, harness.Config.Wire, terrain);
            harness.StepUntil(s => s.Wire.Phase == WirePhase.Attached, input, 30);

            Assert.That(preview.Result, Is.EqualTo(WireShotResult.Attach));
            Assert.That(Vector3.Distance(preview.EndPoint, harness.State.Wire.Anchor), Is.LessThan(1e-3f));
        }

        [Test]
        public void ABigAnchor_IsFoundFromFartherOffTheAim()
        {
            // 真上 10m のアンカーを、横に 1.5m ずれた線で狙う（角度は約 8.5°。ふつうのアンカーは 6° と 0.8m の外）。
            var tuning = new WireTuning();
            var aim = Vector3.Normalize(new Vector3(1.5f, 10f, 0f));
            BoxTerrain small = TestArenas.Ground().AddAnchor(0f, StandingY + 10f);
            BoxTerrain big = TestArenas.Ground().AddAnchor(0f, StandingY + 10f, 2f);

            Assert.That(WireTargeting.TryFindAssistTarget(Origin, aim, tuning, small, out _), Is.False, "前提: ふつうのアンカーは外れる");
            Assert.That(WireTargeting.TryFindAssistTarget(Origin, aim, tuning, big, out Vector3 target), Is.True, "大きいアンカーが見つからない");
            Assert.That(target.Y, Is.EqualTo(StandingY + 10f).Within(1e-4f));
            Assert.That(big.AnchorRadii[0], Is.EqualTo(2f));
        }
    }
}
