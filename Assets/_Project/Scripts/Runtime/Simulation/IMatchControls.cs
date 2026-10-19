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
    }
}
