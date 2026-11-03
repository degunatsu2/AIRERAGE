using Airerage.Runtime.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 英語と日本語の 2 つの文を持つ説明の文字（操作の説明など。シーンのビルダーが置く）。オプションの LANGUAGE に合わせて出し、
    /// 日本語のときは日本語の字体（M PLUS 1）にする（2026-11-03 のユーザーの要望: 日本語の設定では、すべての画面の説明書きを日本語に。
    /// ボタンや見出しの英語の表記はそのまま）。オプションで言語を変えたら、次のフレームで入れ替わる。
    /// </summary>
    [RequireComponent(typeof(Text))]
    public sealed class LocalizedText : MonoBehaviour
    {
        [SerializeField] private string _english = string.Empty;
        [SerializeField] private string _japanese = string.Empty;

        private Text _text;
        private int _shown = -1;

        /// <summary>英語の文。</summary>
        public string English => _english;

        /// <summary>日本語の文。</summary>
        public string Japanese => _japanese;

        /// <summary>text に英語 en と日本語 ja を持たせる（LocalizedText がなければ付ける）。</summary>
        public static LocalizedText Attach(Text text, string en, string ja)
        {
            LocalizedText localized = text.GetComponent<LocalizedText>();
            if (localized == null)
            {
                localized = text.gameObject.AddComponent<LocalizedText>();
            }
            localized.Set(en, ja);
            return localized;
        }

        /// <summary>
        /// 説明の文を、今の言語で text に出す（実行中に文を作るところ用。日本語なら日本語の字体、英語なら英字の字体に切り替える）。
        /// </summary>
        public static void Show(Text text, string en, string ja)
        {
            if (text == null)
            {
                return;
            }
            bool japanese = Loc.Japanese;
            text.font = GameFonts.Switch(text.font, japanese);
            text.text = japanese ? ja ?? string.Empty : en ?? string.Empty;
        }

        /// <summary>文を変えて、すぐに出し直す。</summary>
        public void Set(string en, string ja)
        {
            _english = en ?? string.Empty;
            _japanese = ja ?? string.Empty;
            _shown = -1;
            Apply();
        }

        private void OnEnable()
        {
            _shown = -1;
            Apply();
        }

        private void Update()
        {
            Apply();
        }

        private void Apply()
        {
            if (_text == null)
            {
                _text = GetComponent<Text>();
            }
            int language = Loc.Japanese ? 1 : 0;
            if (language == _shown || _text == null)
            {
                return;
            }
            _shown = language;
            Show(_text, _english, _japanese);
        }
    }
}
