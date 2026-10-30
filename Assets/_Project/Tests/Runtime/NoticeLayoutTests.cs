using System.Collections.Generic;
using Airerage.Runtime.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// タイマーの右の告知の並べ方（NoticeHud.Place。ユーザーの要望、2026-10-30）: 新しい札がいちばん上、札どうしは重ならない、左の縁はタイマーの
    /// 斜めの縁に沿う、右上の表にかからない、タイマーより下の札は流れのバーにかからない。画面が狭ければ小さくし、それでも入らなければタイマーの下。
    /// </summary>
    public sealed class NoticeLayoutTests
    {
        private const float FallbackTop = 106f;

        [TestCase(1920f)]
        [TestCase(1600f)]
        [TestCase(1440f)]
        public void Chips_StackBesideTheTimer_WithoutOverlapping(float screenWidth)
        {
            var widths = new List<float> { 330f, 260f, 300f, 260f };
            var presences = new List<float> { 1f, 1f, 1f, 1f };
            var rects = new List<Rect>();
            float scale = NoticeHud.Place(widths, presences, screenWidth, FallbackTop, rects, out bool beside);

            Assert.That(beside, Is.True, "タイマーの右に入らない");
            Assert.That(scale, Is.GreaterThanOrEqualTo(NoticeHud.MinChipScale));
            float centerX = screenWidth * 0.5f;
            Assert.That(rects[0].y, Is.EqualTo(NoticeHud.ChipTop), "新しい札がいちばん上にない");
            for (int i = 0; i < rects.Count; i++)
            {
                Rect rect = rects[i];
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(screenWidth - NoticeHud.ScoreboardReserve + 0.01f), $"{i} 番目の札が右の表にかかる");
                if (i > 0)
                {
                    Assert.That(rect.y, Is.GreaterThanOrEqualTo(rects[i - 1].yMax), $"{i} 番目の札が上の札と重なる");
                }
                if (rect.yMax <= TimerPlate.Height + 2f)
                {
                    // 札の左上の角（斜めの縁の上の端）が、タイマーの右の縁から少し右。
                    float topLeft = rect.x + NoticeHud.ChipSlant * scale;
                    Assert.That(topLeft - TimerPlate.RightEdgeAt(centerX, rect.y), Is.EqualTo(NoticeHud.ChipGap).Within(0.01f), $"{i} 番目の札がタイマーの縁に沿わない");
                }
                else
                {
                    Assert.That(rect.x, Is.GreaterThan(centerX + TimerPlate.TimelineWidth * 0.5f), $"{i} 番目の札が流れのバーにかかる");
                }
            }
        }

        [Test]
        public void ChipsMovingInOrOut_TakeOnlyTheirShareOfTheSpace()
        {
            // 出始め（場所を空けている途中）の札の下の札は、空けた分だけ下がる。
            var widths = new List<float> { 260f, 260f };
            var rects = new List<Rect>();
            NoticeHud.Place(widths, new List<float> { 0f, 1f }, 1920f, FallbackTop, rects, out _);
            Assert.That(rects[1].y, Is.EqualTo(rects[0].y), "場所を空ける前から下がった");
            NoticeHud.Place(widths, new List<float> { 0.5f, 1f }, 1920f, FallbackTop, rects, out _);
            Assert.That(rects[1].y - rects[0].y, Is.EqualTo((NoticeHud.ChipHeight + NoticeHud.ChipGap) * 0.5f).Within(0.01f));
        }

        [Test]
        public void Chips_SlideOutFromBehindTheTimer_AndKeepTheirWholeFrame()
        {
            // 札は文字ごと、タイマーの後ろから右へせり出す（ずれは出ている割合で決まり、出始めは札の幅だけ左に隠れている。ユーザーの要望、2026-10-30）。
            Assert.That(NoticeHud.SlideOffset(300f, 0f), Is.EqualTo(-300f));
            Assert.That(NoticeHud.SlideOffset(300f, 0.5f), Is.EqualTo(-150f));
            Assert.That(NoticeHud.SlideOffset(300f, 1f), Is.EqualTo(0f));
            // 札の幅は、形の絵と同じ 10 ピクセルごとの幅（絵より狭く切ると、右上の斜めの角が切れていた。ユーザーの指摘、2026-10-30）。
            Assert.That(NoticeHud.ShapeWidth(263.2f), Is.EqualTo(270f));
            Assert.That(NoticeHud.ShapeWidth(260f), Is.EqualTo(260f));
            Assert.That(NoticeHud.ShapeWidth(NoticeHud.CountdownChipWidth), Is.EqualTo(NoticeHud.CountdownChipWidth));
        }

        [Test]
        public void NarrowScreen_PutsTheChipsUnderTheTimer()
        {
            var rects = new List<Rect>();
            NoticeHud.Place(new List<float> { 300f }, new List<float> { 1f }, 1280f, FallbackTop, rects, out bool beside);
            Assert.That(beside, Is.False, "狭い画面で右の表に重なる");
            Assert.That(rects[0].y, Is.EqualTo(FallbackTop));
            Assert.That(rects[0].center.x, Is.EqualTo(640f).Within(0.01f), "タイマーの下の中央にない");
        }
    }
}
