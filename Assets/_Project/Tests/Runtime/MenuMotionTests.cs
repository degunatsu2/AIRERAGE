using Airerage.Runtime.UI;
using NUnit.Framework;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// メニューの演出（D-87・D-88。2026-11-07）の動きの曲線と、タイトルからの切り替えのカメラの突進の量。
    /// </summary>
    public class MenuMotionTests
    {
        [Test]
        public void Ease_StartsAtZero_EndsAtOne_AndOutBackOvershoots()
        {
            System.Func<float, float>[] curves = { UiEase.OutCubic, UiEase.InCubic, UiEase.InOutCubic, t => UiEase.OutBack(t), SlantedPlate.MoveEase };
            foreach (System.Func<float, float> curve in curves)
            {
                Assert.That(curve(0f), Is.EqualTo(0f).Within(0.0001f));
                Assert.That(curve(1f), Is.EqualTo(1f).Within(0.0001f));
                Assert.That(curve(-1f), Is.EqualTo(curve(0f)), "範囲の外を切らない");
                Assert.That(curve(2f), Is.EqualTo(curve(1f)), "範囲の外を切らない");
            }
            Assert.That(UiEase.OutCubic(0.5f), Is.GreaterThan(0.5f), "だんだん遅く止まらない");
            Assert.That(UiEase.InCubic(0.5f), Is.LessThan(0.5f), "だんだん速くならない");
            float peak = 0f;
            for (int i = 0; i <= 100; i++)
            {
                peak = System.Math.Max(peak, SlantedPlate.MoveEase(i / 100f));
            }
            Assert.That(peak, Is.GreaterThan(1.05f), "選んだ札が行きすぎてから戻らない（B2）");
        }

        [Test]
        public void CameraDive_PushesForward_ThenSettlesFromBehind()
        {
            Assert.That(MenuCameraDrift.DivePush(-0.1f), Is.EqualTo(0f));
            Assert.That(MenuCameraDrift.DivePush(0.39f), Is.GreaterThan(20f), "前へ突進しない");
            Assert.That(MenuCameraDrift.DivePush(0.45f), Is.LessThan(0f), "白い光の間に後ろへ移らない");
            Assert.That(MenuCameraDrift.DivePush(MenuCameraDrift.DiveSeconds), Is.EqualTo(0f), "元の距離へ戻らない");
            Assert.That(System.Math.Abs(MenuCameraDrift.DivePush(MenuCameraDrift.DiveSeconds - 0.01f)), Is.LessThan(0.5f), "戻るときに跳ぶ");
        }
    }
}
