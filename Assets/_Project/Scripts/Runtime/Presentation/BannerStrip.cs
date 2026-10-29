using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 画面を横切る斜めの帯（ユーザーの選択、2026-10-14。お知らせの案 B）: 黒い帯が開き、大きな文字が右から走り込んで止まり、
    /// 左へ走り去って帯が閉じる。上下の縁は色（虹色にもできる）。お知らせ（NoticeHud）と、アディショナルタイム・試合の終わり
    /// （MatchHud）で同じ見た目を使う。座標は OnGUI の座標。
    /// </summary>
    public sealed class BannerStrip
    {
        private GUIStyle _title;
        private GUIStyle _sub;

        /// <summary>
        /// 帯を描く。progress は 0〜1（開く → 文字が走り込む → 止まる → 走り去って閉じる）。centerY は帯の中心の高さ（ピクセル）。
        /// rainbow なら縁と文字が虹色に移り変わる（color は使わない）。
        /// </summary>
        public void Draw(string title, string sub, Color color, bool rainbow, float progress, float centerY)
        {
            EnsureStyles();
            float open = progress < 0.1f ? Ease(progress / 0.1f) : progress > 0.85f ? 1f - Ease((progress - 0.85f) / 0.15f) : 1f;
            if (open <= 0f || progress >= 1f)
            {
                return;
            }
            float slide = progress < 0.2f ? 1f - Ease(progress / 0.2f) : progress > 0.75f ? -Ease((progress - 0.75f) / 0.17f) : 0f;
            float height = 118f * open;
            Matrix4x4 matrix = GUI.matrix;
            Color previous = GUI.color;
            GUIUtility.RotateAroundPivot(-3f, new Vector2(Screen.width * 0.5f, centerY));
            var bar = new Rect(-Screen.width * 0.1f, centerY - height * 0.5f, Screen.width * 1.2f, height);
            GUI.color = new Color(0.02f, 0.03f, 0.07f, 0.82f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time / 1.2f, 1f), 0.7f, 1f) : color;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width, 5f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(bar.x, bar.yMax - 5f, bar.width, 5f), Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.matrix = matrix;

            float alpha = 1f - Mathf.Abs(slide);
            float offset = slide * Screen.width * 0.6f;
            Color titleColor = rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time / 1.2f + 0.3f, 1f), 0.55f, 1f) : color;
            titleColor.a = alpha;
            GuidePlate.DrawText(new Rect(offset, centerY - 50f, Screen.width, 76f), title, _title, titleColor);
            if (!string.IsNullOrEmpty(sub))
            {
                GuidePlate.DrawText(new Rect(offset, centerY + 22f, Screen.width, 28f), sub, _sub, new Color(1f, 1f, 1f, alpha));
            }
            GUI.color = previous;
        }

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        private void EnsureStyles()
        {
            if (_title != null)
            {
                return;
            }
            _title = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter, wordWrap = false });
            _sub = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false });
            _title.normal.textColor = Color.white;
            _sub.normal.textColor = Color.white;
        }
    }
}
