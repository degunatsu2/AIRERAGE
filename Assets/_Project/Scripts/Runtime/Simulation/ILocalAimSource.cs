using Airerage.Core.Physics;
using Airerage.Core.Simulation;

namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// この PC で操作している人の今の照準と、表示の予測に使う当たり（JUGGERNAUT のフラックの弾道の予測線・メーデーの場所のカーソル。
    /// JuggernautEffectsView）。SimulationRunner・LocalMatchRunner・OnlineRunner が持つ。表示だけに使い、シミュレーションには関わらない。
    /// </summary>
    public interface ILocalAimSource
    {
        /// <summary>
        /// キャラクター index をこの PC で操作していれば、その今の照準（入力の照準。なければ移動のスティック。シミュレーションが技に使うのと
        /// 同じ決め方）を aim に入れて true。
        /// </summary>
        bool TryPeekAim(int index, out System.Numerics.Vector2 aim);

        /// <summary>キャラクター index をこの PC で操作していて、メーデーで場所を選んでいれば、そのカーソルの位置を入れて true。</summary>
        bool TryGetTargetCursor(int index, out System.Numerics.Vector2 cursor);

        /// <summary>地形（動く足場・置かれた物のアンカーを含む）。</summary>
        ICollisionQuery Collision { get; }

        /// <summary>ステージの範囲（撃破の線。なければ null）。</summary>
        StageBounds? Bounds { get; }
    }
}
