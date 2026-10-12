using SimInput = Degunatsu.Core.Input.PlayerInput;

namespace Degunatsu.Runtime.Simulation
{
    /// <summary>プレイヤーの入力の代わりに、ティックごとに入力を渡すもの（入力の再生など、デバッグ用）。</summary>
    public interface ITickInputSource
    {
        /// <summary>次のティックの入力。もう入力がなければ false。</summary>
        bool TryGetNext(out SimInput input);
    }
}
