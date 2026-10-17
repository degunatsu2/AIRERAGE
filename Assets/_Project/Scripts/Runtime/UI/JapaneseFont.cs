using System;
using UnityEngine;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 日本語の文字に使う字体（キャラクター選択のハッシュタグと技の説明。ユーザーの要望、2026-10-17）。Unity に入っている字体
    /// （LegacyRuntime）には日本語の字がないので、PC に入っている日本語の字体を使う（字体のファイルを足さない。D-69）。
    /// 候補の最初に見つかったものを使い、残りを足りない字の代わりにする。どれもなければ null（Unity に入っている字体のまま）。
    /// 作った字体は、使う側が要らなくなったときに Destroy する。
    /// </summary>
    public static class JapaneseFont
    {
        /// <summary>候補（Windows、macOS、Linux の順）。</summary>
        public static readonly string[] Candidates =
        {
            "Yu Gothic UI", "Meiryo UI", "Meiryo", "Yu Gothic", "MS Gothic",
            "Hiragino Sans", "Hiragino Kaku Gothic ProN",
            "Noto Sans CJK JP", "Noto Sans JP", "WenQuanYi Zen Hei",
        };

        /// <summary>PC に入っている候補で字体を作る（大きさは size。どれもなければ null）。</summary>
        public static Font Create(int size)
        {
            string[] installed = Font.GetOSInstalledFontNames();
            string[] names = Array.FindAll(Candidates, name => Array.IndexOf(installed, name) >= 0);
            if (names.Length == 0)
            {
                Debug.LogWarning($"日本語の字体が見つからない（候補: {string.Join(", ", Candidates)}）。日本語の字が出ないことがある");
                return null;
            }
            return Font.CreateDynamicFontFromOSFont(names, size);
        }
    }
}
