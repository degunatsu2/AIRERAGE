namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// 一時停止のメニュー（PauseMenu。docs/15 T6-1）が止める、オフラインの試合の実行（トレーニングと検証用のシーンの SimulationRunner、
    /// ローカル対戦の LocalMatchRunner）。
    /// </summary>
    public interface IPauseTarget
    {
        /// <summary>シミュレーションの速さの倍率（1 = 等速、0 = 止める）。</summary>
        float SpeedScale { get; set; }

        /// <summary>RESET POSITION（出現位置に戻す）を出すか（トレーニングと検証用のシーンだけ。対戦では出さない）。</summary>
        bool CanResetPosition { get; }

        /// <summary>出現位置に戻す（CanResetPosition のときだけ呼ぶ）。</summary>
        void ResetPlayer();
    }
}
