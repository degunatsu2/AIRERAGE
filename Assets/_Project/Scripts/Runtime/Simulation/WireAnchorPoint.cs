using UnityEngine;

namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// ワイヤーのエイムアシストの対象になるアンカーポイント（この GameObject の位置）。
    /// シミュレーションの開始時に集めるので、プレイ中に動かしても反映されない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WireAnchorPoint : MonoBehaviour
    {
    }
}
