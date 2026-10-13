using Degunatsu.Core.Common;
using NUnit.Framework;
using UnityEngine;

namespace Degunatsu.Net.Tests
{
    public sealed class CorrectionSmootherTests
    {
        private static CorrectionSmoothingSettings Settings(bool enabled = true)
        {
            return new CorrectionSmoothingSettings { Enabled = enabled, TimeConstantSeconds = 0.1f, SnapDistance = 2f };
        }

        [Test]
        public void Correction_KeepsTheDisplayWhereItWas()
        {
            var smoother = new CorrectionSmoother(2, Settings());

            smoother.AddCorrection(1, new Vector3(0.5f, -0.2f, 0f));

            Assert.That(smoother.Offset(1), Is.EqualTo(new Vector3(-0.5f, 0.2f, 0f)));
            Assert.That(smoother.Offset(0), Is.EqualTo(Vector3.zero), "ほかのプレイヤーには影響しない");
        }

        [Test]
        public void Offset_ShrinksWithTheTimeConstant_AndDisappears()
        {
            var smoother = new CorrectionSmoother(1, Settings());
            smoother.AddCorrection(0, new Vector3(1f, 0f, 0f));

            // 時定数（0.1 秒）たつと約 37%（1/e）になる。
            for (int i = 0; i < 6; i++)
            {
                smoother.Update(1f / 60f);
            }
            Assert.That(smoother.Offset(0).x, Is.EqualTo(-Mathf.Exp(-1f)).Within(0.01f));

            for (int i = 0; i < 60; i++)
            {
                smoother.Update(1f / 60f);
            }
            Assert.That(smoother.Offset(0), Is.EqualTo(Vector3.zero), "1 秒後には消えている");
        }

        [Test]
        public void Corrections_Accumulate()
        {
            var smoother = new CorrectionSmoother(1, Settings());

            smoother.AddCorrection(0, new Vector3(0.5f, 0f, 0f));
            smoother.AddCorrection(0, new Vector3(0.25f, 0f, 0f));

            Assert.That(smoother.Offset(0).x, Is.EqualTo(-0.75f).Within(1e-6f));
        }

        [Test]
        public void LargeCorrection_SnapsWithoutSmoothing()
        {
            var smoother = new CorrectionSmoother(1, Settings());

            smoother.AddCorrection(0, new Vector3(3f, 0f, 0f));

            Assert.That(smoother.Offset(0), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Disabled_DoesNotOffset_AndClearsWhenTurnedOff()
        {
            CorrectionSmoothingSettings settings = Settings();
            var smoother = new CorrectionSmoother(1, settings);
            smoother.AddCorrection(0, new Vector3(1f, 0f, 0f));

            settings.Enabled = false;
            smoother.Update(1f / 60f);
            Assert.That(smoother.Offset(0), Is.EqualTo(Vector3.zero), "切ったら、すぐ正しい位置に表示する");

            smoother.AddCorrection(0, new Vector3(1f, 0f, 0f));
            Assert.That(smoother.Offset(0), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Settings_RejectNonPositiveValues()
        {
            var settings = new CorrectionSmoothingSettings { TimeConstantSeconds = 0f };
            Assert.Throws<InvalidTuningException>(() => new CorrectionSmoother(1, settings));
        }
    }
}
