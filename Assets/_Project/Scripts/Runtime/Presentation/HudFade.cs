using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// HUD の部品を透かして描く範囲（HudOcclusion。キャラクターと重なった HUD を透かす）。IMGUI の GUI.color と同じく、OnGUI で描く間だけの値:
    /// Begin(alpha) から End までの間は、GUI.color を (1, 1, 1, alpha) から始め、描く側は新しい色を入れるときに Apply を通す（不透明さに alpha を
    /// 掛ける）。前の GUI.color に戻すだけのところは、そのままでよい（範囲の初めの色が透けた白なので）。
    /// 試合中の表示の全体の出し具合 Reveal も掛ける（試合の始まりの演出の間は 0 で隠し、演出の後にタイマーと一緒にフェードインで出す。
    /// 2026-11-18 のユーザーの要望。値を決めるのは MatchHud）。範囲の外では、Apply は Reveal だけを掛ける。
    /// </summary>
    public static class HudFade
    {
        // 今描いている部品の不透明さ（範囲の中だけ）。IMGUI は 1 つのスレッドで順に描くので、範囲は入れ子にしない。
        private static float s_alpha = 1f;
        private static bool s_inRange;
        private static Color s_savedColor = Color.white;

        /// <summary>
        /// 試合中の表示の全体の出し具合（0〜1。ふだんは 1）。試合の始まりの演出の間は 0 にして隠し、演出の後にフェードインする
        /// （MatchHud が毎フレーム決め、外れるときに 1 に戻す）。
        /// </summary>
        public static float Reveal { get; private set; } = 1f;

        /// <summary>試合中の表示を出しているか（出し具合が 0 より大きいか）。0 の間は、描く側は描かなくてよい。</summary>
        public static bool Revealed => Reveal > 0f;

        /// <summary>今の不透明さ（範囲の外は Reveal）。</summary>
        public static float Alpha => s_inRange ? s_alpha : Reveal;

        /// <summary>試合中の表示の全体の出し具合を決める（MatchHud。試合の始まりの演出）。</summary>
        public static void SetReveal(float reveal)
        {
            Reveal = Mathf.Clamp01(reveal);
        }

        /// <summary>不透明さ alpha で描く範囲を始める（End で終える）。全体の出し具合 Reveal も掛ける。</summary>
        public static void Begin(float alpha)
        {
            s_savedColor = GUI.color;
            s_alpha = Mathf.Clamp01(alpha) * Reveal;
            s_inRange = true;
            GUI.color = new Color(1f, 1f, 1f, s_alpha);
        }

        /// <summary>範囲を終え、GUI.color を始める前に戻す。</summary>
        public static void End()
        {
            s_alpha = 1f;
            s_inRange = false;
            GUI.color = s_savedColor;
        }

        /// <summary>色 color の不透明さに、今の範囲の不透明さ（範囲の外は Reveal）を掛けた色。</summary>
        public static Color Apply(Color color)
        {
            color.a *= Alpha;
            return color;
        }
    }
}
