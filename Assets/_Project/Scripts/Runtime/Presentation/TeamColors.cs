using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// チーム対戦（docs/19）のチームの色と名前（チームの番号 1〜4。Core の CombatState.Team・TeamRules と同じ番号）。待機所・試合の画面で
    /// 同じ色を使う。プレイヤーの色（1P〜8P）とは別。
    /// </summary>
    public static class TeamColors
    {
        private static readonly Color[] Colors =
        {
            new Color(0.75f, 0.75f, 0.78f),
            new Color(0.95f, 0.27f, 0.25f),
            new Color(0.27f, 0.52f, 1f),
            new Color(0.3f, 0.85f, 0.38f),
            new Color(0.98f, 0.8f, 0.18f),
        };

        private static readonly string[] Names = { "-", "RED", "BLUE", "GREEN", "YELLOW" };

        /// <summary>チーム team の色（範囲の外とチームなしは灰色）。</summary>
        public static Color Of(int team)
        {
            return team >= 0 && team < Colors.Length ? Colors[team] : Colors[0];
        }

        /// <summary>
        /// 試合の画面で人に使う色: チーム対戦ならチームの色、個人戦ならプレイヤーの色 playerColor（右上の札・頭上の札・画面の外の札など。T9-6）。
        /// </summary>
        public static Color ForCharacter(in Airerage.Core.Simulation.CharacterState state, Color playerColor)
        {
            return state.Combat.Team != 0 ? Of(state.Combat.Team) : playerColor;
        }

        /// <summary>チーム team の名前（RED など。範囲の外とチームなしは「-」）。</summary>
        public static string NameOf(int team)
        {
            return team >= 0 && team < Names.Length ? Names[team] : Names[0];
        }
    }
}
