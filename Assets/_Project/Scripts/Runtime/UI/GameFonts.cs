using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// ゲームの字体（D-77、2026-10-29。英字・数字は Orbitron、日本語は M PLUS 1。どちらも SIL Open Font License 1.1 で、
    /// 字体のファイルと OFL.txt を Resources/Fonts に同梱する）。Orbitron に足りない字（かな・漢字・記号）は、字体の取り込みの
    /// 設定で M PLUS 1 から借りる（.meta の fontNames に「M PLUS 1」、fallbackFontReferences にその太さのファイル。両方が要る）。
    /// 太字は Unity に太らせず、太い字体のファイルで書く（For と Use。太らせると Orbitron が潰れる）。
    /// 日本語の文（説明・ハッシュタグなど）は、英字も日本語の字体でそろえる（japanese）。
    /// </summary>
    public static class GameFonts
    {
        private const string Folder = "Fonts/";

        private static Font _latin;
        private static Font _latinBold;
        private static Font _latinBlack;
        private static Font _japanese;
        private static Font _japaneseBold;

        /// <summary>英字のふつうの太さ（Orbitron Medium。足りない字は M PLUS 1 Regular）。</summary>
        public static Font Latin => Load(ref _latin, "Orbitron-Medium");

        /// <summary>英字の太字（Orbitron Bold。足りない字は M PLUS 1 ExtraBold）。</summary>
        public static Font LatinBold => Load(ref _latinBold, "Orbitron-Bold");

        /// <summary>英字のいちばん太い字（Orbitron Black。タイトルなどの大きな見出し）。</summary>
        public static Font LatinBlack => Load(ref _latinBlack, "Orbitron-Black");

        /// <summary>日本語のふつうの太さ（M PLUS 1 Regular）。</summary>
        public static Font Japanese => Load(ref _japanese, "MPLUS1-Regular");

        /// <summary>日本語の太字（M PLUS 1 ExtraBold）。</summary>
        public static Font JapaneseBold => Load(ref _japaneseBold, "MPLUS1-ExtraBold");

        /// <summary>書体 style の文字に使う字体（太字は太い字体のファイル）。japanese なら日本語の字体。</summary>
        public static Font For(FontStyle style, bool japanese = false)
        {
            bool bold = style == FontStyle.Bold || style == FontStyle.BoldAndItalic;
            if (japanese)
            {
                return bold ? JapaneseBold : Japanese;
            }
            return bold ? LatinBold : Latin;
        }

        /// <summary>For の字体で書くときに Unity に付けさせる書体（太さは字体のファイルで出すので、斜体だけ残す）。</summary>
        public static FontStyle Remaining(FontStyle style)
        {
            switch (style)
            {
                case FontStyle.Bold:
                    return FontStyle.Normal;
                case FontStyle.BoldAndItalic:
                    return FontStyle.Italic;
                default:
                    return style;
            }
        }

        /// <summary>IMGUI の書体をゲームの字体にする（書体の太字を字体のファイルに置き換える）。同じ書体を返す。</summary>
        public static GUIStyle Use(GUIStyle style, bool japanese = false)
        {
            style.font = For(style.fontStyle, japanese);
            style.fontStyle = Remaining(style.fontStyle);
            return style;
        }

        /// <summary>uGUI の文字をゲームの字体にする（シーンのビルダー用。書体の太字を字体のファイルに置き換える）。</summary>
        public static void Use(Text text, bool japanese = false)
        {
            text.font = For(text.fontStyle, japanese);
            text.fontStyle = Remaining(text.fontStyle);
        }

        /// <summary>今の字体 current と同じ太さで、言語を切り替えた字体（japanese なら日本語の字体、でなければ英字の字体）。</summary>
        public static Font Switch(Font current, bool japanese)
        {
            bool bold = current != null && (current == LatinBold || current == LatinBlack || current == JapaneseBold);
            return For(bold ? FontStyle.Bold : FontStyle.Normal, japanese);
        }

        private static Font Load(ref Font cache, string name)
        {
            if (cache == null)
            {
                cache = Resources.Load<Font>(Folder + name);
                if (cache == null)
                {
                    Debug.LogError($"字体 Resources/{Folder}{name} が読み込めない（Git LFS のファイルを取り込めていない可能性。git lfs pull）");
                }
            }
            return cache;
        }
    }
}
