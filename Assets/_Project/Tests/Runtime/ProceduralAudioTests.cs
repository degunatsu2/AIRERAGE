using System;
using Degunatsu.Runtime.Audio;
using NUnit.Framework;

namespace Degunatsu.Runtime.Tests
{
    /// <summary>仮の効果音と BGM の合成（ユーザーの要望、2026-10-13）。音の良し悪しは耳で確かめる。ここでは壊れていないことだけを見る。</summary>
    public class ProceduralAudioTests
    {
        [Test]
        public void EverySound_IsAudible_AndDoesNotClip([Values] SoundId id)
        {
            float[] samples = ProceduralAudio.Synthesize(id);

            Assert.That(samples.Length, Is.GreaterThan(ProceduralAudio.SeSampleRate / 20), "短すぎる");
            float peak = Peak(samples, id.ToString());
            Assert.That(peak, Is.GreaterThan(0.3f), "小さすぎる");
            Assert.That(peak, Is.LessThanOrEqualTo(1f), "音割れする");
        }

        [Test]
        public void Bgm_IsEightBarsAt140Bpm_AndDoesNotClip()
        {
            float[] samples = ProceduralAudio.SynthesizeBgm();

            float seconds = (float)samples.Length / ProceduralAudio.BgmSampleRate;
            float expected = ProceduralAudio.BgmBars * 4 * 60f / ProceduralAudio.BgmTempo;
            Assert.That(seconds, Is.EqualTo(expected).Within(0.01f));
            float peak = Peak(samples, "BGM");
            Assert.That(peak, Is.GreaterThan(0.3f));
            Assert.That(peak, Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void Synthesis_IsTheSameEveryTime()
        {
            Assert.That(ProceduralAudio.Synthesize(SoundId.Hit), Is.EqualTo(ProceduralAudio.Synthesize(SoundId.Hit)));
        }

        private static float Peak(float[] samples, string name)
        {
            float peak = 0f;
            foreach (float s in samples)
            {
                Assert.That(float.IsNaN(s) || float.IsInfinity(s), Is.False, $"{name} に数でない値がある");
                peak = Math.Max(peak, Math.Abs(s));
            }
            return peak;
        }
    }
}
