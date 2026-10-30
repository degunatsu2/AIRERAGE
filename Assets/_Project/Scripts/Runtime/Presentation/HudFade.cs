using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// HUD の部品を透かして描く範囲（HudOcclusion。キャラクターと重なった HUD を透かす）。IMGUI の GUI.color と同じく、OnGUI で描く間だけの値:
    /// Begin(alpha) から End までの間は、GUI.color を (1, 1, 1, alpha) から始め、描く側は新しい色を入れるときに Apply を通す（不透明さに alpha を
    /// 掛ける）。前の GUI.color に戻すだけのところは、そのままでよい（範囲の初めの色が透けた白なので）。範囲の外では Apply は何もしない。
    /// </summary>
    public static class HudFade
    {
        // 今描いている部品の不透明さ（範囲の外は 1）。IMGUI は 1 つのスレッドで順に描くので、範囲は入れ子にしない。
        private static float s_alpha = 1f;
        private static Color s_savedColor = Color.white;

        /// <summary>今の不透明さ（範囲の外は 1）。</summary>
        public static float Alpha => s_alpha;

        /// <summary>不透明さ alpha で描く範囲を始める（End で終える）。</summary>
        public static void Begin(float alpha)
        {
            s_savedColor = GUI.color;
            s_alpha = Mathf.Clamp01(alpha);
            GUI.color = new Color(1f, 1f, 1f, s_alpha);
        }

        /// <summary>範囲を終え、GUI.color を始める前に戻す。</summary>
        public static void End()
        {
            s_alpha = 1f;
            GUI.color = s_savedColor;
        }

        /// <summary>色 color の不透明さに、今の範囲の不透明さを掛けた色。</summary>
        public static Color Apply(Color color)
        {
            color.a *= s_alpha;
            return color;
        }
    }
}
