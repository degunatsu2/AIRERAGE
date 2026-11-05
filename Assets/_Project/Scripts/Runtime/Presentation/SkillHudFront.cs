using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// SkillHud の、拾ったアイテムの札をほかの表示より前に描く部品（ユーザーの要望、2026-11-05）。IMGUI は部品ごとに GUI.depth の小さい順に
    /// 上へ重なるので、この部品だけ小さくする。SkillHud が実行中に足す（シーンには置かない）。
    /// </summary>
    public sealed class SkillHudFront : MonoBehaviour
    {
        /// <summary>前に描く深さ（ほかの HUD は 0）。</summary>
        public const int Depth = -100;

        internal SkillHud Hud;

        private void OnGUI()
        {
            if (Hud == null || !Hud.isActiveAndEnabled)
            {
                return;
            }
            GUI.depth = Depth;
            Hud.DrawFront();
        }
    }
}
