using System.Collections.Generic;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Settings;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Net
{
    /// <summary>
    /// 途中から人が部屋に入った知らせ（OnlineSession.JoinNotices。2026-11-03 のユーザーの要望）を、画面の左下に小さな黄色い文字で出す
    /// （日本語は「名前が入室しました」、英語は「名前 JOIN」）。古い知らせは最後の 1 秒で薄くなって消える。OnGUI の中で呼ぶ。
    /// </summary>
    public static class JoinNoticeHud
    {
        private static readonly Color Yellow = new Color(1f, 0.86f, 0.25f);
        private static GUIStyle _style;

        /// <summary>知らせを、新しいものほど下に並べて描く。</summary>
        public static void Draw(IReadOnlyList<OnlineSession.JoinNotice> notices)
        {
            if (notices == null || notices.Count == 0)
            {
                return;
            }
            if (_style == null)
            {
                _style = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerLeft });
                _style.normal.textColor = Yellow;
            }
            float y = Screen.height - 28f;
            for (int i = notices.Count - 1; i >= 0; i--)
            {
                OnlineSession.JoinNotice notice = notices[i];
                float left = OnlineSession.JoinNotice.Seconds - notice.Age;
                Color color = Yellow;
                color.a = Mathf.Clamp01(left);
                string text = Loc.T($"{notice.Name} JOIN", $"{notice.Name}が入室しました");
                GuidePlate.DrawText(new Rect(18f, y - 22f, 600f, 24f), text, _style, color);
                y -= 24f;
            }
        }
    }
}
