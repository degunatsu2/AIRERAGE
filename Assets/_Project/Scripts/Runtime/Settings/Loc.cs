namespace Airerage.Runtime.Settings
{
    /// <summary>
    /// 説明の文（操作の説明・案内・状態の文など。ボタンや見出しの英語の表記はそのまま）を、オプションの LANGUAGE に合わせて日本語か英語で
    /// 出す（2026-11-03 のユーザーの要望: 日本語の設定では、すべての画面の説明書きを日本語に）。言語は PlayerPrefs から読むので、
    /// オプションで変えるとすぐに反映される。
    /// </summary>
    public static class Loc
    {
        /// <summary>日本語で出すか（GameSettings の LANGUAGE。初めは日本語）。</summary>
        public static bool Japanese => GameSettings.LoadLanguage() == GameLanguage.Japanese;

        /// <summary>英語 en か日本語 ja のどちらか。</summary>
        public static string T(string en, string ja)
        {
            return Japanese ? ja : en;
        }
    }
}
