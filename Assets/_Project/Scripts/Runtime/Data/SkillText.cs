using System;
using UnityEngine;

namespace Airerage.Runtime.Data
{
    /// <summary>
    /// 技（特殊攻撃と必殺技）の表示用の文（キャラクター選択。docs/15 6.5）。シミュレーションには使わない。クールタイムとスタック数は
    /// シミュレーションの調整値（CombatTuning）から出すので、ここには書かない。
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

        public SkillText()
        {
        }

        public SkillText(string name, string description, string cooldownNote = "")
        {
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            CooldownNote = cooldownNote ?? string.Empty;
        }
    }
}
