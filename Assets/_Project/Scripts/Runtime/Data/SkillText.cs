using System;
using Airerage.Runtime.Settings;
using UnityEngine;

namespace Airerage.Runtime.Data
{
    /// <summary>
    /// 技（特殊攻撃と必殺技）の表示用の文（キャラクター選択。docs/15 6.5）。シミュレーションには使わない。クールタイムとスタック数は
    /// シミュレーションの調整値（CombatTuning）から出すので、ここには書かない。日本語と英語（オプションの LANGUAGE で切り替える。
    /// 2026-10-27）。
    /// </summary>
    [Serializable]
    public sealed class SkillText
    {
        [Tooltip("技の名前（カタカナ）")]
        public string Name = string.Empty;

        [Tooltip("何ができるか（短く）")]
        [TextArea(2, 4)]
        public string Description = string.Empty;

        [Tooltip("クールタイムを数え始めるとき（使ったときからなら空。例: 効果が終わってから）")]
        public string CooldownNote = string.Empty;

        [Tooltip("英語の名前・説明・クールタイムを数え始めるとき（空なら日本語のものを出す）")]
        public string NameEn = string.Empty;
        [TextArea(2, 4)]
        public string DescriptionEn = string.Empty;
        public string CooldownNoteEn = string.Empty;

        public SkillText()
        {
        }

        public SkillText(string name, string description, string cooldownNote = "")
        {
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            CooldownNote = cooldownNote ?? string.Empty;
        }

        /// <summary>英語の文を足す（この文を返す）。</summary>
        public SkillText English(string name, string description, string cooldownNote = "")
        {
            NameEn = name ?? string.Empty;
            DescriptionEn = description ?? string.Empty;
            CooldownNoteEn = cooldownNote ?? string.Empty;
            return this;
        }

        public string NameIn(GameLanguage language) => Pick(language, Name, NameEn);
        public string DescriptionIn(GameLanguage language) => Pick(language, Description, DescriptionEn);
        public string CooldownNoteIn(GameLanguage language) => Pick(language, CooldownNote, CooldownNoteEn);

        // 英語なら英語（なければ日本語）。
        private static string Pick(GameLanguage language, string japanese, string english)
        {
            return language == GameLanguage.English && !string.IsNullOrEmpty(english) ? english : japanese;
        }
    }
}
