namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// 結果の画面のボタン（docs/12_match_plan.md T4-9 の先取り。ユーザーの要望、2026-10-13。docs/15 T6-8）。試合を進める側
    /// （OnlineRunner、LocalMatchRunner）が持つ。
    /// </summary>
    public interface IMatchControls
    {
        /// <summary>「Restart」を押せるか（オンラインでは、試合の進行を受け持つホストだけ）。</summary>
        bool CanRestart { get; }

        /// <summary>同じ人で試合を始め直す。</summary>
        void Restart();

        /// <summary>「キャラクター選択へ」を押せるか（App のシーンから始めたときだけ。オンラインではホストだけ）。</summary>
        bool CanReturnToCharacterSelect { get; }

        /// <summary>同じ人でキャラクター選択へ戻る（オンラインでは全員がロビーのキャラクター選択へ）。</summary>
        void ReturnToCharacterSelect();

        /// <summary>メニューへ戻る（App のシーンから始めていなければ、ゲームを終える。エディタでは再生を止める）。</summary>
        void Quit();

        /// <summary>
        /// 待機所（部屋）がある試合か（ネットワーク対戦。結果の画面の 3 つ目の項目を MAIN MENU ではなく BACK ROOM にする。2026-11-03 の
        /// ユーザーの要望）。
        /// </summary>
        bool HasRoom { get; }

        /// <summary>「BACK ROOM」（全員を待機所へ戻す）を押せるか（ネットワーク対戦のホストだけ）。</summary>
        bool CanBackToRoom { get; }

        /// <summary>全員を待機所へ戻す。</summary>
        void BackToRoom();

        /// <summary>途中から部屋に入って待っている人がいるか（いれば REMATCH・CHARACTER SELECT を押せない。BACK ROOM で戻す）。</summary>
        bool NewcomersWaiting { get; }

        /// <summary>
        /// 結果の画面でほかの人が抜けて、試合に出られる人が 1 人だけになったか（REMATCH・CHARACTER SELECT を押せない。BACK ROOM で待機所へ
        /// 戻って人を待つ。2026-11-03）。
        /// </summary>
        bool TooFewPlayers { get; }
    }
}
