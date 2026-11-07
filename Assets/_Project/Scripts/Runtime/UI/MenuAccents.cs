using UnityEngine;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メインメニューの項目のイメージカラー（2026-11-07）。札の縁・裏の札・縁を回る光・左の絵・右の見出しの札に使い、その項目から入った
    /// 画面（キャラクター選択・ステージ選択・オプションなど）の文字の色にも使う（以前は水色だった文字。2026-11-08 のユーザーの要望）。
    /// 色はここに 1 か所だけ書く（シーンのビルダーの MenuUiBuilder.MainAccents もここから取る）。
    /// </summary>
    public static class MenuAccents
    {
        public static readonly Color Single = new Color(0.38f, 0.62f, 1f);
        public static readonly Color Local = new Color(1f, 0.58f, 0.3f);
        public static readonly Color Network = new Color(0.2f, 0.86f, 0.78f);
        public static readonly Color Training = new Color(0.62f, 0.86f, 0.3f);
        public static readonly Color Options = new Color(0.68f, 0.56f, 1f);
        public static readonly Color Quit = new Color(1f, 0.38f, 0.5f);

        /// <summary>画面の色を決める前の色（以前のメニューの水色）。</summary>
        public static readonly Color Default = new Color(0.35f, 0.85f, 1f);

        /// <summary>ステージ選択の RANDOM のイメージカラー（以前の帯の色。2026-11-08）。</summary>
        public static readonly Color Random = new Color(0.72f, 0.74f, 0.8f);

        /// <summary>SINGLE・LOCAL・NETWORK・TRAINING・OPTIONS・QUIT の順（メインメニューの項目の順）。</summary>
        public static Color[] MainItems() => new[] { Single, Local, Network, Training, Options, Quit };
    }
}
