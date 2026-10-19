namespace Airerage.Runtime.UI
{
    /// <summary>
    /// ネットワーク対戦のメニュー（docs/15 T6-7）。中身は通信を使うので Net のアセンブリにあり（OnlineMenu）、メインメニュー（MenuScreens）は
    /// この形でだけ呼ぶ（Runtime は Net を参照しない）。
    /// </summary>
    public interface IOnlineMenu
    {
        /// <summary>つながっているか（試合から戻ったときに、ネットワーク対戦のメニューを開き直すのに使う）。</summary>
        bool IsConnected { get; }

        /// <summary>開く（つながっていなければホスト・参加の画面、つながっていれば今の段階の画面）。</summary>
        void Open();
    }
}
