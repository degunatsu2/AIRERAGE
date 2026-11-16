using System.Collections.Generic;
using Airerage.Runtime.UI;
using NUnit.Framework;
using UnityEngine;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// タイトルロゴ（D-96。ユーザーの決定、2026-11-16）: 案の I2 アンカー × F2 ラムダ × シアンを手直しした形と、出すときの演出。
    /// - アンカーは文字の右。ワイヤーは A の横棒の位置を通り、2 つの E の縦線（傾いて文字の下まで）になって、アンカーの中心へつながる。
    /// - E の横線はワイヤーの斜めの線から始まり、右の端はそろう（イタリック体の E のような見た目）。
    /// - ワイヤーは角（キーフレーム）ごとにイーズアウトする（直線では速く、角で止まる）。角とつながる瞬間に控えめな形の動き。
    /// </summary>
    public sealed class TitleLogoTests
    {
        [Test]
        public void Anchor_IsRightOfTheWord_WithAGap()
        {
            float frameLeft = TitleLogoShape.AnchorCenter.x - TitleLogoShape.AnchorHalf - TitleLogoShape.FrameWidth * 0.71f;
            Assert.That(frameLeft - TitleLogoShape.WordRight, Is.InRange(15f, 60f), "アンカーが文字の右に程よく離れていない");
            Assert.That(TitleLogoShape.AnchorCenter.y, Is.EqualTo(TitleLogoShape.Cap * 0.5f).Within(0.01f), "アンカーが文字の高さの真ん中にない");
        }

        [Test]
        public void Wire_FillsTheCrossbarOfTheFirstA()
        {
            float center = TitleLogoShape.Left(0) + TitleLogoShape.Width(0) * 0.5f;
            float y = WireYAt(0, center);
            Assert.That(y / TitleLogoShape.Cap, Is.InRange(0.45f, 0.75f), "ワイヤーが最初の A の横棒の位置を通らない");
            Assert.That(TitleLogoShape.WirePoint(0).x, Is.LessThan(TitleLogoShape.Left(0)), "ワイヤーが A の左の外から始まらない");
        }

        [Test]
        public void Wire_IsTheSlantedSpineOfBothEs_AndTheBarsStartOnIt()
        {
            int[] corners = { 1, 3 };
            for (int n = 0; n < TitleLogoShape.EIndices.Length; n++)
            {
                int e = TitleLogoShape.EIndices[n];
                Vector2 top = TitleLogoShape.WirePoint(corners[n]);
                Vector2 bottom = TitleLogoShape.WirePoint(corners[n] + 1);
                Assert.That((top.x - bottom.x) / (bottom.y - top.y), Is.EqualTo(TitleLogoShape.Lean).Within(0.0001f), $"{e} 番目の E の縦線の傾き");
                Assert.That(bottom.y, Is.GreaterThan(TitleLogoShape.Cap), "E の縦線が文字の下まで伸びない");
                var bars = new List<Vector2[]>(TitleLogoShape.LetterQuads(e));
                Assert.That(bars.Count, Is.EqualTo(3), "E の横線が 3 本でない");
                foreach (Vector2[] bar in bars)
                {
                    Assert.That(bar[0].x, Is.EqualTo(TitleLogoShape.SpineX(e, bar[0].y)).Within(0.001f), "E の横線がワイヤーの線から始まらない");
                    Assert.That(bar[3].x, Is.EqualTo(TitleLogoShape.SpineX(e, bar[3].y)).Within(0.001f));
                }
                Assert.That(bars[0][1].x, Is.EqualTo(bars[2][1].x).Within(0.001f), "E の上と下の横線の右の端がそろわない");
                Assert.That(bars[0][1].x - bars[0][0].x, Is.LessThan(bars[2][1].x - bars[2][0].x - 15f), "E がイタリック体の形でない（上の横線が短くない）");
                Assert.That(SpineX(e, TitleLogoShape.StrokeWidth * 0.5f), Is.EqualTo(top.x).Within(0.001f), "ワイヤーの角が E の左上にない");
            }
        }

        [Test]
        public void RisingParts_AreParallel()
        {
            Vector2 a = TitleLogoShape.WirePoint(1) - TitleLogoShape.WirePoint(0);
            Vector2 b = TitleLogoShape.WirePoint(3) - TitleLogoShape.WirePoint(2);
            Assert.That(a.y / a.x, Is.EqualTo(b.y / b.x).Within(0.0001f), "A を通る線と E から E へ渡る線が平行でない");
            Assert.That(TitleLogoShape.WirePoint(TitleLogoShape.WirePointCount - 1), Is.EqualTo(TitleLogoShape.AnchorCenter), "ワイヤーがアンカーの中心で終わらない");
        }

        [Test]
        public void Wire_IsAsThickAsTheLetters_AndItsCornersStayUnderTheCapLine()
        {
            Assert.That(TitleLogoShape.WireWidth, Is.EqualTo(TitleLogoShape.StrokeWidth), "ワイヤーの太さが文字の線と違う");
            // 角は面取り: E の左上の角（区間の縁の端）も字の上の線より上へ出ない。
            float half = TitleLogoShape.WireWidth * 0.5f;
            foreach (int k in new[] { 1, 3 })
            {
                Vector2 p = TitleLogoShape.WirePoint(k);
                Vector2 d0 = (p - TitleLogoShape.WirePoint(k - 1)).normalized;
                Vector2 d1 = (TitleLogoShape.WirePoint(k + 1) - p).normalized;
                foreach (Vector2 d in new[] { d0, d1 })
                {
                    Vector2 n = new Vector2(-d.y, d.x) * half;
                    Assert.That(Mathf.Min((p + n).y, (p - n).y), Is.GreaterThan(-0.5f), $"ワイヤーの角 {k} が字の上の線より上へ出る");
                }
            }
        }

        [Test]
        public void Wire_RunsFlatAfterTheLastE_ThenEntersTheAnchorEdgeAtRightAngles()
        {
            int last = TitleLogoShape.WirePointCount - 1;
            Vector2 bend = TitleLogoShape.WirePoint(last - 1);
            Vector2 below = TitleLogoShape.WirePoint(last - 2);
            Vector2 center = TitleLogoShape.AnchorCenter;
            Assert.That(bend.y, Is.EqualTo(below.y).Within(0.001f), "最後の E の下で曲がった後に水平に伸びない");
            Assert.That(bend.x, Is.GreaterThan(below.x + 20f), "水平に伸びる区間がない");
            // アンカーの左下の辺（左の角から下の角へ）に垂直に入り、辺の真ん中を通る。
            Vector2[] diamond = TitleLogoShape.Diamond(center, TitleLogoShape.AnchorHalf);
            Vector2 edge = diamond[2] - diamond[3];
            Vector2 approach = center - bend;
            Assert.That(Vector2.Dot(edge.normalized, approach.normalized), Is.EqualTo(0f).Within(0.0001f), "アンカーの辺に垂直に入らない");
            Vector2 middle = (diamond[2] + diamond[3]) * 0.5f;
            float cross = (middle.x - bend.x) * approach.y - (middle.y - bend.y) * approach.x;
            Assert.That(cross, Is.EqualTo(0f).Within(0.01f), "アンカーの辺の真ん中を通らない");
            Assert.That(bend.y, Is.GreaterThan(TitleLogoShape.Cap), "水平の区間が文字の下を通らない");
        }

        [Test]
        public void Bounds_ContainEveryPart()
        {
            Rect bounds = TitleLogoShape.Bounds;
            for (int k = 0; k < TitleLogoShape.WirePointCount; k++)
            {
                Assert.That(bounds.Contains(TitleLogoShape.WirePoint(k)), Is.True, $"ワイヤーの点 {k} が範囲の外");
            }
            for (int i = 0; i < TitleLogoShape.LetterCount; i++)
            {
                var parts = new List<Vector2[]>(TitleLogoShape.LetterLines(i));
                parts.AddRange(TitleLogoShape.LetterQuads(i));
                Assert.That(parts.Count, Is.GreaterThan(0), $"文字 {i} の形がない");
                foreach (Vector2[] part in parts)
                {
                    foreach (Vector2 p in part)
                    {
                        Assert.That(bounds.Contains(p), Is.True, $"文字 {i} が範囲の外");
                        // 字は字の高さの中に収める（A の頂も字の上の線で平らに切る）。
                        Assert.That(p.y, Is.InRange(0f, TitleLogoShape.Cap), $"文字 {i} が字の上下の線からはみ出す");
                    }
                }
            }
            foreach (Vector2 p in TitleLogoShape.Diamond(TitleLogoShape.AnchorCenter, TitleLogoShape.AnchorHalf))
            {
                Assert.That(bounds.Contains(p), Is.True, "アンカーの枠が範囲の外");
            }
        }

        [Test]
        public void Wire_EasesOutPerKeyframe_FastOnLines_AndStopsAtCorners()
        {
            int segments = TitleLogoShape.WirePointCount - 1;
            Assert.That(TitleLogoTimeline.WireLength(TitleLogoTimeline.WireStart - 0.01f), Is.EqualTo(0f), "始まる前にワイヤーが出ている");
            for (int k = 0; k < segments; k++)
            {
                float start = TitleLogoTimeline.SegmentStart(k);
                float seconds = TitleLogoTimeline.SegmentSeconds(k);
                float from = TitleLogoShape.WireLengthTo(k);
                float to = TitleLogoShape.WireLengthTo(k + 1);
                // 最初の 3 割の時間で 7 割以上進み（直線では速く）、角（キーフレーム）にぴたりと止まる。
                float early = (TitleLogoTimeline.WireLength(start + seconds * 0.3f) - from) / (to - from);
                Assert.That(early, Is.GreaterThan(0.7f), $"区間 {k} の始めが速くない");
                Assert.That(TitleLogoTimeline.WireLength(start + seconds), Is.EqualTo(to).Within(0.01f), $"区間 {k} が角で止まらない");
                if (k < segments - 1)
                {
                    float hold = start + seconds + TitleLogoTimeline.CornerHold * 0.5f;
                    Assert.That(TitleLogoTimeline.WireLength(hold), Is.EqualTo(to).Within(0.01f), $"角 {k + 1} で止まっていない");
                }
            }
            float previous = 0f;
            for (float t = 0f; t < TitleLogoTimeline.Duration; t += 0.01f)
            {
                float length = TitleLogoTimeline.WireLength(t);
                Assert.That(length, Is.GreaterThanOrEqualTo(previous - 0.0001f), "ワイヤーが戻った");
                previous = length;
            }
            Assert.That(TitleLogoTimeline.Duration, Is.InRange(1.2f, 2.4f), "演出が短すぎる・長すぎる");
        }

        [Test]
        public void Shapes_PopAtCorners_AndWhenTheHookConnects()
        {
            for (int k = 0; k < TitleLogoTimeline.CornerCount; k++)
            {
                float arrive = TitleLogoTimeline.ArriveAt(k + 1);
                Assert.That(TitleLogoTimeline.CornerPulse(arrive - 0.01f, k, out _, out _), Is.False, $"角 {k} に着く前に輪が出た");
                Assert.That(TitleLogoTimeline.CornerPulse(arrive + 0.05f, k, out float half, out float alpha), Is.True, $"角 {k} で輪が出ない");
                Assert.That(half, Is.LessThan(TitleLogoShape.StrokeWidth * 2f), "角の輪が控えめでない");
                Assert.That(alpha, Is.LessThan(1f));
            }
            float connect = TitleLogoTimeline.ConnectAt;
            Assert.That(TitleLogoTimeline.InnerScale(connect - 0.01f), Is.EqualTo(0f), "つながる前に中の菱形が出た");
            Assert.That(TitleLogoTimeline.InnerScale(connect + TitleLogoTimeline.PopSeconds * 0.5f), Is.GreaterThan(1f), "中の菱形がはねない");
            Assert.That(TitleLogoTimeline.InnerScale(connect + 1f), Is.EqualTo(1f).Within(0.001f));
            Assert.That(TitleLogoTimeline.ConnectRing(connect + 0.1f, out float ring, out _), Is.True, "つながったときに輪が広がらない");
            Assert.That(ring, Is.InRange(1f, 1.5f));
            TitleLogoTimeline.Frame(connect + 1f, out float frameAlpha, out float frameScale);
            Assert.That(frameAlpha, Is.EqualTo(1f).Within(0.001f));
            Assert.That(frameScale, Is.EqualTo(1f).Within(0.001f), "演出の後に枠の大きさが戻らない");
        }

        [Test]
        public void Letters_FadeIn_FromTheLeft_BeforeTheHook()
        {
            TitleLogoTimeline.Letter(0f, 0, out float alpha0, out float shift0);
            Assert.That(alpha0, Is.EqualTo(0f).Within(0.001f));
            Assert.That(shift0, Is.LessThan(0f), "文字が左からすべり込まない");
            TitleLogoTimeline.Letter(TitleLogoTimeline.WireStart, 0, out float alphaAtWire, out _);
            Assert.That(alphaAtWire, Is.GreaterThan(0.8f), "ワイヤーが伸び始めるときに文字が出ていない");
            int last = TitleLogoShape.LetterCount - 1;
            TitleLogoTimeline.Letter(TitleLogoTimeline.Duration, last, out float alphaEnd, out float shiftEnd);
            Assert.That(alphaEnd, Is.EqualTo(1f).Within(0.001f));
            Assert.That(shiftEnd, Is.EqualTo(0f).Within(0.001f));
        }

        private static float SpineX(int e, float y) => TitleLogoShape.SpineX(e, y);

        // 区間 k（k 番目の点から k+1 番目の点まで）の、x での y。
        private static float WireYAt(int k, float x)
        {
            Vector2 a = TitleLogoShape.WirePoint(k);
            Vector2 b = TitleLogoShape.WirePoint(k + 1);
            return Mathf.Lerp(a.y, b.y, (x - a.x) / (b.x - a.x));
        }
    }
}
