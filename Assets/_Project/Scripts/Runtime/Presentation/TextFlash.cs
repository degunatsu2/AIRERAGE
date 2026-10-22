using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 文字エフェクト（MISS・BLOCK!・FATAL!・GUARD・CHARGE!・COMBO・技の通知など）の共通の色と書体（ユーザーの要望、2026-10-21）。
    /// - 出た瞬間は白く光り（WhiteSeconds）、FadeSeconds かけて本来の色に変わる（Apply）。
    /// - 書体は、どの状態（ふつう・マウスが乗った・押した）でも文字の色を白にし、色は GUI.color だけで決める（Style）。
    ///   スキンの状態ごとの色に左右されて、本来の色でなく白っぽく出ることがないように。
    /// </summary>
    public static class TextFlash
    {
        /// <summary>出てから白いままの時間（秒）。</summary>
        public const float WhiteSeconds = 0.06f;

        /// <summary>白から本来の色に変わるまでの時間（秒）。</summary>
        public const float FadeSeconds = 0.14f;

        /// <summary>出てから age 秒の色（はじめは白、すぐに color へ。透明度は color のまま）。</summary>
        public static Color Apply(Color color, float age)
        {
            float t = age <= WhiteSeconds ? 0f : Mathf.Clamp01((age - WhiteSeconds) / FadeSeconds);
            Color mixed = Color.Lerp(Color.white, color, t);
            mixed.a = color.a;
            return mixed;
        }

        /// <summary>文字エフェクトの書体（どの状態でも文字は白、背景なし。色は GUI.color で決める）。</summary>
        public static GUIStyle Style(int fontSize, FontStyle fontStyle, TextAnchor alignment)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = fontStyle, alignment = alignment, wordWrap = false };
            foreach (GUIStyleState state in new[]
            {
                style.normal, style.hover, style.active, style.focused, style.onNormal, style.onHover, style.onActive, style.onFocused,
            })
            {
                state.textColor = Color.white;
                state.background = null;
            }
            return style;
        }

        /// <summary>影つきの文字（影は右下に offset ずらした黒）。色は GUI.color で付け、終わったら元に戻す。</summary>
        public static void Draw(Rect rect, string text, GUIStyle style, Color color, float shadowAlpha = 0.85f, float offset = 2f)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, shadowAlpha * color.a);
            GUI.Label(new Rect(rect.x + offset, rect.y + offset, rect.width, rect.height), text, style);
            GUI.color = color;
            GUI.Label(rect, text, style);
            GUI.color = previous;
        }
    }
}
