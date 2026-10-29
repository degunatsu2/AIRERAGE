using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 日本語の行の折り返しの決まり（禁則処理。ユーザーの要望、2026-10-29）。Unity の文字（uGUI の Text）は、どの字の前でも折り返すので、
    /// 「、」「。」が行の頭に来ることがある。行の頭に置かない字（NotAtLineStart）が行の頭に来たら、その前の字ごと次の行へ送る
    /// （行の終わりに置かない字 NotAtLineEnd も同じ）。折り返しの位置は Unity の文字の並べ方（TextGenerator）で調べ、送る所に改行を入れる。
    /// 文字に出すときは LineBreakText（枠の大きさが変わったら入れ直す）。
    /// </summary>
    public static class LineBreakRules
    {
        /// <summary>行の頭に置かない字（句読点・閉じ括弧・小さいかな・長音など）。</summary>
        public const string NotAtLineStart = "、。，．・：；？！ー―～〜…‥）」』】〕〉》］｝’”ゝゞヽヾ々ぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ,.:;!?)]}%";

        /// <summary>行の終わりに置かない字（開き括弧）。</summary>
        public const string NotAtLineEnd = "（「『【〔〈《［｛‘“([{";

        // 改行を入れ直す回数の上限（1 回に 1 か所ずつ直す）。
        private const int MaxPasses = 16;

        private static readonly TextGenerator Generator = new TextGenerator();
        private static readonly List<int> Starts = new List<int>();

        /// <summary>
        /// 行の頭（lineStarts。text の中の位置）のうち、決まりに合わない最初の行について、改行を入れる位置を返す（直す所がなければ -1）。
        /// 行の頭の字が NotAtLineStart か、前の行の終わりの字が NotAtLineEnd なら、その前の字から次の行へ送る。前の行の字が
        /// なくなるほど戻るときは、送らない（直せない）。
        /// </summary>
        public static int FindBreak(string text, IList<int> lineStarts)
        {
            int previous = 0;
            for (int i = 0; i < lineStarts.Count; i++)
            {
                int start = lineStarts[i];
                if (start <= 0 || start >= text.Length)
                {
                    previous = Mathf.Max(previous, start);
                    continue;
                }
                if (text[start - 1] != '\n')
                {
                    int at = start;
                    while (at - 1 > previous && (IsNotAtStart(text[at]) || IsNotAtEnd(text[at - 1])))
                    {
                        at--;
                    }
                    if (at != start && !IsNotAtStart(text[at]) && !IsNotAtEnd(text[at - 1]))
                    {
                        return at;
                    }
                }
                previous = start;
            }
            return -1;
        }

        /// <summary>
        /// text の枠と字体で並べたときに、決まりに合うように改行を入れた value（折り返さない文字か、枠の大きさが決まっていないときは、
        /// そのまま）。文字に出すときは LineBreakText を使う（枠の大きさが変わったら入れ直す）。
        /// </summary>
        public static string Wrap(Text text, string value)
        {
            Vector2 size = text.rectTransform.rect.size;
            if (text.horizontalOverflow != HorizontalWrapMode.Wrap || size.x <= 0f || string.IsNullOrEmpty(value) || text.font == null)
            {
                return value;
            }
            return Wrap(value, text.GetGenerationSettings(new Vector2(size.x, 100000f)));
        }

        /// <summary>settings（字体・大きさ・枠の幅）で並べたときに、決まりに合うように改行を入れた value。</summary>
        public static string Wrap(string value, TextGenerationSettings settings)
        {
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            string current = value;
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                Generator.Populate(current, settings);
                Starts.Clear();
                foreach (UILineInfo line in Generator.lines)
                {
                    Starts.Add(line.startCharIdx);
                }
                int at = FindBreak(current, Starts);
                if (at < 0)
                {
                    break;
                }
                current = current.Insert(at, "\n");
            }
            return current;
        }

        private static bool IsNotAtStart(char c)
        {
            return NotAtLineStart.IndexOf(c) >= 0;
        }

        private static bool IsNotAtEnd(char c)
        {
            return NotAtLineEnd.IndexOf(c) >= 0;
        }
    }
}
