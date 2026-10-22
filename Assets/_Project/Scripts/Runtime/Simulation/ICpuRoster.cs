namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// CPU が操作するキャラクターがいる試合（シングルモード。docs/16 T7-2）。表示が、CPU の人の技の札などを出さないために使う。
    /// </summary>
    public interface ICpuRoster
    {
        /// <summary>キャラクター index を CPU が操作しているか。</summary>
        bool IsCpu(int index);
    }
}
