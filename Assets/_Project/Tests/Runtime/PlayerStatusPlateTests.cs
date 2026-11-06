using Airerage.Core.Simulation;
using Airerage.Runtime.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// 右上の札（2026-11-06 にユーザーが選んだ案 A + C + F + B と、追加の指定）: 数の短い書き方、順位の四角の上下の隙間、アイテムバッグの
    /// 大きさ、区切りの斜め線が名前・バッグ・数にかからないこと、縁が右下の角まで欠けないこと、順位の数え方。
    /// </summary>
    public class PlayerStatusPlateTests
    {
        [Test]
        public void FormatCount_WritesLargeNumbersShort_FromTenThousand()
        {
            Assert.That(PlayerStatusPlate.FormatCount(0, true), Is.EqualTo("0"));
            Assert.That(PlayerStatusPlate.FormatCount(9860, true), Is.EqualTo("9,860"));
            Assert.That(PlayerStatusPlate.FormatCount(12480, true), Is.EqualTo("12.4K"));
            Assert.That(PlayerStatusPlate.FormatCount(99999, true), Is.EqualTo("99.9K"), "境目で 100.0K に繰り上がった");
            Assert.That(PlayerStatusPlate.FormatCount(124500, true), Is.EqualTo("124K"));
            Assert.That(PlayerStatusPlate.FormatCount(1250000, true), Is.EqualTo("1.2M"));
            Assert.That(PlayerStatusPlate.FormatCount(12480, false), Is.EqualTo("12,480"), "結果の画面は正確な数");
        }

        // 順位の四角の上（上の縁）と下（上の段と下の段の区切りの線）の隙間が同じ（ユーザーの指定）。
        [Test]
        public void RankBox_HasEqualGapsToTheTopEdgeAndTheDivider()
        {
            foreach (bool bag in new[] { true, false })
            {
                PlayerStatusPlate.Layout layout = PlayerStatusPlate.LayoutOf(bag);
                float above = layout.Rank.yMin - PlayerStatusPlate.Border;
                float below = PlayerStatusPlate.DividerY - layout.Rank.yMax;
                Assert.That(above, Is.EqualTo(below).Within(0.01f));
                Assert.That(above, Is.GreaterThan(1f));
            }
        }

        // アイテムバッグの 1 枠は順位の四角と同じ大きさで、右の斜めの辺にかからない。
        [Test]
        public void BagSlots_AreRankSized_AndStayInsideTheSlantedEdge()
        {
            PlayerStatusPlate.Layout layout = PlayerStatusPlate.LayoutOf(true);
            Assert.That(layout.Slots.Length, Is.EqualTo(3));
            foreach (Rect slot in layout.Slots)
            {
                Assert.That(slot.width, Is.EqualTo(layout.Rank.width).Within(0.01f));
                Assert.That(slot.height, Is.EqualTo(layout.Rank.height).Within(0.01f));
                Assert.That(slot.xMax, Is.LessThan(PlayerStatusPlate.RightAt(slot.yMax) - PlayerStatusPlate.Border));
            }
            Assert.That(PlayerStatusPlate.LayoutOf(false).Slots.Length, Is.Zero, "アイテムのモードでなければバッグを出さない");
            Assert.That(PlayerStatusPlate.LayoutOf(false).Name.width, Is.GreaterThan(layout.Name.width), "バッグがなければ名前を広く");
        }

        // 区切りの斜め線（縁と同じ角度）が、上の段の名前とバッグ、下の段の数の欄にかからない。
        [Test]
        public void Separators_DoNotTouchTheNameTheBagOrTheValues()
        {
            PlayerStatusPlate.Layout layout = PlayerStatusPlate.LayoutOf(true);
            float topMid = (PlayerStatusPlate.Border + PlayerStatusPlate.DividerY) * 0.5f;
            Assert.That(layout.TopSeparators.Length, Is.EqualTo(1));
            foreach (float y in new[] { layout.Rank.yMin, layout.Rank.yMax })
            {
                float x = layout.TopSeparators[0] + PlayerStatusPlate.Slant * (topMid - y);
                Assert.That(x, Is.GreaterThan(layout.Name.xMax), $"高さ {y} で名前にかかる");
                Assert.That(x, Is.LessThan(layout.Slots[0].xMin), $"高さ {y} でバッグにかかる");
            }
            Assert.That(layout.Cells.Length, Is.EqualTo(4));
            Assert.That(layout.BottomSeparators.Length, Is.EqualTo(3));
            float rowTop = PlayerStatusPlate.DividerY + PlayerStatusPlate.Border;
            float rowBottom = PlayerStatusPlate.BaseHeight - PlayerStatusPlate.Border;
            float bottomMid = (rowTop + rowBottom) * 0.5f;
            for (int i = 0; i < layout.BottomSeparators.Length; i++)
            {
                foreach (float y in new[] { rowTop, rowBottom })
                {
                    float x = layout.BottomSeparators[i] + PlayerStatusPlate.Slant * (bottomMid - y);
                    Assert.That(x, Is.GreaterThan(layout.Cells[i].xMax), $"区切り {i} が欄 {i} にかかる");
                    Assert.That(x, Is.LessThan(layout.Cells[i + 1].xMin), $"区切り {i} が欄 {i + 1} にかかる");
                }
            }
            Assert.That(layout.Cells[0].xMin, Is.GreaterThan(PlayerStatusPlate.LeftAt(rowTop) + PlayerStatusPlate.Border - 4f));
            Assert.That(layout.Cells[3].xMax, Is.LessThan(PlayerStatusPlate.RightAt(rowBottom) - PlayerStatusPlate.Border));
            // BANK の欄がいちばん広く、ほかの 3 つは同じくらい（数の大きさは COIN・K・F が同じ）。
            Assert.That(layout.Cells[0].width, Is.GreaterThan(layout.Cells[1].width));
        }

        // 縁は斜めの辺・上下の辺・右下の角まで欠けずに出る（案の表示では右下が切れていた）。区切りの直線も縁と同じ太さ。
        [Test]
        public void Outline_IsCompleteAroundThePlate_IncludingTheBottomRightCorner()
        {
            float h = PlayerStatusPlate.BaseHeight;
            for (float y = 0.5f; y < h; y += 1f)
            {
                Assert.That(PlayerStatusPlate.FrameAlpha(PlayerStatusPlate.RightAt(y) - 0.6f, y, 1f), Is.GreaterThan(0.5f), $"右の辺が高さ {y} で欠ける");
                Assert.That(PlayerStatusPlate.FrameAlpha(PlayerStatusPlate.LeftAt(y) + 0.6f, y, 1f), Is.GreaterThan(0.5f), $"左の辺が高さ {y} で欠ける");
            }
            for (float x = PlayerStatusPlate.LeftAt(h) + 2f; x < PlayerStatusPlate.RightAt(h) - 2f; x += 1f)
            {
                Assert.That(PlayerStatusPlate.FrameAlpha(x, h - 0.5f, 1f), Is.GreaterThan(0.5f), $"下の辺が {x} で欠ける");
            }
            Assert.That(PlayerStatusPlate.FrameAlpha(PlayerStatusPlate.RightAt(h - 1f) - 1f, h - 1f, 1f), Is.GreaterThan(0.5f), "右下の角が欠ける");
            Assert.That(PlayerStatusPlate.FrameAlpha(160f, PlayerStatusPlate.DividerY + PlayerStatusPlate.Border * 0.5f, 1f), Is.GreaterThan(0.9f),
                "上の段と下の段の区切りの線がない");
            Assert.That(PlayerStatusPlate.FrameAlpha(160f, 15f, 1f), Is.EqualTo(0f), "中に縁がある");
            Assert.That(PlayerStatusPlate.FillAlpha(PlayerStatusPlate.RightAt(h) + 3f, h - 1f, 1f), Is.EqualTo(0f), "札の外を塗る");
        }

        // 順位: 確定コインが多い人の数 + 1（同じ数は同じ順位）。チーム対戦はチームの順位。
        [Test]
        public void RankOf_CountsHigherScores_AndTeamsAsOne()
        {
            var states = new CharacterState[4];
            int[] banked = { 100, 300, 300, 0 };
            for (int i = 0; i < states.Length; i++)
            {
                states[i].Coins.Banked = banked[i];
            }
            Assert.That(ScoreboardHud.RankOf(states, 1), Is.EqualTo(1));
            Assert.That(ScoreboardHud.RankOf(states, 2), Is.EqualTo(1));
            Assert.That(ScoreboardHud.RankOf(states, 0), Is.EqualTo(3));
            Assert.That(ScoreboardHud.RankOf(states, 3), Is.EqualTo(4));

            // チーム対戦: 赤（0 と 1。合計 400）と青（2 と 3。合計 300）。青の人はチームの順位で 2 位。
            byte[] teams = { 1, 1, 2, 2 };
            for (int i = 0; i < states.Length; i++)
            {
                states[i].Combat.Team = teams[i];
            }
            Assert.That(ScoreboardHud.RankOf(states, 0), Is.EqualTo(1));
            Assert.That(ScoreboardHud.RankOf(states, 1), Is.EqualTo(1));
            Assert.That(ScoreboardHud.RankOf(states, 2), Is.EqualTo(2));
            Assert.That(ScoreboardHud.RankOf(states, 3), Is.EqualTo(2));
        }
    }
}
