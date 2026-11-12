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
    
        // ---- 照準の届く先（マウスカーソル・右スティックの倒し具合。2026-11-12 のユーザーの要望） ----

        [Test]
        public void PredictShot_WithReach_EndsAtTheCursor_WhenNothingIsInTheWay()
        {
            // 天井（8m）より手前の 4m を狙う: 照準器はカーソルの所（外れる）。
            var tuning = new WireTuning();
            float reach = 4f / tuning.MaxLength;

            WireShotPreview preview = WireTargeting.PredictShot(Origin, Up, 1, tuning, GroundWithCeiling(attachable: true), reach);

            Assert.That(preview.Result, Is.EqualTo(WireShotResult.OutOfRange), "カーソルの先の天井へ届いてしまう");
            Assert.That(preview.EndPoint.Y, Is.EqualTo(StandingY + 4f).Within(1e-3f), "照準器がカーソルの所にない");
        }

        [Test]
        public void PredictShot_WithReach_AttachesToWhatIsInTheWay()
        {
            // 天井（8m）の先の 10m を狙う: 途中の天井につながる（照準器は天井。カーソルの所には別の印を出す）。
            var tuning = new WireTuning();
            float reach = 10f / tuning.MaxLength;

            WireShotPreview preview = WireTargeting.PredictShot(Origin, Up, 1, tuning, GroundWithCeiling(attachable: true), reach);

            Assert.That(preview.Result, Is.EqualTo(WireShotResult.Attach));
            Assert.That(preview.EndPoint.Y, Is.EqualTo(8f).Within(1e-3f));
        }

        [Test]
        public void PredictShot_WithReach_StillAttaches_WhenTheCursorIsJustShortOfASurface()
        {
            // 天井の 0.6m 手前を狙っても、余裕（ReachMargin 1m）の中なので天井につながる。余裕の外なら外れる。
            var tuning = new WireTuning();
            BoxTerrain terrain = GroundWithCeiling(attachable: true);
            float near = (8f - StandingY - 0.6f) / tuning.MaxLength;
            float far = (8f - StandingY - 1.6f) / tuning.MaxLength;

            Assert.That(WireTargeting.PredictShot(Origin, Up, 1, tuning, terrain, near).Result, Is.EqualTo(WireShotResult.Attach), "面の少し手前でつながらない");
            Assert.That(WireTargeting.PredictShot(Origin, Up, 1, tuning, terrain, far).Result, Is.EqualTo(WireShotResult.OutOfRange), "余裕より手前でもつながる");
        }

        [Test]
        public void AimAssist_WithReach_SkipsAnchorsFarBeyondTheCursor()
        {
            // 真上 10m のアンカー: カーソルが 4m なら選ばない。9.5m（判定の半径の中）なら選ぶ。
            var tuning = new WireTuning();
            BoxTerrain terrain = TestArenas.Ground().AddAnchor(0f, StandingY + 10f);
            var up = new Vector3(0f, 1f, 0f);

            Assert.That(WireTargeting.TryFindAssistTarget(Origin, up, tuning, terrain, out _, 4f / tuning.MaxLength), Is.False, "カーソルの先の遠いアンカーを選んだ");
            Assert.That(WireTargeting.TryFindAssistTarget(Origin, up, tuning, terrain, out _, 9.5f / tuning.MaxLength), Is.True, "カーソルの近くのアンカーを選ばない");
            Assert.That(WireTargeting.TryFindAssistTarget(Origin, up, tuning, terrain, out _), Is.True, "届く先の指定がなければ今までどおり");
        }

        [Test]
        public void Fire_WithReach_MissesAtTheCursor_AndAttachesWhenTheCursorIsOnTheCeiling()
        {
            var harness = new SimHarness(GroundWithCeiling(attachable: true));
            float maxLength = harness.Config.Wire.MaxLength;
            PlayerInput shortShot = PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(Up, 4f / maxLength);

            harness.Step(shortShot);
            Assert.That(harness.State.Wire.HookReach, Is.EqualTo(4f + harness.Config.Wire.ReachMargin).Within(0.1f), "フックが飛べる距離がカーソルまでになっていない");
            harness.StepUntil(s => s.Wire.Phase != WirePhase.Firing, shortShot, 60);
            Assert.That(harness.State.Wire.Phase, Is.Not.EqualTo(WirePhase.Attached), "カーソルの先の天井につながった");
            Assert.That(harness.State.Wire.LastMissReason, Is.EqualTo(WireMissReason.OutOfRange));

            var second = new SimHarness(GroundWithCeiling(attachable: true));
            PlayerInput onCeiling = PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(Up, 8f / maxLength);
            second.StepUntil(s => s.Wire.Phase == WirePhase.Attached, onCeiling, 60);
            Assert.That(second.State.Wire.Phase, Is.EqualTo(WirePhase.Attached), "カーソルを天井に置いてもつながらない");
            Assert.That(second.State.Wire.Anchor.Y, Is.EqualTo(8f).Within(1e-3f));
        }

        [Test]
        public void Reach_IsQuantized_AndKeptOnlyOnTheWirePressTick()
        {
            PlayerInput aimed = PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(Up, 0.5f);
            Assert.That(aimed.Reach, Is.EqualTo(0.5f).Within(1f / PlayerInput.ReachMax), "届く先が量子化されない");
            Assert.That(PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(Up, 0f).AimReach, Is.EqualTo(1), "0 は指定なしと区別できない");
            Assert.That(PlayerInput.FromAxes(0f, 0f, InputButtons.None).WithAim(Up).Reach, Is.EqualTo(1f), "届く先の指定がなければ射程いっぱい");

            PlayerInput pressed = Airerage.Core.Rollback.InputNormalizer.Normalize(aimed, default, 0);
            PlayerInput held = Airerage.Core.Rollback.InputNormalizer.Normalize(aimed, pressed, 0);
            Assert.That(pressed.AimReach, Is.EqualTo(aimed.AimReach), "撃つティックの届く先が消えた");
            Assert.That(held.AimReach, Is.EqualTo(0), "押し続けている間も届く先が残っている（予測が外れやすくなる）");
        }
}
}
