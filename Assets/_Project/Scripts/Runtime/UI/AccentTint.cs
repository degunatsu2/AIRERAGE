using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 画面のイメージカラーで塗る印（以前は水色だった文字と、それに付く線・印。2026-11-08 のユーザーの要望）。画面を出すときに
    /// AccentTint.Apply でその画面の色にする（濃さは元のまま）。キャラクター選択・ステージ選択・アイテムを選ぶ画面は、入ってきた項目
    /// （SINGLE・LOCAL・NETWORK・TRAINING・OPTIONS）の色になる。
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public sealed class AccentTint : MonoBehaviour
    {
        /// <summary>白い背景の上の文字を暗くする割合（明るいイメージカラーは白の上で読みにくい。2026-11-08）。</summary>
        public const float OnWhiteDarken = 0.4f;

        [Tooltip("色を黒に寄せる割合（白い背景の上の文字。0 ならイメージカラーそのまま）")]
        [SerializeField] private float _darken;

        /// <summary>この印の色をイメージカラー accent にしたときの色（テスト用。白い背景の上の文字は黒に寄せた色）。</summary>
        public Color Expected(Color accent) => _darken > 0f ? Color.Lerp(accent, Color.black, _darken) : accent;

        /// <summary>白い背景の上の色（イメージカラーを黒に寄せる）。</summary>
        public static Color OnWhite(Color accent)
        {
            Color c = Color.Lerp(accent, Color.black, OnWhiteDarken);
            c.a = accent.a;
            return c;
        }

        /// <summary>白い背景の上の文字に印を付ける（イメージカラーを黒に寄せて塗る。シーンのビルダー用）。同じ graphic を返す。</summary>
        public static T MarkOnWhite<T>(T graphic) where T : Graphic
        {
            Mark(graphic);
            if (graphic != null)
            {
                graphic.GetComponent<AccentTint>()._darken = OnWhiteDarken;
                Color c = OnWhite(graphic.color);
                graphic.color = c;
                // 白の上では暗い縁取りがにじんで見えるので外す。
                Outline outline = graphic.GetComponent<Outline>();
                if (outline != null)
                {
                    outline.enabled = false;
                }
            }
            return graphic;
        }
        /// <summary>root の中の印をすべて accent にする。</summary>
        public static void Apply(Component root, Color accent)
        {
            if (root == null)
            {
                return;
            }
            foreach (AccentTint tint in root.GetComponentsInChildren<AccentTint>(true))
            {
                tint.Set(accent);
            }
        }

        /// <summary>graphic に印を付ける（シーンのビルダー用）。同じ graphic を返す。</summary>
        public static T Mark<T>(T graphic) where T : Graphic
        {
            if (graphic != null && graphic.GetComponent<AccentTint>() == null)
            {
                graphic.gameObject.AddComponent<AccentTint>();
            }
            return graphic;
        }

        /// <summary>この印の色を accent にする（濃さは元のまま）。</summary>
        public void Set(Color accent)
        {
            // 白い背景（MainMenuPanel）は、帯の色を変える（地の色はそのまま。2026-11-08）。
            MainMenuPanel panel = GetComponent<MainMenuPanel>();
            if (panel != null)
            {
                panel.SetAccent(accent);
                return;
            }
            // 縁取りした見出し（D-92 の T2）は、文字は白のまま縁と影の色を変える（前の印もこの色に合わせる。2026-11-09）。
            TitleStroke stroke = GetComponent<TitleStroke>();
            if (stroke != null)
            {
                stroke.SetAccent(accent);
                return;
            }
            Graphic graphic = GetComponent<Graphic>();
            if (graphic == null)
            {
                return;
            }
            Color color = _darken > 0f ? Color.Lerp(accent, Color.black, _darken) : accent;
            color.a = graphic.color.a;
            graphic.color = color;
        }
    }
}
